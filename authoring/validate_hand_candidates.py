import runpy,json
from pathlib import Path
R=Path(__file__).resolve().parents[1];O=R/'authoring/hand-repair'
v=runpy.run_path(str(R/'authoring/validate_cycler_thumb.py'))['validate']
errors=[]
for family,name,label,key in [('cycler','Cycler thumb grip','SMG ','weapon-smg-'),('shotgun','Shotgun thumb grip','Shotgun ','weapon-bounce-shotgun-'),('plate','Plate thumb opposition','PlateLauncher ','weapon-plate-')]:
 try:v('thumb-volume-'+family,name,label,key,O)
 except Exception as e:errors.append(str(e))
(O/'validation-errors.json').write_text(json.dumps(errors,indent=2));print(errors)
