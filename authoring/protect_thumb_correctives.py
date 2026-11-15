"""Offline triangle-area guard for inverse-baked thumb correctives; Blender MCP."""
import runpy,json,numpy as np
from pathlib import Path
R=Path(__file__).resolve().parents[1];T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py'));M=T['M'];BAKE=runpy.run_path(str(R/'authoring/bake_thumb_grip_review.py'))
def protect(v,b,ss,variant,pose,target,clips):
 delta=target-v['p'];tri=np.concatenate(ss);tri=tri[np.any(np.linalg.norm(delta[tri],axis=2)>1e-8,axis=1)];used=np.unique(tri);mapping=np.full(len(v),-1);mapping[used]=np.arange(len(used));tr=mapping[tri];base=[];matrices=[];weights=[]
 for clip in clips:
  for f in range(clip['offset'],clip['offset']+clip['count']):
   bones=BAKE['native'][f,:28].astype(float);a=BAKE['activation'](pose,bones)
   if a<1e-5:continue
   matrix,_=T['blend'](v,b,bones,variant);base.append(np.einsum('nij,nj->ni',matrix[used],np.c_[v['p'][used],np.ones(len(used))])[:,:3]);matrices.append(matrix[used,:3,:3]*a)
 base=np.array(base);matrices=np.array(matrices);area=lambda p:np.linalg.norm(np.cross(p[:,tr[:,1]]-p[:,tr[:,0]],p[:,tr[:,2]]-p[:,tr[:,0]]),axis=2);before=area(base);valid=before>1e-10
 groups={}
 for i,p in enumerate(v['p']):groups.setdefault(tuple(np.round(p,6)),[]).append(i)
 neighbors=[set() for _ in v]
 for face in np.concatenate(ss):
  for i in face:neighbors[i].update(face)
 factor=np.ones(len(v));history=[]
 for iteration in range(32):
  after=base+np.einsum('fnij,nj->fni',matrices,delta[used]*factor[used,None]);ratio=np.where(valid,area(after)/np.maximum(before,1e-30),1);minimum=ratio.min();history.append(float(minimum))
  bad=np.any(ratio<.12,axis=0)
  if not bad.any():break
  selected=np.unique(tri[bad]);factor[selected]*=.75
  near=set()
  for i in selected:near.update(neighbors[i])
  for i in near:
   if i not in selected:factor[i]*=.93
  for ids in groups.values():factor[ids]=factor[ids].min()
 assert minimum>=.12,(variant,pose['name'],history)
 return v['p']+delta*factor[:,None],{'iterations':iteration,'minimumAreaRatio':float(minimum),'minDisplacementScale':float(factor.min()),'attenuatedVertices':int((factor<1).sum()),'history':history}
