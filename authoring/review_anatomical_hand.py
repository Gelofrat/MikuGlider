"""Blender MCP render and surface checks of the actual Unity candidate output."""
import bpy, runpy, json
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];O=R/'authoring/anatomical-hand'
CAPTURES=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures')
M=runpy.run_path(str(R/'authoring/retarget_first_person_hands.py'))
V=runpy.run_path(str(R/'authoring/review_thumb_return.py'));V['render'].__globals__['O']=O
A=runpy.run_path(str(R/'authoring/audit_hand_surface.py'))
C=runpy.run_path(str(R/'authoring/check_thumb_return.py'))

def review():
 reports=[]
 for variant,side in [('v1','left'),('v3','right')]:
  _,v,b,ss=M['read'](M['chunks']((R/M['PATHS'][variant]).read_bytes())['arm-'+side]);tri=np.concatenate(ss)
  d=json.loads((O/(variant+'-candidate.json')).read_text());p=np.array(d['positions']);j=np.array(d['anatomicalBones']).reshape(28,4,4)[:,:3,3]
  cam=np.array(d['camera']['firstPersonPass']['worldToCamera']).reshape(4,4);cam[:3,3]+=cam[:3,:3]@d['origin']
  center=j[[3,5,10,15,20,25]].mean(0);normal=np.cross(j[5]-j[20],j[10]-j[3]);normal/=np.linalg.norm(normal)
  tex=R/Path(M['PATHS'][variant]).parent/'textures/body-albedo.png'
  for label,eye,with_weapon in [('palm',center-normal*.25,True),('dorsal',center+normal*.25,False)]:
   V['render'](variant+'-'+label,p,tri,v['uv'],tex,center,eye,d['weapons'] if with_weapon else [],scale=.13)
  fw=np.where(v['ids']>=3,v['w'],0).sum(1);hand=tri[np.all(fw[tri]>.8,axis=1)];weld=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1]
  reports.append({'variant':variant,'self':len(A['intersections'](p,hand,weld)),'contact':C['contact'](p,hand,d['weapons'])})
 (O/'surface-checks.json').write_text(json.dumps(reports,indent=2));print(json.dumps(reports))

if __name__=='__main__':review()

def replay_surfaces(include_legacy=True):
 capture_root=CAPTURES
 reports=[] if include_legacy else [r for r in json.loads((O/'replay-surfaces.json').read_text()) if r['legacy']]
 for record in json.loads((O/'replay-checks.json').read_text()):
  var=record['variant'];side='left' if var=='v1' else 'right';capture=record['capture'];n=record['vertices']
  _,v,b,ss=M['read'](M['chunks']((R/M['PATHS'][var]).read_bytes())['arm-'+side]);tri=np.concatenate(ss);fw=np.where(v['ids']>=3,v['w'],0).sum(1);hand=tri[np.all(fw[tri]>.8,axis=1)];weld=np.unique(np.round(v['p'],6),axis=0,return_inverse=True)[1]
  baseline=json.loads((O/(var+'-baseline.json')).read_text());frames=[json.loads(l)['character'] for l in (capture_root/capture/'frames.jsonl').read_text().splitlines()]
  # Mesh-local geometry from the same weapon, transformed separately for every frame.
  reference_capture='20261116-180944-759-d974ee' if var=='v1' else '20261116-181646-129-73fa08'
  reference=json.loads((capture_root/reference_capture/'frames.jsonl').read_text().splitlines()[0])['character'];weapons={}
  for w in baseline['weapons']:
   name=w['name'].removeprefix('Native weapon ');r=next((r for r in reference['renderers'] if r['role']=='weapon' and r['path'].split('/')[-1]==name),None)
   if r is None:continue
   m=np.array(r['localToWorld']).reshape(4,4);m[:3,3]-=baseline['origin'];local=(np.c_[w['positions'],np.ones(len(w['positions']))]@np.linalg.inv(m).T)[:,:3];weapons[name]=(local,w['triangles'])
  for legacy in ((False,True) if include_legacy else (False,)):
   file=O/(var+'-'+capture+('-legacy' if legacy else '')+'-positions.bin')
   if not file.exists():continue
   positions=np.fromfile(file,'<f4').reshape(-1,n,3);rows=[]
   for i,(p,frame) in enumerate(zip(positions,frames)):
    if not np.isfinite(p).all():raise ValueError((var,capture,i,'Non-finite skinned surface'))
    posed=[];origin=np.array(frame['camera']['position'])
    for w in frame['renderers']:
     name=w['path'].split('/')[-1]
     if w['role']!='weapon' or name not in weapons:continue
     local,t=weapons[name];m=np.array(w['localToWorld']).reshape(4,4);m[:3,3]-=origin;posed.append({'name':name,'positions':(np.c_[local,np.ones(len(local))]@m.T)[:,:3],'triangles':t})
    contact=C['contact'](p,hand,posed);self_count=len(A['intersections'](p,hand,weld))
    rows.append({'frame':i,'self':self_count,'contact':contact['interiorTrianglePairs'],'depth':contact['maximumPlaneStraddleDepth']})
   reports.append({'variant':var,'capture':capture,'legacy':legacy,'frames':len(rows),'selfFrames':sum(r['self']>0 for r in rows),'contactFrames':sum(r['contact']>0 for r in rows),'maximumPlaneStraddleDepthMeters':max(r['depth'] for r in rows),'rows':rows})
   print(json.dumps({k:v for k,v in reports[-1].items() if k!='rows'}),flush=True)
 (O/'replay-surfaces.json').write_text(json.dumps(reports,indent=2))
