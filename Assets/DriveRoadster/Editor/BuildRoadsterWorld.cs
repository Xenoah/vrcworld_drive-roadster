#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;
using UdonSharp;
using UdonSharp.Compiler;
using VRC.SDK3.Components;

public static class BuildRoadsterWorld
{
    private const string Root = "Assets/DriveRoadster";
    [Serializable] public class Gate { public string name; public string required_mode; public float[] position; public float[] forward; public float[] up; public float width = 16; public float height = 12; }
    [Serializable] public class Pose { public float[] position; public float[] forward; public float[] up; }
    [Serializable] public class FlightGap { public float[] allowed_bounds_center; public float[] allowed_bounds_size; }
    [Serializable] public class CourseData { public Gate[] checkpoints; public Pose spawn; public Pose playerSpawn; public FlightGap flight_gap; public int flightStartGate = -1; public int flightEndGate = -1; }

    [MenuItem("Drive Roadster/Build vertical slice scene")]
    public static void Build()
    {
        // Never overwrite the active user's scene or an existing generated scene implicitly.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string manifestPath = Root + "/Art/Course/course_manifest.json";
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Course manifest missing", manifestPath);
        CourseData data = JsonUtility.FromJson<CourseData>(File.ReadAllText(manifestPath));
        if (data.checkpoints == null || data.checkpoints.Length < 3) throw new InvalidDataException("At least three ordered checkpoints required");
        string vehiclePath = Root + "/Art/Vehicle/DR_Asterion_LOD0.fbx";
        string coursePath = Root + "/Art/Course/VectorCoastal_Course.fbx";
        if (!File.Exists(vehiclePath) || !File.Exists(coursePath)) throw new InvalidDataException("Vehicle and course FBX files are required; generate source assets first");
        EnsureProgramAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject world = new GameObject("DriveRoadsterWorld");
        GameObject course = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(coursePath));
        course.transform.SetParent(world.transform, false);
        MatchManifestHandedness(course);
        foreach (Transform child in course.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.StartsWith("COL_")) continue;
            MeshFilter mesh = child.GetComponent<MeshFilter>();
            if (mesh != null)
            {
                MeshCollider collider = child.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
            }
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
        }
        if (course.GetComponentsInChildren<MeshCollider>().Length == 0) throw new InvalidDataException("Main course must contain COL_ collision meshes");
        Transform spawn = NewTransform("VehicleSpawn", world.transform, Vec(data.spawn.position), Orientation(data.spawn.forward, data.spawn.up));
        Transform playerSpawn = NewTransform("PlayerSpawn", world.transform, Vec(data.playerSpawn.position), Orientation(data.playerSpawn.forward, data.playerSpawn.up));
        var descriptor = world.AddComponent<VRCSceneDescriptor>();
        descriptor.spawns = new[] { playerSpawn };
        GameObject car = new GameObject("Roadster_Authority");
        car.transform.SetParent(world.transform, false);
        car.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        // Walkthrough (17) does not collide with players. Roads remain Default; rays exclude own body.
        car.layer = 17;
        Rigidbody body = car.AddComponent<Rigidbody>();
        body.mass = 1200f; body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.drag = 0.03f; body.angularDrag = 0.5f; body.useGravity = false;
        BoxCollider chassis = car.AddComponent<BoxCollider>();
        chassis.center = new Vector3(0f, .62f, 0f); chassis.size = new Vector3(1.8f, .65f, 4.3f);
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(vehiclePath));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.transform.SetParent(car.transform, false);
        MatchManifestHandedness(model);
        SetLayerRecursively(model, 17);
        RemapVehicleMaterials(model);
        GameObject[] lodModels = new GameObject[3]; lodModels[0] = model;
        LOD[] lods = new LOD[3];
        for (int level = 0; level < 3; level++)
        {
            if (level > 0)
            {
                string lodPath = Root + "/Art/Vehicle/DR_Asterion_LOD" + level + ".fbx";
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(lodPath);
                if (asset == null) throw new FileNotFoundException("Missing LOD", lodPath);
                lodModels[level] = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(lodModels[level], PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                lodModels[level].transform.SetParent(car.transform, false); MatchManifestHandedness(lodModels[level]); SetLayerRecursively(lodModels[level], 17); RemapVehicleMaterials(lodModels[level]);
            }
            lods[level] = new LOD(level == 0 ? .30f : level == 1 ? .10f : .015f, lodModels[level].GetComponentsInChildren<Renderer>());
        }
        LODGroup group = car.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
        Transform seat = NewTransform("DriverSeat", car.transform, new Vector3(-.40f, .53f, -.12f), Quaternion.identity);
        Transform exit = NewTransform("SafeExit", world.transform, playerSpawn.position, playerSpawn.rotation);
        VRCStation station = car.AddComponent<VRCStation>();
        station.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;
        station.seated = true; station.disableStationExit = false;
        station.stationEnterPlayerLocation = seat;
        station.stationExitPlayerLocation = exit;
        RoadsterVehicle vehicle = car.AddUdonSharpComponent<RoadsterVehicle>();
        RoadsterRun run = car.AddUdonSharpComponent<RoadsterRun>();
        vehicle.body = body; vehicle.station = station; vehicle.spawn = spawn; vehicle.safeExit = exit;
        vehicle.run = run; vehicle.flightBoundsCenter = Vec(data.flight_gap.allowed_bounds_center); vehicle.flightBoundsSize = Vec(data.flight_gap.allowed_bounds_size); vehicle.roadMask = 1; run.vehicle = vehicle;
        vehicle.suspensionPoints = new Transform[4]; vehicle.wheels = new Transform[12];
        string[] wheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        for (int i = 0; i < 4; i++)
        {
            vehicle.suspensionPoints[i] = NewTransform("Suspension_" + i, car.transform,
                new Vector3(i % 2 == 0 ? -.88f : .88f, .60f, i < 2 ? 1.42f : -1.38f), Quaternion.identity);
            for (int level = 0; level < 3; level++)
            {
                Transform wheel = FindChild(lodModels[level].transform, wheelNames[i]);
                if (wheel == null) throw new InvalidDataException("Missing wheel: " + wheelNames[i]);
                // Imported FBX pivots use Blender axes. Keep a Unity-aligned animated parent.
                Transform pivot = NewTransform("WheelPivot_LOD" + level + "_" + wheelNames[i], car.transform,
                    car.transform.InverseTransformPoint(wheel.position), Quaternion.identity);
                wheel.SetParent(pivot, true);
                if (wheel.parent != pivot) throw new InvalidOperationException("Wheel pivot reparent failed");
                vehicle.wheels[level * 4 + i] = pivot;
            }
        }
        RoadsterThrusterSetup.Build(vehicle, model.transform);
        run.gates = new Transform[data.checkpoints.Length];
        run.gateHalfWidths = new float[run.gates.Length]; run.gateHalfHeights = new float[run.gates.Length]; run.requiredModes = new int[run.gates.Length];
        for (int i = 0; i < run.gates.Length; i++)
        {
            Gate gate = data.checkpoints[i];
            if (gate.required_mode != "Ground" && gate.required_mode != "WallRide" && gate.required_mode != "AssistedFlight" && gate.required_mode != "Flight" && gate.required_mode != "Air" && gate.required_mode != "Any") throw new InvalidDataException("Unknown gate mode: " + gate.required_mode);
            run.gates[i] = NewTransform("Gate_" + i + "_" + gate.name, world.transform, Vec(gate.position), Orientation(gate.forward, gate.up));
            run.requiredModes[i] = gate.required_mode == "WallRide" ? 2 : gate.required_mode == "AssistedFlight" || gate.required_mode == "Flight" || gate.required_mode == "Air" ? 3 : gate.required_mode == "Ground" ? 1 : 0;
            run.gateHalfWidths[i] = gate.width * .5f; run.gateHalfHeights[i] = gate.height * .5f;
        }
        run.flightStartGate = data.flightStartGate; run.flightEndGate = data.flightEndGate;
        run.display = CreateBoard(world.transform, playerSpawn.position + Vector3.up * 2f + playerSpawn.forward * 5f, "Drive Roadster");
        run.display.transform.parent.rotation = playerSpawn.rotation;
        run.cockpitDisplay = CreateBoard(car.transform, car.transform.TransformPoint(new Vector3(-.4f, .86f, .60f)), "READY");
        run.cockpitDisplay.transform.parent.localRotation = Quaternion.identity;
        run.cockpitDisplay.transform.parent.localScale = Vector3.one * .00055f;
        run.cockpitDisplay.rectTransform.sizeDelta = new Vector2(700, 220);
        // In-cockpit buttons remain usable in VR; desktop R/T shortcuts are supplemental.
        CreateButton(car.transform, new Vector3(.10f, .85f, .50f), "RESTART", 0, vehicle, run);
        CreateButton(car.transform, new Vector3(.30f, .85f, .50f), "TIME ATTACK", 1, vehicle, run);
        CreateButton(car.transform, new Vector3(.50f, .85f, .50f), "EXIT", 2, vehicle, run);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(38, -35, 0);
        RenderSettings.ambientLight = new Color(.35f, .42f, .50f);
        Directory.CreateDirectory(Root + "/Scenes");
        string scenePath = AssetDatabase.GenerateUniqueAssetPath(Root + "/Scenes/DriveRoadster.unity");
        if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Scene save failed");
        AssetDatabase.SaveAssets();
        Debug.Log("Roadster scene generated: " + scenePath + ". Compile UdonSharp and run SDK validation before Build & Test.");
    }

    static void MatchManifestHandedness(GameObject model)
    {
        // These FBX exports import with X reflected in Unity. The JSON uses X-right.
        // Reflect only the imported art, leaving physics, seats and gate frames canonical.
        Vector3 scale = model.transform.localScale;
        scale.x = -scale.x;
        model.transform.localScale = scale;
    }

    static void EnsureProgramAssets()
    {
        // AddUdonSharpComponent requires a compiled program asset even on a fresh clone.
        string[] scriptNames = { "RoadsterVehicle", "RoadsterRun", "RoadsterAction" };
        foreach (string scriptName in scriptNames)
        {
            string scriptPath = Root + "/Scripts/" + scriptName + ".cs";
            MonoScript source = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (source == null) throw new FileNotFoundException("Missing UdonSharp source", scriptPath);
            bool found = false;
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                UdonSharpProgramAsset existing = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && existing.sourceCsScript == source) { found = true; break; }
            }
            if (found) continue;
            UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = source;
            program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            AssetDatabase.CreateAsset(program, AssetDatabase.GenerateUniqueAssetPath(Root + "/Scripts/" + scriptName + ".asset"));
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UdonSharpCompilerV1.CompileSync();
        foreach (UdonSharpProgramAsset program in UdonSharpProgramAsset.GetAllUdonSharpPrograms())
            if (program.sourceCsScript != null && AssetDatabase.GetAssetPath(program.sourceCsScript).StartsWith(Root + "/Scripts/") && program.GetRealProgram() == null)
                throw new InvalidOperationException("Udon compilation failed: " + program.name);
    }

    static void RemapVehicleMaterials(GameObject model)
    {
        string dir = Root + "/Art/GeneratedMaterials"; Directory.CreateDirectory(dir); AssetDatabase.Refresh();
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                string name = materials[i].name.Replace(" (Instance)", "");
                string path = dir + "/" + name + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Standard")); material.name = name;
                    Color color = materials[i].color; float metallic = .05f; float smoothness = .5f;
                    if (name.Contains("PearlAlloy")) { color = new Color(.78f,.81f,.83f); metallic = .75f; smoothness = .88f; }
                    if (name.Contains("Carbon")) { color = new Color(.035f,.042f,.05f); metallic = .2f; smoothness = .45f; }
                    if (name.Contains("Rubber")) { color = new Color(.022f,.024f,.028f); smoothness = .2f; }
                    if (name.Contains("Titanium")) { color = new Color(.38f,.40f,.43f); metallic = .9f; smoothness = .65f; }
                    if (name.Contains("Ivory")) { color = new Color(.78f,.75f,.68f); smoothness = .25f; }
                    if (name.Contains("Glass"))
                    {
                        color = new Color(.34f,.16f,.075f,.34f); metallic = .1f; smoothness = .95f;
                        material.SetFloat("_Mode", 3); material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetInt("_ZWrite", 0); material.EnableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
                    }
                    material.color = color;
                    if (name.Contains("Carbon"))
                    {
                        Texture2D carbonTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Vehicle/Textures/DR_Carbon_BaseColor.png");
                        if (carbonTexture != null) { material.mainTexture = carbonTexture; material.color = Color.white; }
                    }
                    material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", smoothness);
                    if (name.Contains("LED") || name.Contains("ThrusterCyan"))
                    {
                        Color emission = name.Contains("Red") ? new Color(1f,.025f,.008f) : name.Contains("Cyan") ? new Color(.05f,.4f,.5f) : new Color(.7f,.82f,1f);
                        material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", emission);
                    }
                    AssetDatabase.CreateAsset(material, path);
                }
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
        }
    }

    static void CreateButton(Transform parent, Vector3 position, string label, int action, RoadsterVehicle vehicle, RoadsterRun run)
    {
        GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.GetComponent<Collider>().isTrigger = true;
        button.name = label; button.transform.SetParent(parent, false); button.layer = 8;
        button.transform.localPosition = position; button.transform.localScale = new Vector3(.16f, .07f, .12f);
        GameObject labelObject = new GameObject("Label"); labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = position + new Vector3(0f, .06f, -.03f);
        TextMesh text = labelObject.AddComponent<TextMesh>(); text.text = label; text.fontSize = 28; text.characterSize = .012f; text.anchor = TextAnchor.MiddleCenter; text.color = Color.cyan;
        var script = button.AddUdonSharpComponent<RoadsterAction>();
        script.vehicle = vehicle; script.run = run; script.action = action;
    }
    static Text CreateBoard(Transform parent, Vector3 position, string initial)
    {
        GameObject canvasObject = new GameObject("TimingBoard"); canvasObject.transform.SetParent(parent, false);
        canvasObject.transform.position = position; canvasObject.transform.localScale = Vector3.one * .008f;
        Canvas canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        RectTransform panel = canvasObject.GetComponent<RectTransform>(); panel.sizeDelta = new Vector2(700, 500);
        GameObject textObject = new GameObject("Records", typeof(RectTransform)); textObject.transform.SetParent(panel, false);
        Text text = textObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 28; text.color = Color.white; text.text = initial;
        text.rectTransform.sizeDelta = panel.sizeDelta; return text;
    }
    static Transform NewTransform(string name, Transform parent, Vector3 position, Quaternion rotation)
    { Transform t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; t.localRotation = rotation; return t; }
    static Vector3 Vec(float[] xyz) { if (xyz == null || xyz.Length != 3) throw new InvalidDataException("Expected xyz triple"); return new Vector3(xyz[0], xyz[1], xyz[2]); }
    static Quaternion Orientation(float[] forward, float[] up) { return Quaternion.LookRotation(Vec(forward), Vec(up)); }
    static Transform FindChild(Transform root, string name)
    { foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
    static void SetLayerRecursively(GameObject root, int layer) { foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }
}
#endif
