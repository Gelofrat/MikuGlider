"""Blender-only target authoring from repaired candidates, with bounded deltas."""
import runpy,json
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1]
A=runpy.run_path(str(R/'authoring/repair_hand_anatomy.py'));M=A['M'];O=A['O'];T=A['T']
D=runpy.run_path(str(R/'authoring/thumb_volume_target.py'))

def build():
 reports={}
 for var,path in M['PATHS'].items():
  _,v,b,ss=M['read'](M['chunks']((O/(var+'.mikumesh')).read_bytes())['arm-left'])
  profile=json.loads(((R/path).parent/'corrections.json').read_text());reports[var]={}
  for family,key,name in [('cycler','clip-381','Cycler thumb grip'),('shotgun','clip-367','Shotgun thumb grip'),('plate','clip-313','Plate thumb opposition')]:
   pose=next((p for m in profile['meshes'] for p in m['poses'] if p['name']==name),None)
   if pose is None:continue
   stem=R/f'simulation/Reports/thumb-refinement/{var}-{key}-final.png'
   bones=np.array(json.loads(Path(str(stem)+'.mesh.json').read_text())['bones']).reshape(-1,4,4)
   target,_=D['target'](v,b,ss,bones,var,(0,5,0),.97,-15 if family=='plate' else 0)
   target=D['relax'](v,ss,target,iterations=6)
   delta=target-v['p']
   # Bound inverse-skinning amplification. This is a global continuous scalar,
   # not per-vertex clipping, which would create a new edge discontinuity.
   peak=float(np.linalg.norm(delta,axis=1).max());scale=min(1.,.003/max(peak,1e-10));delta*=scale
   ids=np.flatnonzero(np.linalg.norm(delta,axis=1)>1e-8)
   pose['vertices']=ids.tolist();pose['deltas']=[dict(zip(('x','y','z'),map(float,d))) for d in delta[ids]]
   pose['sourcePose']='Blender DQ target after local thumb weight repair; smooth welded deltas; maximum rest displacement 3 mm'
   reports[var][family]=dict(unboundedMaximumRestDelta=peak,scale=scale,vertices=len(ids))
   mat,_=T['blend'](v,b,bones,var);points=np.einsum('nij,nj->ni',mat,np.c_[v['p']+delta,np.ones(len(v))])[:,:3]
   if family=='shotgun':
    weapons=json.loads(Path(str(stem)+'.weapon.json').read_text())
    T['render'](points,np.concatenate(ss),weapons,bones[1]@b[1],var+'-repair-target',bones[25,:3,3]*.6+bones[24,:3,3]*.4)
  (O/(var+'-corrections.json')).write_text(json.dumps(profile,indent=2)+'\n')
 (O/'targets.json').write_text(json.dumps(reports,indent=2));print(json.dumps(reports))
if __name__=='__main__':build()
