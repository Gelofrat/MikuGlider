using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabHandRigChecks
    {
        public static string MathChecks(string output)
        {
            var root=new GameObject("Hand IK math checks");
            float endpoint=0,length=0,shoulder=0,rotationError=0,curlError=0,hingeExcess=0;
            int cases=0,curlCases=0,hingeCases=0;
            try
            {
                var a=root.transform;
                var b=new GameObject("Elbow").transform;b.SetParent(a,false);
                var c=new GameObject("Wrist").transform;c.SetParent(b,false);
                foreach(float scale in new[]{.5f,1f,1.7f})
                    for(int i=0;i<80;i++)
                    {
                        a.SetPositionAndRotation(new Vector3(21,-7,13),Quaternion.Euler(11,37,9));a.localScale=Vector3.one*scale;
                        b.localPosition=new Vector3(.42f,0,0);b.localRotation=Quaternion.Euler(0,0,42);
                        c.localPosition=new Vector3(.36f,0,0);c.localRotation=Quaternion.identity;
                        var start=a.position;float la=Vector3.Distance(a.position,b.position),lb=Vector3.Distance(b.position,c.position);
                        var direction=new Vector3(Mathf.Cos(i*.31f),Mathf.Sin(i*.31f),.2f).normalized;
                        float reach=(i%4==0?1.1f:i%4==1?.02f:.45f)*scale;
                        var target=start+direction*reach;
                        HandKinematics.SolveArm(a,b,c,target,b.position);
                        float projected=Mathf.Clamp(reach,Mathf.Abs(la-lb)+1e-6f,la+lb-1e-6f);
                        endpoint=Mathf.Max(endpoint,Vector3.Distance(c.position,start+direction*projected));
                        length=Mathf.Max(length,Mathf.Abs(la-Vector3.Distance(a.position,b.position)),Mathf.Abs(lb-Vector3.Distance(b.position,c.position)));
                        shoulder=Mathf.Max(shoulder,Vector3.Distance(start,a.position));cases++;
                    }
                var joint=new HandJoint{rotation=Quaternion.Euler(5,11,17),sourceRotation=Quaternion.Euler(2,-45,7),axisMap=Quaternion.Euler(-7,12,3)};
                rotationError=Quaternion.Angle(HandKinematics.Retarget(joint,joint.sourceRotation),joint.rotation);
                // Swing and quaternion sign must not masquerade as finger curl.
                foreach(float close in new[]{-120f,-45f,45f,120f})
                    foreach(float fraction in new[]{0f,.25f,.5f,1f,1.25f})
                        foreach(float swing in new[]{-70f,0f,70f})
                            foreach(float sign in new[]{-1f,1f})
                            {
                                var rest=Quaternion.Euler(12,37,-21);
                                var current=rest*Quaternion.AngleAxis(swing,Vector3.right)*Quaternion.AngleAxis(close*fraction,Vector3.forward);
                                current=new Quaternion(current.x*sign,current.y*sign,current.z*sign,current.w*sign);
                                float curl=HandKinematics.Curl(rest,rest*Quaternion.AngleAxis(close,Vector3.forward),current,Vector3.forward);
                                curlError=Mathf.Max(curlError,Mathf.Abs(curl-Mathf.Clamp01(fraction)));curlCases++;
                            }
                var chain=new Transform[28];chain[24]=a;chain[25]=b;chain[26]=c;
                var seed=new Quaternion[28];var axes=new Vector3[28];
                for(int i=24;i<=26;i++){seed[i]=Quaternion.identity;axes[i]=Vector3.forward;}
                var contact=new FingerContact{root=24,tip=26,localPoint=new Vector3(.02f,0,0),maxAngle=12};
                foreach(float weight in new[]{0f,.3f,1f})
                    foreach(float bend in new[]{-.04f,.04f})
                    {
                        a.SetPositionAndRotation(Vector3.zero,Quaternion.identity);a.localScale=Vector3.one;
                        b.localPosition=new Vector3(.03f,0,0);c.localPosition=new Vector3(.025f,0,0);
                        b.localRotation=c.localRotation=Quaternion.identity;
                        var target=new Vector3(.055f,bend,0);float before=Vector3.Distance(c.TransformPoint(contact.localPoint),target);
                        // Repeated iterations must stay inside an absolute envelope.
                        for(int pass=0;pass<10;pass++)HandKinematics.SolveFinger(chain,contact,target,seed,axes,weight);
                        for(int i=24;i<=26;i++)hingeExcess=Mathf.Max(hingeExcess,Mathf.Abs(HandKinematics.SignedTwist(chain[i].localRotation,axes[i]))-12*weight);
                        if(Vector3.Distance(c.TransformPoint(contact.localPoint),target)>before+1e-6f||Mathf.Abs(b.localPosition.magnitude-.03f)>1e-6f||Mathf.Abs(c.localPosition.magnitude-.025f)>1e-6f)
                            throw new InvalidOperationException("Finger contact increased error or changed segment length");
                        hingeCases++;
                    }
                bool passed=endpoint<.00003f&&length<.00001f&&shoulder==0&&rotationError<.05f&&curlError<.00001f&&hingeExcess<.0001f;
                var report=new{passed,cases,curlCases,hingeCases,maximumEndpointErrorMeters=endpoint,maximumLengthErrorMeters=length,maximumShoulderMotionMeters=shoulder,restRotationErrorDegrees=rotationError,curlError,hingeExcess};
                File.WriteAllText(output,JsonConvert.SerializeObject(report,Formatting.Indented));
                if(!passed)throw new InvalidOperationException(JsonConvert.SerializeObject(report));
                return JsonConvert.SerializeObject(report);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        public static string ExportCandidates(string folder,string captures)
        {
            foreach(var item in new[]{new[]{"v1","left","20261116-180944-759-d974ee"},new[]{"v3","right","20261116-181646-129-73fa08"}})
                LabThumbReplay.Export(item[0],Path.Combine(captures,item[2]),0,Path.Combine(folder,item[0]+"-candidate.json"),null,Path.Combine(folder,item[0]+"-corrections.json"),true,item[1]);
            return "Exported anatomical controls and skinned surfaces for both candidates";
        }

        public static string SchemaChecks(string folder)
        {
            int rejected=0;
            foreach(string variant in new[]{"v1","v3"})
            {
                string json=File.ReadAllText(Path.Combine(folder,variant+"-corrections.json"));
                var valid=ModelCorrections.Parse(json).meshes.Single(m=>m.handRig!=null);
                valid.handRig.Validate(37,valid.poses);
                foreach(Action<HandRigDefinition> corrupt in new Action<HandRigDefinition>[]
                {
                    r=>r.grips[0].intent[1]=r.grips[0].intent[0],
                    r=>r.grips[0].intent=r.grips[0].intent.Where(d=>d.bone<24).ToArray(),
                    r=>r.joints[0].position=new Vector3(float.NaN,0,0),
                    r=>r.grips[0].closed[0]=default,
                    r=>r.grips[0].contacts[0].maxAngle=-1,
                    r=>r.grips[0].skinPose=99999
                })
                {
                    var candidate=ModelCorrections.Parse(json).meshes.Single(m=>m.handRig!=null);corrupt(candidate.handRig);
                    try{candidate.handRig.Validate(37,candidate.poses);}
                    catch(InvalidDataException){rejected++;continue;}
                    throw new InvalidOperationException("Invalid anatomical profile was accepted");
                }
            }
            return "Rejected "+rejected+" invalid anatomical profiles";
        }

        public static string BindingChecks(string folder)
        {
            var root=new GameObject("Anatomical rig lobby rebind checks");
            var meshes=new List<Mesh>();var aMesh=new Mesh();var bMesh=new Mesh();float error=0;int checks=0;
            try
            {
                var oldRig=new LabRig("arms-rig",root.transform);var nextRig=new LabRig("arms-rig",root.transform);
                nextRig.Root.transform.SetPositionAndRotation(new Vector3(17,-6,23),Quaternion.Euler(13,71,-5));
                foreach(string variant in new[]{"v1","v3","v1"})
                {
                    int side=variant=="v1"?0:1;var assets=LabAssetLibrary.Get(variant);
                    var original=assets.GetMesh(side==0?"arm-left":"arm-right");var prepared=ModelPoseCorrections.GetApplied(original);
                    var raw=UnityEngine.Object.Instantiate(original);raw.name=original.name;raw.ClearBlendShapes();meshes.Add(raw);
                    if(prepared!=null)raw.boneWeights=prepared.OriginalWeights;
                    var profile=ModelCorrections.Parse(File.ReadAllText(Path.Combine(folder,variant+"-corrections.json")));
                    var oldBones=oldRig.Data.renderers[side].bones.Select(i=>oldRig.Nodes[i]).ToArray();
                    var nextBones=nextRig.Data.renderers[side].bones.Select(i=>nextRig.Nodes[i]).ToArray();
                    var a=new GameObject("Reused "+variant).AddComponent<SkinnedMeshRenderer>();
                    var b=new GameObject("Fresh "+variant).AddComponent<SkinnedMeshRenderer>();
                    a.transform.SetParent(root.transform,false);b.transform.SetParent(root.transform,false);
                    a.sharedMesh=b.sharedMesh=raw;a.bones=oldBones;b.bones=nextBones;
                    if(side==0)FirstPersonWristVisual.Attach(a,raw,assets.ModelDefinition);
                    ModelPoseCorrections.Attach(a,profile);
                    if(a.GetComponent<ModelPoseCorrections>()?.HandRig==null)throw new InvalidOperationException("Missing anatomical controls before rebind");
                    a.bones=nextBones;
                    foreach(var surface in new[]{a,b})
                    {
                        if(side==0)FirstPersonWristVisual.Attach(surface,raw,assets.ModelDefinition);
                        ModelPoseCorrections.Attach(surface,profile);var correction=surface.GetComponent<ModelPoseCorrections>();
                        if(correction?.HandRig==null||!correction.SourceBones.Take(28).SequenceEqual(nextBones.Take(28)))
                            throw new InvalidOperationException("Anatomical hand retained a departed lobby skeleton");
                        correction.EvaluatePose(0);
                        for(int i=0;i<28;i++)for(int k=0;k<16;k++)
                            if(Mathf.Abs(surface.bones[i].localToWorldMatrix[k]-nextBones[i].localToWorldMatrix[k])>1e-5f)
                                throw new InvalidOperationException("An unconfigured weapon received an anatomical joint correction");
                    }
                    a.BakeMesh(aMesh,true);b.BakeMesh(bMesh,true);var av=aMesh.vertices;var bv=bMesh.vertices;
                    for(int i=0;i<av.Length;i++)error=Mathf.Max(error,Vector3.Distance(av[i],bv[i]));
                    ModelPoseCorrections.PrepareMeshChange(a);ModelPoseCorrections.PrepareMeshChange(b);
                    if(!a.bones.SequenceEqual(nextBones)||!b.bones.SequenceEqual(nextBones))throw new InvalidOperationException("Anatomical detach retained temporary bones");
                    UnityEngine.Object.DestroyImmediate(a.gameObject);UnityEngine.Object.DestroyImmediate(b.gameObject);checks++;
                }
                if(error>.00001f)throw new InvalidOperationException("Anatomical rebind surface mismatch "+error);
                string report=JsonConvert.SerializeObject(new{passed=true,checks,maximumSurfaceDifferenceMeters=error});
                File.WriteAllText(Path.Combine(folder,"binding-checks.json"),report);return report;
            }
            finally
            {
                foreach(var surface in root.GetComponentsInChildren<SkinnedMeshRenderer>())ModelPoseCorrections.PrepareMeshChange(surface);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(aMesh);UnityEngine.Object.DestroyImmediate(bMesh);
                foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        public static string Replay(string folder,string captures,bool legacy=false)
        {
            var reports=new List<object>();
            foreach(var item in new[]{new[]{"v1","left","20261116-180944-759-d974ee"},new[]{"v1","left","20261116-162906-805-0aca39"},new[]{"v3","right","20261116-181646-129-73fa08"}})
            {
                string variant=item[0],side=item[1],capture=item[2];LabAssetLibrary.Clear();
                var actor=new LabActor(new LabVariant{key=variant,label="Anatomical replay"},true,false);
                Mesh raw=null,baked=new Mesh();
                try
                {
                    string key=variant=="v1"?"clip-313":"clip-297";
                    var entry=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips.First(c=>c.key==key);
                    var clip=Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);actor.Animate(entry,clip,clip,1,0,false,false);
                    var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.EndsWith("arm-"+side));
                    ModelPoseCorrections.PrepareMeshChange(arm);var original=actor.Skin.Assets.GetMesh("arm-"+side);var applied=ModelPoseCorrections.GetApplied(original);
                    raw=UnityEngine.Object.Instantiate(original);raw.name=original.name;raw.ClearBlendShapes();if(applied!=null)raw.boneWeights=applied.OriginalWeights;arm.sharedMesh=raw;
                    if(side=="left")FirstPersonWristVisual.Attach(arm,raw,actor.Skin.Assets.ModelDefinition);
                    var weaponRoot=actor.Rig.Nodes.Single(t=>t.name=="cc_WeaponRoot");
                    var profile=ModelCorrections.Parse(File.ReadAllText(Path.Combine(folder,variant+(legacy?"-original-corrections.json":"-corrections.json"))));
                    ModelPoseCorrections.Attach(arm,profile,weaponRoot,"Native weapon ");var correction=arm.GetComponent<ModelPoseCorrections>();
                    if(correction==null||(!legacy&&correction.HandRig==null))throw new InvalidOperationException("Anatomical candidate was rejected");
                    var source=correction.SourceBones;var filters=actor.Root.GetComponentsInChildren<MeshFilter>();
                    var nailGroups=JObject.Parse(File.ReadAllText(Path.Combine(folder,variant+"-nails.json"))).Properties().Select(p=>p.Value.ToObject<int[]>()).ToArray();
                    var nailDistances=nailGroups.Select(ids=>new float[ids.Length*ids.Length]).ToArray();
                    float sourceError=0,shoulderError=0,lengthError=0,nailError=0,contactError=0,maxWeightStep=0,previousWeight=0;
                    var frameRows=new List<object>();int count=0;double previousTime=0;
                    using(var writer=new BinaryWriter(File.Create(Path.Combine(folder,variant+"-"+capture+(legacy?"-legacy":"")+"-positions.bin"))))
                    foreach(string line in File.ReadLines(Path.Combine(captures,capture,"frames.jsonl")))
                    {
                        var frame=JObject.Parse(line);var ch=frame["character"];var record=ch["renderers"].First(r=>r["role"].ToString()=="arm_"+side);
                        var cp=ch["camera"]["position"].ToObject<float[]>();var origin=new Vector3(cp[0],cp[1],cp[2]);
                        LabThumbReplay.SetWorld(arm.transform,record["localToWorld"],origin);
                        var matrices=(record["sourceBoneMatrices"]?.Type==JTokenType.Array?record["sourceBoneMatrices"]:record["boneMatrices"]).ToObject<float[]>();
                        for(int i=0;i<28;i++)LabThumbReplay.SetWorld(source[i],JArray.FromObject(matrices.Skip(i*16).Take(16).ToArray()),origin);
                        LabThumbReplay.SetWorld(weaponRoot,ch["weaponMatrix"],origin);
                        foreach(var w in ch["renderers"].Where(r=>r["role"].ToString()=="weapon"))
                        {
                            string leaf=w["path"].ToString().Split('/').Last();var f=filters.FirstOrDefault(m=>m.name=="Native weapon "+leaf);
                            if(f!=null)LabThumbReplay.SetWorld(f.transform,w["localToWorld"],origin);
                        }
                        var before=source.Take(28).Select(t=>t.localToWorldMatrix).ToArray();
                        double time=(double)frame["realtime"];float dt=count==0?0:(float)(time-previousTime);
                        correction.EvaluatePose(dt);var rig=correction.HandRig;arm.BakeMesh(baked,true);var p=baked.vertices;
                        foreach(var point in p){var world=arm.transform.TransformPoint(point);writer.Write(world.x);writer.Write(world.y);writer.Write(world.z);}
                        for(int i=0;i<28;i++)for(int k=0;k<16;k++)sourceError=Mathf.Max(sourceError,Mathf.Abs(before[i][k]-source[i].localToWorldMatrix[k]));
                        var controls=rig?.Controls??arm.bones;
                        shoulderError=Mathf.Max(shoulderError,Vector3.Distance(controls[0].position,source[0].position));
                        for(int i=1;i<28;i++)
                        {
                            float value=controls[i].localPosition.magnitude;
                            // Captured native arm translations have float quantization
                            // noise. Compare arm lengths to this frame's native input;
                            // finger lengths to their fixed anatomical calibration.
                            float expected=i<4||legacy?source[i].localPosition.magnitude:profile.meshes.First(m=>m.handRig!=null).handRig.joints[i-4].position.magnitude;
                            lengthError=Mathf.Max(lengthError,Mathf.Abs(value-expected));
                        }
                        for(int n=0;n<nailGroups.Length;n++)for(int i=0;i<nailGroups[n].Length;i++)for(int j=i+1;j<nailGroups[n].Length;j++)
                        {
                            int[] ids=nailGroups[n];float distance=arm.transform.TransformVector(p[ids[i]]-p[ids[j]]).magnitude;int pair=i*ids.Length+j;
                            if(count==0)nailDistances[n][pair]=distance;else nailError=Mathf.Max(nailError,Mathf.Abs(distance-nailDistances[n][pair]));
                        }
                        float gripWeight=rig?.Weight??arm.GetBlendShapeWeight(arm.sharedMesh.blendShapeCount-1)/100;
                        if((rig?.ContactWeight??0)>.99f)contactError=Mathf.Max(contactError,rig?.ContactError??0);
                        if(dt>0&&dt<.067f)maxWeightStep=Mathf.Max(maxWeightStep,Mathf.Abs(gripWeight-previousWeight)/(dt*60));
                        frameRows.Add(new{frame=count,weight=gripWeight,contactWeight=rig?.ContactWeight??0,contactError=rig?.ContactError??0});previousWeight=gripWeight;previousTime=time;count++;
                    }
                    var rendererBones=arm.bones;for(int i=0;i<5;i++)ModelPoseCorrections.Attach(arm,profile,weaponRoot,"Native weapon ");
                    bool stable=arm.bones.SequenceEqual(rendererBones)&&correction.SourceBones.SequenceEqual(source);
                    correction.enabled=false;correction.SendMessage("OnDisable");bool released=arm.bones[7]==source[7];correction.enabled=true;correction.EvaluatePose(0);bool rebound=arm.bones[7]!=source[7];
                    // Warm up and invoke the exact runtime update without reflection.
                    for(int i=0;i<50;i++)correction.EvaluatePose(1f/60);
                    long allocation=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)correction.EvaluatePose(1f/60);allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
                    bool passed=sourceError==0&&shoulderError<1e-6f&&lengthError<.00001f&&nailError<.00002f&&allocation==0&&stable&&released&&rebound;
                    reports.Add(new{variant,capture,frames=count,vertices=raw.vertexCount,sourceError,shoulderError,lengthError,nailError,contactError,maxWeightStep,steadyStateAllocatedBytes=allocation,stable,released,rebound,passed,timeline=frameRows});
                }
                finally{actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);UnityEngine.Object.DestroyImmediate(baked);if(raw!=null)UnityEngine.Object.DestroyImmediate(raw);}
            }
            File.WriteAllText(Path.Combine(folder,legacy?"legacy-replay-checks.json":"replay-checks.json"),JsonConvert.SerializeObject(reports,Formatting.Indented));
            return JsonConvert.SerializeObject(reports.Select(r=>JObject.FromObject(r)).Select(r=>{r.Remove("timeline");return r;}));
        }
    }
}
