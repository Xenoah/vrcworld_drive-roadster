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
using UdonSharp.Compiler;

[InitializeOnLoad]
public static class RoadsterSeatFanValidation
{
    const string Key="RoadsterSeatFanValidation.Active";
    [Serializable] public class Result{public string name;public bool passed;public string detail;}
    [Serializable] public class Report{public string environment="Unity ClientSim desktop; real Udon/PhysX ticks on temporary 58/90/180 degree surfaces; scripted controls; no HMD or multiplayer clients";public List<Result> checks=new List<Result>();public List<string> errors=new List<string>();}
    static Report report;static UdonBehaviour vehicle;static Rigidbody body;static int stage;static float next,headBefore,seatBefore;
    static Vector3 surface,normal,start;static GameObject fixture;static AudioSource fan;
    static RoadsterSeatFanValidation(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Tick;}
    public static void Begin()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play first");
        UdonSharpCompilerV1.CompileSync();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var s=ClientSimSettings.Instance;s.enableClientSim=true;s.spawnPlayer=true;s.hideMenuOnLaunch=true;s.customLocalPlayerName="Seat and Fan QA";
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
    static void Check(string n,bool ok,string detail){report.checks.Add(new Result{name=n,passed=ok,detail=detail});}
    static float Head(){return body.transform.InverseTransformPoint(Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position).y;}
    static void Button(int action)
    {
        var b=body.GetComponentsInChildren<UdonBehaviour>(true).First(u=>u.programSource!=null&&u.programSource.name=="RoadsterAction"&&(int)u.GetProgramVariable("action")==action);
        b.Interact();
    }
    static void Surface(float angle)
    {
        if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);
        fixture=GameObject.CreatePrimitive(PrimitiveType.Cube);fixture.name="Temporary fan QA surface";
        var rot=Quaternion.AngleAxis(angle,Vector3.forward);normal=rot*Vector3.up;surface=new Vector3(-400,60,-100);
        fixture.transform.SetPositionAndRotation(surface-normal*.5f,rot);fixture.transform.localScale=new Vector3(70,1,160);
        body.position=surface+normal*.02f;body.rotation=rot;body.velocity=Vector3.forward*12;body.angularVelocity=Vector3.zero;
        Set("surfaceUp",normal);Set("wasGrounded",true);Set("fanReleaseUntil",0f);Set("stuntJumpActive",false);Set("jumpHeld",false);Set("previousJumpHeld",false);
        Set("moveVertical",0f);Set("moveHorizontal",0f);Set("manualFlight",false);Set("flightAllowed",false);Set("contactTime",-100f);
        Physics.SyncTransforms();start=body.position;
    }
    static void CheckSurface(string label)
    {
        float gap=Vector3.Dot(body.position-surface,normal),alignment=Vector3.Dot(body.transform.up,normal),travel=Vector3.Dot(body.position-start,Vector3.forward);
        Check(label+" maintains wheel contact",V<bool>("wasGrounded")&&Mathf.Abs(gap)<.2f&&alignment>.98f,"gap="+gap+", alignment="+alignment+", travel="+travel);
        Check(label+" applies 2 G into surface",Mathf.Abs(V<float>("fanAcceleration")-19.62f)<.01f&&Vector3.Dot(V<Vector3>("fanForceDirection"),-normal)>.99f,"fan="+V<float>("fanAcceleration")+", direction="+V<Vector3>("fanForceDirection"));
        Check(label+" coasts without key input",travel>20&&Mathf.Abs(V<float>("throttle"))<.001f,"travel="+travel+", speed="+body.velocity.magnitude);
        Check(label+" fan sound is active",fan.isPlaying&&fan.volume>.2f,"playing="+fan.isPlaying+", volume="+fan.volume);
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
                    var car=GameObject.Find("Roadster_01");body=car.GetComponent<Rigidbody>();vehicle=car.GetComponents<UdonBehaviour>().First(u=>u.programSource!=null&&u.programSource.name=="RoadsterVehicle");
                    fan=car.transform.Find("Surface suction fan").GetComponent<AudioSource>();
                    var cars=UnityEngine.Object.FindObjectsOfType<RoadsterVehicle>(true);
                    Check("All 32 cars share raised reference and height controls",cars.Length==32&&cars.All(v=>Mathf.Abs(v.cockpitEye.localPosition.y-1.08f)<.001f&&v.transform.Find("DriverControls/Control_9")!=null),"32 shared prefab instances");
                    Check("Parked fan voices are silent",cars.All(v=>!v.transform.Find("Surface suction fan").GetComponent<AudioSource>().isPlaying),"32 sources stopped");
                    Event("ViewUp");Check("Unseated caller cannot move seat",Mathf.Abs(V<float>("viewHeightOffset"))<.001f,"driver guard");
                    vehicle.Interact();next=Time.time+2;break;
                case 1:
                    headBefore=Head();seatBefore=V<float>("seatHeightOffset");
                    Check("Boarding aligns default eye to 1.08 m",Mathf.Abs(headBefore-1.08f)<.025f,"local head="+headBefore);
                    Button(9);next=Time.time+.5f;break;
                case 2:
                    Check("Cockpit plus button raises actual head 2 cm",Mathf.Abs(Head()-headBefore-.02f)<.01f,"delta="+(Head()-headBefore));
                    Check("Height changes calibrated station offset",Mathf.Abs(V<float>("seatHeightOffset")-seatBefore-.02f)<.001f,"seat delta="+(V<float>("seatHeightOffset")-seatBefore));
                    for(int i=0;i<20;i++)Event("ViewUp");next=Time.time+.4f;break;
                case 3:
                    Check("Upper height limit stays under roof",Mathf.Abs(Head()-1.12f)<.025f&&Mathf.Abs(V<float>("viewHeightOffset")-.04f)<.001f,"head="+Head());
                    for(int i=0;i<20;i++)Button(8);next=Time.time+.4f;break;
                case 4:
                    Check("Lower height limit is 0.96 m",Mathf.Abs(Head()-.96f)<.025f&&Mathf.Abs(V<float>("viewHeightOffset")+.12f)<.001f,"head="+Head());
                    Button(10);next=Time.time+.4f;break;
                case 5:
                    Check("Default button restores calibrated view",Mathf.Abs(Head()-1.08f)<.025f&&Mathf.Abs(V<float>("viewHeightOffset"))<.001f,"head="+Head());
                    RoadsterAirportValidation.Capture("airport-raised-cockpit",Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position,body.rotation*Quaternion.Euler(6,14,0));
                    var head=Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    var console=body.transform.Find("DriverControls");
                    RoadsterAirportValidation.Capture("airport-view-controls",head,Quaternion.LookRotation(console.TransformPoint(new Vector3(0,-140,0))-head,body.transform.up));
                    Check("Flat ground leaves suction off",V<float>("fanAcceleration")==0,"fan="+V<float>("fanAcceleration"));
                    Surface(58);next=Time.time+2.5f;break;
                case 6:
                    CheckSurface("58 degree bank");
                    UnityEngine.Object.DestroyImmediate(fixture);fixture=null;
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/DriveRoadster/Art/Airport/Meshes/AirportCircuit.asset");var verts=mesh.vertices;int sample=76;
                    var centre=(verts[2*sample]+verts[2*sample+1])*.5f;
                    var tangent=((verts[2*sample+2]+verts[2*sample+3])-(verts[2*sample-2]+verts[2*sample-1])).normalized;
                    normal=Vector3.Cross(tangent,(verts[2*sample+1]-verts[2*sample]).normalized).normalized;
                    body.position=centre;body.rotation=Quaternion.LookRotation(tangent,normal);body.velocity=tangent*20;body.angularVelocity=Vector3.zero;
                    Set("surfaceUp",normal);Set("wasGrounded",true);start=body.position;Physics.SyncTransforms();stage=14;next=Time.time+2;break;
                case 14:
                    RaycastHit contact;bool onRoad=Physics.Raycast(body.position+body.transform.up*.6f,-body.transform.up,out contact,.9f,1,QueryTriggerInteraction.Ignore);
                    Check("Actual airport bank follows changing slope",V<bool>("wasGrounded")&&onRoad&&contact.collider.name=="AirportCircuit"&&Vector3.Distance(start,body.position)>25,"grounded="+V<bool>("wasGrounded")+", collider="+(onRoad?contact.collider.name:"none")+", travel="+Vector3.Distance(start,body.position));
                    Check("Actual bank fan follows road normal",V<float>("fanAcceleration")>19.6f&&onRoad&&Vector3.Dot(V<Vector3>("fanForceDirection"),-contact.normal)>.97f,"fan="+V<float>("fanAcceleration"));
                    RoadsterAirportValidation.Capture("airport-fan-bank",body.position+new Vector3(8,5,-11),Quaternion.LookRotation(body.position-(body.position+new Vector3(8,5,-11))));
                    Surface(90);stage=7;next=Time.time+2.5f;break;
                case 7:
                    CheckSurface("Vertical wall");
                    RoadsterAirportValidation.Capture("airport-fan-wall-validation",body.position+new Vector3(-5,3,-7),Quaternion.LookRotation(body.position-(body.position+new Vector3(-5,3,-7))));
                    Surface(180);next=Time.time+2.5f;break;
                case 8:
                    CheckSurface("Inverted surface");
                    // Pack actual full fan demand without introducing another network field.
                    vehicle.RunProgram("_onPreSerialization");Check("Fan strength packs into existing snapshot",((V<int>("netControl")>>16)&255)==255,"quantized fan="+((V<int>("netControl")>>16)&255));
                    UnityEngine.Object.DestroyImmediate(fixture);fixture=null;body.position=new Vector3(-400,65,-100);body.velocity=Vector3.zero;body.rotation=Quaternion.identity;Set("landingAssistEnabled",false);next=Time.time+.5f;break;
                case 9:
                    Check("Airborne car releases suction",V<float>("fanAcceleration")==0&&!V<bool>("wasGrounded"),"fan="+V<float>("fanAcceleration"));
                    Check("Fan sound fades out in air",fan.volume<.02f,"volume="+fan.volume);
                    Event("Recover");Set("landingAssistEnabled",true);Set("inputMethodName","Gamepad");next=Time.time+.6f;break;
                case 10:
                    Set("jumpHeld",true);next=Time.time+.08f;break;
                case 11:
                    Check("Flat jump releases suction",V<bool>("stuntJumpActive")&&V<float>("fanAcceleration")==0&&body.velocity.y>4,"fan="+V<float>("fanAcceleration")+", upward velocity="+body.velocity.y);
                    Set("jumpHeld",false);Event("Recover");Event("ExitVehicle");next=Time.time+.5f;break;
                case 12:
                    Check("Exit stops fan and resets station",!fan.isPlaying&&V<float>("seatHeightOffset")==0,"fan playing="+fan.isPlaying);
                    Check("No Udon or runtime errors",report.errors.Count==0,string.Join("\n",report.errors));Finish();break;
            }
        }
        catch(Exception e){Check("Validation execution",false,e.ToString());Finish();}
    }
    static void Finish()
    {
        Directory.CreateDirectory("docs/validation");File.WriteAllText("docs/validation/airport-seat-fan.json",JsonUtility.ToJson(report,true));
        Debug.Log("SEAT_FAN_VALIDATION "+report.checks.Count(c=>c.passed)+"/"+report.checks.Count);EditorApplication.isPlaying=false;
    }
}
#endif
