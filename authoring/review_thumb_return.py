"""Blender MCP: textured, same-camera review of captured thumb transitions."""
import bpy,runpy,json,gzip
from pathlib import Path
import numpy as np
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parents[1];O=R/'scratch/thumb-return'
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M']
def render(name,p,tri,uv,texture,center,camera,weapons=(),scale=.045):
 old=bpy.context.window.scene;scene=bpy.data.scenes.new(name);bpy.context.window.scene=scene
 scene.render.engine='CYCLES';scene.cycles.samples=12;scene.render.resolution_x=800;scene.render.resolution_y=600;scene.render.resolution_percentage=100
 world=bpy.data.worlds.new(name);world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.23,.23,.23,1);world.node_tree.nodes['Background'].inputs[1].default_value=.8;scene.world=world
 for label,points,faces in [('hand',p,tri)]+[(w['name'],np.array(w['positions']),np.array(w['triangles']).reshape(-1,3)) for w in weapons]:
  mesh=bpy.data.meshes.new(label);mesh.from_pydata(points.tolist(),[],faces[:,::-1].tolist());obj=bpy.data.objects.new(label,mesh);scene.collection.objects.link(obj)
  mat=bpy.data.materials.new(label);mat.use_nodes=True;shader=mat.node_tree.nodes['Principled BSDF'];shader.inputs['Roughness'].default_value=.65;shader.inputs['Base Color'].default_value=(.34,.36,.38,1) if label=='hand' else (.09,.11,.13,1);mesh.materials.append(mat)
  if label=='hand':
   for f in mesh.polygons:f.use_smooth=True
   if texture:
    layer=mesh.uv_layers.new();layer.data.foreach_set('uv',np.array([uv[x.vertex_index] for x in mesh.loops]).ravel());im=mat.node_tree.nodes.new('ShaderNodeTexImage');im.image=bpy.data.images.load(str(texture),check_existing=True);mat.node_tree.links.new(im.outputs['Color'],shader.inputs['Base Color'])
 cam=bpy.data.objects.new(name+' camera',bpy.data.cameras.new(name));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=scale;cam.data.clip_start=.0001;cam.location=camera
 forward=(Vector(center)-cam.location).normalized();right=forward.cross(Vector((0,1,0))).normalized();up=right.cross(forward);cam.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
 lamp=bpy.data.objects.new(name+' light',bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(lamp);lamp.location=Vector(camera)+Vector((-.03,.04,.02));lamp.data.energy=.08;lamp.data.size=.1;lamp.rotation_euler=(Vector(center)-lamp.location).to_track_quat('-Z','Y').to_euler()
 scene.render.filepath=str(O/(name+'.png'));bpy.ops.render.render(write_still=True);bpy.context.window.scene=old
 for obj in list(scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
 bpy.data.scenes.remove(scene)

def rest():
 diag=json.loads((O/'diagnosis.json').read_text())
 for var in ('v2','v3'):
  _,v,b,ss=M['read'](M['chunks']((R/M['PATHS'][var]).read_bytes())['arm-left']);j=np.linalg.inv(b)[:,:3,3];nail=np.array(diag[var]['nail']);ctr=v['p'][nail].mean(0);axis=j[27]-j[26];axis/=np.linalg.norm(axis);normal=ctr-(j[26]+axis*np.dot(ctr-j[26],axis));normal/=np.linalg.norm(normal)
  center=j[25]*.3+j[26]*.7;offset=center
  for side,direction in [('nail',normal),('side',np.cross(normal,axis))]:render(var+'-rest-'+side,v['p']-offset,np.concatenate(ss),v['uv'],R/f'assets/{var}/textures/body-albedo.png',center-offset,center-offset+direction*.2,scale=.095)

def recorded(indices=(40,167,168),stage='before'):
 ref=json.loads((R/'references/glider-first-person.json').read_text())['renderers'][0];g=json.loads(gzip.decompress((R/'references'/ref['mesh']['geometryFile']).read_bytes()));nb=np.array(ref['mesh']['bindposesUnity'])
 for index in indices:
  for var in ('v2','v3','glider'):
   data=json.loads((O/f'{"v2" if var=="glider" else var}-{stage}-{index}.json').read_text());cam=np.array(data['camera']['firstPersonPass']['worldToCamera']).reshape(4,4);bones=cam@np.array(data['bones']).reshape(-1,4,4)
   if var=='glider':
    p=np.array(g['positions']);idx=np.array(g['boneIndices']);w=np.array(g['boneWeights']);p=sum(np.einsum('nij,nj->ni',(bones@nb)[idx[:,k]],np.c_[p,np.ones(len(p))])*w[:,k,None] for k in range(4))[:,:3];tri=np.concatenate([np.array(x['indices']).reshape(-1,3) for x in g['submeshes']]);uv=g['uv0'];tex=None
   else:p=(np.c_[data['positions'],np.ones(len(data['positions']))]@cam.T)[:,:3];tri=np.array(data['triangles']).reshape(-1,3);uv=data['uv'];tex=R/f'assets/{var}/textures/body-albedo.png'
   weapons=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@cam.T)[:,:3]) for w in data['weapons']]
   center=(bones[25,:3,3]+bones[26,:3,3])*.5
   render(f'{var}-{stage}-recorded-{index}',p,tri,uv,tex,center,np.zeros(3),weapons,scale=.042)

if __name__=='__main__':rest()
