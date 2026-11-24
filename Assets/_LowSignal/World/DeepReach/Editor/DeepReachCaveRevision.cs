using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Union the passages before meshing so branch mouths have no overlapping walls.
public static class DeepReachCaveRevision
{
    const string ScenePath="Assets/_LowSignal/Scenes/DeepReach.unity";
    const float Cell=1f;
    static readonly Vector3[] Main={ P(0,0),P(0,18),P(-19,34),P(-22,51),P(7,66),P(28,82),P(24,101),P(-6,116),P(-16,134),P(2,150),P(36,165),P(49,183),P(34,200),P(34,220) };
    static readonly Vector3[][] Branches={
        new[]{P(-22,51),P(-36,56),P(-41,67)},
        new[]{P(24,101),P(40,106),P(46,116)},
        new[]{P(-16,134),P(-32,137),P(-38,147)} };
    static Vector3 P(float x,float z)=>new Vector3(x,7-z*.05f,z*1.18f-39.6f);
    [MenuItem("Low Signal/Level 2/Update winding cave and dead ends")]
    public static void Revise()
    {
        var previous=SceneManager.GetActiveScene(); var scene=SceneManager.GetSceneByPath(ScenePath); bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var cave=scene.GetRootGameObjects().Single(g=>g.name=="01 - Winding entrance cave");
            var samples=Smooth(Main); var branchSamples=Branches.Select(Smooth).ToArray();
            var cells=new HashSet<Vector2Int>();
            foreach(var p in samples) Fill(cells,p,p.z>194?Mathf.Lerp(6.5f,16,(p.z-194)/26):6.5f);
            foreach(var branch in branchSamples)foreach(var p in branch)Fill(cells,p,4.2f);
            cells.RemoveWhere(c=>c.y>=220);
            var vertices=new List<Vector3>();var tris=new List<int>();
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            { int rows=Mathf.Abs(a.y-b.y)>2?20:1,cols=Mathf.Abs(a.y-d.y)>2?20:1;
                Vector3 V(float t,float u)=>Vector3.Lerp(Vector3.Lerp(a,b,t),Vector3.Lerp(d,c,t),u);
                for(int j=0;j<rows;j++)for(int k=0;k<cols;k++) {float t=j/(float)rows,T=(j+1)/(float)rows,u=k/(float)cols,U=(k+1)/(float)cols;
                    int n=vertices.Count;vertices.AddRange(new[]{V(t,u),V(T,u),V(T,U),V(t,U)});tris.AddRange(new[]{n,n+1,n+2,n,n+2,n+3}); }
            }

            Vector3 At(float x,float z,bool top)=>new Vector3(x,top?13-(z+39.6f)/1.18f*.025f:1-(z+39.6f)/1.18f*.05f,z);
            foreach(var cell in cells)
            {
                float x=cell.x*Cell,z=cell.y*Cell;
                Vector3 a=At(x,z,false),b=At(x+Cell,z,false),c=At(x+Cell,z+Cell,false),d=At(x,z+Cell,false);
                Vector3 A=At(x,z,true),B=At(x+Cell,z,true),C=At(x+Cell,z+Cell,true),D=At(x,z+Cell,true);
                Quad(a,d,c,b); Quad(A,B,C,D);
                if(!cells.Contains(cell+Vector2Int.left))Quad(a,A,D,d);
                if(!cells.Contains(cell+Vector2Int.right))Quad(b,c,C,B);
                if(!cells.Contains(cell+Vector2Int.down))Quad(a,b,B,A);
                // Leave the far end open to the seafloor.
                if(!cells.Contains(cell+Vector2Int.up)&&z<219)Quad(d,D,C,c);
            }
            RockSurface(vertices,tris);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_LowSignal/World/DeepReach/Meshes/WindingCave.asset");
            Undo.RecordObject(mesh,"Reshape Deep Reach cave"); mesh.Clear(); mesh.indexFormat=IndexFormat.UInt32;
            ValidateSeams(vertices,tris);
            mesh.SetVertices(vertices); mesh.SetTriangles(tris,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var shell=cave.GetComponentInChildren<MeshCollider>(); shell.sharedMesh=null; shell.cookingOptions=MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase; shell.sharedMesh=mesh;
            foreach(var t in cave.GetComponentsInChildren<Transform>().Where(t=>t.name=="Sealed starting end").ToArray())Undo.DestroyObjectImmediate(t.gameObject);
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Guide glow"||t.name=="Faint cave guide growth").ToArray())Undo.DestroyObjectImmediate(t.gameObject);
            var details=scene.GetRootGameObjects().FirstOrDefault(g=>g.name.StartsWith("03 -")); if(details!=null)details.name="03 - Rock silhouettes";
            var markers=scene.GetRootGameObjects().Single(g=>g.name.StartsWith("04 -"));
            foreach(Transform t in markers.transform.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
            for(int i=0;i<Main.Length;i++)Marker(markers.transform,"Main route "+i,Main[i]);
            for(int i=0;i<Branches.Length;i++)Marker(markers.transform,"Dead end "+(i+1),Branches[i].Last());
            var player=scene.GetRootGameObjects().FirstOrDefault(g=>g.GetComponent<UnderwaterSwimController>()!=null);
            if(player!=null)player.transform.position=Main[0];
            Physics.SyncTransforms();
            CheckRoute(scene,samples,"main passage");
            foreach(var branch in branchSamples)CheckRoute(scene,branch,"dead end");
            // Dead ends must have a solid wall immediately beyond their short approach.
            foreach(var branch in Branches)
            { var end=branch.Last(); var direction=(end-branch[branch.Length-2]).normalized;
                if(!shell.Raycast(new Ray(end,direction),out _,8))throw new Exception("Dead end is not capped"); }
            if(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Any(t=>t.name=="Guide glow"||t.name=="Faint cave guide growth"))throw new Exception("Tunnel guide lights remain");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("DEEP_REACH_REVISION_PASS: main and branch routes swept with 2.2m clearance; three end walls verified; tunnel guide lights removed. Main route length "+Length(samples).ToString("F0")+"m.");
        }
        finally { if(opened)EditorSceneManager.CloseScene(scene,true); if(previous.IsValid())SceneManager.SetActiveScene(previous); }
    }
    static void RockSurface(List<Vector3> vertices,List<int> tris)
    {
        var unique=new List<Vector3>();var map=new Dictionary<Vector3Int,int>();var ids=new int[vertices.Count];
        for(int i=0;i<vertices.Count;i++) { var v=vertices[i];var key=new Vector3Int(Mathf.RoundToInt(v.x*1000),Mathf.RoundToInt(v.y*1000),Mathf.RoundToInt(v.z*1000));
            if(!map.TryGetValue(key,out int id)){id=unique.Count;map[key]=id;unique.Add(v);}ids[i]=id; }
        for(int i=0;i<tris.Count;i++)tris[i]=ids[tris[i]];
        var neighbours=new HashSet<int>[unique.Count];for(int i=0;i<unique.Count;i++)neighbours[i]=new HashSet<int>();
        for(int i=0;i<tris.Count;i+=3)for(int j=0;j<3;j++){int a=tris[i+j],b=tris[i+(j+1)%3];neighbours[a].Add(b);neighbours[b].Add(a);}
        for(int pass=0;pass<24;pass++) { var next=new Vector3[unique.Count];for(int i=0;i<unique.Count;i++) {
            Vector3 average=Vector3.zero;foreach(int n in neighbours[i])average+=unique[n];
            next[i]=unique[i].z>218?unique[i]:Vector3.Lerp(unique[i],average/Mathf.Max(1,neighbours[i].Count),.48f);
        }unique=new List<Vector3>(next); }
        for(int i=0;i<unique.Count;i++) { var v=unique[i];float fade=1-Mathf.Clamp01((v.z-210)/9);
            float Noise(float a,float b)=> (Mathf.PerlinNoise(a,b)-.5f);
            v+=fade*new Vector3(Noise(v.y*.32f+34,v.z*.24f)*1.5f,Noise(v.x*.25f+19,v.z*.3f)*1.1f,Noise(v.x*.34f+62,v.y*.26f)*1.3f);
            float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(198,218,v.z));
            float bottom=1-(v.z+39.6f)/1.18f*.05f,top=13-(v.z+39.6f)/1.18f*.025f;
            float mid=(bottom+top)*.5f,half=(top-bottom)*.5f;
            float nx=(v.x-34)/16f,ny=(v.y-mid)/half;
            float radius=Mathf.Max(Mathf.Abs(nx),Mathf.Abs(ny));
            float angle=Mathf.Atan2(ny,nx);
            var rounded=new Vector3(34+16*radius*Mathf.Cos(angle),mid+half*radius*Mathf.Sin(angle),v.z);
            unique[i]=Vector3.Lerp(v,rounded,blend);
        }
        vertices.Clear();vertices.AddRange(unique);
    }
    static void ValidateSeams(List<Vector3> vertices,List<int> triangles)
    {
        var edges=new Dictionary<ulong,int>();
        for(int i=0;i<triangles.Count;i+=3)for(int j=0;j<3;j++) {
            int a=triangles[i+j],b=triangles[i+(j+1)%3];ulong key=((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
            edges.TryGetValue(key,out int count);edges[key]=count+1;
        }
        int opening=0;
        foreach(var edge in edges) {if(edge.Value==2)continue;
            int a=(int)(edge.Key>>32),b=(int)(edge.Key&0xffffffff);
            if(edge.Value!=1||vertices[a].z<219.9f||vertices[b].z<219.9f)throw new Exception("Unsealed cave edge at "+vertices[a]);opening++;
        }
        if(opening==0)throw new Exception("Cave mouth is closed");
        Debug.Log("CAVE_SEAMS_PASS: every internal edge shared by exactly two triangles; only the seafloor mouth is open.");
    }
    static void Marker(Transform parent,string name,Vector3 p) { var g=new GameObject(name);g.tag="EditorOnly";g.transform.SetParent(parent);g.transform.position=p; }
    public static void ValidateRoutes(Scene scene)
    { var root=scene.GetRootGameObjects().Single(g=>g.name=="01 - Winding entrance cave").transform;
        var descent=root.Find(DeepReachCaveDescent.RouteGroup);
        if(descent){foreach(Transform route in descent)CheckRoute(scene,route.Cast<Transform>().Select(t=>t.position).ToList(),route.name);return;}
        CheckRoute(scene,Smooth(Main).Select(root.TransformPoint).ToList(),"main passage"); foreach(var branch in Branches)CheckRoute(scene,Smooth(branch).Select(root.TransformPoint).ToList(),"dead end"); }
    static float Length(List<Vector3> path) { float n=0;for(int i=1;i<path.Count;i++)n+=Vector3.Distance(path[i-1],path[i]);return n; }
    static void CheckRoute(Scene scene,List<Vector3> path,string label)
    { for(int i=1;i<path.Count;i++){var d=path[i]-path[i-1];foreach(var hit in Physics.SphereCastAll(path[i-1],1.1f,d.normalized,d.magnitude))if(hit.collider.gameObject.scene==scene&&hit.collider.GetComponentInParent<UnderwaterSwimController>()==null)throw new Exception(label+" obstructed: "+hit.collider.name+" at "+path[i]);} }
    static void Fill(HashSet<Vector2Int> cells,Vector3 p,float radius)
    { for(int x=Mathf.FloorToInt(p.x-radius);x<=Mathf.CeilToInt(p.x+radius);x++)for(int z=Mathf.FloorToInt(p.z-radius);z<=Mathf.CeilToInt(p.z+radius);z++)if(new Vector2(x+.5f-p.x,z+.5f-p.z).sqrMagnitude<radius*radius)cells.Add(new Vector2Int(x,z)); }
    static List<Vector3> Smooth(Vector3[] points)
    {
        var result=new List<Vector3>();
        for(int i=0;i<points.Length-1;i++)for(int s=0;s<40;s++)
        { float t=s/40f;Vector3 a=points[Mathf.Max(0,i-1)],b=points[i],c=points[i+1],d=points[Mathf.Min(points.Length-1,i+2)];result.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)); }
        result.Add(points.Last());return result;
    }
}
