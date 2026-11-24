using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Broad flat fracture faces and narrow bevels, rather than tessellated spheres.
public static class DeepReachRockDetail
{
    const string Root="Assets/_LowSignal/World/DeepReach/RockFormations";
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static Bounds BoundsOf(Transform t) {var rr=t.GetComponentsInChildren<Renderer>();var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);return b;}
    static bool Near(Bounds a,Bounds b,float gap)=>a.min.x<b.max.x+gap&&a.max.x>b.min.x-gap&&a.min.z<b.max.z+gap&&a.max.z>b.min.z-gap;
    static Material Mat(string n,Color color) {
        string path=Root+"/"+n+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.color=color;m.SetFloat("_Smoothness",.08f);EditorUtility.SetDirty(m);return m;
    }
    static Mesh Rock(int seed) {
        string path=Root+"/Bevelled monolith "+seed+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
        var random=new System.Random(857+seed);float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        const int sides=7;var points=new List<Vector3>();var faces=new List<int[]>();
        var radii=new float[sides];for(int j=0;j<sides;j++)radii[j]=R(.74f,1.08f);
        for(int ring=0;ring<4;ring++)for(int j=0;j<sides;j++) {
            float angle=j*Mathf.PI*2/sides;float radius=new[]{.37f,.49f,.40f,.24f}[ring]*radii[j];
            float height=ring==0?0:ring==1?.10f+R(-.03f,.03f):ring==2?.70f+Mathf.Cos(angle+.7f)*.14f+R(-.04f,.04f):1+Mathf.Cos(angle-1.2f)*.12f+R(-.025f,.025f);
            points.Add(V(Mathf.Cos(angle)*radius+ring*.035f,height,Mathf.Sin(angle)*radius-ring*.018f));
        }
        faces.Add(Enumerable.Range(0,sides).Reverse().ToArray());faces.Add(Enumerable.Range(sides*3,sides).ToArray());
        for(int ring=0;ring<3;ring++)for(int j=0;j<sides;j++)faces.Add(new[]{ring*sides+j,ring*sides+(j+1)%sides,(ring+1)*sides+(j+1)%sides,(ring+1)*sides+j});
        var verts=new List<Vector3>();var sub=new[]{new List<int>(),new List<int>(),new List<int>(),new List<int>()};
        void Polygon(List<Vector3> poly,int material) {
            int index=verts.Count;verts.AddRange(poly);
            for(int j=1;j<poly.Count-1;j++)sub[material].AddRange(new[]{index,index+j,index+j+1});
        }
        var edges=new Dictionary<(int,int),List<(Vector3,Vector3)>>();var corners=new Dictionary<int,List<Vector3>>();
        foreach(var rawFace in faces) {
            var face=(int[])rawFace.Clone();var center=face.Aggregate(Vector3.zero,(a,b)=>a+points[b])/face.Length;
            var normal=Vector3.Cross(points[face[1]]-points[face[0]],points[face[2]]-points[face[0]]).normalized;
            if(Vector3.Dot(normal,center-V(0,.5f,0))<0)Array.Reverse(face);
            var inset=face.Select(i=>Vector3.Lerp(points[i],center,.09f)).ToArray();
            Polygon(inset.ToList(),center.y>.9f?3:random.Next(3));
            for(int j=0;j<face.Length;j++) {
                int a=face[j],b=face[(j+1)%face.Length];var key=(Mathf.Min(a,b),Mathf.Max(a,b));
                if(!edges.ContainsKey(key))edges[key]=new List<(Vector3,Vector3)>();
                edges[key].Add(a<b?(inset[j],inset[(j+1)%face.Length]):(inset[(j+1)%face.Length],inset[j]));
                if(!corners.ContainsKey(a))corners[a]=new List<Vector3>();corners[a].Add(inset[j]);
            }
        }
        void Outward(List<Vector3> poly,Vector3 direction) {if(Vector3.Dot(Vector3.Cross(poly[1]-poly[0],poly[2]-poly[0]),direction)<0)poly.Reverse();Polygon(poly,3);}
        foreach(var edge in edges) {
            if(edge.Value.Count!=2)throw new Exception("Open rock topology");
            var a=edge.Value[0];var b=edge.Value[1];Outward(new List<Vector3>{a.Item1,a.Item2,b.Item2,b.Item1},(points[edge.Key.Item1]+points[edge.Key.Item2])*.5f-V(0,.5f,0));
        }
        foreach(var corner in corners) {
            var center=corner.Value.Aggregate(Vector3.zero,(a,b)=>a+b)/corner.Value.Count;
            var normal=(points[corner.Key]-V(0,.5f,0)).normalized;var axis=Vector3.Cross(normal,Vector3.up).normalized;if(axis.sqrMagnitude<.01f)axis=Vector3.right;
            var right=Vector3.Cross(normal,axis);var ordered=corner.Value.OrderBy(p=>Mathf.Atan2(Vector3.Dot(p-center,right),Vector3.Dot(p-center,axis))).ToList();Outward(ordered,normal);
        }
        mesh.SetVertices(verts);mesh.subMeshCount=4;for(int i=0;i<4;i++)mesh.SetTriangles(sub[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
    }
    public static void Apply() {
        Directory.CreateDirectory(Root);AssetDatabase.Refresh();
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/_LowSignal/Scenes/DeepReach.unity");
        var terrain=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>()).Single();
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;
        var root=scene.GetRootGameObjects().Single(g=>g.name.StartsWith("03 -")).transform;
        var old=root.Cast<Transform>().Where(t=>t.name.StartsWith("Basalt outcrop")).ToArray();
        foreach(var t in old)UnityEngine.Object.DestroyImmediate(t.gameObject);
        var palette=new[]{Mat("Warm grey fracture faces",new Color(.29f,.29f,.25f)),Mat("Slate shadow faces",new Color(.22f,.25f,.25f)),Mat("Olive stone faces",new Color(.32f,.33f,.28f)),Mat("Pale bevels",new Color(.37f,.40f,.37f))};
        var meshes=Enumerable.Range(0,12).Select(Rock).ToArray();
        var obstacles=new List<Bounds>();
        foreach(var cargo in ship.GetComponentsInChildren<Transform>().Where(t=>t.name=="Ribbed freight container"))obstacles.Add(BoundsOf(cargo));
        foreach(var hull in ship.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("shell")||r.name.Contains("fitted deck")))obstacles.Add(hull.bounds);
        foreach(var plate in ship.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Scattered hull plating"))obstacles.Add(plate.bounds);
        // Irregular deposits: dense banks, broken shoals, and scattered solitary stones.
        // Collision rejection is per stone, allowing loose cluster envelopes to interleave.
        var occupied=new List<Bounds>();var random=new System.Random(19453);float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        int totalRocks=0;const int groupCount=28;
        for(int i=0;i<groupCount;i++) {
            bool dense=i<8,loose=i>=8&&i<18;
            int desired=dense?random.Next(12,23):loose?random.Next(4,10):random.Next(1,3);
            float radius=dense?R(17,29):loose?R(10,20):R(3,8);
            float heading=R(0,Mathf.PI*2),stretch=R(.5f,1.6f);
            Vector3 anchor=V(R(-66,218),0,R(251,502));
            var formation=new GameObject("Basalt outcrop "+(i+1)+" - "+(dense?"dense irregular bank":loose?"broken rock shoal":"isolated stones")).transform;
            formation.SetParent(root,false);formation.position=anchor;
            int accepted=0;
            for(int attempt=0;attempt<desired*120&&accepted<desired;attempt++) {
                float angle=R(0,Mathf.PI*2),distance=Mathf.Sqrt(R(0,1))*radius;
                float dx=Mathf.Cos(angle)*distance,dz=Mathf.Sin(angle)*distance*stretch;
                var pos=anchor+V(dx*Mathf.Cos(heading)-dz*Mathf.Sin(heading),0,dx*Mathf.Sin(heading)+dz*Mathf.Cos(heading));
                if(pos.x< -79||pos.x>233||pos.z<245||pos.z>508||(Mathf.Abs(pos.x-34)<12&&pos.z<302))continue;
                float width=R(2.2f,8.5f);if(random.NextDouble()<.12)width=R(9,13);
                float height=width*R(.45f,1.65f);if(random.NextDouble()<.15)height=width*R(1.7f,2.4f);
                var g=new GameObject("Fractured seabed rock "+(accepted+1));g.transform.SetParent(formation,false);g.transform.position=pos;
                g.transform.rotation=Quaternion.Euler(R(-13,13),R(0,360),R(-14,14));g.transform.localScale=V(width,height,width*R(.6f,1.25f));
                var mesh=meshes[random.Next(meshes.Length)];g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterials=palette;
                var footprint=BoundsOf(g.transform);
                if(footprint.min.x< -85||footprint.max.x>242||footprint.max.z>516||obstacles.Any(b=>Near(footprint,b,2.5f))||occupied.Any(b=>Near(footprint,b,.25f))) {UnityEngine.Object.DestroyImmediate(g);continue;}
                float y=float.NegativeInfinity;
                foreach(var vertex in mesh.vertices) {var p=g.transform.TransformPoint(vertex);y=Mathf.Max(y,terrain.transform.position.y+terrain.SampleHeight(p)-p.y);}
                g.transform.position+=Vector3.up*(y-.15f);
                var collider=g.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;g.isStatic=true;
                occupied.Add(footprint);accepted++;totalRocks++;
            }
            if(accepted==0)UnityEngine.Object.DestroyImmediate(formation.gameObject);
        }
        if(totalRocks<120)throw new Exception("Rock deposit distribution too sparse: "+totalRocks);
        Physics.SyncTransforms();Validate(ship,root);DeepReachCaveRevision.ValidateRoutes(scene);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);DeepReachCargoShip.Capture(ship);
        Capture(root.Cast<Transform>().Where(t=>t.name.StartsWith("Basalt outcrop")).OrderByDescending(t=>t.childCount).First());
        Debug.Log("ROCK_LAYOUT_PASS: "+totalRocks+" independently varied rocks in irregular banks, shoals and isolated deposits; all rock/cargo penetration checks passed; cave route clear.");
    }
    static void Validate(Transform ship,Transform rocks) {
        var cargo=ship.GetComponentsInChildren<Transform>().Where(t=>t.name=="Cargo body").Select(t=>t.GetComponent<BoxCollider>()).ToArray();
        bool Penetrates(Collider a,Collider b)=>Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float distance)&&distance>.025f;
        for(int i=0;i<cargo.Length;i++)for(int j=i+1;j<cargo.Length;j++)if(Penetrates(cargo[i],cargo[j]))throw new Exception("Overlapping cargo containers");
        var formations=rocks.Cast<Transform>().Where(t=>t.name.StartsWith("Basalt outcrop")).ToArray();
        var allRocks=formations.SelectMany(t=>t.GetComponentsInChildren<Collider>()).ToArray();
        for(int i=0;i<allRocks.Length;i++)for(int j=i+1;j<allRocks.Length;j++)
            if(allRocks[i].bounds.Intersects(allRocks[j].bounds)&&Penetrates(allRocks[i],allRocks[j]))throw new Exception("Rock deposits overlap");
        for(int i=0;i<formations.Length;i++) {
            var cols=formations[i].GetComponentsInChildren<Collider>();
            foreach(var col in cols)foreach(var box in cargo)if(Penetrates(col,box))throw new Exception("Rock overlaps cargo");
            for(int j=0;j<cols.Length;j++)for(int k=j+1;k<cols.Length;k++)if(Penetrates(cols[j],cols[k]))throw new Exception("Rock spires overlap");
        }
    }
    static void Capture(Transform rock) {
        var bounds=BoundsOf(rock);var cameraObject=new GameObject("Temporary rock preview");var cam=cameraObject.AddComponent<Camera>();
        float extent=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
        cam.transform.position=bounds.center+V(-extent*.75f,extent*.75f,-extent*1.1f);cam.transform.LookAt(bounds.center);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;
        var lamp=new GameObject("Temporary rock inspection light");var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.9f;light.color=new Color(.76f,.83f,.86f);lamp.transform.rotation=Quaternion.Euler(45,-30,0);
        var rt=RenderTexture.GetTemporary(1280,900,24);var previous=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(1280,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,900),0,0);tex.Apply();File.WriteAllBytes("Previews/DeepReach/Detailed rock formations.png",tex.EncodeToPNG());
        cam.transform.position=V(75,240,290);cam.transform.LookAt(V(75,0,375));cam.farClipPlane=600;cam.Render();tex.ReadPixels(new Rect(0,0,1280,900),0,0);tex.Apply();File.WriteAllBytes("Previews/DeepReach/Rock distribution.png",tex.EncodeToPNG());
        cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lamp);
    }
}

