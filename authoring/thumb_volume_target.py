"""Author a volume-preserving thumb target in Blender, then bake to linear skinning.

Blender's Preserve Volume armature deformation is used only during authoring.
The runtime consumes the resulting ordinary corrective shape, with no DQ solver.
"""
import bpy,runpy,numpy as np
from pathlib import Path
from mathutils import Matrix
R=Path(__file__).resolve().parents[1];T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));B=T['B'];C=B['C'];CI=B['CI']

def relax(v,sections,target,iterations=3):
 # Relax displacement across welded UV copies, keeping zero-displacement
 # vertices fixed. This avoids concentrating inverse-bake shear in one web edge.
 groups={};ids=[];mapping=[]
 for i,p in enumerate(v['p']):
  key=tuple(np.round(p,6))
  if key not in groups:groups[key]=len(ids);ids.append([])
  mapping.append(groups[key]);ids[groups[key]].append(i)
 mapping=np.array(mapping);neighbors=[set() for _ in ids]
 for face in np.concatenate(sections):
  for a,z in [(0,1),(1,2),(2,0)]:
   x,y=mapping[face[a]],mapping[face[z]]
   if x!=y:neighbors[x].add(y);neighbors[y].add(x)
 delta=target-v['p'];d=np.array([delta[g].mean(0) for g in ids]);mask=np.linalg.norm(d,axis=1)>1e-7
 for _ in range(iterations):
  nextd=d.copy()
  for i in np.flatnonzero(mask):
   if neighbors[i]:nextd[i]+=.35*(d[list(neighbors[i])].mean(0)-d[i])
  d=nextd
 return v['p']+d[mapping]

def target(v,b,sections,bones,variant,angles,length_scale=1,opposition=0):
 old=bpy.context.window.scene;scene=bpy.data.scenes.new('Thumb volume authoring')
 try:
  obj,rig,names=B['create_arm'](scene,variant,'left',v,b,sections,__import__('json').loads((R/'references/glider-first-person.json').read_text())['renderers'][0],True,True)
  _,blend,nativeTarget=T['goal'](v,b,bones,variant,angles,length_scale)
  if opposition:
   axis=bones[10,:3,3]-bones[3,:3,3];axis/=np.linalg.norm(axis)
   rotation=T['rotation'](axis,opposition,bones[24,:3,3])
   nativeTarget[24:28]=rotation@nativeTarget[24:28]
  helpers,_,_=B['wrist'](v,b,bones,.24 if variant=='v3' else 0)
  matrices=np.concatenate([nativeTarget@np.linalg.inv(b),helpers@np.linalg.inv(b[1])]);posed=[Matrix((C@m@CI).tolist()) for m in matrices]
  for i,pb in enumerate(rig.pose.bones):
   parent=names.index(pb.parent.name) if pb.parent else -1
   pb.matrix_basis=pb.bone.matrix_local.inverted()@(pb.parent.bone.matrix_local@posed[parent].inverted() if parent>=0 else Matrix.Identity(4))@posed[i]
  obj.modifiers[0].use_deform_preserve_volume=True;bpy.context.view_layer.update()
  deps=bpy.context.evaluated_depsgraph_get();evaluated=obj.evaluated_get(deps);mesh=evaluated.to_mesh();dq=np.array([p.co for p in mesh.vertices])@C[:3,:3];evaluated.to_mesh_clear()
  before=np.einsum('nij,nj->ni',blend,np.c_[v['p'],np.ones(len(v))])[:,:3]
  total=np.where(v['ids']>=24,v['w'],0).sum(1);f=np.clip((total-.05)/.45,0,1);f=f*f*(3-2*f)
  goal=before+f[:,None]*(dq-before)
  rest=np.einsum('nij,nj->ni',np.linalg.inv(blend),np.c_[goal,np.ones(len(goal))])[:,:3]
  return rest,goal
 finally:
  bpy.context.window.scene=old
  for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
  bpy.data.scenes.remove(scene)
