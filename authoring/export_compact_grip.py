"""Editable Blender rig and shape keys for the accepted runtime grip."""
import bpy,runpy,json
from pathlib import Path
import numpy as np
from mathutils import Matrix
R=Path(__file__).resolve().parents[1];F=runpy.run_path(str(R/'authoring/fit_v1_plate_grip.py'));O=F['O'];B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));E=runpy.run_path(str(R/'authoring/pose_corrections.py'))

def save(name):
    v,b,ss,d,bones=F['load']();profile=json.loads((O/'v1-corrections.json').read_text());v=E['apply_weights'](v,profile);scene=bpy.data.scenes.new('V1 Plate compact grip - editable');obj,rig,names=B['create_arm'](scene,'v1','left',v,b,ss,E['BA']['reference'][0],helpers=True,preview=True);C=B['C'];CI=B['CI'];obj.shape_key_add(name='Basis')
    for pose in profile['meshes'][0]['poses']:
        key=obj.shape_key_add(name=pose['name']);delta=np.zeros((len(v),3));delta[pose['vertices']]=[[x[k] for k in ('x','y','z')] for x in pose['deltas']];key.data.foreach_set('co',((v['p']+delta)@C[:3,:3].T).ravel());key.value=1. if pose['weapon']=='YAPlateLauncher' else 0.
    p,posed,_=E['evaluate'](v,b,bones,profile,'v1','YAPlateLauncher');helpers,_,_=B['wrist'](v,b,posed,0);world=np.concatenate([posed,helpers@np.linalg.inv(b[1])])
    poses=[Matrix((C@matrix@CI).tolist()) for matrix in world]
    for i,n in enumerate(names):
        bone=rig.pose.bones[n];parent=E['PARENTS'][i] if i<28 else None;basis=bone.bone.matrix_local.inverted()
        if parent is not None and parent>=0:basis=basis@rig.pose.bones[names[parent]].bone.matrix_local@poses[parent].inverted()
        bone.matrix_basis=basis@poses[i]
    bpy.context.view_layer.update();evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh();coords=np.array([vertex.co[:] for vertex in mesh.vertices])@CI[:3,:3].T;evaluated.to_mesh_clear();error=float(np.linalg.norm(coords-p,axis=1).max())
    (O/'editable-rig-check.json').write_text(json.dumps({'maximumPositionErrorMeters':error,'passed':error<2e-5},indent=2));assert error<2e-5,error
    rig['corrections_profile']=(O/'v1-corrections.json').read_text();rig['source_capture']=str(F['CAP']);rig['purpose']='Editable source of renderer-only retargeting. Original animation bones remain unchanged in-game.'
    for w in d['weapons']:
        mesh=bpy.data.meshes.new(w['name']);points=np.array(w['positions'])@C[:3,:3].T;mesh.from_pydata(points.tolist(),[],np.array(w['triangles']).reshape(-1,3)[:,::-1].tolist());weapon=bpy.data.objects.new(w['name'],mesh);scene.collection.objects.link(weapon)
    text=bpy.data.texts.new('Grip authoring notes');text.write('V1 Plate Launcher compact grip\nReference: user grip photographs, recorded native Glider rig.\nThe .json correction profile is embedded on the rig. Mesh shape keys and pose bones are editable.\n'+json.dumps(json.loads((O/(name+'.json')).read_text()),indent=2))
    bpy.context.window.scene=scene;bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    # Save only this deliverable scene and dependencies, preserving other work
    # currently open in the Blender session.
    path=O/'v1-compact-grip.blend';bpy.data.libraries.write(str(path),{scene,text},fake_user=True,compress=True);print(str(path),flush=True)


def parity():
    v,b,ss,d,bones=F['load']();profile=json.loads((O/'v1-corrections.json').read_text());records=[json.loads(line) for line in (F['CAP']/'frames.jsonl').read_text().splitlines()];rows=[]
    for index in (0,100,285):
        data=json.loads((O/f'unity-{index}.json').read_text());native=np.array(data['bones']).reshape(-1,4,4);ch=records[index]['character'];handle=np.array(next(r for r in ch['renderers'] if r['path'].endswith('/Handle'))['localToWorld']).reshape(4,4);handle[:3,3]-=data['origin'];expected,_,_=E['evaluate'](v,b,native,profile,'v1','YAPlateLauncher',handle);error=float(np.linalg.norm(expected-np.array(data['positions']),axis=1).max());rows.append({'frame':index,'maximumPositionErrorMeters':error,'passed':error<2e-5})
    (O/'unity-parity.json').write_text(json.dumps(rows,indent=2));print(rows,flush=True);assert all(row['passed'] for row in rows)


def review():
    data=np.load(O/'motion-grip.npz');v,b,ss,d,bones=F['load']();actual=json.loads((O/'unity-0.json').read_text());np.savez(O/'unity-final.npz',points=np.array(actual['positions']));np.savez(O/'before-grip.npz',points=np.array(d['positions']))
    F['render']('unity-final',('game','palm','dorsal'));F['render']('before-grip',('game','palm'))

def native_checks():
    v,b,ss,d,bones=F['load']();profile=json.loads((O/'v1-corrections.json').read_text());old=json.loads((O/'original/v1-corrections.json').read_text());folder=R/'simulation/Reports/cycler-thumb-review/native-clips';catalog=json.loads((folder/'clips.json').read_text());frames=np.fromfile(folder/'clip-bones.bin','<f4').reshape(catalog['frames'],56,4,4);tri=np.concatenate(ss);fw=np.where(v['ids']>=4,v['w'],0).sum(1);tri=tri[np.all(fw[tri]>.8,axis=1)];area=lambda p:np.linalg.norm(np.cross(p[tri[:,1]]-p[tri[:,0]],p[tri[:,2]]-p[tri[:,0]]),axis=1)
    report={'frames':0,'newDegenerateFaces':0,'otherWeaponMaximumPositionDifference':0.,'minimumFiniteCoordinate':True,'poseActivationRange':[1.,0.], 'anchorNote':'Native bone-only catalog has no animated weapon surface matrices; live capture replay covers the contact anchor.'}
    for clip in catalog['clips']:
        weapon='YAPlateLauncher' if clip['label'].startswith('Plate') or clip['key'].startswith('weapon-plate-launcher-') else 'SMG' if clip['label'].startswith('SMG ') or clip['key'].startswith('weapon-smg-') else 'Lever Action Shotgun' if clip['label'].startswith('Shotgun ') or clip['key'].startswith('weapon-bounce-shotgun-') else None
        if weapon is None:continue
        for i in range(clip['offset'],clip['offset']+clip['count']):
            native=frames[i,:28].astype(float);p,_,weights=E['evaluate'](v,b,native,profile,'v1',weapon);before=E['evaluate'](v,b,native,old,'v1',weapon)[0];report['newDegenerateFaces']+=int(((area(before)>1e-10)&(area(p)<=1e-10)).sum());report['minimumFiniteCoordinate'] &= bool(np.isfinite(p).all());report['frames']+=1
            if weapon!='YAPlateLauncher':report['otherWeaponMaximumPositionDifference']=max(report['otherWeaponMaximumPositionDifference'],float(np.linalg.norm(p-before,axis=1).max()))
            else:report['poseActivationRange']=[min(report['poseActivationRange'][0],weights[-1]),max(report['poseActivationRange'][1],weights[-1])]
    report['otherWeaponChangeScope']='No grip correction applied on other weapons.'
    report['passed']=report['frames']==2993 and report['newDegenerateFaces']==0 and report['minimumFiniteCoordinate'] and report['otherWeaponMaximumPositionDifference']==0.;(O/'native-regression.json').write_text(json.dumps(report,indent=2));print(report,flush=True)
