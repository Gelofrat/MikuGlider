"""Verify the current embedded hand assets and generate a candid review report."""
import json,hashlib,sys,shutil,html
from pathlib import Path
R=Path(__file__).resolve().parents[1];O=R/'authoring/plate-grip';read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'));sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
paths={'v1':R/'assets/V1/MikuGlider.mikumesh','v3':R/'assets/v3/MikuGliderV3.mikumesh'};errors=[]
def require(ok,message):
 if not ok:errors.append(message)
export=read(O/'export.json');assembly=read(O/'assembly.json');parity=read(O/'unity-parity.json');runtime=read(O/'unity-runtime-checks.json');motion=read(O/'registration-motion.json');recorded=read(O/'recorded-index-audit.json');nail=read(O/'shotgun-nail-audit.json');depth=read(O/'handle-depth.json');loaded=read(O/'exported-load.json')
for var,path in paths.items():
 h,p=sha(path),sha(path.parent/'corrections.json');prefix='MikuGlider' if var=='v1' else 'MikuGliderV3'
 require(export['models'][var]==dict(model=h,corrections=p),var+': stale export')
 require(assembly['resources'][prefix+'.Model']==h and assembly['resources'][prefix+'.Corrections']==p,var+': embedded assets differ')
 checks=[x for x in parity+runtime+motion+loaded if x['variant']==var]+[recorded[var]['candidate']]+([nail] if var=='v3' else [])
 require(all(x['modelSha256']==h and x['correctionsSha256']==p for x in checks),var+': stale measurement hashes')
 require(all(x['passed'] for x in parity+runtime+loaded if x['variant']==var),var+': Unity check failed')
 require(next(x for x in motion if x['variant']==var)['passedNoNewDegenerates'],var+': new collapsed triangles')
 require(recorded[var]['candidate']['indexNailCrossingFrames']==0,var+': index nail crosses weapon')
 require(next(x for x in depth if x['variant']==var and x['stage']=='candidate')['maximumPenetrationMeters']<=next(x for x in depth if x['variant']==var and x['stage']=='original')['maximumPenetrationMeters'],var+': increased pad penetration')
require(nail['passed'] and nail['frames']==705,'Shotgun nail/skin regression')
require(sha(R/'assets/v2/MikuGliderV2.mikumesh')=='5fc869fb0b0e8e0ebd9be61c9348172f6c4fc1fa725d7afe2da3e0c4acbaba5b','v2 changed')
require(sha(R/'assets/v2/corrections.json')=='ee6708ff14feb3bf230303e184cf337b270942817b0297d5d9d9a9753270c9cd','v2 profile changed')
require(assembly['version']=='0.12.31.0' and assembly['dll']==sha(R/'bin/Release/MikuGlider.dll'),'Assembly version/hash differs')
require(export['blendSha256']==sha(O/'repaired-hands.blend'),'Blender source changed')
installed=Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/plugins/MikuGlider.dll');installedMatches=installed.exists() and sha(installed)==assembly['dll']
if '--installed' in sys.argv:require(installedMatches,'Installed DLL differs')
result=dict(version='0.12.31',focusedSurfaceRepairPassed=not errors,wholeHandAnatomyCertified=False,installedDllMatches=installedMatches,models=export['models'],dllSha256=assembly['dll'],errors=errors,limitations=['Residual Plate pad/handle penetrations remain, including in the native Glider reference.','No new live-game capture of 0.12.31; recordings are replayed in Unity.','No degenerate triangles is not proof of valid anatomy; local compression and other self-contacts remain outside this focused acceptance.'])
(O/'acceptance.json').write_text(json.dumps(result,indent=2)+'\n')
review=O/'review';review.mkdir(exist_ok=True)
for var in ('v1','v3'):
 for stage in ('before','candidate','glider'):
  for view in ('game','side'):
   name=f'{var}-{stage}-0-{view}.png';shutil.copy2(R/'scratch/plate-grip'/name,review/name)
shutil.copy2(R/'scratch/plate-grip/v3-nail-dorsal.png',review/'v3-nail-dorsal.png')
parts=['<!doctype html><meta charset="utf-8"><title>Miku Glider 0.12.31 — кисти</title><style>body{font:16px/1.5 system-ui;background:#15191f;color:#e6ebf0;margin:32px auto;max-width:1500px;padding:0 20px}h1{font-size:28px}h2{margin-top:36px}.notice{padding:18px;background:#46361c;border-left:4px solid #e5ad48}.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}img{width:100%;display:block}figure{margin:0}figcaption{padding:7px;background:#222a35}a{color:#72d9d0}table{border-collapse:collapse;width:100%}td,th{padding:9px;border-bottom:1px solid #45505d;text-align:left}@media(max-width:800px){.grid{grid-template-columns:1fr}}code{overflow-wrap:anywhere}</style><h1>Miku Glider 0.12.31 — исправление формы пальцев</h1><p>v1: устранён острый излом указательного пальца. v3: исправлены выступающие суставы и поверхность ногтя большого пальца. Геометрия и веса изменены в Blender MCP, воспроизведение записей проверено через Unity MCP.</p><p class="notice"><b>Полная анатомическая приёмка не пройдена.</b> Кожа пальцев ещё пересекает рукоять Plate Launcher. Эта сборка исправляет наблюдаемые изломы и ноготь; остаточные контакты перечислены ниже. Нужен перезапуск игры для загрузки установленной DLL.</p>']
for var in ('v1','v3'):
 parts.append(f'<h2>{var}: одинаковая записанная поза и камера</h2>')
 for view,label in [('game','Направление игровой камеры'),('side','Профиль хвата')]:
  parts.append(f'<p>{label}. Нейтральный свет стенда; материал отличается от игрового шейдера.</p><div class="grid">')
  for stage,label in [('glider','Glider — эталон позы'),('before','До: 0.12.30'),('candidate','После: 0.12.31')]:parts.append(f'<figure><img src="{var}-{stage}-0-{view}.png"><figcaption>{label}</figcaption></figure>')
  parts.append('</div>')
parts.append('<h2>Ноготь v3, крупный вид тыльной стороны</h2><img style="max-width:800px" src="v3-nail-dorsal.png"><p>Округлая пластина с жёсткой привязкой к дистальной фаланге. Старое ломаное пятно на кончике убрано. Новые грани включены в тест пересечений.</p><h2>Измерения и ограничения</h2><table><tr><th>Проверка</th><th>Результат</th></tr>')
parts.append(f'<tr><td>Unity: записи Plate Launcher</td><td>351 кадр v1 + 345 кадров v3; ногти сохраняют форму, скелет игры не меняется</td></tr><tr><td>Blender / Unity BakeMesh</td><td>6 поз; максимальная ошибка {max(x["errorMeters"] for x in parity)*1000:.4f} мм при допуске 0.02 мм</td></tr><tr><td>Ноготь и кончик большого пальца v3 / shotgun</td><td>705 кадров; пересечений с оружием: {nail["weaponCrossingFrames"]}; ногтя с кожей: {nail["nailSkinCrossingFrames"]}</td></tr><tr><td>Штатные клипы</td><td>2993 отсчёта на каждую изменённую кисть; новых вырожденных треугольников нет. Это не проверка правильности всей анатомии.</td></tr>')
for var in ('v1','v3'):
 old=next(x for x in depth if x['variant']==var and x['stage']=='original');new=next(x for x in depth if x['variant']==var and x['stage']=='candidate');native=next(x for x in depth if x['variant']==var and x['stage']=='Glider');row=recorded[var]['candidate']
 parts.append(f'<tr><td>{var}: кожа указательного / Plate</td><td>Пересечения остаются в {row["indexCrossingFrames"]} из {row["frames"]} кадров. Глубина внутри рукояти в исходной контрольной позе: {old["maximumPenetrationMeters"]*1000:.3f} → {new["maximumPenetrationMeters"]*1000:.3f} мм; Glider: {native["maximumPenetrationMeters"]*1000:.3f} мм.</td></tr>')
parts.append('</table><p>Новая запись игры с 0.12.31 пока отсутствует. Проверка использует записанные матрицы костей и отдельных частей оружия. Исходная геометрия правых рук и v2 сохранена.</p><p><a href="../acceptance.json">Итог и хеши</a> · <a href="../recorded-index-audit.json">Контакты на записях</a> · <a href="../shotgun-nail-audit.json">Проверка ногтя</a> · <a href="../repaired-hands.blend">Редактируемый Blender-файл</a></p>')
(review/'index.html').write_text(''.join(parts),encoding='utf-8');print(json.dumps(result,indent=2));raise SystemExit(bool(errors))
