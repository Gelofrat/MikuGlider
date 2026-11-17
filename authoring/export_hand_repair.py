"""Apply validated local candidates via Blender MCP; preserve exact backups."""
import bpy,runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/repair_hand_anatomy.py'));M=A['M'];B=A['B'];O=A['O']
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()

def fingerprints():
 for var in M['PATHS']:
  p=O/(var+'-corrections.json');profile=json.loads(p.read_text())
  fp=json.loads((O/(var+'-fingerprint.png.pose.json')).read_text())
  mesh=next(m for m in profile['meshes'] if m['key']=='arm-left wrist transition')
  if fp['key']!=mesh['key']:raise ValueError('Unexpected Unity fingerprint mesh')
  mesh['fingerprint']=fp['fingerprint'];p.write_text(json.dumps(profile,indent=2)+'\n')

def export():
 report=json.loads((O/'candidate-report.json').read_text());backup=O/'original';backup.mkdir(exist_ok=True)
 prepared={}
 for var,path in M['PATHS'].items():
  if sha(R/path)!=report[var]['inputSha256']:raise ValueError('Source changed or already exported: '+var)
  model=O/(var+'.mikumesh');profile=O/(var+'-corrections.json')
  for family in ('cycler','shotgun','plate'):
   row=next(x for x in json.loads((O/f'thumb-volume-{family}-animation-validation.json').read_text()) if x['variant']==var)
   if not row['passed'] or row['sourceSha256']!=sha(model) or row['correctionsSha256']!=sha(profile):raise ValueError('Missing/current validation: '+var+' '+family)
  original=M['chunks']((R/path).read_bytes());parts=M['chunks'](model.read_bytes())
  if any(parts[k]!=original[k] for k in original if k!='arm-left'):raise ValueError('Unexpected edit outside left hand')
  _,v,b,ss=M['read'](parts['arm-left']);_,old,ob,oss=M['read'](original['arm-left'])
  if not np.array_equal(v['uv'],old['uv']) or not np.array_equal(b,ob) or any(not np.array_equal(x,y) for x,y in zip(ss,oss)):raise ValueError('Topology/UV/bind change')
  if not np.isfinite(v['p']).all() or np.max(np.abs(v['w'].sum(1)-1))>1e-5:raise ValueError('Invalid geometry/weights')
  prepared[var]=(path,model.read_bytes(),profile.read_bytes())
 for var,(path,raw,profile) in prepared.items():
  for dest,source in [(backup/(var+'.mikumesh'),R/path),(backup/(var+'-corrections.json'),(R/path).parent/'corrections.json')]:
   if dest.exists():raise ValueError('Backup already exists: '+str(dest))
   dest.write_bytes(source.read_bytes())
 for var,(path,raw,profile) in prepared.items():
  (R/path).write_bytes(raw);((R/path).parent/'corrections.json').write_bytes(profile)
  scene=bpy.data.scenes.new(var+' repaired export');bpy.context.window.scene=scene
  for si,side in enumerate(('left','right')):
   _,v,b,ss=M['read'](M['chunks'](raw)['arm-'+side])
   obj,rig,_=B['create_arm'](scene,var,side,v,b,ss,A['reference'][si],False,False)
   if side=='left':
    obj.shape_key_add(name='Basis')
    for m in json.loads(profile)['meshes']:
     if m['key']!='arm-left wrist transition':continue
     for pose in m['poses']:
      p=v['p'].copy();p[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']])
      key=obj.shape_key_add(name=pose['name']);key.data.foreach_set('co',(p@B['C'][:3,:3].T).ravel())
  report[var].update(outputSha256=sha(R/path),correctionsSha256=sha((R/path).parent/'corrections.json'),rightArmUnchanged=True)
 for family,name in [('cycler','Cycler thumb grip'),('shotgun','Shotgun thumb grip'),('plate','Plate thumb opposition')]:
  manifest={'method':'Blender anatomical support and bounded DQ corrective repair','models':{}}
  for var,path in M['PATHS'].items():
   p=json.loads(((R/path).parent/'corrections.json').read_text());pose=next((p for m in p['meshes'] for p in m['poses'] if p['name']==name),None)
   if pose:manifest['models'][var]={'sha256':sha(R/path),'pose':pose}
  (R/f'authoring/thumb-volume-{family}.json').write_text(json.dumps(manifest,indent=2)+'\n')
 (O/'export.json').write_text(json.dumps(report,indent=2)+'\n')
 bpy.ops.wm.save_as_mainfile(filepath=str(O/'repaired-hands.blend'),compress=True)
 print('Exported repaired left hands for v1/v2/v3; exact original backups saved')
