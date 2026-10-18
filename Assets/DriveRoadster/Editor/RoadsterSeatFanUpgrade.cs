#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharp;
using UdonSharpEditor;

public static class RoadsterSeatFanUpgrade
{
    const string Root="Assets/DriveRoadster";
    public static void Configure(RoadsterVehicle v)
    {
        v.cockpitEye.localPosition=new Vector3(-.395f,1.08f,-.08f);
        v.viewHeightStep=.02f;v.minViewHeightOffset=-.12f;v.maxViewHeightOffset=.04f;
        v.fanDownforceG=2f;v.fanContactExtension=.22f;
        RoadsterCockpitSetup.AddViewControls(v);
        var audio=v.GetComponent<RoadsterAudio>();
        var t=v.transform.Find("Surface suction fan");
        if(t==null){t=new GameObject("Surface suction fan").transform;t.SetParent(v.transform,false);t.localPosition=new Vector3(0,.25f,-.6f);}
        var source=t.GetComponent<AudioSource>();if(source==null)source=t.gameObject.AddComponent<AudioSource>();
        source.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/Surface_Fan_Loop.wav");
        source.loop=true;source.playOnAwake=false;source.spatialBlend=1;source.minDistance=2;source.maxDistance=65;
        source.rolloffMode=AudioRolloffMode.Linear;source.dopplerLevel=.1f;source.volume=0;source.priority=130;
        if(audio!=null)audio.fan=source;
    }
    [MenuItem("Drive Roadster/Update cockpit height and surface fans")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Stop Play Mode first");
        string path=Root+"/Prefabs/Asterion_Shared.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try
        {
            Configure(prefab.GetComponent<RoadsterVehicle>());
            foreach(var p in prefab.GetComponentsInChildren<UdonSharpBehaviour>(true))UdonSharpEditorUtility.CopyProxyToUdon(p);
            PrefabUtility.SaveAsPrefabAsset(prefab,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
        // Preserve each instance's fleet livery, bay, station and timing references.
        foreach(var v in Object.FindObjectsOfType<RoadsterVehicle>(true))
        {
            Configure(v);
            foreach(var p in v.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {UdonSharpEditorUtility.CopyProxyToUdon(p);EditorUtility.SetDirty(p);PrefabUtility.RecordPrefabInstancePropertyModifications(p);}
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
#endif
