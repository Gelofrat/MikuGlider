using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabFingerPoseChecks
    {
        static string Hash(string p) { using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant(); }
        public static string Run(string folder,string captures,string output)
        {
            LabAssetLibrary.Clear();var results=new List<object>();var regions=JObject.Parse(File.ReadAllText(Path.Combine(folder,"candidate-report.json")));
            foreach(string variant in new[]{"v1","v3"})
            {
                string capture=variant=="v1"?"20261116-162906-805-0aca39":"20261116-163111-497-b3c226";
                var actor=new LabActor(new LabVariant{key=variant,label="Plate hand acceptance"},true,false);var baked=new Mesh();
                try
                {
                    var entry=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips.First(c=>c.key=="clip-313");var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);actor.Animate(entry,clip,clip,1,0,false,false);
                    var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-left"));LabHandReview.Candidate(arm,Path.Combine(folder,variant+"-arm-left-candidate.json"));FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition{firstPersonWristTwistLength=variant=="v3"?.24f:0});
                    var root=actor.Rig.Nodes.Single(n=>n.name=="cc_WeaponRoot");var profile=ModelCorrections.Parse(File.ReadAllText(Path.Combine(folder,variant+"-corrections.json")));ModelPoseCorrections.Attach(arm,profile,root,"Native weapon ");var component=arm.GetComponent<ModelPoseCorrections>();
                    if(component==null||!component.enabled)throw new InvalidOperationException("Rejected profile "+variant);
                    var update=(Action)Delegate.CreateDelegate(typeof(Action),component,typeof(ModelPoseCorrections).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic));var sources=arm.bones;var copies=arm.bones;var rest=arm.sharedMesh.vertices;var binds=arm.sharedMesh.bindposes;
                    int[] indexNail=regions[variant]["indexNailVertices"].ToObject<int[]>();int[] thumbNail=variant=="v3"?regions[variant]["nail"]["vertices"].ToObject<int[]>():new int[0];
                    if(indexNail.Length<3||variant=="v3"&&thumbNail.Length<3)throw new InvalidOperationException("Missing nail coverage");
                    int count=0;float nailError=0,sourceError=0;var sourceBefore=new Matrix4x4[28];
                    foreach(string line in File.ReadLines(Path.Combine(captures,capture,"frames.jsonl")))
                    {
                        var ch=JObject.Parse(line)["character"];var record=ch["renderers"].First(r=>r["role"].ToString()=="arm_left");var cp=ch["camera"]["position"].ToObject<float[]>();var origin=new Vector3(cp[0],cp[1],cp[2]);
                        LabThumbReplay.SetWorld(arm.transform,record["localToWorld"],origin);var matrices=(record["sourceBoneMatrices"]?.Type==JTokenType.Array?record["sourceBoneMatrices"]:record["boneMatrices"]).ToObject<float[]>();
                        for(int i=0;i<28;i++)LabThumbReplay.SetWorld(sources[i],JArray.FromObject(matrices.Skip(i*16).Take(16).ToArray()),origin);
                        LabThumbReplay.SetWorld(root,ch["weaponMatrix"],origin);arm.GetComponent<FirstPersonWristVisual>().UpdatePose();for(int i=0;i<28;i++)sourceBefore[i]=sources[i].localToWorldMatrix;
                        update();arm.BakeMesh(baked,true);var vertices=baked.vertices;
                        foreach(var pair in new[]{Tuple.Create(indexNail,7),Tuple.Create(thumbNail,26)})
                            for(int a=0;a<pair.Item1.Length;a++)for(int z=a+1;z<pair.Item1.Length;z++)
                            {
                                int x=pair.Item1[a],y=pair.Item1[z];var actual=arm.transform.TransformVector(vertices[x]-vertices[y]);var expected=copies[pair.Item2].TransformVector(binds[pair.Item2].MultiplyVector(rest[x]-rest[y]));nailError=Mathf.Max(nailError,Mathf.Abs(actual.magnitude-expected.magnitude));
                            }
                        for(int i=0;i<28;i++)for(int k=0;k<16;k++)sourceError=Mathf.Max(sourceError,Mathf.Abs(sourceBefore[i][k]-sources[i].localToWorldMatrix[k]));count++;
                    }
                    // Repeated binding must not append copies or accumulate rotations.
                    for(int i=0;i<5;i++){ModelPoseCorrections.Attach(arm,profile,root,"Native weapon ");update();}
                    if(arm.bones[7]!=sources[7])throw new InvalidOperationException("Rebind changed native skeleton");
                    for(int i=0;i<100;i++)update();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)update();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                    var active=new bool[root.childCount];for(int i=0;i<active.Length;i++){active[i]=root.GetChild(i).gameObject.activeSelf;root.GetChild(i).gameObject.SetActive(false);}update();float inactiveAngle=Quaternion.Angle(copies[7].localRotation,sources[7].localRotation);
                    for(int i=0;i<active.Length;i++)root.GetChild(i).gameObject.SetActive(active[i]);update();component.enabled=false;
                    // This harness runs in Edit Mode. Runtime-only MonoBehaviours
                    // do not receive lifecycle messages there automatically.
                    component.SendMessage("OnDisable");
                    if(arm.bones[7]!=sources[7] || arm.bones[8]!=sources[8])throw new InvalidOperationException("Disable changed native skeleton");
                    float disabledAngle=Quaternion.Angle(arm.bones[7].localRotation,sources[7].localRotation);component.enabled=true;update();
                    bool passed=count==(variant=="v1"?351:345)&&nailError<.00002f&&sourceError==0&&allocated==0&&inactiveAngle<.05f&&disabledAngle<.05f;
                    results.Add(new{variant,frames=count,indexNailVertices=indexNail.Length,thumbNailVertices=thumbNail.Length,maximumNailDistanceErrorMeters=nailError,maximumNativeTransformChange=sourceError,steadyStateAllocatedBytes=allocated,inactiveAngle,disabledAngle,passed,modelSha256=Hash(Path.Combine(folder,variant+".mikumesh")),correctionsSha256=Hash(Path.Combine(folder,variant+"-corrections.json")),captureSha256=Hash(Path.Combine(captures,capture,"frames.jsonl"))});
                }
                finally{UnityEngine.Object.DestroyImmediate(baked);actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
            }
            File.WriteAllText(output,JsonConvert.SerializeObject(results,Formatting.Indented));return "Recorded renderer-only finger pose checks completed";
        }
    }
}
