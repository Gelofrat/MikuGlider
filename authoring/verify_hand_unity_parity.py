"""Compare actual Unity BakeMesh exports with authoring deformation on identical poses."""
import runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];O=R/'authoring/hand-repair'
rows=[]
for var,path in M['PATHS'].items():
 parts=M['chunks']((R/path).read_bytes());profile=json.loads(((R/path).parent/'corrections.json').read_text())
 poses=next(x for x in profile['meshes'] if x['key']=='arm-left wrist transition')['poses']
 for key in ('clip-381','clip-367','clip-313'):
  stem=O/f'{var}-{key}-unity.png';pose=json.loads(Path(str(stem)+'.pose.json').read_text())
  for side in ('left','right'):
   _,v,b,ss=M['read'](parts['arm-'+side]);data=json.loads(Path(str(stem)+('.mesh.json' if side=='left' else '.right.json')).read_text());bones=np.array(data['bones']).reshape(-1,4,4)
   if side=='left' or var=='v2':mat,_=T['blend'](v,b,bones,var if side=='left' else 'v2')
   else:
    native=bones@b;mat=sum(native[v['ids'][:,k]]*v['w'][:,k,None,None] for k in range(4))
   points=v['p'].astype(float).copy()
   if side=='left':
    if len(pose['shapeWeights'])!=len(poses):raise ValueError('Runtime correction rejected: '+var)
    for p,weight in zip(poses,pose['shapeWeights']):points[p['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in p['deltas']])*weight/100
   expected=np.einsum('nij,nj->ni',mat,np.c_[points,np.ones(len(points))])[:,:3]
   actual=np.array(data['positions'])
   if side=='left':actual=(np.c_[actual,np.ones(len(actual))]@(bones[1]@b[1]).T)[:,:3]
   error=float(np.linalg.norm(expected-actual,axis=1).max())
   # Float quaternion decomposition/wrist interpolation differ slightly between
   # Unity and mathutils. 20 micrometres is also the surface contact tolerance;
   # the older lab bake allowed 100 micrometres. Keep the measured error visible.
   rows.append(dict(variant=var,side=side,clip=key,maximumWorldError=error,toleranceWorld=2e-5,passed=error<2e-5,modelSha256=hashlib.sha256((R/path).read_bytes()).hexdigest(),correctionsSha256=hashlib.sha256(((R/path).parent/'corrections.json').read_bytes()).hexdigest()))
(O/'unity-parity.json').write_text(json.dumps(rows,indent=2)+'\n')
print(json.dumps(rows));assert all(r['passed'] for r in rows), 'Unity/Blender deformation differs'
