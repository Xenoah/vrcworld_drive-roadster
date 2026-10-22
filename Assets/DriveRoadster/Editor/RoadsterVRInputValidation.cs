#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRCStation = VRC.SDK3.Components.VRCStation;
using VRC.Udon;
using VRC.Udon.Common;

// Opt-in regression fixture. Creates a new test scene; never rebuilds the airport.
// SDK button events and synthetic tracking exercise the actual Udon VM and PhysX.
[InitializeOnLoad]
public static class RoadsterVRInputValidation
{
    const string Active="RoadsterVRInputValidation.Active";
    [Serializable] public class CheckResult { public string name; public bool passed; public string detail; }
    [Serializable] public class Report
    {
        public string utc,unity,sdk="3.10.5";
        public string environment="Isolated Unity ClientSim project; actual Udon VM and PhysX; synthetic VR identity, hand pose and SDK input events; no physical HMD or multiplayer";
        public List<CheckResult> checks=new List<CheckResult>();
        public List<string> errors=new List<string>();
    }
    static Report report;
    static UdonBehaviour vehicle,panel;
    static Rigidbody body;
    static int stage;
    static float next;
    static Vector3 initial;
    static Delegate originalTracking,originalVR;
    static bool fixtureVR;
    static Vector3 handOffset=Vector3.right*2f;
    static Quaternion handRotation=Quaternion.identity;
    static RoadsterVRInputValidation() { EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Tick; }

    [MenuItem("Drive Roadster/Validate VR trigger acceleration in isolated scene")]
    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode first");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        foreach(string name in new[]{"RoadsterVehicle","RoadsterRun","RoadsterAction"})
        {
            var source=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/DriveRoadster/Scripts/"+name+".cs");
            if(!UdonSharpProgramAsset.GetAllUdonSharpPrograms().Any(p=>p.sourceCsScript==source))
            {
                var program=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();program.sourceCsScript=source;program.ScriptVersion=UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.CreateAsset(program,AssetDatabase.GenerateUniqueAssetPath("Assets/DriveRoadster/Scripts/"+name+".asset"));
            }
        }
        AssetDatabase.SaveAssets();UdonSharpCompilerV1.CompileSync();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("InputRegressionWorld");
        var spawn=new GameObject("VehicleSpawn").transform;
        var exit=new GameObject("PlayerSpawn").transform;exit.position=new Vector3(5,1,0);
        root.AddComponent<VRCSceneDescriptor>().spawns=new[]{exit};
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="InputTestFloor";floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(500,1,500);
        var car=new GameObject("InputTestCar");car.layer=17;
        var rb=car.AddComponent<Rigidbody>();rb.mass=1200;rb.useGravity=false;rb.interpolation=RigidbodyInterpolation.Interpolate;
        var hull=car.AddComponent<BoxCollider>();hull.center=new Vector3(0,.62f,0);hull.size=new Vector3(1.8f,.65f,4.3f);
        var seat=new GameObject("Seat").transform;seat.SetParent(car.transform,false);seat.localPosition=new Vector3(0,.3f,0);
        var station=car.AddComponent<VRCStation>();station.PlayerMobility=VRCStation.Mobility.ImmobilizeForVehicle;station.disableStationExit=true;station.seated=true;station.stationEnterPlayerLocation=seat;station.stationExitPlayerLocation=exit;
        var v=car.AddUdonSharpComponent<RoadsterVehicle>();v.body=rb;v.station=station;v.spawn=spawn;v.safeExit=exit;v.wheels=new Transform[0];v.roadMask=1;
        v.flightBoundsCenter=new Vector3(0,40,0);v.flightBoundsSize=new Vector3(500,200,500);
        v.suspensionPoints=new Transform[4];
        for(int i=0;i<4;i++){var t=new GameObject("Suspension"+i).transform;t.SetParent(car.transform,false);t.localPosition=new Vector3(i%2==0 ? -.88f:.88f,.6f,i<2 ? 1.42f:-1.38f);v.suspensionPoints[i]=t;}
        var anchor=new GameObject("YokeAnchor").transform;anchor.SetParent(car.transform,false);anchor.localPosition=new Vector3(0,.7f,.3f);v.yokeAnchor=anchor;
        var button=new GameObject("TestHelpPanel");button.transform.SetParent(car.transform,false);var action=button.AddUdonSharpComponent<RoadsterAction>();action.vehicle=v;action.action=4;
        UdonSharpEditorUtility.CopyProxyToUdon(v);UdonSharpEditorUtility.CopyProxyToUdon(action);
        // A named scene prevents the SDK's empty-scene template initializer from
        // trying to open a replacement scene during the Play Mode domain reload.
        var scenePath=AssetDatabase.GenerateUniqueAssetPath("Assets/RoadsterVRInputFixture.unity");
        if(!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),scenePath))throw new IOException("Could not save input fixture scene");
        var settings=ClientSimSettings.Instance;settings.enableClientSim=true;settings.spawnPlayer=true;settings.hideMenuOnLaunch=true;settings.customLocalPlayerName="VR trigger regression";
        SessionState.SetBool(Active,true);EditorApplication.isPlaying=true;
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Active,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){report=new Report{utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion};stage=0;next=Time.time+7;Application.runInBackground=true;Application.logMessageReceived+=Log;}
        if(state==PlayModeStateChange.ExitingPlayMode)RestoreFixture();
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Active,false);Application.logMessageReceived-=Log;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-roadsterExit")>=0)EditorApplication.Exit(report!=null && report.checks.All(c=>c.passed)?0:1);
        }
    }
    static void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message);}
    static T V<T>(string name){return (T)vehicle.GetProgramVariable(name);}
    static void Set(string name,object value){vehicle.SetProgramVariable(name,value);}
    static void Event(string name){vehicle.SendCustomEvent(name);}
    static void Check(string name,bool passed,string detail){report.checks.Add(new CheckResult{name=name,passed=passed,detail=detail});Debug.Log("VR_INPUT_CHECK "+name+" "+passed+" "+detail);}
    static void Button(string name,bool value,HandType hand=HandType.RIGHT){UdonManager.Instance.RunInputAction(name,new UdonInputEventArgs(value,hand));}
    static void Use(bool value,HandType hand=HandType.RIGHT){Button(UdonManager.UDON_INPUT_USE,value,hand);}
    static void Grab(bool value){Button(UdonManager.UDON_INPUT_GRAB,value);}
    static void Move(float x,float y){UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_HORIZONTAL,new UdonInputEventArgs(x,HandType.LEFT));UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_VERTICAL,new UdonInputEventArgs(y,HandType.LEFT));}
    static void Neutral(){Use(false);Use(false,HandType.LEFT);Grab(false);Button(UdonManager.UDON_INPUT_JUMP,false);Move(0,0);Set("desktopBrake",false);Set("desktopThrust",false);Set("desktopRoll",0f);}
    static VRCPlayerApi.TrackingData Tracking(VRCPlayerApi p,VRCPlayerApi.TrackingDataType type)
    {
        if(fixtureVR&&p.isLocal&&type==VRCPlayerApi.TrackingDataType.RightHand)return new VRCPlayerApi.TrackingData(V<Transform>("yokeAnchor").position+body.transform.TransformDirection(handOffset),body.rotation*handRotation);
        return (VRCPlayerApi.TrackingData)originalTracking.DynamicInvoke(p,type);
    }
    static bool IsVR(VRCPlayerApi p){return fixtureVR&&p.isLocal||(bool)originalVR.DynamicInvoke(p);}
    static void InstallFixture()
    {
        var flags=BindingFlags.Static|BindingFlags.Public;var local=BindingFlags.Static|BindingFlags.NonPublic;
        var tracking=typeof(VRCPlayerApi).GetField("_GetTrackingData",flags);var vr=typeof(VRCPlayerApi).GetField("_IsUserInVR",flags);
        originalTracking=(Delegate)tracking.GetValue(null);originalVR=(Delegate)vr.GetValue(null);
        tracking.SetValue(null,Delegate.CreateDelegate(tracking.FieldType,typeof(RoadsterVRInputValidation).GetMethod("Tracking",local)));
        vr.SetValue(null,Delegate.CreateDelegate(vr.FieldType,typeof(RoadsterVRInputValidation).GetMethod("IsVR",local)));fixtureVR=true;
        UdonManager.Instance.RunEvent(UdonManager.UDON_EVENT_ONINPUTMETHODCHANGED,("inputMethod",VRCInputMethod.SteamVR2));
    }
    static void RestoreFixture()
    {
        var flags=BindingFlags.Static|BindingFlags.Public;
        if(originalTracking!=null)typeof(VRCPlayerApi).GetField("_GetTrackingData",flags).SetValue(null,originalTracking);
        if(originalVR!=null)typeof(VRCPlayerApi).GetField("_IsUserInVR",flags).SetValue(null,originalVR);
        originalTracking=null;originalVR=null;fixtureVR=false;
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Active,false)||!EditorApplication.isPlaying||report==null||Time.time<next)return;
        next=Time.time+.35f;
        try
        {
            switch(stage++)
            {
                case 0:
                    body=GameObject.Find("InputTestCar").GetComponent<Rigidbody>();vehicle=body.GetComponents<UdonBehaviour>().First(u=>u.programSource!=null&&u.programSource.name=="RoadsterVehicle");panel=body.GetComponentsInChildren<UdonBehaviour>().First(u=>u.programSource!=null&&u.programSource.name=="RoadsterAction");
                    InstallFixture();Grab(true);Use(true);vehicle.Interact();next=Time.time+1.6f;break;
                case 1:
                    Check("Held boarding trigger cannot accelerate",V<bool>("localSeated")&&!V<bool>("inputArmed")&&V<float>("throttle")==0,"seated with Use and Grab held");Use(false);break;
                case 2:
                    Check("Holding optional grip does not block drive arming",V<bool>("inputArmed"),"right grip held outside yoke radius; trigger released");initial=body.position;Use(true);next=Time.time+.6f;break;
                case 3:
                    Check("Right trigger accelerates without acquiring yoke",!V<bool>("yokeHeld")&&V<float>("throttle")>.8f,"throttle="+V<float>("throttle"));
                    Check("Ungrasped trigger moves actual Rigidbody",body.position.z-initial.z>.5f&&body.velocity.z>1,"distance="+(body.position.z-initial.z)+", velocity="+body.velocity.z);
                    Use(false);Grab(false);Event("ProcessPilotInput");Check("Right trigger release clears propulsion immediately",V<float>("throttle")==0,"throttle="+V<float>("throttle"));Use(true,HandType.LEFT);break;
                case 4:
                    Check("Left index independently brakes without yoke",V<float>("throttle")<-.8f,"throttle="+V<float>("throttle"));Use(true);Event("ProcessPilotInput");Check("Simultaneous index buttons prioritize brake",V<float>("throttle")<=0,"throttle="+V<float>("throttle"));Use(false,HandType.LEFT);break;
                case 5:
                    Check("Left release preserves held right acceleration",V<float>("throttle")>.5f,"throttle="+V<float>("throttle"));Use(false);Move(1,0);break;
                case 6:
                    Check("Left stick steers without acquiring yoke",V<float>("steering")>.7f&&!V<bool>("yokeHeld"),"steering="+V<float>("steering"));Move(0,1);break;
                case 7:
                    Check("Left stick Y still cannot accelerate",V<float>("throttle")==0,"MoveVertical=1");Neutral();handOffset=Vector3.zero;break;
                case 8:
                    Grab(true);Check("Nearby right grip acquires attitude yoke",V<bool>("yokeHeld"),"right-hand pose at anchor");Use(true);break;
                case 9:
                    Check("Right trigger also accelerates while gripping yoke",V<float>("throttle")>.8f,"throttle="+V<float>("throttle"));Grab(false);break;
                case 10:
                    Check("Yoke release cuts held trigger until release",!V<bool>("yokeHeld")&&V<float>("throttle")==0,"right Use still held for 0.35 s");Use(false);Use(true);break;
                case 11:
                    Check("Fresh trigger press restores drive without regripping",V<float>("throttle")>.8f,"throttle="+V<float>("throttle"));Neutral();break;
                case 12:Grab(true);Use(true);break;
                case 13:handOffset=Vector3.right*2f;break;
                case 14:
                    Check("Leaving yoke radius cuts held trigger",!V<bool>("yokeHeld")&&V<float>("throttle")==0,"hand moved 2 m; right Use held");Neutral();break;
                case 15:Use(true);Event("ProcessPilotInput");panel.Interact();break;
                case 16:
                    Check("Panel action clears and blocks held acceleration",!V<bool>("inputArmed")&&V<float>("throttle")==0,"Use then panel Interact");Use(false);break;
                case 17:
                    Check("Panel input re-arms after trigger release",V<bool>("inputArmed"),"released for 0.35 s");panel.Interact();Use(true);break;
                case 18:
                    Check("Panel-before-Use ordering remains disarmed",!V<bool>("inputArmed")&&V<float>("throttle")==0,"panel Interact then Use");Neutral();break;
                case 19:
                    Grab(true);Event("ProcessPilotInput");Check("Grip outside reach cannot become acceleration",!V<bool>("yokeHeld")&&V<float>("throttle")==0,"grip is not an index trigger");Grab(false);Button(UdonManager.UDON_INPUT_GRAB,true,HandType.LEFT);Event("ProcessPilotInput");Check("Left grip is not a brake",!V<bool>("leftUseHeld")&&V<float>("throttle")==0,"left Grab event");Button(UdonManager.UDON_INPUT_GRAB,false,HandType.LEFT);
                    Event("ToggleFlightMode");body.position=new Vector3(0,30,0);body.velocity=Vector3.zero;Event("EnableFlight");break;
                case 20:Use(true);next=Time.time+.5f;break;
                case 21:
                    Check("Manual air thrust works without yoke",V<bool>("manualFlight")&&!V<bool>("yokeHeld")&&V<float>("throttle")>.8f&&body.velocity.z>1,"throttle="+V<float>("throttle")+", forward velocity="+body.velocity.z);Neutral();handOffset=Vector3.zero;break;
                case 22:Grab(true);handRotation=Quaternion.Euler(20,0,25);break;
                case 23:
                    Check("Manual yoke pitch and roll still work",V<float>("pitchInput")>.2f&&V<float>("rollInput")>.3f,"pitch="+V<float>("pitchInput")+", roll="+V<float>("rollInput"));Button(UdonManager.UDON_INPUT_JUMP,true);break;
                case 24:
                    Check("Gripped VR Jump retains held lift",V<float>("liftInput")>.9f,"lift="+V<float>("liftInput"));Grab(false);Event("ProcessPilotInput");Check("Yoke release still cancels held lift",V<float>("liftInput")==0,"Jump remains held");Neutral();break;
                case 25:
                    Use(true);foreach(var p in Resources.FindObjectsOfTypeAll<ClientSimPlayer>())if(p.Player!=null&&p.Player.isLocal)p.isSuspended=true;Event("ProcessPilotInput");Check("Suspension immediately clears acceleration",!V<bool>("inputArmed")&&V<float>("throttle")==0,"ClientSim suspend state");foreach(var p in Resources.FindObjectsOfTypeAll<ClientSimPlayer>())if(p.Player!=null&&p.Player.isLocal)p.isSuspended=false;break;
                case 26:
                    Check("Resume cannot reuse held trigger",!V<bool>("inputArmed")&&V<float>("throttle")==0,"Use still held");Neutral();break;
                case 27:
                    Use(true);vehicle.RunEvent("_onOwnershipTransferred",("Player",Networking.LocalPlayer));Event("ProcessPilotInput");Check("Ownership callback clears held acceleration",!V<bool>("inputArmed")&&V<float>("throttle")==0,"single-client callback, not multiplayer transfer");Neutral();Event("Recover");Set("manualFlight",false);RestoreFixture();Set("vrInput",false);Set("inputMethodName","Gamepad");break;
                case 28:initial=body.position;Move(0,1);next=Time.time+.5f;break;
                case 29:
                    Check("Desktop ground throttle regression",V<float>("throttle")>.8f&&body.position.z-initial.z>.3f,"synthetic MoveVertical; throttle="+V<float>("throttle"));Neutral();Event("ExitVehicle");break;
                case 30:
                    Check("Explicit exit releases driver",!V<bool>("localSeated")&&V<int>("driverId")==-1,"ExitVehicle -> station callback");Check("No Udon runtime errors",report.errors.Count==0,string.Join("\n",report.errors));Finish();break;
            }
        }
        catch(Exception e){Check("Validation execution",false,e.ToString());Finish();}
    }
    static void Finish()
    {
        RestoreFixture();string path="docs/validation/vr-trigger-regression.json";var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadsterInputReport");if(at>=0&&at+1<args.Length)path=args[at+1];
        Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));Debug.Log("VR_INPUT_VALIDATION "+report.checks.Count(c=>c.passed)+"/"+report.checks.Count);EditorApplication.isPlaying=false;
    }
}
#endif
