using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Baked authoring geometry: no runtime generation, all holds and decks are editable.
public static class DeepReachCargoShip
{
    const string Root="Assets/_LowSignal/World/DeepReach/CargoShip";
    const string ScenePath="Assets/_LowSignal/Scenes/DeepReach.unity";
    static Material hull,rust,deck,pale,glass,steel,green;
    static int meshId;
    const float WreckScale=1.5f;
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static Transform Group(string n,Transform p=null) {var t=new GameObject(n).transform;t.SetParent(p,false);return t;}
    static Material Mat(string n,Color c) {
        string path=Root+"/Materials/"+n+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.color=c;m.SetFloat("_Smoothness",.08f);m.SetFloat("_Cull",0);EditorUtility.SetDirty(m);return m;
    }
    static Transform Box(string n,Transform p,Vector3 pos,Vector3 size,Material m,bool solid=true) {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(p,false);g.transform.localPosition=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;
        if(!solid)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.isStatic=true;return g.transform;
    }
    static void Beam(string n,Transform p,Vector3 a,Vector3 b,float width,Material m) {
        var t=Box(n,p,(a+b)*.5f,V(width,(b-a).magnitude,width),m);t.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
    }
    static void Rod(string name,Transform parent,Vector3 a,Vector3 b,float radius,Material material) {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent,false);
        go.transform.localPosition=(a+b)*.5f;go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
        go.transform.localScale=V(radius*2,(b-a).magnitude*.5f,radius*2);go.GetComponent<Renderer>().sharedMaterial=material;go.isStatic=true;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    static void Cranes(Transform bow) {
        var crossdeck=Box("Crane structural crossdeck between holds",bow,V(27.5f,10,0),V(3,.8f,20),deck);
        foreach(float x in new[]{27.5f,47}) {
            var crane=Group("Deck mounted lattice cargo crane",bow);crane.localPosition=V(x,10.4f,0);
            Box("Bolted foundation plate",crane,V(0,.15f,0),V(2.8f,.3f,2.8f),rust);
            Rod("Round pedestal",crane,V(0,.3f,0),V(0,3.5f,0),.85f,steel);
            Rod("Slewing ring",crane,V(0,3.1f,0),V(0,3.6f,0),1.1f,rust);
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}) {
                Beam("Pedestal foundation gusset",crane,V(sx*1.2f,.3f,sz*1.2f),V(sx*.6f,2,sz*.6f),.16f,rust);
                Rod("Foundation bolt",crane,V(sx*1.1f,.3f,sz*1.1f),V(sx*1.1f,.48f,sz*1.1f),.09f,steel);
            }
            var upper=Group("Rotating crane head",crane);upper.localPosition=V(0,3.6f,0);upper.localRotation=Quaternion.Euler(0,x<30?-55:65,0);
            Box("Winch housing",upper,V(-1,1,0),V(2,1.8f,1.7f),rust);
            Box("Operator cab",upper,V(-.7f,1,1.5f),V(1.8f,1.8f,1.3f),pale);
            Box("Cab front glazing",upper,V(.21f,1.15f,1.5f),V(.05f,.95f,1),glass,false);
            Box("Cab side glazing",upper,V(-.7f,1.15f,2.16f),V(1.3f,.95f,.05f),glass,false);
            Rod("Boom pivot pin",upper,V(.5f,1,-1),V(.5f,1,1),.25f,steel);
            var start=V(.5f,1,0);var tip=V(16,9,0);var axis=(tip-start).normalized;
            var up=Vector3.Cross(Vector3.forward,axis).normalized;
            Vector3 Corner(float t,int u,int s)=>Vector3.Lerp(start,tip,t)+(up*u+Vector3.forward*s)*Mathf.Lerp(.65f,.16f,t);
            foreach(int u in new[]{-1,1})foreach(int s in new[]{-1,1})Rod("Tapered boom chord",upper,Corner(0,u,s),Corner(1,u,s),.105f,rust);
            for(int i=0;i<8;i++) {
                float a=i/8f,b=(i+1)/8f;
                foreach(int s in new[]{-1,1}) {
                    Rod("Triangular lattice side brace",upper,Corner(a,i%2==0?-1:1,s),Corner(b,i%2==0?1:-1,s),.055f,steel);
                    Rod("Boom cross tie",upper,Corner(a,-1,s),Corner(a,1,s),.055f,rust);
                }
                Rod("Lattice top diagonal",upper,Corner(a,1,-1),Corner(b,1,1),.05f,steel);
            }
            Rod("A frame left stay",upper,V(-1,1,-.7f),V(-.5f,6,0),.14f,steel);
            Rod("A frame right stay",upper,V(1,1,.7f),V(-.5f,6,0),.14f,steel);
            Rod("Boom suspension cable",upper,V(-.5f,6,0),tip+up*.16f,.045f,steel);
            Rod("Hoist rope",upper,tip,V(16,-.5f,0),.045f,steel);
            Rod("Tip sheave",upper,tip-V(0,0,.23f),tip+V(0,0,.23f),.3f,steel);
            Box("Hanging hook block",upper,V(16,-.7f,0),V(.5f,.65f,.4f),rust,false);
            var hook=new[]{V(16,-1,0),V(16,-1.65f,0),V(16.4f,-1.85f,0),V(16.75f,-1.6f,0),V(16.75f,-1.3f,0)};
            for(int i=1;i<hook.Length;i++)Rod("Cargo hook",upper,hook[i-1],hook[i],.11f,steel);
            // Probe the actual deck below each mounting point, excluding crane geometry.
            var support=x<30?crossdeck:(bow.Find("Foredeck")??bow.Find("Bow fitted deck"));
            Physics.SyncTransforms();
            if(!support.GetComponent<Collider>().Raycast(new Ray(crane.TransformPoint(V(0,1,0)),-bow.up),out _,3*WreckScale))throw new Exception("Crane has no deck support");
        }
    }
    public static void ReviseCranes() {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath);
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;
        var bow=ship.Find("02 - Broken forward cargo hull");
        foreach(var t in bow.Cast<Transform>().Where(t=>new[]{"Cargo derrick mast","Collapsed loading boom","Derrick cable","Deck mounted lattice cargo crane","Crane structural crossdeck between holds"}.Contains(t.name)).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
        hull=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Deep blue hull.mat");
        rust=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Oxide red plating.mat");steel=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Exposed frames.mat");
        pale=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Weathered ivory.mat");glass=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Dead bridge windows.mat");deck=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Silted deck.mat");
        Cranes(bow);Physics.SyncTransforms();DeepReachCaveRevision.ValidateRoutes(scene);
        DeepReachHullRepair.Apply();
        Debug.Log("CARGO_CRANES_PASS: two lattice cranes mounted on crossdeck and foredeck; deck support raycasts and cave routes passed.");
    }
    static void MeshObject(string n,Transform p,List<Vector3> v,List<int> tr,Material m) {
        string path=Root+"/Meshes/Ship"+(meshId++)+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
        mesh.name=n;mesh.SetVertices(v);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var g=Group(n,p).gameObject;g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=m;g.AddComponent<MeshCollider>().sharedMesh=mesh;g.isStatic=true;
    }
    static void Panel(string n,Transform p,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Material m) {
        var vertices=new List<Vector3>{a,b,c,d};var triangles=new List<int>();
        if(Vector3.Cross(b-a,c-a).sqrMagnitude>.00001f)triangles.AddRange(new[]{0,1,2});
        if(Vector3.Cross(c-a,d-a).sqrMagnitude>.00001f)triangles.AddRange(new[]{0,2,3});
        MeshObject(n,p,vertices,triangles,m);
    }
    // Flat shaded stations taper into a pointed bow; independent strips leave the fracture open.
    static void Hull(Transform p,float[] xs,float[] widths,bool brokenFront) {
        for(int i=0;i<xs.Length-1;i++)for(int side=-1;side<=1;side+=2) {
            float a=xs[i],b=xs[i+1],wa=widths[i]*side,wb=widths[i+1]*side;
            float ya=11,yb=11;
            if(i==0&&!brokenFront)ya=side==1?7:10;
            if(i==xs.Length-2&&brokenFront)yb=side==1?10:7;
            Panel("Red lower hull",p,V(a,1,wa*.65f),V(b,1,wb*.65f),V(b,4,wb*.85f),V(a,4,wa*.85f),rust);
            Panel("Faceted blue steel hull",p,V(a,4,wa*.85f),V(b,4,wb*.85f),V(b,yb,wb),V(a,ya,wa),hull);
            Beam("Weathered gunwale",p,V(a,ya,wa),V(b,yb,wb),.45f,rust);
            if(side==1)Panel("Hold keel floor",p,V(a,1,-Mathf.Abs(wa)*.65f),V(a,1,Mathf.Abs(wa)*.65f),V(b,1,Mathf.Abs(wb)*.65f),V(b,1,-Mathf.Abs(wb)*.65f),steel);
        }
    }
    static void Container(Transform p,Vector3 pos,float yaw,Material mat) {
        var t=Group("Ribbed freight container",p);t.localPosition=pos;t.localRotation=Quaternion.Euler(0,yaw,0);t.localScale=Vector3.one/WreckScale;
        Box("Cargo body",t,V(0,2,0),V(11,4,4.7f),mat);
        for(int i=0;i<12;i++)foreach(int s in new[]{-1,1})Box("Raised corrugation",t,V(-5+i*.9f,2,s*2.4f),V(.12f,3.8f,.12f),mat,false);
        foreach(float x in new[]{-5.55f,5.55f}) {Box("Door seam",t,V(x,2,0),V(.08f,3.8f,.07f),steel,false);foreach(float z in new[]{-1.2f,1.2f})Box("Door locking bar",t,V(x,2,z),V(.1f,3.5f,.1f),rust,false);}
    }
    [MenuItem("Low Signal/Level 2/Build cargo ship wreck")]
    public static void Build() {
        Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();meshId=0;
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="05 - Cargo wreck - MERIDIAN");if(old)UnityEngine.Object.DestroyImmediate(old);
        hull=Mat("Deep blue hull",new Color(.105f,.19f,.22f));rust=Mat("Oxide red plating",new Color(.40f,.18f,.105f));deck=Mat("Silted deck",new Color(.32f,.33f,.25f));pale=Mat("Weathered ivory",new Color(.47f,.49f,.42f));glass=Mat("Dead bridge windows",new Color(.04f,.095f,.11f));steel=Mat("Exposed frames",new Color(.15f,.21f,.20f));green=Mat("Algae freight",new Color(.20f,.30f,.24f));
        // Expand the northeast basin while leaving the cave mouth and floor elevation intact.
        var basin=scene.GetRootGameObjects().Single(g=>g.name=="02 - Open seafloor basin").transform;
        void Resize(string name,Vector3 position,Vector3 size) {var t=basin.Find(name);t.position=position;t.localScale=size;}
        Resize("Continuous basin floor",V(77,-12,360),V(346,4,320));
        Resize("West basin shelf",V(-101,-1,360),V(20,28,330));
        Resize("East basin shelf",V(260,-1,360),V(20,28,330));
        Resize("Far basin escarpment",V(77,-1,529),V(386,28,18));
        var ship=Group("05 - Cargo wreck - MERIDIAN");ship.position=V(110,-10,400);ship.rotation=Quaternion.Euler(0,-18,0);ship.localScale=Vector3.one*WreckScale;
        var stern=Group("01 - Stern and accommodation",ship);stern.localRotation=Quaternion.Euler(-3,0,0);
        Hull(stern,new[]{-67f,-59,-40,-20,-8},new[]{10f,15,16,16,15},true);
        Panel("Stern transom",stern,V(-67,1,-6.5f),V(-67,1,6.5f),V(-67,11,10),V(-67,11,-10),hull);
        Box("Accommodation deck",stern,V(-49,10,0),V(35,1,28),deck);
        foreach(int s in new[]{-1,1})Box("Port and starboard passage",stern,V(-20,10,s*12.5f),V(24,.7f,5),deck);
        var bridge=Group("Accommodation tower",stern);
        Box("Lower house",bridge,V(-49,14,1),V(24,7,20),pale);
        Box("Upper house",bridge,V(-53,20,2),V(17,5,17),pale);
        Box("Wheelhouse",bridge,V(-50,25,0),V(20,5,21),pale);
        Box("Bridge roof",bridge,V(-50,27.8f,0),V(22,.7f,23),rust);
        for(int i=0;i<7;i++)foreach(int s in new[]{-1,1})Box("Bridge glazing",bridge,V(-58+i*2.7f,25.3f,s*10.55f),V(1.9f,2.1f,.12f),glass,false);
        for(int i=0;i<5;i++)Box("Forward glazing",bridge,V(-39.94f,25.3f,-8+i*4),V(.12f,2.1f,2.6f),glass,false);
        for(int i=0;i<7;i++)foreach(int s in new[]{-1,1})Box("Cabin porthole",bridge,V(-59+i*3.1f,14.6f,s*10.06f+1),V(.9f,1.3f,.12f),glass,false);
        Box("Funnel",stern,V(-60,28,4),V(5,9,6),hull);Box("Funnel rust band",stern,V(-60,31,4),V(5.1f,1.7f,6.1f),rust);
        Beam("Signal mast",stern,V(-48,28,0),V(-50,40,0),.55f,steel);Beam("Mast crossarm",stern,V(-49,35,-5),V(-49,35,5),.35f,steel);
        var bow=Group("02 - Broken forward cargo hull",ship);bow.localPosition=V(5,0,2);bow.localRotation=Quaternion.Euler(5,-5,0);
        Hull(bow,new[]{5f,19,39,54,65},new[]{15f,16,15,10,0},false);
        foreach(int s in new[]{-1,1})Box("Open hold side walkway",bow,V(29,10,s*12.4f),V(46,.7f,4.5f),deck);
        Box("Foredeck",bow,V(47,10,0),V(13,.8f,20),deck);
        Panel("Tapered forecastle",bow,V(53,10,-10),V(53,10,10),V(65,10,0),V(65,10,0),deck);
        foreach(float x in new[]{18f,37}) {
            foreach(int s in new[]{-1,1})Box("Cargo hatch coaming",bow,V(x,11,s*9.6f),V(16,1.5f,.5f),rust);
            foreach(float dx in new[]{-8f,8})Box("Cargo hatch end rim",bow,V(x+dx,11,0),V(.6f,1.5f,19.5f),rust);
        }
        for(int i=0;i<4;i++)Container(bow,V(14+i%2*13,1.6f,-5+i/2*9),0,i%2==0?rust:green);
        Container(stern,V(-23,1.6f,0),8,green);
        // Exposed ribs and ripped plates frame a broad swim-through amidships.
        foreach(var part in new[]{stern,bow})foreach(int s in new[]{-1,1}) {
            float x=part==stern?-10:7;
            Beam("Broken transverse rib",part,V(x,1,s*9),V(x+2,9,s*14),.65f,rust);
            Panel("Torn steel tooth",part,V(x,4,s*12),V(x,10,s*15),V(x+(part==stern?5:-5),7,s*14),V(x+(part==stern?3:-3),5,s*12),hull);
        }
        Cranes(bow);
        foreach(int s in new[]{-1,1})for(int i=0;i<8;i++) {
            float x=8+i*5;
            Beam("Deck rail post",bow,V(x,10,s*14.6f),V(x,12,s*14.6f),.15f,steel);
            if(i<7)Beam("Deck handrail",bow,V(x,12,s*14.6f),V(x+5,12,s*14.6f),.15f,steel);
        }
        var debris=Group("03 - Cargo spill and torn plates",ship);
        // Four irregular rows leave a wide central approach to the fractured hull.
        // Containers retain their original world dimensions as the ship gets larger.
        int cargoIndex=0;
        foreach(float z in new[]{-52f,-34,29,44})foreach(float x in new[]{-65f,-43,-21,21,43,65}) {
            int i=cargoIndex++;float offset=(i%3-1)*2;
            var pos=V(x,.05f,z+offset);float yaw=(i*37)%70-35;
            var material=i%3==0?rust:i%3==1?green:hull;
            Container(debris,pos,yaw,material);
            if(i%4==1)Container(debris,pos+V(0,4/WreckScale,0),yaw, i%2==0?green:rust);
        }
        for(int i=0;i<12;i++) {var t=Box("Scattered hull plating",debris,V(-19+i*3.7f,.4f,-18-(i%3)*7),V(2+i%3,.3f,3+i%4),i%2==0?rust:hull);t.localRotation=Quaternion.Euler(i%3*6,i*31,8);}
        // Remove only outcrops inside the ship/spill footprint, retaining the surrounding basin.
        var rocks=scene.GetRootGameObjects().FirstOrDefault(g=>g.name.StartsWith("03 -"));
        if(rocks)foreach(Transform t in rocks.transform.Cast<Transform>().ToArray()) {var p=ship.InverseTransformPoint(t.position);if(t.name.StartsWith("Basalt outcrop")&&p.x>-82&&p.x<81&&p.z>-62&&p.z<54)UnityEngine.Object.DestroyImmediate(t.gameObject);}
        var lighting=Group("04 - Filtered light over wreck",ship);
        foreach(float x in new[]{-48f,0,45}) {
            var t=Group("Soft cold overhead shaft",lighting);t.localPosition=V(x,48,-16);t.localRotation=Quaternion.Euler(72,0,0);
            var light=t.gameObject.AddComponent<Light>();light.type=LightType.Spot;light.color=new Color(.48f,.72f,.78f);light.intensity=1800*WreckScale*WreckScale;light.range=110*WreckScale;light.spotAngle=100;light.innerSpotAngle=65;light.shadows=LightShadows.Soft;
        }
        RenderSettings.fogDensity=.0085f;
        Physics.SyncTransforms();DeepReachCaveRevision.ValidateRoutes(scene);
        // Check the fracture and both open holds using a player-sized sphere.
        foreach(var p in new[]{ship.TransformPoint(V(0,5,0)),bow.TransformPoint(V(20,8,0)),stern.TransformPoint(V(-20,8,0))})
            if(Physics.OverlapSphere(p,1.1f).Any(c=>c.gameObject.scene==scene))throw new Exception("Cargo swim space obstructed at "+p);
        for(int z=-65;z<=0;z+=2) {
            var p=ship.TransformPoint(V(0,3,z));
            if(Physics.OverlapSphere(p,1.1f).Any(c=>c.gameObject.scene==scene))throw new Exception("Wreck approach obstructed at "+p);
        }
        foreach(var r in ship.GetComponentsInChildren<Renderer>()) {
            var b=r.bounds;
            if(b.min.x<-91||b.max.x>250||b.min.z<200||b.max.z>520)throw new Exception("Wreck outside basin: "+r.name);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        DeepReachHullRepair.Apply();
        Debug.Log("CARGO_SHIP_PASS: wreck enlarged 50 percent; 30 exterior containers; expanded basin bounds, cave routes, central approach and 2.2m cargo clearances verified.");
    }
    public static void BuildBatch(){try{Build();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void Capture(Transform ship) {
        Directory.CreateDirectory("Previews/DeepReach");var g=new GameObject("Temporary ship preview");var cam=g.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;cam.farClipPlane=500;cam.fieldOfView=60;
        Shot(cam,V(-25,96,235),ship.position+V(0,16,0),"Cargo wreck overview");
        Shot(cam,V(34,3,225),ship.position+V(0,12,0),"Cargo wreck cave reveal");
        Shot(cam,ship.TransformPoint(V(4,16,-47)),ship.TransformPoint(V(-5,10,0)),"Cargo wreck fracture");
        var bow=ship.Find("02 - Broken forward cargo hull");
        Shot(cam,bow.TransformPoint(V(3,29,-34)),bow.TransformPoint(V(32,19,0)),"Cargo cranes");
        UnityEngine.Object.DestroyImmediate(g);
    }
    static void Shot(Camera cam,Vector3 pos,Vector3 target,string name){cam.transform.position=pos;cam.transform.LookAt(target);var rt=RenderTexture.GetTemporary(1600,900,24);var old=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes("Previews/DeepReach/"+name+".png",tex.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}
}

