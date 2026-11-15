"""Render same-camera before/after from actual Unity skinned geometry in Blender."""
import bpy,runpy,json,numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parents[1];T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];O=R/'simulation/Reports/thumb-refinement'
def render():
 for var,path in M['PATHS'].items():
  _,v,b,ss=M['read'](M['chunks']((R/path).read_bytes())['arm-left']);_,old,_,_=M['read'](M['chunks']((O/f'{var}-before.mikumesh').read_bytes())['arm-left']);tri=np.concatenate(ss);profile=json.loads((O/f'{var}-before-corrections.json').read_text())
  for weapon,key,shape in [('cycler','clip-381','Cycler thumb grip'),('shotgun','clip-367','Shotgun thumb grip')]:
   j=json.loads((O/f'{var}-{key}-final.png.mesh.json').read_text());bones=np.array(j['bones']).reshape(-1,4,4);forearm=bones[1]@b[1];weapons=json.loads((O/f'{var}-{key}-final.png.weapon.json').read_text());after=(np.c_[j['positions'],np.ones(len(j['positions']))]@forearm.T)[:,:3]
   pose=next(p for m in profile['meshes'] for p in m['poses'] if p['name']==shape);p=old['p'].astype(float).copy();p[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']]);mat,_=T['blend'](old,b,bones,var);before=np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3]
   def convert(p):
    q=p.copy();q[:,1]-=1;q[:,2]*=-1;return q
   center=convert(np.array([bones[25,:3,3]*.6+bones[24,:3,3]*.4]))[0];forward=Vector(center).normalized();right=forward.cross(Vector((0,1,0))).normalized();up=right.cross(forward)
   name=f'{var} - {weapon} 0.12.28 comparison';scene=bpy.data.scenes.new(name);bpy.context.window.scene=scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.render.resolution_x=1400;scene.render.resolution_y=800;scene.render.resolution_percentage=100
   world=bpy.data.worlds.new(name);world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.2,.2,.2,1);world.node_tree.nodes['Background'].inputs[1].default_value=.7;scene.world=world
   for label,points,offset in [('before',before,-.035),('after',after,.035)]:
    for part,pts,faces,color in [('hand',convert(points),tri[:,::-1],(.55,.37,.25,1))]+[(x['name'],convert((np.c_[x['positions'],np.ones(len(x['positions']))]@forearm.T)[:,:3]),np.array(x['triangles']).reshape(-1,3)[:,::-1],(.12,.18,.22,1)) for x in weapons]:
     pts=pts+np.array(right)*offset;mesh=bpy.data.meshes.new(var+' '+label+' '+part);mesh.from_pydata(pts.tolist(),[],faces.tolist());obj=bpy.data.objects.new(mesh.name,mesh);scene.collection.objects.link(obj);mat=bpy.data.materials.new(mesh.name);mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=color;mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.8;mesh.materials.append(mat)
     if part=='hand':
      for face in mesh.polygons:face.use_smooth=True
      # Clay-only welded shading avoids mistaking atlas seams for anatomy.
      normals=np.zeros_like(pts);cross=np.cross(pts[faces[:,1]]-pts[faces[:,0]],pts[faces[:,2]]-pts[faces[:,0]])
      for k in range(3):np.add.at(normals,faces[:,k],cross)
      groups={}
      for i,pt in enumerate(pts):groups.setdefault(tuple(np.round(pt,6)),[]).append(i)
      for group in groups.values():
       normal=normals[group].sum(0);length=np.linalg.norm(normal)
       if length>1e-14:normals[group]=normal/length
      mesh.normals_split_custom_set_from_vertices(normals.tolist())
   cam=bpy.data.objects.new(name+' camera',bpy.data.cameras.new(name));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.14;cam.data.clip_start=.001;cam.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
   light=bpy.data.objects.new(name+' light',bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(light);light.data.energy=.04;light.data.size=.15;light.location=Vector(center)+Vector((-.06,.08,.08));light.rotation_euler=(Vector(center)-light.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(R/f'authoring/thumb-{weapon}-{var}-comparison.png');bpy.ops.render.render(write_still=True)
   scene['left']='0.12.27';scene['right']='0.12.28';scene['geometry']='Actual Unity final BakeMesh; original pose evaluated with original 0.12.27 base and corrections. Clay material only.'
 print('Rendered six same-camera comparisons')
if __name__=='__main__':render()
