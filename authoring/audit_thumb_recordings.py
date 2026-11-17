"""Blender MCP: nail rigidity, distal strain and weapon crossings in real recordings."""
import runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];O=R/'authoring/thumb-return';S=R/'scratch/thumb-return'
A=runpy.run_path(str(R/'authoring/check_thumb_return.py'));M=A['M'];T=A['T'];BA=A['BA']
CAP=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def audit():
 frames=[];sources=[]
 for capture in sorted(CAP.glob('20261116-13*')):
  path=capture/'frames.jsonl';rows=[json.loads(x) for x in path.read_text().splitlines()];sources.append({'path':str(path),'sha256':sha(path),'frames':len(rows)})
  for i,f in enumerate(rows):frames.append((capture.name,i,f))
 reference=json.loads((S/'v2-before-0.json').read_text());root=np.array(json.loads((CAP/'20261116-133221-136-93ae4f'/'frames.jsonl').open().readline())['character']['weaponMatrix']).reshape(4,4)
 weaponLocal=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@np.linalg.inv(root).T)[:,:3]) for w in reference['weapons']]
 report={'captures':sources,'variants':{},'notes':['Recorded matrices sampled at approximately 30 Hz; skipped samples remain explicitly identified.','Weapon geometry comes from the native Unity catalog, transformed by recorded PlayerAnimation.WeaponRoot.','Triangle crossings test surface intersection; they do not prove absence of fully enclosed vertices.','No claim of an anatomical medical standard.']}
 for var in ('v2','v3'):
  entry={};nail=np.array(json.loads((R/'authoring/thumb-nail-regions.json').read_text())[var]['nail'])
  _,region,regionBind,regionSections=M['read'](M['chunks']((O/'original'/f'{var}.mikumesh').read_bytes())['arm-left']);rj=np.linalg.inv(regionBind)[:,:3,3];ra=rj[27]-rj[26];ra/=np.linalg.norm(ra);regionTip=(np.where(region['ids']>=24,region['w'],0).sum(1)>.8)&(((region['p']-rj[26])@ra)>.004)
  for stage in ('original','candidate'):
   model=O/('original/'+var+'.mikumesh' if stage=='original' else var+'.mikumesh');profilepath=O/('original/'+var+'-corrections.json' if stage=='original' else var+'-corrections.json');profile=json.loads(profilepath.read_text());_,v,b,ss=M['read'](M['chunks'](model.read_bytes())['arm-left']);tri=np.concatenate(ss);j=np.linalg.inv(b)[:,:3,3];axis=j[27]-j[26];axis/=np.linalg.norm(axis)
   # Freeze anatomical vertex IDs before edits, so moving a failing triangle
   # cannot silently remove it from the tested region.
   tr=tri[np.all(regionTip[tri],axis=1)]
   edges=np.unique(np.sort(np.concatenate([tr[:,[0,1]],tr[:,[1,2]],tr[:,[2,0]]]),axis=1),axis=0);length=np.linalg.norm(v['p'][edges[:,1]]-v['p'][edges[:,0]],axis=1);edges=edges[length>1e-6];length=length[length>1e-6]
   ne=np.array([(int(x),int(y)) for k,x in enumerate(nail) for y in nail[k+1:]]);nl=np.linalg.norm(v['p'][ne[:,1]]-v['p'][ne[:,0]],axis=1);ne=ne[nl>.0005];nl=nl[nl>.0005]
   row={'modelSha256':sha(model),'correctionsSha256':sha(profilepath),'frames':0,'tipTriangles':len(tr),'nailVertices':len(nail),'maximumNailDistanceErrorRatio':0.,'minimumDistalEdgeRatio':1.,'maximumDistalEdgeRatio':1.,'maximumEdgeStrainChangePer60Hz':0.,'weaponCrossingFrames':0,'maximumWeaponCrossingPairs':0,'idleCrossingFrames':0,'rows':[]};previous=None
   for capture,index,f in frames:
    ch=f['character'];bones=np.array(ch['renderers'][0]['boneMatrices']).reshape(-1,4,4)[:28];cam=np.array(ch['camera']['firstPersonPass']['worldToCamera']).reshape(4,4);weapon=np.array(ch['weaponMatrix']).reshape(4,4);p=A['skin'](v,b,bones,profile,var,weapon);p=(np.c_[p,np.ones(len(p))]@cam.T)[:,:3];scale=float(np.linalg.norm((bones[26]@b[26])[:3,:3],axis=0).mean())
    err=float(np.max(abs(np.linalg.norm(p[ne[:,1]]-p[ne[:,0]],axis=1)/(nl*scale)-1)));ratios=np.linalg.norm(p[edges[:,1]]-p[edges[:,0]],axis=1)/(length*scale);strain=0.
    if previous is not None and previous[0]==capture:
     dt=f['realtime']-previous[1]
     if 0<dt<=.067:strain=float(np.max(abs(ratios-previous[2]))/(dt*60))
    previous=(capture,f['realtime'],ratios)
    world=cam@np.array(ch['weaponMatrix']).reshape(4,4);weapons=[dict(w,positions=(np.c_[w['positions'],np.ones(len(w['positions']))]@world.T)[:,:3]) for w in weaponLocal]
    hit=A['contact'](p,tr,weapons);cross=hit['interiorTrianglePairs'];idle=ch['animations'][0]['clip']=='Idle'
    row['frames']+=1;row['maximumNailDistanceErrorRatio']=max(row['maximumNailDistanceErrorRatio'],err);row['minimumDistalEdgeRatio']=min(row['minimumDistalEdgeRatio'],float(ratios.min()));row['maximumDistalEdgeRatio']=max(row['maximumDistalEdgeRatio'],float(ratios.max()));row['maximumEdgeStrainChangePer60Hz']=max(row['maximumEdgeStrainChangePer60Hz'],strain);row['weaponCrossingFrames']+=int(cross>0);row['idleCrossingFrames']+=int(cross>0 and idle);row['maximumWeaponCrossingPairs']=max(row['maximumWeaponCrossingPairs'],cross)
    row['rows'].append({'capture':capture,'index':index,'sample':f['sample'],'time':f['realtime'],'nailError':err,'edgeStrainChangePer60Hz':strain,'crossings':cross,'idle':idle})
   entry[stage]=row;print(var,stage,{k:v for k,v in row.items() if k!='rows'},flush=True)
  report['variants'][var]=entry
 (O/'recorded-audit.json').write_text(json.dumps(report,indent=2));return report
if __name__=='__main__':audit()
