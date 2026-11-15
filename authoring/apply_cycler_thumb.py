"""Apply the Blender-authored Cycler corrective, without changing base meshes.

Run through Blender MCP with first-person-hands.blend open. The shape is
enabled only for the SMG gun model and the matching closed-thumb pose.
"""
import hashlib
import json
import runpy
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
B = runpy.run_path(str(ROOT / 'authoring/build_first_person_hand_review.py'))
M = B['model']
SHAPE = 'Cycler thumb grip'


def shape(obj, target):
    if obj.data.shape_keys is None:
        obj.shape_key_add(name='Basis')
    key = obj.data.shape_keys.key_blocks.get(SHAPE) or obj.shape_key_add(name=SHAPE)
    key.data.foreach_set('co', (target @ B['C'][:3, :3].T).ravel())
    key.value = 0
    obj['cycler_correction'] = 'SMG only; thumb A/B pose match; base mesh unchanged'
    return key


def apply():
    manifest = json.loads((ROOT / 'authoring/cycler-thumb.json').read_text())
    assert Path(bpy.data.filepath).resolve() == (ROOT / 'authoring/first-person-hands.blend').resolve()
    payload = np.load(ROOT / 'authoring/cycler-thumb.npz')
    for variant, path in M['PATHS'].items():
        source = (ROOT / path).read_bytes()
        assert hashlib.sha256(source).hexdigest() == manifest['models'][variant]['sha256']
        _, vertices, _, _ = M['read'](M['chunks'](source)['arm-left'])
        target = payload[variant]
        assert target.shape == vertices['p'].shape and np.isfinite(target).all()
        for suffix in ('EXPORT', 'PREVIEW'):
            shape(bpy.data.objects[f'{variant} arm-left [{suffix}]'], target)
        profile = manifest['models'][variant]['profile']
        destination = (ROOT / path).parent / 'corrections.json'
        current = json.loads(destination.read_text())
        previous = next((entry for entry in current['meshes'] if entry['key'] == profile['key']), None)
        if previous:
            profile['poses'] += [pose for pose in previous['poses'] if pose['name'] != SHAPE]
        current['meshes'] = [entry for entry in current['meshes'] if entry['key'] != profile['key']] + [profile]
        destination.write_text(json.dumps(current, indent=2) + '\n')
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath, compress=True)
    print('Saved three Cycler shape keys and scoped runtime profiles; base models unchanged.')


if __name__ == '__main__':
    apply()
