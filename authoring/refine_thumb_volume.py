import bpy,runpy,json,hashlib,subprocess,numpy as np
from pathlib import Path
R=Path(__file__).resolve().parents[1];D=runpy.run_path(str(R/'authoring/thumb_volume_target.py'));T=D['T'];M=T['M'];B=T['B'];A=runpy.run_path(str(R/'authoring/refine_thumb_grips.py'))
EXPECTED={'v1':'3f0bc185582cdd5131174aafabf63ffccfbf3d1b9de3c66a966471a170d9504f','v2':'08501d17223842a327211cf57aeb73dbf62bddff375bcc8d8f8c41e0d99fc359','v3':'cdd704cfc43af184bca21684938c30bad6d8ea03d34047616081962789e3fb37'}
WEAPONS=[('cycler','clip-381','Cycler thumb grip',(0,15,-15),.85,0,'SMG'),('shotgun','clip-367','Shotgun thumb grip',(0,10,15),.85,0,'Lever Action Shotgun'),('plate','clip-313','Plate thumb opposition',(0,0,0),.95,-50,'YAPlateLauncher')]
def apply():
 assert Path(bpy.data.filepath).resolve()==(R/'authoring/first-person-hands.blend').resolve()
 for var,path in M['PATHS'].items():assert hashlib.sha256((R/path).read_bytes()).hexdigest()==EXPECTED[var],var
 report={'version':'0.12.29','checkpoint':'8572dd6','inputs':EXPECTED,'sourceThumbCommit':'fe7e510','method':'Remove stacked base thumb edits, restore pre-opposition thumb geometry/weights; author volume-preserving weapon targets in Blender and inverse-bake to existing linear skinning. Plate opposition rotates around the palm long axis. No runtime DQ solver.','models':{}}
 manifests={w[0]:{'version':'0.12.29','models':{}} for w in WEAPONS};payload={}
 for var,path in M['PATHS'].items():
  raw=(R/path).read_bytes();parts=M['chunks'](raw);_,v,b,ss=M['read'](parts['arm-left']);c=v.copy();_,original,_,_=M['read'](M['chunks'](subprocess.check_output(['git','show','fe7e510:'+path],cwd=R))['arm-left']);total=np.where(v['ids']>=24,v['w'],0).sum(1);mask=total>.05
  for key in ['p','w','ids']:c[key][mask]=original[key][mask]
  changed=np.linalg.norm(c['p']-v['p'],axis=1)>1e-8;A['normals'](c,v['p'],np.concatenate(ss),changed)
  parts['arm-left']=M['write']('arm-left',c,b,ss);newraw=M['archive'](parts);newhash=hashlib.sha256(newraw).hexdigest();payload[var+'-base']=c
  profilepath=(R/path).parent/'corrections.json';profile=json.loads(profilepath.read_text());mesh=next(x for x in profile['meshes'] if x['key']=='arm-left wrist transition')
  for weapon,key,name,angles,scale,roll,scope in WEAPONS:
   if weapon=='plate' and var!='v1':continue
   stem=R/f'simulation/Reports/thumb-refinement/{var}-{key}-final.png';bones=np.array(json.loads(Path(str(stem)+'.mesh.json').read_text())['bones']).reshape(-1,4,4)
   target,_=D['target'](c,b,ss,bones,var,angles,scale,roll)
   if var in ('v2','v3'):target=D['relax'](c,ss,target)
   delta=target-c['p'];selected=np.flatnonzero(np.linalg.norm(delta,axis=1)>1e-7);rotations=json.loads(Path(str(stem)+'.pose.json').read_text())['rotations']
   pose=next((p for p in mesh['poses'] if p['name']==name),None)
   if pose is None:pose={'name':name};mesh['poses'].append(pose)
   pose.update(weapon=scope,sourcePose=f'Blender Preserve Volume 0.12.29; CMC/MCP/IP {angles}; axial scale {scale}; palm opposition {roll}',drivers=[dict(rotations[i],innerRadius=5,radius=60) for i in [24,25,26]],vertices=selected.tolist(),deltas=[dict(zip(('x','y','z'),map(float,x))) for x in delta[selected]])
   if var in ('v2','v3'):
    guard=runpy.run_path(str(R/'authoring/protect_thumb_correctives.py'));prefix='SMG ' if weapon=='cycler' else 'Shotgun ';keyprefix='weapon-smg-' if weapon=='cycler' else 'weapon-bounce-shotgun-'
    clips=[clip for clip in guard['BAKE']['catalog']['clips'] if clip['label'].startswith(prefix) or clip['key'].startswith(keyprefix)]
    target,_=guard['protect'](c,b,ss,var,pose,target,clips);delta=target-c['p'];selected=np.flatnonzero(np.linalg.norm(delta,axis=1)>1e-7)
    pose['vertices']=selected.tolist();pose['deltas']=[dict(zip(('x','y','z'),map(float,x))) for x in delta[selected]];pose['sourcePose']+='; welded web relaxation and offline triangle-area guard'
   manifests[weapon]['models'][var]={'sha256':newhash,'pose':pose};payload[var+'-'+weapon]=target.astype('<f4');payload[var+'-'+weapon+'-bones']=bones.astype('<f4')
  for suffix in ['EXPORT','PREVIEW']:
   obj=bpy.data.objects[f'{var} arm-left [{suffix}]'];points=(c['p']@B['C'][:3,:3].T).ravel();obj.data.vertices.foreach_set('co',points)
   if not obj.data.shape_keys:obj.shape_key_add(name='Basis')
   obj.data.shape_keys.key_blocks[0].data.foreach_set('co',points)
   for w,_,name,_,_,_,_ in WEAPONS:
    if var+'-'+w not in payload:continue
    key=obj.data.shape_keys.key_blocks.get(name) or obj.shape_key_add(name=name);key.data.foreach_set('co',(payload[var+'-'+w]@B['C'][:3,:3].T).ravel())
   # Reproduce the native weights (export) or the existing wrist helper split
   # (preview). No extra bones or per-vertex influence slots are introduced.
   for group in obj.vertex_groups:group.remove(list(range(len(c))))
   helpers=suffix=='PREVIEW'
   if helpers:_,low,fraction=B['wrist'](c,b,np.linalg.inv(b),.24 if var=='v3' else 0)
   for i in range(len(c)):
    values={};core=0.
    for bone,weight in zip(c['ids'][i],c['w'][i]):
     if helpers and bone in (1,2,3):core+=float(weight)
     elif weight>0:values[int(bone)]=values.get(int(bone),0)+float(weight)
    if helpers:values[28+int(low[i])]=core*(1-float(fraction[i]));values[29+int(low[i])]=core*float(fraction[i])
    for bone,weight in values.items():
     if weight>0:obj.vertex_groups[bone].add([i],weight,'REPLACE')
   obj.data.normals_split_custom_set_from_vertices((c['n']@B['C'][:3,:3].T).tolist());obj.data.update()
  (R/path).write_bytes(newraw);profilepath.write_text(json.dumps(profile,indent=2)+'\n');report['models'][var]={'sha256':newhash,'changedBaseVertices':int(changed.sum()),'changedWeightVertices':int(np.any(c['w']!=v['w'],axis=1).sum()),'thumbPartitionRestored':int(mask.sum()),'rightArmAndOtherChunksUnchanged':all(parts[k]==M['chunks'](raw)[k] for k in parts if k!='arm-left'),'vertexCount':len(c),'nativeBones':len(b),'maxInfluences':int((c['w']>0).sum(1).max())}
 for weapon,data in manifests.items():(R/f'authoring/thumb-volume-{weapon}.json').write_text(json.dumps(data,indent=2)+'\n')
 np.savez_compressed(R/'authoring/thumb-volume-grip.npz',**payload);(R/'authoring/thumb-volume-grip.json').write_text(json.dumps(report,indent=2)+'\n')
 note=bpy.data.texts.get('Thumb refinement - 0.12.29') or bpy.data.texts.new('Thumb refinement - 0.12.29');note.clear();note.write(report['method']+'\n00_EXPORT and PREVIEW meshes match runtime. Older comparison scenes are historical. Current dense animations: thumb-grip-baked.blend.')
 bpy.ops.wm.save_as_mainfile(filepath=str(R/'authoring/first-person-hands.blend'),compress=True);print(json.dumps(report))
if __name__=='__main__':apply()
