"""Apply the reviewed Blender edits to the three left first-person hands.

Run through Blender MCP with runpy.run_path(..., run_name='__main__').
The compressed payload is a lossless snapshot of the reviewed vertex data;
the editable meshes and native weights live in first-person-hands.blend.
Inputs are pinned to 0.12.22 so this cannot silently overwrite later edits.
No topology, UV, bind pose, right arm or third-person data is replaced.
"""
import hashlib
import json
import runpy
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
MODEL = runpy.run_path(str(ROOT / 'authoring/retarget_first_person_hands.py'))


def apply():
    manifest = json.loads((ROOT / 'authoring/hand-surface-refinement.json').read_text())
    payload = np.load(ROOT / 'authoring/hand-surface-refinement.npz')
    prepared = {}
    for variant, path in MODEL['PATHS'].items():
        original = (ROOT / path).read_bytes()
        assert hashlib.sha256(original).hexdigest() == manifest['inputs'][variant], path
        parts = MODEL['chunks'](original)
        name, before, binds, sections = MODEL['read'](parts['arm-left'])
        after = payload[variant]
        assert after.shape == before.shape and after.dtype == before.dtype
        assert np.array_equal(after['uv'], before['uv'])
        assert all(np.isfinite(after[field]).all() for field in ['p', 'n', 'w'])
        assert (after['w'] >= 0).all() and np.max(abs(after['w'].sum(1) - 1)) < 1e-5
        assert (after['ids'] >= 0).all() and (after['ids'] < len(binds)).all()
        parts[name] = MODEL['write'](name, after, binds, sections)
        prepared[path] = MODEL['archive'](parts)
    for path, data in prepared.items():
        (ROOT / path).write_bytes(data)
    print('Applied the three reviewed left-hand edits; native topology and runtime cost unchanged.')


if __name__ == '__main__':
    apply()
