using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Sediment contact patches follow the actual keel and cargo underside triangles.
public static class DeepReachGrounding
{
    sealed class Patch {public Vector3 a,b,c;public float margin;public Bounds bounds;}
    static readonly List<Patch> patches=new List<Patch>();
    static Transform ship;
    static float baseY;
    static float N(float x,float z,float s)=>Mathf.PerlinNoise(x/s+47.2f,z/s+13.8f);
    public static float Shape(float x,float z) {
        var p=ship.InverseTransformPoint(new Vector3(x,ship.position.y,z));
        float bank=Mathf.Exp(-Mathf.Pow((Mathf.Abs(p.z)-18)/12,2))*Mathf.Exp(-Mathf.Pow(p.x/83,4))*2.8f;
        float broad=1.3f+N(x,z,65)*4.8f+N(x+80,z,21)*2.2f;
        float ridges=Mathf.Sin(x*.11f+z*.055f+N(x,z,30)*4)*1.3f+N(x-70,z,9)*1.1f;
        float entrance=Mathf.SmoothStep(0,1,Mathf.InverseLerp(224,264,z));
        return .14f+entrance*(broad+ridges+bank);
    }
    static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
    static Vector3 Edge(Vector3 a,Vector3 b,Vector2 p) {var d=XZ(b-a);return Vector3.Lerp(a,b,Mathf.Clamp01(Vector2.Dot(p-XZ(a),d)/d.sqrMagnitude));}
    public static float Height(float x,float z) {
        float shape=Shape(x,z),weight=0,target=shape;var p=new Vector2(x,z);
        foreach(var t in patches) {
            if(x<t.bounds.min.x||x>t.bounds.max.x||z<t.bounds.min.z||z>t.bounds.max.z)continue;
            var a=XZ(t.a);var b=XZ(t.b);var c=XZ(t.c);
            float den=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);
            float u=((b.y-c.y)*(x-c.x)+(c.x-b.x)*(z-c.y))/den;
            float v=((c.y-a.y)*(x-c.x)+(a.x-c.x)*(z-c.y))/den;
            float w=1-u-v;Vector3 near;
            if(u>=0&&v>=0&&w>=0)near=t.a*u+t.b*v+t.c*w;
            else {var ab=Edge(t.a,t.b,p);var bc=Edge(t.b,t.c,p);var ca=Edge(t.c,t.a,p);near=ab;if((XZ(bc)-p).sqrMagnitude<(XZ(near)-p).sqrMagnitude)near=bc;if((XZ(ca)-p).sqrMagnitude<(XZ(near)-p).sqrMagnitude)near=ca;}
            float distance=Vector2.Distance(XZ(near),p);float blend=1-Mathf.SmoothStep(0,1,distance/t.margin);
            if(blend>weight) {weight=blend;target=near.y-baseY+.14f;}
        }
        return Mathf.Lerp(shape,target,weight);
    }
    static void Triangle(Vector3 a,Vector3 b,Vector3 c,float margin) {
        if(Mathf.Abs(Vector3.Cross(b-a,c-a).y)<.001f)return;
        var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);bounds.Expand(margin*2);
        patches.Add(new Patch{a=a,b=b,c=c,margin=margin,bounds=bounds});
    }
    static Vector3 Normal(float x,float z) {return new Vector3(Shape(x-.5f,z)-Shape(x+.5f,z),1,Shape(x,z-.5f)-Shape(x,z+.5f)).normalized;}
    static Vector3[] Bottom(Transform body) {
        return new[]{body.TransformPoint(new Vector3(-.5f,-.5f,-.5f)),body.TransformPoint(new Vector3(-.5f,-.5f,.5f)),body.TransformPoint(new Vector3(.5f,-.5f,.5f)),body.TransformPoint(new Vector3(.5f,-.5f,-.5f))};
    }
    public static void Prepare(Transform wreck,float bedY) {
        ship=wreck;baseY=bedY;patches.Clear();
        foreach(string name in new[]{"01 - Stern and accommodation","02 - Broken forward cargo hull"}) {
            var part=ship.Find(name);var local=part.localPosition;local.y=0;part.localPosition=local;
            var meshFilter=part.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.EndsWith("sealed lower shell"));
            var mesh=meshFilter.sharedMesh;var verts=mesh.vertices;var tris=mesh.triangles;
            var bottoms=new List<Vector3>();
            for(int i=0;i<tris.Length;i+=3) {var a=meshFilter.transform.TransformPoint(verts[tris[i]]);var b=meshFilter.transform.TransformPoint(verts[tris[i+1]]);var c=meshFilter.transform.TransformPoint(verts[tris[i+2]]);
                if(Vector3.Cross(b-a,c-a).normalized.y<-.8f)bottoms.AddRange(new[]{a,b,c});}
            if(bottoms.Count==0)throw new Exception("Missing keel surface on "+name);
            var center=bottoms.Aggregate(Vector3.zero,(a,b)=>a+b)/bottoms.Count;
            float shift=baseY+Shape(center.x,center.z)-center.y;
            part.position+=Vector3.up*shift;
            int holdCargo=0;
            foreach(var container in part.Cast<Transform>().Where(t=>t.name=="Ribbed freight container")) {
                container.localPosition=name.StartsWith("02")?new Vector3(holdCargo%2==0?18:37,1,holdCargo/2==0?-4:4):new Vector3(-23,1,0);
                container.localRotation=Quaternion.identity;holdCargo++;
            }
            for(int i=0;i<bottoms.Count;i+=3)Triangle(bottoms[i]+Vector3.up*shift,bottoms[i+1]+Vector3.up*shift,bottoms[i+2]+Vector3.up*shift,9);
        }
        var debris=ship.Find("03 - Cargo spill and torn plates");
        var cargo=debris.Cast<Transform>().Where(t=>t.name=="Ribbed freight container").ToArray();
        // Reconstruct the authored stacks explicitly: tilted stack offsets change world X/Z,
        // so grouping by world position on subsequent runs would collapse the upper boxes.
        int cargoIndex=0;
        foreach(float z in new[]{-52f,-34,29,44})foreach(float x in new[]{-65f,-43,-21,21,43,65}) {
            int cell=Array.IndexOf(new[]{-52f,-34,29,44},z)*6+Array.IndexOf(new[]{-65f,-43,-21,21,43,65},x);
            var bottom=cargo[cargoIndex++];var body=bottom.Find("Cargo body");
            bottom.localPosition=new Vector3(x,0,z+(cell%3-1)*2);bottom.localRotation=Quaternion.Euler(0,(cell*37)%70-35,0);
            bottom.rotation=Quaternion.FromToRotation(Vector3.up,Normal(bottom.position.x,bottom.position.z))*Quaternion.Euler(0,bottom.eulerAngles.y,0);
            var points=Bottom(body);float average=points.Average(p=>p.y);float floor=baseY+Shape(bottom.position.x,bottom.position.z);
            bottom.position+=Vector3.up*(floor-average);
            points=Bottom(body);Triangle(points[0],points[1],points[2],3);Triangle(points[0],points[2],points[3],3);
            if(cell%4==1) {var upper=cargo[cargoIndex++];upper.rotation=bottom.rotation;upper.position=bottom.position+bottom.up*4;}
        }
        foreach(var plate in debris.Cast<Transform>().Where(t=>t.name=="Scattered hull plating")) {
            plate.rotation=Quaternion.FromToRotation(Vector3.up,Normal(plate.position.x,plate.position.z))*Quaternion.Euler(0,plate.eulerAngles.y,0);
            var points=Bottom(plate);float floor=baseY+Shape(plate.position.x,plate.position.z);
            plate.position+=Vector3.up*(floor-points.Average(p=>p.y));
            points=Bottom(plate);Triangle(points[0],points[1],points[2],1.5f);Triangle(points[0],points[2],points[3],1.5f);
        }
    }
    public static int SettleLoose(UnityEngine.SceneManagement.Scene scene,Terrain terrain) {
        float Ground(Vector3 p)=>terrain.transform.position.y+terrain.SampleHeight(p);
        int count=0;
        var rocks=scene.GetRootGameObjects().FirstOrDefault(g=>g.name.StartsWith("03 -"));
        var debris=ship.Find("03 - Cargo spill and torn plates");
        var objects=rocks?rocks.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Basalt outcrop")):Enumerable.Empty<Transform>();
        foreach(var t in objects) {
            var mf=t.GetComponent<MeshFilter>();if(!mf)continue;
            float correction=float.NegativeInfinity;
            // Lowest contact over the footprint; bury a small amount to avoid visible air gaps.
            foreach(var vertex in mf.sharedMesh.vertices) {var p=t.TransformPoint(vertex);correction=Mathf.Max(correction,Ground(p)-p.y);}
            t.position+=Vector3.up*(correction-.06f);count++;
        }
        // Validate all lower hull vertices and container corners against the baked heightmap.
        float worstGap=0;
        foreach(var p in patches)foreach(var point in new[]{p.a,p.b,p.c})worstGap=Mathf.Max(worstGap,point.y-Ground(point));
        if(worstGap>.06f)throw new Exception("Ground contact gap exceeds tolerance: "+worstGap);
        Debug.Log("GROUND_CONTACT_PASS: hulls, cargo and scrap plates supported; "+count+" rocks settled; maximum underside corner gap "+worstGap.ToString("F3")+"m.");
        return count;
    }
}
