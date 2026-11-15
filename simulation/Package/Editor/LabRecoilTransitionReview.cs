using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabRecoilTransitionReview
    {
        static object Get(object target,string name) => target.GetType().GetProperty(name).GetValue(target,null);
        static void Set(object target,string name,object value) => target.GetType().GetProperty(name).SetValue(target,value,null);
        static object Call(object target,string name,Type[] signature,params object[] args) => target.GetType().GetMethod(name,signature).Invoke(target,args);
        public static string Run(string animancerPath,string output)
        {
            var assembly=Assembly.LoadFrom(animancerPath);var type=assembly.GetType("Animancer.AnimancerComponent",true);
            var catalog=JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            Func<string,AnimationClip> clip=key=>Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/"+catalog.First(c=>c.key==key && c.rig=="arms").resource);
            var rows=new List<object>();
            foreach(string variant in new[]{"v1","v2","v3"})
            {
                var actor=new LabActor(new LabVariant{key=variant,label="Recoil transition"},true,false);
                try
                {
                    var animator=actor.Rig.AnimationRoot.GetComponent<Animator>();var component=actor.Rig.AnimationRoot.AddComponent(type);
                    Set(component,"Animator",animator);var layers=Get(component,"Layers");
                    var primary=layers.GetType().GetProperty("Item").GetValue(layers,new object[]{0});var recoil=layers.GetType().GetProperty("Item").GetValue(layers,new object[]{2});Set(recoil,"IsAdditive",true);
                    var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.Contains("arm-left"));
                    Action evaluate=()=>Call(component,"Evaluate",new[]{typeof(float)},0f);
                    foreach(string target in new[]{"clip-297","clip-313"})
                    {
                        Call(primary,"Play",new[]{typeof(AnimationClip)},clip(target));Set(Get(primary,"CurrentState"),"Time",.1f);Call(recoil,"Stop",Type.EmptyTypes);evaluate();
                        var baseline=arm.bones.Select(b=>b.position).ToArray();
                        foreach(var entry in catalog.Where(c=>c.key.StartsWith("weapon-bounce-shotgun-fire-") && c.rig=="arms"))
                        foreach(float time in new[]{.12f,clip(entry.key).length+3f})
                        {
                            var state=Call(recoil,"Play",new[]{typeof(AnimationClip)},clip(entry.key));Set(state,"Time",time);evaluate();
                            float before=Vector3.Distance(arm.bones[3].position,baseline[3]);
                            Call(recoil,"Stop",Type.EmptyTypes);evaluate();
                            float after=arm.bones.Select((b,i)=>Vector3.Distance(b.position,baseline[i])).Max();
                            bool cleared=Get(recoil,"CurrentState")==null && (float)Get(recoil,"Weight")==0;
                            // Gun.PlayAdditive takes its fresh-state branch after Stop clears
                            // CurrentState, so a shot during Enable is preserved.
                            var next=Call(recoil,"Play",new[]{typeof(AnimationClip)},clip("weapon-mini-cannon-fire-1"));Set(next,"Time",.12f);evaluate();
                            bool restarted=(float)Get(recoil,"Weight")>0 && (bool)Get(next,"IsPlaying");
                            if(!cleared || !restarted || after>1e-5f)throw new InvalidOperationException("Recoil reset/restart failed");
                            rows.Add(new{variant,target,source=entry.key,time,handOffsetBefore=before,maximumBoneOffsetAfter=after,cleared,restarted,passed=true});
                            Call(recoil,"Stop",Type.EmptyTypes);evaluate();
                        }
                    }
                }
                finally{actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
            }
            string result=JsonConvert.SerializeObject(rows,Formatting.Indented);File.WriteAllText(output,result);return "Validated "+rows.Count+" native Animancer transitions";
        }
    }
}
