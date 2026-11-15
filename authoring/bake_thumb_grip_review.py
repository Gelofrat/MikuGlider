"""Bake corrected native hand motion through Blender, at <=1/60 s sampling.

These are fully composed review clips (base + additive already evaluated).
They must not be substituted for additive game clips in the Animancer graph.
"""
import bpy,runpy,json,hashlib,math
from pathlib import Path
import numpy as np
from mathutils import Matrix,Quaternion,Vector
R=Path(__file__).resolve().parents[1];B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));M=B['model'];C=B['C'];CI=B['CI']
F=R/'simulation/Reports/cycler-thumb-review/native-clips';catalog=json.loads((F/'clips.json').read_text());native=np.fromfile(F/'clip-bones.bin','<f4').reshape(catalog['frames'],56,4,4)
clips=[c for c in catalog['clips'] if c['label'].startswith(('SMG ','Shotgun ')) or c['key'].startswith(('weapon-smg-','weapon-bounce-shotgun-'))]
reference=json.loads((R/'references/glider-first-person.json').read_text())['renderers'];OUT=R/'authoring/thumb-grip-bake';OUT.mkdir(exist_ok=True)

def activation(pose,bones):
 value=1.
 for d in pose['drivers']:
  i=d['bone'];p=reference[0]['bones'][i]['parentBoneIndex'];q=Matrix((np.linalg.inv(bones[p])@bones[i]).tolist()).to_quaternion();r=d['rotation'];a=math.degrees(2*math.acos(np.clip(abs(q.dot(Quaternion((r['w'],r['x'],r['y'],r['z'])))),0,1)));t=np.clip((a-d['innerRadius'])/(d['radius']-d['innerRadius']),0,1);value=min(value,1-t*t*(3-2*t))
 return value

def action(obj,frames,channels,name,id_type='OBJECT'):
 a=bpy.data.actions.new(name);a.use_fake_user=True;slot=a.slots.new(id_type=id_type,name=obj.name);layer=a.layers.new('Baked 60 Hz');strip=layer.strips.new(type='KEYFRAME');bag=strip.channelbag(slot,ensure=True)
 for path,index,values in channels:
  # Constant channels need endpoints only. No lossy rotation simplification.
  use=[0,len(frames)-1] if np.max(np.abs(values-values[0]))<1e-8 else np.arange(len(frames))
  curve=bag.fcurves.new(data_path=path,index=index);curve.keyframe_points.add(len(use));curve.keyframe_points.foreach_set('co',np.column_stack([frames[use],values[use]]).ravel())
  for k in curve.keyframe_points:k.interpolation='LINEAR'
  curve.update()
 obj.animation_data_create();obj.animation_data.action=a;obj.animation_data.action_slot=slot
 return a

def bake():
 for old in list(bpy.data.scenes):
  if ' - BAKED 60 Hz' in old.name:
   for obj in list(old.objects):bpy.data.objects.remove(obj,do_unlink=True)
   bpy.data.scenes.remove(old)
 # Discard only this generator's abandoned fake-user actions from earlier runs.
 for old in list(bpy.data.actions):
  if old.use_fake_user and old.users==1 and ('native poses + wrist helpers' in old.name or 'baked thumb corrective weights' in old.name):bpy.data.actions.remove(old)
 scenes=[];manifest={'version':'0.12.28','sampleRate':60,'scope':'composed review clips; not a drop-in replacement for native additive clips','clips':[],'variants':{},'validation':{}}
 cursor=1;offset=0;timeline=[]
 for clip in clips:
  frames=cursor+np.linspace(0,clip['duration']*60,clip['count']);timeline.extend(frames);manifest['clips'].append(dict(clip,bakeOffset=offset,startFrame=float(cursor),endFrame=float(frames[-1])));offset+=clip['count'];cursor=int(math.ceil(frames[-1]))+12
 timeline=np.array(timeline);manifest['framesPerVariant']=offset
 for var,path in M['PATHS'].items():
  scene=bpy.data.scenes.new(var+' - BAKED 60 Hz');scenes.append(scene);bpy.context.window.scene=scene;scene.render.fps=60;scene.frame_end=cursor;scene.render.resolution_x=1280;scene.render.resolution_y=720
  for clip in manifest['clips']:scene.timeline_markers.new(clip['key'],frame=int(clip['startFrame']))
  profile=json.loads(((R/path).parent/'corrections.json').read_text());poses=next(x for x in profile['meshes'] if x['key']=='arm-left wrist transition')['poses'];shapes=np.zeros((offset,2));sideinfo=[];unity=[];rigs=[]
  for sideidx,side in enumerate(['left','right']):
   _,v,b,ss=M['read'](M['chunks']((R/path).read_bytes())['arm-'+side]);helpers=side=='left' or var=='v2';obj,rig,names=B['create_arm'](scene,var,side,v,b,ss,reference[sideidx],helpers,True);obj.name=var+' baked '+side;rig.name=obj.name+' rig';rig.show_in_front=False;rig.data.display_type='STICK';rig.scale.x=-1;rigs.append(rig)
   parents=[x['parentBoneIndex'] if x['parentBoneIndex'] is not None else -1 for x in reference[sideidx]['bones']]+([-1]*9 if helpers else []);count=len(names);basis=np.empty((offset,count,10));localunity=np.empty_like(basis);worldlist=[];previous=[None]*count;rest=[pb.bone.matrix_local.copy() for pb in rig.pose.bones];sample=0
   if side=='left':
    obj.shape_key_add(name='Basis')
    for pose in poses:
     key=obj.shape_key_add(name=pose['name']);points=v['p'].copy();points[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']]);key.data.foreach_set('co',(points@C[:3,:3].T).ravel())
   for clip in clips:
    which=0 if clip['label'].startswith('SMG ') or clip['key'].startswith('weapon-smg-') else 1
    for f in range(clip['offset'],clip['offset']+clip['count']):
     bones=native[f,sideidx*28:sideidx*28+28].astype(float)
     if side=='left':shapes[sample,which]=activation(poses[which],bones)
     matrices=bones
     if helpers:
      h,_,_=B['wrist'](v,b,bones,.24 if var=='v3' and side=='left' else 0);matrices=np.concatenate([bones,h@np.linalg.inv(b[1])])
     world=[Matrix((C@m@CI).tolist()) for m in matrices]
     for i in range(count):
      parent=parents[i];local=world[i] if parent<0 else world[parent].inverted()@world[i];base=rest[i].inverted()@(rest[parent] if parent>=0 else Matrix.Identity(4))@local;p,q,s=base.decompose()
      if previous[i] is not None:q.make_compatible(previous[i])
      q.normalize();previous[i]=q.copy();basis[sample,i]=[*p,q.w,q.x,q.y,q.z,*s]
      # Reconstruct from the actual TRS that Blender will animate, then convert
      # back to Unity axes. This also catches non-TRS/shear loss in the bake.
      reconstructed=(rest[parent].inverted() if parent>=0 else Matrix.Identity(4))@rest[i]@Matrix.LocRotScale(p,q,s);pu,qu,su=Matrix((CI@np.array(reconstructed)@C).tolist()).decompose();qu.normalize();localunity[sample,i]=[*pu,qu.x,qu.y,qu.z,qu.w,*su]
     sample+=1
   channels=[]
   for i,name in enumerate(names):
    rig.pose.bones[name].rotation_mode='QUATERNION'
    for prop,start,size in [('location',0,3),('rotation_quaternion',3,4),('scale',7,3)]:
     for k in range(size):channels.append((f'pose.bones["{name}"].{prop}',k,basis[:,i,start+k]))
   action(rig,timeline,channels,var+' '+side+' native poses + wrist helpers')
   if side=='left':action(obj.data.shape_keys,timeline,[(f'key_blocks["{p["name"]}"].value',0,shapes[:,i]) for i,p in enumerate(poses)],var+' baked thumb corrective weights','KEY')
   sideinfo.append({'name':side,'names':names,'parents':parents});unity.append(localunity.astype('<f4'))
  rows=np.concatenate([unity[0].reshape(offset,-1),unity[1].reshape(offset,-1),(shapes*100).astype('<f4')],axis=1).astype('<f4');rows.tofile(OUT/(var+'.bin'))
  manifest['variants'][var]={'modelSha256':hashlib.sha256((R/path).read_bytes()).hexdigest(),'correctionsSha256':hashlib.sha256(((R/path).parent/'corrections.json').read_bytes()).hexdigest(),'sides':sideinfo,'stride':rows.shape[1],'file':var+'.bin','shapeNames':['MikuManual:'+p['name'] for p in poses]}
  cam=bpy.data.objects.new(var+' camera',bpy.data.cameras.new(var+' camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.rotation_euler=Vector((0,-1,0)).to_track_quat('-Z','Y').to_euler();cam.data.sensor_fit='VERTICAL';cam.data.lens=cam.data.sensor_height/(2*math.tan(math.radians(27.5)));cam.data.clip_start=.001
  scene['instructions']='All 40 Cycler/shotgun poses, inspection, reload, fire and locomotion are baked at <=1/60 s. Timeline markers identify clips. Both arms plus wrist helpers and corrective weights. Weapons are viewed in Unity; this scene contains hands only.'
  # Evaluate real Blender F-curves at endpoints and midpoint of every clip.
  error=0.;checked=0
  for clip in manifest['clips']:
   for j in sorted(set([0,clip['count']//2,clip['count']-1])):
    row=clip['bakeOffset']+j;frame=timeline[row];scene.frame_set(int(frame),subframe=float(frame%1));bpy.context.view_layer.update()
    for si,rig in enumerate(rigs):
     for i,pb in enumerate(rig.pose.bones):
      parent=sideinfo[si]['parents'][i];loc=pb.matrix if parent<0 else rig.pose.bones[sideinfo[si]['names'][parent]].matrix.inverted()@pb.matrix;pu,qu,su=Matrix((CI@np.array(loc)@C).tolist()).decompose();expected=unity[si][row,i];error=max(error,float(np.linalg.norm(np.array(pu)-expected[:3])),float(np.linalg.norm(np.array(su)-expected[7:])));checked+=1
      qe=Quaternion((expected[6],*expected[3:6]));qu.normalize();qe.normalize();assert abs(qu.dot(qe))>.999999,(var,clip['key'],j,si,i,pb.name,float(qu.dot(qe)))
  assert error<1e-4,(var,error);manifest['validation'][var]={'sampledBoneTransforms':checked,'maximumLocalPositionOrScaleError':error}
  scene.frame_set(next(int(c['startFrame']) for c in manifest['clips'] if c['key']=='clip-381'));print('Baked',var,offset,'frames',flush=True)
 (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');bpy.data.libraries.write(str(R/'authoring/thumb-grip-baked.blend'),set(scenes),fake_user=True,compress=True)
 print('Saved dense Blender bake and Unity interchange',json.dumps(manifest['validation']))
if __name__=='__main__':bake()
