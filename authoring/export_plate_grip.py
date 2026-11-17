"""Export only hash-matched, visually reviewed hand repairs through Blender MCP.

The gate covers the reported surface defects. Remaining Plate pad penetrations
are recorded explicitly and are NOT a whole-hand anatomical certification.
"""
import bpy,runpy,json,hashlib
from pathlib import Path
import numpy as np
from mathutils import Matrix
R=Path(__file__).resolve().parents[1];O=R/'authoring/plate-grip';B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));M=B['model'];C=B['C'];CI=B['CI']
read=lambda p:json.loads(p.read_text());sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def source(sources):
 ref=read(R/'references/glider-first-person.json')['renderers'][0];scenes=[]
 export=bpy.data.scenes.new('0.12.31 - editable export hands');scenes.append(export)
 for number,(var,v,b,ss,profile) in enumerate(sources):
  obj,rig,names=B['create_arm'](export,var,'left',v,b,ss,ref);rig.location.x=number*2.5
  obj['modelSha256']=sha(O/f'{var}.mikumesh');obj['correctionsSha256']=sha(O/f'{var}-corrections.json');obj['status']='Joint registration and nail surface repair; residual Plate pad contacts documented'
  if obj.data.shape_keys is None:obj.shape_key_add(name='Basis')
  for mesh in profile['meshes']:
   for pose in mesh['poses']:
    points=v['p'].copy();points[pose['vertices']]+=np.array([[d[k] for k in ('x','y','z')] for d in pose['deltas']]);key=obj.shape_key_add(name=pose['name']);key.data.foreach_set('co',(points@C[:3,:3].T).ravel());key.value=0;obj[pose['name']+' drivers']=json.dumps(pose['drivers'])
  scene=bpy.data.scenes.new(var+' - recorded Plate poses');scenes.append(scene);scene.render.fps=30
  preview,prig,pnames=B['create_arm'](scene,var,'left',v,b,ss,ref,True,True)
  capture='20261116-162906-805-0aca39' if var=='v1' else '20261116-163111-497-b3c226';frames=[json.loads(l) for l in (Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures')/capture/'frames.jsonl').read_text().splitlines()]
  scene.frame_start=1;scene.frame_end=len(frames);previous={}
  for index in sorted(set(range(0,len(frames),5))|{len(frames)-1,120,150}):
   frame=frames[index]['character'];record=next(r for r in frame['renderers'] if r['role']=='arm_left');bones=np.array(record['boneMatrices']).reshape(-1,4,4)[:28];bones[:,:3,3]-=frame['camera']['position'];helpers,_,_=B['wrist'](v,b,bones,.24 if var=='v3' else 0);matrices=np.concatenate([bones,helpers@np.linalg.inv(b[1])]);poses=[Matrix((C@m@CI).tolist()) for m in matrices];scene.frame_set(index+1)
   for i,name in enumerate(pnames):
    pb=prig.pose.bones[name];pb.rotation_mode='QUATERNION';parent=ref['bones'][i]['parentBoneIndex'] if i<28 else None;basis=pb.bone.matrix_local.inverted()
    if parent is not None and parent>=0:basis=basis@prig.pose.bones[pnames[parent]].bone.matrix_local@poses[parent].inverted()
    pb.matrix_basis=basis@poses[i];q=pb.rotation_quaternion.copy()
    if name in previous:q.make_compatible(previous[name])
    pb.rotation_quaternion=q;previous[name]=q.copy()
    for prop in ('location','rotation_quaternion','scale'):pb.keyframe_insert(data_path=prop,frame=index+1)
  for layer in prig.animation_data.action.layers:
   for strip in layer.strips:
    for bag in strip.channelbags:
     for curve in bag.fcurves:
      for key in curve.keyframe_points:key.interpolation='LINEAR'
  prig['recording']=capture;prig['preview_note']='Recorded source bones and runtime wrist helpers, sampled every five frames with linear interpolation. Native export and existing corrective shape keys are in the export scene; this preview omits the tiny thumb correctives.';scene.frame_set(1)
 note=bpy.data.texts.new('0.12.31 hand repair notes');note.write('Editable native 28-bone meshes and existing shape keys are in the export scene. v1 index joint registration; v3 index pad aligned to measured Glider offset and middle knuckle registered; rounded v3 thumb nail. Native clips and recorded Unity BakeMesh were checked separately. Plate handle pad contacts remain and are quantified in handle-depth.json. No new runtime bone offsets are included.\n')
 bpy.data.libraries.write(str(O/'repaired-hands.blend'),set(scenes)|{note},path_remap='RELATIVE_ALL',compress=True)
 bpy.context.window.scene=export
def export():
 metadata=read(O/'candidate-report.json');parity=read(O/'unity-parity.json');runtime=read(O/'unity-runtime-checks.json');motion=read(O/'registration-motion.json');records=read(O/'recorded-index-audit.json');depth=read(O/'handle-depth.json');nail=read(O/'shotgun-nail-audit.json');prepared=[];sources=[]
 for var in ('v1','v3'):
  model=O/f'{var}.mikumesh';profilefile=O/f'{var}-corrections.json';h=sha(model);ph=sha(profilefile);target=R/M['PATHS'][var]
  assert sha(target)==metadata[var]['inputSha256'] and target.read_bytes()==(O/'original'/f'{var}.mikumesh').read_bytes(),'Source changed before export'
  def matches(row):return row['modelSha256']==h and row['correctionsSha256']==ph
  row=next(r for r in runtime if r['variant']==var);assert matches(row) and row['passed'] and row['frames']==(351 if var=='v1' else 345),'Unity replay failed/stale'
  rows=[r for r in parity if r['variant']==var];assert {r['index'] for r in rows}=={0,120,150} and all(matches(r) and r['passed'] for r in rows),'Parity failed/stale'
  row=next(r for r in motion if r['variant']==var);assert matches(row) and row['passedNoNewDegenerates'],'New collapsed triangles'
  row=records[var]['candidate'];assert matches(row) and row['frames']==(351 if var=='v1' else 345) and row['indexNailCrossingFrames']==0,'Recorded index nail penetration'
  assert row['maximumIndexCrossingPairs']<=records[var]['original']['maximumIndexCrossingPairs'],'Worse index contact count'
  assert next(r for r in depth if r['variant']==var and r['stage']=='candidate')['maximumPenetrationMeters']<=next(r for r in depth if r['variant']==var and r['stage']=='original')['maximumPenetrationMeters'],'Worse Plate pad penetration'
  for family in ('plate','cycler','shotgun'):
   row=next(r for r in read(O/f'thumb-volume-{family}-animation-validation.json') if r['variant']==var);assert row['passed'] and row['sourceSha256']==h and row['correctionsSha256']==ph,'Existing thumb correction regression'
  profile=read(profilefile);assert not any(p.get('bones') or p.get('contact') for m in profile['meshes'] for p in m['poses']),'Rejected runtime experiment'
  old=M['chunks'](target.read_bytes());new=M['chunks'](model.read_bytes());assert old.keys()==new.keys() and all(old[k]==new[k] for k in old if k!='arm-left'),'Unrelated mesh changed'
  _,v,b,ss=M['read'](new['arm-left']);_,ov,ob,os=M['read'](old['arm-left']);assert np.array_equal(b,ob) and np.isfinite(v['p']).all() and np.min(v['w'])>=0 and np.max(abs(v['w'].sum(1)-1))<1e-6
  assert all(int(s.min())>=0 and int(s.max())<len(v) for s in ss)
  if var=='v1':assert all(np.array_equal(a,z) for a,z in zip(ss,os)) and np.array_equal(v['uv'],ov['uv'])
  if var=='v3':assert matches(nail) and nail['passed'] and nail['nailVertices']==257,'Nail attachment/shotgun regression'
  prepared.append((var,target,model.read_bytes(),profilefile.read_bytes()));sources.append((var,v,b,ss,profile))
 assert sha(R/'assets/v2/MikuGliderV2.mikumesh')=='5fc869fb0b0e8e0ebd9be61c9348172f6c4fc1fa725d7afe2da3e0c4acbaba5b','Unexpected v2 change'
 source(sources)
 for var,target,model,profile in prepared:target.write_bytes(model);(target.parent/'corrections.json').write_bytes(profile)
 result={'version':'0.12.31','focusedSurfaceRepairPassed':True,'wholeHandAnatomyCertified':False,'models':{var:{'model':sha(target),'corrections':sha(target.parent/'corrections.json')} for var,target,_,_ in prepared},'remainingPlateContacts':depth,'blendSha256':sha(O/'repaired-hands.blend')}
 (O/'export.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='remainingPlateContacts'}),flush=True)
if __name__=='__main__':export()
