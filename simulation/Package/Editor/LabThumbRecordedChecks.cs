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
    public static class LabThumbRecordedChecks
    {
        static string Hash(string file) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant(); }
        public static string Run(string folder,string captures,string output)
        {
            LabAssetLibrary.Clear();var results=new List<object>();
            var regions=JObject.Parse(File.ReadAllText(Path.Combine(folder,"candidate-report.json")));
            foreach(string variant in new[]{"v2","v3"})
            {
                var actor=new LabActor(new LabVariant{key=variant,label="Recorded distal rigidity"},true,false);
                var baked=new Mesh();
                Mesh fixtureMesh=null, candidateMesh=null;
                try
                {
                    var entry=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips.First(c=>c.key=="clip-367");
                    var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
                    actor.Animate(entry,clip,clip,1,0,false,false);
                    var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-left"));
                    // A recorded fixture must use its own bind poses/topology, not
                    // combine old candidate vertices with the latest installed model.
                    using(var stream=File.OpenRead(Path.Combine(folder,variant+".mikumesh")))
                    {
                        var data=MeshResourceReader.Read(stream).Single(m=>m.Name=="arm-left");
                        fixtureMesh=new Mesh{name=variant+".arm-left"};
                        fixtureMesh.indexFormat=data.Vertices.Length>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
                        fixtureMesh.vertices=data.Vertices;fixtureMesh.normals=data.Normals;fixtureMesh.uv=data.UV;
                        fixtureMesh.bindposes=data.BindPoses;fixtureMesh.boneWeights=data.Weights;
                        MeshIndices.Assign(fixtureMesh,data.Submeshes);
                        arm.sharedMesh=fixtureMesh;
                    }
                    LabHandReview.Candidate(arm,Path.Combine(folder,variant+"-arm-left-candidate.json"));
                    candidateMesh=arm.sharedMesh;
                    var rest=arm.sharedMesh.vertices;var binds=arm.sharedMesh.bindposes;
                    var nail=regions[variant]["nailVertices"].ToObject<int[]>();
                    FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition{firstPersonWristTwistLength=variant=="v3"?.24f:0});
                    var root=actor.Rig.Nodes.Single(n=>n.name=="cc_WeaponRoot");
                    var profile=ModelCorrections.Parse(File.ReadAllText(Path.Combine(folder,variant+"-corrections.json")));
                    ModelPoseCorrections.Attach(arm,profile,root,"Native weapon ");
                    var component=arm.GetComponent<ModelPoseCorrections>();
                    if(component==null||!component.enabled)throw new InvalidOperationException("Rejected "+variant+" profile");
                    var update=(Action)Delegate.CreateDelegate(typeof(Action),component,typeof(ModelPoseCorrections).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic));
                    int count=0;float error=0;var recordings=new List<object>();
                    foreach(string capture in new[]{"20261116-133145-152-7db26b","20261116-133221-136-93ae4f"})
                    {
                        var path=Path.Combine(captures,capture,"frames.jsonl");recordings.Add(new{capture,sha256=Hash(path)});
                        foreach(string line in File.ReadLines(path))
                        {
                            var frame=JObject.Parse(line);var ch=frame["character"];var record=ch["renderers"].First(r=>r["role"].ToString()=="arm_left");
                            LabThumbReplay.SetWorld(arm.transform,record["localToWorld"]);var matrices=record["boneMatrices"].ToObject<float[]>();
                            for(int i=0;i<28;i++)LabThumbReplay.SetWorld(arm.bones[i],JArray.FromObject(matrices.Skip(i*16).Take(16).ToArray()));
                            LabThumbReplay.SetWorld(root,ch["weaponMatrix"]);
                            arm.GetComponent<FirstPersonWristVisual>().UpdatePose();update();arm.BakeMesh(baked,true);
                            var vertices=baked.vertices;
                            for(int a=0;a<nail.Length;a++)for(int z=a+1;z<nail.Length;z++)
                            {
                                var actual=arm.transform.TransformVector(vertices[nail[a]]-vertices[nail[z]]);
                                var expected=arm.bones[26].TransformVector(binds[26].MultiplyVector(rest[nail[a]]-rest[nail[z]]));
                                error=Mathf.Max(error,Mathf.Abs(actual.magnitude-expected.magnitude));
                            }
                            count++;
                        }
                    }
                    if(count!=705||error>=.00002f)throw new InvalidOperationException("Nail rigidity gate failed");
                    results.Add(new{variant,frames=count,nailVertices=nail.Length,maximumNailDistanceErrorMeters=error,toleranceMeters=.00002f,passed=true,recordings,
                        modelSha256=Hash(Path.Combine(folder,variant+".mikumesh")),correctionsSha256=Hash(Path.Combine(folder,variant+"-corrections.json"))});
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);
                    if(candidateMesh!=null)UnityEngine.Object.DestroyImmediate(candidateMesh);
                    if(fixtureMesh!=null)UnityEngine.Object.DestroyImmediate(fixtureMesh);
                }
            }
            File.WriteAllText(output,JsonConvert.SerializeObject(results,Formatting.Indented));return "Passed 1410 recorded Unity BakeMesh frames";
        }
    }
}
