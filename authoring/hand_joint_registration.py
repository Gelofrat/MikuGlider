"""Blender MCP candidates; never overwrites the active game assets."""
import bpy,runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/review_plate_grip.py'));M=A['M'];T=A['T'];O=A['O'];S=A['S']
def smooth(x):
 x=np.clip(x,0,1);return x*x*(3-2*x)
def pack(c,i,values):
 pairs=sorted(((k,w) for k,w in values.items() if w>1e-8),key=lambda x:-x[1])[:4];total=sum(w for _,w in pairs);c['ids'][i]=0;c['w'][i]=0
 for k,(bone,w) in enumerate(pairs):c['ids'][i,k]=bone;c['w'][i,k]=w/total

def fingers(v,b,starts=(4,9,14,19)):
 c=v.copy();j=np.linalg.inv(b)[:,:3,3]
 for i in range(len(v)):
  values={}
  for bone,w in zip(v['ids'][i],v['w'][i]):values[int(bone)]=values.get(int(bone),0)+float(w)
  for start in starts:
   total=sum(values.pop(k,0) for k in range(start,start+5))
   if total<1e-8:continue
   chain=j[start:start+5];length=np.linalg.norm(np.diff(chain,axis=0),axis=1);arc=np.r_[0,np.cumsum(length)];candidates=[]
   for k in range(4):
    axis=(chain[k+1]-chain[k])/length[k];along=(v['p'][i]-chain[k])@axis;q=chain[k]+np.clip(along,0,length[k])*axis
    candidates.append((float(np.linalg.norm(v['p'][i]-q)),arc[k]+along))
   at=min(candidates)[1];s=[smooth((at-arc[k]+width)/(2*width)) for k,width in [(1,.008),(2,.006),(3,.004)]]
   weights=[1-s[0],s[0]*(1-s[1]),s[1]*(1-s[2]),s[2]]
   for k,w in enumerate(weights):values[start+k]=total*float(w)
  pack(c,i,values)
 return c

def center_fingers(c,b,ss,starts=(4,9,14,19),target_native=True):
 # Place the tissue axis through its joints; retain the measured radial shape.
 # Constant distal translation preserves each nail instead of stretching it.
 sec=runpy.run_path(str(R/'authoring/repair_thumb_distal.py'))['section_center']
 j=np.linalg.inv(b)[:,:3,3];tri=np.concatenate(ss);p=c['p'].astype(float).copy();delta=np.zeros_like(p)
 g=A['G'];gp=np.array(g['positions']);gi=np.array(g['boneIndices']);gw=np.array(g['boneWeights']);gt=np.concatenate([np.array(t['indices']).reshape(-1,3) for t in g['submeshes']])
 for start in starts:
  total=np.where((c['ids']>=start)&(c['ids']<start+5),c['w'],0).sum(1);tr=tri[np.all(total[tri]>.5,axis=1)]
  axis=j[start+4]-j[start+1];axis/=np.linalg.norm(axis);at=(p-j[start+1])@axis
  end=(j[start+3]-j[start+1])@axis
  nativeTotal=np.where((gi>=start)&(gi<start+5),gw,0).sum(1);nativeTri=gt[np.all(nativeTotal[gt]>.5,axis=1)]
  samples=np.r_[0,np.linspace(.005,end,7),end+.006];offsets=[]
  for x in samples:
   try:offsets.append(sec(p,tr,j[start+1],axis,x)-(sec(gp,nativeTri,j[start+1],axis,x) if target_native else j[start+1]+axis*x))
   except ValueError:offsets.append(offsets[-1] if offsets else np.zeros(3))
  samples=np.r_[-.040,samples];offsets=np.vstack([np.zeros(3),offsets]);move=np.column_stack([np.interp(at,samples,offsets[:,k]) for k in range(3)])
  delta-=move*smooth(total)[:,None]
 c['p']=p+delta
 return c

def normals(v,before,tri,changed):
 n=np.zeros((len(v),3));cross=np.cross(v['p'][tri[:,2]]-v['p'][tri[:,0]],v['p'][tri[:,1]]-v['p'][tri[:,0]])
 for k in range(3):np.add.at(n,tri[:,k],cross)
 groups={}
 for i,p in enumerate(before):groups.setdefault(tuple(np.round(p,5)),[]).append(i)
 for ids in groups.values():
  if changed[ids].any():
   normal=n[ids].sum(0);length=np.linalg.norm(normal)
   if length>1e-10:v['n'][ids]=normal/length
