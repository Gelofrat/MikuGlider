"""Blender MCP: v3 new nail attachment and shotgun regression on 705 frames."""
import runpy,json
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/check_plate_release.py'));M=A['M'];O=A['O'];S=A['S'];CAP=A['CAP'];C=A['C']
def audit():
 v,b,tri,profile=A['load']('v3');new=np.array(json.loads((O/'candidate-report.json').read_text())['v3']['nail']['vertices']);nailtri=tri[np.all(np.isin(tri,new),axis=1)]
 # Keep the pre-0.12.30 anatomical IDs used by the previous regression suite.
 _,rv,rb,rs=M['read'](M['chunks']((R/'authoring/thumb-return/original/v3.mikumesh').read_bytes())['arm-left']);j=np.linalg.inv(rb)[:,:3,3];axis=j[27]-j[26];axis/=np.linalg.norm(axis);mask=np.r_[(np.where(rv['ids']>=24,rv['w'],0).sum(1)>.8)&(((rv['p']-j[26])@axis)>.004),np.ones(len(new),dtype=bool)];tiptri=tri[np.all(mask[tri],axis=1)]
 thumb=(np.where(v['ids']>=24,v['w'],0).sum(1)>.8)&~np.isin(np.arange(len(v)),new);skintri=tri[np.all(thumb[tri],axis=1)]
 pair=np.array([(a,z) for k,a in enumerate(new) for z in new[k+1:]]);length=np.linalg.norm(v['p'][pair[:,1]]-v['p'][pair[:,0]],axis=1)
 reference=json.loads((R/'scratch/thumb-return/v3-before-0.json').read_text());frame=json.loads((CAP/'20261116-133221-136-93ae4f/frames.jsonl').open().readline());root=np.array(frame['character']['weaponMatrix']).reshape(4,4)
 local=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@np.linalg.inv(root).T)[:,:3]) for w in reference['weapons']]
 row=dict(frames=0,tipAndNailTriangles=len(tiptri),nailVertices=len(new),weaponCrossingFrames=0,maximumNailDistanceErrorRatio=0,minimumNailSkinDistance=1.,maximumNailSkinDistance=0,nailSkinCrossingFrames=0,modelSha256=A['sha'](O/'v3.mikumesh'),correctionsSha256=A['sha'](O/'v3-corrections.json'),captures=[])
 for capture in ('20261116-133145-152-7db26b','20261116-133221-136-93ae4f'):
  path=CAP/capture/'frames.jsonl';row['captures'].append(dict(path=str(path),sha256=A['sha'](path)))
  for line in path.read_text().splitlines():
   ch=json.loads(line)['character'];rec=next(x for x in ch['renderers'] if x['role']=='arm_left');bones=np.array(rec.get('sourceBoneMatrices') or rec['boneMatrices']).reshape(-1,4,4)[:28];origin=np.array(ch['camera']['position']);bones[:,:3,3]-=origin;p=A['skin'](v,b,bones,'v3',profile,'Lever Action Shotgun');scale=float(np.linalg.norm((bones[26]@b[26])[:3,:3],axis=0).mean());world=np.array(ch['weaponMatrix']).reshape(4,4);world[:3,3]-=origin;weapons=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@world.T)[:,:3]) for w in local]
   error=float(np.max(abs(np.linalg.norm(p[pair[:,1]]-p[pair[:,0]],axis=1)/(length*scale)-1)));hit=C['contact'](p,tiptri,weapons)['interiorTrianglePairs'];body=[dict(name='thumb skin',positions=p,triangles=skintri.ravel())];skinHit=C['contact'](p,nailtri,body)['interiorTrianglePairs'];tree=BVHTree.FromPolygons(p.tolist(),skintri.tolist(),all_triangles=True);dist=[tree.find_nearest(Vector(p[i]))[3] for i in new]
   row['frames']+=1;row['weaponCrossingFrames']+=int(hit>0);row['nailSkinCrossingFrames']+=int(skinHit>0);row['maximumNailDistanceErrorRatio']=max(row['maximumNailDistanceErrorRatio'],error);row['minimumNailSkinDistance']=min(row['minimumNailSkinDistance'],min(dist));row['maximumNailSkinDistance']=max(row['maximumNailSkinDistance'],max(dist))
 row['passed']=row['frames']==705 and row['weaponCrossingFrames']==0 and row['maximumNailDistanceErrorRatio']<.001 and row['nailSkinCrossingFrames']==0 and .00001<row['minimumNailSkinDistance'] and row['maximumNailSkinDistance']<.0002
 (O/'shotgun-nail-audit.json').write_text(json.dumps(row,indent=2));print(row,flush=True)
def dorsal():
 v,b,tri,profile=A['load']('v3');nail=json.loads((O/'candidate-report.json').read_text())['v3']['nail']['vertices'];p=v['p'].astype(float);center=p[nail].mean(0);normal=v['n'][nail].mean(0);normal/=np.linalg.norm(normal)
 A['A']['V']['render']('v3-nail-dorsal',p,tri,v['uv'],R/'assets/v3/textures/body-albedo.png',center,center+normal*.15,[],scale=.035)
if __name__=='__main__':audit();dorsal()
