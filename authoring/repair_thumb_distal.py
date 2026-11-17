"""Blender MCP authoring: align distal tissue with its joint; keep the nail rigid."""
import bpy,json,runpy,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
R=Path(__file__).resolve().parents[1];O=R/'authoring/thumb-return';O.mkdir(exist_ok=True)
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];B=T['B']
REF=json.loads((R/'references/glider-first-person.json').read_text())['renderers']
DIAG=json.loads((R/'authoring/thumb-nail-regions.json').read_text())
def smooth(x):
 x=np.clip(x,0,1);return x*x*(3-2*x)
def section_center(p,tr,origin,axis,at):
 q=p[tr];d=(q-origin)@axis-at;hits=[]
 for i,j in ((0,1),(1,2),(2,0)):
  mask=(d[:,i]*d[:,j]<0);t=d[mask,i]/(d[mask,i]-d[mask,j]);hits.extend(q[mask,i]+t[:,None]*(q[mask,j]-q[mask,i]))
 if len(hits)<6:raise ValueError('Missing distal section')
 u=np.array([0.,1.,0.]);u-=axis*np.dot(axis,u);u/=np.linalg.norm(u);z=np.cross(axis,u);q=np.array(hits)-(origin+axis*at)
 return origin+axis*at+u*(np.min(q@u)+np.max(q@u))/2+z*(np.min(q@z)+np.max(q@z))/2
def build(clearances=None):
 report={};backup=O/'original';backup.mkdir(exist_ok=True)
 for var in ('v2','v3'):
  path=R/M['PATHS'][var];raw=path.read_bytes();profilepath=path.parent/'corrections.json'
  if not (backup/f'{var}.mikumesh').exists():
   (backup/f'{var}.mikumesh').write_bytes(raw);(backup/f'{var}-corrections.json').write_bytes(profilepath.read_bytes())
  assert raw==(backup/f'{var}.mikumesh').read_bytes(),'Inputs changed; do not stack edits'
  parts=M['chunks'](raw);_,v,b,ss=M['read'](parts['arm-left']);c=v.copy();j=np.linalg.inv(b)[:,:3,3];axis=j[27]-j[26];axis/=np.linalg.norm(axis)
  tw=np.where(v['ids']>=24,v['w'],0).sum(1);tri=np.concatenate(ss);thumbtri=tri[np.all(tw[tri]>.5,axis=1)]
  start=section_center(v['p'],thumbtri,j[26],axis,0);end=section_center(v['p'],thumbtri,j[26],axis,.016)
  direction=end-start;direction/=np.linalg.norm(direction);rot=np.array(Vector(direction).rotation_difference(Vector(axis)).to_matrix())
  # A rigid transform over the complete distal segment preserves the nail's
  # native shape. Blend only across the proximal soft-tissue transition.
  # Distribute the alignment over the full proximal shaft. Concentrating this
  # translation in a short IP ring makes an artificial hooked silhouette.
  along=(v['p']-j[26])@axis;factor=smooth((along+.065)/.069)*smooth((tw-.20)/.70)
  # Keep the distal pad on the exposed side of the native shotgun housing.
  # This rest-space clearance is shared by every pose, never switched at
  # inspect exit. It is checked against fixed original tip IDs in recordings.
  distance=(clearances or {'v2':.020,'v3':.018})[var]
  clearance=np.array([distance,-distance/6,distance*4/15])
  desired=(v['p']-start)@rot.T+j[26]+clearance
  c['p']=v['p']+factor[:,None]*(desired-v['p'])
  # Remove palm influence from the distal pad and nail. Keep the web/palm intact.
  rigid=smooth((along+.008)/.012)*smooth((tw-.25)/.65)
  nail=np.array(DIAG[var]['nail']);rigid[nail]=1
  for i in np.flatnonzero(rigid>0):
   values={}
   for k,w in zip(c['ids'][i],c['w'][i]):values[int(k)]=values.get(int(k),0)+float(w)*(1-rigid[i])
   values[26]=values.get(26,0)+float(rigid[i]);pairs=sorted(values.items(),key=lambda x:-x[1])[:4];norm=sum(w for _,w in pairs);c['ids'][i]=0;c['w'][i]=0
   for k,(bone,w) in enumerate(pairs):c['ids'][i,k]=bone;c['w'][i,k]=w/norm
  # Weld copies at UV seams, including skin/nail borders where coincident.
  groups={}
  for i,p in enumerate(v['p']):groups.setdefault(tuple(np.round(p,6)),[]).append(i)
  for ids in groups.values():
   if max(rigid[ids])<1e-7:continue
   c['p'][ids]=c['p'][ids].mean(0)
   if any(i in nail for i in ids):c['ids'][ids]=[26,0,0,0];c['w'][ids]=[1,0,0,0]
  # Smooth only the joint's skin ring, retaining the complete rigid nail/pad.
  groups=list(groups.values());mapping=np.zeros(len(v),int)
  for gi,ids in enumerate(groups):mapping[ids]=gi
  neighbors=[set() for _ in groups]
  for f in tri:
   for a,z in ((0,1),(1,2),(2,0)):
    x,y=mapping[f[a]],mapping[f[z]]
    if x!=y:neighbors[x].add(y);neighbors[y].add(x)
  pos=np.array([c['p'][ids].mean(0) for ids in groups]);strength=np.array([float(smooth((along[ids].mean()+.020)/.010)*(1-smooth((along[ids].mean()-.001)/.007))*smooth((tw[ids].mean()-.5)/.4)) for ids in groups]);strength[mapping[nail]]=0
  for step in (.35,-.36)*4:
   new=pos.copy()
   for gi in np.flatnonzero(strength>0):
    if neighbors[gi]:new[gi]+=step*strength[gi]*(pos[list(neighbors[gi])].mean(0)-pos[gi])
   pos=new
  c['p']=pos[mapping]
  normals=runpy.run_path(str(R/'authoring/refine_thumb_grips.py'))['normals'];normals(c,v['p'],tri,np.linalg.norm(c['p']-v['p'],axis=1)>1e-8)
  parts['arm-left']=M['write']('arm-left',c,b,ss);(O/f'{var}.mikumesh').write_bytes(M['archive'](parts))
  profile=json.loads(profilepath.read_text());mesh=next(x for x in profile['meshes'] if x['key']=='arm-left wrist transition')
  for pose in mesh['poses']:
   ids=np.array(pose['vertices']);delta=np.array([[q['x'],q['y'],q['z']] for q in pose['deltas']]);delta*=1-rigid[ids,None]
   if var=='v3' and pose['name']=='Cycler thumb grip':delta*=.25
   pose['deltas']=[dict(zip(('x','y','z'),map(float,q))) for q in delta];pose['sourcePose']+='; distal pad and nail protected from corrective deformation'
   if var=='v3' and pose['name']=='Cycler thumb grip':pose['sourcePose']+='; proximal Cycler displacement reduced to 25% after native inspect area test'
  (O/f'{var}-corrections.json').write_text(json.dumps(profile,indent=2))
  (O/f'{var}-arm-left-candidate.json').write_text(json.dumps(dict(positions=c['p'].tolist(),normals=c['n'].tolist(),weights=c['w'].tolist(),indices=c['ids'].tolist())))
  report[var]={'inputSha256':hashlib.sha256(raw).hexdigest(),'outputSha256':hashlib.sha256((O/f'{var}.mikumesh').read_bytes()).hexdigest(),'sourceJointCenterOffset':(start-j[26]).tolist(),'clearanceRestOffset':clearance.tolist(),'distalAxisRotationDegrees':float(np.degrees(np.arccos(np.clip(np.dot(direction,axis),-1,1)))),'nailVertices':nail.tolist(),'maximumRestDisplacement':float(np.linalg.norm(c['p']-v['p'],axis=1).max()),'editedVertices':int((np.linalg.norm(c['p']-v['p'],axis=1)>1e-8).sum())}
  scene=bpy.data.scenes.new(var+' distal thumb candidate');obj,rig,_=B['create_arm'](scene,var,'left',c,b,ss,REF[0],False,False);obj['nail_vertices']=nail.tolist();obj['source']='thumb-return/original';obj['status']='candidate; requires recorded motion and weapon contact review'
 (O/'candidate-report.json').write_text(json.dumps(report,indent=2));bpy.ops.wm.save_as_mainfile(filepath=str(O/'distal-thumb-candidates.blend'),compress=True);print(json.dumps(report))
if __name__=='__main__':build()
