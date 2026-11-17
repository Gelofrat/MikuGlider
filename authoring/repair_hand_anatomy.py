"""Blender MCP authoring. Creates candidates before explicit export()."""
import bpy, json, runpy, hashlib, gzip
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1]
T=runpy.run_path(str(R/'authoring/thumb_grip_tools.py')); M=T['M']; B=T['B']
O=R/'authoring/hand-repair'; O.mkdir(exist_ok=True)
reference=json.loads((R/'references/glider-first-person.json').read_text())['renderers']

def repair(v,b,ss):
 c=v.copy(); p=v['p'].astype(float); joints=np.linalg.inv(b)[24:28,:3,3]
 lengths=np.linalg.norm(np.diff(joints,axis=0),axis=1); arcs=np.r_[0,np.cumsum(lengths)]
 total=np.where((v['ids']>=24)&(v['ids']<=27),v['w'],0).sum(1)
 # Anatomical support is local to the nearest thumb segment, with shorter
 # transition zones at IP than MCP. Preserve the existing palm partition.
 distances=[]; coords=[]; centers=[]
 for k in range(3):
  axis=(joints[k+1]-joints[k])/lengths[k]; along=(p-joints[k])@axis
  center=joints[k]+np.clip(along,0,lengths[k])[:,None]*axis
  distances.append(np.linalg.norm(p-center,axis=1)); coords.append(arcs[k]+along)
  centers.append(joints[k]+along[:,None]*axis)
 near=np.argmin(distances,axis=0); ar=np.array(coords)[near,np.arange(len(p))]
 center=np.array(centers)[near,np.arange(len(p))]
 smooth=lambda x: np.clip(x,0,1)**2*(3-2*np.clip(x,0,1))
 s=smooth((ar-arcs[1]+.006)/.012); t=smooth((ar-arcs[2]+.004)/.008)
 for i in np.flatnonzero(total>1e-6):
  values={}
  for bone,w in zip(v['ids'][i],v['w'][i]):
   if bone<24 and w>0:values[int(bone)]=values.get(int(bone),0)+float(w)
  for bone,w in zip((24,25,26),(1-s[i],s[i]*(1-t[i]),t[i])):
   if w>1e-7:values[bone]=float(total[i]*w)
  pairs=sorted(values.items(),key=lambda x:-x[1])[:4]; norm=sum(w for _,w in pairs)
  c['w'][i]=0;c['ids'][i]=0
  for slot,(bone,w) in enumerate(pairs):c['ids'][i,slot]=bone;c['w'][i,slot]=w/norm
 # Small radial adjustment retains joint centers and nail length, adding
 # support to the narrow proximal shaft. It is not an anatomical standard.
 radial=p-center
 strength=.10*smooth((total-.5)/.5)*smooth((ar-arcs[1]+.012)/.02)*(1-smooth((ar-arcs[2])/.018))
 c['p']=p+radial*strength[:,None]
 # Weld UV copies for local smoothing and weight continuity.
 groups={}
 for i,q in enumerate(p):groups.setdefault(tuple(np.round(q,6)),[]).append(i)
 groups=list(groups.values()); mapping=np.zeros(len(v),int)
 for j,g in enumerate(groups):mapping[g]=j
 pos=np.array([c['p'][g].mean(0) for g in groups]); neighbors=[set() for g in groups]
 for f in np.concatenate(ss):
  for a,z in ((0,1),(1,2),(2,0)):
   x,y=mapping[f[a]],mapping[f[z]]
   if x!=y:neighbors[x].add(y);neighbors[y].add(x)
 weight=np.array([total[g].mean() for g in groups]); arc=np.array([ar[g].mean() for g in groups])
 mask=(weight>.03)&(weight<.95)&(arc<arcs[1]+.008)
 # Paired positive/negative smoothing reduces web spikes with less shrinkage.
 for step in (.25,-.26,.25,-.26):
  new=pos.copy()
  for j in np.flatnonzero(mask):
   if neighbors[j]:new[j]+=step*(pos[list(neighbors[j])].mean(0)-pos[j])
  pos=new
 c['p']=pos[mapping]
 for g in groups:
  if total[g].max()<=1e-6:continue
  values={}
  for i in g:
   for bone,w in zip(c['ids'][i],c['w'][i]):values[int(bone)]=values.get(int(bone),0)+float(w)/len(g)
  pairs=sorted(values.items(),key=lambda x:-x[1])[:4]; norm=sum(w for _,w in pairs)
  for i in g:
   c['w'][i]=0;c['ids'][i]=0
   for slot,(bone,w) in enumerate(pairs):c['ids'][i,slot]=bone;c['w'][i,slot]=w/norm
 A=runpy.run_path(str(R/'authoring/refine_thumb_grips.py'))
 A['normals'](c,v['p'],np.concatenate(ss),np.linalg.norm(c['p']-v['p'],axis=1)>1e-8)
 return c

def candidates():
 report={}
 for var,path in M['PATHS'].items():
  raw=(R/path).read_bytes();parts=M['chunks'](raw);report[var]={'inputSha256':hashlib.sha256(raw).hexdigest(),'sides':{}}
  for si,side in enumerate(('left','right')):
   name='arm-'+side;_,v,b,ss=M['read'](parts[name]);c=repair(v,b,ss)
   parts[name]=M['write'](name,c,b,ss)
   report[var]['sides'][side]={'movedVertices':int((np.linalg.norm(c['p']-v['p'],axis=1)>1e-8).sum()),'maximumRestDisplacement':float(np.linalg.norm(c['p']-v['p'],axis=1).max()),'changedWeights':int(np.any(c['w']!=v['w'],axis=1).sum())}
   (O/f'{var}-arm-{side}-candidate.json').write_text(json.dumps(dict(positions=c['p'].tolist(),normals=c['n'].tolist(),indices=c['ids'].tolist(),weights=c['w'].tolist())))
   if side!='left':continue
   stem=R/f'simulation/Reports/thumb-refinement/{var}-clip-367-final.png'
   bones=np.array(json.loads(Path(str(stem)+'.mesh.json').read_text())['bones']).reshape(-1,4,4)
   weapons=json.loads(Path(str(stem)+'.weapon.json').read_text());mat,_=T['blend'](c,b,bones,var)
   points=np.einsum('nij,nj->ni',mat,np.c_[c['p'],np.ones(len(c))])[:,:3]
   T['render'](points,np.concatenate(ss),weapons,bones[1]@b[1],var+'-repair-base',bones[25,:3,3]*.6+bones[24,:3,3]*.4)
  (O/(var+'.mikumesh')).write_bytes(M['archive'](parts))
 (O/'candidate-report.json').write_text(json.dumps(report,indent=2))
 # Native reference uses its original bindposes and skinning, no wrist helpers.
 g=json.loads(gzip.decompress((R/'references'/reference[0]['mesh']['geometryFile']).read_bytes()))
 p=np.array(g['positions']);idx=np.array(g['boneIndices']);w=np.array(g['boneWeights']);bind=np.array(reference[0]['mesh']['bindposesUnity']);native=bones@bind
 points=sum(np.einsum('nij,nj->ni',native[idx[:,k]],np.c_[p,np.ones(len(p))])*w[:,k,None] for k in range(4))[:,:3]
 tri=np.concatenate([np.array(x['indices']).reshape(-1,3) for x in g['submeshes']])
 T['render'](points,tri,weapons,bones[1]@bind[1],'glider-native-reference',bones[25,:3,3]*.6+bones[24,:3,3]*.4)
 print(json.dumps(report))

if __name__=='__main__':candidates()
