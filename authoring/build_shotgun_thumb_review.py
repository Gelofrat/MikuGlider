import bpy,runpy,json,numpy as np
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
R=Path(__file__).resolve().parents[1];B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));M=B['model'];C=B['C'];CI=B['CI'];O=R/'simulation/Reports/shotgun-thumb-review';A=runpy.run_path(str(R/'authoring/apply_shotgun_thumb.py'));reference=json.load(open(R/'references/glider-first-person.json'))['renderers'][0]
manifest=json.load(open(R/'authoring/shotgun-thumb.json'));payload=np.load(R/'authoring/shotgun-thumb.npz');N=R/'simulation/Reports/cycler-thumb-review/native-clips';cat=json.load(open(N/'clips.json'));allbones=np.fromfile(N/'clip-bones.bin','<f4').reshape(cat['frames'],56,4,4)
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
for scene in list(bpy.data.scenes):
 if scene.name.startswith('Shotgun thumb diagnostic') or scene.name.endswith(' - Shotgun thumb'):bpy.data.scenes.remove(scene)
def apply_pose(rig,names,matrices):
 poses=[Matrix((C@x@CI).tolist()) for x in matrices]
 for i,name in enumerate(names):
  pb=rig.pose.bones[name];parent=reference['bones'][i]['parentBoneIndex'] if i<28 else None;basis=pb.bone.matrix_local.inverted()
  if parent is not None and parent>=0:basis=basis@rig.pose.bones[names[parent]].bone.matrix_local@poses[parent].inverted()
  pb.matrix_basis=basis@poses[i]
for var,path in M['PATHS'].items():
 _,v,b,ss=M['read'](M['chunks']((R/path).read_bytes())['arm-left']);target=payload[var];bones=np.array(json.load(open(O/f'{var}-clip-367.png.mesh.json'))['bones']).reshape(-1,4,4)
 scene=bpy.data.scenes.new(var+' - Shotgun thumb');bpy.context.window.scene=scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=1500;scene.render.resolution_y=900;scene.render.resolution_percentage=100
 world=bpy.data.worlds.new(var+' Shotgun studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.15,.15,.15,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 mat=bpy.data.materials.new(var+' Shotgun clay');mat.use_nodes=True;mat.diffuse_color=(.48,.32,.22,1);mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=mat.diffuse_color;mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.8
 metal=bpy.data.materials.new(var+' Shotgun weapon');metal.diffuse_color=(.14,.19,.23,1);metal.use_nodes=True;metal.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=metal.diffuse_color
 view=np.eye(4);view[0,0]=-1;view[1,3]=-1
 helper,_,_=B['wrist'](v,b,bones,.24 if var=='v3' else 0);matrices=np.concatenate([bones,helper@np.linalg.inv(b[1])]);forearm=bones[1]@b[1]
 for label,offset,value in [('before',.035,0),('after',-.035,1)]:
  obj,rig,names=B['create_arm'](scene,var,'left',v,b,ss,reference,True,True);obj.name=var+' Shotgun '+label+' editable mesh';rig.name=var+' Shotgun '+label+' rig';key=A['shape'](obj,target);key.value=value;apply_pose(rig,names,np.einsum('ij,njk->nik',view,matrices));rig.location.x=offset;rig.show_in_front=False;rig.data.display_type='STICK';rig.hide_set(True);obj.data.materials.clear();obj.data.materials.append(mat)
  for item in json.load(open(O/f'{var}-clip-367.png.weapon.json')):
   p=np.array(item['positions']);p=(np.c_[p,np.ones(len(p))]@(C@view@forearm).T)[:,:3];p[:,0]+=offset;tri=np.array(item['triangles']).reshape(-1,3);mesh=bpy.data.meshes.new(var+' Shotgun '+label+' '+item['name']);mesh.from_pydata(p.tolist(),[],tri.tolist());mesh.materials.append(metal);weapon=bpy.data.objects.new(mesh.name,mesh);scene.collection.objects.link(weapon)
 center=(C@view@np.r_[bones[25,:3,3],1])[:3];camera=bpy.data.objects.new(var+' Shotgun comparison camera',bpy.data.cameras.new('Shotgun comparison camera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=.133;camera.data.clip_start=.001;camera.location=(0,0,0);camera.rotation_euler=Vector(center).to_track_quat('-Z','Y').to_euler()
 light=bpy.data.objects.new(var+' Shotgun studio key',bpy.data.lights.new('Shotgun studio key','AREA'));scene.collection.objects.link(light);light.location=Vector(center)+Vector((.05,.08,.1));light.rotation_euler=(Vector(center)-light.location).to_track_quat('-Z','Y').to_euler();light.data.energy=.08;light.data.size=.15
 scene['comparison_before']='0.12.26, left';scene['comparison_after']='0.12.27, right: Shotgun-only corrective';scene['instructions']='Both meshes are editable. Shape Keys > Shotgun thumb grip; 0 = baseline, 1 = corrected. Rigs are hidden only for clear viewing.'

 scene.render.filepath=str(R/'authoring'/('shotgun-thumb-'+var+'-comparison.png'));bpy.ops.render.render(write_still=True)
 preview=bpy.data.scenes[var+' - weapon poses'];bpy.context.window.scene=preview;cursor=2600;previous={}
 for marker in list(preview.timeline_markers):
  if marker.frame>=2600:preview.timeline_markers.remove(marker)
 left=bpy.data.objects[var+' arm-left [PREVIEW]'];key=left.data.shape_keys.key_blocks[A['SHAPE']]
 key.value=0;key.keyframe_insert('value',frame=1);key.keyframe_insert('value',frame=2599)
 cycler=left.data.shape_keys.key_blocks['Cycler thumb grip'];cycler.value=0;cycler.keyframe_insert('value',frame=2599)
 for clipkey in ['clip-367','weapon-bounce-shotgun-inspect','weapon-bounce-shotgun-reload','weapon-bounce-shotgun-fire-1']:
  clip=next(c for c in cat['clips'] if c['key']==clipkey);preview.timeline_markers.new(clipkey,frame=cursor)
  for sample in sorted(set(range(0,clip['count'],6))|{clip['count']-1}):
   preview.frame_set(cursor+sample);index=clip['offset']+sample
   for side_index,side in enumerate(['left','right']):
    obj=bpy.data.objects[var+' arm-'+side+' [PREVIEW]'];rig=obj.parent;names=[g.name for g in obj.vertex_groups]
    _,vv,bb,_=M['read'](M['chunks']((R/path).read_bytes())['arm-'+side]);native=allbones[index,side_index*28:side_index*28+28]
    if len(names)>28:
     helped,_,_=B['wrist'](vv,bb,native,.24 if var=='v3' and side=='left' else 0);native=np.concatenate([native,helped@np.linalg.inv(bb[1])])
    apply_pose(rig,names,native)
    for pb in rig.pose.bones:
     pb.rotation_mode='QUATERNION';identity=(side,pb.name)
     if identity in previous:pb.rotation_quaternion.make_compatible(previous[identity])
     previous[identity]=pb.rotation_quaternion.copy()
     for prop in ['location','rotation_quaternion','scale']:pb.keyframe_insert(prop,frame=cursor+sample)
   value=1
   for driver in manifest['models'][var]['pose']['drivers']:
    i=driver['bone'];parent=reference['bones'][i]['parentBoneIndex'];q=Matrix((np.linalg.inv(allbones[index,parent])@allbones[index,i]).tolist()).to_quaternion();r=driver['rotation'];targetq=Quaternion((r['w'],r['x'],r['y'],r['z']));angle=np.degrees(2*np.arccos(np.clip(abs(q.dot(targetq)),0,1)));t=np.clip((angle-5)/55,0,1);value=min(value,1-t*t*(3-2*t))
   key.value=value;key.keyframe_insert('value',frame=cursor+sample);cycler.value=0;cycler.keyframe_insert('value',frame=cursor+sample)
  cursor+=clip['count']+10
 preview.frame_end=cursor;preview.frame_set(1)
for action in bpy.data.actions:
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for curve in bag.fcurves:
     for point in curve.keyframe_points:
      if point.co.x>=2599:point.interpolation='LINEAR'
bpy.context.window.scene=bpy.data.scenes['v3 - Shotgun thumb'];bpy.context.window.scene.frame_set(1)
text=bpy.data.texts.get('Shotgun thumb - 0.12.27') or bpy.data.texts.new('Shotgun thumb - 0.12.27');text.clear();text.write('Shotgun support-thumb correction. Before: left; after: right.\nEditable shape key: Shotgun thumb grip.\nNative weapon poses: frames 2600 onward; hold, inspect, reload, fire.\nThe base mesh is unchanged. Native total thumb weight <= 0.1 is excluded; smooth transition to 0.3.\nRuntime matches Lever Action Shotgun and thumb A/B rotations.\nCycler and shotgun shapes are mutually exclusive.\n')
bpy.ops.wm.save_as_mainfile(filepath=str(R/'authoring/first-person-hands.blend'),compress=True);print('Saved editable shotgun comparisons and native animation previews')
