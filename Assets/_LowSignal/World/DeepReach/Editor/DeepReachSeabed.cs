using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Native painted terrain and batched detail meshes, saved for further hand authoring.
public static class DeepReachSeabed
{
    const string Root="Assets/_LowSignal/World/DeepReach/Seabed";
    const string GroupName="06 - Sand drifts and gravel seabed";
    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    static float Noise(float x,float z,float scale)=>Mathf.PerlinNoise(x/scale+47.2f,z/scale+13.8f);
    static float Height(float x,float z) {
        return DeepReachGrounding.Height(x,z);
    }
    static Material Material(string name,Color c) {
        string path=Root+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.color=c;mat.SetFloat("_Smoothness",.06f);EditorUtility.SetDirty(mat);return mat;
    }
    static TerrainLayer Layer(string name,TerrainLayer source,Vector3 tint,float tile) {
        string path=Root+"/"+name+".terrainlayer";var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if(!layer){layer=new TerrainLayer();AssetDatabase.CreateAsset(layer,path);}
        layer.diffuseTexture=source.diffuseTexture;layer.normalMapTexture=source.normalMapTexture;
        layer.tileSize=new Vector2(tile,tile);layer.normalScale=.9f;layer.smoothness=.06f;
        layer.diffuseRemapMin=Vector4.zero;layer.diffuseRemapMax=new Vector4(tint.x,tint.y,tint.z,1);
        EditorUtility.SetDirty(layer);return layer;
    }
    [MenuItem("Low Signal/Level 2/Detail Deep Reach seabed")]
    public static void Apply() {
        const string scenePath="Assets/_LowSignal/Scenes/DeepReach.unity";
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);if(!scene.isLoaded)scene=EditorSceneManager.OpenScene(scenePath);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        Directory.CreateDirectory(Root);AssetDatabase.Refresh();
        var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GroupName);if(old)UnityEngine.Object.DestroyImmediate(old);
        var parent=new GameObject(GroupName).transform;
        var floor=scene.GetRootGameObjects().Single(g=>g.name=="02 - Open seafloor basin").transform.Find("Continuous basin floor");
        var bounds=floor.GetComponent<Renderer>().bounds;
        float width=bounds.size.x,depth=bounds.size.z,baseY=bounds.max.y;
        var origin=V(bounds.min.x,baseY,bounds.min.z);
        var wreck=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;
        DeepReachGrounding.Prepare(wreck,baseY);
        var source=AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_LowSignal/World/Shallows/Textures/Sand/Sand.terrainlayer");
        if(!source||!source.diffuseTexture||!source.normalMapTexture)throw new Exception("Shallows sand textures missing");
        var grey=Layer("Grey mineral silt",source,V(.30f,.40f,.46f),3.1f);
        var sand=Layer("Shallows sand drifts",source,V(.87f,.91f,.88f),4);
        var gravel=Layer("Dark gravel sediment",source,V(.20f,.26f,.29f),1.5f);
        string dataPath=Root+"/DeepReach seabed.asset";var data=AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if(!data){data=new TerrainData();AssetDatabase.CreateAsset(data,dataPath);}
        data.heightmapResolution=513;data.alphamapResolution=512;data.baseMapResolution=1024;
        data.size=V(width,16,depth);data.terrainLayers=new[]{grey,sand,gravel};
        var heights=new float[513,513];
        for(int z=0;z<513;z++)for(int x=0;x<513;x++)heights[z,x]=Height(origin.x+x*width/512,origin.z+z*depth/512)/16;
        data.SetHeights(0,0,heights);
        var paint=new float[512,512,3];double sandCoverage=0;
        for(int z=0;z<512;z++)for(int x=0;x<512;x++) {
            float wx=origin.x+x*width/511,wz=origin.z+z*depth/511;
            float n=Noise(wx,wz,40)*.7f+Noise(wx,wz,13)*.3f;
            float sandy=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.39f,.60f,n));
            float dark=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.58f,.75f,Noise(wx+190,wz,19)))*.45f;
            paint[z,x,0]=(1-sandy)*(1-dark);paint[z,x,1]=sandy*(1-dark);paint[z,x,2]=dark;sandCoverage+=paint[z,x,1];
        }
        data.SetAlphamaps(0,0,paint);EditorUtility.SetDirty(data);
        var terrainObject=Terrain.CreateTerrainGameObject(data);terrainObject.name="Painted grey silt and Shallows sand";terrainObject.transform.SetParent(parent,false);terrainObject.transform.position=origin;
        var terrain=terrainObject.GetComponent<Terrain>();terrain.heightmapPixelError=3;terrain.basemapDistance=700;terrain.drawInstanced=true;
        string matPath=Root+"/Blended seabed.mat";var terrainMat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if(!terrainMat){terrainMat=new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));AssetDatabase.CreateAsset(terrainMat,matPath);}
        terrainMat.EnableKeyword("_NORMALMAP");terrain.materialTemplate=terrainMat;EditorUtility.SetDirty(terrainMat);
        var rock=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LowSignal/World/SeaFloor/Models/Boulder.obj").GetComponentInChildren<MeshFilter>().sharedMesh;
        var pebbleMat=Material("Charcoal gravel",new Color(.20f,.25f,.25f));
        var paleMat=Material("Pale stone fragments",new Color(.48f,.47f,.38f));
        var stoneMat=Material("Grey siltstone lenses",new Color(.29f,.33f,.32f));
        var lists=new[]{new List<CombineInstance>(),new List<CombineInstance>(),new List<CombineInstance>()};
        var random=new System.Random(6641);float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        // Dispersed clusters rather than an even carpet; low relief keeps swimming routes open.
        for(int cluster=0;cluster<95;cluster++) {
            float cx=R(origin.x+8,origin.x+width-8),cz=R(Mathf.Max(235,origin.z+8),origin.z+depth-8);
            int count=cluster%5==0?20:9;
            for(int i=0;i<count;i++) {
                float x=cx+R(-6,6),z=cz+R(-5,5);int type=i%7==0?2:i%3==0?1:0;
                float size=type==2?R(.65f,1.6f):R(.12f,.50f),h=type==2?R(.10f,.24f):R(.05f,.17f);
                var pos=V(x,baseY+terrain.SampleHeight(V(x,0,z))+h*.18f,z);
                var shape=Matrix4x4.TRS(pos,Quaternion.Euler(R(-8,8),R(0,360),R(-8,8)),V(size,h,size*R(.6f,1.4f)));
                var normalize=Matrix4x4.Scale(V(1/rock.bounds.size.x,1/rock.bounds.size.y,1/rock.bounds.size.z))*Matrix4x4.Translate(-rock.bounds.center);
                lists[type].Add(new CombineInstance{mesh=rock,transform=shape*normalize});
            }
        }
        var materials=new[]{pebbleMat,paleMat,stoneMat};
        for(int i=0;i<3;i++) {
            string path=Root+"/Sediment detail "+i+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(lists[i].ToArray(),true,true);EditorUtility.SetDirty(mesh);
            var g=new GameObject(materials[i].name);g.transform.SetParent(parent,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=materials[i];g.isStatic=true;
        }
        DeepReachGrounding.SettleLoose(scene,terrain);
        Physics.SyncTransforms();DeepReachCaveRevision.ValidateRoutes(scene);
        if(sandCoverage/(512*512)<.15||sandCoverage/(512*512)>.85)throw new Exception("Sand/silt blend lacks variation");
        if(terrain.SampleHeight(V(34,0,225))+baseY>baseY+.4f)throw new Exception("Cave approach raised too far");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        var ship=scene.GetRootGameObjects().Single(g=>g.name=="05 - Cargo wreck - MERIDIAN").transform;DeepReachCargoShip.Capture(ship);
        CaptureDetail(V(55,baseY+Height(55,310)+7,310),V(66,baseY+Height(66,322),322));
        Debug.Log("DEEP_REACH_SEABED_PASS: Shallows sand and normal textures blended with grey silt and dark sediment; "+lists.Sum(l=>l.Count)+" stone details; cave routes verified. Sand coverage "+(sandCoverage/(512*512)).ToString("P0"));
    }
    static void CaptureDetail(Vector3 position,Vector3 target) {
        var g=new GameObject("Temporary seabed preview");var cam=g.AddComponent<Camera>();cam.transform.position=position;cam.transform.LookAt(target);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;
        var rt=RenderTexture.GetTemporary(1600,900,24);var previous=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes("Previews/DeepReach/Seabed detail.png",tex.EncodeToPNG());
        cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(g);
    }
}
