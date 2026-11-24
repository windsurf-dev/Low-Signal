using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Reshape the sealed branch network without changing its topology or its exit ring.
public static class DeepReachCaveDescent
{
    const string Root="Assets/_LowSignal/World/DeepReach";
    public const string RouteGroup="Descent route markers";
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static Vector3 P(float x,float z)=>V(x,7-z*.05f,z*1.18f-39.6f);
    static readonly Vector3[] Main={P(0,0),P(0,18),P(-19,34),P(-22,51),P(7,66),P(28,82),P(24,101),P(-6,116),P(-16,134),P(2,150),P(36,165),P(49,183),P(34,200),P(34,220)};
    static readonly Vector3[][] Branches={new[]{P(-22,51),P(-36,56),P(-41,67)},new[]{P(24,101),P(40,106),P(46,116)},new[]{P(-16,134),P(-32,137),P(-38,147)}};
    static Vector3 Reshape(Vector3 p) {
        if(p.z>=202)return p;
        float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(166,202,p.z));
        float progress=Mathf.Clamp01((220-p.z)/259.6f);
        float width=1+fade*(.16f*Mathf.Sin(p.z*.075f)+.08f*Mathf.Sin(p.z*.19f+2));
        float oldFloor=1-(p.z+39.6f)/1.18f*.05f,oldRoof=13-(p.z+39.6f)/1.18f*.025f,center=(oldFloor+oldRoof)*.5f;
        float height=1+fade*(.16f*Mathf.Sin(p.z*.09f+1)+.11f*Mathf.Cos(p.z*.18f));
        float roughX=(Mathf.PerlinNoise(p.y*.24f+21,p.z*.21f+54)-.5f)*1.7f;
        float roughY=(Mathf.PerlinNoise(p.x*.28f+61,p.z*.23f+19)-.5f)*1.25f;
        return V(p.x*width+fade*(Mathf.Sin(p.z*.04f)*3+roughX),center+(p.y-center)*height+52*Mathf.Pow(progress,1.05f)*fade+roughY*fade,p.z-(202-p.z)*.55f*fade);
    }
    static List<Vector3> Smooth(Vector3[] points) {
        var samples=new List<Vector3>();
        for(int i=0;i<points.Length-1;i++)for(int j=0;j<100;j++) {
            float t=j/100f;var a=points[Mathf.Max(0,i-1)];var b=points[i];var c=points[i+1];var d=points[Mathf.Min(points.Length-1,i+2)];
            samples.Add(Reshape(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)));
        }
        samples.Add(Reshape(points.Last()));return samples;
    }
    static Transform Group(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
    [MenuItem("Low Signal/Level 2/Extend and deepen the entrance cave")]
    public static void Apply() {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/_LowSignal/Scenes/DeepReach.unity");if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/DeepReach.unity");
        var cave=scene.GetRootGameObjects().Single(g=>g.name=="01 - Winding entrance cave").transform;
        var shell=cave.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.StartsWith("Continuous cave shell"));
        var source=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/WindingCave.asset");var oldVertices=source.vertices;var vertices=oldVertices.Select(Reshape).ToArray();var triangles=source.triangles;
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Descending irregular cave.asset");if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,Root+"/Meshes/Descending irregular cave.asset");}else mesh.Clear();
        mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        shell.sharedMesh=mesh;var shellCollider=shell.GetComponent<MeshCollider>();shellCollider.sharedMesh=null;shellCollider.sharedMesh=mesh;
        foreach(string name in new[]{RouteGroup,"Embedded cave fracture shelves"}){var old=cave.Find(name);if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);}
        var markers=Group(RouteGroup,cave);markers.gameObject.tag="EditorOnly";
        var route=Smooth(Main);var routes=new List<List<Vector3>>{route};routes.AddRange(Branches.Select(Smooth));
        for(int i=0;i<routes.Count;i++) {
            var group=Group(i==0?"Main passage":"Dead end "+i,markers);group.gameObject.tag="EditorOnly";
            for(int j=0;j<routes[i].Count;j++){var point=Group("Sample "+j,group);point.localPosition=routes[i][j];point.gameObject.tag="EditorOnly";}
        }
        // Retain the visible layout controls, moved to their corresponding deformed locations.
        var layout=scene.GetRootGameObjects().FirstOrDefault(g=>g.name.StartsWith("04 -"));
        if(layout)foreach(var t in layout.transform.Cast<Transform>()) {
            if(t.name.StartsWith("Main route ")&&int.TryParse(t.name.Substring(11),out int id)&&id<Main.Length)t.position=cave.TransformPoint(Reshape(Main[id]));
            if(t.name.StartsWith("Dead end ")&&int.TryParse(t.name.Substring(9),out int branch)&&branch>=1&&branch<=Branches.Length)t.position=cave.TransformPoint(Reshape(Branches[branch-1].Last()));
        }
        var player=scene.GetRootGameObjects().Single(g=>g.GetComponent<UnderwaterSwimController>());
        player.transform.position=cave.TransformPoint(route[0]);var facing=cave.TransformDirection(route[12]-route[0]);player.transform.rotation=Quaternion.LookRotation(new Vector3(facing.x,0,facing.z));
        Physics.SyncTransforms();DeepReachCaveRevision.ValidateRoutes(scene);
        var shelves=Group("Embedded cave fracture shelves",cave);var random=new System.Random(81623);float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        var rockMeshes=Enumerable.Range(0,12).Select(i=>AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/RockFormations/Bevelled monolith "+i+".asset")).ToArray();
        var palette=new[]{"Warm grey fracture faces","Slate shadow faces","Olive stone faces","Pale bevels"}.Select(n=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/RockFormations/"+n+".mat")).ToArray();
        var probeObject=new GameObject("Temporary cave clearance probe");var probe=probeObject.AddComponent<SphereCollider>();probe.radius=1.2f;
        var worldRoutes=routes.SelectMany(r=>r.Select(cave.TransformPoint)).ToArray();int accepted=0;
        for(int i=40;i<route.Count-100;i+=17) {
            if(route[i].z>178)continue;
            var origin=cave.TransformPoint(route[i]);var forward=(route[Mathf.Min(route.Count-1,i+5)]-route[i-5]).normalized;
            var side=Vector3.Cross(Vector3.up,forward).normalized;var direction=i%4==0?Vector3.up:i%3==0?-side:side;
            if(!shellCollider.Raycast(new Ray(origin,direction),out var hit,28))continue;
            float depth=R(3.5f,6.5f),span=R(4.5f,9);
            var rock=Group(i%4==0?"Embedded ceiling fracture":"Embedded wall shelf",shelves);rock.position=hit.point-hit.normal*depth*.67f;
            rock.rotation=Quaternion.FromToRotation(Vector3.up,hit.normal)*Quaternion.Euler(0,R(0,360),0);rock.localScale=V(span,depth,span*R(.45f,.8f));
            var rockMesh=rockMeshes[random.Next(rockMeshes.Length)];rock.gameObject.AddComponent<MeshFilter>().sharedMesh=rockMesh;var renderer=rock.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=palette;
            var col=rock.gameObject.AddComponent<MeshCollider>();col.sharedMesh=rockMesh;col.convex=true;rock.gameObject.isStatic=true;Physics.SyncTransforms();
            var bounds=renderer.bounds;bounds.Expand(2.4f);
            bool obstructs=worldRoutes.Where(bounds.Contains).Any(p=>Physics.ComputePenetration(probe,p,Quaternion.identity,col,rock.position,rock.rotation,out _,out _));
            if(obstructs)UnityEngine.Object.DestroyImmediate(rock.gameObject);else accepted++;
        }
        UnityEngine.Object.DestroyImmediate(probeObject);Physics.SyncTransforms();DeepReachCaveRevision.ValidateRoutes(scene);
        ValidateMesh(vertices,triangles,oldVertices);
        foreach(var branch in routes.Skip(1)) {
            var end=cave.TransformPoint(branch.Last());var direction=cave.TransformDirection(branch.Last()-branch[branch.Count-6]).normalized;
            if(!shellCollider.Raycast(new Ray(end,direction),out _,15))throw new Exception("Cave dead end lost its cap");
        }
        float length=0;for(int i=1;i<route.Count;i++)length+=Vector3.Distance(route[i-1],route[i]);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Capture(cave,route);
        Debug.Log("CAVE_DESCENT_PASS: "+length.ToString("F0")+"m main route, "+(route[0].y-route.Last().y).ToString("F1")+"m descent, "+accepted+" embedded fractures; full 2.2m clearance, three capped branches and unchanged exit ring verified.");
    }
    static void ValidateMesh(Vector3[] vertices,int[] triangles,Vector3[] original) {
        var edges=new Dictionary<(int,int),int>();for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++){int a=triangles[i+j],b=triangles[i+(j+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));edges.TryGetValue(key,out int count);edges[key]=count+1;}
        foreach(var edge in edges)if(edge.Value!=2&&(edge.Value!=1||vertices[edge.Key.Item1].z<219.99f||vertices[edge.Key.Item2].z<219.99f))throw new Exception("Unexpected opening in descending cave");
        for(int i=0;i<original.Length;i++)if(original[i].z>=202&&vertices[i]!=original[i])throw new Exception("Exit join moved");
        Debug.Log("CAVE_DESCENT_SEAMS_PASS: only existing exit boundary open; final 18m and mouth vertices unchanged.");
    }
    static void Capture(Transform cave,List<Vector3> route) {
        Directory.CreateDirectory("Previews/DeepReach");var go=new GameObject("Temporary descending cave preview");var cam=go.AddComponent<Camera>();cam.fieldOfView=76;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;
        var lamp=go.AddComponent<Light>();lamp.type=LightType.Point;lamp.color=new Color(.60f,.76f,.76f);lamp.intensity=180;lamp.range=35;
        var rt=RenderTexture.GetTemporary(1600,900,24);var previous=RenderTexture.active;var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
        foreach(var shot in new[]{("Descending cave start",10),("Irregular cave shelves",340),("Descending cave lower bend",790)}) {
            cam.transform.position=cave.TransformPoint(route[shot.Item2]);cam.transform.LookAt(cave.TransformPoint(route[shot.Item2+30]));cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();File.WriteAllBytes("Previews/DeepReach/"+shot.Item1+".png",texture.EncodeToPNG());
        }
        cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(go);
    }
}
