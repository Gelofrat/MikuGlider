"""Same recorded camera/weapon comparison, executed through Blender MCP."""
import bpy, runpy, json, gzip
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];O=R/'authoring/plate-grip';S=R/'scratch/plate-grip'
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];BA=runpy.run_path(str(R/'authoring/bake_thumb_grip_review.py'))
V=runpy.run_path(str(R/'authoring/review_thumb_return.py'));V['render'].__globals__['O']=S
REF=json.loads((R/'references/glider-first-person.json').read_text())['renderers'][0]
G=json.loads(gzip.decompress((R/'references'/REF['mesh']['geometryFile']).read_bytes()))

def sample(var,index=0):
 d=json.loads((S/f'{var}-before-{index}.json').read_text());cam=np.array(d['camera']['firstPersonPass']['worldToCamera']).reshape(4,4);cam[:3,3]+=cam[:3,:3]@np.array(d['origin'])
 bones=cam@np.array(d['bones']).reshape(-1,4,4)
 weapons=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@cam.T)[:,:3]) for w in d['weapons']]
 return d,cam,bones,weapons

def points(v,b,bones,var,profile):
 p=v['p'].astype(float).copy()
 poses=[p for m in profile['meshes'] for p in m['poses'] if p.get('weapon')=='YAPlateLauncher']
 for pose in poses:
  if not pose['vertices']:continue
  p[pose['vertices']]+=np.array([[d[k] for k in ('x','y','z')] for d in pose['deltas']])*BA['activation'](pose,bones)
 if any(p.get('bones') for p in poses):raise ValueError('Rejected runtime bone offset')
 mat,_=T['blend'](v,b,bones,var)
 return np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3]

def glider(bones):
 p=np.array(G['positions']);idx=np.array(G['boneIndices']);w=np.array(G['boneWeights']);mat=bones@np.array(REF['mesh']['bindposesUnity'])
 return sum(np.einsum('nij,nj->ni',mat[idx[:,k]],np.c_[p,np.ones(len(p))])*w[:,k,None] for k in range(4))[:,:3]

def review(stage='before',indices=(0,),variants=('v1','v3')):
 for var in variants:
  raw=(O/'original'/f'{var}.mikumesh' if stage=='before' else O/f'{var}.mikumesh').read_bytes();_,v,b,ss=M['read'](M['chunks'](raw)['arm-left']);tri=np.concatenate(ss)
  profile=json.loads((O/('original/' if stage=='before' else '')/f'{var}-corrections.json').read_text())
  for index in indices:
   d,cam,bones,weapons=sample(var,index);p=points(v,b,np.array(d['bones']).reshape(-1,4,4),var,profile);p=(np.c_[p,np.ones(len(p))]@cam.T)[:,:3]
   if stage=='before':print(var,index,'CPU/Unity error',float(np.linalg.norm(p-(np.c_[d['positions'],np.ones(len(d['positions']))]@cam.T)[:,:3],axis=1).max()),flush=True)
   center=bones[24,:3,3]*.45+bones[25,:3,3]*.55
   tex=R/('assets/V1/textures/body-albedo.png' if var=='v1' else 'assets/v3/textures/body-albedo.png')
   for view,eye in [('game',np.zeros(3)),('side',center+np.array([.12,.02,0]))]:
    V['render'](f'{var}-{stage}-{index}-{view}',p,tri,v['uv'],tex if tex.exists() else None,center,eye,weapons,scale=.070)
    if stage=='before':V['render'](f'{var}-glider-{index}-{view}',glider(bones),np.concatenate([np.array(x['indices']).reshape(-1,3) for x in G['submeshes']]),G['uv0'],None,center,eye,weapons,scale=.070)

if __name__=='__main__':review()
