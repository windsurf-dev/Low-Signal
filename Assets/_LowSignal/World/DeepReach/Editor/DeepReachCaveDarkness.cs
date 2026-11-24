using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Per-material cave lighting: exterior lights, environment and player abilities are unchanged.
public static class DeepReachCaveDarkness
{
    const string Root="Assets/_LowSignal/World/DeepReach";
    static float Average(Texture2D image)=>image.GetPixels().Average(p=>p.grayscale);
    static Texture2D Capture(Camera camera,string name) {
        var rt=RenderTexture.GetTemporary(1280,720,24);var old=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
        Directory.CreateDirectory("Previews/DeepReach");File.WriteAllBytes("Previews/DeepReach/"+name+".png",tex.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);return tex;
    }
    [MenuItem("Low Signal/Level 2/Darken only the cave")]
    public static void Apply() {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/_LowSignal/Scenes/DeepReach.unity");if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/DeepReach.unity");
        foreach(var old in scene.GetRootGameObjects().Where(g=>g.name=="Temporary cave darkness validation").ToArray())UnityEngine.Object.DestroyImmediate(old);
        var cave=scene.GetRootGameObjects().Single(g=>g.name=="01 - Winding entrance cave").transform;
        var route=cave.Find("Descent route markers/Main passage");if(!route)throw new Exception("Cave descent route missing");
        void AimInside(Camera cam){cam.transform.position=route.GetChild(340).position;var forward=(route.GetChild(370).position-cam.transform.position).normalized;var side=Vector3.Cross(Vector3.up,forward).normalized;cam.transform.LookAt(cam.transform.position+forward*4+side*5);}
        var lighting=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>()).Select(l=>(l,l.color,l.intensity,l.enabled)).ToArray();
        var ambient=RenderSettings.ambientSkyColor;var fog=RenderSettings.fogColor;float density=RenderSettings.fogDensity;
        var cameraObject=new GameObject("Temporary cave darkness validation");var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=76;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;camera.farClipPlane=400;
        var shader=Shader.Find("Low Signal/Deep Reach/Cave Darkness");if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Cave shader failed to compile");
        AimInside(camera);
        var before=Capture(camera,"Cave darkness before");
        camera.transform.position=new Vector3(65,30,231);camera.transform.LookAt(new Vector3(65,-2,290));var basinBefore=Capture(camera,"Basin lighting before");
        Directory.CreateDirectory(Root+"/CaveLighting");AssetDatabase.Refresh();
        var replacements=new Dictionary<Material,Material>();
        foreach(var renderer in cave.GetComponentsInChildren<MeshRenderer>()) {
            renderer.shadowCastingMode=ShadowCastingMode.TwoSided;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>{
                if(replacements.TryGetValue(source,out var found))return found;
                string name=source.name.Replace("Cave dark - ","");string path=Root+"/CaveLighting/Cave dark - "+name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
                var canonical=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat")??AssetDatabase.LoadAssetAtPath<Material>(Root+"/RockFormations/"+name+".mat");
                var color=name=="Basalt"?new Color(.28f,.29f,.27f):(canonical?canonical:source).GetColor("_BaseColor");material.shader=shader;material.SetColor("_BaseColor",color);material.SetFloat("_InteriorAmbient",.04f);material.SetFloat("_InteriorFill",name=="Basalt"?.06f:.065f);
                material.SetFloat("_ExitZ",cave.TransformPoint(new Vector3(34,0,220)).z);material.SetFloat("_TransitionLength",18);material.SetColor("_CaveFogColor",new Color(.002f,.004f,.006f));material.SetFloat("_Cull",0);
                EditorUtility.SetDirty(material);replacements[source]=material;return material;
            }).ToArray();
        }
        AimInside(camera);
        var dark=Capture(camera,"Dark cave - flashlight off");
        // Match the existing LIGHT ability settings for the inspection beam.
        var flashlight=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Flashlight>()).Single();var settings=new SerializedObject(flashlight);
        var beam=cameraObject.AddComponent<Light>();beam.type=LightType.Spot;beam.color=settings.FindProperty("beamColor").colorValue;beam.intensity=settings.FindProperty("beamIntensity").floatValue;
        beam.range=settings.FindProperty("beamRange").floatValue;beam.spotAngle=settings.FindProperty("beamAngle").floatValue;beam.innerSpotAngle=settings.FindProperty("innerBeamAngle").floatValue;beam.shadows=LightShadows.Soft;
        var lit=Capture(camera,"Dark cave - flashlight on");beam.enabled=false;
        camera.transform.position=new Vector3(65,30,231);camera.transform.LookAt(new Vector3(65,-2,290));var basinAfter=Capture(camera,"Basin lighting after");
        float caveMean=Average(dark),litMean=Average(lit);float delta=basinBefore.GetPixels().Zip(basinAfter.GetPixels(),(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Average()/3;
        if(caveMean<.003f||caveMean>.065f)throw new Exception("Cave visibility outside dim readable range: "+caveMean);
        if(litMean<=caveMean+.001f)throw new Exception("Flashlight does not illuminate cave: dark="+caveMean+", lit="+litMean);
        if(delta>.002f)throw new Exception("Exterior lighting changed: "+delta);
        if(RenderSettings.ambientSkyColor!=ambient||RenderSettings.fogColor!=fog||RenderSettings.fogDensity!=density||lighting.Any(s=>s.l.color!=s.color||s.l.intensity!=s.intensity||s.l.enabled!=s.enabled))throw new Exception("Scene lighting changed");
        UnityEngine.Object.DestroyImmediate(cameraObject);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("CAVE_DARKNESS_PASS: cave-only ambient reduction, surface light suppression and 18m exit blend; flashlight visibility verified. Before "+Average(before).ToString("F4")+", dark "+caveMean.ToString("F4")+", beam "+litMean.ToString("F4")+", exterior pixel difference "+delta.ToString("F6"));
        foreach(var tex in new[]{before,dark,lit,basinBefore,basinAfter})UnityEngine.Object.DestroyImmediate(tex);
    }
}
