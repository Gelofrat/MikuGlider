"""Apply the Blender-reviewed v1 left index correction, pinned to 0.12.23.

Run through Blender MCP with first-person-hands.blend open. The payload is
the exact reviewed native vertex data; the editable export and animated
preview meshes are updated together. No runtime animation driver is added.
"""
import hashlib
import json
import runpy
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
BUILDER = runpy.run_path(str(ROOT / 'authoring/build_first_person_hand_review.py'))
MODEL = BUILDER['model']


def update_mesh(obj, vertices, binds):
    assert len(obj.data.vertices) == len(vertices)
    basis = BUILDER['C'][:3, :3]
    obj.data.vertices.foreach_set('co', (vertices['p'] @ basis.T).ravel())
    obj.data.update()
    obj.data.normals_split_custom_set_from_vertices((vertices['n'] @ basis.T).tolist())
    all_ids = list(range(len(vertices)))
    for group in obj.vertex_groups:
        group.remove(all_ids)
    helpers = bool(obj.get('preview_only'))
    if helpers:
        _, low, fraction = BUILDER['wrist'](vertices, binds, np.linalg.inv(binds), 0)
    for i, vertex in enumerate(vertices):
        weights = {}
        core = 0.0
        for bone, weight in zip(vertex['ids'], vertex['w']):
            if helpers and bone in (1, 2, 3):
                core += float(weight)
            elif weight > 0:
                weights[int(bone)] = weights.get(int(bone), 0) + float(weight)
        if helpers:
            weights[28 + int(low[i])] = core * (1 - float(fraction[i]))
            weights[29 + int(low[i])] = core * float(fraction[i])
        for bone, weight in weights.items():
            if weight > 0:
                obj.vertex_groups[bone].add([i], weight, 'REPLACE')


def apply():
    manifest = json.loads((ROOT / 'authoring/index-curl.json').read_text())
    path = ROOT / MODEL['PATHS']['v1']
    source = path.read_bytes()
    assert hashlib.sha256(source).hexdigest() == manifest['inputSha256']
    assert Path(bpy.data.filepath).resolve() == (ROOT / 'authoring/first-person-hands.blend').resolve()
    parts = MODEL['chunks'](source)
    name, before, binds, sections = MODEL['read'](parts['arm-left'])
    with np.load(ROOT / 'authoring/index-curl.npz') as payload:
        after = payload['v1'].copy()
    assert before.dtype == after.dtype and before.shape == after.shape
    assert np.array_equal(before['uv'], after['uv'])
    assert all(np.isfinite(after[field]).all() for field in ('p', 'n', 'w'))
    assert (after['w'] >= 0).all() and np.max(abs(after['w'].sum(1) - 1)) < 1e-5
    assert (after['ids'] >= 0).all() and (after['ids'] < len(binds)).all()
    targets = [bpy.data.objects[n] for n in (
        'v1 arm-left [EXPORT]', 'v1 arm-left [PREVIEW]',
        'v1 before editable mesh', 'v1 after editable mesh')]
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for obj in targets:
        update_mesh(obj, before if obj.name == 'v1 before editable mesh' else after, binds)
    parts[name] = MODEL['write'](name, after, binds, sections)
    result = MODEL['archive'](parts)
    assert hashlib.sha256(result).hexdigest() == manifest['outputSha256']
    path.write_bytes(result)
    scene = bpy.data.scenes['v1 - surface comparison']
    scene['comparison_before'] = '0.12.23'
    scene['comparison_after'] = '0.12.24: left index tucked behind thumb'
    bpy.context.window.scene = scene
    scene.frame_set(1)
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath, compress=True)
    print('Applied v1 index correction to runtime asset and editable Blender meshes.')


if __name__ == '__main__':
    apply()
