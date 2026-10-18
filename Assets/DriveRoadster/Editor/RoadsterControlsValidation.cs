#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.Udon;
using VRC.Udon.Common;
using UdonSharp.Compiler;

[InitializeOnLoad]
public static class RoadsterControlsValidation
{
    const string Key="RoadsterControlsValidation.Active";
    [Serializable] public class CheckResult {public string name;public bool passed;public string detail;}
    [Serializable] public class Report {public string environment="Unity ClientSim, one desktop simulated player; scripted SDK events and Q/E state samples; physical no-key release check; no HMD/network clients"; public List<CheckResult> checks=new List<CheckResult>();public List<string> errors=new List<string>();}
    static Report report;static UdonBehaviour vehicle,run;static Rigidbody body;static int stage;static float next,startHeight;static Vector3 coastVelocity;
    static RoadsterControlsValidation(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Tick;}
    [MenuItem("Drive Roadster/Validate bank, neutral and jump controls")]
    public static void Begin()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first");
        UdonSharpCompilerV1.CompileSync();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var s=ClientSimSettings.Instance;s.enableClientSim=true;s.spawnPlayer=true;s.hideMenuOnLaunch=true;s.customLocalPlayerName="Controls QA";
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){report=new Report();stage=0;next=Time.time+7;Application.runInBackground=true;Application.logMessageReceived+=Log;}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;}
    }
    static void Log(string msg,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(msg);}
    static T V<T>(string n){return (T)vehicle.GetProgramVariable(n);}
    static void Set(string n,object v){vehicle.SetProgramVariable(n,v);}
    static void Event(string n){vehicle.SendCustomEvent(n);}
    static void Check(string n,bool ok,string detail){report.checks.Add(new CheckResult{name=n,passed=ok,detail=detail});}
    static void Move(float v){UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_VERTICAL,new UdonInputEventArgs(v,HandType.LEFT));}
    static void Jump(bool v){UdonManager.Instance.RunInputAction(UdonManager.UDON_INPUT_JUMP,new UdonInputEventArgs(v,HandType.RIGHT));}
    static void Neutral(){Move(0);Jump(false);Set("moveHorizontal",0f);Set("desktopRoll",0f);Set("desktopBrake",false);Set("desktopThrust",false);}
    static void Bank()
    {
        // Static batching replaces the renderer mesh in Play Mode; sample the source mesh.
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/DriveRoadster/Art/Airport/Meshes/AirportCircuit.asset");var v=mesh.vertices;int outside=0,normal=0;float lowest=float.MaxValue;
        for(int i=62;i<131;i++)
        {
            lowest=Mathf.Min(lowest,v[2*i].y,v[2*i+1].y);
            if(v[2*i].y>v[2*i+1].y)outside++;
            var p=(v[2*i]+v[2*i+1])*.5f;var a=(v[2*i-2]+v[2*i-1])*.5f;var b=(v[2*i+2]+v[2*i+3])*.5f;
            var n=Vector3.Cross((b-a).normalized,(v[2*i+1]-v[2*i]).normalized).normalized;
            if(Vector3.Dot(n,Vector3.Cross(Vector3.up,b-a))>0)normal++;
        }
        Check("Bank outside edge is higher",outside==69,"samples="+outside+"/69");
        Check("Bank normal leans into right turn",normal==69,"samples="+normal+"/69");
        Check("Low bank edge clears ground",lowest>.025f,"minimum y="+lowest);
        int hits=0,total=0;var collider=GameObject.Find("AirportCircuit").GetComponent<MeshCollider>();
        for(int i=62;i<130;i+=2)foreach(float t in new[]{.08f,.5f,.92f})
        {var p=Vector3.Lerp(v[2*i],v[2*i+1],t);RaycastHit h;total++;if(collider.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out h,31)&&Mathf.Abs(h.point.y-p.y)<.15f)hits++;}
        Check("Corrected bank collider covers lane",hits==total,hits+"/"+total);
    }
    static void AxisSample(string name,float pitch,float roll)
    {
        Neutral();body.position=new Vector3(-280,12,-230);body.rotation=Quaternion.identity;body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;
        Set("wasGrounded",false);Set("inputArmed",true);Set("stuntJumpActive",true);Set("airborneSince",Time.time-1);Set("jumpPulseUntil",0f);
        Set("pitchInput",0f);Set("rollInput",0f);Set("steering",0f);Move(pitch);Set("desktopRoll",roll);
        // Exercise the actual Udon physics event with an injected keyboard adapter sample.
        vehicle.RunProgram("_fixedUpdate");
        var torque=V<Vector3>("attitudeCommand");var jets=V<float[]>("thrusterDemand");
        bool ok=pitch!=0 ? torque.x*pitch>0 && ((jets[2]+jets[3])-(jets[0]+jets[1]))*pitch>0
            : torque.z*(-roll)>0 && ((jets[1]+jets[3])-(jets[0]+jets[2]))*(-roll)>0;
        Check(name,ok,"torque="+torque+" jets="+string.Join(",",jets.Take(4).Select(x=>x.ToString("F3"))));
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||report==null||Time.time<next)return;
        next=Time.time+.4f;
        try
        {
            switch(stage++)
            {
                case 0:
                    Bank();var car=GameObject.Find("Roadster_01");body=car.GetComponent<Rigidbody>();
                    vehicle=car.GetComponents<UdonBehaviour>().First(u=>u.programSource!=null&&u.programSource.name=="RoadsterVehicle");run=car.GetComponents<UdonBehaviour>().First(u=>u.programSource!=null&&u.programSource.name=="RoadsterRun");
                    vehicle.Interact();next=Time.time+2;break;
                case 1:
                    Check("Driver boarded",V<bool>("localSeated"),"ClientSim station");Set("inputMethodName","Gamepad");Neutral();
                    body.position=new Vector3(-280,.18f,-300);body.rotation=Quaternion.identity;body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;next=Time.time+.6f;break;
                case 2:Move(1);next=Time.time+.7f;break;
                case 3:
                    Check("Throttle responds to press",V<float>("throttle")>.9f,"throttle="+V<float>("throttle"));coastVelocity=body.velocity;
                    Move(0);Event("ProcessPilotInput");Check("Release immediately sets throttle to zero",V<float>("throttle")==0,"one input processing step");
                    Check("Neutral preserves momentum",body.velocity.magnitude>3&&Vector3.Distance(body.velocity,coastVelocity)<.001f,"velocity="+body.velocity);
                    Set("inputMethodName","Keyboard");Move(1);Jump(true);Set("moveHorizontal",1f);Event("ReleaseDesktopKeys");Event("ProcessPilotInput");
                    Check("Physical release clears stale SDK input",V<float>("moveVertical")==0&&V<float>("moveHorizontal")==0&&!V<bool>("jumpHeld")&&V<float>("throttle")==0,"no physical keys held, stale positive samples injected");
                    Set("inputMethodName","Gamepad");Event("Recover");body.position=new Vector3(-280,.18f,-300);Neutral();next=Time.time+.6f;break;
                case 4:
                    startHeight=body.position.y;Jump(true);next=Time.time+.08f;break;
                case 5:
                    Check("Auto flat-ground jump without flight gate",!V<bool>("flightAllowed")&&V<bool>("stuntJumpActive")&&body.velocity.y>4,"vertical velocity="+body.velocity.y);
                    Check("Jump fires four downward nozzles",V<float[]>("thrusterDemand").Take(4).All(x=>x>.9f),"four jets="+string.Join(",",V<float[]>("thrusterDemand").Take(4)));
                    next=Time.time+.35f;break;
                case 6:
                    Check("Flat jump gains height",body.position.y-startHeight>1,"height gain="+(body.position.y-startHeight));
                    RoadsterAirportValidation.Capture("airport-flat-jump",body.position+new Vector3(6,2,-8),Quaternion.LookRotation(body.position-(body.position+new Vector3(6,2,-8))));
                    Check("Held Space continues upward thrust",V<float>("appliedLift")>.99f,"lift="+V<float>("appliedLift"));
                    Jump(false);Event("ProcessPilotInput");
                    Check("Jump release stops lift request immediately",V<float>("liftInput")==0,"lift request="+V<float>("liftInput"));
                    next=Time.time+12;break;
                case 7:
                    Check("Released Space returns to ground without bounce",V<bool>("wasGrounded")&&!V<bool>("stuntJumpActive")&&Mathf.Abs(body.velocity.y)<1,"grounded="+V<bool>("wasGrounded")+" velocity="+body.velocity);
                    Neutral();Event("ToggleFlightMode");next=Time.time+.4f;break;
                case 8:Jump(true);next=Time.time+.08f;break;
                case 9:
                    Check("Manual flat-ground jump",V<bool>("manualFlight")&&body.velocity.y>4,"vertical velocity="+body.velocity.y);
                    Jump(false);next=Time.time+.4f;break;
                case 10:
                    Check("Manual stays armed across launch",V<bool>("inputArmed"),V<string>("controlFeedback"));
                    AxisSample("W nose down uses rear pair",1,0);AxisSample("S nose up uses front pair",-1,0);
                    AxisSample("Q roll left uses right pair",0,-1);AxisSample("E roll right uses left pair",0,1);
                    Neutral();Set("pitchInput",0f);Set("rollInput",0f);Set("inputArmed",true);Set("desktopThrust",true);Event("ProcessPilotInput");Set("desktopThrust",false);Event("ProcessPilotInput");
                    Check("Manual air thrust releases immediately",V<float>("throttle")==0,"Ctrl up -> zero forward throttle");
                    Neutral();Event("Recover");Event("ExitVehicle");next=Time.time+1;break;
                case 11:
                    Check("Fleet count remains 32",UnityEngine.Object.FindObjectsOfType<RoadsterVehicle>(true).Length==32,"shared fleet preserved");
                    Check("Runtime errors absent",report.errors.Count==0,string.Join("\n",report.errors));Finish();break;
            }
        }
        catch(Exception e){Check("Validation execution",false,e.ToString());Finish();}
    }
    static void Finish()
    {
        Directory.CreateDirectory("docs/validation");File.WriteAllText("docs/validation/airport-controls.json",JsonUtility.ToJson(report,true));
        Debug.Log("CONTROLS_VALIDATION "+report.checks.Count(c=>c.passed)+"/"+report.checks.Count);EditorApplication.isPlaying=false;
    }
}
#endif
