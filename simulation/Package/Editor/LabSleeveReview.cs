using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabSleeveReview
    {
        public static string PreviewRecorded(string variant, string capture, string output)
        {
            LabAssetLibrary.Clear();
            var frame=JObject.Parse(File.ReadLines(Path.Combine(capture,"frames.jsonl")).First());
            var character=frame["character"];
            var arm=character["renderers"].First(r=>r["role"].ToString()=="arm_left");
            JObject dump;
            using(var file=File.OpenRead(Path.Combine(capture,"renderers/00001.json.gz")))
            using(var zip=new GZipStream(file,CompressionMode.Decompress))
            using(var reader=new StreamReader(zip)) dump=JObject.Parse(reader.ReadToEnd());
            var root=new GameObject("Recorded sleeve preview");
            var surface=root.AddComponent<SkinnedMeshRenderer>();
            surface.sharedMesh=LabAssetLibrary.Get(variant).GetMesh("arm-left");
            var bones=dump["bonePaths"].Select(p=>new GameObject(p.ToString().Split('/').Last()).transform).ToArray();
            int[] parents=dump["boneParents"].ToObject<int[]>();
            SetMatrix(root.transform,arm["localToWorld"].ToObject<float[]>(),0);
            float[] values=arm["boneMatrices"].ToObject<float[]>();
            for(int i=0;i<bones.Length;i++){bones[i].SetParent(parents[i]>=0?bones[parents[i]]:root.transform,false);SetMatrix(bones[i],values,i*16);}
            surface.bones=bones;surface.rootBone=bones[0];surface.quality=SkinQuality.Bone4;surface.updateWhenOffscreen=true;
            var material=LabActor.MakeMaterial(variant,"body-albedo");
            surface.sharedMaterial=material;
            var cameraObject=new GameObject("Recorded first person camera");var camera=cameraObject.AddComponent<Camera>();
            float[] p=character["camera"]["position"].ToObject<float[]>(),q=character["camera"]["rotation"].ToObject<float[]>();
            camera.transform.SetPositionAndRotation(new Vector3(p[0],p[1],p[2]),new Quaternion(q[0],q[1],q[2],q[3]));
            camera.fieldOfView=55;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.15f,.15f);camera.aspect=16f/9;
            var texture=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try{camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(output,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(material);}
            return output;
        }
        private static void SetMatrix(Transform t,float[] values,int offset)
        {
            var matrix=new Matrix4x4();for(int r=0;r<4;r++)for(int c=0;c<4;c++)matrix[r,c]=values[offset+r*4+c];
            t.SetPositionAndRotation(matrix.GetColumn(3),matrix.rotation);Vector3 p=t.parent==null?Vector3.one:t.parent.lossyScale,s=matrix.lossyScale;t.localScale=new Vector3(s.x/p.x,s.y/p.y,s.z/p.z);
        }
        // Native Unity sampling, including additive fire on its weapon-specific base.
        public static string ExportPoses(string folder, bool allWeapons=false)
        {
            Directory.CreateDirectory(folder);
            var host = new GameObject("Sleeve clip audit");
            var rig = new LabRig("arms-rig", host.transform);
            var catalog = JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            var selected = catalog.Where(c => c.rig == "arms" && (allWeapons || c.label.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0 || (c.weaponKey ?? "").Contains("shotgun")))
                .GroupBy(c => c.resource + ":" + c.baseKey + ":" + c.additive).Select(g => g.First()).ToArray();
            var records = new List<object>();
            int total = 0;
            try
            {
                using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "clip-bones.bin"))))
                using (var weapons = new BinaryWriter(File.Create(Path.Combine(folder, "clip-weapons.bin"))))
                foreach (var entry in selected)
                {
                    var clip = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + entry.resource);
                    var basis = catalog.FirstOrDefault(c => c.key == entry.baseKey && c.rig == "arms");
                    var baseClip = basis == null ? clip : Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + basis.resource);
                    var graph = PlayableGraph.Create("Sleeve audit " + entry.key);
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var layer = AnimationLayerMixerPlayable.Create(graph, 2);
                        var primary = AnimationClipPlayable.Create(graph, entry.overlay ? baseClip : clip);
                        var overlay = AnimationClipPlayable.Create(graph, clip);
                        primary.SetApplyFootIK(false); primary.SetApplyPlayableIK(false); primary.SetSpeed(0);
                        overlay.SetApplyFootIK(false); overlay.SetApplyPlayableIK(false); overlay.SetSpeed(0);
                        graph.Connect(primary, 0, layer, 0); layer.SetInputWeight(0, 1);
                        graph.Connect(overlay, 0, layer, 1); layer.SetInputWeight(1, entry.overlay ? 1 : 0);
                        layer.SetLayerAdditive(1, entry.additive);
                        var output = AnimationPlayableOutput.Create(graph, "Arms", rig.AnimationRoot.GetComponent<Animator>());
                        output.SetSourcePlayable(layer); graph.Play();
                        int count = Math.Max(2, Mathf.CeilToInt(clip.length * 60) + 1);
                        for (int i = 0; i < count; i++)
                        {
                            rig.Restore(); double t = clip.length * i / (count - 1d);
                            primary.SetTime(entry.overlay ? t % baseClip.length : t); overlay.SetTime(t); graph.Evaluate(0);
                            foreach (var bone in rig.Data.renderers.SelectMany(r => r.bones).Select(i => rig.Nodes[i]))
                                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) writer.Write(bone.localToWorldMatrix[row,col]);
                            var weapon = rig.Nodes.Single(n => n.name == "cc_WeaponRoot");
                            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) weapons.Write(weapon.localToWorldMatrix[row,col]);
                        }
                        records.Add(new { entry.key, entry.label, entry.resource, entry.baseKey, entry.additive, entry.weaponKey, offset = total, count, duration = clip.length }); total += count;
                    }
                    finally { graph.Destroy(); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            string result = JsonConvert.SerializeObject(new { frames = total, bones = rig.Data.renderers.Sum(r => r.bones.Length), clips = records }, Formatting.Indented);
            File.WriteAllText(Path.Combine(folder, "clips.json"), result);
            return "Exported " + selected.Length + " native clips, " + total + " frames";
        }
    }
}
