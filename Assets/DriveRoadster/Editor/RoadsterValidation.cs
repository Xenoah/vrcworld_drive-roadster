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
        public string utc, unity, sdk = "3.10.5", environment = "Unity Editor Play Mode / ClientSim / one local simulated player / scripted inputs and section placements";
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
    static float next;
    static bool scheduled;
    static Vector3 start;
    static int modeMask;
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
            stage = 0; next = Time.time + 6f; scheduled = true;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
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
    { vehicle.SetProgramVariable("throttle", throttle); vehicle.SetProgramVariable("steering", steering); }
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
    static void Tick()
    {
        if (!scheduled || !EditorApplication.isPlaying || report == null) return;
        try
        {
            if (body != null)
            {
                modeMask |= 1 << V<int>(vehicle, "motionMode");
                if (overlay != null) overlay.text = "UNITY PLAY MODE / CLIENTSIM - SCRIPTED SECTION TEST\n" +
                    (body.velocity.magnitude * 3.6f).ToString("F0") + " km/h   MODE " + V<int>(vehicle,"motionMode") +
                    "   CP " + V<int>(run,"nextGate") + "/10   " + V<string>(run,"status");
                if (camera != null)
                {
                    var up = body.transform.up;
                    camera.transform.position = body.position - body.transform.forward * 8f + up * 3.5f + body.transform.right * 3f;
                    camera.transform.LookAt(body.position + body.transform.forward * 3f + up * .7f, Vector3.up);
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
                    rect.pivot = new Vector2(.5f,1); rect.anchoredPosition = new Vector2(14,-14); rect.sizeDelta = new Vector2(-28,80);
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
                    Event(vehicle,"Recover"); Event(vehicle,"ExitVehicle"); next = Time.time + 1f;
                    break;
                case 11:
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
