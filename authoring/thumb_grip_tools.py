import bpy,runpy,json,numpy as np
from pathlib import Path
from mathutils import Quaternion,Vector,Matrix
R=Path(__file__).resolve().parents[1];B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));M=B['model'];O=R/'simulation/Reports/thumb-refinement';O.mkdir(exist_ok=True)
def rotation(axis,angle,pivot):
 q=np.array(Quaternion(Vector(axis),np.deg2rad(angle)).to_matrix());m=np.eye(4);m[:3,:3]=q;m[:3,3]=pivot-q@pivot;return m
def blend(v,b,bones,var):
 native=bones@b;helper,low,fraction=B['wrist'](v,b,bones,.24 if var=='v3' else 0);core=(v['ids']>=1)&(v['ids']<=3);ww=np.where(core,0,v['w']);cw=np.where(core,v['w'],0).sum(1);mat=np.zeros((len(v),4,4))
 for k in range(4):mat+=native[v['ids'][:,k]]*ww[:,k,None,None]
 mat+=(helper[low]*(1-fraction[:,None,None])+helper[low+1]*fraction[:,None,None])*cw[:,None,None]
 return mat,ww
def goal(v,b,bones,var,angles,length_scale=1.):
 native=bones@b;mat,weights=blend(v,b,bones,var);p=np.c_[v['p'],np.ones(len(v))];points=np.einsum('nij,nj->ni',mat,p);pos=bones[:,:3,3]
 axis=np.cross(pos[25]-pos[24],pos[6]-pos[24]);axis/=np.linalg.norm(axis)
 shorter=pos.copy()
 for i in range(25,28):shorter[i]=shorter[i-1]+length_scale*(pos[i]-pos[i-1])
 r1=rotation(axis,angles[0],pos[24]);r2=rotation(axis,angles[1],(r1@np.r_[shorter[25],1])[:3]);r3=rotation(axis,angles[2],(r2@r1@np.r_[shorter[26],1])[:3]);target=native.copy()
 for i in range(24,28):
  direction=pos[min(i+1,27)]-pos[min(i,26)];direction/=np.linalg.norm(direction)
  shrink=np.eye(4);shrink[:3,:3]+=np.outer(direction,direction)*(length_scale-1);shrink[:3,3]=shorter[i]-shrink[:3,:3]@pos[i]
  target[i]=(r1 if i==24 else r2@r1 if i==25 else r3@r2@r1)@shrink@native[i]
 delta=np.zeros_like(points)
 for k in range(4):delta+=np.einsum('nij,nj->ni',target[v['ids'][:,k]]-native[v['ids'][:,k]],p)*weights[:,k,None]
 total=np.where(v['ids']>=24,v['w'],0).sum(1);t=np.clip((total-.1)/.2,0,1);delta*= (t*t*(3-2*t))[:,None]
 return points+delta,mat,target
def render(points,tri,weapons,forearm,name,center,side=False):
 old=bpy.context.window.scene;scene=bpy.data.scenes.new(name);bpy.context.window.scene=scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
 world=bpy.data.worlds.new(name);world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.2,.2,.2,1);world.node_tree.nodes['Background'].inputs[1].default_value=.7;scene.world=world
 def convert(p):
  q=p.copy();q[:,1]-=1;q[:,2]*=-1;return q
 pp=convert(points[:,:3]);center=convert(np.array([center]))[0]
 for label,p,tr,color in [('hand',pp,tri[:,::-1],(.55,.37,.25,1))]+[(x['name'],convert((np.c_[x['positions'],np.ones(len(x['positions']))]@forearm.T)[:,:3]),np.array(x['triangles']).reshape(-1,3)[:,::-1],(.12,.18,.22,1)) for x in weapons]:
  mesh=bpy.data.meshes.new(name+label);mesh.from_pydata(p.tolist(),[],tr.tolist());obj=bpy.data.objects.new(mesh.name,mesh);scene.collection.objects.link(obj)
  mat=bpy.data.materials.new(name+label);mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=color;mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.8;mesh.materials.append(mat)
  if label=='hand':
   for face in mesh.polygons:face.use_smooth=True
 camera=bpy.data.objects.new(name+' camera',bpy.data.cameras.new(name));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=.063;camera.data.clip_start=.001
 camera.location=Vector(center)+Vector((-.1,0,.015)) if side else Vector((0,0,0));forward=(Vector(center)-camera.location).normalized();right=forward.cross(Vector((0,1,0))).normalized();up=right.cross(forward);camera.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
 light=bpy.data.objects.new(name+' light',bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(light);light.data.energy=.025;light.data.size=.1;light.location=Vector(center)+Vector((-.06,.08,.08));light.rotation_euler=(Vector(center)-light.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(O/(name+'.png'));bpy.ops.render.render(write_still=True)
 bpy.context.window.scene=old
 for obj in list(scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
 bpy.data.scenes.remove(scene)
