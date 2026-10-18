#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Generated once per scene. No Instantiate/Destroy, collision module, or particle network traffic at runtime.
public static class RoadsterThrusterSetup
{
    const string DirectoryPath = "Assets/DriveRoadster/Art/GeneratedMaterials";
    public static void Build(RoadsterVehicle vehicle, Transform model)
    {
        Material material = CreateSmokeMaterial();
        string[] names = { "Thruster_Down_FL", "Thruster_Down_FR", "Thruster_Down_L", "Thruster_Down_R", "Thruster_Front_L", "Thruster_Front_R", "Thruster_Rear" };
        vehicle.thrusterJets = new ParticleSystem[7];
        vehicle.groundMist = new ParticleSystem[4];
        vehicle.thrusterSmoke = new ParticleSystem[7];
        for (int i = 0; i < names.Length; i++)
        {
            Transform anchor = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == names[i]) { anchor = t; break; }
            if (anchor == null) throw new System.IO.InvalidDataException("Missing nozzle anchor: " + names[i]);
            Vector3 direction = i < 4 ? Vector3.down : i == 4 ? Vector3.left : i == 5 ? Vector3.right : Vector3.back;
            var jet = CreateSystem(names[i] + "_Jet", vehicle.transform, material, false);
            jet.transform.localPosition = vehicle.transform.InverseTransformPoint(anchor.position);
            jet.transform.localRotation = Quaternion.LookRotation(direction);
            vehicle.thrusterJets[i] = jet;
            var smoke = CreateSystem(names[i] + "_ResidualSmoke", vehicle.transform, material, false);
            smoke.transform.localPosition = jet.transform.localPosition; smoke.transform.localRotation = jet.transform.localRotation;
            var smokeMain = smoke.main; smokeMain.simulationSpace = ParticleSystemSimulationSpace.World;
            smokeMain.maxParticles = 192;
            smokeMain.startLifetime = new ParticleSystem.MinMaxCurve(1.8f,3.5f);
            smokeMain.startSpeed = new ParticleSystem.MinMaxCurve(4f,8f);
            smokeMain.startSize = new ParticleSystem.MinMaxCurve(.65f,1.15f);
            smokeMain.startColor = new Color(.95f,.97f,1f,.32f);
            var smokeShape = smoke.shape; smokeShape.angle = 12f;
            var smokeLimit = smoke.limitVelocityOverLifetime; smokeLimit.limit = .8f; smokeLimit.dampen = .04f;
            var smokeNoise = smoke.noise; smokeNoise.strength = .4f;
            smoke.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            vehicle.thrusterSmoke[i] = smoke;
            if (i < 4) vehicle.groundMist[i] = CreateSystem(names[i] + "_GroundMist", vehicle.transform, material, true);
        }
    }

    static ParticleSystem CreateSystem(string name, Transform parent, Material material, bool mist)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = 17;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true; main.simulationSpace = mist ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.maxParticles = mist ? 192 : 64;
        main.startLifetime = new ParticleSystem.MinMaxCurve(mist ? 2.2f : .18f, mist ? 4f : .3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(mist ? 1.6f : 14f, mist ? 3.2f : 22f);
        main.startSize = new ParticleSystem.MinMaxCurve(mist ? 1.1f : .045f, mist ? 1.8f : .07f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = mist ? new Color(.95f,.97f,1f,.34f) : new Color(.88f,.95f,1f,.72f);
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = mist ? 84f : 3.5f; shape.radius = mist ? .22f : .025f;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0,1,1,mist ? 4f : 5f));
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
            new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.08f), new GradientAlphaKey(.65f,.6f), new GradientAlphaKey(0,1) });
        colour.color = gradient;
        var noise = ps.noise; noise.enabled = true; noise.strength = mist ? .3f : .12f;
        noise.frequency = .65f; noise.scrollSpeed = .5f; noise.quality = ParticleSystemNoiseQuality.Low;
        var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = mist ? 2f : 8f; limit.dampen = mist ? .06f : .015f;
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = mist ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = .07f; renderer.lengthScale = 3f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        return ps;
    }

    static Material CreateSmokeMaterial()
    {
        Directory.CreateDirectory(DirectoryPath);
        string texturePath = DirectoryPath + "/ColdGasPuff.png";
        if (!File.Exists(texturePath))
        {
            // Original procedural radial opacity field, no external imagery.
            var texture = new Texture2D(64,64,TextureFormat.RGBA32,false);
            for (int y=0;y<64;y++) for (int x=0;x<64;x++)
            {
                Vector2 uv = new Vector2((x-31.5f)/31.5f,(y-31.5f)/31.5f);
                float alpha = Mathf.Pow(Mathf.Clamp01(1-uv.sqrMagnitude),2);
                alpha *= .65f + .35f * Mathf.PerlinNoise(x*.15f,y*.15f);
                texture.SetPixel(x,y,new Color(1,1,1,alpha));
            }
            texture.Apply(); File.WriteAllBytes(texturePath, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
        }
        string materialPath = DirectoryPath + "/ColdGasWhite.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            material.name = "ColdGasWhite"; material.SetColor("_TintColor",new Color(.8f,.8f,.8f,.8f));
            AssetDatabase.CreateAsset(material,materialPath);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        EditorUtility.SetDirty(material);
        return material;
    }
}
#endif
