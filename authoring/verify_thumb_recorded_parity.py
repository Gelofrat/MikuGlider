"""Blender MCP: compare final Unity BakeMesh with offline skinning at captured poses."""
import runpy,json,hashlib
from pathlib import Path
import numpy as np
R=Path(__file__).resolve().parents[1];A=runpy.run_path(str(R/'authoring/check_thumb_return.py'));M=A['M'];O=A['O'];S=A['S']
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
rows=[]
for var in ('v2','v3'):
 _,v,b,ss=M['read'](M['chunks']((O/f'{var}.mikumesh').read_bytes())['arm-left']);profile=json.loads((O/f'{var}-corrections.json').read_text())
 for index in (0,80,167,168):
  d=json.loads((S/f'{var}-unity-{index}.json').read_text());bones=np.array(d['bones']).reshape(-1,4,4)
  expected=A['skin'](v,b,bones,profile,var,np.array(d['weaponMatrix']).reshape(4,4));actual=np.array(d['positions']);error=float(np.linalg.norm(expected-actual,axis=1).max())
  rows.append(dict(variant=var,index=index,maximumWorldError=error,tolerance=2e-5,passed=error<2e-5,modelSha256=sha(O/f'{var}.mikumesh'),correctionsSha256=sha(O/f'{var}-corrections.json'),shapeWeights=d['shapeWeights']))
(O/'unity-parity.json').write_text(json.dumps(rows,indent=2)+'\n');print(json.dumps(rows));assert all(x['passed'] for x in rows)
