"""Focused recorded-motion tests and Blender previews for distal thumb candidates."""
import bpy,json,runpy,hashlib
from pathlib import Path
import numpy as np
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1];O=R/'authoring/thumb-return';S=R/'scratch/thumb-return'
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];BA=runpy.run_path(str(R/'authoring/bake_thumb_grip_review.py'));V=runpy.run_path(str(R/'authoring/review_thumb_return.py'))
crossing=runpy.run_path(str(R/'authoring/thumb_contact.py'))['interior_crossing']
def skin(v,b,bones,profile,var,weapon=None):
 poses=[p for m in profile['meshes'] for p in m['poses'] if p['name']=='Shotgun thumb grip'];p=v['p'].astype(float).copy()
 for pose in poses:
  if pose.get('contact'):raise ValueError('Experimental contact controller was rejected; do not export')
  weight=BA['activation'](pose,bones)
  p[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']])*weight
 mat,_=T['blend'](v,b,bones,var);return np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3]
def contact(points,tri,weapons):
 # Intersect triangles in camera-local coordinates for stable float precision.
 tree=BVHTree.FromPolygons(points.tolist(),tri.tolist(),all_triangles=True);pairs=[];depth=0.
 for weapon in weapons:
  p=np.array(weapon['positions']);faces=np.array(weapon['triangles']).reshape(-1,3);target=BVHTree.FromPolygons(p.tolist(),faces.tolist(),all_triangles=True)
  for a,b in tree.overlap(target):
   qa=points[tri[a]];qb=p[faces[b]];na=np.cross(qa[1]-qa[0],qa[2]-qa[0]);nb=np.cross(qb[1]-qb[0],qb[2]-qb[0]);la=np.linalg.norm(na);lb=np.linalg.norm(nb)
   if min(la,lb)<1e-12:continue
   da=(qb-qa[0])@(na/la);db=(qa-qb[0])@(nb/lb)
   if crossing(qa,qb):pairs.append((a,b,weapon['name']));depth=max(depth,min(-db.min(),db.max()))
 return {'interiorTrianglePairs':len(pairs),'maximumPlaneStraddleDepth':depth,'handTriangles':sorted(set(a for a,b,n in pairs))}
def fingerprints():
 for var in ('v2','v3'):
  path=O/f'{var}-corrections.json';profile=json.loads(path.read_text());next(m for m in profile['meshes'] if m['key']=='arm-left wrist transition')['fingerprint']=json.loads((O/f'{var}-fingerprint.json').read_text())['fingerprint'];path.write_text(json.dumps(profile,indent=2)+'\n')
def preview(indices=(0,80,167,168)):
 report={}
 capture=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures/20261116-133221-136-93ae4f');frames=[json.loads(x) for x in (capture/'frames.jsonl').read_text().splitlines()]
 for var in ('v2','v3'):
  _,v,b,ss=M['read'](M['chunks']((O/f'{var}.mikumesh').read_bytes())['arm-left']);tri=np.concatenate(ss);profile=json.loads((O/f'{var}-corrections.json').read_text());mask=np.where(v['ids']>=25,v['w'],0).sum(1)>.8;tr=tri[np.all(mask[tri],axis=1)];report[var]=[]
  for index in indices:
   data=json.loads((S/f'{var}-before-{index}.json').read_text());bones=np.array(data['bones']).reshape(-1,4,4);cam=np.array(data['camera']['firstPersonPass']['worldToCamera']).reshape(4,4)
   weapon=np.array(frames[index]['character']['weaponMatrix']).reshape(4,4);points=skin(v,b,bones,profile,var,weapon);points=(np.c_[points,np.ones(len(points))]@cam.T)[:,:3]
   weapons=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@cam.T)[:,:3]) for w in data['weapons']]
   before=(np.c_[data['positions'],np.ones(len(data['positions']))]@cam.T)[:,:3]
   report[var].append({'index':index,'before':contact(before,tr,weapons),'candidate':contact(points,tr,weapons)})
   if index in (0,80):
    center=((cam@bones)[25,:3,3]+(cam@bones)[26,:3,3])*.5
    V['render'](f'{var}-candidate-recorded-{index}',points,tri,v['uv'],R/f'assets/{var}/textures/body-albedo.png',center,np.zeros(3),weapons,scale=.042)
   data['positions']=skin(v,b,bones,profile,var,weapon).tolist();(S/f'{var}-candidate-{index}.json').write_text(json.dumps(data))
 (O/'preview-contact.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
if __name__=='__main__':fingerprints();preview()
