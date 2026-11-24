using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Authoring only: all geometry and collision are saved as editable scene objects.
public static class DeepReachBuilder
{
    const string Root = "Assets/_LowSignal/World/DeepReach";
    const string ScenePath = "Assets/_LowSignal/Scenes/DeepReach.unity";
    static readonly Vector3[] Route = {
        new Vector3(0,7,0), new Vector3(0,7,24), new Vector3(16,6,47),
        new Vector3(36,5,64), new Vector3(38,4,88), new Vector3(18,3,112),
        new Vector3(14,2,137), new Vector3(32,2,162), new Vector3(34,2,188),
        new Vector3(34,2,212) };
    static Material rock, silt, glow;
    public static void BuildBatch() { try { Build(); EditorApplication.Exit(0); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
    [MenuItem("Low Signal/Level 2/Validate Deep Reach and capture previews")]
    static void Finish()
    {
        if(SceneManager.GetSceneByPath(ScenePath).isLoaded) throw new Exception("Close DeepReach before running the saved-scene validation.");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/WindingCave.asset");
        foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Root}))
        { var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)); m.SetFloat("_Cull",0); EditorUtility.SetDirty(m); }
        AssetDatabase.SaveAssets();
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive); SceneManager.SetActiveScene(scene);
        try
        {
            var shell=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>()).Single();
            shell.sharedMesh=null; shell.sharedMesh=mesh; Physics.SyncTransforms();
            DeepReachCaveRevision.ValidateRoutes(scene);
            Directory.CreateDirectory("Previews/DeepReach");
            var cam=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).First();
            Capture(cam,"Cave start");
            cam.transform.position=new Vector3(34,3,195); cam.transform.rotation=Quaternion.Euler(8,0,0);
            Capture(cam,"Seafloor reveal");
            Debug.Log("DEEP_REACH_CLEARANCE_PASS: 2.2m diameter swept along entire cave; previews captured.");
        }
        finally { EditorSceneManager.CloseScene(scene,true); if(previous.IsValid())SceneManager.SetActiveScene(previous); }
    }
    static void Capture(Camera cam,string name)
    {
        var rt=RenderTexture.GetTemporary(1280,720,24); var old=RenderTexture.active;
        cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt;
        var tex=new Texture2D(1280,720,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,1280,720),0,0); tex.Apply();
        File.WriteAllBytes("Previews/DeepReach/"+name+".png",tex.EncodeToPNG());
        cam.targetTexture=null; RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(tex);
    }
    [MenuItem("Low Signal/Level 2/Create Deep Reach blockout")]
    public static void Build()
    {
        if (File.Exists(ScenePath)) throw new Exception("DeepReach already exists. Edit the saved scene rather than overwriting it.");
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            rock = Mat("Basalt", new Color(.105f,.15f,.17f));
            silt = Mat("Deep silt", new Color(.18f,.23f,.24f));
            glow = Mat("Bioluminescence", new Color(.08f,.55f,.57f), true);
            var cave = Group("01 - Winding entrance cave");
            var basin = Group("02 - Open seafloor basin");
            var details = Group("03 - Rock silhouettes and guide lights");
            var markers = Group("04 - Layout route markers (editor only)");
            // A continuous elliptical tube: floor, walls and roof meet without seams.
            const int sides = 20;
            var vertices = new Vector3[Route.Length*sides];
            for(int i=0;i<Route.Length;i++)
            {
                Vector3 direction = i==0 ? Route[1]-Route[0] : i==Route.Length-1 ? Route[i]-Route[i-1] : Route[i+1]-Route[i-1];
                Vector3 right = Vector3.Cross(Vector3.up, direction.normalized).normalized;
                float width = i>=7 ? Mathf.Lerp(8,16,(i-7)/2f) : 7.5f + Mathf.Sin(i*1.7f);
                float height = i>=7 ? Mathf.Lerp(6,12,(i-7)/2f) : 6;
                for(int j=0;j<sides;j++)
                {
                    float a=j*Mathf.PI*2/sides;
                    float rough=1+.045f*Mathf.Sin(i*4.7f+j*2.3f);
                    vertices[i*sides+j]=Route[i]+right*(Mathf.Cos(a)*width*rough)+Vector3.up*(Mathf.Sin(a)*height);
                }
                var marker=Group(i==0 ? "START - face into cave" : i==Route.Length-1 ? "REVEAL - open basin" : "Bend "+i,markers);
                marker.position=Route[i]; marker.gameObject.tag="EditorOnly";
            }
            var triangles=new int[(Route.Length-1)*sides*6]; int k=0;
            for(int i=0;i<Route.Length-1;i++) for(int j=0;j<sides;j++)
            {
                int a=i*sides+j,b=i*sides+(j+1)%sides,c=a+sides,d=b+sides;
                // Inward-facing normals keep the interior lit correctly.
                foreach(int n in new[]{a,c,b,b,c,d}) triangles[k++]=n;
            }
            var mesh=new Mesh { name="Continuous winding cave shell",vertices=vertices,triangles=triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Root+"/Meshes/WindingCave.asset");
            var shell=Group("Continuous cave shell - collidable",cave).gameObject;
            shell.AddComponent<MeshFilter>().sharedMesh=mesh;
            shell.AddComponent<MeshRenderer>().sharedMaterial=rock;
            shell.AddComponent<MeshCollider>().sharedMesh=mesh;
            Box("Sealed starting end",cave,new Vector3(0,7,-3),new Vector3(19,16,3),rock);
            Box("Continuous basin floor",basin,new Vector3(34,-12,305),new Vector3(260,4,210),silt);
            // Cliffs frame the reveal; the cave mouth itself remains clear.
            Box("West basin shelf",basin,new Vector3(-101,-1,302),new Vector3(20,28,225),rock);
            Box("East basin shelf",basin,new Vector3(169,-1,302),new Vector3(20,28,225),rock);
            Box("Far basin escarpment",basin,new Vector3(34,-1,420),new Vector3(290,28,18),rock);
            var random=new System.Random(2702);
            for(int i=0;i<65;i++)
            {
                float x=-80+(float)random.NextDouble()*225,z=235+(float)random.NextDouble()*166;
                float h=3+(float)random.NextDouble()*19;
                var b=GameObject.CreatePrimitive(PrimitiveType.Sphere); b.name="Basalt outcrop "+(i+1);
                b.transform.SetParent(details); b.transform.position=new Vector3(x,-11+h*.35f,z);
                b.transform.localScale=new Vector3(6+(float)random.NextDouble()*12,h,8+(float)random.NextDouble()*12);
                b.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*180,12);
                b.GetComponent<Renderer>().sharedMaterial=rock; b.isStatic=true;
            }
            Point("Cold light beyond cave mouth",details,new Vector3(34,11,226),5,65);
            RenderSettings.skybox=null; RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=new Color(.015f,.055f,.075f); RenderSettings.fogDensity=.019f;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.10f,.17f,.21f);
            RenderSettings.ambientEquatorColor=new Color(.055f,.095f,.12f);
            RenderSettings.ambientGroundColor=new Color(.025f,.04f,.05f);
            var sun=Group("Very faint filtered surface light").gameObject.AddComponent<Light>();
            sun.type=LightType.Directional; sun.color=new Color(.34f,.55f,.7f); sun.intensity=.25f;
            sun.transform.rotation=Quaternion.Euler(68,-25,0); sun.shadows=LightShadows.Soft; RenderSettings.sun=sun;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LowSignal/Characters/Robot/Player.prefab");
            if(prefab==null) throw new Exception("Player prefab missing");
            var player=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene); player.transform.position=Route[0]; player.transform.rotation=Quaternion.identity;
            foreach(var cam in player.GetComponentsInChildren<Camera>()) { cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=RenderSettings.fogColor; cam.farClipPlane=240; }
            Physics.SyncTransforms();
            foreach(var col in player.GetComponentsInChildren<Collider>()) col.enabled=false;
            for(int i=0;i<Route.Length-1;i++)
            {
                Vector3 delta=Route[i+1]-Route[i];
                // Test only this scene's geometry, independent of other scenes open in the editor.
                foreach(var col in cave.GetComponentsInChildren<Collider>())
                    if(col.Raycast(new Ray(Route[i],delta.normalized),out _,delta.magnitude)) throw new Exception("Cave route obstructed at bend "+i);
            }
            foreach(var col in player.GetComponentsInChildren<Collider>()) col.enabled=true;
            if(player.GetComponent<UnderwaterSwimController>()==null) throw new Exception("Swim controller missing");
            EditorSceneManager.SaveScene(scene,ScenePath);
            var entries=EditorBuildSettings.scenes.ToList();
            if(!entries.Any(e=>e.path==ScenePath)) entries.Add(new EditorBuildSettingsScene(ScenePath,true));
            EditorBuildSettings.scenes=entries.ToArray(); AssetDatabase.SaveAssets();
            Debug.Log("DEEP_REACH_PASS: saved playable cave and basin; all route segments clear.");
        }
        finally { EditorSceneManager.CloseScene(scene,true); if(previous.IsValid()) SceneManager.SetActiveScene(previous); }
        DeepReachCaveRevision.Revise();
    }
    static Transform Group(string name,Transform parent=null) { var t=new GameObject(name).transform; t.SetParent(parent,false); return t; }
    static Material Mat(string name,Color color,bool emission=false)
    {
        string path=Root+"/Materials/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path); if(m!=null)return m;
        m=new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color=color; m.SetFloat("_Smoothness",.12f); m.SetFloat("_Cull",0);
        if(emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor",color*2); }
        AssetDatabase.CreateAsset(m,path); return m;
    }
    static void Box(string name,Transform parent,Vector3 position,Vector3 size,Material material)
    { var b=GameObject.CreatePrimitive(PrimitiveType.Cube); b.name=name; b.transform.SetParent(parent); b.transform.position=position; b.transform.localScale=size; b.GetComponent<Renderer>().sharedMaterial=material; b.isStatic=true; }
    static void Point(string name,Transform parent,Vector3 position,float intensity,float range)
    { var t=Group(name,parent); t.position=position; var l=t.gameObject.AddComponent<Light>(); l.type=LightType.Point; l.color=new Color(.15f,.65f,.75f); l.intensity=intensity; l.range=range; }
}
