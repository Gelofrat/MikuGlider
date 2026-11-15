"""Replay the reviewed 0.12.28 Blender thumb edits, pinned to 0.12.27 inputs.

Run through Blender MCP. The payload contains reviewed base vertices/normals
and the final corrective targets, including the v2 welded delta relaxation.
Rig, skin weights, topology, UVs, right arms and other model chunks are retained.
"""
import bpy,runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1]
B=runpy.run_path(str(R/'authoring/build_first_person_hand_review.py'));M=B['model']

def apply():
 manifest=json.loads((R/'authoring/thumb-grip-refinement.json').read_text())
 payload=np.load(R/'authoring/thumb-grip-refinement.npz')
 poses={w:json.loads((R/f'authoring/thumb-grip-{w}.json').read_text()) for w in ['cycler','shotgun']}
 prepared={}
 for var,path in M['PATHS'].items():
  raw=(R/path).read_bytes();assert hashlib.sha256(raw).hexdigest()==manifest['inputs'][var],f'{var}: changed input or already applied'
  parts=M['chunks'](raw);_,before,b,sections=M['read'](parts['arm-left']);after=payload[var+'-base'];assert before.shape==after.shape and before.dtype==after.dtype
  for key in ['w','ids','uv']:assert np.array_equal(before[key],after[key]),key
  assert np.isfinite(after['p']).all() and np.isfinite(after['n']).all()
  parts['arm-left']=M['write']('arm-left',after,b,sections);result=M['archive'](parts);assert hashlib.sha256(result).hexdigest()==manifest['models'][var]['sha256']
  profilepath=(R/path).parent/'corrections.json';profile=json.loads(profilepath.read_text());mesh=next(x for x in profile['meshes'] if x['key']=='arm-left wrist transition');mesh['fingerprint']=manifest['models'][var]['fingerprint'];mesh['poses']=[poses[w]['models'][var]['pose'] for w in ['cycler','shotgun']]
  prepared[var]=(path,result,profilepath,profile,after)
 for var,(path,result,profilepath,profile,after) in prepared.items():
  (R/path).write_bytes(result);profilepath.write_text(json.dumps(profile,indent=2)+'\n')
  for suffix in ['EXPORT','PREVIEW']:
   obj=bpy.data.objects[f'{var} arm-left [{suffix}]'];coords=(after['p']@B['C'][:3,:3].T).ravel();obj.data.vertices.foreach_set('co',coords)
   if obj.data.shape_keys is None:obj.shape_key_add(name='Basis')
   obj.data.shape_keys.key_blocks[0].data.foreach_set('co',coords)
   for weapon,shape in [('cycler','Cycler thumb grip'),('shotgun','Shotgun thumb grip')]:
    key=obj.data.shape_keys.key_blocks.get(shape) or obj.shape_key_add(name=shape);key.data.foreach_set('co',(payload[var+'-'+weapon]@B['C'][:3,:3].T).ravel())
   obj.data.normals_split_custom_set_from_vertices((after['n']@B['C'][:3,:3].T).tolist());obj.data.update()
 bpy.ops.wm.save_as_mainfile(filepath=str(R/'authoring/first-person-hands.blend'),compress=True)
 print('Applied reviewed 0.12.28 thumb repair; rebuild dense animation bakes with bake_thumb_grip_review.py.')
if __name__=='__main__':apply()
