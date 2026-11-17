using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabGripChecks
    {
        public static string Run(string folder,string captures,string output)
        {
            LabAssetLibrary.Clear();var results=new List<object>();
            foreach(string capture in new[]{"20261116-180944-759-d974ee","20261116-162906-805-0aca39"})
            {
                var actor=new LabActor(new LabVariant{key="v1",label="Human Plate grip"},true,false);var baked=new Mesh();
                try
                {
                    var entry=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips.First(c=>c.key=="clip-313");var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);actor.Animate(entry,clip,clip,1,0,false,false);
                    var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-left"));LabHandReview.Candidate(arm,Path.Combine(folder,"v1-arm-left-candidate.json"));FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition());
                    var root=actor.Rig.Nodes.Single(n=>n.name=="cc_WeaponRoot");var profile=ModelCorrections.Parse(File.ReadAllText(Path.Combine(folder,"v1-corrections.json")));ModelPoseCorrections.Attach(arm,profile,root,"Native weapon ");var component=arm.GetComponent<ModelPoseCorrections>();
                    if(component==null||!component.enabled)throw new InvalidOperationException("Rejected profile");
                    var update=(Action)Delegate.CreateDelegate(typeof(Action),component,typeof(ModelPoseCorrections).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic));var sources=component.SourceBones;var copies=arm.bones;var rest=arm.sharedMesh.vertices;var binds=arm.sharedMesh.bindposes;
                    var handle=actor.Root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Native weapon Handle").transform;
                    int[] nail=JObject.Parse(File.ReadAllText(Path.Combine(folder,"regions.json")))["indexNailVertices"].ToObject<int[]>();
                    var nailRegions=JArray.Parse(File.ReadAllText(Path.Combine(folder,"nail-regions.json")));
                    var nailIds=nailRegions.Select(r=>r["vertices"].ToObject<int[]>()).ToArray();
                    if(nailIds.Length!=5||nailIds.Any(ids=>ids.Length<20))throw new InvalidOperationException("Incomplete nail coverage");
                    var nailDistances=nailIds.Select(ids=>new float[ids.Length*ids.Length]).ToArray();
                    var nailMotionErrors=new float[nailIds.Length];
                    int count=0;float nailError=0,sourceError=0,lengthError=0,maximumWeightStep=0;var sourceBefore=new Matrix4x4[28];float previousWeight=0;double previousTime=0;var timeline=new List<object>();
                    foreach(string line in File.ReadLines(Path.Combine(captures,capture,"frames.jsonl")))
                    {
                        var frame=JObject.Parse(line);var ch=frame["character"];var record=ch["renderers"].First(r=>r["role"].ToString()=="arm_left");var cp=ch["camera"]["position"].ToObject<float[]>();var origin=new Vector3(cp[0],cp[1],cp[2]);
                        LabThumbReplay.SetWorld(arm.transform,record["localToWorld"],origin);var matrices=(record["sourceBoneMatrices"]?.Type==JTokenType.Array?record["sourceBoneMatrices"]:record["boneMatrices"]).ToObject<float[]>();
                        for(int i=0;i<28;i++)LabThumbReplay.SetWorld(sources[i],JArray.FromObject(matrices.Skip(i*16).Take(16).ToArray()),origin);
                        LabThumbReplay.SetWorld(root,ch["weaponMatrix"],origin);LabThumbReplay.SetWorld(handle,ch["renderers"].First(r=>r["path"].ToString().EndsWith("/Handle"))["localToWorld"],origin);arm.GetComponent<FirstPersonWristVisual>().UpdatePose();for(int i=0;i<28;i++)sourceBefore[i]=sources[i].localToWorldMatrix;
                        update();arm.BakeMesh(baked,true);var vertices=baked.vertices;
                        for(int a=0;a<nail.Length;a++)for(int z=a+1;z<nail.Length;z++)
                        {
                            int x=nail[a],y=nail[z];var actual=arm.transform.TransformVector(vertices[x]-vertices[y]);var expected=copies[7].TransformVector(binds[7].MultiplyVector(rest[x]-rest[y]));nailError=Mathf.Max(nailError,Mathf.Abs(actual.magnitude-expected.magnitude));
                        }
                        for(int n=0;n<nailIds.Length;n++)
                        {
                            var ids=nailIds[n];
                            for(int a=0;a<ids.Length;a++)for(int z=a+1;z<ids.Length;z++)
                            {
                                float distance=arm.transform.TransformVector(vertices[ids[a]]-vertices[ids[z]]).magnitude;
                                int pair=a*ids.Length+z;
                                if(count==0)nailDistances[n][pair]=distance;
                                else nailMotionErrors[n]=Mathf.Max(nailMotionErrors[n],Mathf.Abs(distance-nailDistances[n][pair]));
                            }
                        }
                        for(int i=0;i<28;i++)
                        {
                            for(int k=0;k<16;k++)sourceError=Mathf.Max(sourceError,Mathf.Abs(sourceBefore[i][k]-sources[i].localToWorldMatrix[k]));
                            if(i!=3)lengthError=Mathf.Max(lengthError,(sources[i].localPosition-copies[i].localPosition).magnitude);
                        }
                        float weight=arm.GetBlendShapeWeight(arm.sharedMesh.blendShapeCount-1);double time=(double)frame["realtime"];
                        if(count>0&&time-previousTime<.067)maximumWeightStep=Mathf.Max(maximumWeightStep,Mathf.Abs(weight-previousWeight)/100f/(float)((time-previousTime)*60));
                        previousWeight=weight;previousTime=time;timeline.Add(new{frame=count,weight});count++;
                    }
                    for(int i=0;i<5;i++){ModelPoseCorrections.Attach(arm,profile,root,"Native weapon ");update();}
                    bool stable=arm.bones[7]==copies[7]&&component.SourceBones[7]==sources[7]&&copies[7]!=sources[7];
                    for(int i=0;i<100;i++)update();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)update();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                    var active=new bool[root.childCount];for(int i=0;i<active.Length;i++){active[i]=root.GetChild(i).gameObject.activeSelf;root.GetChild(i).gameObject.SetActive(false);}update();float inactiveAngle=Quaternion.Angle(copies[7].localRotation,sources[7].localRotation);
                    for(int i=0;i<active.Length;i++)root.GetChild(i).gameObject.SetActive(active[i]);update();component.enabled=false;component.SendMessage("OnDisable");bool restored=arm.bones[7]==sources[7]&&arm.bones[8]==sources[8];component.enabled=true;update();bool rebound=arm.bones[7]==copies[7];
                    var allNails=nailRegions.Select((r,n)=>new{name=r["name"].ToString(),vertices=nailIds[n].Length,maximumPairDistanceChangeMeters=nailMotionErrors[n]}).ToArray();
                    bool passed=count==(capture.Contains("180944")?373:351)&&nailError<.00002f&&nailMotionErrors.All(e=>e<.00002f)&&sourceError==0&&lengthError<1e-7f&&allocated==0&&inactiveAngle<.05f&&stable&&restored&&rebound&&maximumWeightStep<.08f;
                    results.Add(new{capture,frames=count,nailVertices=nail.Length,maximumNailDistanceErrorMeters=nailError,allNails,maximumNativeTransformChange=sourceError,maximumLocalJointTranslationError=lengthError,steadyStateAllocatedBytes=allocated,inactiveAngle,stable,restored,rebound,maximumWeightStepPer60Hz=maximumWeightStep,passed,timeline});
                }
                finally{UnityEngine.Object.DestroyImmediate(baked);actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
            }
            File.WriteAllText(output,JsonConvert.SerializeObject(results,Formatting.Indented));return "Human grip replay checks completed";
        }
    }
}
