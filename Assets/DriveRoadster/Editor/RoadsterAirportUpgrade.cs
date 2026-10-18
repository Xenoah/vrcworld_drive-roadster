#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using UdonSharp.Compiler;
using VRC.SDK3.Components;
using Object=UnityEngine.Object;

public static class RoadsterAirportUpgrade
{
    const string Root="Assets/DriveRoadster";
    const string Art=Root+"/Art/Airport";
    const int Steps=24;
    static Transform environment;
    static Material asphalt,concrete,grass,white,navy,amber,cyan,red;
    static readonly List<Vector3> route=new List<Vector3>();
    static readonly List<Vector3> ups=new List<Vector3>();
    static readonly List<Transform> gates=new List<Transform>();
    static readonly Vector3[] controls={
        new Vector3(-280,0,-260),new Vector3(-280,0,100),new Vector3(-280,0,470),new Vector3(-240,3,570),new Vector3(-130,8,590),new Vector3(-60,3,530),
        new Vector3(-90,0,440),new Vector3(20,0,380),new Vector3(120,0,460),new Vector3(240,0,450),new Vector3(340,9,350),new Vector3(380,22,240),
        new Vector3(360,18,100),new Vector3(300,0,30),new Vector3(350,0,-60),new Vector3(420,0,-150),new Vector3(400,0,-290),new Vector3(280,0,-380),
        new Vector3(140,0,-350),new Vector3(40,0,-450),new Vector3(-100,0,-470),new Vector3(-220,0,-410),new Vector3(-280,0,-330)};
    static readonly int[] gateSamples={0,48,96,144,192,240,264,288,312,360,408,456,504,0};
    static readonly string[] gateNames={"START","RUNWAY","BANK","SOUTH S","TERMINAL","LAUNCH","AIR 01","AIR 02","LANDING","HANGAR","HAIRPIN","CHICANE","RETURN","FINISH"};
    static readonly int[] modes={1,1,2,1,1,1,3,3,1,1,1,1,1,1};

    [MenuItem("Drive Roadster/Upgrade to 32 car airport")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first");
        EnsurePrograms();
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var original=Object.FindObjectsOfType<RoadsterVehicle>(true).OrderBy(v=>v.fleetNumber).FirstOrDefault();
        if(original==null)throw new InvalidOperationException("Open DriveRoadster scene containing the existing vehicle");
        if(!File.Exists(Root+"/Scenes/BeforeAirport_20261018.unity"))EditorSceneManager.SaveScene(scene,Root+"/Scenes/BeforeAirport_20261018.unity",true);
        original.transform.SetParent(null,true);
        var world=GameObject.Find("DriveRoadsterWorld");
        if(world==null)world=new GameObject("DriveRoadsterWorld");
        // Source scene is preserved above. Replace all old course objects and old cars.
        foreach(Transform child in world.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
        foreach(var extra in Object.FindObjectsOfType<RoadsterVehicle>(true))if(extra!=original)Object.DestroyImmediate(extra.gameObject);
        Directory.CreateDirectory(Art+"/Meshes");Directory.CreateDirectory(Art+"/Materials");Directory.CreateDirectory(Root+"/Prefabs");
        AssetDatabase.Refresh();ConfigureImports();
        asphalt=Mat("RunwayAsphalt",new Color(.095f,.115f,.13f));concrete=Mat("ApronConcrete",new Color(.34f,.39f,.41f));
        grass=Mat("AirfieldGrass",new Color(.22f,.29f,.21f));white=Mat("MarkingWhite",new Color(.85f,.90f,.89f));
        navy=Mat("AirportNavy",new Color(.025f,.06f,.09f));amber=Mat("SafetyAmber",new Color(1,.48f,.08f));
        cyan=Mat("RouteCyan",new Color(.04f,.7f,.8f));red=Mat("CurbRed",new Color(.65f,.07f,.04f));
        environment=New("AirportEnvironment",world.transform,Vector3.zero);
        BuildLand();BuildRoute();BuildPractice();BuildBuildings();
        var playerSpawn=New("PlayerSpawn",world.transform,new Vector3(-560,1,-325));
        var exit=New("SafeExit",world.transform,new Vector3(-552,1,-325));
        var descriptor=world.GetComponent<VRCSceneDescriptor>();if(descriptor==null)descriptor=world.AddComponent<VRCSceneDescriptor>();descriptor.spawns=new[]{playerSpawn};
        var staging=New("TimeTrialStaging",world.transform,new Vector3(-280,.1f,-281));
        BuildEntrance(world.transform);
        var fleet=New("SpawnFleet_32",world.transform,Vector3.zero);
        var spawnRoot=New("FleetSpawnPoints",world.transform,Vector3.zero);
        PrepareVehicle(original);
        original.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        original.spawn=null;original.safeExit=null;original.run.gates=new Transform[0];original.run.timeTrialStart=null;
        foreach(var p in original.GetComponentsInChildren<UdonSharpBehaviour>(true))UdonSharpEditorUtility.CopyProxyToUdon(p);
        PrefabUtility.SaveAsPrefabAsset(original.gameObject,Root+"/Prefabs/Asterion_Shared.prefab");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Asterion_Shared.prefab");
        Object.DestroyImmediate(original.gameObject);
        for(int i=0;i<32;i++)
        {
            var car=(GameObject)PrefabUtility.InstantiatePrefab(prefab,fleet);
            car.name="Roadster_"+(i+1).ToString("D2");
            // Four rows of eight, each with a drive aisle. Every car faces the exit aisle.
            Vector3 position=new Vector3(-530+(i%8)*7,.12f,-288+(i/8)*18);
            var spawn=New("Bay_"+(i+1).ToString("D2"),spawnRoot,position);
            var vehicle=car.GetComponent<RoadsterVehicle>();vehicle.fleetNumber=i+1;vehicle.spawn=spawn;vehicle.safeExit=exit;
            vehicle.station.stationExitPlayerLocation=exit;
            vehicle.flightBoundsCenter=new Vector3(345,35,215);vehicle.flightBoundsSize=new Vector3(230,170,450);
            car.transform.SetPositionAndRotation(position,Quaternion.identity);
            var run=vehicle.run;run.gates=gates.ToArray();run.gateHalfWidths=gates.Select((g,k)=>k==6||k==7 ? 18f : 13f).ToArray();
            run.gateHalfHeights=gates.Select((g,k)=>k==6||k==7 ? 16f : k==2 ? 20f : 8f).ToArray();run.requiredModes=modes.ToArray();
            run.flightStartGate=5;run.flightEndGate=8;run.courseVersion="airfield-v2";run.timeTrialStart=staging;run.display=null;
            ApplyLivery(car,i+1);
            foreach(var p in car.GetComponentsInChildren<UdonSharpBehaviour>(true))UdonSharpEditorUtility.CopyProxyToUdon(p);
            PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);PrefabUtility.RecordPrefabInstancePropertyModifications(run);
            Box("BayLine",environment,new Vector3(position.x-2.6f,.015f,position.z),new Vector3(.12f,.025f,6.5f),white,false);
            Label("BayNumber",environment,(i+1).ToString("D2"),new Vector3(position.x,.035f,position.z-4.4f),Quaternion.Euler(90,0,0),1.1f,white.color);
        }
        var sun=Object.FindObjectsOfType<Light>().FirstOrDefault(l=>l.type==LightType.Directional);
        if(sun==null)sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(36,-24,0);
        RenderSettings.ambientLight=new Color(.58f,.64f,.70f);RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.00032f;RenderSettings.fogColor=new Color(.69f,.78f,.82f);
        if(Object.FindObjectOfType<Camera>()==null)
        { var camera=new GameObject("AirportPreviewCamera").AddComponent<Camera>();camera.transform.position=new Vector3(-620,60,-410);camera.transform.LookAt(new Vector3(-450,0,-230));camera.farClipPlane=2500;camera.enabled=false; }
        var reference=GameObject.Find("AirportPreviewCamera").GetComponent<Camera>();reference.nearClipPlane=.02f;reference.farClipPlane=3000;descriptor.ReferenceCamera=reference.gameObject;descriptor.DrawDistance=3000;
        foreach(var p in world.GetComponentsInChildren<UdonSharpBehaviour>(true))UdonSharpEditorUtility.CopyProxyToUdon(p);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Root+"/Scenes/DriveRoadster.unity");AssetDatabase.SaveAssets();
        Debug.Log("AIRPORT_UPGRADE_COMPLETE: 32 shared-prefab vehicles, 14 ordered gates, 1800x1600m land");
    }

    static void EnsurePrograms()
    {
        foreach(string n in new[]{"RoadsterVehicle","RoadsterRun","RoadsterAction","RoadsterAudio","RoadsterMusic","RoadsterMusicButton"})
        {
            var source=AssetDatabase.LoadAssetAtPath<MonoScript>(Root+"/Scripts/"+n+".cs");
            if(source==null)throw new Exception("Missing script "+n);
            bool exists=AssetDatabase.FindAssets("t:UdonSharpProgramAsset").Any(g=>AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(g)).sourceCsScript==source);
            if(!exists){var program=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();program.sourceCsScript=source;program.ScriptVersion=UdonSharpProgramVersion.CurrentVersion;AssetDatabase.CreateAsset(program,Root+"/Scripts/"+n+".asset");}
        }
        AssetDatabase.SaveAssets();UdonSharpCompilerV1.CompileSync();
    }
    static void BuildLand()
    {
        Box("DistantAirfieldTerrain",environment,new Vector3(0,-5.5f,30),new Vector3(6000,2,6000),grass,false);
        Box("AirfieldLand_1800x1600",environment,new Vector3(0,-2.25f,30),new Vector3(1800,4,1600),grass,true);
        Box("Runway_18_36",environment,new Vector3(-280,-.14f,0),new Vector3(48,.18f,1220),asphalt,true);
        Box("Taxiway_Alpha",environment,new Vector3(-365,-.13f,0),new Vector3(22,.18f,1180),concrete,true);
        Box("PaddockApron",environment,new Vector3(-495,-.09f,-257),new Vector3(190,.14f,150),concrete,true);
        Box("PitExitTaxiway",environment,new Vector3(-370,-.07f,-291),new Vector3(220,.12f,14),concrete,true);
        Box("TerminalApron",environment,new Vector3(80,-.15f,240),new Vector3(260,.12f,180),concrete,true);
        Box("HangarApron",environment,new Vector3(540,-.15f,-140),new Vector3(220,.12f,520),concrete,true);
        for(int z=-570;z<590;z+=30)
        {
            Box("RunwayDash",environment,new Vector3(-280,.005f,z),new Vector3(.6f,.012f,15),white,false);
            foreach(int side in new[]{-1,1})
            {Box("RunwayEdgeLight",environment,new Vector3(-280+side*25,.15f,z),new Vector3(.32f,.3f,.32f),cyan,false);}
        }
        foreach(int side in new[]{-1,1})Box("RunwayEdge",environment,new Vector3(-280+side*22,.005f,0),new Vector3(.35f,.012f,1190),white,false);
        for(int i=-4;i<=4;i++)for(int end=-1;end<=1;end+=2)Box("ThresholdPianoKey",environment,new Vector3(-280+i*4,.008f,end*565),new Vector3(2,.014f,22),white,false);
        Label("Runway18",environment,"18",new Vector3(-280,.03f,-530),Quaternion.Euler(90,0,0),12,Color.white);
        Label("Runway36",environment,"36",new Vector3(-280,.03f,530),Quaternion.Euler(90,180,0),12,Color.white);
        // Taxiway center lines and apron seams.
        Box("TaxiCenter",environment,new Vector3(-365,.005f,0),new Vector3(.22f,.012f,1160),amber,false);
        for(int x=-580;x<=-410;x+=20)Box("ApronJoint",environment,new Vector3(x,-.012f,-257),new Vector3(.04f,.008f,150),navy,false);
        for(int z=-330;z<=-190;z+=20)Box("ApronJoint",environment,new Vector3(-495,-.012f,z),new Vector3(190,.008f,.04f),navy,false);
        for(int z=-650;z<=700;z+=45)
        {Box("PerimeterFenceWest",environment,new Vector3(-780,1.2f,z),new Vector3(.16f,2.7f,45),navy,false);Box("PerimeterFenceEast",environment,new Vector3(780,1.2f,z),new Vector3(.16f,2.7f,45),navy,false);}
    }
    public static void BuildPractice()
    {
        if(environment==null)environment=GameObject.Find("AirportEnvironment").transform;
        if(asphalt==null){asphalt=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/RunwayAsphalt.mat");white=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/MarkingWhite.mat");cyan=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/RouteCyan.mat");navy=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/AirportNavy.mat");}
        if(environment.Find("PracticeCeiling")!=null)return;
        var savedRoute=route.ToArray();var savedUps=ups.ToArray();
        route.Clear();ups.Clear();
        for(int i=0;i<=160;i++)
        {
            float t=i/160f;float roll=180*Mathf.Pow(Mathf.Sin(t*Mathf.PI),2);
            route.Add(new Vector3(-80,8*(1-Mathf.Cos(roll*Mathf.Deg2Rad))+.06f,-100+i));ups.Add(Quaternion.AngleAxis(roll,Vector3.forward)*Vector3.up);
        }
        Strip("PracticeCeiling",-6,6,0,asphalt,true,false);Strip("PracticeCeilingGuideL",-6,-5.8f,.035f,cyan,false,false);Strip("PracticeCeilingGuideR",5.8f,6,.035f,cyan,false,false);
        Box("PracticeCatchPad",environment,new Vector3(-80,-.13f,-20),new Vector3(52,.15f,195),asphalt,true);
        Label("CeilingPracticeLabel",environment,"CEILING LAB / FREE DRIVE",new Vector3(-80,3,-107),Quaternion.identity,1,white.color);
        route.Clear();ups.Clear();
        for(int i=0;i<=50;i++){route.Add(new Vector3(40,.06f,-100+i));ups.Add(Vector3.up);}
        for(int i=1;i<=125;i++){float a=i*Mathf.Deg2Rad;route.Add(new Vector3(40,30*(1-Mathf.Cos(a))+.06f,-50+30*Mathf.Sin(a)));ups.Add(new Vector3(0,Mathf.Cos(a),-Mathf.Sin(a)));}
        Strip("PracticeVerticalCurl",-4,4,0,asphalt,true,false);Strip("PracticeCurlGuideL",-4,-3.8f,.035f,cyan,false,false);Strip("PracticeCurlGuideR",3.8f,4,.035f,cyan,false,false);
        Box("PracticeCurlCatchPad",environment,new Vector3(40,-.13f,-35),new Vector3(65,.15f,175),asphalt,true);
        Label("CurlPracticeLabel",environment,"VERTICAL CURL / RESET TO RETURN",new Vector3(40,3,-107),Quaternion.identity,1,white.color);
        route.Clear();route.AddRange(savedRoute);ups.Clear();ups.AddRange(savedUps);
    }
    static void ConfigureImports()
    {
        foreach(string path in Directory.GetFiles(Root+"/Audio","*.wav"))
        {
            string p=path.Replace('\\','/');var importer=(AudioImporter)AssetImporter.GetAtPath(p);var settings=importer.defaultSampleSettings;
            settings.loadType=p.Contains("BGM") ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.7f;settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings=settings;importer.forceToMono=!p.Contains("BGM");importer.SaveAndReimport();
        }
        foreach(string path in Directory.GetFiles(Art,"*.png",SearchOption.AllDirectories))
        {
            string p=path.Replace('\\','/');var importer=(TextureImporter)AssetImporter.GetAtPath(p);if(importer==null)continue;
            importer.maxTextureSize=p.Contains("Livery_")?256:2048;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            if(p.EndsWith("normal.png"))importer.textureType=TextureImporterType.NormalMap;
            importer.sRGBTexture=!(p.Contains("metallic")||p.Contains("roughness")||p.EndsWith("normal.png"));
            importer.SaveAndReimport();
        }
    }
    static Vector3 Spline(float u)
    {
        int n=controls.Length,i=Mathf.FloorToInt(u);float t=u-i;i=(i%n+n)%n;
        var a=controls[(i+n-1)%n];var b=controls[i];var c=controls[(i+1)%n];var d=controls[(i+2)%n];
        return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
    }
    static bool Gap(int i){float u=(float)i/Steps;return u>10.12f && u<12.85f;}
    static void BuildRoute()
    {
        route.Clear();ups.Clear();gates.Clear();
        for(int i=0;i<=controls.Length*Steps;i++)
        {
            float u=(float)i/Steps;Vector3 p=Spline(u);p.y=Mathf.Max(0,p.y)+.06f;route.Add(p);
            Vector3 tangent=(Spline(u+.005f)-Spline(u-.005f)).normalized;
            float bank=u>2.5f && u<5.5f ? 58*Mathf.Pow(Mathf.Sin((u-2.5f)/3*Mathf.PI),2) : 0;
            ups.Add(Quaternion.AngleAxis(bank,tangent)*Vector3.up);
        }
        Strip("AirportCircuit",-10,10,0,asphalt,true,true);
        Strip("LeftEdge",-9.5f,-9.25f,.025f,white,false,true);Strip("RightEdge",9.25f,9.5f,.025f,white,false,true);
        Strip("LeftGuidance",-10.1f,-9.85f,.04f,cyan,false,true);Strip("RightGuidance",9.85f,10.1f,.04f,cyan,false,true);
        for(int i=0;i<route.Count-1;i+=8)
        {
            if(Gap(i))continue;
            Vector3 f=(route[Mathf.Min(i+1,route.Count-1)]-route[Mathf.Max(0,i-1)]).normalized;
            var q=Quaternion.LookRotation(f,ups[i]);
            if(i%24==0){var mark=Box("DirectionDash",environment,route[i]+ups[i]*.04f,new Vector3(.22f,.025f,4),cyan,false);mark.transform.rotation=q;}
            foreach(int side in new[]{-1,1})
            {var curb=Box("Curb",environment,route[i]+q*new Vector3(side*10.5f,.04f,0),new Vector3(.8f,.08f,3.5f),(i/8)%2==0 ? red:white,false);curb.transform.rotation=q;}
        }
        for(int k=0;k<gateSamples.Length;k++)
        {
            int i=gateSamples[k];Vector3 p=route[i];Vector3 f=(route[(i+1)%route.Count]-route[(i+route.Count-2)%(route.Count-1)]).normalized;
            var g=New("Gate_"+k.ToString("D2")+"_"+gateNames[k].Replace(" ","_"),environment,p+Vector3.up*(k==6||k==7 ? 0:2));g.rotation=Quaternion.LookRotation(f,Vector3.up);gates.Add(g);
            if(k==13)continue;
            bool air=k==6||k==7;float w=air ? 36:27;float h=air ? 30:9;
            var structure=New("Portal",g,Vector3.zero);structure.localRotation=Quaternion.identity;
            foreach(int side in new[]{-1,1})Box("PortalLeg",structure,new Vector3(side*w*.5f,air ? 0:h*.5f-2,0),new Vector3(.6f,h,.6f),navy,false);
            Box("PortalTop",structure,new Vector3(0,air ? h*.5f:h-2,0),new Vector3(w+1,2.1f,1),navy,false);
            string title=k==0 ? "START / FINISH" : gateNames[k];
            float yy=air ? h*.5f:h-2;
            Label("GateLabelFront",structure,title,new Vector3(0,yy,-.53f),Quaternion.identity,k==0 ? 3f:2.5f,Color.white);
            Label("GateLabelBack",structure,title,new Vector3(0,yy,.53f),Quaternion.Euler(0,180,0),k==0 ? 3f:2.5f,Color.white);
            if(air)Box("PortalBottom",structure,new Vector3(0,-h*.5f,0),new Vector3(w,.4f,.4f),cyan,false);
        }
        for(int x=0;x<20;x++)for(int z=0;z<2;z++)Box("StartChecker",environment,new Vector3(-289.5f+x,.07f,-260+z),new Vector3(1,.025f,1),(x+z)%2==0?white:navy,false);
        Label("StagingLabel",environment,"STOP + T",new Vector3(-280,.09f,-282),Quaternion.Euler(90,0,0),2.5f,amber.color);
        float length=0;for(int i=1;i<route.Count;i++)length+=Vector3.Distance(route[i-1],route[i]);
        File.WriteAllText("docs/validation/airport-route-length.txt",length.ToString("F1")+" metres; 14 gates; 1800 x 1600 metres land");
    }
    static void Strip(string name,float left,float right,float height,Material mat,bool collision,bool omitGap)
    {
        var verts=new Vector3[route.Count*2];var uv=new Vector2[verts.Length];var tris=new List<int>();
        for(int i=0;i<route.Count;i++)
        {
            var f=(route[Mathf.Min(i+1,route.Count-1)]-route[Mathf.Max(0,i-1)]).normalized;
            var r=Vector3.Cross(ups[i],f).normalized;verts[i*2]=route[i]+r*left+ups[i]*height;verts[i*2+1]=route[i]+r*right+ups[i]*height;
            uv[i*2]=new Vector2(0,i);uv[i*2+1]=new Vector2(1,i);
            if(i<route.Count-1 && (!omitGap || (!Gap(i)&&!Gap(i+1))))tris.AddRange(new[]{i*2,i*2+2,i*2+1,i*2+1,i*2+2,i*2+3});
        }
        var mesh=new Mesh{name=name};mesh.vertices=verts;mesh.uv=uv;mesh.triangles=tris.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Art+"/Meshes/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old!=null){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(environment,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;go.isStatic=true;
        if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
    public static void BuildBuildings()
    {
        if(environment==null)environment=GameObject.Find("AirportEnvironment").transform;
        var previous=environment.Find("MeshyBuildings");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
        var root=New("MeshyBuildings",environment,Vector3.zero);
        PlaceBuilding(root,"ControlTower",new Vector3(-430,-.18f,210),new Vector3(34,50,34),0);
        PlaceBuilding(root,"Terminal",new Vector3(80,-.18f,205),new Vector3(190,26,80),180);
        PlaceBuilding(root,"Terminal",new Vector3(-585,-.18f,-70),new Vector3(130,19,58),90);
        for(int i=0;i<6;i++)PlaceBuilding(root,"Hangar",new Vector3(600,-.18f,-360+i*115),new Vector3(95,28,75),-90);
        for(int i=0;i<3;i++)PlaceBuilding(root,"Hangar",new Vector3(-580,-.18f,170+i*130),new Vector3(85,25,65),90);
    }
    static void PlaceBuilding(Transform parent,string name,Vector3 position,Vector3 size,float yaw)
    {
        string path=Art+"/Meshy/"+name+"/"+name+".fbx";
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(model==null){Debug.LogWarning("Awaiting Meshy "+path);return;}
        var container=New(name,parent,position);container.rotation=Quaternion.Euler(0,yaw,0);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(model,container);var rends=instance.GetComponentsInChildren<Renderer>();
        if(rends.Length==0)return;Bounds b=rends[0].bounds;foreach(var r in rends)b.Encapsulate(r.bounds);
        float scale=Mathf.Min(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);instance.transform.localScale*=scale;
        b=rends[0].bounds;foreach(var r in rends)b.Encapsulate(r.bounds);
        instance.transform.position+=new Vector3(position.x-b.center.x,position.y-b.min.y,position.z-b.center.z);
        string tex=Art+"/Meshy/"+name+"/"+name+"_textures/";
        Material material=Mat("Meshy_"+name,Color.white);material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"base_color.png");
        material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"normal.png"));material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"unity_metallic_smoothness.png"));material.EnableKeyword("_METALLICGLOSSMAP");EditorUtility.SetDirty(material);
        foreach(var r in rends){r.sharedMaterials=r.sharedMaterials.Select(m=>material).ToArray();r.gameObject.isStatic=true;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;}
        var col=container.gameObject.AddComponent<BoxCollider>();col.center=container.InverseTransformPoint(b.center+new Vector3(position.x-b.center.x,position.y-b.min.y,position.z-b.center.z));col.size=new Vector3(size.x*.78f,Mathf.Min(size.y,b.size.y),size.z*.78f);
        var lod=container.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.012f,rends)});lod.RecalculateBounds();
    }
    static void PrepareVehicle(RoadsterVehicle v)
    {
        v.station.stationEnterPlayerLocation.localPosition=new Vector3(-.395f,.30f,-.12f);
        v.cockpitEye.localPosition=new Vector3(-.395f,.98f,-.08f);
        v.run.display=null;v.run.cockpitDisplay=null;
        v.cockpitPanels=new[]{"DriverInstruments","FlightSystems","DriverControls"}.Select(n=>v.transform.Find(n)).Where(t=>t!=null).Select(t=>t.gameObject).ToArray();
        foreach(var ps in v.GetComponentsInChildren<ParticleSystem>(true))Object.DestroyImmediate(ps.gameObject);
        Transform model=v.transform.Cast<Transform>().First(t=>t.name.Contains("LOD0"));
        RoadsterThrusterSetup.Build(v,model);
        Material glass=Mat("CanopyCopper",new Color(.23f,.13f,.07f,.16f),"DriveRoadster/CanopyGlassTwoSided");
        foreach(var r in v.GetComponentsInChildren<MeshRenderer>(true))
        {var ms=r.sharedMaterials;for(int k=0;k<ms.Length;k++)if(ms[k]!=null && ms[k].name.Contains("CopperSmokedGlass"))ms[k]=glass;r.sharedMaterials=ms;}
        if(v.transform.Find("InteriorRoofLiner")==null)
        {var liner=Box("InteriorRoofLiner",v.transform,new Vector3(0,1.165f,-.22f),new Vector3(1.16f,.018f,.77f),Mat("RoofLiner",new Color(.055f,.065f,.075f)),false);liner.layer=17;}
        var audio=v.GetComponent<RoadsterAudio>();if(audio==null)audio=v.gameObject.AddUdonSharpComponent<RoadsterAudio>();audio.vehicle=v;
        audio.motor=Sound(v.transform,"EV Motor","EV_Motor_Loop",true,1.7f,85);audio.rolling=Sound(v.transform,"Road and wind","Tyre_Wind_Loop",true,1.5f,70);
        audio.gas=Sound(v.transform,"Cold gas thrust","Cold_Gas_Loop",true,3,110);audio.impact=Sound(v.transform,"Touchdown","Touchdown",false,2,70);audio.impact.volume=.4f;
        var group=v.GetComponent<LODGroup>();var lods=group.GetLODs();lods[0].screenRelativeTransitionHeight=.28f;lods[1].screenRelativeTransitionHeight=.09f;lods[2].screenRelativeTransitionHeight=.009f;group.SetLODs(lods);
        if(v.transform.Find("FleetBadge_Hood")==null)
        {
            var badge=GameObject.CreatePrimitive(PrimitiveType.Quad);badge.name="FleetBadge_Hood";badge.transform.SetParent(v.transform,false);badge.transform.localPosition=new Vector3(.38f,.683f,1.35f);badge.transform.localRotation=Quaternion.Euler(90,0,0);badge.transform.localScale=new Vector3(.32f,.32f,1);Object.DestroyImmediate(badge.GetComponent<Collider>());badge.layer=17;
            foreach(int side in new[]{-1,1})
            {var sideBadge=Object.Instantiate(badge,v.transform);sideBadge.name="FleetBadge_Side_"+side;sideBadge.transform.localPosition=new Vector3(side*1.025f,.61f,-.15f);sideBadge.transform.localRotation=Quaternion.Euler(0,side>0 ? -90:90,0);sideBadge.transform.localScale=new Vector3(.38f,.24f,1);}
        }
    }
    static void ApplyLivery(GameObject car,int number)
    {
        Material mat=Mat("Livery_"+number.ToString("D2"),Color.white);
        mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/Livery_"+number.ToString("D2")+".png");mat.SetFloat("_Glossiness",.45f);EditorUtility.SetDirty(mat);
        foreach(var r in car.GetComponentsInChildren<Renderer>(true))if(r.name.StartsWith("FleetBadge_"))r.sharedMaterial=mat;
    }
    static AudioSource Sound(Transform parent,string name,string clip,bool loop,float min,float max)
    {
        var old=parent.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);
        var source=New(name,parent,new Vector3(0,.5f,-1)).gameObject.AddComponent<AudioSource>();source.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/"+clip+".wav");
        source.loop=loop;source.playOnAwake=false;source.spatialBlend=1;source.minDistance=min;source.maxDistance=max;source.rolloffMode=AudioRolloffMode.Linear;source.dopplerLevel=.25f;source.volume=0;source.priority=120;return source;
    }
    static void BuildEntrance(Transform world)
    {
        var root=New("Entrance",world,Vector3.zero);
        Sign(root,"Controls_JA",new Vector3(-560,3.2f,-315),new Vector2(7.2f,5.4f),"Entrance_Controls_JA.png");
        Sign(root,"CourseMap",new Vector3(-550,3.2f,-315),new Vector2(5.4f,5.4f),"Course_Map.png");
        foreach(float x in new[]{-563,-557,-552,-548})Box("SignPost",root,new Vector3(x,1.8f,-314.8f),new Vector3(.18f,3.6f,.18f),navy,true);
        var music=New("AirportBGM",world,Vector3.zero).gameObject.AddUdonSharpComponent<RoadsterMusic>();
        var source=music.gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.volume=0;source.priority=200;
        music.source=source;music.playlist=Enumerable.Range(1,3).Select(i=>AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/Airport_BGM_"+i.ToString("D2")+".wav")).ToArray();
        var panel=New("BGMPanel",root,new Vector3(-540,1.6f,-315));
        Box("RadioHousing",panel,Vector3.zero,new Vector3(4,2.6f,.28f),navy,true);
        var canvas=new GameObject("RadioDisplay",typeof(RectTransform),typeof(Canvas));canvas.transform.SetParent(panel,false);canvas.transform.localPosition=new Vector3(0,.6f,-.16f);canvas.transform.localScale=Vector3.one*.005f;canvas.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        var txt=new GameObject("Status",typeof(RectTransform),typeof(Text)).GetComponent<Text>();txt.transform.SetParent(canvas.transform,false);txt.rectTransform.sizeDelta=new Vector2(700,160);txt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");txt.fontSize=36;txt.alignment=TextAnchor.MiddleCenter;txt.color=Color.white;music.display=txt;
        string[] names={"ON / OFF","NEXT","VOL -","VOL +"};
        for(int i=0;i<4;i++)
        {var b=Box(names[i],panel,new Vector3(-1.4f+i*.94f,-.45f,-.22f),new Vector3(.8f,.62f,.18f),i==0?amber:concrete,true);b.layer=8;var button=b.AddUdonSharpComponent<RoadsterMusicButton>();button.music=music;button.action=i;button.InteractionText=names[i];Label("Label",panel,names[i],new Vector3(-1.4f+i*.94f,-.45f,-.32f),Quaternion.identity,.12f,Color.white);}
        Label("EntranceTitle",root,"PADDOCK  /  32",new Vector3(-500,6,-333),Quaternion.identity,1.3f,Color.white);
        Box("EntranceHeader",root,new Vector3(-500,6,-332),new Vector3(26,2,1),navy,false);
    }
    static void Sign(Transform parent,string name,Vector3 position,Vector2 size,string texture)
    {
        Box(name+"Backing",parent,position+Vector3.forward*.10f,new Vector3(size.x+.15f,size.y+.15f,.18f),navy,true);
        var q=GameObject.CreatePrimitive(PrimitiveType.Quad);q.name=name;q.transform.SetParent(parent,false);q.transform.localPosition=position;q.transform.localScale=new Vector3(size.x,size.y,1);Object.DestroyImmediate(q.GetComponent<Collider>());
        var mat=Mat(name,Color.white,"Unlit/Texture");mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/"+texture);q.GetComponent<Renderer>().sharedMaterial=mat;
    }
    static Material Mat(string name,Color color,string shader="Standard")
    {
        string path=Art+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find(shader));mat.name=name;AssetDatabase.CreateAsset(mat,path);}mat.color=color;EditorUtility.SetDirty(mat);return mat;
    }
    static Transform New(string name,Transform parent,Vector3 local)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=local;return t;}
    static GameObject Box(string name,Transform parent,Vector3 local,Vector3 size,Material material,bool collision)
    {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=local;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;if(!collision)Object.DestroyImmediate(g.GetComponent<Collider>());g.isStatic=parent==environment;return g;}
    static void Label(string name,Transform parent,string text,Vector3 local,Quaternion rotation,float size,Color color)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=local;go.transform.localRotation=rotation;var tm=go.AddComponent<TextMesh>();tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var material=Mat("WorldFont",Color.white,"DriveRoadster/WorldText");material.mainTexture=tm.font.material.mainTexture;tm.GetComponent<Renderer>().sharedMaterial=material;tm.text=text;tm.fontSize=64;tm.characterSize=size*.1f;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=color;}
}
#endif
