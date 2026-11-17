import json,runpy
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/repair_hand_anatomy.py'));M=A['M'];O=A['O']
for var,path in M['PATHS'].items():
 original=M['chunks']((R/path).read_bytes());parts=M['chunks']((O/(var+'.mikumesh')).read_bytes())
 # Right-hand candidates showed no thickness benefit; retain their baseline.
 parts['arm-right']=original['arm-right']
 _,v,b,ss=M['read'](original['arm-right'])
 (O/f'{var}-arm-right-candidate.json').write_text(json.dumps(dict(positions=v['p'].tolist(),normals=v['n'].tolist(),indices=v['ids'].tolist(),weights=v['w'].tolist())))
 (O/(var+'.mikumesh')).write_bytes(M['archive'](parts))
 profile=json.loads((O/(var+'-corrections.json')).read_text())
 for mesh in profile['meshes']:
  for pose in mesh['poses']:
   if not pose['name'].startswith(('Cycler thumb','Shotgun thumb','Plate thumb')):continue
   d=np.array([[x['x'],x['y'],x['z']] for x in pose['deltas']]);peak=float(np.linalg.norm(d,axis=1).max())
   d*=min(1.,.001/max(peak,1e-10))
   pose['deltas']=[dict(zip(('x','y','z'),map(float,x))) for x in d]
   pose['sourcePose']='Blender DQ authoring; local thumb support; welded relaxation; rest delta bounded to 1 mm after full-clip deformation review'
 (O/(var+'-corrections.json')).write_text(json.dumps(profile,indent=2)+'\n')
print('Prepared final candidates with original right arms and bounded corrective displacement')
