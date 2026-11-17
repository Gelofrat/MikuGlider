import bpy, runpy, json, gzip
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1]
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py')); M=T['M']
ref=json.loads((R/'references/glider-first-person.json').read_text())['renderers']
report=[]
for variant,path in M['PATHS'].items():
 for si,side in enumerate(('left','right')):
  _,v,b,ss=M['read'](M['chunks']((R/path).read_bytes())['arm-'+side])
  rest=np.linalg.inv(b)[:,:3,3]; chain=rest[24:28]; axis=chain[3]-chain[2];axis/=np.linalg.norm(axis)
  thumb=np.where(v['ids']>=24,v['w'],0).sum(1)>.5
  report.append(dict(variant=variant,side=side,jointLengths=np.linalg.norm(np.diff(chain,axis=0),axis=1).tolist(),
                     distalExtension=float(((v['p'][thumb]-chain[2])@axis).max()),thumbVertices=int(thumb.sum())))
  if side!='left':continue
  stem=R/f'simulation/Reports/thumb-refinement/{variant}-clip-367-final.png'
  bones=np.array(json.loads(Path(str(stem)+'.mesh.json').read_text())['bones']).reshape(-1,4,4)
  weapons=json.loads(Path(str(stem)+'.weapon.json').read_text());mat,_=T['blend'](v,b,bones,variant)
  for mode in ('base','current'):
   p=v['p'].astype(float).copy()
   if mode=='current':
    profile=json.loads(((R/path).parent/'corrections.json').read_text());pose=next(p for m in profile['meshes'] for p in m['poses'] if p['name']=='Shotgun thumb grip')
    p[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']])
   points=np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3]
   T['render'](points,np.concatenate(ss),weapons,bones[1]@b[1],variant+'-diagnosis-'+mode,bones[25,:3,3]*.6+bones[24,:3,3]*.4)
(R/'authoring/hand-anatomy-diagnosis.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
