using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Authored cliff volumes, an exact cave-mouth collar, and a painted terrain transition.
public static class DeepReachCliffs
{
    const string Root="Assets/_LowSignal/World/DeepReach/Cliffs";
    const string GroupName="07 - Basin cliffs and cave exit gorge";
    const float Pullback=65;
    static Terrain terrain;
    static Material[] materials;
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static float Noise(float a,float b)=>Mathf.PerlinNoise(a+67.1f,b+24.7f);
    static float Ground(float x,float z) {var p=terrain.transform.position;var size=terrain.terrainData.size;return p.y+terrain.terrainData.GetInterpolatedHeight(Mathf.Clamp01((x-p.x)/size.x),Mathf.Clamp01((z-p.z)/size.z));}
    static float Center(float z)=>z<265?34+Mathf.Sin((z-220)*.075f)*2:Mathf.Lerp(34,65,Mathf.SmoothStep(0,1,Mathf.InverseLerp(265,310,z)));
    static float Width(float z)=>z<244?Mathf.Lerp(16,7,Mathf.SmoothStep(0,1,Mathf.InverseLerp(220,244,z))):z<276?7:Mathf.Lerp(7,43,Mathf.SmoothStep(0,1,Mathf.InverseLerp(276,310,z)));
    sealed class Geometry {
        readonly List<Vector3> verts=new List<Vector3>();readonly List<int>[] tris={new List<int>(),new List<int>(),new List<int>()};
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material=0) {
            int n=verts.Count;verts.AddRange(new[]{a,b,c,d});
            if(Vector3.Cross(b-a,c-a).sqrMagnitude>.00001f)tris[material].AddRange(new[]{n,n+1,n+2});
            if(Vector3.Cross(c-a,d-a).sqrMagnitude>.00001f)tris[material].AddRange(new[]{n,n+2,n+3});
        }
        public GameObject Save(string name,Transform parent) {
            if(name=="Solid cave mouth surround"||name=="Continuous gorge and exterior cliffs") {
                var ids=new Dictionary<Vector3Int,int>();var welded=new int[verts.Count];
                for(int i=0;i<verts.Count;i++){var v=verts[i];var key=new Vector3Int(Mathf.RoundToInt(v.x*10000),Mathf.RoundToInt(v.y*10000),Mathf.RoundToInt(v.z*10000));if(!ids.TryGetValue(key,out int id)){id=ids.Count;ids[key]=id;}welded[i]=id;}
                var edges=new Dictionary<(int,int),int>();
                foreach(var sub in tris)for(int i=0;i<sub.Count;i+=3)for(int j=0;j<3;j++){int a=welded[sub[i+j]],b=welded[sub[i+(j+1)%3]];var key=(Math.Min(a,b),Math.Max(a,b));edges.TryGetValue(key,out int count);edges[key]=count+1;}
                if(edges.Values.Any(n=>n!=2))throw new Exception("Open edge in "+name);
                Debug.Log("CLIFF_SEAMS_PASS: all edges shared by exactly two triangles in "+name);
            }
            string path=Root+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.SetVertices(verts);mesh.subMeshCount=3;
            for(int i=0;i<3;i++)mesh.SetTriangles(tris[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;return go;
        }
    }
    static void Wall(string name,Transform parent,List<Vector3> line,Vector3 outward,float seed,bool gorge) {
        if(outward.x<0||outward.z>0)line.Reverse();
        var g=new Geometry();var profiles=new List<Vector3[]>();
        for(int i=0;i<line.Count;i++) {
            var p=line[i];float height=(gorge?42:50)+Noise(i*.2f,seed)*19;
            if(gorge)height*=Mathf.Lerp(1,.12f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(280,310,p.z+Pullback)));
            float bottom=Ground(p.x,p.z)-1.4f;
            var profile=new List<Vector3>();
            // Stepped ledges and offset strata make substantial crags with undercuts.
            float[] tiers={0,.08f,.14f,.17f,.36f,.39f,.43f,.64f,.69f,.88f,1};
            float[] shifts={0,-.7f,-1.1f,1.9f,2.8f,.8f,2.7f,3.8f,1.6f,4,7};
            for(int j=0;j<tiers.Length;j++) {
                float irregular=(Noise(i*.5f+j*.4f,seed)-.5f)*3;
                profile.Add(p+outward*(shifts[j]+irregular)+Vector3.up*(bottom+height*tiers[j]));
            }
            profile.Add(p+outward*23+Vector3.up*(bottom+height*.83f));profile.Add(p+outward*26+Vector3.up*(bottom-3));profiles.Add(profile.ToArray());
        }
        for(int i=1;i<profiles.Count;i++)for(int j=0;j<profiles[i].Length;j++) {
            int k=(j+1)%profiles[i].Length;g.Quad(profiles[i-1][j],profiles[i][j],profiles[i][k],profiles[i-1][k],j>=8?1:j%3==2?2:0);
        }
        foreach(int i in new[]{0,profiles.Count-1}) {
            var ring=profiles[i];var center=ring.Aggregate(Vector3.zero,(a,b)=>a+b)/ring.Length;
            for(int j=0;j<ring.Length;j++)g.Quad(center,ring[j],ring[(j+1)%ring.Length],center,0);
        }
        g.Save(name,parent);
    }
    static void Enclosure(Transform parent) {
        var line=new List<Vector3>();
        void Add(Vector3 p){if(line.Count==0||(line.Last()-p).sqrMagnitude>.001f)line.Add(p);}
        void Straight(Vector3 end) {var start=line.Last();int n=Mathf.CeilToInt(Vector3.Distance(start,end)/4);for(int i=1;i<=n;i++)Add(Vector3.Lerp(start,end,i/(float)n));}
        List<Vector3> Curve(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int n) {
            var points=new List<Vector3>();for(int i=0;i<=n;i++){float t=i/(float)n,u=1-t;points.Add(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d);}return points;
        }
        var left=new List<Vector3>();var right=new List<Vector3>();
        for(float z=218;z<=275;z+=3){left.Add(V(Center(z)-Width(z),0,z-Pullback));right.Add(V(Center(z)+Width(z),0,z-Pullback));}
        foreach(var p in left)Add(p);
        var l=line.Last();foreach(var p in Curve(l,l+V(0,0,18),V(-88,0,237),V(-88,0,280),40))Add(p);
        Straight(V(-88,0,494));foreach(var p in Curve(line.Last(),V(-88,0,518),V(-72,0,518),V(-64,0,518),12))Add(p);
        Straight(V(222,0,518));foreach(var p in Curve(line.Last(),V(248,0,518),V(248,0,508),V(248,0,492),12))Add(p);
        Straight(V(248,0,290));
        var r=right.Last();foreach(var p in Curve(r,r+V(7,0,19),V(248,0,238),V(248,0,290),50).AsEnumerable().Reverse())Add(p);
        foreach(var p in right.AsEnumerable().Reverse())Add(p);
        var geometry=new Geometry();var profiles=new List<Vector3[]>();
        float[] tiers={0,.08f,.14f,.17f,.36f,.39f,.43f,.64f,.69f,.88f,1};
        float[] shifts={0,-.7f,-1.1f,1.9f,2.8f,.8f,2.7f,3.8f,1.6f,4,7};
        for(int i=0;i<line.Count;i++) {
            var p=line[i];var tangent=(line[Mathf.Min(i+1,line.Count-1)]-line[Mathf.Max(i-1,0)]).normalized;
            var outward=Vector3.Cross(tangent,Vector3.up).normalized;
            // The same height and strata continue around the complete basin boundary.
            float height=49+Noise(p.x*.035f,p.z*.032f)*16,bottom=Ground(p.x,p.z)-1.5f;
            var ring=new List<Vector3>();
            for(int j=0;j<tiers.Length;j++) {
                float irregular=(Noise(p.x*.12f+j*.4f,p.z*.12f)-.5f)*2.5f;
                ring.Add(p+outward*(shifts[j]+irregular)+Vector3.up*(bottom+height*tiers[j]));
            }
            ring.Add(p+outward*23+Vector3.up*(bottom+height*.85f));ring.Add(p+outward*26+Vector3.up*(bottom-3));profiles.Add(ring.ToArray());
        }
        for(int i=1;i<profiles.Count;i++)for(int j=0;j<profiles[i].Length;j++) {
            int k=(j+1)%profiles[i].Length;geometry.Quad(profiles[i-1][k],profiles[i][k],profiles[i][j],profiles[i-1][j],j>=8?1:j%3==2?2:0);
        }
        foreach(int i in new[]{0,profiles.Count-1}) {
            var ring=profiles[i];var center=ring.Aggregate(Vector3.zero,(a,b)=>a+b)/ring.Length;
            for(int j=0;j<ring.Length;j++){int k=(j+1)%ring.Length;if(i==0)geometry.Quad(center,ring[j],ring[k],center);else geometry.Quad(center,ring[k],ring[j],center);}
        }
        geometry.Save("Continuous gorge and exterior cliffs",parent);
        // Both flared shoulders must stay tall, rather than becoming freestanding ramps.
        for(int i=0;i<line.Count;i++)if(line[i].z>210&&line[i].z<291&&profiles[i][10].y-profiles[i][0].y<45)throw new Exception("Cliff shoulder drops below enclosing wall height");
    }
    static void BlendFloor(Vector3[] rim) {
        var data=terrain.terrainData;int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);var before=(float[,])heights.Clone();
        var bottom=rim.Where(v=>v.y< -1).OrderBy(v=>v.x).ToArray();
        float Mouth(float x) {
            for(int i=1;i<bottom.Length;i++)if(x<=bottom[i].x)return Mathf.Lerp(bottom[i-1].y,bottom[i].y,Mathf.InverseLerp(bottom[i-1].x,bottom[i].x,x));
            return bottom.Last().y;
        }
        for(int iz=0;iz<n;iz++)for(int ix=0;ix<n;ix++) {
            float x=terrain.transform.position.x+ix*data.size.x/(n-1),z=terrain.transform.position.z+iz*data.size.z/(n-1)+Pullback;
            if(z<220||z>312)continue;
            float distance=Mathf.Abs(x-Center(z)),width=Width(z);if(distance>width+9)continue;
            float old=terrain.transform.position.y+heights[iz,ix]*data.size.y;
            float transition=Mathf.SmoothStep(0,1,Mathf.InverseLerp(220,298,z));
            float floor=Mathf.Lerp(-9.96f,old,transition);
            float shoulder=Mathf.SmoothStep(0,1,Mathf.InverseLerp(width*.5f,width+3,distance))*2.5f;
            float mouth=Mouth(Mathf.Clamp(34+(x-Center(z))*16/width,18.1f,49.9f));
            float desired=Mathf.Lerp(mouth,floor+shoulder,Mathf.SmoothStep(0,1,Mathf.InverseLerp(220,243,z)));
            float edge=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(width+2,width+9,distance));
            float end=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(296,312,z));
            heights[iz,ix]=Mathf.Clamp01((Mathf.Lerp(old,desired,edge*end)-terrain.transform.position.y)/data.size.y);
        }
        data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);
        float Sample(float[,] hh,float x,float z) {
            float fx=Mathf.Clamp01((x-terrain.transform.position.x)/data.size.x)*(n-1),fz=Mathf.Clamp01((z-terrain.transform.position.z)/data.size.z)*(n-1);
            int ix=Mathf.Min(n-2,Mathf.FloorToInt(fx)),iz=Mathf.Min(n-2,Mathf.FloorToInt(fz));
            return Mathf.Lerp(Mathf.Lerp(hh[iz,ix],hh[iz,ix+1],fx-ix),Mathf.Lerp(hh[iz+1,ix],hh[iz+1,ix+1],fx-ix),fz-iz)*data.size.y;
        }
        // Preserve the gravel's contact with the newly sloped entrance floor.
        foreach(var filter in terrain.transform.parent.GetComponentsInChildren<MeshFilter>()) {
            var mesh=filter.sharedMesh;var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++) {var p=filter.transform.TransformPoint(vertices[i]);p.y+=Sample(heights,p.x,p.z)-Sample(before,p.x,p.z);vertices[i]=filter.transform.InverseTransformPoint(p);}
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        }
    }
    static void ExtendSeabed(UnityEngine.SceneManagement.Scene scene) {
        if(terrain.transform.position.z<150) {
            var shipRoot=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;DeepReachGrounding.Prepare(shipRoot,terrain.transform.position.y);
            var td=terrain.terrainData;int resolution=td.heightmapResolution;var h=td.GetHeights(0,0,resolution,resolution);
            var details=terrain.transform.parent.GetComponentsInChildren<MeshFilter>();
            var previous=details.Select(f=>f.sharedMesh.vertices.Select(v=>{var p=f.transform.TransformPoint(v);return Ground(p.x,p.z);}).ToArray()).ToArray();
            for(int iz=0;iz<resolution;iz++)for(int ix=0;ix<resolution;ix++) {
                float z=terrain.transform.position.z+iz*td.size.z/(resolution-1);if(z>340)continue;
                float x=terrain.transform.position.x+ix*td.size.x/(resolution-1);
                float baseline=DeepReachGrounding.Height(x,z)/td.size.y;float blend=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(326,340,z));h[iz,ix]=Mathf.Lerp(h[iz,ix],baseline,blend);
            }
            td.SetHeights(0,0,h);terrain.Flush();EditorUtility.SetDirty(td);
            for(int i=0;i<details.Length;i++) {var mesh=details[i].sharedMesh;var vertices=mesh.vertices;for(int j=0;j<vertices.Length;j++){var p=details[i].transform.TransformPoint(vertices[j]);p.y+=Ground(p.x,p.z)-previous[i][j];vertices[j]=details[i].transform.InverseTransformPoint(p);}mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);}
            return;
        }
        var original=terrain.terrainData;var origin=terrain.transform.position;var size=original.size;
        int n=original.heightmapResolution,a=original.alphamapResolution;var hh=original.GetHeights(0,0,n,n);var aa=original.GetAlphamaps(0,0,a,a);
        float OldHeight(float x,float z) {
            float fx=Mathf.Clamp01((x-origin.x)/size.x)*(n-1),fz=Mathf.Clamp01((z-origin.z)/size.z)*(n-1);int ix=Mathf.Min(n-2,(int)fx),iz=Mathf.Min(n-2,(int)fz);
            return origin.y+Mathf.Lerp(Mathf.Lerp(hh[iz,ix],hh[iz,ix+1],fx-ix),Mathf.Lerp(hh[iz+1,ix],hh[iz+1,ix+1],fx-ix),fz-iz)*size.y;
        }
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;
        DeepReachGrounding.Prepare(ship,origin.y);
        string path=Root+"/Extended seabed.asset";var data=AssetDatabase.LoadAssetAtPath<TerrainData>(path);if(!data){data=new TerrainData();AssetDatabase.CreateAsset(data,path);}
        data.heightmapResolution=1025;data.alphamapResolution=512;data.baseMapResolution=1024;data.size=V(size.x,size.y,390);data.terrainLayers=original.terrainLayers;
        var heights=new float[1025,1025];
        for(int z=0;z<1025;z++)for(int x=0;x<1025;x++) {
            float wx=origin.x+x*size.x/1024,wz=130+z*390f/1024;
            float y=wz<origin.z?origin.y+.14f:OldHeight(wx,wz);
            // Restore the old gorge footprint to the surrounding natural seabed.
            if(wz>=200&&wz<=330)y=origin.y+DeepReachGrounding.Height(wx,wz);
            heights[z,x]=(y-origin.y)/size.y;
        }
        data.SetHeights(0,0,heights);
        var alpha=new float[512,512,3];
        for(int z=0;z<512;z++)for(int x=0;x<512;x++) {
            float wx=origin.x+x*size.x/511,wz=130+z*390f/511;
            if(wz<origin.z){float sand=.3f+Noise(wx*.035f,wz*.035f)*.4f;alpha[z,x,0]=1-sand;alpha[z,x,1]=sand;continue;}
            float fx=x*(a-1)/511f,fz=Mathf.Clamp01((wz-origin.z)/size.z)*(a-1);int ix=Mathf.Min(a-2,(int)fx),iz=Mathf.Min(a-2,(int)fz);
            for(int l=0;l<3;l++)alpha[z,x,l]=Mathf.Lerp(Mathf.Lerp(aa[iz,ix,l],aa[iz,ix+1,l],fx-ix),Mathf.Lerp(aa[iz+1,ix,l],aa[iz+1,ix+1,l],fx-ix),fz-iz);
        }
        data.SetAlphamaps(0,0,alpha);EditorUtility.SetDirty(data);
        terrain.terrainData=data;terrain.GetComponent<TerrainCollider>().terrainData=data;terrain.transform.position=V(origin.x,origin.y,130);Physics.SyncTransforms();terrain.Flush();
        foreach(var filter in terrain.transform.parent.GetComponentsInChildren<MeshFilter>()) {
            var mesh=filter.sharedMesh;var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++){var p=filter.transform.TransformPoint(vertices[i]);p.y+=Ground(p.x,p.z)-OldHeight(p.x,p.z);vertices[i]=filter.transform.InverseTransformPoint(p);}
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        }
        var floor=scene.GetRootGameObjects().Single(g=>g.name=="02 - Open seafloor basin").transform.Find("Continuous basin floor");
        var pos=floor.position;pos.z=325;floor.position=pos;var scale=floor.localScale;scale.z=390;floor.localScale=scale;
    }
    [MenuItem("Low Signal/Level 2/Build basin cliffs and cave gorge")]
    public static void Apply() {
        Directory.CreateDirectory(Root);AssetDatabase.Refresh();
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/_LowSignal/Scenes/DeepReach.unity");if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/DeepReach.unity");
        terrain=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>()).Single();
        var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GroupName);if(old)UnityEngine.Object.DestroyImmediate(old);
        var root=new GameObject(GroupName).transform;
        materials=new Material[3];var colors=new[]{new Color(.19f,.25f,.25f),new Color(.31f,.36f,.33f),new Color(.13f,.20f,.19f)};
        for(int i=0;i<3;i++) {
            string path=Root+"/Weathered cliff "+i+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.color=colors[i];m.SetFloat("_Smoothness",.08f);m.SetFloat("_Cull",0);EditorUtility.SetDirty(m);materials[i]=m;
        }
        var caveRoot=scene.GetRootGameObjects().Single(g=>g.name=="01 - Winding entrance cave").transform;
        var offset=V(0,0,-Pullback)-caveRoot.position;caveRoot.position+=offset;
        foreach(var g in scene.GetRootGameObjects().Where(g=>g.GetComponent<UnderwaterSwimController>()||g.name.StartsWith("04 -")))g.transform.position+=offset;
        ExtendSeabed(scene);
        var cave=caveRoot.GetComponentInChildren<MeshFilter>();
        var rim=cave.sharedMesh.vertices.Where(p=>p.z>219.99f).Select(cave.transform.TransformPoint).Distinct().ToArray();if(rim.Length<20)throw new Exception("Cave exit boundary not found");
        BlendFloor(rim);
        var collar=new Geometry();var center=V(34,-1.25f,220-Pullback);var ordered=rim.OrderBy(p=>Mathf.Atan2(p.y-center.y,p.x-center.x)).ToArray();
        for(int i=0;i<ordered.Length;i++) {
            int j=(i+1)%ordered.Length;
            Vector3 Outer(Vector3 p) {var d=p-center;float scale=Mathf.Min(40/Mathf.Max(.001f,Mathf.Abs(d.x)),(d.y>0?54.25f:18.75f)/Mathf.Max(.001f,Mathf.Abs(d.y)));return center+V(d.x*scale,d.y*scale,-1.2f);}
            var a=ordered[i];var b=ordered[j];var A=Outer(a);var B=Outer(b);var back=V(0,0,-5);
            collar.Quad(a,b,B,A,i%9==0?1:0);collar.Quad(A,B,B+back,A+back);collar.Quad(a+back,b+back,b,a);collar.Quad(a+back,A+back,B+back,b+back);
        }
        collar.Save("Solid cave mouth surround",root);
        var apron=new Geometry();var lower=rim.Where(p=>p.y<=center.y).GroupBy(p=>Mathf.RoundToInt(p.x*1000)).Select(g=>g.OrderBy(p=>p.y).First()).OrderBy(p=>p.x).ToArray();
        for(int i=1;i<lower.Length;i++){var a=lower[i-1];var b=lower[i];var A=V(a.x,Ground(a.x,225-Pullback)-.03f,225-Pullback);var B=V(b.x,Ground(b.x,225-Pullback)-.03f,225-Pullback);apron.Quad(a,A,B,b);}
        apron.Save("Cave floor seam apron",root);
        Enclosure(root);
        var basin=scene.GetRootGameObjects().Single(g=>g.name=="02 - Open seafloor basin").transform;
        foreach(string name in new[]{"West basin shelf","East basin shelf","Far basin escarpment"}) {var t=basin.Find(name);if(t)t.gameObject.SetActive(false);}
        foreach(float z in new[]{247f,278}) {
            var lightObject=new GameObject("Filtered light into the gorge");lightObject.transform.SetParent(root,false);lightObject.transform.position=V(Center(z),38,z-Pullback);lightObject.transform.rotation=Quaternion.Euler(90,0,0);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Spot;light.color=new Color(.40f,.68f,.72f);light.intensity=4500;light.range=100;light.spotAngle=85;light.innerSpotAngle=45;light.shadows=LightShadows.Soft;
        }
        Physics.SyncTransforms();SettleAndClear(scene,root);
        DeepReachCaveRevision.ValidateRoutes(scene);
        // Sweep the whole cave-to-basin channel using player-sized clearance.
        Vector3 previous=V(34,-4,219-Pullback);
        for(float z=220;z<=316;z+=1) {
            var p=V(Center(Mathf.Min(z,310)),Ground(Center(Mathf.Min(z,310)),z-Pullback)+3,z-Pullback);var delta=p-previous;
            foreach(var hit in Physics.SphereCastAll(previous,1.1f,delta.normalized,delta.magnitude))if(hit.collider.gameObject.scene==scene&&hit.collider.GetComponentInParent<UnderwaterSwimController>()==null){Capture();throw new Exception("Gorge route blocked by "+hit.collider.name+" at "+p+" from "+previous+" hit "+hit.point+" ground "+Ground(p.x,p.z)+" nearby "+Ground(p.x-1,p.z)+", "+Ground(p.x+1,p.z));}
            previous=p;
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Capture();
        Debug.Log("DEEP_REACH_CLIFFS_PASS: cave and gorge pulled back 65m; solid cave surround and floor apron saved; cave and gorge clearances passed.");
    }
    static void SettleAndClear(UnityEngine.SceneManagement.Scene scene,Transform cliffs) {
        var cliffCols=cliffs.GetComponentsInChildren<Collider>();
        var rocks=scene.GetRootGameObjects().Single(g=>g.name.StartsWith("03 -")).GetComponentsInChildren<MeshCollider>();
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN");var cargo=ship.GetComponentsInChildren<BoxCollider>();
        var hullBounds=ship.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("shell")||r.name.Contains("fitted deck")).Select(r=>r.bounds).ToArray();
        var random=new System.Random(5251);float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        bool Overlap(Collider a,Collider b)=>a.bounds.Intersects(b.bounds)&&Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float depth)&&depth>.03f;
        foreach(var rock in rocks) {
            var t=rock.transform;var bounds=rock.bounds;
            if(bounds.min.z<345) {
                float dy=float.NegativeInfinity;foreach(var v in rock.sharedMesh.vertices){var p=t.TransformPoint(v);dy=Mathf.Max(dy,Ground(p.x,p.z)-p.y);}t.position+=Vector3.up*(dy-.15f);
            }
            Physics.SyncTransforms();
            bool Blocked()=>cliffCols.Any(c=>Overlap(rock,c))||cargo.Any(c=>Overlap(rock,c));
            if(!Blocked())continue;
            bool clear=false;
            for(int attempt=0;attempt<500;attempt++) {
                var p=t.position;t.position=V(R(-73,228),0,R(325,501));float dy=float.NegativeInfinity;
                foreach(var v in rock.sharedMesh.vertices){var point=t.TransformPoint(v);dy=Mathf.Max(dy,Ground(point.x,point.z)-point.y);}t.position+=Vector3.up*(dy-.15f);Physics.SyncTransforms();
                var rb=rock.bounds;
                if(Blocked()||hullBounds.Any(b=>rb.min.x<b.max.x+3&&rb.max.x>b.min.x-3&&rb.min.z<b.max.z+3&&rb.max.z>b.min.z-3)||rocks.Any(other=>other!=rock&&Overlap(rock,other)))continue;
                clear=true;break;
            }
            if(!clear)throw new Exception("Cannot clear cliff overlap for "+t.name);
        }
        foreach(var box in cargo)foreach(var wall in cliffCols)if(Overlap(box,wall))throw new Exception("Cliff overlaps cargo "+box.name);
    }
    static void Capture() {
        Directory.CreateDirectory("Previews/DeepReach");var g=new GameObject("Temporary gorge preview");var cam=g.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;cam.farClipPlane=500;cam.fieldOfView=72;
        var rt=RenderTexture.GetTemporary(1600,900,24);var old=RenderTexture.active;var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);
        foreach(var shot in new[]{("Cave exit gorge",V(34,-3,224-Pullback),V(Center(253),5,253-Pullback)),("Gorge into basin",V(Center(280),Ground(Center(280),280-Pullback)+5,280-Pullback),V(75,10,340)),("Cave cliff exterior",V(Center(252),2,252-Pullback),V(34,-1,220-Pullback))}) {
            cam.transform.position=shot.Item2;cam.transform.LookAt(shot.Item3);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes("Previews/DeepReach/"+shot.Item1+".png",tex.EncodeToPNG());
        }
        // Authoring overview with temporary inspection light; scene lighting is preserved.
        bool fog=RenderSettings.fog;RenderSettings.fog=false;
        var lamp=new GameObject("Temporary enclosure inspection light");var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.8f;lamp.transform.rotation=Quaternion.Euler(55,-30,0);
        cam.transform.position=V(80,265,65);cam.transform.LookAt(V(80,0,330));cam.farClipPlane=800;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes("Previews/DeepReach/Continuous cliff enclosure.png",tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(lamp);RenderSettings.fog=fog;
        cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(g);
    }
}

