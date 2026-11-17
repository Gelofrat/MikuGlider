using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MikuGlider;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabThirdPersonGrip
    {
        static Vector3 V(JToken a) {var x=a.ToObject<float[]>();return new Vector3(x[0],x[1],x[2]);}
        static Quaternion Q(JToken a) {var x=a.ToObject<float[]>();return new Quaternion(x[0],x[1],x[2],x[3]);}
        static float[] M(Matrix4x4 m)=>Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();

        public static string Export(string capture,int index,string folder,string output,string profile=null)
        {
            LabAssetLibrary.Clear();var actor=new LabActor(new LabVariant{key="v1",label="Third-person grip"},false);
            var fixture=new GameObject("Weapon fixture");WeaponModelCopy copy=null;var baked=new Mesh();
            try
            {
                var data=JObject.Parse(File.ReadAllText(Path.Combine(folder,"plate-source.json")));
                var frame=JObject.Parse(File.ReadLines(Path.Combine(capture,"frames.jsonl")).Skip(index).First());var ch=frame["character"];var body=actor.Body;
                var record=ch["renderers"].First(r=>r["role"].ToString()=="body");var origin=V(ch["camera"]["position"]);
                LabThumbReplay.SetWorld(body.transform,record["localToWorld"],origin);
                var matrices=(record["sourceBoneMatrices"]?.Type==JTokenType.Array?record["sourceBoneMatrices"]:record["boneMatrices"]).ToObject<float[]>();var native=body.GetComponent<ModelPoseCorrections>()?.SourceBones??body.bones;
                for(int i=0;i<native.Length;i++)LabThumbReplay.SetWorld(native[i],JArray.FromObject(matrices.Skip(i*16).Take(16).ToArray()),origin);
                var nodes=data["weapon"]["nodes"].ToArray();var objects=nodes.Select(n=>new GameObject(n["name"].ToString()).transform).ToArray();
                for(int i=0;i<nodes.Length;i++)
                {
                    var n=nodes[i];objects[i].SetParent((int)n["parent"]<0?fixture.transform:objects[(int)n["parent"]],false);
                    objects[i].SetLocalPositionAndRotation(V(n["position"]),Q(n["rotation"]));objects[i].localScale=V(n["scale"]);
                    if(n["mesh"].ToString()=="")continue;
                    objects[i].gameObject.AddComponent<MeshFilter>().sharedMesh=Resources.Load<Mesh>("MikuPhysicsLab/"+n["mesh"]);
                    objects[i].gameObject.AddComponent<MeshRenderer>();
                }
                var source=objects.Single(t=>t.name=="YAPlateLauncher");var handle=objects.Single(t=>t.name=="Handle");
                var prefab=new GameObject("Prefab").transform;prefab.SetParent(fixture.transform,false);
                var prefabHandle=new GameObject("Handle").transform;prefabHandle.SetParent(prefab,false);
                prefabHandle.SetLocalPositionAndRotation(V(data["handle"]["position"]),Q(data["handle"]["rotation"]));prefabHandle.localScale=V(data["handle"]["scale"]);
                copy=new WeaponModelCopy(source,native[39]);copy.Root.name="YAPlateLauncher";
                copy.Root.SetLocalPositionAndRotation(V(data["thirdPerson"]["position"]),Q(data["thirdPerson"]["rotation"]));copy.Root.localScale=V(data["thirdPerson"]["scale"]);
                copy.AddDetached(handle,prefabHandle,prefab);copy.Finish();copy.Sync();
                var clone=copy.Root.Find("Handle");if(clone==null||clone.GetComponent<MeshFilter>()==null)throw new Exception("Detached handle missing");
                var before=clone.localToWorldMatrix;var sourceBefore=handle.localToWorldMatrix;
                handle.SetLocalPositionAndRotation(new Vector3(5,9,-7),Quaternion.Euler(40,20,10));handle.localScale=Vector3.one*8;handle.gameObject.SetActive(false);copy.Sync();
                float error=Enumerable.Range(0,16).Max(i=>Mathf.Abs(before[i]-clone.localToWorldMatrix[i]));
                bool visible=clone.gameObject.activeInHierarchy&&clone.GetComponent<Renderer>().enabled;
                if(error>1e-6f||!visible)throw new Exception("First-person socket changed third-person attachment");
                float nativeError=0,segmentError=0;
                if(profile!=null)
                {
                    var original=native.Select(t=>t.localToWorldMatrix).ToArray();var lengths=native.Select(t=>t.localPosition.magnitude).ToArray();
                    ModelPoseCorrections.Attach(body,ModelCorrections.Parse(File.ReadAllText(profile)),copy.Root.parent);var component=body.GetComponent<ModelPoseCorrections>();
                    if(component==null||!component.enabled)throw new Exception("Third-person correction rejected");component.SendMessage("LateUpdate");
                    for(int i=0;i<native.Length;i++)
                    {
                        nativeError=Mathf.Max(nativeError,Enumerable.Range(0,16).Max(k=>Mathf.Abs(original[i][k]-native[i].localToWorldMatrix[k])));
                        if(i>=17&&i<=35&&i!=17&&i!=20&&i!=24&&i!=28&&i!=32)segmentError=Mathf.Max(segmentError,Mathf.Abs(body.bones[i].localPosition.magnitude-lengths[i]));
                    }
                    if(nativeError>0||segmentError>.00001f)throw new Exception("Native skeleton or phalanx lengths changed");
                }
                body.BakeMesh(baked,true);
                var result=new{index,camera=ch["camera"],origin=new[]{origin.x,origin.y,origin.z},fingerprint=ModelCorrections.Fingerprint(body.sharedMesh),
                    bones=native.Select(t=>M(t.localToWorldMatrix)).ToArray(),posedBones=body.bones.Select(t=>M(t.localToWorldMatrix)).ToArray(),
                    positions=baked.vertices.Select(p=>body.transform.TransformPoint(p)).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=baked.triangles,
                    uv=body.sharedMesh.uv.Select(p=>new[]{p.x,p.y}).ToArray(),
                    weapons=copy.Root.GetComponentsInChildren<MeshFilter>().Select(w=>new{name=w.name,positions=w.sharedMesh.vertices.Select(p=>w.transform.TransformPoint(p)).Select(p=>new[]{p.x,p.y,p.z}).ToArray(),triangles=w.sharedMesh.triangles}).ToArray(),
                    attachmentCheck=new{present=true,visible,maximumMatrixChange=error,meshes=copy.Root.GetComponentsInChildren<MeshFilter>().Length},rigCheck=new{nativeMatrixChange=nativeError,phalanxLengthChange=segmentError}};
                File.WriteAllText(output,JsonConvert.SerializeObject(result));return "Third-person pose and detached handle exported";
            }
            finally{copy?.Dispose();UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(fixture);actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
        }
    }
}
