"""Read-only deformation validation; run through Blender MCP after lab export."""
import hashlib
import json
import runpy
from pathlib import Path

import numpy as np
from mathutils import Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[1]
B = runpy.run_path(str(ROOT / 'authoring/build_first_person_hand_review.py'))
M = B['model']
REVIEW = ROOT / 'simulation/Reports/cycler-thumb-review'


def validate():
    catalog = json.loads((REVIEW / 'native-clips/clips.json').read_text())
    bones = np.fromfile(REVIEW / 'native-clips/clip-bones.bin', '<f4').reshape(catalog['frames'], 56, 4, 4)
    clips = [c for c in catalog['clips'] if c['label'].startswith('SMG ') or c['key'].startswith('weapon-smg-')]
    manifest = json.loads((ROOT / 'authoring/cycler-thumb.json').read_text())
    reference = json.loads((ROOT / 'references/glider-first-person.json').read_text())['renderers'][0]['bones']
    results = []
    for variant, path in M['PATHS'].items():
        raw = (ROOT / path).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == manifest['models'][variant]['sha256']
        _, vertices, binds, sections = M['read'](M['chunks'](raw)['arm-left'])
        profile = json.loads(((ROOT / path).parent / 'corrections.json').read_text())
        correction = next(c for c in profile['meshes'] if c['weapon'] == 'SMG')
        pose = correction['poses'][0]
        delta = np.zeros((len(vertices), 3))
        delta[pose['vertices']] = [[v['x'], v['y'], v['z']] for v in pose['deltas']]
        tri = np.concatenate(sections)
        tri = tri[np.any(np.linalg.norm(delta[tri], axis=2) > 1e-8, axis=1)]
        core = (vertices['ids'] >= 1) & (vertices['ids'] <= 3)
        weights = np.where(core, 0, vertices['w'])
        core_weight = np.where(core, vertices['w'], 0).sum(1)
        points = np.c_[vertices['p'], np.ones(len(vertices))]
        row = dict(variant=variant, clips=len(clips), frames=0, newDegenerateTriangleFrames=0,
                   minimumTriangleAreaRatio=1., maximumWorldDisplacement=0.,
                   maximumActivationStepAt60Hz=0., baseMeshUnchanged=True, activationByClip={})
        for clip in clips:
            activations = []
            for frame in range(clip['offset'], clip['offset'] + clip['count']):
                native_bones = bones[frame, :28].astype(float)
                activation = 1.
                for driver in pose['drivers']:
                    index = driver['bone']
                    parent = reference[index]['parentBoneIndex']
                    local = np.linalg.inv(native_bones[parent]) @ native_bones[index]
                    q = Matrix(local.tolist()).to_quaternion()
                    r = driver['rotation']
                    target = Quaternion((r['w'], r['x'], r['y'], r['z']))
                    angle = np.degrees(2 * np.arccos(np.clip(abs(q.dot(target)), 0, 1)))
                    t = np.clip((angle - driver['innerRadius']) / (driver['radius'] - driver['innerRadius']), 0, 1)
                    activation = min(activation, 1 - t * t * (3 - 2 * t))
                activations.append(float(activation))
                native = native_bones @ binds
                helpers, low, fraction = B['wrist'](vertices, binds, native_bones, .24 if variant == 'v3' else 0)
                blend = np.zeros((len(vertices), 4, 4))
                for k in range(4):
                    blend += native[vertices['ids'][:, k]] * weights[:, k, None, None]
                blend += (helpers[low] * (1 - fraction[:, None, None]) + helpers[low + 1] * fraction[:, None, None]) * core_weight[:, None, None]
                before = np.einsum('nij,nj->ni', blend, points)[:, :3]
                displacement = np.einsum('nij,nj->ni', blend[:, :3, :3], delta) * activation
                after = before + displacement
                area = lambda p: np.linalg.norm(np.cross(p[tri[:, 1]] - p[tri[:, 0]], p[tri[:, 2]] - p[tri[:, 0]]), axis=1)
                a, b = area(before), area(after)
                valid = a > 1e-10
                row['newDegenerateTriangleFrames'] += int(np.sum(valid & (b < 1e-10)))
                row['minimumTriangleAreaRatio'] = min(row['minimumTriangleAreaRatio'], float(np.min(b[valid] / a[valid])))
                row['maximumWorldDisplacement'] = max(row['maximumWorldDisplacement'], float(np.linalg.norm(displacement, axis=1).max()))
                row['frames'] += 1
            step = float(np.max(np.abs(np.diff(activations)))) if len(activations) > 1 else 0.
            row['maximumActivationStepAt60Hz'] = max(row['maximumActivationStepAt60Hz'], step)
            row['activationByClip'][clip['key']] = dict(minimum=min(activations), maximum=max(activations), maximumStep=step)
        assert row['newDegenerateTriangleFrames'] == 0, row
        results.append(row)
    output = ROOT / 'authoring/cycler-thumb-animation-validation.json'
    output.write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps([{k: v for k, v in row.items() if k != 'activationByClip'} for row in results]))


if __name__ == '__main__':
    validate()
