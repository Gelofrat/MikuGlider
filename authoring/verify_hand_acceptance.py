"""Fail-closed current QA; report technical validation separately from anatomy."""
import json, hashlib, datetime
from pathlib import Path
R=Path(__file__).resolve().parents[1];O=R/'authoring/hand-repair'
PATHS={'v1':'assets/V1/MikuGlider.mikumesh','v2':'assets/v2/MikuGliderV2.mikumesh','v3':'assets/v3/MikuGliderV3.mikumesh'}
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
errors=[];anatomy=[];assets={}
def read(p):
    if not p.is_file():errors.append('Missing '+str(p.relative_to(R)));return None
    return json.loads(p.read_text(encoding='utf-8-sig'))
for var,path in PATHS.items():
    model=R/path;profile=model.parent/'corrections.json'
    assets[var]={'model':sha(model),'corrections':sha(profile)}
    for family in ('cycler','shotgun','plate'):
        rows=read(O/f'thumb-volume-{family}-animation-validation.json')
        row=next((x for x in rows or [] if x['variant']==var),{})
        if row.get('passed') is not True:errors.append(var+'/'+family+': corrective gate missing or failed')
        for key,file in [('sourceSha256',model),('correctionsSha256',profile),
                         ('clipCatalogSha256',R/'simulation/Reports/cycler-thumb-review/native-clips/clips.json'),
                         ('clipBonesSha256',R/'simulation/Reports/cycler-thumb-review/native-clips/clip-bones.bin')]:
            if row.get(key)!=sha(file):errors.append(var+'/'+family+': stale '+key)
    for side in ('left','right'):
        row=read(O/f'{var}-{side}-True-audit.json')
        if not row:continue
        if row.get('sha256')!=sha(model):errors.append(var+'/'+side+': stale surface audit')
        for key,file in [('correctionsSha256',profile),('clipCatalogSha256',R/'simulation/Reports/cycler-thumb-review/native-clips/clips.json'),('clipBonesSha256',R/'simulation/Reports/cycler-thumb-review/native-clips/clip-bones.bin')]:
            if row.get(key)!=sha(file):errors.append(var+'/'+side+': stale surface input '+key)
        if not row.get('allFrameThumbHandIntersections') or row.get('frames',0)<=0:errors.append(var+'/'+side+': incomplete collision coverage')
        if row.get('missingSections',1)>0:anatomy.append(var+'/'+side+': missing cross sections')
        if row.get('intersectionFrames',0)>0:anatomy.append(var+'/'+side+': interior intersections in '+str(row['intersectionFrames'])+' frames')
        if min(row['minimumSectionRatios'])<.75:anatomy.append(var+'/'+side+': section width below provisional 75% bound')
parity=read(O/'unity-parity.json')
expected={(v,s,c) for v in PATHS for s in ('left','right') for c in ('clip-381','clip-367','clip-313')}
if {(x['variant'],x['side'],x['clip']) for x in parity or []}!=expected:errors.append('Missing Unity parity coverage')
for row in parity or []:
    current=assets[row['variant']]
    if not row['passed'] or row.get('modelSha256')!=current['model'] or row.get('correctionsSha256')!=current['corrections']:errors.append('Unity parity failed or stale: '+row['variant'])
runtime=read(O/'unity-runtime-checks.json')
if {x['variant'] for x in runtime or []}!=set(PATHS):errors.append('Missing runtime scope/release tests')
for row in runtime or []:
    if row['allocatedBytes'] or row['changingAllocatedBytes']:errors.append('Runtime allocation regression')
    current=assets[row['variant']]
    if row.get('modelSha256')!=current['model'] or row.get('correctionsSha256')!=current['corrections']:errors.append('Stale runtime scope/release tests: '+row['variant'])
result=dict(completedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),assets=assets,
            technicalPassed=not errors,anatomyPassed=not errors and not anatomy,
            errors=errors,anatomyFailures=anatomy,
            limitations=['Live gameplay transition interruptions and weapon penetration are not certified.',
                        'A clean numerical gate does not replace an approved multi-view visual review.'],
            dllSha256=sha(R/'bin/Release/MikuGlider.dll'))
(O/'acceptance.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
if not result['anatomyPassed']:raise SystemExit(1)
