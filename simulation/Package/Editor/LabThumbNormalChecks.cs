using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using MikuGlider;
namespace Horse.MikuPhysicsLab.Editor
{
 public static class LabThumbNormalChecks
 {
  public static string Run(string output)
  {
   var p=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.zero,Vector3.up,Vector3.forward};
   var n=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.left,Vector3.left,Vector3.left};
   var q=Quaternion.Euler(31,17,-9);var moved=p.Select(x=>q*x).ToArray();
   var d=ModelPoseCorrections.CalculateNormalDeltas(p,moved,n,new[]{0,1,2,3,4,5});
   for(int i=0;i<p.Length;i++)if(Vector3.Distance(n[i]+d[i],q*n[i])>1e-5f)throw new Exception("Clockwise normal rotation or hard-edge preservation failed");
   var none=ModelPoseCorrections.CalculateNormalDeltas(p,p,n,new[]{0,1,2,3,4,5});
   if(none.Any(x=>x.sqrMagnitude>1e-12f))throw new Exception("Unchanged surface normal drift");
   LabAssetLibrary.Clear();var rows=new List<object>();
   foreach(string variant in new[]{"v1","v2","v3"})
   {
    var actor=new LabActor(new LabVariant{key=variant,label="Normal seams"},true);
    try
    {
     var arm=actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name.Contains("arm-left"));var mesh=arm.sharedMesh;
     if(mesh.blendShapeCount!=(variant=="v1"?3:2))throw new Exception("Corrective missing");
     var points=mesh.vertices;var normals=mesh.normals;var groups=Enumerable.Range(0,points.Length).GroupBy(i=>Vector3Int.RoundToInt(points[i]*1000000));
     for(int s=0;s<mesh.blendShapeCount;s++)
     {
      var delta=new Vector3[points.Length];var dn=new Vector3[points.Length];mesh.GetBlendShapeFrameVertices(s,0,delta,dn,null);int pairs=0;float worst=0;
      foreach(var group in groups)foreach(int i in group)foreach(int j in group)
       if(i<j && delta[i].sqrMagnitude>1e-12f && Vector3.Dot(normals[i],normals[j])>.9999f && (delta[i]-delta[j]).sqrMagnitude<1e-12f)
       {pairs++;worst=Mathf.Max(worst,Vector3.Distance(dn[i],dn[j]));}
      if(worst>1e-5f)throw new Exception("UV normal split remains: "+variant+" "+s+" "+worst);
      rows.Add(new{variant,shape=mesh.GetBlendShapeName(s),pairs,maximumNormalDeltaSeam=worst});
     }
    }
    finally{actor.Dispose();UnityEngine.Object.DestroyImmediate(actor.Root);}
   }
   string json=JsonConvert.SerializeObject(new{clockwiseNormals=true,hardEdges=true,unchangedSurface=true,meshes=rows},Formatting.Indented);File.WriteAllText(output,json);return json;
  }
 }
}
