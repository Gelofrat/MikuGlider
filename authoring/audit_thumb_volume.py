import bpy,runpy,json,hashlib,numpy as np
from pathlib import Path
from mathutils import Matrix
R=Path(__file__).resolve().parents[1];B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));M=B['model'];refs=json.loads((R/'references/glider-first-person.json').read_text())['renderers'];report={'version':'0.12.29','models':{},'sourceBaseline':'fe7e510 pre-opposition thumb; historical source comparison: thumb-source-comparison.json','boneDisplay':'Uniform short tails visualize native transform orientation, not anatomical segment length. Rest joint positions and hierarchy are checked separately.'}
for var,path in M['PATHS'].items():
 report['models'][var]={}
 for si,side in enumerate(['left','right']):
  _,v,b,ss=M['read'](M['chunks']((R/path).read_bytes())['arm-'+side]);obj=bpy.data.objects[f'{var} arm-{side} [EXPORT]'];rig=obj.parent;reference=refs[si]['bones'];error=0;parents=0;weight_error=0;head_error=0;rotation_error=0
  for i,bone in enumerate(reference):
   pb=rig.data.bones[bone['name']];expected=B['C']@np.linalg.inv(b[i])@B['CI'];error=max(error,float(np.max(np.abs(np.array(pb.matrix_local)-expected))));head_error=max(head_error,float(np.linalg.norm(np.array(pb.matrix_local)[:3,3]-expected[:3,3])));q=pb.matrix_local.to_quaternion();q.normalize();z=Matrix(expected.tolist()).to_quaternion();z.normalize();rotation_error=max(rotation_error,float(np.degrees(2*np.arccos(np.clip(abs(q.dot(z)),0,1)))));parent=bone['parentBoneIndex'];name=reference[parent]['name'] if parent is not None and parent>=0 else None;parents+=int((pb.parent.name if pb.parent else None)!=name)
  for i,vert in enumerate(obj.data.vertices):
   dense=np.zeros(28)
   for g in vert.groups:dense[g.group]=g.weight
   expected=np.zeros(28)
   for idx,w in zip(v['ids'][i],v['w'][i]):expected[idx]+=w
   weight_error=max(weight_error,float(np.max(np.abs(dense-expected))))
  points=np.array([p.co for p in obj.data.vertices])@B['C'][:3,:3];position_error=float(np.max(np.linalg.norm(points-v['p'],axis=1)));tri=np.concatenate(ss);area=np.linalg.norm(np.cross(v['p'][tri[:,1]]-v['p'][tri[:,0]],v['p'][tri[:,2]]-v['p'][tri[:,0]]),axis=1)
  row={'vertices':len(v),'triangles':len(tri),'nativeBones':len(b),'maxInfluences':int((v['w']>0).sum(1).max()),'maxWeightSumError':float(np.max(abs(v['w'].sum(1)-1))),'unweightedVertices':int((v['w'].sum(1)<.99).sum()),'invalidBoneIndices':int(((v['ids']<0)|(v['ids']>=28)).sum()),'negativeWeights':int((v['w']<0).sum()),'degenerateRestTriangles':int((area<1e-12).sum()),'blendPositionError':position_error,'blendWeightsError':weight_error,'blendBindMatrixError':error,'blendParentMismatches':parents}
  row.update(blendJointPositionError=head_error,blendRestRotationDifferenceDegrees=rotation_error)
  row['passed']=position_error<1e-7 and weight_error<1e-6 and head_error<1e-6 and rotation_error<.1 and parents==0 and row['maxWeightSumError']<1e-5 and row['unweightedVertices']==row['invalidBoneIndices']==row['negativeWeights']==row['degenerateRestTriangles']==0;assert row['passed'],(var,side,row);report['models'][var][side]=row
folder=R/'simulation/Reports/cycler-thumb-review/native-clips';cat=json.loads((folder/'clips.json').read_text());native=np.fromfile(folder/'clip-bones.bin','<f4').reshape(cat['frames'],56,4,4);det=np.linalg.det(native[:,:,:3,:3]);report['animationRig']={'nativeClips':len(cat['clips']),'poseFrames':cat['frames'],'transforms':int(det.size),'nonFiniteValues':int((~np.isfinite(native)).sum()),'minimumDeterminant':float(det.min()),'nonPositiveDeterminants':int((det<=0).sum())};assert report['animationRig']['nonFiniteValues']==report['animationRig']['nonPositiveDeterminants']==0
(R/'authoring/thumb-volume-rig-audit.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))
