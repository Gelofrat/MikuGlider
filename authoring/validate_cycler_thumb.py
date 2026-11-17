"""Read-only deformation validation; run through Blender MCP after lab export."""
import hashlib
import json
import runpy
import sys
from pathlib import Path

import numpy as np
from mathutils import Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'authoring'))
_quality = runpy.run_path(str(ROOT / 'authoring/hand_quality.py'))
surface_metrics, failures, DEFAULT_LIMITS = (_quality[k] for k in ('surface_metrics', 'failures', 'DEFAULT_LIMITS'))
B = runpy.run_path(str(ROOT / 'authoring/build_first_person_hand_review.py'))
M = B['model']
REVIEW = ROOT / 'simulation/Reports/cycler-thumb-review'


def validate(manifest_name='cycler-thumb', pose_name='Cycler thumb grip',
             label_prefix='SMG ', key_prefix='weapon-smg-', candidate_folder=None):
    catalog = json.loads((REVIEW / 'native-clips/clips.json').read_text())
    bones = np.fromfile(REVIEW / 'native-clips/clip-bones.bin', '<f4').reshape(catalog['frames'], 56, 4, 4)
    clips = [c for c in catalog['clips'] if c['label'].startswith(label_prefix) or c['key'].startswith(key_prefix)]
    if not clips or any(c['count'] < 2 or c['duration'] <= 0 for c in clips):
        raise ValueError('Missing clips or invalid sampling duration')
    manifest = json.loads((ROOT / 'authoring' / (manifest_name + '.json')).read_text())
    reference = json.loads((ROOT / 'references/glider-first-person.json').read_text())['renderers'][0]['bones']
    results = []
    for variant, path in M['PATHS'].items():
        model_file = Path(candidate_folder)/(variant+'.mikumesh') if candidate_folder else ROOT/path
        if candidate_folder and not model_file.exists():
            continue
        profile_file = Path(candidate_folder)/(variant+'-corrections.json') if candidate_folder else (ROOT/path).parent/'corrections.json'
        raw = model_file.read_bytes()
        if not candidate_folder and variant in manifest['models'] and hashlib.sha256(raw).hexdigest() != manifest['models'][variant]['sha256']:
            raise ValueError('Stale corrective manifest: '+variant)
        _, vertices, binds, sections = M['read'](M['chunks'](raw)['arm-left'])
        profile = json.loads(profile_file.read_text())
        pose = next((p for c in profile['meshes'] for p in c['poses'] if p['name']==pose_name), None)
        if pose is None:
            results.append(dict(variant=variant, passed=True, correctionStatus='not_applicable', anatomyStatus='not_checked',
                sourceSha256=hashlib.sha256(raw).hexdigest(),
                correctionsSha256=hashlib.sha256(profile_file.read_bytes()).hexdigest(),
                clipCatalogSha256=hashlib.sha256((REVIEW/'native-clips/clips.json').read_bytes()).hexdigest(),
                clipBonesSha256=hashlib.sha256((REVIEW/'native-clips/clip-bones.bin').read_bytes()).hexdigest(),
                failures=[], unchecked=['base hand anatomy is checked by audit_hand_surface.py']))
            continue
        delta = np.zeros((len(vertices), 3))
        delta[pose['vertices']] = [[v['x'], v['y'], v['z']] for v in pose['deltas']]
        tri = np.concatenate(sections)
        tri = tri[np.any(np.linalg.norm(delta[tri], axis=2) > 1e-8, axis=1)]
        if not len(tri):
            raise ValueError('Empty corrective coverage: '+variant)
        core = (vertices['ids'] >= 1) & (vertices['ids'] <= 3)
        weights = np.where(core, 0, vertices['w'])
        core_weight = np.where(core, vertices['w'], 0).sum(1)
        points = np.c_[vertices['p'], np.ones(len(vertices))]
        row = dict(variant=variant, clips=len(clips), frames=0, newDegenerateTriangleFrames=0,
                   minimumTriangleAreaRatio=1., maximumWorldDisplacement=0.,
                   maximumActivationStepAt60Hz=0., baseline='Current base mesh with corrective disabled', activationByClip={})
        row['maximumCorrectiveJumpOverPalmWidth']=0.
        row.update(minimumEdgeLengthRatio=1., maximumEdgeLengthRatio=1.,
                   largeNormalRotationTriangleFrames=0, baselineDegenerateTriangles=0,
                   sourceSha256=hashlib.sha256(raw).hexdigest(),
                   correctionsSha256=hashlib.sha256(profile_file.read_bytes()).hexdigest(),
                   clipCatalogSha256=hashlib.sha256((REVIEW/'native-clips/clips.json').read_bytes()).hexdigest(),
                   clipBonesSha256=hashlib.sha256((REVIEW/'native-clips/clip-bones.bin').read_bytes()).hexdigest(),
                   worstSamples={}, limits=DEFAULT_LIMITS,
                   anatomyStatus='not_checked',
                   unchecked=['right hand', 'uncorrected base anatomy', 'joint limits',
                              'thumb thickness', 'self intersections', 'weapon contact',
                              'runtime transitions', 'matched Glider visual review'])
        if pose.get('contact'):
            raise ValueError('Experimental contact controller is not accepted for release')
        for clip in clips:
            activations = []
            previous_activation = None
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
                if previous_activation is not None:
                    full = np.einsum('nij,nj->ni', blend[:, :3, :3], delta)
                    palm_width = np.linalg.norm(native_bones[5,:3,3]-native_bones[20,:3,3])
                    if palm_width<=1e-8:raise ValueError('Degenerate palm scale')
                    jump=float(np.linalg.norm(full,axis=1).max()*abs(activation-previous_activation)/palm_width/(clip['duration']/(clip['count']-1)*60))
                    if jump>row['maximumCorrectiveJumpOverPalmWidth']:
                        row['maximumCorrectiveJumpOverPalmWidth']=jump
                        row['worstSamples']['correctiveJump']=dict(clip=clip['key'],frame=frame-clip['offset'],value=jump)
                previous_activation=activation
                after = before + displacement
                metrics = surface_metrics(before, after, tri)
                sample = dict(clip=clip['key'], frame=frame-clip['offset'],
                              timeSeconds=(frame-clip['offset'])*clip['duration']/(clip['count']-1),
                              triangle=metrics['worstTriangle'])
                for field in ('minimumEdgeLengthRatio', 'maximumEdgeLengthRatio'):
                    worse = metrics[field] < row[field] if field.startswith('minimum') else metrics[field] > row[field]
                    if worse:
                        row[field] = metrics[field]
                        row['worstSamples'][field] = {**sample, 'value': metrics[field],
                            'edge': metrics['minimumEdge' if field.startswith('minimum') else 'maximumEdge']}
                row['largeNormalRotationTriangleFrames'] += metrics['largeNormalRotationTriangleFrames']
                row['baselineDegenerateTriangles'] = max(row['baselineDegenerateTriangles'], metrics['baselineDegenerateTriangles'])
                area = lambda p: np.linalg.norm(np.cross(p[tri[:, 1]] - p[tri[:, 0]], p[tri[:, 2]] - p[tri[:, 0]]), axis=1)
                a, b = area(before), area(after)
                valid = a > 1e-10
                row['newDegenerateTriangleFrames'] += int(np.sum(valid & (b < 1e-10)))
                ratio = np.where(valid, b / np.maximum(a, 1e-30), 1)
                worst = int(np.argmin(ratio))
                if ratio[worst] < row['minimumTriangleAreaRatio']:
                    row['minimumTriangleAreaRatio'] = float(ratio[worst])
                    row['smallestAreaSample'] = dict(clip=clip['key'], frame=frame-clip['offset'],
                        activation=float(activation), triangle=tri[worst].tolist(),
                        beforeDoubleArea=float(a[worst]), afterDoubleArea=float(b[worst]))
                row['maximumWorldDisplacement'] = max(row['maximumWorldDisplacement'], float(np.linalg.norm(displacement, axis=1).max()))
                row['frames'] += 1
            # Normalize to a 1/60 second interval; do not assume export rate.
            changes = np.abs(np.diff(activations)) / (clip['duration']/(clip['count']-1)*60)
            step = float(np.max(changes))
            if step > row['maximumActivationStepAt60Hz']:
                index = int(np.argmax(changes))
                row['worstSamples']['maximumActivationStepAt60Hz'] = dict(
                    clip=clip['key'], frame=index+1,
                    timeSeconds=(index+1)*clip['duration']/(clip['count']-1), value=step)
            row['maximumActivationStepAt60Hz'] = max(row['maximumActivationStepAt60Hz'], step)
            row['activationByClip'][clip['key']] = dict(minimum=min(activations), maximum=max(activations), maximumStep=step)
        row['failures'] = failures(row)
        row['passed'] = not row['failures']
        results.append(row)
    output = (Path(candidate_folder) if candidate_folder else ROOT / 'authoring') / (manifest_name + '-animation-validation.json')
    output.write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps([{k: v for k, v in row.items() if k != 'activationByClip'} for row in results]))
    if any(not row['passed'] for row in results):
        raise RuntimeError('Corrective deformation gate failed; details saved to '+str(output))


if __name__ == '__main__':
    validate()
