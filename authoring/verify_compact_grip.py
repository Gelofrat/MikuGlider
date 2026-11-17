"""Release gate for the v1 Plate grip and its exact built resources."""
import hashlib,json,math,sys
from pathlib import Path
R=Path(__file__).resolve().parents[1];O=R/'authoring/grip-fix'
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
errors=[]

def require(condition,message):
    if not condition:errors.append(message)

profile=O/'v1-corrections.json';reports={}
for name in ('recorded-contact-checks','unity-checks','native-regression','unity-parity','editable-rig-check','assembly'):
    path=O/(name+'.json');reports[name]=read(path)
    require(path.stat().st_mtime_ns>=profile.stat().st_mtime_ns,name+': report predates candidate profile')
contact=reports['recorded-contact-checks'];runtime=reports['unity-checks']
require([r['frames'] for r in contact]==[373,351],'Incomplete collision coverage')
require([r['frames'] for r in runtime]==[373,351],'Incomplete Unity coverage')
require(all(r['candidateContactFrames']==0 and r['candidateSelfIntersectionFrames']==0 and r['newDegenerateFaces']==0 for r in contact),'Recorded collision/geometry failure')
require(all(r['passed'] and len(r['allNails'])==5 for r in runtime),'Unity rig/nail check failed')
require(reports['native-regression']['passed'] and reports['native-regression']['frames']==2993,'Native clip regression failed')
require(len(reports['unity-parity'])==3 and all(r['passed'] for r in reports['unity-parity']),'Unity/CPU parity failed')
require(reports['editable-rig-check']['passed'],'Editable Blender rig differs')
require(sha(profile)==sha(R/'assets/V1/corrections.json'),'Production corrections differ')
candidate=read(O/'unity-0.json');production=read(O/'production-unity-0.json')
require(len(candidate['positions'])==len(production['positions']),'Production vertex count differs')
production_error=max(math.dist(a,b) for a,b in zip(candidate['positions'],production['positions']))
require(production_error<2e-5 and candidate['shapeWeights']==production['shapeWeights'],'Production asset load differs')
assembly=reports['assembly'];previous=read(R/'authoring/plate-grip/assembly.json')
require(assembly['version']=='0.12.32.0' and assembly['diagnosticsVersion']=='0.4.4.0','Assembly version differs')
require(assembly['dll']==sha(R/'bin/Release/MikuGlider.dll'),'Built DLL differs')
for key,path in {
    'MikuGlider.Model':'assets/V1/MikuGlider.mikumesh','MikuGlider.Corrections':'assets/V1/corrections.json',
    'MikuGliderV2.Model':'assets/v2/MikuGliderV2.mikumesh','MikuGliderV2.Corrections':'assets/v2/corrections.json',
    'MikuGliderV3.Model':'assets/v3/MikuGliderV3.mikumesh','MikuGliderV3.Corrections':'assets/v3/corrections.json'
}.items():
    require(assembly['resources'][key]==sha(R/path),key+': embedded resource differs')
    if key!='MikuGlider.Corrections':require(assembly['resources'][key]==previous['resources'][key],key+': unexpected asset change')
installed=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/plugins')
matches=(installed/'MikuGlider.dll').is_file() and sha(installed/'MikuGlider.dll')==assembly['dll']
if '--installed' in sys.argv:
    require(matches,'Installed mod differs')
    require(sha(installed/'MikuGliderDiagnostics/MikuGlider.Diagnostics.dll')==sha(R/'diagnostics/bin/Release/MikuGlider.Diagnostics.dll'),'Installed diagnostics differ')
result=dict(version='0.12.32',scope='v1 left hand / Plate Launcher',passed=not errors,installedDllMatches=matches,errors=errors,
    recordedFrames=724,nativeClipFrames=2993,weaponCrossingFrames=sum(r['candidateContactFrames'] for r in contact),
    selfIntersectionFrames=sum(r['candidateSelfIntersectionFrames'] for r in contact),
    maximumNailPairDistanceChangeMeters=max(n['maximumPairDistanceChangeMeters'] for r in runtime for n in r['allNails']),
    maximumUnityParityErrorMeters=max(r['maximumPositionErrorMeters'] for r in reports['unity-parity']),
    productionLoadErrorMeters=production_error,dllSha256=assembly['dll'],correctionsSha256=sha(profile),
    blenderSha256=sha(O/'v1-compact-grip.blend'),evidenceSha256={name:sha(O/(name+'.json')) for name in reports},
    limitations=['Replays existing recordings; no new live-game capture of 0.12.32 yet.',
        'Triangle tests cover sampled frames and exclude adjacent/shared-edge contacts; between-frame intersections are not certified.',
        'This release changes v1 Plate grip. It is not acceptance of all v2/v3 poses or certification of human anatomy.'])
(O/'acceptance.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result,indent=2));raise SystemExit(bool(errors))
