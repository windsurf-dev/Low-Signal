using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Shared station profiles keep plating, decks and rails on the same outline.
public static class DeepReachHullRepair
{
    const string Root="Assets/_LowSignal/World/DeepReach/CargoShip";
    static Material blue,red,silt,frame;
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    sealed class Surface {
        public readonly List<Vector3> vertices=new List<Vector3>();
        public readonly List<int> triangles=new List<int>();
        public void Tri(Vector3 a,Vector3 b,Vector3 c) {
            if(Vector3.Cross(b-a,c-a).sqrMagnitude<.000001f)return;
            int n=vertices.Count;vertices.AddRange(new[]{a,b,c});triangles.AddRange(new[]{n,n+1,n+2});
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Tri(a,b,c);Tri(a,c,d);}
        public void Side(int sign,Vector3 a,Vector3 b,Vector3 c,Vector3 d){if(sign>0)Quad(a,b,c,d);else Quad(d,c,b,a);}
        public void Save(string name,Transform parent,Material mat) {
            string path=Root+"/Meshes/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial=mat;g.AddComponent<MeshCollider>().sharedMesh=mesh;g.isStatic=true;
        }
    }
    static float Width(float x,float[] xs,float[] ws) {
        for(int i=1;i<xs.Length;i++)if(x<=xs[i])return Mathf.Lerp(ws[i-1],ws[i],Mathf.InverseLerp(xs[i-1],xs[i],x));
        return ws.Last();
    }
    static void Remove(Transform parent,params string[] names) {
        foreach(var t in parent.Cast<Transform>().Where(t=>names.Contains(t.name)).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
    }
    static void Bar(string name,Transform p,Vector3 a,Vector3 b,float size,Material m) {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(p,false);
        g.transform.localPosition=(a+b)*.5f;g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);g.transform.localScale=V(size,(b-a).magnitude,size);
        g.GetComponent<Renderer>().sharedMaterial=m;g.isStatic=true;
    }
    static void Section(Transform part,bool bow) {
        Remove(part,"Red lower hull","Faceted blue steel hull","Hold keel floor","Weathered gunwale","Stern transom","Accommodation deck","Port and starboard passage","Open hold side walkway","Foredeck","Tapered forecastle","Cargo hatch coaming","Cargo hatch end rim","Broken transverse rib","Torn steel tooth","Deck rail post","Deck handrail","Crane structural crossdeck between holds",
            "Stern sealed lower shell","Stern continuous upper shell","Stern fitted deck","Bow sealed lower shell","Bow continuous upper shell","Bow fitted deck","Fitted gunwale","Fitted rail","Hatch raised rim","Fracture edge frame");
        float[] xs=bow?new[]{5f,19,39,54,65}:new[]{-67f,-59,-40,-20,-8};
        float[] ws=bow?new[]{15f,16,15,10,0}:new[]{10f,15,16,16,15};
        var lower=new Surface();var upper=new Surface();var deck=new Surface();
        for(int i=1;i<xs.Length;i++) {
            float a=xs[i-1],b=xs[i],wa=ws[i-1],wb=ws[i];
            foreach(int s in new[]{-1,1}) {
                lower.Side(s,V(a,1,s*wa*.65f),V(b,1,s*wb*.65f),V(b,4,s*wb*.85f),V(a,4,s*wa*.85f));
                upper.Side(s,V(a,4,s*wa*.85f),V(b,4,s*wb*.85f),V(b,10.4f,s*wb),V(a,10.4f,s*wa));
                Bar("Fitted gunwale",part,V(a,10.6f,s*wa),V(b,10.6f,s*wb),.25f,red);
                // Rails follow the taper instead of projecting beyond the hull.
                if(bow||a<-32) {
                    Bar("Fitted rail",part,V(a,12,s*wa),V(b,12,s*wb),.12f,frame);
                    int count=Mathf.CeilToInt((b-a)/4);
                    for(int j=0;j<count;j++) {float x=Mathf.Lerp(a,b,j/(float)count);float w=Width(x,xs,ws);
                        Bar("Fitted rail",part,V(x,10.4f,s*w),V(x,12,s*w),.12f,frame);}
                }
            }
            lower.Quad(V(b,1,-wb*.65f),V(b,1,wb*.65f),V(a,1,wa*.65f),V(a,1,-wa*.65f));
        }
        if(!bow) {
            // The transom uses exactly the same three width stations as the hull sides.
            lower.Quad(V(-67,1,-6.5f),V(-67,1,6.5f),V(-67,4,8.5f),V(-67,4,-8.5f));
            upper.Quad(V(-67,4,-8.5f),V(-67,4,8.5f),V(-67,10.4f,10),V(-67,10.4f,-10));
        }
        var holes=bow?new[]{new Vector2(10,26),new Vector2(29,45)}:new[]{new Vector2(-31,-9)};
        float half=bow?9.35f:9.5f;
        var cuts=xs.Concat(holes.SelectMany(h=>new[]{h.x,h.y})).Distinct().OrderBy(x=>x).ToArray();
        void Strip(float a,float b,float za,float zb,float Za,float Zb) {
            deck.Quad(V(a,10.4f,za),V(a,10.4f,Za),V(b,10.4f,Zb),V(b,10.4f,zb));
            deck.Quad(V(a,9.8f,za),V(b,9.8f,zb),V(b,9.8f,Zb),V(a,9.8f,Za));
        }
        for(int i=1;i<cuts.Length;i++) {
            float a=cuts[i-1],b=cuts[i],wa=Width(a,xs,ws),wb=Width(b,xs,ws);
            bool open=holes.Any(h=>(a+b)*.5f>h.x&&(a+b)*.5f<h.y);
            if(open) {
                Strip(a,b,-wa,-wb,-half,-half);Strip(a,b,half,half,wa,wb);
                foreach(int s in new[]{-1,1})deck.Quad(V(a,9.8f,s*half),V(a,10.4f,s*half),V(b,10.4f,s*half),V(b,9.8f,s*half));
            }else Strip(a,b,-wa,-wb,wa,wb);
        }
        foreach(var h in holes) {
            foreach(float x in new[]{h.x,h.y})deck.Quad(V(x,9.8f,-half),V(x,10.4f,-half),V(x,10.4f,half),V(x,9.8f,half));
            foreach(int s in new[]{-1,1})Bar("Hatch raised rim",part,V(h.x,10.65f,s*(half+.15f)),V(h.y,10.65f,s*(half+.15f)),.3f,red);
            foreach(float x in new[]{h.x-.15f,h.y+.15f})Bar("Hatch raised rim",part,V(x,10.65f,-half-.3f),V(x,10.65f,half+.3f),.3f,red);
        }
        string prefix=bow?"Bow":"Stern";
        lower.Save(prefix+" sealed lower shell",part,red);upper.Save(prefix+" continuous upper shell",part,blue);deck.Save(prefix+" fitted deck",part,silt);
        float end=bow?xs[0]:xs.Last(),endWidth=bow?ws[0]:ws.Last();
        foreach(int s in new[]{-1,1})Bar("Fracture edge frame",part,V(end,1,s*endWidth*.65f),V(end,10.4f,s*endWidth),.3f,red);
    }
    public static void Apply() {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/_LowSignal/Scenes/DeepReach.unity");
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/DeepReach.unity");
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;
        blue=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Deep blue hull.mat");red=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Oxide red plating.mat");
        silt=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Silted deck.mat");frame=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Exposed frames.mat");
        var stern=ship.Find("01 - Stern and accommodation");var bow=ship.Find("02 - Broken forward cargo hull");
        Section(stern,false);Section(bow,true);
        var house=stern.Find("Accommodation tower");
        foreach(var pair in new[]{("Lower house",13.9f),("Upper house",19.9f),("Wheelhouse",24.9f),("Bridge roof",27.75f)}) {
            var t=house.Find(pair.Item1);var p=t.localPosition;p.y=pair.Item2;t.localPosition=p;
        }
        // Seat the funnel on the roof and keep its casing within the roof outline.
        stern.Find("Funnel").localPosition=V(-57,32.6f,4);
        stern.Find("Funnel rust band").localPosition=V(-57,35.6f,4);
        Physics.SyncTransforms();Validate(bow,true);Validate(stern,false);DeepReachCaveRevision.ValidateRoutes(scene);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        DeepReachCargoShip.Capture(ship);
        Debug.Log("CARGO_HULL_REPAIR_PASS: fitted decks, continuous hull profiles and transom; deck coverage, hull raycasts, crane seating and cave routes passed.");
    }
    static void Validate(Transform part,bool bow) {
        var cols=part.GetComponentsInChildren<Collider>();
        bool Hit(Vector3 from,Vector3 dir,float length)=>cols.Any(c=>c.Raycast(new Ray(part.TransformPoint(from),part.TransformDirection(dir)),out _,length*1.5f));
        float[] xs=bow?new[]{5f,19,39,54,65}:new[]{-67f,-59,-40,-20,-8};
        float[] ws=bow?new[]{15f,16,15,10,0}:new[]{10f,15,16,16,15};
        for(float x=xs[0]+.5f;x<xs.Last();x+=1) {
            float w=Width(x,xs,ws);
            for(float z=-w+.4f;z<w-.3f;z+=1) {
                bool hole=bow?((x>10&&x<26)||(x>29&&x<45))&&Mathf.Abs(z)<9.35f:x>-31&&x<-9&&Mathf.Abs(z)<9.5f;
                if(!hole&&!Hit(V(x,10.9f,z),Vector3.down,1))throw new Exception("Deck gap at "+V(x,10.4f,z));
            }
            foreach(int s in new[]{-1,1})for(float y=1.5f;y<10.2f;y+=.75f)
                if(!Hit(V(x,y,s*(w+2)),V(0,0,-s),w+3))throw new Exception("Hull gap at "+V(x,y,s*w));
        }
        foreach(var crane in part.Cast<Transform>().Where(t=>t.name=="Deck mounted lattice cargo crane")) {
            var p=crane.localPosition;
            if(!Hit(p+Vector3.up,Vector3.down,1.2f))throw new Exception("Crane deck gap");
        }
    }
}
