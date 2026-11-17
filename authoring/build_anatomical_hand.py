"""Blender MCP: calibrate anatomical controls and export isolated grip candidates.

The game mesh bind frames are preserved; skin adapters map the new joint pivots
to those frames. No animation-specific mesh or frame table is generated.
"""
import bpy, runpy, json
from pathlib import Path
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

R=Path(__file__).resolve().parents[1]
O=R/'authoring/anatomical-hand'
CAPTURES=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures')
M=runpy.run_path(str(R/'authoring/retarget_first_person_hands.py'))
B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'))
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'))
section_center=runpy.run_path(str(R/'authoring/repair_thumb_distal.py'))['section_center']
nail_mask=runpy.run_path(str(R/'authoring/relax_grip_surface.py'))['nail_mask']
REF=json.loads((R/'references/glider-first-person.json').read_text())['renderers']

def vec(v): return dict(zip('xyz',map(float,v)))
def quat(m):
 q=Matrix(m.tolist()).to_quaternion().normalized() if isinstance(m,np.ndarray) else m.normalized()
 return dict(zip('xyzw',map(float,(q.x,q.y,q.z,q.w))))
def rot(m): return np.array(Matrix(m.tolist()).to_quaternion().normalized().to_matrix())
def local(a,i,parents): return a[i] if parents[i] is None or parents[i]<0 else np.linalg.inv(a[parents[i]])@a[i]

def build():
 O.mkdir(exist_ok=True);reports=[];scenes=[]
 for variant,side,weapon in [('v1','left','YAPlateLauncher'),('v3','right','MiniCannon')]:
  path=R/M['PATHS'][variant];raw=path.read_bytes()
  _,v,bind,sections=M['read'](M['chunks'](raw)['arm-'+side]);v=v.copy();tri=np.concatenate(sections)
  profile=json.loads((path.parent/'corrections.json').read_text());mesh=next(m for m in profile['meshes'] if m['key'].startswith('arm-'+side))
  if mesh.get('handRig'): raise ValueError('Candidate is already installed; use the original input snapshot')
  (O/(variant+'-original-corrections.json')).write_text(json.dumps(profile,separators=(',',':')))
  ref=REF[side=='right'];parents=[b['parentBoneIndex'] for b in ref['bones']]
  rest=np.linalg.inv(bind).astype(float);anatomy=rest.copy();j=rest[:,:3,3];rows=[]
  for start,end in [(4,8),(9,13),(14,18),(19,23),(24,27)]:
   fw=np.where((v['ids']>=start)&(v['ids']<=end),v['w'],0).sum(1);tr=tri[np.all(fw[tri]>.5,axis=1)]
   axis=j[end]-j[start+1];axis/=np.linalg.norm(axis)
   for i in range(start+1,end):
    center=section_center(v['p'],tr,j[i],axis,.0001);offset=center-j[i];offset-=axis*np.dot(axis,offset)
    if np.linalg.norm(offset)>.02: raise ValueError(('Unreliable joint section',variant,i,offset))
    anatomy[i,:3,3]+=offset;rows.append({'bone':i,'offsetMeters':float(np.linalg.norm(offset))})
   anatomy[end,:3,3]+=anatomy[end-1,:3,3]-j[end-1]
   for i in range(start,end):
    a=Vector(j[i+1]-j[i]);z=Vector(anatomy[i+1,:3,3]-anatomy[i,:3,3])
    anatomy[i,:3,:3]=np.array(a.rotation_difference(z).to_matrix())@rest[i,:3,:3]
   anatomy[end,:3,:3]=anatomy[end-1,:3,:3]
  joints=[]
  for i in range(4,28):
   q=local(anatomy,i,parents);s=local(rest,i,parents);adapter=np.linalg.inv(anatomy[i])@rest[i]
   joints.append({'bone':i,'parent':parents[i],'name':ref['bones'][i]['name'],'position':vec(q[:3,3]),'rotation':quat(q),'sourceRotation':quat(s),
    'axisMap':quat(anatomy[i,:3,:3].T@rest[i,:3,:3]),'skinPosition':vec(adapter[:3,3]),'skinRotation':quat(adapter)})
  original_weights=v.copy()
  before=json.loads((O/(variant+'-baseline.json')).read_text());source=np.array(before['bones']).reshape(28,4,4);oldposed=np.array(before['posedBones']).reshape(28,4,4);goal=np.array(before['positions'])
  scale=np.linalg.norm(source[3,:3,:3],axis=0).mean();posed=oldposed.copy();nails=nail_mask(v,path.parent/'textures/body-albedo.png');dominant=v['ids'][np.arange(len(v)),v['w'].argmax(1)]
  nail_sets={i:np.flatnonzero(nails&(dominant==i)) for i in (7,12,17,22,26)}
  target_rot=[rot(oldposed[i])@rest[i,:3,:3].T@anatomy[i,:3,:3] for i in range(28)];nail_targets={}
  for i,ids in nail_sets.items():
   if len(ids)<3: raise ValueError(('Nail mask incomplete',variant,i,len(ids)))
   a=v['p'][ids].astype(float);z=goal[ids];u,_,vt=np.linalg.svd((a-a.mean(0)).T@(z-z.mean(0)));r=vt.T@u.T
   if np.linalg.det(r)<0:vt[-1]*=-1;r=vt.T@u.T
   # Fit the nail plate itself. Rotating the entire distal phalanx to fit a
   # distorted old nail also rotates its skin and creates folded fingertips.
   nail_targets[i]=(a-a.mean(0))@r.T*scale+z.mean(0)
   # Rigid nail plates, including UV seam duplicates.
   v['ids'][ids]=0;v['w'][ids]=0;v['ids'][ids,0]=i;v['w'][ids,0]=1
  closed=[quat(rot(oldposed[2]).T@target_rot[3])]
  for i in range(4,28):
   local_rotation=target_rot[parents[i]].T@target_rot[i];closed.append(quat(local_rotation))
   q=local(anatomy,i,parents);q[:3,:3]=local_rotation
   posed[i]=posed[parents[i]]@q
  adapters=np.linalg.inv(anatomy)@rest;rendered=posed@adapters
  blend=T['blend'](v,bind,rendered,variant)[0] if side=='left' else np.sum((rendered@bind)[v['ids']]*v['w'][:,:,None,None],axis=1)
  delta=np.linalg.solve(blend,np.c_[goal,np.ones(len(goal))][:,:,None])[:,:3,0]-v['p']
  hand=np.where(v['ids']>=3,v['w'],0).sum(1);delta[hand<.01]=0
  # This grip calibration is fixed in bind space (not interpolated with curl).
  # Rigid nail targets therefore stay rigid under their single distal bone.
  for i,ids in nail_sets.items():
   delta[ids]=np.linalg.solve(blend[ids],np.c_[nail_targets[i],np.ones(len(ids))][:,:,None])[:,:3,0]-v['p'][ids]
  ids=np.flatnonzero(np.linalg.norm(delta,axis=1)>1e-7)
  old_grip=next(p for p in mesh['poses'] if p.get('gripAnchor'))
  anchor=old_grip['gripAnchor']
  # Use the actual handle transform, including its scale.
  capture='20261116-180944-759-d974ee' if variant=='v1' else '20261116-181646-129-73fa08'
  frame=json.loads((CAPTURES/capture/'frames.jsonl').read_text().splitlines()[0])['character']
  leaf=anchor['path'].split('/')[-1];record=next(r for r in frame['renderers'] if r['role']=='weapon' and r['path'].endswith('/'+leaf))
  handle=np.array(record['localToWorld']).reshape(4,4);handle[:3,3]-=before['origin'];inverse_handle=np.linalg.inv(handle)
  contacts=[];actual=np.einsum('nij,nj->ni',blend,np.c_[v['p']+delta,np.ones(len(v))])[:,:3]
  weapon_mesh=next(w for w in before['weapons'] if w['name'].endswith(leaf));tree=BVHTree.FromPolygons(weapon_mesh['positions'],np.array(weapon_mesh['triangles']).reshape(-1,3).tolist())
  contact_gaps=[]
  for bone in (7,12,17,22,26):
   candidates=np.flatnonzero((dominant==bone)&~nails&(v['w'].max(1)>.99));nearest=sorted((tree.find_nearest(Vector(actual[k]))[3],int(k)) for k in candidates)[:5]
   chosen=np.array([k for _,k in nearest]);point=actual[chosen].mean(0);local_point=(np.linalg.inv(posed[bone])@np.r_[point,1])[:3]
   contacts.append({'root':24 if bone==26 else bone-2,'tip':bone,'localPoint':vec(local_point),'target':vec((inverse_handle@np.r_[point,1])[:3]),'axis':vec((0,0,1)),'maxAngle':12})
   contact_gaps.append({'bone':bone,'padVertices':chosen.tolist(),'referenceGapMeters':float(tree.find_nearest(Vector(point))[3])})
  mesh['poses']=[p for p in mesh['poses'] if p.get('weapon',mesh.get('weapon',''))!=weapon]
  ids=np.flatnonzero(np.linalg.norm(delta,axis=1)>1e-7);skin_index=len(mesh['poses'])
  mesh['poses'].append({'name':'Anatomical grasp calibration','weapon':weapon,'group':'anatomical-calibration','sourcePose':'Fixed bind-space calibration, independent of animation clips',
   'drivers':[{'bone':3,'name':ref['bones'][3]['name'],'rotation':quat(local(source,3,parents)),'innerRadius':1,'radius':90}],
   'vertices':ids.tolist(),'deltas':[vec(q) for q in delta[ids]],'boneRotations':[]})
  changed=np.flatnonzero(np.any(v['ids']!=original_weights['ids'],axis=1)|np.any(np.abs(v['w']-original_weights['w'])>1e-7,axis=1));changed_set=set(changed.tolist())
  mesh['weights']=[w for w in mesh['weights'] if w['vertex'] not in changed_set]
  for i in changed:
   value={**{'m_BoneIndex'+str(k):int(v['ids'][i,k]) for k in range(4)},**{'m_Weight'+str(k):float(v['w'][i,k]) for k in range(4)}}
   mesh['weights'].append({'vertex':int(i),'value':value})
  intent=[{'bone':i,'name':ref['bones'][i]['name'],'rotation':quat(local(source,i,parents)),'axis':vec((0,0,1))} for i in (5,6,7,10,11,12,15,16,17,20,21,22,24,25,26)]
  mesh['handRig']={'joints':joints,'grips':[{'weapon':weapon,'path':anchor['path'],'position':anchor['position'],'rotation':anchor['rotation'],
   'closed':closed,'reference':[quat(local(source,i,parents)) for i in range(3,28)],'axes':[quat(target_rot[i].T@rot(source[i])) for i in range(3,28)],
   'intent':intent,'contacts':contacts,'skinPose':skin_index,'innerDistance':anchor['innerDistance'],'outerDistance':anchor['outerDistance'],'blendTime':.08}]}
  (O/(variant+'-corrections.json')).write_text(json.dumps(profile,separators=(',',':')))
  (O/(variant+'-nails.json')).write_text(json.dumps({str(k):v.tolist() for k,v in nail_sets.items()}))
  report={'variant':variant,'jointCalibration':rows,'contacts':contact_gaps,'nailVertices':{str(k):len(ids) for k,ids in nail_sets.items()},'maximumRestAdapterError':float(np.abs(anatomy@adapters-rest).max())}
  reports.append(report)
  # Editable real joint rig, never replace/reset the user's existing scene.
  scene=bpy.data.scenes.new('Anatomical '+variant+' '+side);scenes.append(scene)
  obj,rig,names=B['create_arm'](scene,variant,side,v,np.linalg.inv(anatomy),sections,ref)
  obj.shape_key_add(name='Basis');key=obj.shape_key_add(name='Grasp bind calibration');key.data.foreach_set('co',((v['p']+delta)@B['C'][:3,:3].T).ravel())
  rig['hand_rig_definition']=json.dumps(mesh['handRig']);rig['purpose']='Anatomical controls; skin bind adapters preserve original game bones. Candidate pending Unity acceptance.'
  rig['joint_section_measurements']=json.dumps(rows)
 (O/'calibration.json').write_text(json.dumps(reports,indent=2))
 bpy.data.libraries.write(str(O/'anatomical-hand-rig.blend'),set(scenes),fake_user=True,compress=True)
 print(json.dumps([{'variant':r['variant'],'joints':len(r['jointCalibration']),'maxOffset':max(q['offsetMeters'] for q in r['jointCalibration']),'restAdapterError':r['maximumRestAdapterError']} for r in reports]))

if __name__=='__main__': build()
