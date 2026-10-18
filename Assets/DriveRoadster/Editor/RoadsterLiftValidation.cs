#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.Udon;
using VRC.Udon.Common;
using UdonSharp.Compiler;

[InitializeOnLoad]
public static class RoadsterLiftValidation
{
    const string Key="RoadsterLiftValidation.Active";
    [Serializable] public class Result{public string name;public bool passed;public string detail;}
    [Serializable] public class Report{public string environment="Unity ClientSim desktop; real Udon/PhysX; synthetic SDK Jump, movement and VR hand tracking/identity; no physical HMD or multiplayer clients";public List<Result> checks=new List<Result>();public List<string> errors=new List<string>();}
    static Report report;static UdonBehaviour vehicle,run;static Rigidbody body;static int stage;static float next,height;
    static Delegate originalTracking,originalVR;static bool fixtureVR;
    static RoadsterLiftValidation(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Tick;}
    [MenuItem("Drive Roadster/Validate held lift, Auto level and VR guides")]
    public static void Begin()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play first");
        UdonSharpCompilerV1.CompileSync();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var s=ClientSimSettings.Instance;s.enableClientSim=true;s.spawnPlayer=true;s.hideMenuOnLaunch=true;s.customLocalPlayerName="Lift and VR QA";
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){report=new Report();stage=0;next=Time.time+7;Application.runInBackground=true;Application.logMessageReceived+=Log;}
        if(state==PlayModeStateChange.ExitingPlayMode)RestoreFixture();
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;}
    }
    static void Log(string msg,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(msg);}
    static T V<T>(string name){return (T)vehicle.GetProgramVariable(name);}
    static void Set(string name,object value){vehicle.SetProgramVariable(name,value);}
    static void Event(string name){vehicle.SendCustomEvent(name);}
    static void Check(string name,bool pass,string detail){report.checks.Add(new Result{name=name,passed=pass,detail=detail});}
    static void Button(string name,bool value,HandType hand=HandType.RIGHT){UdonManager.Instance.RunInputAction(name,new UdonInputEventArgs(value,hand));}
    static void Jump(bool value){Button(UdonManager.UDON_INPUT_JUMP,value);}
    static void Move(float value){UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_VERTICAL,new UdonInputEventArgs(value,HandType.LEFT));}
    static void Neutral(){Jump(false);Move(0);Set("moveHorizontal",0f);Set("desktopRoll",0f);Set("desktopThrust",false);Set("desktopBrake",false);Button(UdonManager.UDON_INPUT_GRAB,false);Button(UdonManager.UDON_INPUT_USE,false);Button(UdonManager.UDON_INPUT_USE,false,HandType.LEFT);}
    static void Panel(int code){body.GetComponentsInChildren<UdonBehaviour>(true).First(u=>u.programSource!=null&&u.programSource.name=="RoadsterAction"&&(int)u.GetProgramVariable("action")==code).Interact();}
    static float Tilt(){return Vector3.Angle(body.transform.up,Vector3.up);}
    static string Help(){return ((UnityEngine.UI.Text)run.GetProgramVariable("helpText")).text;}
    static void CaptureGuide(string name)
    {
        var head=Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;var panel=body.transform.Find("DriverControls");
        RoadsterAirportValidation.Capture(name,head,Quaternion.LookRotation(panel.TransformPoint(new Vector3(0,-140,0))-head,body.transform.up));
    }
    static VRCPlayerApi.TrackingData Tracking(VRCPlayerApi p,VRCPlayerApi.TrackingDataType type)
    {
        if(fixtureVR&&p.isLocal&&type==VRCPlayerApi.TrackingDataType.RightHand)return new VRCPlayerApi.TrackingData(V<Transform>("yokeAnchor").position,body.rotation);
        return (VRCPlayerApi.TrackingData)originalTracking.DynamicInvoke(p,type);
    }
    static bool IsVR(VRCPlayerApi p){return fixtureVR&&p.isLocal||(bool)originalVR.DynamicInvoke(p);}
    static void InstallFixture()
    {
        var flags=BindingFlags.Static|BindingFlags.Public;var local=BindingFlags.Static|BindingFlags.NonPublic;
        var tracking=typeof(VRCPlayerApi).GetField("_GetTrackingData",flags);var vr=typeof(VRCPlayerApi).GetField("_IsUserInVR",flags);
        originalTracking=(Delegate)tracking.GetValue(null);originalVR=(Delegate)vr.GetValue(null);
        tracking.SetValue(null,Delegate.CreateDelegate(tracking.FieldType,typeof(RoadsterLiftValidation).GetMethod("Tracking",local)));
        vr.SetValue(null,Delegate.CreateDelegate(vr.FieldType,typeof(RoadsterLiftValidation).GetMethod("IsVR",local)));fixtureVR=true;
        UdonManager.Instance.RunEvent(UdonManager.UDON_EVENT_ONINPUTMETHODCHANGED,("inputMethod",VRCInputMethod.SteamVR2));
    }
    static void RestoreFixture()
    {
        var flags=BindingFlags.Static|BindingFlags.Public;
        if(originalTracking!=null)typeof(VRCPlayerApi).GetField("_GetTrackingData",flags).SetValue(null,originalTracking);
        if(originalVR!=null)typeof(VRCPlayerApi).GetField("_IsUserInVR",flags).SetValue(null,originalVR);
        fixtureVR=false;originalTracking=null;originalVR=null;
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||report==null||Time.time<next)return;
        next=Time.time+.35f;
        try
        {
            switch(stage++)
            {
                case 0:
                    var car=GameObject.Find("Roadster_01");body=car.GetComponent<Rigidbody>();var programs=car.GetComponents<UdonBehaviour>();
                    vehicle=programs.First(u=>u.programSource!=null&&u.programSource.name=="RoadsterVehicle");run=programs.First(u=>u.programSource!=null&&u.programSource.name=="RoadsterRun");
                    Check("VR controller board is present",GameObject.Find("VRControls_JA")!=null,"dedicated Japanese VR guide at entrance");
                    Check("All 32 cars have PC/VR guide switch",UnityEngine.Object.FindObjectsOfType<RoadsterVehicle>(true).Count(v=>v.transform.Find("DriverControls/Control_11")!=null)==32,"shared prefab controls");
                    vehicle.Interact();next=Time.time+2;break;
                case 1:
                    Set("inputMethodName","Gamepad");Neutral();Event("Recover");Set("landingAssistEnabled",false);next=Time.time+.6f;break;
                case 2:height=body.position.y;Jump(true);next=Time.time+.6f;break;
                case 3:
                    Check("Auto held Space keeps lift beyond launch pulse",!V<bool>("flightAllowed")&&V<float>("appliedLift")>.99f&&body.position.y-height>3,"lift="+V<float>("appliedLift")+", height="+(body.position.y-height));
                    Check("Continuous lift runs all four down jets",V<float[]>("thrusterDemand").Take(4).All(x=>x>.95f),"jets="+string.Join(",",V<float[]>("thrusterDemand").Take(4)));
                    body.rotation=Quaternion.Euler(25,0,40);body.angularVelocity=Vector3.zero;body.velocity=Vector3.up*10;Physics.SyncTransforms();
                    // Rigidbody interpolation publishes the new Transform on a real
                    // physics tick; sample the correction after that tick has occurred.
                    stage=18;next=Time.time+.12f;break;
                case 18:
                    var jets=V<float[]>("thrusterDemand");
                    Check("Auto tilt produces differential nozzle demand",Tilt()>20&&V<Vector3>("attitudeCommand").sqrMagnitude>1&&jets.Take(4).Max()-jets.Take(4).Min()>.15f,"tilt="+Tilt()+", command="+V<Vector3>("attitudeCommand")+", jets="+string.Join(",",jets.Take(4)));
                    height=body.position.y;stage=4;next=Time.time+2.5f;break;
                case 4:
                    Check("Auto restores level while Jump stays held",Tilt()<3&&V<float>("appliedLift")>.99f,"tilt="+Tilt()+", lift="+V<float>("appliedLift"));
                    Check("Held lift continues outside flight gates",body.position.y-height>20&&!V<bool>("flightAllowed"),"height gained="+(body.position.y-height));
                    Jump(false);Event("ProcessPilotInput");Check("Jump release clears lift immediately",V<float>("liftInput")==0,"lift request="+V<float>("liftInput"));
                    body.rotation=Quaternion.Euler(-30,0,-35);body.velocity=Vector3.down*4;body.angularVelocity=Vector3.zero;next=Time.time+.25f;break;
                case 5:
                    Check("Collective lift remains off after release",V<float>("appliedLift")==0,"attitude jets may still operate");
                    Check("Auto attitude jets stay active after Jump release",V<float[]>("thrusterDemand").Take(4).Max()>.05f,"tilt="+Tilt());
                    next=Time.time+2.5f;break;
                case 6:
                    Check("Auto returns to level without held Jump",Tilt()<3,"tilt="+Tilt());
                    body.position=new Vector3(-500,100.5f,-260);body.velocity=Vector3.zero;Jump(true);Set("inputArmed",true);Set("liftInput",1f);vehicle.RunProgram("_fixedUpdate");
                    Check("Height ceiling cuts held lift",V<float>("appliedLift")==0,"height="+body.position.y);
                    Neutral();Event("Recover");Set("manualFlight",true);next=Time.time+.6f;break;
                case 7:Move(1);Jump(true);next=Time.time+.65f;break;
                case 8:
                    Check("Manual held Jump survives takeoff",V<float>("appliedLift")>.99f&&V<bool>("inputArmed")&&!V<bool>("wasGrounded"),"lift="+V<float>("appliedLift"));
                    Check("Held ground W does not become air pitch",V<bool>("manualAxesAwaitNeutral")&&Mathf.Abs(V<float>("pitchInput"))<.01f,"pitch="+V<float>("pitchInput"));
                    Move(0);next=Time.time+.3f;break;
                case 9:
                    Check("Axes rearm without releasing Space",!V<bool>("manualAxesAwaitNeutral")&&V<float>("appliedLift")>.99f,"Jump remains held");
                    Move(1);next=Time.time+.3f;break;
                case 10:
                    Check("Manual pitch works alongside held lift",V<float>("pitchInput")>.6f&&V<float>("appliedLift")>.99f,"pitch="+V<float>("pitchInput"));
                    Move(0);Jump(false);Set("desktopThrust",true);Event("ProcessPilotInput");
                    Check("Ctrl forward thrust is separate from lift",V<float>("throttle")>0&&V<float>("liftInput")==0,"throttle="+V<float>("throttle"));
                    Set("desktopThrust",false);Event("ProcessPilotInput");Check("Ctrl release clears forward thrust",V<float>("throttle")==0,"throttle="+V<float>("throttle"));
                    Neutral();Event("Recover");Set("manualFlight",false);InstallFixture();next=Time.time+.6f;break;
                case 11:
                    Button(UdonManager.UDON_INPUT_GRAB,true);Jump(true);next=Time.time+.7f;break;
                case 12:
                    Check("VR Jump plus grip provides continuous lift",V<bool>("vrInput")&&V<bool>("yokeHeld")&&V<float>("appliedLift")>.99f,"synthetic hand + SDK Jump; lift="+V<float>("appliedLift"));
                    Check("VR automatically selects controller help",Help().Contains("R GRIP")&&Help().Contains("right A"),Help());
                    Button(UdonManager.UDON_INPUT_USE,true,HandType.LEFT);Event("ProcessPilotInput");
                    Check("VR left brake overrides held Jump",V<float>("liftInput")==0&&V<float>("throttle")<=0,"lift="+V<float>("liftInput"));
                    next=Time.time+.3f;break;
                case 13:Button(UdonManager.UDON_INPUT_USE,false,HandType.LEFT);next=Time.time+.3f;break;
                case 14:
                    Check("VR lift resumes after brake release",V<float>("appliedLift")>.99f,"lift="+V<float>("appliedLift"));
                    Button(UdonManager.UDON_INPUT_GRAB,false);Event("ProcessPilotInput");
                    Check("VR grip release cancels lift",!V<bool>("yokeHeld")&&V<float>("liftInput")==0,"held Jump cannot bypass grip");
                    Neutral();RestoreFixture();Set("vrInput",false);Set("inputMethodName","Gamepad");Event("Recover");Set("landingAssistEnabled",true);Set("feedbackUntil",0f);Panel(11);next=Time.time+.4f;break;
                case 15:
                    Check("PC user can read VR guide without changing input mode",!V<bool>("vrInput")&&Help().Contains("R GRIP"),"guide selection only");
                    CaptureGuide("airport-cockpit-vr-guide");Panel(11);next=Time.time+.4f;break;
                case 16:
                    Check("Guide switches back to PC controls",Help().Contains("Space HOLD")&&Help().Contains("Ctrl forward"),Help());
                    CaptureGuide("airport-cockpit-pc-guide");
                    RoadsterAirportValidation.Capture("airport-vr-controller-sign",new Vector3(-580,3.2f,-322),Quaternion.identity);
                    RoadsterAirportValidation.Capture("airport-thruster-guide",new Vector3(-570,3.2f,-322),Quaternion.identity);
                    Event("ExitVehicle");next=Time.time+.5f;break;
                case 17:Check("No runtime or Udon errors",report.errors.Count==0,string.Join("\n",report.errors));Finish();break;
            }
        }
        catch(Exception e){Check("Validation execution",false,e.ToString());Finish();}
    }
    static void Finish()
    {
        RestoreFixture();Directory.CreateDirectory("docs/validation");File.WriteAllText("docs/validation/airport-held-lift.json",JsonUtility.ToJson(report,true));
        Debug.Log("HELD_LIFT_VALIDATION "+report.checks.Count(c=>c.passed)+"/"+report.checks.Count);EditorApplication.isPlaying=false;
    }
}
#endif
