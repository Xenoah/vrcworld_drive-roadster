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
public static class RoadsterAirportValidation
{
    const string Key="RoadsterAirportValidation.Active";
    [Serializable] public class Result {public string name;public bool passed;public string detail;}
    [Serializable] public class Report {public string environment="Unity Editor ClientSim; one local desktop simulated player; scripted inputs; no HMD or network clients";public List<Result> checks=new List<Result>();public List<string> errors=new List<string>();}
    static Report report;static int stage;static float next;static UdonBehaviour vehicle,run,music;static Rigidbody body;static AudioSource motor,gas;static Vector3 start;static float beforePitch;static AudioClip beforeTrack;static bool feed;static Camera capture;
    static RoadsterAirportValidation(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Tick;}
    public static void Begin()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop play first");
        UdonSharpCompilerV1.CompileSync();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var s=ClientSimSettings.Instance;s.enableClientSim=true;s.spawnPlayer=true;s.hideMenuOnLaunch=true;s.customLocalPlayerName="Airport QA";
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){report=new Report();stage=0;next=Time.time+7;feed=false;Application.runInBackground=true;Application.logMessageReceived+=Log;}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;}
    }
    static void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message);}
    static void Check(string name,bool ok,string detail){report.checks.Add(new Result{name=name,passed=ok,detail=detail});}
    static UdonBehaviour Program(GameObject g,string name){return g.GetComponents<UdonBehaviour>().First(u=>u.programSource!=null && u.programSource.name==name);}
    static T V<T>(UdonBehaviour b,string n){return (T)b.GetProgramVariable(n);}
    static void Move(float value){UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_VERTICAL,new UdonInputEventArgs(value,HandType.LEFT));}
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||report==null)return;
        if(feed)Move(1);
        if(Time.time<next)return;next=Time.time+1;
        try
        {
            switch(stage++)
            {
                case 0:
                    var cars=UnityEngine.Object.FindObjectsOfType<UdonBehaviour>().Where(u=>u.programSource!=null&&u.programSource.name=="RoadsterVehicle").ToArray();
                    Check("32 initialized vehicle programs",cars.Length==32,"count="+cars.Length);
                    Check("32 distinct respawn bays",cars.Select(u=>V<Transform>(u,"spawn")).Distinct().Count()==32,"shared references must not collapse");
                    Check("Each vehicle run belongs to its own car",cars.All(u=>V<UdonBehaviour>(u,"run").gameObject==u.gameObject),"clone-local Udon references");
                    Check("Local player initialized",ClientSimMain.HasInstance()&&Utilities.IsValid(Networking.LocalPlayer),"ClientSim");
                    var car=GameObject.Find("Roadster_01");vehicle=Program(car,"RoadsterVehicle");run=Program(car,"RoadsterRun");body=car.GetComponent<Rigidbody>();
                    motor=car.transform.Find("EV Motor").GetComponent<AudioSource>();gas=car.transform.Find("Cold gas thrust").GetComponent<AudioSource>();
                    Check("Parked fleet has no motor or gas voices",cars.All(u=>!u.transform.Find("EV Motor").GetComponent<AudioSource>().isPlaying && !u.transform.Find("Cold gas thrust").GetComponent<AudioSource>().isPlaying),"no 32-car idle chorus");
                    music=Program(GameObject.Find("AirportBGM"),"RoadsterMusic");var source=V<AudioSource>(music,"source");
                    Check("BGM starts with an assigned clip",source.isPlaying&&source.clip!=null,"track="+source.clip.name);
                    beforeTrack=source.clip;music.SendCustomEvent("NextTrack");
                    Check("BGM track advance",source.clip!=beforeTrack&&source.isPlaying,"track="+source.clip.name);
                    music.SendCustomEvent("ToggleMusic");Check("Local BGM mute pauses audio",!source.isPlaying,"independent local playback");
                    music.SendCustomEvent("ToggleMusic");Check("Local BGM resume",source.isPlaying,"playback restored");
                    for(int i=0;i<20;i++)music.SendCustomEvent("VolumeDown");Check("BGM minimum clamp",V<float>(music,"volume")==0,"volume="+V<float>(music,"volume"));
                    for(int i=0;i<20;i++)music.SendCustomEvent("VolumeUp");Check("BGM maximum clamp",V<float>(music,"volume")<=.6001f,"volume="+V<float>(music,"volume"));music.SetProgramVariable("volume",.18f);
                    vehicle.Interact();next=Time.time+2;break;
                case 1:
                    Check("Board driver station",V<bool>(vehicle,"localSeated"),"driverId="+V<int>(vehicle,"driverId"));
                    var head=Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    var localHead=body.transform.InverseTransformPoint(head);
                    Check("Seated eye is below roof",localHead.y<1.14f&&localHead.y>.75f,"actual ClientSim local head="+localHead);
                    Capture("airport-clientsim-cockpit",head,body.rotation);
                    var bay=V<Transform>(vehicle,"spawn");run.SendCustomEvent("BeginRun");
                    Check("Time attack cannot start in parking bays",!V<bool>(run,"running"),V<string>(run,"status"));
                    body.position=new Vector3(-280,.18f,-284);body.rotation=Quaternion.identity;body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;
                    run.SetProgramVariable("previousPosition",body.position);start=body.position;run.SendCustomEvent("BeginRun");
                    Check("Time attack staging does not teleport to bay",V<bool>(run,"running")&&Vector3.Distance(body.position,start)<.1f,"position="+body.position);
                    Move(0);next=Time.time+.5f;break;
                case 2:
                    start=body.position;feed=true;beforePitch=motor.pitch;next=Time.time+2.7f;break;
                case 3:
                    feed=false;Move(0);
                    Check("Vehicle accelerates along runway",body.position.z-start.z>18&&body.velocity.magnitude>12,"distance="+(body.position.z-start.z)+" speed="+body.velocity.magnitude);
                    Check("Motor responds to acceleration",motor.isPlaying&&motor.volume>.05f&&motor.pitch>beforePitch,"volume="+motor.volume+" pitch="+motor.pitch);
                    Check("Swept START gate advances timer",V<bool>(run,"running")&&V<int>(run,"nextGate")==1,"gate="+V<int>(run,"nextGate")+" "+V<string>(run,"status"));
                    Capture("airport-clientsim-runway",body.position+new Vector3(-5,3,-8),Quaternion.LookRotation(body.position-(body.position+new Vector3(-5,3,-8))));
                    vehicle.SendCustomEvent("Recover");
                    Check("Reset returns only to own bay",Vector3.Distance(body.position,V<Transform>(vehicle,"spawn").position)<.1f&&!V<bool>(run,"running"),"position="+body.position);
                    next=Time.time+1;break;
                case 4:
                    Check("Stopped motor becomes silent",!motor.isPlaying||motor.volume<.005f,"volume="+motor.volume);
                    body.position=new Vector3(360,8,105);body.rotation=Quaternion.Euler(0,180,0);body.velocity=new Vector3(0,-8,-12);body.angularVelocity=Vector3.zero;
                    vehicle.SendCustomEvent("EnableFlight");next=Time.time+.65f;break;
                case 5:
                    var particles=body.GetComponentsInChildren<ParticleSystem>();
                    Check("Flight and landing jets drive gas audio",gas.isPlaying&&gas.volume>.05f,"volume="+gas.volume);
                    Check("Increased smoke emits live particles",particles.Where(p=>p.name.Contains("ResidualSmoke")||p.name.Contains("GroundMist")).Sum(p=>p.particleCount)>20,"smoke="+particles.Where(p=>p.name.Contains("ResidualSmoke")||p.name.Contains("GroundMist")).Sum(p=>p.particleCount));
                    Capture("airport-clientsim-smoke",body.position+new Vector3(10,3,12),Quaternion.LookRotation(body.position-(body.position+new Vector3(10,3,12))));
                    vehicle.SendCustomEvent("ExitVehicle");next=Time.time+1;break;
                case 6:
                    Check("Exit returns player to entrance",!V<bool>(vehicle,"localSeated")&&Vector3.Distance(Networking.LocalPlayer.GetPosition(),V<Transform>(vehicle,"safeExit").position)<2,"player="+Networking.LocalPlayer.GetPosition());
                    Check("Unoccupied car resets after exit",Vector3.Distance(body.position,V<Transform>(vehicle,"spawn").position)<.2f,"position="+body.position);
                    var last=Program(GameObject.Find("Roadster_32"),"RoadsterVehicle");last.Interact();next=Time.time+2;break;
                case 7:
                    var lastCar=Program(GameObject.Find("Roadster_32"),"RoadsterVehicle");
                    Check("Car 32 has independent usable station",V<bool>(lastCar,"localSeated")&&V<int>(vehicle,"driverId")==-1,"car32 driver="+V<int>(lastCar,"driverId"));lastCar.SendCustomEvent("Recover");
                    Check("Car 32 reset uses bay 32",Vector3.Distance(lastCar.GetComponent<Rigidbody>().position,V<Transform>(lastCar,"spawn").position)<.1f,"body position="+lastCar.GetComponent<Rigidbody>().position);
                    lastCar.SendCustomEvent("ExitVehicle");next=Time.time+1;break;
                case 8:
                    Check("No runtime exceptions",report.errors.Count==0,string.Join("\n",report.errors));
                    Finish();break;
            }
        }
        catch(Exception e){Check("Validation execution",false,e.ToString());Finish();}
    }
    static void Finish()
    {
        feed=false;Directory.CreateDirectory("docs/validation");File.WriteAllText("docs/validation/airport-clientsim.json",JsonUtility.ToJson(report,true));
        Debug.Log("AIRPORT_VALIDATION "+report.checks.Count(c=>c.passed)+"/"+report.checks.Count);EditorApplication.isPlaying=false;
    }
    public static void Capture(string name,Vector3 position,Quaternion rotation)
    {
        bool temporary=capture==null;if(temporary){capture=new GameObject("AirportQA_RenderCamera").AddComponent<Camera>();capture.enabled=false;}
        capture.transform.SetPositionAndRotation(position,rotation);capture.nearClipPlane=.02f;capture.farClipPlane=3000;capture.fieldOfView=65;capture.cullingMask=~(1<<19);
        var rt=new RenderTexture(1600,1000,24);capture.targetTexture=rt;capture.Render();var prior=RenderTexture.active;RenderTexture.active=rt;
        var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();Directory.CreateDirectory("docs/images");File.WriteAllBytes("docs/images/"+name+".png",tex.EncodeToPNG());
        RenderTexture.active=prior;capture.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
        if(temporary){UnityEngine.Object.DestroyImmediate(capture.gameObject);capture=null;}
    }
}
#endif
