"""Apply the reviewed 0.12.25 geometry through Blender MCP.

The native meshes retain their topology, UVs and bind poses. Right cuffs
follow the forearm; left-hand weights remain unchanged. The payload contains
the two right cuffs and the locally rounded v3 left hand.
Run with first-person-hands.blend open; inputs are pinned to 0.12.24.
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


def update_mesh(obj, vertices, binds, twist_length):
    basis = BUILDER['C'][:3, :3]
    assert len(obj.data.vertices) == len(vertices)
    obj.data.vertices.foreach_set('co', (vertices['p'] @ basis.T).ravel())
    obj.data.update()
    obj.data.normals_split_custom_set_from_vertices((vertices['n'] @ basis.T).tolist())
    helpers = bool(obj.get('preview_only'))
    if helpers:
        _, low, fraction = BUILDER['wrist'](vertices, binds, np.linalg.inv(binds), twist_length)
    for group in obj.vertex_groups:
        group.remove(list(range(len(vertices))))
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
    manifest = json.loads((ROOT / 'authoring/sleeve-weapon-clearance.json').read_text())
    assert Path(bpy.data.filepath).resolve() == (ROOT / 'authoring/first-person-hands.blend').resolve()
    payload = np.load(ROOT / 'authoring/sleeve-weapon-clearance.npz')
    prepared = {}
    edits = []
    for variant, sides in manifest['changedMeshes'].items():
        path = ROOT / MODEL['PATHS'][variant]
        source = path.read_bytes()
        assert hashlib.sha256(source).hexdigest() == manifest['inputs'][variant]
        parts = MODEL['chunks'](source)
        for side in sides:
            name, before, binds, sections = MODEL['read'](parts['arm-' + side])
            after = payload[variant + '_' + side]
            assert before.shape == after.shape and before.dtype == after.dtype
            for field in (('uv', 'w', 'ids') if side == 'left' else ('uv',)):
                assert np.array_equal(before[field], after[field]), field
            assert np.isfinite(after['p']).all() and np.isfinite(after['n']).all()
            assert np.isfinite(after['w']).all() and (after['w'] >= 0).all()
            assert np.max(abs(after['w'].sum(1) - 1)) < 1e-5
            assert (after['ids'] >= 0).all() and (after['ids'] < len(binds)).all()
            for suffix in ('EXPORT', 'PREVIEW'):
                edits.append((bpy.data.objects[f'{variant} arm-{side} [{suffix}]'], after, binds, variant))
            if variant == 'v3' and side == 'left':
                edits.append((bpy.data.objects['v3 before editable mesh'], before, binds, variant))
                edits.append((bpy.data.objects['v3 after editable mesh'], after, binds, variant))
            parts[name] = MODEL['write'](name, after, binds, sections)
        result = MODEL['archive'](parts)
        assert hashlib.sha256(result).hexdigest() == manifest['outputs'][variant]
        prepared[path] = result
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for obj, vertices, binds, variant in edits:
        update_mesh(obj, vertices, binds, .24 if variant == 'v3' else 0)
    for path, result in prepared.items():
        path.write_bytes(result)
    scene = bpy.data.scenes['v3 - surface comparison']
    scene['comparison_before'] = '0.12.24'
    scene['comparison_after'] = '0.12.25: rounded knuckles; right cuffs in weapon-pose scenes'
    bpy.context.window.scene = scene
    scene.frame_set(1)
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath, compress=True)
    print('Saved the reviewed cuffs and v3 knuckles to native assets and Blender.')


if __name__ == '__main__':
    apply()
