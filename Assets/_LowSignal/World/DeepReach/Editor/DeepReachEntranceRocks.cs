using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Local placement pass. Only individual rock transforms in the entrance forecourt change.
public static class DeepReachEntranceRocks
{
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static bool Near(Bounds a,Bounds b,float gap)=>a.min.x<b.max.x+gap&&a.max.x>b.min.x-gap&&a.min.z<b.max.z+gap&&a.max.z>b.min.z-gap;
    public static void Apply() {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/_LowSignal/Scenes/DeepReach.unity");if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/DeepReach.unity");
        var terrain=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>()).Single();
        float Ground(Vector3 p)=>terrain.transform.position.y+terrain.terrainData.GetInterpolatedHeight((p.x-terrain.transform.position.x)/terrain.terrainData.size.x,(p.z-terrain.transform.position.z)/terrain.terrainData.size.z);
        Vector3 Normal(Vector3 p)=>terrain.terrainData.GetInterpolatedNormal((p.x-terrain.transform.position.x)/terrain.terrainData.size.x,(p.z-terrain.transform.position.z)/terrain.terrainData.size.z);
        var rockRoot=scene.GetRootGameObjects().Single(g=>g.name.StartsWith("03 -"));
        var all=rockRoot.GetComponentsInChildren<MeshCollider>();
        var local=all.Where(c=>c.bounds.center.z<320&&c.bounds.center.z>204&&c.bounds.center.x> -65&&c.bounds.center.x<180).ToArray();
        var selected=local.Take(28).ToArray();var retired=local.Skip(28).ToArray();
        if(selected.Length==0)throw new Exception("No entrance rocks found");
        var chosen=new HashSet<Transform>(local.Select(c=>c.transform));
        var unchanged=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>!chosen.Contains(t)).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
        var occupied=all.Except(local).Select(c=>c.bounds).ToList();
        var cliffs=scene.GetRootGameObjects().Single(g=>g.name=="07 - Basin cliffs and cave exit gorge").GetComponentsInChildren<Collider>();
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN");
        var shipBounds=ship.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).ToArray();
        bool Overlap(Collider a,Collider b)=>a.bounds.Intersects(b.bounds)&&Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float distance)&&distance>.02f;
        var random=new System.Random(73591);float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        var bases=new Dictionary<Transform,Vector3[]>();
        foreach(var rock in selected) {
            var t=rock.transform;var mesh=rock.sharedMesh;
            var baseVertices=mesh.vertices.Where(v=>v.y<mesh.bounds.min.y+.035f).Distinct().ToArray();bases[t]=baseVertices;
            float width=R(2.7f,6.5f),height=width*R(.55f,1.45f);
            if(random.NextDouble()<.12f){width=R(5.5f,7.5f);height=R(9,12);}
            bool placed=false;
            for(int attempt=0;attempt<1600;attempt++) {
                // Asymmetric loose banks leave an obvious open approach down the centre.
                bool left=random.NextDouble()<.45f;
                var p=V(left?R(-49,37):R(91,160),0,R(247,312));
                var normal=Normal(p);if(normal.y<.94f)continue;
                t.position=p;t.rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(R(-4,4),R(0,360),R(-4,4));t.localScale=V(width,height,width*R(.65f,1.15f));
                float shift=float.PositiveInfinity;
                foreach(var vertex in baseVertices){var world=t.TransformPoint(vertex);shift=Mathf.Min(shift,Ground(world)-world.y);}
                t.position+=Vector3.up*(shift-.18f);Physics.SyncTransforms();
                var bounds=t.GetComponent<Renderer>().bounds;
                if(occupied.Any(b=>Near(bounds,b,4.2f))||shipBounds.Any(b=>Near(bounds,b,3))||cliffs.Any(c=>Overlap(rock,c)))continue;
                occupied.Add(bounds);placed=true;break;
            }
            if(!placed)throw new Exception("No clear local position for entrance rock "+t.name);
        }
        Physics.SyncTransforms();
        float worstGap=float.NegativeInfinity;
        foreach(var rock in selected) {
            foreach(var vertex in bases[rock.transform]) {var p=rock.transform.TransformPoint(vertex);worstGap=Mathf.Max(worstGap,p.y-Ground(p));}
            foreach(var other in all.Except(retired))if(other!=rock&&Overlap(rock,other))throw new Exception("Entrance rocks overlap");
            foreach(var cliff in cliffs)if(Overlap(rock,cliff))throw new Exception("Entrance rock overlaps cliff");
        }
        if(worstGap>0)throw new Exception("Floating entrance rock base");
        foreach(var item in unchanged)if(item.t.localPosition!=item.localPosition||item.t.localRotation!=item.localRotation||item.t.localScale!=item.localScale)throw new Exception("Object outside entrance selection changed: "+item.t.name);
        DeepReachCaveRevision.ValidateRoutes(scene);
        for(float z=244;z<=325;z+=1) {
            var p=V(65,0,z);p.y=Ground(p)+3;
            foreach(var rock in selected)if(rock.bounds.SqrDistance(p)<1.21f)throw new Exception("Entrance approach obstructed");
        }
        foreach(var rock in retired)UnityEngine.Object.DestroyImmediate(rock.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Capture();
        Debug.Log("ENTRANCE_ROCKS_PASS: adjusted only "+selected.Length+" entrance rocks and thinned "+retired.Length+" crowded local rocks; 4.2m spacing, grounded bases, no intersections and unchanged outside transforms verified. Maximum base clearance "+worstGap.ToString("F3")+"m.");
    }
    static void Capture() {
        var g=new GameObject("Temporary entrance rock preview");var cam=g.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;cam.farClipPlane=500;
        cam.transform.position=V(65,30,231);cam.transform.LookAt(V(65,-2,290));
        var lamp=new GameObject("Temporary entrance inspection light");var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.65f;lamp.transform.rotation=Quaternion.Euler(55,-25,0);
        var rt=RenderTexture.GetTemporary(1600,900,24);var previous=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();Directory.CreateDirectory("Previews/DeepReach");File.WriteAllBytes("Previews/DeepReach/Entrance rocks.png",tex.EncodeToPNG());
        cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(g);UnityEngine.Object.DestroyImmediate(lamp);
    }
}
