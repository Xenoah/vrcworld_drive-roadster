#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

// Editor-only, opt-in integration smoke test. Inputs/section starting poses are scripted.
// Runs the real Udon VM in ClientSim; it is not a full lap or a network/VR test.
[InitializeOnLoad]
public static class RoadsterValidation
{
    const string Active = "RoadsterValidation.Active";
    const string Output = "docs/validation";
    [Serializable] public class CheckResult { public string name; public bool passed; public string detail; }
    [Serializable] public class Report
    {
        public string utc, unity, sdk = "3.10.5", environment = "Unity Editor Play Mode / ClientSim / one local simulated player / scripted SDK events, desktop key-state adapter samples, synthetic VR identity and right-hand tracking fixture / no physical keyboard or HMD input";
        public string gpu, cpu;
        public List<CheckResult> checks = new List<CheckResult>();
        public List<string> captures = new List<string>();
    }
    static Report report;
    static UdonBehaviour vehicle, run;
    static Rigidbody body;
    static Camera camera;
    static UnityEngine.UI.Text overlay;
    static int stage;
    static int cockpitCameraMode;
    static float next;
    static bool scheduled;
    static bool deferredMove;
    static bool capturePending, thrustAfterArm, gapSample, boundsSample;
    static float deferredVertical, deferredHorizontal;
    static Delegate originalTracking, originalVR;
    static bool fixtureVR;
    static Quaternion fixtureHandRotation=Quaternion.identity;
    static Vector3 fixtureHandOffset;
    static Quaternion manualInitialRotation;
    static Vector3 start;
    static int modeMask;
    static float initialTilt, baselineImpact, assistedImpact, reboundPeak, brakeHeight, mistHeight;
    static int particlePeak, contactStart, smokeAtStop;
    static bool landingSample;
    static GameObject flightLandingPad;
    static string testLabel = "BASELINE SECTIONS";
    static readonly List<string> errors = new List<string>();

    static RoadsterValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Active, false)) ConfigureClientSim();
    }

    public static void BuildScene()
    {
        BuildRoadsterWorld.Build();
        string generated=EditorSceneManager.GetActiveScene().path;
        const string canonical="Assets/DriveRoadster/Scenes/DriveRoadster.unity";
        if(generated!=canonical)
        {
            if(!generated.StartsWith("Assets/DriveRoadster/Scenes/DriveRoadster")) throw new InvalidOperationException("Unexpected generated scene path");
            if(!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),canonical)) throw new IOException("Canonical scene save failed");
            AssetDatabase.DeleteAsset(generated);
        }
        UdonSharpCompilerV1.CompileSync();
        AssetDatabase.SaveAssets();
        Debug.Log("ROADSTER_BUILD_COMPLETE");
    }
    [MenuItem("Drive Roadster/Run ClientSim smoke validation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var path = "Assets/DriveRoadster/Scenes/DriveRoadster.unity";
        if (!File.Exists(path)) BuildScene();
        else EditorSceneManager.OpenScene(path);
        UdonSharpCompilerV1.CompileSync();
        SessionState.SetBool(Active, true);
        ConfigureClientSim();
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.position = new Rect(80, 80, 1280, 760);
        gameView.Show(); gameView.Focus();
        EditorApplication.isPlaying = true;
    }

    static void ConfigureClientSim()
    {
        // In-memory only: do not change the user's global ClientSim preferences.
        var s = ClientSimSettings.Instance;
        s.enableClientSim = true; s.spawnPlayer = true; s.hideMenuOnLaunch = true;
        s.customLocalPlayerName = "Roadster QA"; s.setTargetFrameRate = true; s.targetFrameRate = 60;
    }

    static void OnPlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Directory.CreateDirectory(Output); Directory.CreateDirectory("docs/images");
            report = new Report { utc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName, cpu = SystemInfo.processorType };
            errors.Clear(); Application.logMessageReceived += Log;
            Application.runInBackground = true;
            stage = 0; cockpitCameraMode=0; particlePeak = 0; next = Time.time + 6f; scheduled = true;
            capturePending=false;thrustAfterArm=false;gapSample=false;boundsSample=false;deferredMove=false;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            RestoreInputFixture();
            Application.logMessageReceived -= Log;
            SessionState.SetBool(Active, false); scheduled = false;
            if (Environment.GetCommandLineArgs().Length > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-roadsterExit") >= 0)
                EditorApplication.Exit(SessionState.GetInt("RoadsterValidation.ExitCode", 1));
        }
    }

    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }
    static T V<T>(UdonBehaviour u, string name) { return (T)u.GetProgramVariable(name); }
    static void Event(UdonBehaviour u, string name) { u.SendCustomEvent(name); }
    static void Input(float throttle, float steering = 0f)
    {
        deferredMove=false;
        if(vehicle!=null && !V<bool>(vehicle,"inputArmed") && (Mathf.Abs(throttle)>.001f || Mathf.Abs(steering)>.001f))
        {
            SendMove(0,0); deferredVertical=throttle;deferredHorizontal=steering;deferredMove=true;return;
        }
        SendMove(throttle,steering);
    }
    static void SendMove(float throttle,float steering)
    {
        UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_VERTICAL,new UdonInputEventArgs(throttle,HandType.LEFT));
        UdonManager.Instance.RunInputAction(UdonManager.UDON_MOVE_HORIZONTAL,new UdonInputEventArgs(steering,HandType.LEFT));
    }
    static void Button(string name,bool value,HandType hand=HandType.RIGHT)
    { UdonManager.Instance.RunInputAction(name,new UdonInputEventArgs(value,hand)); }
    static void Neutral()
    {
        deferredMove=false;SendMove(0,0);
        Button(UdonManager.UDON_INPUT_USE,false);Button(UdonManager.UDON_INPUT_USE,false,HandType.LEFT);
        Button(UdonManager.UDON_INPUT_GRAB,false);Button(UdonManager.UDON_INPUT_JUMP,false);
        vehicle.SetProgramVariable("desktopRoll",0f);vehicle.SetProgramVariable("desktopBrake",false);
    }
    static void ArmSample()
    {
        Neutral();Event(vehicle,"SuspendForPanel");
        vehicle.SetProgramVariable("neutralSince",Time.time-.2f);Event(vehicle,"ProcessPilotInput");
    }
    static void SampleAxis(string label,float vertical,float horizontal,float roll,bool thrust,bool brake,Vector3 expectedSigns)
    {
        ArmSample();SendMove(vertical,horizontal);
        // Hardware polling is outside this fixture: these are desktop key-state adapter samples.
        vehicle.SetProgramVariable("desktopRoll",roll);vehicle.SetProgramVariable("desktopBrake",brake);
        Button(UdonManager.UDON_INPUT_JUMP,thrust);Event(vehicle,"ProcessPilotInput");
        Vector3 actual=new Vector3(V<float>(vehicle,"pitchInput"),V<float>(vehicle,"steering"),V<float>(vehicle,"rollInput"));
        bool ok=true;for(int k=0;k<3;k++) ok&=expectedSigns[k]==0 ? Mathf.Abs(actual[k])<.001f : actual[k]*expectedSigns[k]>.01f;
        Check(label,ok,"SDK movement/Jump + synthetic Q/E/Shift adapter sample; P/Y/R="+actual.ToString("F3"));
    }
    static VRCPlayerApi.TrackingData FixtureTracking(VRCPlayerApi player,VRCPlayerApi.TrackingDataType type)
    {
        if(fixtureVR && player.isLocal && type==VRCPlayerApi.TrackingDataType.RightHand)
            return new VRCPlayerApi.TrackingData(V<Transform>(vehicle,"yokeAnchor").position+body.transform.TransformDirection(fixtureHandOffset),body.rotation*fixtureHandRotation);
        return (VRCPlayerApi.TrackingData)originalTracking.DynamicInvoke(player,type);
    }
    static bool FixtureIsVR(VRCPlayerApi player)
    { return fixtureVR && player.isLocal || (bool)originalVR.DynamicInvoke(player); }
    static void InstallInputFixture()
    {
        var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public;
        var local=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        var tracking=typeof(VRCPlayerApi).GetField("_GetTrackingData",flags);
        var vr=typeof(VRCPlayerApi).GetField("_IsUserInVR",flags);
        originalTracking=(Delegate)tracking.GetValue(null);originalVR=(Delegate)vr.GetValue(null);
        tracking.SetValue(null,Delegate.CreateDelegate(tracking.FieldType,typeof(RoadsterValidation).GetMethod("FixtureTracking",local)));
        vr.SetValue(null,Delegate.CreateDelegate(vr.FieldType,typeof(RoadsterValidation).GetMethod("FixtureIsVR",local)));
        fixtureVR=true;fixtureHandRotation=Quaternion.identity;fixtureHandOffset=Vector3.zero;
        UdonManager.Instance.RunEvent(UdonManager.UDON_EVENT_ONINPUTMETHODCHANGED,("inputMethod",VRCInputMethod.SteamVR2));
    }
    static void RestoreInputFixture()
    {
        var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public;
        if(originalTracking!=null)typeof(VRCPlayerApi).GetField("_GetTrackingData",flags).SetValue(null,originalTracking);
        if(originalVR!=null)typeof(VRCPlayerApi).GetField("_IsUserInVR",flags).SetValue(null,originalVR);
        originalTracking=null;originalVR=null;fixtureVR=false;
    }
    static UdonBehaviour Action(int code)
    {
        foreach(var u in body.GetComponentsInChildren<UdonBehaviour>())
            if(u.programSource!=null && u.programSource.name=="RoadsterAction" && V<int>(u,"action")==code) return u;
        throw new InvalidOperationException("Missing action "+code);
    }
    static void Check(string name, bool passed, string detail)
    { report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail }); Debug.Log("ROADSTER_CHECK " + name + " " + passed + " " + detail); }
    static string Pose()
    { return "position=" + body.position.ToString("F3") + "; speed=" + body.velocity.magnitude.ToString("F3") + "; mode=" + V<int>(vehicle, "motionMode"); }
    static void Place(Vector3 p, Quaternion q, Vector3 velocity)
    {
        Input(0); Event(vehicle, "Recover");
        body.position = p; body.rotation = q; body.velocity = velocity; body.angularVelocity = Vector3.zero;
        body.transform.SetPositionAndRotation(p, q); Physics.SyncTransforms();
        run.SetProgramVariable("previousPosition", p);
    }
    static void Capture(string name)
    {
        var path = "docs/images/" + name + ".png";
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(path));
        report.captures.Add(path);
    }
    static bool CaptureBeforeChange(string name)
    {
        if(capturePending) { capturePending=false;return false; }
        Capture(name);capturePending=true;stage--;next=Time.time+.15f;return true;
    }
    static void Tick()
    {
        if (!scheduled || !EditorApplication.isPlaying || report == null) return;
        try
        {
            if(deferredMove && vehicle!=null && V<bool>(vehicle,"inputArmed"))
            { deferredMove=false;SendMove(deferredVertical,deferredHorizontal); }
            if(thrustAfterArm && vehicle!=null && V<bool>(vehicle,"inputArmed"))
            { thrustAfterArm=false;Button(UdonManager.UDON_INPUT_JUMP,true); }
            if (body != null)
            {
                modeMask |= 1 << V<int>(vehicle, "motionMode");
                if (overlay != null) overlay.text = "UNITY PLAY MODE / CLIENTSIM - SCRIPTED SECTION TEST\n" +
                    testLabel + "\n" + (body.velocity.magnitude * 3.6f).ToString("F0") + " km/h   MODE " + V<int>(vehicle,"motionMode") +
                    "   CP " + V<int>(run,"nextGate") + "/10   " + V<string>(run,"status");
                if (camera != null)
                {
                    var up = body.transform.up;
                    camera.transform.position = body.position - body.transform.forward * 8f + up * 3.5f + body.transform.right * 3f;
                    camera.transform.LookAt(body.position + body.transform.forward * 3f + up * .7f, Vector3.up);
                    if (stage >= 12)
                    {
                        camera.transform.position = body.position - body.transform.forward*6f + body.transform.right*6f + Vector3.up*1.6f;
                        camera.transform.LookAt(body.position + Vector3.up*.3f,Vector3.up);
                    }
                }
            }
            if (camera!=null && body!=null && cockpitCameraMode>0)
            {
                camera.nearClipPlane=.015f;
                camera.cullingMask=~((1<<19)|(1<<10)); // Exclude the local avatar, as a first-person view does.
                if(cockpitCameraMode==1)
                {
                    var eye=V<Transform>(vehicle,"cockpitEye");
                    camera.transform.SetParent(eye,false);camera.transform.localPosition=Vector3.zero;
                    camera.transform.localRotation=Quaternion.Euler(20,12,0);camera.fieldOfView=74;
                }
                else if(cockpitCameraMode==2)
                {
                    var panel=body.transform.Find("DriverControls");
                    camera.transform.SetParent(panel,false);camera.transform.localScale=Vector3.one;
                    // World-space distance accounts for the canvas scale.
                    camera.transform.localPosition=Vector3.back*(.67f/panel.lossyScale.z);
                    camera.transform.localRotation=Quaternion.identity;camera.fieldOfView=44;
                }
                else
                {
                    camera.transform.SetParent(body.transform,false);camera.transform.localPosition=new Vector3(-2.7f,1.4f,-.35f);
                    camera.transform.LookAt(body.transform.TransformPoint(new Vector3(-.7f,.9f,-.12f)),Vector3.up);camera.fieldOfView=60;
                }
            }
            if (vehicle != null && body != null)
            {
                int count = 0;
                var jets = V<ParticleSystem[]>(vehicle,"thrusterJets");
                var mist = V<ParticleSystem[]>(vehicle,"groundMist");
                foreach (var ps in jets) count += ps.particleCount;
                foreach (var ps in V<ParticleSystem[]>(vehicle,"thrusterSmoke")) count += ps.particleCount;
                foreach (var ps in mist) count += ps.particleCount;
                particlePeak = Mathf.Max(particlePeak,count);
                if (landingSample)
                {
                    float height = V<float>(vehicle,"groundClearance");
                    if (V<float>(vehicle,"landingAcceleration") > 1f) brakeHeight = Mathf.Max(brakeHeight,height);
                    int clouds = 0; foreach (var ps in mist) clouds += ps.particleCount;
                    if (clouds > 0 && V<int>(vehicle,"touchdownCount") == contactStart) mistHeight = Mathf.Max(mistHeight,height);
                    if (V<int>(vehicle,"touchdownCount") > contactStart) reboundPeak = Mathf.Max(reboundPeak,body.velocity.y);
                }
            }
            if (Time.time < next) return;
            next = Time.time + 2f;
            switch (stage++)
            {
                case 0:
                    var car = GameObject.Find("Roadster_Authority");
                    body = car.GetComponent<Rigidbody>();
                    foreach (var u in car.GetComponents<UdonBehaviour>())
                    {
                        if (u.programSource != null && u.programSource.name == "RoadsterVehicle") vehicle = u;
                        if (u.programSource != null && u.programSource.name == "RoadsterRun") run = u;
                    }
                    Check("ClientSim initialized", ClientSimMain.HasInstance() && Utilities.IsValid(Networking.LocalPlayer), "one local player");
                    Check("Udon programs initialized", vehicle != null && run != null, "Vehicle and Run backing behaviours");
                    Check("Scene colliders", UnityEngine.Object.FindObjectsOfType<MeshCollider>().Length >= 10, "mesh colliders=" + UnityEngine.Object.FindObjectsOfType<MeshCollider>().Length);
                    Check("Vehicle LODs", car.GetComponent<LODGroup>().GetLODs().Length == 3, "3 LOD levels");
                    var pivots = V<Transform[]>(vehicle, "wheels");
                    int connected = 0;
                    foreach (var pivot in pivots)
                        if (pivot != null && pivot.childCount > 0 && pivot.GetComponentsInChildren<Renderer>().Length > 0) connected++;
                    Check("Wheel visual pivots connected", pivots.Length == 12 && connected == 12, "connected=" + connected + "/12");
                    camera = new GameObject("Validation spectator camera (Play Mode only)").AddComponent<Camera>();
                    camera.depth = 100; camera.fieldOfView = 65; camera.farClipPlane = 2000;
                    camera.clearFlags = CameraClearFlags.Skybox;
                    camera.cullingMask = ~(1 << 19); // ClientSim InternalUI is not world geometry.
                    var menu = UnityEngine.Object.FindObjectOfType<ClientSimMenu>();
                    if (menu != null) { menu.WarningAccepted(); menu.CloseMenu(); }
                    var canvas = new GameObject("Validation HUD (Play Mode only)").AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    overlay = new GameObject("Caption", typeof(RectTransform)).AddComponent<UnityEngine.UI.Text>();
                    overlay.transform.SetParent(canvas.transform, false);
                    overlay.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); overlay.fontSize = 20;
                    overlay.color = Color.white;
                    var rect = overlay.rectTransform; rect.anchorMin = new Vector2(0,1); rect.anchorMax = new Vector2(1,1);
                    rect.pivot = new Vector2(.5f,1); rect.anchoredPosition = new Vector2(14,-14); rect.sizeDelta = new Vector2(-28,110);
                    overlay.gameObject.AddComponent<UnityEngine.UI.Shadow>();
                    var gates = V<Transform[]>(run, "gates");
                    float maxError = 0;
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                        if (t.name.StartsWith("CP_") && int.TryParse(t.name.Substring(3,2), out int index) && index < gates.Length)
                            maxError = Mathf.Max(maxError, Vector3.Distance(t.position, gates[index].position));
                    Check("FBX markers match manifest", maxError < .01f, "maximum position error=" + maxError.ToString("F6") + " m");
                    vehicle.Interact(); next = Time.time + 3f;
                    break;
                case 1:
                    Check("Station entry", V<bool>(vehicle, "localSeated") && V<int>(vehicle, "driverId") == Networking.LocalPlayer.playerId, "driver=" + V<int>(vehicle, "driverId"));
                    start = body.position; Input(1); next = Time.time + 2.4f;
                    break;
                case 2:
                    Check("Free drive acceleration", body.position.z - start.z > 10 && body.velocity.magnitude > 5 && V<int>(vehicle,"motionMode") == 1, Pose());
                    Capture("clientsim-free-drive"); Input(0); next = Time.time + 1f;
                    break;
                case 3:
                    Event(vehicle, "Recover"); Event(run, "BeginRun"); Input(1); next = Time.time + 2.1f;
                    break;
                case 4:
                    Check("Time attack start gate", V<bool>(run,"running") && V<int>(run,"nextGate") == 1, "nextGate=" + V<int>(run,"nextGate") + "; " + V<string>(run,"status"));
                    Capture("clientsim-time-attack"); Input(0); next = Time.time + 1f;
                    break;
                case 5:
                    Event(vehicle,"Recover");
                    Check("Reset cancels run", !V<bool>(run,"running") && V<int>(run,"nextGate") == 0 && Vector3.Distance(body.position, V<Transform>(vehicle,"spawn").position) < .1f, Pose());
                    var b = 72f * Mathf.Deg2Rad;
                    var wallUp = new Vector3(0, Mathf.Cos(b), -Mathf.Sin(b));
                    Place(new Vector3(90, 8 * Mathf.Sin(b), 340) + wallUp * .04f, Quaternion.LookRotation(Vector3.right, wallUp), Vector3.right * 12f);
                    modeMask = 0; Input(.3f); next = Time.time + .9f;
                    break;
                case 6:
                    Check("Banked section contact", (modeMask & (1 << 2)) != 0, Pose() + "; modeMask=" + modeMask + "; scripted placement at 72 degree bank");
                    Capture("clientsim-bank-section"); Input(0); next = Time.time + 1f;
                    break;
                case 7:
                    // Place before the actual launch gate, then let physics cross it in free drive.
                    Place(new Vector3(180, 8.8f, 157), Quaternion.LookRotation(new Vector3(0, .18f, -1), Vector3.up), new Vector3(0, 6f, -34f));
                    modeMask = 0; Input(1); next = Time.time + 1.8f;
                    break;
                case 8:
                    Check("Free drive launch gate and assisted flight", V<bool>(vehicle,"flightAllowed") && (modeMask & (1 << 3)) != 0, Pose() + "; modeMask=" + modeMask);
                    Capture("clientsim-assisted-flight"); Input(0); next = Time.time + 1f;
                    break;
                case 9:
                    Event(vehicle,"Recover"); Event(run,"BeginRun");
                    body.position += Vector3.right * 25; next = Time.time + .3f;
                    break;
                case 10:
                    Check("Discontinuity invalidates run", !V<bool>(run,"running") && V<string>(run,"status").Contains("discontinuity"), V<string>(run,"status"));
                    Event(vehicle,"Recover"); next = Time.time + .2f;
                    break;
                case 11:
                    testLabel = "ATTITUDE RECOVERY / SCRIPTED TILT";
                    Place(new Vector3(0,22,100),Quaternion.Euler(18,0,-38),new Vector3(0,0,6));
                    initialTilt = Vector3.Angle(body.transform.up,Vector3.up);
                    next = Time.time + .18f;
                    break;
                case 12:
                    var demand = V<float[]>(vehicle,"thrusterDemand");
                    var torque = V<Vector3>(vehicle,"attitudeCommand");
                    float rollMoment = -demand[0]+demand[1]-demand[2]+demand[3];
                    float pitchMoment = -demand[0]-demand[1]+demand[2]+demand[3];
                    Check("Nozzle demand follows correction moment", rollMoment*torque.z > 0 && pitchMoment*torque.x > 0,
                        "torque="+torque.ToString("F3")+"; down jets="+string.Join(",",demand));
                    var nozzle = V<ParticleSystem[]>(vehicle,"thrusterJets")[0].transform;
                    Check("Nozzle exhaust follows body orientation",Vector3.Dot(nozzle.forward,-body.transform.up)>.999f && Vector3.Angle(nozzle.forward,Vector3.down)>10,
                        "world downward angle="+Vector3.Angle(nozzle.forward,Vector3.down).ToString("F2"));
                    Vector3[] modeledForces = { Vector3.up,Vector3.up,Vector3.up,Vector3.up,Vector3.right,Vector3.left,Vector3.forward };
                    var allJets = V<ParticleSystem[]>(vehicle,"thrusterJets");
                    float minimumOpposition=1f;
                    for(int j=0;j<7;j++) minimumOpposition=Mathf.Min(minimumOpposition,Vector3.Dot(-allJets[j].transform.forward,body.transform.TransformDirection(modeledForces[j])));
                    Check("All seven exhaust directions oppose modeled force",minimumOpposition>.999f,"minimum opposite-direction dot="+minimumOpposition.ToString("F6"));
                    Capture("clientsim-thruster-attitude"); next = Time.time + .72f;
                    break;
                case 13:
                    float tilt = Vector3.Angle(body.transform.up,Vector3.up);
                    Check("Air attitude error converges",tilt < initialTilt*.55f,"initial="+initialTilt.ToString("F3")+"; after 0.9 s="+tilt.ToString("F3"));
                    Place(new Vector3(0,22,100),Quaternion.identity,Vector3.zero);
                    body.angularVelocity = new Vector3(1.5f,.8f,-1f); next = Time.time+.45f;
                    break;
                case 14:
                    Check("Angular velocity damping",body.angularVelocity.magnitude < .8f,"initial=1.972; after .45 s="+body.angularVelocity.magnitude.ToString("F3"));
                    testLabel="LANDING A/B / ASSIST DISABLED";
                    Place(new Vector3(0,7,80),Quaternion.identity,new Vector3(0,-11,12));
                    vehicle.SetProgramVariable("landingAssistEnabled",false);
                    contactStart=V<int>(vehicle,"touchdownCount"); landingSample=true; reboundPeak=0; brakeHeight=0; mistHeight=0;
                    next=Time.time+1.5f;
                    break;
                case 15:
                    baselineImpact=V<float>(vehicle,"lastTouchdownSpeed");
                    Check("Unassisted control drop contacts road",V<int>(vehicle,"touchdownCount")>contactStart && baselineImpact>10,
                        "touchdown approach="+baselineImpact.ToString("F3")+" m/s; rebound="+reboundPeak.ToString("F3"));
                    testLabel="LANDING A/B / HEIGHT + CLOSING SPEED BRAKE";
                    Place(new Vector3(0,7,80),Quaternion.identity,new Vector3(0,-11,12));
                    vehicle.SetProgramVariable("landingAssistEnabled",true);
                    contactStart=V<int>(vehicle,"touchdownCount"); reboundPeak=0; brakeHeight=0; mistHeight=0;
                    next=Time.time+.65f;
                    break;
                case 16:
                    next=Time.time+.6f;
                    break;
                case 17:
                    next=Time.time+1.2f;
                    break;
                case 18:
                    assistedImpact=V<float>(vehicle,"lastTouchdownSpeed"); landingSample=false;
                    Check("Precontact landing braking",brakeHeight>3f,"highest braking clearance="+brakeHeight.ToString("F3")+" m");
                    Check("Ground mist begins before contact",mistHeight>.2f,"mist observed at clearance="+mistHeight.ToString("F3")+" m");
                    Check("Landing assistance reduces impact approach",V<int>(vehicle,"touchdownCount")>contactStart && assistedImpact<baselineImpact*.6f,
                        "baseline="+baselineImpact.ToString("F3")+"; assisted="+assistedImpact.ToString("F3")+" m/s");
                    Check("Ground settling without hovering",Mathf.Abs(body.velocity.y)<.3f && body.position.y<.2f,"height="+body.position.y.ToString("F3")+"; vertical="+body.velocity.y.ToString("F3"));
                    Check("Horizontal momentum retained",body.velocity.z>10f,"initial=12; after landing="+body.velocity.z.ToString("F3")+" m/s");
                    Check("Postcontact rebound bounded",reboundPeak<1.5f,"peak upward velocity="+reboundPeak.ToString("F3")+" m/s");
                    // Stop valve output in mid-air without clearing particles: residual smoke must age naturally.
                    Place(new Vector3(0,18,100),Quaternion.Euler(0,0,-40),Vector3.zero);
                    next=Time.time+.3f;
                    break;
                case 19:
                    vehicle.SetProgramVariable("attitudeStrength",0f); vehicle.SetProgramVariable("attitudeDamping",0f);
                    body.rotation=Quaternion.identity; body.angularVelocity=Vector3.zero;
                    vehicle.SetProgramVariable("landingAssistEnabled",false);
                    smokeAtStop=0; foreach(var ps in V<ParticleSystem[]>(vehicle,"thrusterSmoke")) smokeAtStop+=ps.particleCount;
                    next=Time.time+.12f;
                    break;
                case 20:
                    int residual=0; foreach(var ps in V<ParticleSystem[]>(vehicle,"thrusterSmoke")) residual+=ps.particleCount;
                    Check("Smoke persists after valve closes",smokeAtStop>0 && residual>0,"at close="+smokeAtStop+"; after .12 s="+residual);
                    next=Time.time+3f;
                    break;
                case 21:
                    int expired=0; foreach(var ps in V<ParticleSystem[]>(vehicle,"thrusterSmoke")) expired+=ps.particleCount;
                    Check("Smoke expires within lifetime",expired==0,"after 3.12 s="+expired);
                    vehicle.SetProgramVariable("attitudeStrength",9f); vehicle.SetProgramVariable("attitudeDamping",4.5f);
                    vehicle.SetProgramVariable("landingAssistEnabled",true);
                    testLabel="SCRIPTED VERTICAL DROP / APPROACH PLUMES";
                    Place(new Vector3(0,7,100),Quaternion.identity,new Vector3(0,-11,0));
                    next=Time.time+.6f;
                    break;
                case 22:
                    Capture("clientsim-thruster-approach"); next=Time.time+.75f;
                    break;
                case 23:
                    testLabel="SCRIPTED VERTICAL DROP / TOUCHDOWN MIST";
                    Capture("clientsim-thruster-touchdown"); next=Time.time+.7f;
                    break;
                case 24:
                    testLabel="THRUST OFF / WORLD-SPACE RESIDUAL MIST";
                    Capture("clientsim-thruster-residual"); next=Time.time+1f;
                    break;
                case 25:
                    testLabel="ASSISTED FLIGHT / TEMPORARY FLAT LANDING FIXTURE";
                    flightLandingPad=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    flightLandingPad.name="Validation-only landing pad";
                    flightLandingPad.transform.position=new Vector3(180,19.9f,80);
                    flightLandingPad.transform.localScale=new Vector3(12,.2f,120);
                    Place(new Vector3(180,23,40),Quaternion.identity,new Vector3(0,-2,28));
                    Event(vehicle,"EnableFlight"); contactStart=V<int>(vehicle,"touchdownCount"); modeMask=0;
                    next=Time.time+2.6f;
                    break;
                case 26:
                    Check("Assisted flight settles on temporary flat test pad",(modeMask&(1<<3))!=0 && V<int>(vehicle,"touchdownCount")>contactStart && V<int>(vehicle,"motionMode")==1 && Mathf.Abs(body.position.y-20)<.2f,
                        "initial clearance=3 m; closing=2 m/s; forward=28 m/s; after 2.6 s: "+Pose());
                    Event(vehicle,"Recover"); Event(vehicle,"ExitVehicle");
                    UnityEngine.Object.Destroy(flightLandingPad); next=Time.time+1f;
                    break;
                case 27:
                    testLabel="COCKPIT INPUT / SIMULATED OCCUPANCY BRANCH";
                    var yokes=V<Transform[]>(vehicle,"yokes");
                    var accelerators=V<Transform[]>(vehicle,"throttlePedals");
                    var brakes=V<Transform[]>(vehicle,"brakePedals");
                    Check("Cockpit moving parts wired across LODs",yokes.Length==3 && accelerators.Length==3 && brakes.Length==3 && yokes[0].childCount>0 && accelerators[0].childCount>0 && brakes[0].childCount>0,"3 yokes + 3 throttle pedals + 3 brake pedals");
                    Check("Movement exit disabled with explicit escape",V<VRC.SDK3.Components.VRCStation>(vehicle,"station").disableStationExit,"X and EXIT use ExitStation; Jump is flight control");
                    vehicle.SetProgramVariable("driverId",99);Action(5).Interact();
                    Check("Occupied boarding feedback",V<string>(vehicle,"controlFeedback").Contains("OCCUPIED") && !V<bool>(vehicle,"pendingSeat"),"simulated driverId=99; "+V<string>(vehicle,"controlFeedback"));
                    vehicle.SetProgramVariable("driverId",-1);Action(5).Interact();next=Time.time+2f;
                    break;
                case 28:
                    Check("Door control enters station",V<bool>(vehicle,"localSeated") && V<int>(vehicle,"driverId")==Networking.LocalPlayer.playerId,"RoadsterAction Interact -> vehicle -> station");
                    ClientSimPlayerStationManager stationManager=null;
                    foreach(var candidate in Resources.FindObjectsOfTypeAll<ClientSimPlayerStationManager>())
                        if(candidate.InStation()) { stationManager=candidate;break; }
                    if(stationManager==null)throw new InvalidOperationException("No active ClientSim station manager");
                    bool canMove=stationManager.CanPlayerMove(1f);
                    Check("Move axis does not unseat driver",!canMove && stationManager.InStation() && V<bool>(vehicle,"localSeated"),"ClientSim station movement path, axis=1");
                    Input(.08f,.08f);next=Time.time+.3f;
                    break;
                case 29:
                    Check("Input deadzone rejects drift",Mathf.Abs(V<float>(vehicle,"throttle"))<.001f && Mathf.Abs(V<float>(vehicle,"steering"))<.001f,"SDK InputMove callbacks at .08; deadzone=.12");
                    // The ClientSim mannequin has no seated IK; exclude it only from the QA camera.
                    // This does not move the real station/eye or modify the saved scene.
                    foreach(var avatar in Resources.FindObjectsOfTypeAll<ClientSimPlayerAvatarManager>())
                        if(avatar.gameObject.scene.IsValid()) foreach(var renderer in avatar.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer=10;
                    Input(1,.55f);cockpitCameraMode=1;testLabel="AUTHORED DRIVER EYE / SDK INPUT EVENTS / NOT HMD TRACKING";next=Time.time+.65f;
                    break;
                case 30:
                    if(CaptureBeforeChange("clientsim-cockpit-driving"))break;
                    float speedRead;bool speedParsed=float.TryParse(V<UnityEngine.UI.Text>(run,"speedText").text,out speedRead);
                    Check("Live speed and road mode instruments",speedParsed && Mathf.Abs(speedRead-body.velocity.magnitude*3.6f)<10 && V<UnityEngine.UI.Text>(run,"modeText").text=="ROAD","display="+speedRead+" km/h; physics="+(body.velocity.magnitude*3.6f).ToString("F2"));
                    float yokeAngle=Quaternion.Angle(Quaternion.identity,V<Transform[]>(vehicle,"yokes")[0].localRotation);
                    float pedalAngle=Quaternion.Angle(Quaternion.identity,V<Transform[]>(vehicle,"throttlePedals")[0].localRotation);
                    Check("Yoke and throttle pedal follow input",yokeAngle>5 && pedalAngle>8,"yoke="+yokeAngle.ToString("F2")+"; pedal="+pedalAngle.ToString("F2"));
                    start=body.position;Action(0).Interact();
                    Check("Moving reset requires confirmation",Vector3.Distance(body.position,start)<.1f && V<string>(vehicle,"controlFeedback").Contains("PRESS AGAIN"),V<string>(vehicle,"controlFeedback"));next=Time.time+.2f;
                    break;
                case 31:
                    Action(0).Interact();
                    Check("Confirmed reset clears inputs",body.velocity.magnitude<.01f && Mathf.Abs(V<float>(vehicle,"rawThrottle"))<.001f && Mathf.Abs(V<float>(vehicle,"rawSteering"))<.001f,"second Interact restores start box and neutral controls");
                    Action(1).Interact();Input(1);next=Time.time+2.1f;
                    break;
                case 32:
                    Check("Time attack panel starts real run",V<bool>(run,"running") && V<int>(run,"nextGate")==1 && V<UnityEngine.UI.Text>(run,"lapText").text.Contains("LAP 1 / 1"),V<UnityEngine.UI.Text>(run,"lapText").text);
                    Event(vehicle,"Recover");Action(3).Interact();Action(3).Interact();Input(0,1);next=Time.time+.3f;
                    break;
                case 33:
                    Check("Steering profile changes full-scale response",V<int>(vehicle,"sensitivityProfile")==0 && Mathf.Abs(V<float>(vehicle,"steering")-.65f)<.01f,"COMFORT full axis -> "+V<float>(vehicle,"steering").ToString("F3"));
                    Action(3).Interact();Input(-1);next=Time.time+.3f;
                    break;
                case 34:
                    float brakeAngle=Quaternion.Angle(Quaternion.identity,V<Transform[]>(vehicle,"brakePedals")[0].localRotation);
                    Check("Brake pedal follows negative axis",brakeAngle>8 && V<UnityEngine.UI.Text>(run,"driveText").text.Contains("BRAKE"),"brake pedal="+brakeAngle.ToString("F2"));
                    Place(new Vector3(180,20,120),Quaternion.Euler(12,180,24),new Vector3(0,-4,-25));Event(vehicle,"EnableFlight");Input(1,.1f);next=Time.time+.3f;
                    break;
                case 35:
                    Check("Flight and thruster cockpit telemetry",V<UnityEngine.UI.Text>(run,"modeText").text=="FLIGHT" && V<UnityEngine.UI.Text>(run,"systemText").text.Contains("AUTO") && V<float[]>(vehicle,"thrusterDemand")[6]>.4f,V<UnityEngine.UI.Text>(run,"systemText").text);
                    Capture("clientsim-cockpit-flight");next=Time.time+.3f;
                    break;
                case 36:
                    Event(vehicle,"Recover");cockpitCameraMode=2;testLabel="LIVE CONTROLS PANEL / INTERACT BUTTONS / CLIENTSIM";Action(4).Interact();next=Time.time+.2f;
                    break;
                case 37:
                    Check("Help control changes live panel",!V<bool>(vehicle,"showControlHelp") && V<UnityEngine.UI.Text>(run,"helpText").text.Contains("HELP H"),"help hidden via physical panel Interact");Action(4).Interact();next=Time.time+.2f;
                    break;
                case 38:
                    if(CaptureBeforeChange("clientsim-cockpit-controls"))break;
                    Input(1);next=Time.time+.5f;
                    break;
                case 39:
                    UdonManager.Instance.RunInputAction(UdonManager.UDON_INPUT_JUMP,new UdonInputEventArgs(true,HandType.RIGHT));
                    Check("Jump no longer exits the driver seat",V<bool>(vehicle,"localSeated"),"Jump is now a pilot input");
                    Button(UdonManager.UDON_INPUT_JUMP,false);Action(2).Interact();
                    cockpitCameraMode=3;testLabel="DRIVER ACCESS / SAFE EXIT TO PADDOCK";next=Time.time+1f;
                    break;
                case 40:
                    var safe=V<Transform>(vehicle,"safeExit");var playerPosition=Networking.LocalPlayer.GetPosition();
                    Check("Explicit EXIT safely returns to paddock",!V<bool>(vehicle,"localSeated") && V<int>(vehicle,"driverId")==-1 && Vector3.Distance(playerPosition,safe.position)<1.5f,"player="+playerPosition.ToString("F2")+"; exit="+safe.position.ToString("F2"));
                    Check("Exit restores available boarding state",V<UnityEngine.UI.Text>(vehicle,"boardingDisplay").text.Contains("AVAILABLE") && body.velocity.magnitude<.01f,V<UnityEngine.UI.Text>(vehicle,"boardingDisplay").text);
                    Capture("clientsim-cockpit-boarding");next=Time.time+.6f;
                    break;
                case 41:
                    Neutral();Action(5).Interact();next=Time.time+1.2f;
                    break;
                case 42:
                    Check("Boarding defaults to Auto and landing assist",!V<bool>(vehicle,"manualFlight") && V<bool>(vehicle,"landingAssistEnabled"),"new driver defaults");
                    Action(6).Interact();Place(new Vector3(0,50,80),Quaternion.Euler(18,0,30),Vector3.zero);
                    manualInitialRotation=body.rotation;testLabel="MANUAL / NEUTRAL RATE DAMPING / SCRIPTED INPUT";next=Time.time+.7f;
                    break;
                case 43:
                    Check("Manual neutral preserves tilt with landing assist on",Quaternion.Angle(body.rotation,manualInitialRotation)<1f && V<bool>(vehicle,"landingAssistEnabled"),"orientation delta="+Quaternion.Angle(body.rotation,manualInitialRotation));
                    body.angularVelocity=new Vector3(1.5f,.8f,-1f);next=Time.time+.45f;
                    break;
                case 44:
                    Check("Manual neutral damps angular velocity",body.angularVelocity.magnitude<.5f,"initial=1.972; final="+body.angularVelocity.magnitude);
                    SampleAxis("Manual pitch isolated",1,0,0,false,false,new Vector3(1,0,0));
                    SampleAxis("Manual yaw isolated",0,1,0,false,false,new Vector3(0,1,0));
                    SampleAxis("Manual roll isolated",0,0,1,false,false,new Vector3(0,0,-1));
                    SampleAxis("Manual three axes simultaneous",-1,-1,-1,true,false,new Vector3(-1,-1,1));
                    Check("Manual Space supplies forward thrust",V<float>(vehicle,"throttle")>0f,"Space adapter -> thrust="+V<float>(vehicle,"throttle"));
                    vehicle.SetProgramVariable("desktopBrake",true);Event(vehicle,"ProcessPilotInput");
                    Check("Desktop Shift overrides held thrust",V<float>(vehicle,"throttle")<=0f,"throttle="+V<float>(vehicle,"throttle"));
                    Event(vehicle,"ToggleFlightMode");Event(vehicle,"ProcessPilotInput");
                    Check("Mode change clears and blocks held controls",!V<bool>(vehicle,"inputArmed") && V<float>(vehicle,"throttle")==0f && V<float>(vehicle,"pitchInput")==0f,"held inputs require neutral");
                    Neutral();Action(6).Interact();Place(new Vector3(180,35,100),Quaternion.Euler(12,180,24),new Vector3(0,0,-15));Event(vehicle,"EnableFlight");
                    Input(.6f,.25f);thrustAfterArm=true;cockpitCameraMode=1;testLabel="MANUAL DESKTOP / SDK MOVE + JUMP / NOT PHYSICAL KEYS";next=Time.time+.6f;
                    break;
                case 45:
                    if(CaptureBeforeChange("clientsim-manual-desktop"))break;
                    Neutral();Place(new Vector3(180,50,100),Quaternion.identity,Vector3.up*10f);Event(vehicle,"EnableFlight");InstallInputFixture();next=Time.time+.3f;
                    break;
                case 46:
                    Check("VR input method selected by SDK hook",V<bool>(vehicle,"vrInput") && V<string>(vehicle,"inputMethodName")=="SteamVR2","synthetic ClientSim VR identity; no HMD");
                    SendMove(1,0);Button(UdonManager.UDON_INPUT_USE,true);Event(vehicle,"ProcessPilotInput");
                    Check("Unheld yoke blocks right trigger and stick Y",V<float>(vehicle,"throttle")==0f && !V<bool>(vehicle,"yokeHeld"),"no throttle from left stick Y or panel trigger");
                    Neutral();Button(UdonManager.UDON_INPUT_GRAB,true,HandType.LEFT);Event(vehicle,"ProcessPilotInput");
                    Check("Left grip is neither yoke nor index brake",!V<bool>(vehicle,"yokeHeld") && !V<bool>(vehicle,"leftUseHeld") && V<float>(vehicle,"throttle")==0f,"InputGrab LEFT is separate from InputUse LEFT");
                    Button(UdonManager.UDON_INPUT_GRAB,false,HandType.LEFT);
                    fixtureHandOffset=Vector3.right;Button(UdonManager.UDON_INPUT_GRAB,true);
                    Check("Yoke grip outside reach is rejected",!V<bool>(vehicle,"yokeHeld"),"synthetic hand 1 m from anchor");
                    Button(UdonManager.UDON_INPUT_GRAB,false);fixtureHandOffset=Vector3.zero;
                    Button(UdonManager.UDON_INPUT_GRAB,true);
                    Check("Right grip acquires nearby yoke",V<bool>(vehicle,"yokeHeld"),"InputGrab RIGHT, separate from InputUse");
                    Event(vehicle,"ProcessPilotInput");
                    Check("Right grip does not become accelerator",!V<bool>(vehicle,"rightUseHeld") && !V<bool>(vehicle,"leftUseHeld") && V<float>(vehicle,"throttle")==0f,"grip acquires pose control only");
                    Button(UdonManager.UDON_INPUT_USE,true);next=Time.time+.3f;
                    break;
                case 47:
                    Check("Right index button ramps forward thrust",V<float>(vehicle,"throttle")>.8f,"binary Use -> smoothed throttle="+V<float>(vehicle,"throttle"));
                    Button(UdonManager.UDON_INPUT_USE,true,HandType.LEFT);Event(vehicle,"ProcessPilotInput");
                    Check("Both index triggers prioritize brake immediately",V<float>(vehicle,"throttle")<=0f,"right+left held; throttle="+V<float>(vehicle,"throttle"));
                    next=Time.time+.2f;
                    break;
                case 48:
                    Check("Left index button ramps braking",V<float>(vehicle,"throttle")<-.8f,"throttle="+V<float>(vehicle,"throttle"));
                    Button(UdonManager.UDON_INPUT_USE,false);Event(vehicle,"ProcessPilotInput");
                    Check("Right trigger release preserves left brake",V<bool>(vehicle,"leftUseHeld") && !V<bool>(vehicle,"rightUseHeld") && V<float>(vehicle,"rawThrottle")==-1f,"independent right release with left held");
                    Button(UdonManager.UDON_INPUT_USE,true);Button(UdonManager.UDON_INPUT_USE,false,HandType.LEFT);Event(vehicle,"ProcessPilotInput");
                    Check("Left trigger release preserves right request",!V<bool>(vehicle,"leftUseHeld") && V<bool>(vehicle,"rightUseHeld") && V<float>(vehicle,"rawThrottle")==1f,"independent left release; smooth output crosses neutral over time");
                    Button(UdonManager.UDON_INPUT_USE,false,HandType.LEFT);Button(UdonManager.UDON_INPUT_USE,false);next=Time.time+.25f;
                    break;
                case 49:
                    Check("Trigger release returns to zero",Mathf.Abs(V<float>(vehicle,"throttle"))<.001f,"zero after smooth release");
                    fixtureHandRotation=Quaternion.Euler(22,0,0);SendMove(0,0);next=Time.time+.3f;
                    break;
                case 50:
                    Check("VR yoke pitch isolated",V<float>(vehicle,"pitchInput")>.3f && Mathf.Abs(V<float>(vehicle,"rollInput"))<.02f && Mathf.Abs(V<float>(vehicle,"steering"))<.02f,"synthetic right-hand pitch 22 degrees");
                    fixtureHandRotation=Quaternion.Euler(0,0,25);next=Time.time+.3f;
                    break;
                case 51:
                    Check("VR yoke roll isolated",V<float>(vehicle,"rollInput")>.4f && Mathf.Abs(V<float>(vehicle,"pitchInput"))<.02f,"synthetic right-hand roll 25 degrees");
                    fixtureHandRotation=Quaternion.identity;SendMove(0,1);next=Time.time+.3f;
                    break;
                case 52:
                    Check("VR left stick yaw isolated",V<float>(vehicle,"steering")>.8f && Mathf.Abs(V<float>(vehicle,"pitchInput"))<.02f && Mathf.Abs(V<float>(vehicle,"rollInput"))<.02f,"Look inputs unused; yaw from MoveHorizontal");
                    SendMove(1,0);next=Time.time+.3f;
                    break;
                case 53:
                    Check("VR left stick vertical is unused",Mathf.Abs(V<float>(vehicle,"throttle"))<.001f && Mathf.Abs(V<float>(vehicle,"pitchInput"))<.01f,"MoveVertical=1 with no index trigger");
                    fixtureHandRotation=Quaternion.Euler(18,0,24);SendMove(0,.6f);Button(UdonManager.UDON_INPUT_USE,true);
                    testLabel="MANUAL VR BRANCH / SYNTHETIC HAND + SDK BUTTONS / NOT HMD";next=Time.time+.3f;
                    break;
                case 54:
                    if(CaptureBeforeChange("clientsim-manual-vr-inputs"))break;
                    Check("VR three axes plus thrust simultaneous",V<float>(vehicle,"pitchInput")>.2f && V<float>(vehicle,"rollInput")>.3f && V<float>(vehicle,"steering")>.2f && V<float>(vehicle,"throttle")>.8f,"P/Y/R="+new Vector3(V<float>(vehicle,"pitchInput"),V<float>(vehicle,"steering"),V<float>(vehicle,"rollInput")));
                    Action(3).Interact();Event(vehicle,"ProcessPilotInput");
                    Check("Panel Interact disarms held trigger",!V<bool>(vehicle,"yokeHeld") && !V<bool>(vehicle,"inputArmed") && V<float>(vehicle,"throttle")==0f,"panel disarms before action");
                    Neutral();fixtureHandRotation=Quaternion.identity;next=Time.time+.25f;
                    break;
                case 55:
                    Button(UdonManager.UDON_INPUT_GRAB,true);Button(UdonManager.UDON_INPUT_USE,true);next=Time.time+.25f;
                    break;
                case 56:
                    Button(UdonManager.UDON_INPUT_GRAB,false);Event(vehicle,"ProcessPilotInput");
                    Check("Releasing yoke cuts held acceleration",!V<bool>(vehicle,"yokeHeld") && V<float>(vehicle,"throttle")==0f,"right Use remains held while grip releases");
                    Button(UdonManager.UDON_INPUT_GRAB,true);
                    Check("Held accelerator cannot reacquire yoke",!V<bool>(vehicle,"yokeHeld"),"release index trigger before regripping");
                    Neutral();next=Time.time+.25f;
                    break;
                case 57:
                    Button(UdonManager.UDON_INPUT_GRAB,true);Button(UdonManager.UDON_INPUT_USE,true);Event(vehicle,"ProcessPilotInput");
                    Neutral();Event(vehicle,"ProcessPilotInput");
                    Check("SDK menu-style releases clear VR propulsion",!V<bool>(vehicle,"yokeHeld") && V<float>(vehicle,"throttle")==0f,"synthetic release events; not real menu or focus actuation");
                    ArmSample();Button(UdonManager.UDON_INPUT_GRAB,true);Button(UdonManager.UDON_INPUT_USE,true);Event(vehicle,"ProcessPilotInput");
                    // Suspension is the supported SDK boundary; this is not an OS Alt-Tab test.
                    foreach(var p in Resources.FindObjectsOfTypeAll<ClientSimPlayer>())if(p.Player!=null && p.Player.isLocal)p.isSuspended=true;
                    Event(vehicle,"ProcessPilotInput");
                    Check("SDK suspension clears held controls",!V<bool>(vehicle,"inputArmed") && !V<bool>(vehicle,"yokeHeld") && V<float>(vehicle,"throttle")==0f,"ClientSim isSuspended=true, real Udon branch");
                    foreach(var p in Resources.FindObjectsOfTypeAll<ClientSimPlayer>())if(p.Player!=null && p.Player.isLocal)p.isSuspended=false;
                    Event(vehicle,"ProcessPilotInput");
                    Check("Resume waits for neutral",!V<bool>(vehicle,"inputArmed"),"held index/grip do not rearm");
                    Neutral();next=Time.time+.25f;
                    break;
                case 58:
                    if(!gapSample)
                    {
                        Button(UdonManager.UDON_INPUT_GRAB,true);Button(UdonManager.UDON_INPUT_USE,true);Event(vehicle,"ProcessPilotInput");
                        vehicle.SetProgramVariable("lastControlFrame",Time.realtimeSinceStartup-.7f);
                        gapSample=true;stage--;next=Time.time+.1f;break;
                    }
                    Check("Long frame gap disarms controls",!V<bool>(vehicle,"inputArmed") && V<float>(vehicle,"throttle")==0f,"synthetic 0.7 s clock gap; no OS focus API");
                    ArmSample();
                    Button(UdonManager.UDON_INPUT_GRAB,true);Button(UdonManager.UDON_INPUT_USE,true);Event(vehicle,"ProcessPilotInput");
                    vehicle.RunEvent("_onOwnershipTransferred",("Player",Networking.LocalPlayer));
                    Check("Ownership callback clears controls",!V<bool>(vehicle,"inputArmed") && V<float>(vehicle,"throttle")==0f && !V<bool>(vehicle,"yokeHeld"),"single-client callback branch; not network transfer");
                    Neutral();RestoreInputFixture();
                    UdonManager.Instance.RunEvent(UdonManager.UDON_EVENT_ONINPUTMETHODCHANGED,("inputMethod",VRCInputMethod.Keyboard));
                    Check("Input family change clears VR state",!V<bool>(vehicle,"vrInput") && !V<bool>(vehicle,"inputArmed"),"VR fixture restored to ClientSim desktop");
                    Action(7).Interact();
                    Check("Landing switch independent of Manual",V<bool>(vehicle,"manualFlight") && !V<bool>(vehicle,"landingAssistEnabled"),"Manual stays selected; landing OFF");
                    Action(7).Interact();Action(6).Interact();Neutral();Place(new Vector3(180,35,100),Quaternion.identity,Vector3.zero);Event(vehicle,"EnableFlight");
                    next=Time.time+.3f;
                    break;
                case 59:
                    Button(UdonManager.UDON_INPUT_JUMP,true);start=body.position;testLabel="AUTO / JUMP LIFT IN PERMITTED FLIGHT ZONE";next=Time.time+.45f;
                    break;
                case 60:
                    Check("Auto Jump supplies permitted lift without exiting",V<bool>(vehicle,"localSeated") && body.velocity.y>0 && V<float>(vehicle,"liftInput")>.8f,"height delta="+(body.position.y-start.y)+"; vy="+body.velocity.y);
                    Neutral();Event(vehicle,"DisableFlight");Button(UdonManager.UDON_INPUT_JUMP,true);next=Time.time+.35f;
                    break;
                case 61:
                    if(!boundsSample)
                    {
                        Check("Jump cannot bypass flight permission",V<int>(vehicle,"motionMode")==4 && !V<bool>(vehicle,"flightAllowed") && V<float>(vehicle,"appliedLift")==0f,"permission removed; no applied lift or lift exhaust");
                        Neutral();body.position=new Vector3(211,35,100);Event(vehicle,"EnableFlight");
                        boundsSample=true;stage--;next=Time.time+.1f;break;
                    }
                    Check("Crossing flight envelope revokes permission",!V<bool>(vehicle,"flightAllowed") && V<int>(vehicle,"motionMode")==4,"x=211 beyond permitted max x=210");
                    Neutral();Event(vehicle,"Recover");cockpitCameraMode=2;testLabel="AUTO / MANUAL + INDEPENDENT LANDING / ACTUAL GAME VIEW";next=Time.time+.3f;
                    break;
                case 62:
                    if(CaptureBeforeChange("clientsim-pilot-mode-panel"))break;
                    vehicle.SetProgramVariable("driverId",99);
                    Action(2).Interact();next=Time.time+.5f;
                    break;
                case 63:
                    Check("EXIT survives driver authority mismatch",!V<bool>(vehicle,"localSeated") && V<int>(vehicle,"driverId")==-1,"simulated driverId disagreement; local ExitStation still available");
                    int configuredBudget=0;
                    foreach(var ps in V<ParticleSystem[]>(vehicle,"thrusterJets")) configuredBudget+=ps.main.maxParticles;
                    foreach(var ps in V<ParticleSystem[]>(vehicle,"thrusterSmoke")) configuredBudget+=ps.main.maxParticles;
                    foreach(var ps in V<ParticleSystem[]>(vehicle,"groundMist")) configuredBudget+=ps.main.maxParticles;
                    Check("Particle budget respected",configuredBudget==1280 && particlePeak>0 && particlePeak<=configuredBudget,"whole-run peak="+particlePeak+"; configured cap="+configuredBudget);
                    Check("Station exit releases driver", !V<bool>(vehicle,"localSeated") && V<int>(vehicle,"driverId") == -1, "driver=" + V<int>(vehicle,"driverId"));
                    foreach (var path in report.captures) Check("Capture " + Path.GetFileName(path), File.Exists(path) && new FileInfo(path).Length > 10000, File.Exists(path) ? new FileInfo(path).Length + " bytes" : "missing");
                    Check("No runtime errors", errors.Count == 0, string.Join("\n", errors));
                    File.WriteAllText(Output + "/clientsim-smoke.json", JsonUtility.ToJson(report, true));
                    SessionState.SetInt("RoadsterValidation.ExitCode", report.checks.TrueForAll(c => c.passed) ? 0 : 1);
                    Debug.Log("ROADSTER_SMOKE_COMPLETE"); scheduled = false; EditorApplication.isPlaying = false;
                    break;
            }
        }
        catch (Exception e)
        {
            SessionState.SetInt("RoadsterValidation.ExitCode", 1);
            Check("Harness exception", false, e.ToString());
            File.WriteAllText(Output + "/clientsim-smoke.json", JsonUtility.ToJson(report, true));
            scheduled = false; EditorApplication.isPlaying = false;
        }
    }
}
#endif
