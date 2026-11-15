"""Apply the Blender-authored shotgun thumb shape; run through Blender MCP."""
import hashlib
import json
import runpy
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
B = runpy.run_path(str(ROOT / 'authoring/build_first_person_hand_review.py'))
M = B['model']
SHAPE = 'Shotgun thumb grip'


def shape(obj, target):
    if obj.data.shape_keys is None:
        obj.shape_key_add(name='Basis')
    key = obj.data.shape_keys.key_blocks.get(SHAPE) or obj.shape_key_add(name=SHAPE)
    key.data.foreach_set('co', (target @ B['C'][:3, :3].T).ravel())
    key.value = 0
    return key


def apply():
    manifest = json.loads((ROOT / 'authoring/shotgun-thumb.json').read_text())
    assert Path(bpy.data.filepath).resolve() == (ROOT / 'authoring/first-person-hands.blend').resolve()
    payload = np.load(ROOT / 'authoring/shotgun-thumb.npz')
    for variant, path in M['PATHS'].items():
        source = (ROOT / path).read_bytes()
        entry = manifest['models'][variant]
        assert hashlib.sha256(source).hexdigest() == entry['sha256']
        _, vertices, _, _ = M['read'](M['chunks'](source)['arm-left'])
        target = payload[variant]
        assert target.shape == vertices['p'].shape and np.isfinite(target).all()
        for suffix in ('EXPORT', 'PREVIEW'):
            shape(bpy.data.objects[f'{variant} arm-left [{suffix}]'], target)
        destination = (ROOT / path).parent / 'corrections.json'
        current = json.loads(destination.read_text())
        profile = next(p for p in current['meshes'] if p['key'] == entry['key'])
        assert profile['fingerprint'] == entry['fingerprint']
        profile['poses'] = [p for p in profile['poses'] if p['name'] != SHAPE] + [entry['pose']]
        destination.write_text(json.dumps(current, indent=2) + '\n')
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath, compress=True)
    print('Saved shotgun shape keys; Cycler correction and base meshes preserved.')


if __name__ == '__main__':
    apply()
