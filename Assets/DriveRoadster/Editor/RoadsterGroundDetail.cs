#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

public static class RoadsterGroundDetail
{
    const string Art="Assets/DriveRoadster/Art/Airport";
    const string Surface=Art+"/Surfaces";
    const float PatchSize=20f;
    [MenuItem("Drive Roadster/Apply textured ground and light grass")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode first");
        foreach(string name in new[]{"Grass","Asphalt"})
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(Surface+"/Airfield_"+name+"_Seamless.png");
            importer.maxTextureSize=1024;importer.mipmapEnabled=true;importer.streamingMipmaps=true;importer.anisoLevel=4;
            importer.wrapMode=TextureWrapMode.Mirror;importer.filterMode=FilterMode.Trilinear;importer.isReadable=false;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.sRGBTexture=true;importer.SaveAndReimport();
        }
        Material ground=SurfaceMaterial("AirfieldGrass","Grass",new Color(.82f,.88f,.69f),3.5f,.12f,.04f);
        SurfaceMaterial("RunwayAsphalt","Asphalt",new Color(.61f,.68f,.73f),5f,.06f,.10f);
        SurfaceMaterial("ApronConcrete","Asphalt",new Color(1.0f,1.05f,1.04f),7f,.04f,.05f);
        var env=GameObject.Find("AirportEnvironment").transform;
        foreach(string name in new[]{"AirfieldLand_1800x1600","DistantAirfieldTerrain"})env.Find(name).GetComponent<Renderer>().sharedMaterial=ground;
        var old=env.Find("LightGrass");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var root=new GameObject("LightGrass").transform;root.SetParent(env,false);
        var shader=Shader.Find("DriveRoadster/AirfieldTufts");if(shader==null)throw new InvalidOperationException("Grass shader not compiled");
        string matPath=Surface+"/GrassTufts.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,matPath);}mat.shader=shader;mat.color=new Color(.45f,.55f,.34f);mat.enableInstancing=true;EditorUtility.SetDirty(mat);
        var dense=MakePatch("GrassPatch_Dense",150);var sparse=MakePatch("GrassPatch_Sparse",30);
        var centres=new HashSet<Vector2Int>();var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/Meshes/AirportCircuit.asset");var v=mesh.vertices;
        for(int i=0;i<v.Length/2-1;i+=5)
        {
            var p=(v[2*i]+v[2*i+1])*.5f;var right=Vector3.ProjectOnPlane(v[2*i+1]-v[2*i],Vector3.up).normalized;
            foreach(int side in new[]{-1,1})foreach(float offset in new[]{25f,48f})
            {var c=p+right*side*offset;centres.Add(new Vector2Int(Mathf.RoundToInt(c.x/PatchSize),Mathf.RoundToInt(c.z/PatchSize)));}
        }
        for(int x=-620;x<=-380;x+=20)for(int z=-390;z<=-160;z+=20)centres.Add(new Vector2Int(Mathf.RoundToInt(x/PatchSize),Mathf.RoundToInt(z/PatchSize)));
        Physics.SyncTransforms();int count=0;
        foreach(var tile in centres.OrderBy(p=>p.x).ThenBy(p=>p.y))
        {
            var p=new Vector3(tile.x*PatchSize,0,tile.y*PatchSize);bool clear=true;float y=0;
            // Check the entire patch: no tufts on road, aprons, buildings or practice structures.
            foreach(float x in new[]{-9.8f,-5f,0f,5f,9.8f})foreach(float z in new[]{-9.8f,-5f,0f,5f,9.8f})
            {RaycastHit h;if(!Physics.Raycast(p+new Vector3(x,120,z),Vector3.down,out h,150,1,QueryTriggerInteraction.Ignore)||h.collider.name!="AirfieldLand_1800x1600"){clear=false;break;}y=h.point.y;}
            if(!clear)continue;
            var patch=new GameObject("GrassPatch_"+count.ToString("D3"));patch.transform.SetParent(root,false);patch.transform.position=new Vector3(p.x,y+.008f,p.z);
            patch.transform.rotation=Quaternion.Euler(0,((tile.x*17+tile.y*31)&3)*90,0);
            var high=PatchRenderer(patch.transform,"Near",dense,mat);var low=PatchRenderer(patch.transform,"Far",sparse,mat);
            var lod=patch.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.26f,new Renderer[]{high}),new LOD(.14f,new Renderer[]{low})});lod.RecalculateBounds();count++;
        }
        AssetDatabase.SaveAssets();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("docs/validation");File.WriteAllText("docs/validation/airport-ground.json","{\"patches\":"+count+",\"sharedMeshes\":2,\"trianglesPerNearPatch\":"+(dense.triangles.Length/3)+",\"trianglesPerFarPatch\":"+(sparse.triangles.Length/3)+",\"grassColliders\":0,\"grassUpdateScripts\":0,\"textureMaxSize\":1024,\"gpuInstancing\":true,\"shadows\":false}");
        Debug.Log("AIRFIELD_GROUND_COMPLETE: "+count+" culled grass patches, two shared meshes, no grass scripts/colliders");
    }
    static Material SurfaceMaterial(string name,string texture,Color tint,float meters,float variation,float gloss)
    {
        var m=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/"+name+".mat");m.shader=Shader.Find("DriveRoadster/AirfieldSurface");
        m.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Surface+"/Airfield_"+texture+"_Seamless.png");m.color=tint;m.SetFloat("_TileMeters",meters);m.SetFloat("_Variation",variation);m.SetFloat("_Glossiness",gloss);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    static MeshRenderer PatchRenderer(Transform parent,string name,Mesh mesh,Material mat)
    {
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);g.GetComponent<MeshFilter>().sharedMesh=mesh;
        var r=g.GetComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;return r;
    }
    static Mesh MakePatch(string name,int count)
    {
        var rng=new System.Random(826);var verts=new List<Vector3>();var cols=new List<Color>();var uv=new List<Vector2>();var tris=new List<int>();
        for(int i=0;i<count;i++)
        {
            var origin=new Vector3((float)rng.NextDouble()*19.4f-9.7f,0,(float)rng.NextDouble()*19.4f-9.7f);
            var color=Color.Lerp(new Color(.19f,.28f,.08f),new Color(.45f,.49f,.20f),(float)rng.NextDouble());
            for(int blade=0;blade<4;blade++)
            {
                float angle=(float)rng.NextDouble()*Mathf.PI*2;var right=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                float height=.22f+(float)rng.NextDouble()*.38f,width=.025f+(float)rng.NextDouble()*.025f;var bend=right*height*.3f;
                int n=verts.Count;verts.Add(origin-right*width);verts.Add(origin+right*width);verts.Add(origin+Vector3.up*height*.55f+bend*.45f+right*width*.5f);verts.Add(origin+Vector3.up*height+bend);
                uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(new Vector2(1,.55f));uv.Add(Vector2.up);
                cols.Add(color*.65f);cols.Add(color*.65f);cols.Add(color);cols.Add(color*1.14f);
                tris.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
            }
        }
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Surface+"/"+name+".asset");if(mesh==null){mesh=new Mesh{name=name};AssetDatabase.CreateAsset(mesh,Surface+"/"+name+".asset");}
        mesh.Clear();mesh.SetVertices(verts);mesh.SetColors(cols);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(.2f);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);return mesh;
    }
}
#endif
