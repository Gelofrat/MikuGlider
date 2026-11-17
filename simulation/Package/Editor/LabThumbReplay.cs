using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    // Replay the captured final bone matrices, including native animation transitions.
    public static class LabThumbReplay
    {
        internal static void SetWorld(Transform t, JToken values, Vector3 origin = default(Vector3))
        {
            var a=values.ToObject<float[]>(); var m=new Matrix4x4();
            for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=a[r*4+c];
            m.m03-=origin.x;m.m13-=origin.y;m.m23-=origin.z;
            t.SetPositionAndRotation(m.GetColumn(3),m.rotation);
            var p=t.parent==null?Vector3.one:t.parent.lossyScale;var s=m.lossyScale;
            t.localScale=new Vector3(s.x/p.x,s.y/p.y,s.z/p.z);
        }
        private static float[] Matrix(Matrix4x4 m) => Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();
        public static string Export(string variant,string capture,int index,string output,string candidate=null,string corrections=null,bool originRelative=false,string side="left")
        {
            LabAssetLibrary.Clear();
            var frame=JObject.Parse(File.ReadLines(Path.Combine(capture,"frames.jsonl")).Skip(index).First());var ch=frame["character"];
            var cp=ch["camera"]["position"].ToObject<float[]>();var origin=originRelative?new Vector3(cp[0],cp[1],cp[2]):Vector3.zero;
            var actor=new LabActor(new LabVariant{key=variant,label="Recorded thumb"},true,false);
            try
            {
                var catalog=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
                string weapon=ch["weapon"].ToString();
                string clipKey=weapon.Contains("Plate")?"clip-313":weapon.Contains("MiniCannon")?"clip-297":weapon.Contains("SMG")?"clip-381":"clip-367";
                var entry=catalog.First(c=>c.key==clipKey);var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
                actor.Animate(entry,clip,clip,1,0,false,false);
                var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-"+side));
                var record=ch["renderers"].First(r=>r["role"].ToString()=="arm_"+side);
                SetWorld(arm.transform,record["localToWorld"],origin);
                var matrices=(record["sourceBoneMatrices"]?.Type==JTokenType.Array ? record["sourceBoneMatrices"] : record["boneMatrices"]).ToObject<float[]>();
                var nativeSources=arm.GetComponent<ModelPoseCorrections>()?.SourceBones ?? arm.bones;
                for(int i=0;i<28;i++)SetWorld(nativeSources[i],JArray.FromObject(matrices.Skip(i*16).Take(16).ToArray()),origin);
                if(candidate!=null)LabHandReview.Candidate(arm,candidate);
                if(side=="left")
                {
                    FirstPersonWristVisual.Attach(arm,arm.sharedMesh,new SkinModelDefinition{firstPersonWristTwistLength=variant=="v3"?.24f:0});
                    arm.GetComponent<FirstPersonWristVisual>().UpdatePose();
                }
                var profile=corrections==null?actor.Skin.Assets.Corrections:ModelCorrections.Parse(File.ReadAllText(corrections));
                SetWorld(actor.Rig.Nodes.Single(t=>t.name=="cc_WeaponRoot"),ch["weaponMatrix"],origin);
                ModelPoseCorrections.Attach(arm,profile,actor.Rig.Nodes.First(t=>t.name=="cc_WeaponRoot"),"Native weapon ");
                var correction=arm.GetComponent<ModelPoseCorrections>();
                // CharacterCapture records PlayerAnimation.WeaponRoot (the rig socket).
                SetWorld(actor.Rig.Nodes.Single(t=>t.name=="cc_WeaponRoot"),ch["weaponMatrix"],origin);
                var weaponFilters=actor.Root.GetComponentsInChildren<MeshFilter>();
                foreach(var rec in ch["renderers"].Where(r=>r["role"].ToString()=="weapon"))
                {
                    string name=rec["path"].ToString().Split('/').Last();
                    var filter=weaponFilters.FirstOrDefault(f=>f.name=="Native weapon "+name || f.name==name);
                    if(filter!=null)SetWorld(filter.transform,rec["localToWorld"],origin);
                }
                var sourceBefore=nativeSources.Take(28).Select(t=>t.localToWorldMatrix).ToArray();
                if(correction!=null&&correction.enabled)correction.SendMessage("LateUpdate");
                var firstPose=arm.bones.Take(28).Select(t=>t.localToWorldMatrix).ToArray();
                for(int repeat=0;repeat<3;repeat++)if(correction!=null&&correction.enabled)correction.SendMessage("LateUpdate");
                float sourceChange=0,feedbackDrift=0;
                for(int i=0;i<28;i++)for(int k=0;k<16;k++)
                {
                    sourceChange=Mathf.Max(sourceChange,Mathf.Abs(sourceBefore[i][k]-nativeSources[i].localToWorldMatrix[k]));
                    feedbackDrift=Mathf.Max(feedbackDrift,Mathf.Abs(firstPose[i][k]-arm.bones[i].localToWorldMatrix[k]));
                }
                if(sourceChange>1e-6f||feedbackDrift>1e-6f)throw new InvalidOperationException("Pose correction mutated source bones or accumulated feedback.");
                // Match the runtime BakeMesh path; CPU parity is checked after export.
                var baked=new Mesh();arm.BakeMesh(baked,true);
                try
                {
                    var data=new{
                        index,sample=(int)frame["sample"],realtime=(double)frame["realtime"],camera=ch["camera"],origin=new[]{origin.x,origin.y,origin.z},
                        rigCheck=new{sourceMatrixChange=sourceChange,feedbackMatrixDrift=feedbackDrift},
                        weaponMatrix=Matrix(actor.Rig.Nodes.Single(t=>t.name=="cc_WeaponRoot").localToWorldMatrix),
                        fingerprint=ModelCorrections.Fingerprint(arm.sharedMesh),meshKey=ModelCorrections.Key(arm.sharedMesh),
                        boneNames=nativeSources.Take(28).Select(t=>t.name).ToArray(),boneParents=nativeSources.Take(28).Select(t=>Array.IndexOf(nativeSources,t.parent)).ToArray(),
                        bones=(correction!=null?correction.SourceBones:arm.bones).Take(28).Select(t=>Matrix(t.localToWorldMatrix)).ToArray(),
                        posedBones=arm.bones.Take(28).Select(t=>Matrix(t.localToWorldMatrix)).ToArray(),
                        positions=baked.vertices.Select(p=>arm.transform.TransformPoint(p)).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),
                        triangles=baked.triangles,uv=arm.sharedMesh.uv.Select(p=>new[]{p.x,p.y}).ToArray(),
                        shapeWeights=Enumerable.Range(0,arm.sharedMesh.blendShapeCount).Select(i=>arm.GetBlendShapeWeight(i)).ToArray(),
                        weapons=weaponFilters.Select(m=>new{name=m.name,positions=m.sharedMesh.vertices.Select(p=>m.transform.TransformPoint(p)).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=m.sharedMesh.triangles}).ToArray()
                    };
                    File.WriteAllText(output,JsonConvert.SerializeObject(data));
                }
                finally{UnityEngine.Object.DestroyImmediate(baked);}
            }
            finally{actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
            return output;
        }
    }
}
