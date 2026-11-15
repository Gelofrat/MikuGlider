"""Build an editable, runtime-matched Blender review from the exported FP meshes.
Run with Blender MCP after retarget_first_person_hands.apply().
00_EXPORT contains native 28-bone meshes; the preview scenes also reproduce
the existing nine runtime wrist helpers. Edit/export the 00_EXPORT objects.
"""
import bpy,runpy,json,math
from pathlib import Path
import numpy as np
from mathutils import Matrix,Quaternion,Vector

ROOT=Path(__file__).resolve().parents[1]
model=runpy.run_path(str(ROOT/'authoring/retarget_first_person_hands.py'))
C=np.array([[1,0,0,0],[0,0,-1,0],[0,1,0,0],[0,0,0,1]],dtype=float); CI=np.linalg.inv(C)

def wrist(v,b,bones,length):
    matrices=bones@b;rest=np.linalg.inv(b)[:,:3,3];axis=rest[2]-rest[1];axis/=np.linalg.norm(axis)
    a=rest[2]-axis*.09;z=rest[2]+axis*.035
    q0=Matrix(matrices[1].tolist()).to_quaternion();q1=Matrix(matrices[3].tolist()).to_quaternion()
    scale=np.linalg.norm(bones[1,:3,:3],axis=0)
    p0=(matrices[1]@np.r_[a,1])[:3];p1=(matrices[3]@np.r_[z,1])[:3]
    t0=matrices[1,:3,:3]@axis*.125;t1=matrices[3,:3,:3]@axis*.125
    distances=np.r_[-length,-(length+.09)/2,np.linspace(-.09,.035,7)] if length>.09 else np.linspace(-.09,.035,9)
    distance=(v['p']-rest[2])@axis;low=np.clip(np.searchsorted(distances,distance,side='right')-1,0,7)
    fraction=np.clip((distance-distances[low])/(distances[low+1]-distances[low]),0,1)
    retained=((v['w']>0)&((v['ids']<1)|(v['ids']>3))).sum(1);fraction[retained>2]=np.round(fraction[retained>2])
    if length>.09:
        rel=q0.inverted()@q1;projected=np.dot(np.array([rel.x,rel.y,rel.z]),axis)*axis
        twist=Quaternion((rel.w,*projected));twist.normalize()
        if twist.w<0:twist.negate()
        swing=rel@twist.inverted()
    result=[]
    for d in distances:
        t=np.clip((d+.09)/.125,0,1);cube=t*t*t;square=t*t;centerRest=rest[2]+axis*d
        center=((matrices[1]@np.r_[centerRest,1])[:3] if d<-.09 else
                (2*cube-3*square+1)*p0+(cube-2*square+t)*t0+(-2*cube+3*square)*p1+(cube-square)*t1)
        rotation=(q0@Quaternion().slerp(swing,t)@Quaternion().slerp(twist,(d+length)/(length+.035)) if length>.09 else q0.slerp(q1,t))
        transform=np.eye(4);transform[:3,:3]=np.array(rotation.to_matrix())@np.diag(scale)
        transform[:3,3]=center-transform[:3,:3]@centerRest
        result.append(transform)
    return np.array(result),low,fraction

def create_arm(scene,variant,side,v,b,sections,reference,helpers=False,preview=False):
    names=[x['name'] for x in reference['bones']]
    if helpers:names+=['Miku wrist transition '+str(i) for i in range(9)]
    mesh=bpy.data.meshes.new(variant+' '+side);tri=np.concatenate(sections)
    mesh.from_pydata((v['p']@C[:3,:3].T).tolist(),[],tri[:,::-1].tolist());mesh.uv_layers.new(name='Runtime atlas')
    for loop in mesh.loops:mesh.uv_layers[0].data[loop.index].uv=v['uv'][loop.vertex_index]
    for polygon in mesh.polygons:polygon.use_smooth=True
    mesh.normals_split_custom_set_from_vertices((v['n']@C[:3,:3].T).tolist())
    obj=bpy.data.objects.new(variant+' arm-'+side+(' preview' if preview else ' export'),mesh);scene.collection.objects.link(obj)
    mat=bpy.data.materials.get(variant+' arm atlas')
    if mat is None:
        mat=bpy.data.materials.new(variant+' arm atlas');mat.use_nodes=True
        image=bpy.data.images.load(str(ROOT/Path(model['PATHS'][variant]).parent/'textures/body-albedo.png'),check_existing=True);image.pack()
        texture=mat.node_tree.nodes.new('ShaderNodeTexImage');texture.image=image
        shader=mat.node_tree.nodes.get('Principled BSDF');mat.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color']);shader.inputs['Roughness'].default_value=.8
    mesh.materials.append(mat)
    arm=bpy.data.armatures.new(obj.name+' rig');rig=bpy.data.objects.new(arm.name,arm);scene.collection.objects.link(rig)
    bpy.context.window.scene=scene;bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
    for i,name in enumerate(names):
        bone=arm.edit_bones.new(name);bone.head=(0,0,0);bone.tail=(0,.025,0)
        bone.matrix=Matrix((C@np.linalg.inv(b[i] if i<28 else b[1])@CI).tolist());bone.length=.025
        if i<28:
            parent=reference['bones'][i]['parentBoneIndex']
            if parent is not None and parent>=0:bone.parent=arm.edit_bones[names[parent]]
    bpy.ops.object.mode_set(mode='OBJECT');obj.parent=rig;modifier=obj.modifiers.new('Native linear skinning','ARMATURE');modifier.object=rig;modifier.use_deform_preserve_volume=False
    groups=[obj.vertex_groups.new(name=name) for name in names]
    if helpers:_,low,fraction=wrist(v,b,np.linalg.inv(b),.24 if variant=='v3' else 0)
    for i in range(len(v)):
        values={};core=0
        for bone,weight in zip(v['ids'][i],v['w'][i]):
            if helpers and bone in (1,2,3):core+=float(weight)
            elif weight>0:values[int(bone)]=values.get(int(bone),0)+float(weight)
        if helpers:
            values[28+int(low[i])]=core*(1-float(fraction[i]));values[29+int(low[i])]=core*float(fraction[i])
        for bone,weight in values.items():
            if weight>0:groups[bone].add([i],weight,'REPLACE')
    rig.show_in_front=True
    obj['variant']=variant;obj['runtime_mesh']='arm-'+side;obj['preview_only']=helpers;obj['runtime_bindposes']=json.dumps(b.tolist());obj['runtime_sections']=json.dumps([len(s) for s in sections])
    return obj,rig,names

def build():
    if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    reference=json.loads((ROOT/'references/glider-first-person.json').read_text())['renderers']
    folder=ROOT/'simulation/Reports/all-hand-native-clips';catalog=json.loads((folder/'clips.json').read_text());bones=np.fromfile(folder/'clip-bones.bin',dtype='<f4').reshape(catalog['frames'],catalog['bones'],4,4)
    keys=['clip-313','clip-420','clip-367','weapon-plate-launcher-inspect','weapon-wide-gun-inspect','weapon-wide-gun-reload','weapon-bounce-shotgun-inspect']
    samples=[];cursor=1;markers=[]
    for key in keys:
        clip=next(c for c in catalog['clips'] if c['key']==key);markers.append((cursor,key))
        times=[0] if key.startswith('clip-') else sorted(set(range(0,clip['count'],6))|{clip['count']-1})
        for i in times:samples.append((cursor+i,bones[clip['offset']+i]))
        cursor+=(clip['count'] if not key.startswith('clip-') else 30)+10
    export=bpy.data.scenes.new('00_EXPORT - native meshes');created=[export]
    for number,(variant,path) in enumerate(model['PATHS'].items()):
        scene=bpy.data.scenes.new(variant+' - weapon poses');created.append(scene);scene.render.fps=60
        scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
        scene.frame_start=1;scene.frame_end=cursor-10
        for frame,key in markers:scene.timeline_markers.new(key,frame=frame)
        for k,side in enumerate(['left','right']):
            _,v,b,sections=model['read'](model['chunks']((ROOT/path).read_bytes())['arm-'+side])
            obj,rig,_=create_arm(export,variant,side,v,b,sections,reference[k]);rig.location.x=(number-1)*3;obj['export_mesh']=True
            helper=side=='left' or variant=='v2';obj,rig,names=create_arm(scene,variant,side,v,b,sections,reference[k],helper,True);obj['export_mesh']=False
            previous={}
            for frame,pose in samples:
                matrices=pose[k*28:k*28+28]
                if helper:
                    transforms,_,_=wrist(v,b,matrices,.24 if variant=='v3' else 0)
                    matrices=np.concatenate([matrices,transforms@np.linalg.inv(b[1])])
                scene.frame_set(frame)
                poses=[Matrix((C@matrix@CI).tolist()) for matrix in matrices]
                for i,name in enumerate(names):
                    bone=rig.pose.bones[name];bone.rotation_mode='QUATERNION'
                    # Compute the local basis against the desired parent pose.
                    # Assigning pose.matrix in a loop without depsgraph updates
                    # otherwise accumulates stale parent scales into the keys.
                    parent=reference[k]['bones'][i]['parentBoneIndex'] if i<28 else None
                    basis=bone.bone.matrix_local.inverted()
                    if parent is not None and parent>=0:
                        basis=basis@rig.pose.bones[names[parent]].bone.matrix_local@poses[parent].inverted()
                    bone.matrix_basis=basis@poses[i]
                    rotation=bone.rotation_quaternion.copy()
                    if name in previous:rotation.make_compatible(previous[name])
                    bone.rotation_quaternion=rotation;previous[name]=rotation.copy()
                    bone.keyframe_insert(data_path='location',frame=frame);bone.keyframe_insert(data_path='rotation_quaternion',frame=frame);bone.keyframe_insert(data_path='scale',frame=frame)
            rig['preview_note']='Native animation samples at 10 Hz plus endpoints; existing wrist helpers reproduced. No cloth or IK.'
            # Unity's forward/up camera convention is left-handed. Mirror only
            # the preview root so a Blender camera has the same screen-right.
            # Native export vertices and bone-local pose data stay unchanged.
            rig.scale.x=-1
            rig['camera_handedness_preview_only']=True
        camera=bpy.data.objects.new('First-person 55 deg',bpy.data.cameras.new('First-person 55 deg'));scene.collection.objects.link(camera);scene.camera=camera
        camera.rotation_euler=Vector((0,-1,0)).to_track_quat('-Z','Y').to_euler();camera.data.sensor_fit='VERTICAL';camera.data.lens=camera.data.sensor_height/(2*math.tan(math.radians(27.5)));camera.data.clip_start=.001
        scene.frame_set(1)
    note=bpy.data.texts.new('READ ME - hands review');note.write('00_EXPORT contains the six corrected runtime meshes and native 28-bone rigs. Preview scenes contain the existing wrist helper approximation as keyed native poses. Timeline markers: Plate, Trident, shotgun, inspection and reload. Source UV, triangles, vertex counts and bindposes preserved. Older full-character *_Glider.blend files are historical sources and do not contain these FP corrections. The runtime remains linear four-weight skinning.\n')
    for scene in list(bpy.data.scenes):
        if scene not in created:bpy.data.scenes.remove(scene)
    for action in bpy.data.actions:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='LINEAR'
    bpy.context.window.scene=created[-1]
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':area.spaces.active.region_3d.view_perspective='CAMERA'
    bpy.data.orphans_purge(do_recursive=True)
    for i,scene in enumerate(created):
        scene.name='00_EXPORT - native meshes' if i==0 else 'v'+str(i)+' - weapon poses'
        for obj in scene.objects:
            if obj.type=='MESH':
                obj.name=obj['variant']+' '+obj['runtime_mesh']+(' [EXPORT]' if obj['export_mesh'] else ' [PREVIEW]')
                obj.parent.name=obj.name+' rig'
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'authoring/first-person-hands.blend'),compress=True)
    print('Saved six export meshes, three pose scenes,',len(samples),'pose samples per scene')

if __name__=='__main__':build()
