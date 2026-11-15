using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabSleeveCandidates
    {
        public static string Benchmark(string output, float twistLength=0f)
        {
            var actor=new LabActor(new LabVariant{key="v1",label="Wrist benchmark"},true,false);
            var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-left"));
            FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition{firstPersonWristTwistLength=twistLength});
            var visual=arm.GetComponent<FirstPersonWristVisual>();
            for(int i=0;i<1000;i++)visual.UpdatePose();
            var samples=new double[7];
            var timer=new System.Diagnostics.Stopwatch();
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int j=0;j<samples.Length;j++)
            {
                timer.Restart();for(int i=0;i<20000;i++)visual.UpdatePose();timer.Stop();
                samples[j]=timer.Elapsed.TotalMilliseconds/20000;
            }
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            string result=Newtonsoft.Json.JsonConvert.SerializeObject(new{millisecondsPerArmUpdate=samples,allocatedBytes=allocated,iterations=140000,helperBones=9});
            File.WriteAllText(output,result);actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);return result;
        }
        public static string ExportContext(string output)
        {
            var actor=new LabActor(new LabVariant{key="v3",label="Cloth context"},true,false);
            var clips=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            var entry=clips.First(c=>c.key=="clip-367" && c.rig=="arms");
            var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
            actor.Animate(entry,clip,clip,1,.1f,false,false);clip.SampleAnimation(actor.Rig.AnimationRoot,.1f);
            var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-left"));
            var inverse=(arm.bones[1].localToWorldMatrix*arm.sharedMesh.bindposes[1]).inverse;
            var meshes=actor.Root.GetComponentsInChildren<MeshFilter>().Where(m=>m.sharedMesh!=null).Select(m=>new {name=m.name,positions=m.sharedMesh.vertices.Select(p=>inverse.MultiplyPoint3x4(m.transform.TransformPoint(p))).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=m.sharedMesh.triangles}).ToArray();
            File.WriteAllText(output,Newtonsoft.Json.JsonConvert.SerializeObject(meshes));
            actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);
            return "Exported "+meshes.Length+" weapon meshes";
        }
        public static string Render(string variant, string candidate, string key, float time, string output, bool curved=false, float twistLength=0f, bool captureImage=true)
        {
            LabAssetLibrary.Clear();
            var actor=new LabActor(new LabVariant {key=variant,label="Sleeve candidate"},true,false);
            var clips=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            var entry=clips.First(c=>c.key==key && c.rig=="arms");
            var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
            var basis=clips.FirstOrDefault(c=>c.key==entry.baseKey && c.rig=="arms");
            var baseClip=basis==null?clip:Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+basis.resource);
            entry.fadeDuration=0;actor.Animate(entry,clip,baseClip,basis==null?1:basis.speed,time/entry.speed,false,false);
            var graph=PlayableGraph.Create("Immediate hand review");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var layer=AnimationLayerMixerPlayable.Create(graph,2);var primary=AnimationClipPlayable.Create(graph,entry.overlay?baseClip:clip);var overlay=AnimationClipPlayable.Create(graph,clip);
            primary.SetApplyFootIK(false);primary.SetApplyPlayableIK(false);primary.SetSpeed(0);overlay.SetApplyFootIK(false);overlay.SetApplyPlayableIK(false);overlay.SetSpeed(0);
            graph.Connect(primary,0,layer,0);layer.SetInputWeight(0,1);graph.Connect(overlay,0,layer,1);layer.SetInputWeight(1,entry.overlay?1:0);layer.SetLayerAdditive(1,entry.additive);
            var animationOutput=AnimationPlayableOutput.Create(graph,"Arms",actor.Rig.AnimationRoot.GetComponent<Animator>());animationOutput.SetSourcePlayable(layer);
            actor.Rig.Restore();graph.Play();primary.SetTime(entry.overlay?time%baseClip.length:time);overlay.SetTime(time);graph.Evaluate(0);graph.Destroy();
            var right=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-right"));
            if(!string.IsNullOrEmpty(candidate)) LabHandReview.Candidate(right,candidate.Replace("-arm-left-","-arm-right-"));
            if(curved && variant=="v2"){FirstPersonWristVisual.Attach(right,right.sharedMesh);right.GetComponent<FirstPersonWristVisual>().UpdatePose();}
            var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh!=null && r.sharedMesh.name.EndsWith("arm-left"));
            Mesh mesh=null;
            if(!string.IsNullOrEmpty(candidate))
            {
                var data=JObject.Parse(File.ReadAllText(candidate));mesh=UnityEngine.Object.Instantiate(arm.sharedMesh);
                mesh.name=arm.sharedMesh.name;mesh.ClearBlendShapes();
                mesh.vertices=data["positions"].Select(p=>new Vector3((float)p[0],(float)p[1],(float)p[2])).ToArray();
                mesh.normals=data["normals"].Select(p=>new Vector3((float)p[0],(float)p[1],(float)p[2])).ToArray();
                var ids=data["indices"].ToObject<int[][]>();var weights=data["weights"].ToObject<float[][]>();
                mesh.boneWeights=Enumerable.Range(0,ids.Length).Select(i=>new BoneWeight{boneIndex0=ids[i][0],boneIndex1=ids[i][1],boneIndex2=ids[i][2],boneIndex3=ids[i][3],weight0=weights[i][0],weight1=weights[i][1],weight2=weights[i][2],weight3=weights[i][3]}).ToArray();
                mesh.RecalculateBounds();arm.sharedMesh=mesh;
            }
            if(curved){FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition{firstPersonWristTwistLength=twistLength});arm.GetComponent<FirstPersonWristVisual>().UpdatePose();}
            if(curved)
            {
                ModelPoseCorrections.Attach(arm,actor.Skin.Assets.Corrections,actor.Rig.Nodes.First(node=>node.name=="cc_WeaponRoot"),"Native weapon ");
                var correction=arm.GetComponent<ModelPoseCorrections>();
                if(correction!=null && correction.enabled) correction.SendMessage("LateUpdate");
            }
            File.WriteAllText(output+".pose.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{
                key=ModelCorrections.Key(arm.sharedMesh),fingerprint=ModelCorrections.Fingerprint(arm.sharedMesh),
                rotations=arm.bones.Take(28).Select((t,i)=>new {bone=i,name=t.name,rotation=new {x=t.localRotation.x,y=t.localRotation.y,z=t.localRotation.z,w=t.localRotation.w}}).ToArray(),
                shapeWeights=Enumerable.Range(0,arm.sharedMesh.blendShapeCount).Select(i=>arm.GetBlendShapeWeight(i)).ToArray()}));
            var baked=new Mesh();arm.BakeMesh(baked, true);
            var rightBaked=new Mesh();right.BakeMesh(rightBaked,true);
            File.WriteAllText(output+".right.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{positions=rightBaked.vertices.Select(p=>right.transform.TransformPoint(p)).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=rightBaked.triangles,bones=right.bones.Take(28).Select(t=>Enumerable.Range(0,16).Select(i=>t.localToWorldMatrix[i/4,i%4]).ToArray()).ToArray()}));
            UnityEngine.Object.DestroyImmediate(rightBaked);
            var forearmInverse=(arm.bones[1].localToWorldMatrix*arm.sharedMesh.bindposes[1]).inverse;
            File.WriteAllText(output+".mesh.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{positions=baked.vertices.Select(p=>forearmInverse.MultiplyPoint3x4(arm.transform.TransformPoint(p))).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=baked.triangles,bones=arm.bones.Take(28).Select(t=>Enumerable.Range(0,16).Select(i=>t.localToWorldMatrix[i/4,i%4]).ToArray()).ToArray()}));UnityEngine.Object.DestroyImmediate(baked);
            File.WriteAllText(output+".weapon.json",Newtonsoft.Json.JsonConvert.SerializeObject(actor.Root.GetComponentsInChildren<MeshFilter>().Where(m=>m.sharedMesh!=null).Select(m=>new {name=m.name,positions=m.sharedMesh.vertices.Select(p=>forearmInverse.MultiplyPoint3x4(m.transform.TransformPoint(p))).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=m.sharedMesh.triangles}).ToArray()));
            if(!captureImage){actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);return output;}
            var cameraObject=new GameObject("Candidate camera");var camera=cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(actor.Root.transform.position,Quaternion.identity);
            camera.fieldOfView=55;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.aspect=16f/9;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.12f,.14f);
            var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(output,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);}
            return output;
        }
    }
}
