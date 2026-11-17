"""Blender MCP acceptance measurements for index registration and the new nail.

Uses frozen regions, native recorded matrices, exact weapon surface crossings,
and Unity BakeMesh parity. Broad contact counts are diagnostic, not a pass label.
"""
import runpy,json,hashlib,copy
from pathlib import Path
import numpy as np
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/review_plate_grip.py'));M=A['M'];T=A['T'];O=A['O'];S=A['S'];BA=A['BA']
C=runpy.run_path(str(R/'authoring/check_thumb_return.py'))
Q=runpy.run_path(str(R/'authoring/hand_quality.py'));CAP=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def load(var,old=False):
 folder=O/'original' if old else O;_,v,b,ss=M['read'](M['chunks']((folder/f'{var}.mikumesh').read_bytes())['arm-left']);return v,b,np.concatenate(ss),json.loads((folder/f'{var}-corrections.json').read_text())
def skin(v,b,bones,var,profile,weapon):
 if any(p.get('boneRotations') or p.get('group') for m in profile['meshes'] for p in m['poses']):
  return runpy.run_path(str(R/'authoring/pose_corrections.py'))['evaluate'](v,b,bones,profile,var,weapon)[0]
 poses=[q for m in profile['meshes'] for q in m['poses'] if q.get('weapon',m.get('weapon',''))==weapon];p=v['p'].astype(float).copy();weights=[BA['activation'](q,bones) for q in poses];total=max(1,sum(w for q,w in zip(poses,weights) if q['vertices']))
 for q,w in zip(poses,weights):
  if q['vertices']:p[q['vertices']]+=np.array([[d[k] for k in ('x','y','z')] for d in q['deltas']])*w/total
 if any(q.get('bones') for q in poses):raise ValueError('Rejected runtime bone offset')
 mat,_=T['blend'](v,b,bones,var);return np.einsum('nij,nj->ni',mat,np.c_[p,np.ones(len(p))])[:,:3]
def parity():
 rows=[]
 for var in ('v1','v3'):
  v,b,tri,profile=load(var)
  for i in (0,120,150):
   d=json.loads((S/f'{var}-final-{i}.json').read_text());p=skin(v,b,np.array(d['bones']).reshape(-1,4,4),var,profile,'YAPlateLauncher');error=float(np.linalg.norm(p-np.array(d['positions']),axis=1).max());rows.append(dict(variant=var,index=i,errorMeters=error,passed=error<2e-5,modelSha256=sha(O/f'{var}.mikumesh'),correctionsSha256=sha(O/f'{var}-corrections.json')))
 (O/'unity-parity.json').write_text(json.dumps(rows,indent=2));print('Parity',rows,flush=True)
def records(variants=('v1','v3'),stages=('original','candidate')):
 report=json.loads((O/'recorded-index-audit.json').read_text()) if (O/'recorded-index-audit.json').exists() else {};metadata=json.loads((O/'candidate-report.json').read_text())
 for var,capture in [('v1','20261116-162906-805-0aca39'),('v3','20261116-163111-497-b3c226')]:
  if var not in variants:continue
  frames=[json.loads(l) for l in (CAP/capture/'frames.jsonl').read_text().splitlines()];d=json.loads((S/f'{var}-before-0.json').read_text());local=[]
  for w in d['weapons']:
   name=w['name'].removeprefix('Native weapon ');rec=next((x for x in frames[0]['character']['renderers'] if x['role']=='weapon' and x['path'].split('/')[-1]==name),None)
   if rec is None:continue
   mat=np.array(rec['localToWorld']).reshape(4,4);mat[:3,3]-=d['origin'];local.append((name,w,(np.c_[w['positions'],np.ones(len(w['positions']))]@np.linalg.inv(mat).T)[:,:3]))
  report.setdefault(var,{})
  for old in (True,False):
   if ('original' if old else 'candidate') not in stages:continue
   v,b,tri,profile=load(var,old);weight=np.where((v['ids']>=4)&(v['ids']<=8),v['w'],0).sum(1);itr=tri[np.all(weight[tri]>.8,axis=1)];row=dict(frames=0,indexCrossingFrames=0,maximumIndexCrossingPairs=0,indexNailCrossingFrames=0,maximumCorrectionStepPer60Hz=0,maximumCorrectionAngleDegrees=0,maximumCorrectionJumpOverPalmWidth=0,rows=[]);previous=None
   nail=np.array(metadata[var]['indexNailVertices']);nt=tri[np.all(np.isin(tri,nail),axis=1)];noBone=copy.deepcopy(profile)
   for mesh in noBone['meshes']:
    for pose in mesh['poses']:pose['bones']=[]
   pose=next((q for m in profile['meshes'] for q in m['poses'] if q.get('bones')),None)
   for i,f in enumerate(frames):
    ch=f['character'];rec=next(x for x in ch['renderers'] if x['role']=='arm_left');bones=np.array(rec.get('sourceBoneMatrices') or rec['boneMatrices']).reshape(-1,4,4)[:28];origin=np.array(ch['camera']['position']);bones[:,:3,3]-=origin;p=skin(v,b,bones,var,profile,'YAPlateLauncher');weapons=[]
    for name,w,vertices in local:
     rec=next(x for x in ch['renderers'] if x['role']=='weapon' and x['path'].split('/')[-1]==name);mat=np.array(rec['localToWorld']).reshape(4,4);mat[:3,3]-=origin;weapons.append(dict(w,positions=(np.c_[vertices,np.ones(len(vertices))]@mat.T)[:,:3]))
    hit=C['contact'](p,itr,weapons)['interiorTrianglePairs'];nhit=C['contact'](p,nt,weapons)['interiorTrianglePairs'];activation=BA['activation'](pose,bones) if pose else 0;offset=p-skin(v,b,bones,var,noBone,'YAPlateLauncher');jump=0;step=0
    if previous is not None:
     dt=f['realtime']-previous[0]
     if 0<dt<.067:
      step=abs(activation-previous[1])/(dt*60);jump=float(np.linalg.norm(offset-previous[2],axis=1).max()/np.linalg.norm(bones[5,:3,3]-bones[20,:3,3])/(dt*60))
    previous=f['realtime'],activation,offset
    row['frames']+=1;row['indexCrossingFrames']+=int(hit>0);row['maximumIndexCrossingPairs']=max(row['maximumIndexCrossingPairs'],hit);row['indexNailCrossingFrames']+=int(nhit>0);row['maximumCorrectionStepPer60Hz']=max(row['maximumCorrectionStepPer60Hz'],float(step));row['maximumCorrectionAngleDegrees']=max(row['maximumCorrectionAngleDegrees'],float(activation*40));row['maximumCorrectionJumpOverPalmWidth']=max(row['maximumCorrectionJumpOverPalmWidth'],jump);row['rows'].append(dict(frame=i,crossings=hit,nailCrossings=nhit,activation=float(activation),jump=jump))
   folder=O/'original' if old else O;row.update(modelSha256=sha(folder/f'{var}.mikumesh'),correctionsSha256=sha(folder/f'{var}-corrections.json'),captureSha256=sha(CAP/capture/'frames.jsonl'));report[var]['original' if old else 'candidate']=row;print(var,'original' if old else 'candidate',{k:v for k,v in row.items() if k!='rows'},flush=True)
 (O/'recorded-index-audit.json').write_text(json.dumps(report,indent=2));return report
def registration_motion():
 folder=R/'simulation/Reports/cycler-thumb-review/native-clips';catalog=json.loads((folder/'clips.json').read_text());frames=np.fromfile(folder/'clip-bones.bin','<f4').reshape(catalog['frames'],56,4,4);rows=[]
 for var in ('v1','v3'):
  v,b,tri,profile=load(var);ov,ob,ot,op=load(var,True);weight=np.where((ov['ids']>=4)&(ov['ids']<=23),ov['w'],0).sum(1);tr=ot[np.all(weight[ot]>.8,axis=1)];row=dict(variant=var,frames=0,newDegenerateTriangleFrames=0,originalDegenerateTriangleFrames=0,candidateDegenerateTriangleFrames=0,minimumAreaRatioToRest=1.,modelSha256=sha(O/f'{var}.mikumesh'),correctionsSha256=sha(O/f'{var}-corrections.json'))
  area=lambda p:np.linalg.norm(np.cross(p[tr[:,1]]-p[tr[:,0]],p[tr[:,2]]-p[tr[:,0]]),axis=1)
  rest=area(v['p']);valid=rest>1e-9
  for clip in catalog['clips']:
   weapon='YAPlateLauncher' if clip['label'].startswith('Plate') or clip['key'].startswith('weapon-plate-launcher-') else 'SMG' if clip['label'].startswith('SMG ') or clip['key'].startswith('weapon-smg-') else 'Lever Action Shotgun' if clip['label'].startswith('Shotgun ') or clip['key'].startswith('weapon-bounce-shotgun-') else None
   if weapon is None:continue
   for i in range(clip['offset'],clip['offset']+clip['count']):
    bones=frames[i,:28].astype(float);base=skin(ov,ob,bones,var,op,weapon);p=skin(v,b,bones,var,profile,weapon);a=area(base);z=area(p);scale=float(np.linalg.norm((bones[7]@b[7])[:3,:3],axis=0).mean())
    row['newDegenerateTriangleFrames']+=int(np.sum((a>1e-10)&(z<=1e-10)));row['originalDegenerateTriangleFrames']+=int(np.sum(a<=1e-10));row['candidateDegenerateTriangleFrames']+=int(np.sum(z<=1e-10));row['minimumAreaRatioToRest']=min(row['minimumAreaRatioToRest'],float((z[valid]/(rest[valid]*scale*scale)).min()));row['frames']+=1
  row['passedNoNewDegenerates']=row['frames']==2993 and row['newDegenerateTriangleFrames']==0;rows.append(row);print('Registration',row,flush=True)
 (O/'registration-motion.json').write_text(json.dumps(rows,indent=2))
def handle_depth():
 from mathutils import Vector
 rows=[]
 for var in ('v1','v3'):
  d=json.loads((S/f'{var}-before-0.json').read_text());bones=np.array(d['bones']).reshape(-1,4,4);w=next(w for w in d['weapons'] if w['name']=='Native weapon Handle');wp=np.array(w['positions']);wt=np.array(w['triangles']).reshape(-1,3);tree=BVHTree.FromPolygons(wp.tolist(),wt.tolist(),all_triangles=True)
  for stage in ('original','candidate','Glider'):
   if stage=='Glider':
    p=A['glider'](bones);v={'ids':np.array(A['G']['boneIndices']),'w':np.array(A['G']['boneWeights'])}
   else:
    v,b,tr,profile=load(var,stage=='original');p=skin(v,b,bones,var,profile,'YAPlateLauncher')
   mask=np.where((v['ids']>=4)&(v['ids']<=8),v['w'],0).sum(1)>.8;inside=[];details=[];direction=Vector((.371,.619,.692)).normalized()
   for i in np.flatnonzero(mask):
    origin=Vector(p[i]);hits=0
    for _ in range(20):
     loc,_,_,_=tree.ray_cast(origin,direction,1)
     if loc is None:break
     hits+=1;origin=loc+direction*1e-6
    if hits%2:
     loc,normal,face,distance=tree.find_nearest(Vector(p[i]));inside.append(distance);details.append(dict(vertex=int(i),distance=distance,bones=v['ids'][i].tolist(),weights=v['w'][i].tolist(),point=p[i].tolist(),surface=list(loc)))
   rows.append(dict(variant=var,stage=stage,indexVerticesInsideHandle=len(inside),maximumPenetrationMeters=max(inside,default=0),deepest=sorted(details,key=lambda x:-x['distance'])[:5]))
 (O/'handle-depth.json').write_text(json.dumps(rows,indent=2));print(rows,flush=True)
if __name__=='__main__':parity();records();registration_motion();handle_depth()
