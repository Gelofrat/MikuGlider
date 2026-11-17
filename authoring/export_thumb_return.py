"""Blender MCP export of the checked v2/v3 distal-thumb repair."""
import bpy,runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];O=R/'authoring/thumb-return';M=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'))['M']
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()

def export():
 report=json.loads((O/'candidate-report.json').read_text());recorded=json.loads((O/'recorded-audit.json').read_text())['variants']
 parity=json.loads((O/'unity-parity.json').read_text());runtime=json.loads((O/'unity-recorded-checks.json').read_text())
 prepared={}
 for var in ('v2','v3'):
  model=O/f'{var}.mikumesh';profile=O/f'{var}-corrections.json';target=R/M['PATHS'][var];h=sha(model);p=sha(profile)
  assert sha(target)==report[var]['inputSha256'],'Source changed before export'
  assert target.read_bytes()==(O/'original'/f'{var}.mikumesh').read_bytes()
  assert not any(q.get('contact') for m in json.loads(profile.read_text())['meshes'] for q in m['poses']),'Unaccepted experimental contact controller'
  audit=recorded[var]['candidate'];assert audit['modelSha256']==h and audit['correctionsSha256']==p
  assert audit['frames']==705 and audit['weaponCrossingFrames']==0 and audit['maximumNailDistanceErrorRatio']<.001 and audit['maximumEdgeStrainChangePer60Hz']<.001
  for family in ('cycler','shotgun','plate'):
   row=next(x for x in json.loads((O/f'thumb-volume-{family}-animation-validation.json').read_text()) if x['variant']==var)
   assert row['passed'] and row['sourceSha256']==h and row['correctionsSha256']==p,(var,family,'stale deformation validation')
  rows=[x for x in parity if x['variant']==var];assert {x['index'] for x in rows}=={0,80,167,168}
  assert all(x['passed'] and x['modelSha256']==h and x['correctionsSha256']==p for x in rows)
  row=next(x for x in runtime if x['variant']==var);assert row['passed'] and row['frames']==705 and row['modelSha256']==h and row['correctionsSha256']==p
  row=json.loads((O/f'{var}-left-True-audit.json').read_text());assert row['frames']==2993 and row['missingSections']==0 and min(row['minimumSectionRatios'])>=.75
  assert row['sha256']==h and row['correctionsSha256']==p
  old=M['chunks'](target.read_bytes());new=M['chunks'](model.read_bytes());assert old.keys()==new.keys()
  assert all(old[k]==new[k] for k in old if k!='arm-left'),'Changed unrelated mesh'
  _,v,b,ss=M['read'](new['arm-left']);_,ov,ob,os=M['read'](old['arm-left'])
  assert np.array_equal(v['uv'],ov['uv']) and np.array_equal(b,ob) and all(np.array_equal(a,z) for a,z in zip(ss,os))
  assert np.max(abs(v['w'].sum(1)-1))<1e-6 and (v['w']>=0).all()
  prepared[var]=(target,model.read_bytes(),profile.read_bytes())
 for var,(target,model,profile) in prepared.items():
  target.write_bytes(model);(target.parent/'corrections.json').write_bytes(profile)
 # Keep the existing volume manifests current for read-only verification.
 for family,name in [('cycler','Cycler thumb grip'),('shotgun','Shotgun thumb grip'),('plate','Plate thumb opposition')]:
  file=R/f'authoring/thumb-volume-{family}.json';manifest=json.loads(file.read_text())
  for var in ('v2','v3'):
   data=json.loads((O/f'{var}-corrections.json').read_text());pose=next((q for m in data['meshes'] for q in m['poses'] if q['name']==name),None)
   if var in manifest['models']:
    manifest['models'][var]['sha256']=sha(R/M['PATHS'][var]);manifest['models'][var]['pose']=pose
  file.write_text(json.dumps(manifest,indent=2)+'\n')
 for var in ('v2','v3'):
  scene=next(s for s in reversed(list(bpy.data.scenes)) if s.name.startswith(var+' distal thumb candidate'))
  scene.name='Miku '+var+' - 0.12.30 - corrected thumb'
  for obj in scene.objects:
   if obj.type=='MESH':
    obj['status']='Exported v0.12.30; recorded nail/phalange/contact checks passed; full-hand contact audit remains separate'
    _,vertices,_,_=M['read'](M['chunks']((O/f'{var}.mikumesh').read_bytes())['arm-left'])
    coordinate=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'))['B']['C'][:3,:3]
    if obj.data.shape_keys is None:obj.shape_key_add(name='Basis')
    for mesh in json.loads((O/f'{var}-corrections.json').read_text())['meshes']:
     for pose in mesh['poses']:
      points=vertices['p'].copy();points[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']])
      key=obj.data.shape_keys.key_blocks.get(pose['name']) or obj.shape_key_add(name=pose['name']);key.data.foreach_set('co',(points@coordinate.T).ravel());key.value=0
      obj[pose['name']+' drivers']=json.dumps(pose['drivers'])
 bpy.context.window.scene=bpy.data.scenes['Miku v2 - 0.12.30 - corrected thumb']
 bpy.ops.wm.save_as_mainfile(filepath=str(O/'repaired-distal-thumbs.blend'),compress=True)
 (O/'export.json').write_text(json.dumps({v:{'model':sha(R/M['PATHS'][v]),'corrections':sha((R/M['PATHS'][v]).parent/'corrections.json')} for v in ('v2','v3')},indent=2)+'\n')
 print('Exported checked v2/v3 left thumbs; v1, right arms, UVs, topology and bind poses preserved.')

if __name__=='__main__':export()
