using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    // Fully composed Blender review bakes. Do not install these as native
    // additive clips: their base pose has already been evaluated.
    public static class LabBakedThumbClips
    {
        static readonly string[] Properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
        static AnimationCurve Curve(float[] values, int start, int stride, int count, float duration)
        {
            bool constant=true; for(int i=1;i<count;i++) if(Mathf.Abs(values[start+i*stride]-values[start])>1e-7f){constant=false;break;}
            int n=constant?2:count; var keys=new Keyframe[n];
            for(int i=0;i<n;i++)
            {
                int f=constant?(i==0?0:count-1):i; float t=duration*f/(count-1),v=values[start+f*stride];
                float incoming=f==0?0:(v-values[start+(f-1)*stride])*(count-1)/duration;
                float outgoing=f==count-1?0:(values[start+(f+1)*stride]-v)*(count-1)/duration;
                keys[i]=new Keyframe(t,v,incoming,outgoing);
            }
            return new AnimationCurve(keys){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        }
        static float[] ReadFloats(string path)
        { byte[] bytes=File.ReadAllBytes(path);var values=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);return values; }
        static T Save<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;
        }
        static void SetNative(Transform bone,float[] source,int start)
        {
            var m=new Matrix4x4();for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=source[start+r*4+c];
            bone.SetPositionAndRotation((Vector3)m.GetColumn(3)+Vector3.up,m.rotation);
            Vector3 parent=bone.parent.lossyScale,s=m.lossyScale;bone.localScale=new Vector3(s.x/parent.x,s.y/parent.y,s.z/parent.z);
        }
        public static string BuildAndValidate(string folder,string nativeFolder)
        {
            LabAssetLibrary.Clear();var manifest=JObject.Parse(File.ReadAllText(Path.Combine(folder,"manifest.json")));
            float[] native=ReadFloats(Path.Combine(nativeFolder,"clip-bones.bin"));
            var catalog=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            const string output="Assets/BakedThumbReview";Directory.CreateDirectory(output);AssetDatabase.Refresh();
            var reports=new List<object>();
            foreach(var variant in new[]{"v1","v2","v3"})
            {
                var data=manifest["variants"][variant];int stride=(int)data["stride"];float[] values=ReadFloats(Path.Combine(folder,(string)data["file"]));
                var actor=new LabActor(new LabVariant{key=variant,label="Thumb bake source"},true);
                var root=new GameObject(variant+" baked hands");var skinners=new SkinnedMeshRenderer[2];var source=new SkinnedMeshRenderer[2];var bones=new List<Transform>();var paths=new List<string>();
                var temporary=new Mesh();var reference=new Mesh();double maxMesh=0,maxLocal=0,maxRotation=0,maxShape=0;int frames=0,meshFrames=0,clips=0;
                try
                {
                    for(int side=0;side<2;side++)
                    {
                        string sideName=side==0?"left":"right";source[side]=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.Contains("arm-"+sideName));
                        var node=new GameObject(sideName);node.transform.SetParent(root.transform,false);var renderer=node.AddComponent<SkinnedMeshRenderer>();skinners[side]=renderer;
                        renderer.sharedMesh=Save(UnityEngine.Object.Instantiate(source[side].sharedMesh),output+"/"+variant+"-"+sideName+".asset");
                        renderer.sharedMaterial=Save(UnityEngine.Object.Instantiate(source[side].sharedMaterial),output+"/"+variant+"-"+sideName+".mat");renderer.quality=SkinQuality.Bone4;renderer.updateWhenOffscreen=true;
                        var info=data["sides"][side];string[] names=info["names"].ToObject<string[]>();int[] parents=info["parents"].ToObject<int[]>();var joints=new Transform[names.Length];
                        for(int i=0;i<names.Length;i++)
                        {
                            joints[i]=new GameObject(names[i]).transform;joints[i].SetParent(parents[i]<0?node.transform:joints[parents[i]],false);
                            bones.Add(joints[i]);paths.Add(AnimationUtility.CalculateTransformPath(joints[i],root.transform));
                        }
                        renderer.bones=joints;renderer.rootBone=joints[0];renderer.localBounds=new Bounds(Vector3.zero,Vector3.one*8);
                    }
                    int shapeStart=bones.Count*10;var nativeBones=source.SelectMany(r=>r.bones.Take(28)).ToArray();
                    var corrections=source.Select(r=>r.GetComponent<ModelPoseCorrections>()).Select(c=>c==null || !c.enabled ? null : (Action)Delegate.CreateDelegate(typeof(Action),c,typeof(ModelPoseCorrections).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic))).ToArray();
                    foreach(var clipInfo in manifest["clips"])
                    {
                        string key=(string)clipInfo["key"];int count=(int)clipInfo["count"],offset=(int)clipInfo["bakeOffset"];float duration=(float)clipInfo["duration"];
                        var clip=new AnimationClip{name=variant+" "+key+" composed Blender bake",frameRate=60};
                        var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
                        for(int i=0;i<bones.Count;i++)for(int ch=0;ch<10;ch++)
                        { bindings.Add(EditorCurveBinding.FloatCurve(paths[i],typeof(Transform),Properties[ch]));curves.Add(Curve(values,offset*stride+i*10+ch,stride,count,duration)); }
                        for(int i=0;i<2;i++)
                        { bindings.Add(EditorCurveBinding.FloatCurve("left",typeof(SkinnedMeshRenderer),"blendShape."+(string)data["shapeNames"][i]));curves.Add(Curve(values,offset*stride+shapeStart+i,stride,count,duration)); }
                        AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());
                        clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
                        clip=Save(clip,output+"/"+variant+"-"+key+".anim");clips++;
                        var entry=catalog.First(c=>c.rig=="arms" && c.key==key);var original=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
                        actor.Animate(entry,original,original,1,0,false,false);
                        for(int f=0;f<count;f++)
                        {
                            clip.SampleAnimation(root,duration*f/(count-1));int start=(offset+f)*stride;
                            for(int i=0;i<bones.Count;i++)
                            {
                                var t=bones[i];int at=start+i*10;var p=new Vector3(values[at],values[at+1],values[at+2]);var q=new Quaternion(values[at+3],values[at+4],values[at+5],values[at+6]);var s=new Vector3(values[at+7],values[at+8],values[at+9]);
                                maxLocal=Math.Max(maxLocal,Math.Max(Vector3.Distance(t.localPosition,p),Vector3.Distance(t.localScale,s)));maxRotation=Math.Max(maxRotation,Quaternion.Angle(t.localRotation,q));
                            }
                            for(int i=0;i<2;i++)maxShape=Math.Max(maxShape,Mathf.Abs(skinners[0].GetBlendShapeWeight(i)-values[start+shapeStart+i]));
                            frames++;
                            // Full skeleton/shape curves are checked every sample. Skinning is
                            // compared against the live procedural implementation every fourth
                            // sample plus both endpoints of every native clip.
                            if(f%4!=0 && f!=count-1)continue;
                            for(int i=0;i<56;i++)SetNative(nativeBones[i],native,((int)clipInfo["offset"]+f)*56*16+i*16);
                            for(int side=0;side<2;side++){source[side].GetComponent<FirstPersonWristVisual>()?.UpdatePose();corrections[side]?.Invoke();}
                            for(int side=0;side<2;side++)
                            {
                                source[side].BakeMesh(reference,true);skinners[side].BakeMesh(temporary,true);var a=reference.vertices;var b=temporary.vertices;
                                for(int i=0;i<a.Length;i++)maxMesh=Math.Max(maxMesh,Vector3.Distance(source[side].transform.TransformPoint(a[i])-Vector3.up,b[i]));
                            }
                            meshFrames++;
                        }
                    }
                    var first=AssetDatabase.LoadAssetAtPath<AnimationClip>(output+"/"+variant+"-clip-381.anim");first.SampleAnimation(root,.1f);
                    var animator=root.AddComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    PrefabUtility.SaveAsPrefabAsset(root,output+"/"+variant+"-baked-hands.prefab");
                    reports.Add(new{variant,clips,frames,meshFrames,maxLocal,maxRotation,maxShape,maxMesh,passed=maxLocal<.0001 && maxRotation<.1 && maxShape<.05 && maxMesh<.0001});
                }
                finally
                {
                    actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(temporary);UnityEngine.Object.DestroyImmediate(reference);
                }
            }
            AssetDatabase.SaveAssets();string result=JsonConvert.SerializeObject(reports,Formatting.Indented);File.WriteAllText(Path.Combine(folder,"unity-validation.json"),result);return result;
        }
    }
}
