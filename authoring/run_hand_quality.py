"""Run through Blender MCP; save every family report even when a gate fails."""
import runpy
import json
import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
validate = runpy.run_path(str(ROOT/'authoring/validate_cycler_thumb.py'))['validate']
errors = []
for family, pose, label, key in [
    ('cycler', 'Cycler thumb grip', 'SMG ', 'weapon-smg-'),
    ('shotgun', 'Shotgun thumb grip', 'Shotgun ', 'weapon-bounce-shotgun-'),
    ('plate', 'Plate thumb opposition', 'PlateLauncher ', 'weapon-plate-'),
]:
    try:
        validate('thumb-volume-'+family, pose, label, key)
    except Exception as exc:
        errors.append(f'{family}: {exc}')
summary = dict(completedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
               passed=not errors, anatomyStatus='not_checked', errors=errors,
               reports=['thumb-volume-'+f+'-animation-validation.json' for f in ('cycler','shotgun','plate')])
(ROOT/'authoring/strict-hand-quality-summary.json').write_text(json.dumps(summary, indent=2)+'\n')
if errors:
    raise RuntimeError('\n'.join(errors))
