"""Same-pose clay comparison from original assets and final Unity BakeMesh."""
import bpy,runpy,json,gzip
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];O=R/'authoring/hand-repair';images=R/'simulation/Reports/thumb-refinement'
for var,path in M['PATHS'].items():
 key='clip-367';stem=O/f'{var}-{key}-unity.png';data=json.loads(Path(str(stem)+'.mesh.json').read_text());bones=np.array(data['bones']).reshape(-1,4,4)
 weapons=json.loads(Path(str(stem)+'.weapon.json').read_text());_,v,b,ss=M['read'](M['chunks']((O/'original'/f'{var}.mikumesh').read_bytes())['arm-left'])
 profile=json.loads((O/'original'/f'{var}-corrections.json').read_text());pose=next(p for m in profile['meshes'] for p in m['poses'] if p['name']=='Shotgun thumb grip')
 p=v['p'].astype(float).copy();p[pose['vertices']]+=np.array([[d['x'],d['y'],d['z']] for d in pose['deltas']])
 mat,_=T['blend'](v,b,bones,var);before=np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3]
 after=(np.c_[data['positions'],np.ones(len(data['positions']))]@(bones[1]@b[1]).T)[:,:3]
 center=bones[25,:3,3]*.6+bones[24,:3,3]*.4
 for label,points in [('before',before),('after',after)]:
  T['render'](points,np.concatenate(ss),weapons,bones[1]@b[1],var+'-verified-'+label,center)
 tiles=[]
 for label in ('before','after'):
  image=bpy.data.images.load(str(images/(var+'-verified-'+label+'.png')),check_existing=False)
  pixels=np.empty(700*700*4,dtype=np.float32);image.pixels.foreach_get(pixels);tiles.append(pixels.reshape(700,700,4));bpy.data.images.remove(image)
 image=bpy.data.images.new(var+' before left - after right',width=1400,height=700)
 image.pixels.foreach_set(np.concatenate(tiles,axis=1).ravel());image.filepath_raw=str(O/(var+'-comparison.png'));image.file_format='PNG';image.save();bpy.data.images.remove(image)
 # One matched native reference, same skeleton pose, weapon and camera.
 if var=='v1':
  ref=json.loads((R/'references/glider-first-person.json').read_text())['renderers'][0]
  g=json.loads(gzip.decompress((R/'references'/ref['mesh']['geometryFile']).read_bytes()));p=np.array(g['positions']);idx=np.array(g['boneIndices']);w=np.array(g['boneWeights']);nb=np.array(ref['mesh']['bindposesUnity']);native=bones@nb
  points=sum(np.einsum('nij,nj->ni',native[idx[:,k]],np.c_[p,np.ones(len(p))])*w[:,k,None] for k in range(4))[:,:3]
  tri=np.concatenate([np.array(x['indices']).reshape(-1,3) for x in g['submeshes']])
  T['render'](points,tri,weapons,bones[1]@b[1],'native-verified-reference',center)
print('Saved three same-pose comparison sheets and native reference')
