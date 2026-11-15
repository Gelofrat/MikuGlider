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
    public static class LabCyclerThumbChecks
    {
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        public static string Run(string review, string output)
        {
            LabAssetLibrary.Clear();
            var results = new List<object>();
            foreach (string variant in new[] { "v1", "v2", "v3" })
            {
                var actor = new LabActor(new LabVariant { key=variant, label="Cycler checks" }, true);
                try
                {
                    var catalog = JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
                    var entry = catalog.First(c=>c.key=="clip-381");
                    var clip = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+entry.resource);
                    actor.Animate(entry, clip, clip, 1, 1, false, false);
                    var arm = actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.Contains("arm-left"));
                    var correction = arm.GetComponent<ModelPoseCorrections>();
                    Require(correction!=null && correction.enabled, variant+": profile was rejected");
                    var update = (Action)Delegate.CreateDelegate(typeof(Action), correction,
                        typeof(ModelPoseCorrections).GetMethod("LateUpdate", BindingFlags.Instance|BindingFlags.NonPublic));
                    var data = JObject.Parse(File.ReadAllText(Path.Combine(review, variant+"-before.png.pose.json")));
                    foreach (int index in new[] {24,25})
                    {
                        var q=data["rotations"][index]["rotation"];
                        arm.bones[index].localRotation=new Quaternion((float)q["x"],(float)q["y"],(float)q["z"],(float)q["w"]);
                    }
                    Require(arm.sharedMesh.blendShapeCount==2 && arm.bones.Length==37, "Unexpected skinning layout");
                    update(); Require(arm.GetBlendShapeWeight(0)==100, "Cycler grip did not activate");
                    Quaternion closed=arm.bones[25].localRotation;
                    arm.bones[25].localRotation=closed*Quaternion.AngleAxis(65,Vector3.forward);
                    update(); Require(arm.GetBlendShapeWeight(0)==0, "Open thumb did not release correction");
                    arm.bones[25].localRotation=closed*Quaternion.AngleAxis(32.5f,Vector3.forward);
                    update(); Require(Mathf.Abs(arm.GetBlendShapeWeight(0)-50)<.02f, "Pose transition is not smooth");
                    arm.bones[25].localRotation=closed;
                    var root=actor.Rig.Nodes.First(n=>n.name=="cc_WeaponRoot");
                    var weapon=Enumerable.Range(0,root.childCount).Select(i=>root.GetChild(i)).First(t=>t.name=="Native weapon SMG");
                    weapon.gameObject.SetActive(false); update();
                    Require(arm.GetBlendShapeWeight(0)==0,"Inactive weapon retained corrective");
                    int children=root.childCount;
                    weapon.SetParent(actor.Root.transform,true);
                    var other=new GameObject("Native weapon DMLR");other.transform.SetParent(root,false);
                    Require(root.childCount==children,"Weapon swap fixture changed child count");
                    update(); Require(arm.GetBlendShapeWeight(0)==0,"Same-count weapon swap leaked correction");
                    UnityEngine.Object.DestroyImmediate(other);weapon.SetParent(root,true);weapon.gameObject.SetActive(true);
                    update();Require(arm.GetBlendShapeWeight(0)==100,"Re-equipped Cycler failed");
                    correction.enabled=false;
                    if(!Application.isPlaying) correction.SendMessage("OnDisable");
                    Require(arm.GetBlendShapeWeight(0)==0,"Disable did not reset shape");
                    correction.enabled=true;update();Require(arm.GetBlendShapeWeight(0)==100,"Re-enable did not restore shape");
                    // Swap to the actual shotgun gunModel name with the same child count.
                    var originalThumb= new[] {arm.bones[24].localRotation,arm.bones[25].localRotation};
                    weapon.SetParent(actor.Root.transform,true);
                    var shotgun=new GameObject("Native weapon Lever Action Shotgun");shotgun.transform.SetParent(root,false);
                    var shotgunData=JObject.Parse(File.ReadAllText(Path.Combine(review,variant+"-clip-367.png.pose.json")));
                    foreach(int index in new[]{24,25})
                    {
                        var q=shotgunData["rotations"][index]["rotation"];
                        arm.bones[index].localRotation=new Quaternion((float)q["x"],(float)q["y"],(float)q["z"],(float)q["w"]);
                    }
                    update();Require(arm.GetBlendShapeWeight(0)==0 && arm.GetBlendShapeWeight(1)==100,"Shotgun and Cycler scopes overlap");
                    Quaternion shotgunClosed=arm.bones[25].localRotation;
                    arm.bones[25].localRotation=shotgunClosed*Quaternion.AngleAxis(65,Vector3.forward);
                    update();Require(arm.GetBlendShapeWeight(1)==0,"Open shotgun thumb did not release");
                    arm.bones[25].localRotation=shotgunClosed*Quaternion.AngleAxis(32.5f,Vector3.forward);
                    update();Require(Mathf.Abs(arm.GetBlendShapeWeight(1)-50)<.02f,"Shotgun pose transition failed");
                    UnityEngine.Object.DestroyImmediate(shotgun);weapon.SetParent(root,true);
                    arm.bones[24].localRotation=originalThumb[0];arm.bones[25].localRotation=originalThumb[1];
                    update();Require(arm.GetBlendShapeWeight(0)==100 && arm.GetBlendShapeWeight(1)==0,"Shotgun correction leaked after swap");
                    for(int i=0;i<1000;i++)update();
                    var samples=new double[7];var timer=new System.Diagnostics.Stopwatch();
                    long allocated=GC.GetAllocatedBytesForCurrentThread();
                    for(int sample=0;sample<samples.Length;sample++)
                    {
                        timer.Restart();for(int i=0;i<100000;i++)update();timer.Stop();
                        samples[sample]=timer.Elapsed.TotalMilliseconds/100000;
                    }
                    allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                    Require(allocated==0,"Corrective allocates per update");
                    var changingSamples=new double[7];
                    Quaternion shallow=closed*Quaternion.AngleAxis(20,Vector3.forward);
                    Quaternion deep=closed*Quaternion.AngleAxis(40,Vector3.forward);
                    long changingAllocated=GC.GetAllocatedBytesForCurrentThread();
                    for(int sample=0;sample<changingSamples.Length;sample++)
                    {
                        timer.Restart();
                        for(int i=0;i<100000;i++){arm.bones[25].localRotation=(i&1)==0?shallow:deep;update();}
                        timer.Stop();changingSamples[sample]=timer.Elapsed.TotalMilliseconds/100000;
                    }
                    changingAllocated=GC.GetAllocatedBytesForCurrentThread()-changingAllocated;
                    Require(changingAllocated==0,"Changing corrective allocates per update");
                    results.Add(new {variant,vertices=arm.sharedMesh.vertexCount,bones=arm.bones.Length,
                        shapes=arm.sharedMesh.blendShapeCount,allocatedBytes=allocated,millisecondsPerUpdate=samples,
                        changingAllocatedBytes=changingAllocated,changingMillisecondsIncludingDriverWrite=changingSamples,
                        activation=true,openHandRelease=true,smoothTransition=true,weaponIsolation=true,disableReset=true,
                        shotgunIsolation=true,shotgunOpenRelease=true,shotgunTransition=true});
                }
                finally {actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
            }
            string json=JsonConvert.SerializeObject(results,Formatting.Indented);File.WriteAllText(output,json);return json;
        }
    }
}
