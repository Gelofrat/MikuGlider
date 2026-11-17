"""Current, hash-bound acceptance for the reported v2/v3 distal-thumb defect."""
import json,hashlib,datetime
from pathlib import Path
R=Path(__file__).resolve().parents[1];O=R/'authoring/thumb-return'
paths={'v2':'assets/v2/MikuGliderV2.mikumesh','v3':'assets/v3/MikuGliderV3.mikumesh'}
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
errors=[];assets={};summary={}
recorded=read(O/'recorded-audit.json');parity=read(O/'unity-parity.json');unity=read(O/'unity-recorded-checks.json');assembly=read(O/'assembly.json')
def require(ok,message):
 if not ok:errors.append(message)
for var,path in paths.items():
 model=R/path;profile=model.parent/'corrections.json';h,p=sha(model),sha(profile);assets[var]={'model':h,'corrections':p}
 require(read(O/'export.json')[var]==assets[var],var+': stale export')
 require(assembly['resources']['MikuGlider'+var.upper()+'.Model']==h,var+': embedded model differs')
 require(assembly['resources']['MikuGlider'+var.upper()+'.Corrections']==p,var+': embedded corrections differ')
 row=recorded['variants'][var]['candidate'];old=recorded['variants'][var]['original']
 require(row['modelSha256']==h and row['correctionsSha256']==p,var+': stale recordings')
 require(row['frames']==705 and row['weaponCrossingFrames']==0,var+': tip crosses recorded weapon')
 require(row['maximumNailDistanceErrorRatio']<.001 and row['maximumEdgeStrainChangePer60Hz']<.001,var+': distal distortion')
 for family in ('cycler','shotgun','plate'):
  check=next(x for x in read(O/f'thumb-volume-{family}-animation-validation.json') if x['variant']==var)
  require(check['passed'] and check['sourceSha256']==h and check['correctionsSha256']==p,var+': stale/failed '+family)
  for key,file in [('clipCatalogSha256','clips.json'),('clipBonesSha256','clip-bones.bin')]:
   require(check[key]==sha(R/'simulation/Reports/cycler-thumb-review/native-clips'/file),var+': stale native clips')
 checks=[x for x in parity if x['variant']==var]
 require({x['index'] for x in checks}=={0,80,167,168} and all(x['passed'] and x['modelSha256']==h and x['correctionsSha256']==p for x in checks),var+': Unity parity missing or stale')
 check=next(x for x in unity if x['variant']==var)
 require(check['passed'] and check['frames']==705 and check['modelSha256']==h and check['correctionsSha256']==p,var+': Unity recording replay missing or stale')
 surface=read(O/f'{var}-left-True-audit.json')
 require(surface['sha256']==h and surface['correctionsSha256']==p and surface['frames']==2993 and surface['missingSections']==0 and min(surface['minimumSectionRatios'])>=.75,var+': section gate failed')
 summary[var]={'recordedFrames':705,'weaponCrossingFramesBefore':old['weaponCrossingFrames'],'weaponCrossingFramesAfter':row['weaponCrossingFrames'],
  'maximumNailDistanceErrorRatioBefore':old['maximumNailDistanceErrorRatio'],'maximumNailDistanceErrorRatioAfter':row['maximumNailDistanceErrorRatio'],
  'nativeClips':surface['clips'],'nativeFrames':surface['frames'],'minimumTissueSectionRatios':surface['minimumSectionRatios'],
  'remainingSampledSelfContacts':[{k:v for k,v in x.items() if k in ('clip','frame','pairs')} for x in surface['collisionSamples'] if x['pairs']]}
for source in recorded['captures']:require(sha(Path(source['path']))==source['sha256'],'Recording changed')
runtime=read(O/'unity-runtime-checks.json')
require({r['variant'] for r in runtime}=={'v1','v2','v3'},'Runtime scope coverage missing')
for row in runtime:require(row['allocatedBytes']==0 and row['changingAllocatedBytes']==0 and row['shotgunIsolation'] and row['disableReset'],'Runtime regression')
require(sha(R/'assets/V1/MikuGlider.mikumesh')=='6e6841c8ad50ae27ae263c80186e4a5a5b8ffa6edfc3dbc2d2f629bab7ca1a7c','Unexpected v1 change')
require(assembly['dll']==sha(R/'bin/Release/MikuGlider.dll') and assembly['version']=='0.12.30.0','Assembly changed')
result={'version':'0.12.30','checkedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'focusedThumbFixPassed':not errors,'wholeHandAnatomyCertified':False,'assets':assets,'results':summary,'errors':errors,
 'dllSha256':assembly['dll'],'limitations':['Replay uses recorded bone matrices; a fresh live game session with the new DLL has not been captured.',
 'Weapon crossing test excludes tangent/coplanar contacts and intersections below 20 micrometres; not a fully enclosed-point test.',
 'Remaining hand self-contacts in other grips are reported separately; no full-hand anatomical certification.']}
installed=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/plugins/MikuGlider.dll')
result['installedDllMatches']=installed.is_file() and sha(installed)==assembly['dll']
result['restartRequiredForRunningGame']=True
(O/'acceptance.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2));raise SystemExit(bool(errors))
