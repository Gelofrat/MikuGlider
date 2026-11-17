using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabHandReview
    {
        private static void SetWorld(Transform t, float[] values)
        {
            var m=new Matrix4x4();
            for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=values[r*4+c];
            t.SetPositionAndRotation(m.GetColumn(3),m.rotation);
            Vector3 parent=t.parent==null?Vector3.one:t.parent.lossyScale, scale=m.lossyScale;
            t.localScale=new Vector3(scale.x/parent.x,scale.y/parent.y,scale.z/parent.z);
        }
        internal static void Candidate(SkinnedMeshRenderer arm,string file)
        {
            var data=JObject.Parse(File.ReadAllText(file));var mesh=UnityEngine.Object.Instantiate(arm.sharedMesh);
            mesh.ClearBlendShapes();
            mesh.name=arm.sharedMesh.name;mesh.vertices=data["positions"].Select(p=>new Vector3((float)p[0],(float)p[1],(float)p[2])).ToArray();
            mesh.normals=data["normals"].Select(p=>new Vector3((float)p[0],(float)p[1],(float)p[2])).ToArray();
            if(data["uv"]!=null)mesh.uv=data["uv"].Select(p=>new Vector2((float)p[0],(float)p[1])).ToArray();
            if(data["sections"]!=null)
            {
                mesh.subMeshCount=data["sections"].Count();int section=0;
                foreach(var indices in data["sections"])mesh.SetTriangles(indices.ToObject<int[]>(),section++);
            }
            int[][] ids=data["indices"].ToObject<int[][]>();float[][] w=data["weights"].ToObject<float[][]>();
            mesh.boneWeights=Enumerable.Range(0,ids.Length).Select(i=>new BoneWeight{boneIndex0=ids[i][0],boneIndex1=ids[i][1],boneIndex2=ids[i][2],boneIndex3=ids[i][3],weight0=w[i][0],weight1=w[i][1],weight2=w[i][2],weight3=w[i][3]}).ToArray();
            mesh.RecalculateBounds();arm.sharedMesh=mesh;
        }
        public static string Recorded(string variant,string capture,int frameIndex,string candidates,string output)
        {
            LabAssetLibrary.Clear();
            var frame=JObject.Parse(File.ReadLines(Path.Combine(capture,"frames.jsonl")).Skip(frameIndex).First())["character"];
            string weapon=frame["weapon"].ToString();string key=weapon.Contains("Plate")?"clip-313":weapon.Contains("Triple")?"clip-420":"clip-367";
            var actor=new LabActor(new LabVariant{key=variant,label="Recorded hand review"},true,false);
            var catalog=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            var entry=catalog.First(c=>c.key==key);var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
            actor.Animate(entry,clip,clip,1,0,false,false);
            foreach(string side in new[]{"left","right"})
            {
                var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-"+side));
                var record=frame["renderers"].First(r=>r["role"].ToString()=="arm_"+side);
                SetWorld(arm.transform,record["localToWorld"].ToObject<float[]>());
                float[] values=record["boneMatrices"].ToObject<float[]>();
                for(int i=0;i<28;i++)SetWorld(arm.bones[i],values.Skip(i*16).Take(16).ToArray());
                if(!string.IsNullOrEmpty(candidates))Candidate(arm,Path.Combine(candidates,variant+"-arm-"+side+"-anatomy.json"));
                if(side=="left"||variant=="v2")
                {
                    FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition{firstPersonWristTwistLength=variant=="v3"?.24f:0});
                    arm.GetComponent<FirstPersonWristVisual>().UpdatePose();
                }
            }
            var filters=actor.Root.GetComponentsInChildren<MeshFilter>();int matched=0;
            foreach(var record in frame["renderers"].Where(r=>r["role"].ToString()=="weapon"))
            {
                string name=record["path"].ToString().Split('/').Last();
                var filter=filters.FirstOrDefault(f=>f.name=="Native weapon "+name);
                if(filter!=null){SetWorld(filter.transform,record["localToWorld"].ToObject<float[]>());matched++;}
            }
            var go=new GameObject("Recorded viewmodel camera");var camera=go.AddComponent<Camera>();
            float[] p=frame["camera"]["position"].ToObject<float[]>(),q=frame["camera"]["rotation"].ToObject<float[]>();
            camera.transform.SetPositionAndRotation(new Vector3(p[0],p[1],p[2]),new Quaternion(q[0],q[1],q[2],q[3]));
            camera.fieldOfView=55;camera.nearClipPlane=.01f;camera.farClipPlane=20;camera.aspect=16f/9;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.12f,.14f);
            var rt=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(output,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);}
            return "Recorded frame "+frameIndex+", weapon meshes matched "+matched+"/"+filters.Length+": "+output;
        }
    }
}
