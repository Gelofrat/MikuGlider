import argparse
import filecmp
import hashlib
import io
import json
import gzip
import math
import re
import shutil
import struct
from pathlib import Path
from animation_catalog import AnimationCatalog, physics_profile
from weapon_catalog import WeaponCatalog

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / 'simulation/Package'
RESOURCES = PACKAGE / 'Resources/MikuPhysicsLab'
SHARED = [
    'src/Assets/HandRigDefinition.cs', 'src/Animation/HandKinematics.cs', 'src/Animation/AnatomicalHandRig.cs',
    'src/Camera/WeaponModelCopy.cs',
    'src/Assets/ModelCorrections.cs', 'src/Animation/ModelPoseCorrections.cs',
    'src/Animation/ModelCorrectionMeshes.cs', 'src/Animation/RendererBoneBinding.cs',
    'src/Assets/SkinModelDefinition.cs', 'src/Skins/SkinMeshValidator.cs',
    'src/Animation/FlightHairVisual.cs',
    'src/Animation/FirstPersonWristVisual.cs',
    'src/Animation/SleevePoseRig.cs',
    'src/Assets/SkinWeightBlend.cs',
    'src/Physics/SeatedHandClearance.cs',
    'src/Assets/AssetBinaryReader.cs', 'src/Assets/MeshResourceReader.cs',
    'src/Assets/HairResourceReader.cs', 'src/Assets/MeshIndices.cs',
    'src/Animation/HairAnimationData.cs', 'src/Animation/AnimatedHairVisual.cs',
    'src/Physics/CharacterBodyBones.cs',
    'src/Physics/GarmentWidthProfile.cs', 'src/Physics/GarmentProbeSet.cs',
    'src/Physics/GarmentArmClearance.cs', 'src/Physics/GarmentLegClearance.cs', 'src/Physics/PoseGarmentRig.cs',
    'src/Physics/SkirtGarmentVisual.cs', 'src/Physics/HairSpringSolver.cs',
    'src/Physics/SimulationCapsule.cs',
    'diagnostics/src/Data/CaptureData.cs', 'diagnostics/src/Data/CharacterData.cs', 'diagnostics/src/Geometry/GeometryMath.cs',
    'diagnostics/src/Geometry/PointIndex.cs', 'diagnostics/src/Geometry/TriangleIndex.cs',
    'diagnostics/src/Geometry/ClothVertexMapping.cs', 'diagnostics/src/Geometry/ParticleSpace.cs',
    'diagnostics/src/Geometry/SkinningModel.cs', 'diagnostics/src/Analysis/SurfaceGeometry.cs',
    'diagnostics/src/Analysis/ParticleMotion.cs'
]


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    write_text(path, json.dumps(value, ensure_ascii=False, indent=2))


def write_text(path, value):
    write_bytes(path, value.encode('utf8'))


def write_bytes(path, value):
    if not path.is_file() or path.read_bytes() != value:
        path.write_bytes(value)


def copy(source, destination):
    if not destination.is_file() or not filecmp.cmp(source, destination, shallow=False):
        shutil.copy2(source, destination)


def harness_manifest():
    paths = [path for path in sorted(PACKAGE.rglob('*.cs')) if 'Generated' not in path.parts]
    paths.extend([Path(__file__), Path(__file__).with_name('animation_catalog.py'), Path(__file__).with_name('weapon_catalog.py')])
    return [{'path': str(path.relative_to(ROOT)).replace('\\', '/'), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
            for path in paths]


def resource_manifest():
    return [{'path': str(path.relative_to(RESOURCES)).replace('\\', '/'), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
            for path in sorted(RESOURCES.rglob('*')) if path.is_file() and path.suffix != '.meta' and path.name != 'source-manifest.json']


def copy_models():
    for index, folder, prefix in [(1, 'assets/V1', 'MikuGlider'), (2, 'assets/v2', 'MikuGliderV2'), (3, 'assets/v3', 'MikuGliderV3')]:
        output = RESOURCES / ('v' + str(index))
        output.mkdir(exist_ok=True)
        copy(ROOT / folder / (prefix + '.mikumesh'), output / 'model.bytes')
        copy(ROOT / folder / (prefix + '.mikuhair'), output / 'hair.bytes')
        copy(ROOT / folder / 'definition.json', output / 'definition.json')
        copy(ROOT / folder / 'corrections.json', output / 'corrections.json')
        for name in ('body-albedo', 'face-albedo', 'icon'):
            copy(ROOT / folder / 'textures' / (name + '.png'), output / (name + '.png'))


def prefab_rig(source):
    text = source.read_text('utf-8-sig')
    documents = list(re.finditer(r'^--- !u!(\d+) &(-?\d+)\s*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S))
    names = {}
    transforms = []
    renderers = []

    def reference(block, field):
        match = re.search(r'^  ' + field + r': \{fileID: (-?\d+)\}', block, re.M)
        if not match:
            raise ValueError('Missing prefab reference: ' + field)
        return match[1]

    def vector(block, field, axes):
        match = re.search(r'^  ' + field + r': \{([^}]+)\}', block, re.M)
        values = dict(re.findall(r'(\w):\s*([^,}]+)', match[1]))
        return [float(values[axis]) for axis in axes]

    for document in documents:
        kind, identifier, block = document.groups()
        if kind == '1':
            names[identifier] = re.search(r'^  m_Name: (.*)$', block, re.M)[1].strip('"')
        elif kind == '4':
            transforms.append((identifier, block))
        elif kind == '137':
            renderers.append(block)
    ids = {identifier: index for index, (identifier, _) in enumerate(transforms)}
    by_object = {reference(block, 'm_GameObject'): index for index, (_, block) in enumerate(transforms)}
    nodes = [{'name': names[reference(block, 'm_GameObject')], 'parent': ids.get(reference(block, 'm_Father'), -1),
              'position': vector(block, 'm_LocalPosition', 'xyz'), 'rotation': vector(block, 'm_LocalRotation', 'xyzw'),
              'scale': vector(block, 'm_LocalScale', 'xyz')} for _, block in transforms]

    def path(index):
        node = nodes[index]
        return (path(node['parent']) + '/' if node['parent'] >= 0 else '') + node['name']

    result = {'nodes': nodes, 'renderers': []}
    for block in renderers:
        bone_block = re.search(r'^  m_Bones:\n((?:  -[^\n]*\n)*)', block, re.M)[1]
        bones = re.findall(r'fileID: (-?\d+)', bone_block)
        node = by_object[reference(block, 'm_GameObject')]
        result['renderers'].append({'node': node, 'name': path(node), 'root': ids[reference(block, 'm_RootBone')],
                                    'bones': [ids[bone] for bone in bones],
                                    'quality': int(re.search(r'^  m_Quality: (\d+)', block, re.M)[1])})
    if not nodes or not result['renderers']:
        raise ValueError('No native rig found in ' + str(source))
    return result


def matrix_pose(values, offset):
    matrix = [values[offset + row * 4:offset + row * 4 + 4] for row in range(4)]
    for column in range(3):
        scale = math.sqrt(sum(matrix[row][column] ** 2 for row in range(3)))
        if scale < 1e-8:
            raise ValueError('Singular bone matrix in recording')
        for row in range(3):
            matrix[row][column] /= scale
    trace = sum(matrix[i][i] for i in range(3))
    if trace > 0:
        size = math.sqrt(trace + 1) * 2
        rotation = [(matrix[2][1] - matrix[1][2]) / size,
                    (matrix[0][2] - matrix[2][0]) / size,
                    (matrix[1][0] - matrix[0][1]) / size, size / 4]
    else:
        axis = max(range(3), key=lambda i: matrix[i][i])
        other, last = (axis + 1) % 3, (axis + 2) % 3
        size = math.sqrt(1 + matrix[axis][axis] - matrix[other][other] - matrix[last][last]) * 2
        rotation = [0, 0, 0, 0]
        rotation[axis] = size / 4
        rotation[other] = (matrix[axis][other] + matrix[other][axis]) / size
        rotation[last] = (matrix[axis][last] + matrix[last][axis]) / size
        rotation[3] = (matrix[last][other] - matrix[other][last]) / size
    length = math.sqrt(sum(v * v for v in rotation))
    return {'position': [matrix[row][3] for row in range(3)], 'rotation': [v / length for v in rotation]}


def replays(captures):
    entries = []
    destination = RESOURCES / 'Replays'
    destination.mkdir(exist_ok=True)
    folders = sorted(p.parent for p in captures.glob('*/frames.jsonl'))[-16:]
    for folder in folders:
        frames = []
        source_names = None
        skin = ''
        definitions = {}
        for line in (folder / 'frames.jsonl').open(encoding='utf8'):
            f = json.loads(line)
            c = f.get('cloth') or {}
            bones = c.get('bones')
            character = f.get('character') or {}
            renderer = next((r for r in character.get('renderers', []) if r.get('role') == 'skirt_surface' and
                             len(r.get('boneMatrices') or []) >= 72 * 16 and r.get('binding')), None)
            if renderer is not None:
                binding = renderer['binding']
                if binding not in definitions:
                    definitions[binding] = json.loads(gzip.decompress((folder / 'renderers' / (binding + '.json.gz')).read_bytes()))
                paths = definitions[binding]['bonePaths'][:72]
                # A capture of a detached/destroyed rig can contain null bones.
                # It cannot provide a replay pose; do not fabricate joint names.
                if len(paths) < 72 or any(not isinstance(p, str) or not p for p in paths):
                    continue
                names = [p.rsplit('/', 1)[-1] for p in paths]
                bones = [dict(matrix_pose(renderer['boneMatrices'], i * 16), name=name) for i, name in enumerate(names)]
            if not bones or len(bones) < 72:
                continue
            skin = c.get('skin', '').split()[-1] if c.get('skin') else ''
            if skin not in ('v1', 'v2', 'v3'):
                continue
            if source_names is None:
                source_names = [b['name'] for b in bones[:72]]
            flags = sum(bit for bit, key in [(1, 'flying'), (2, 'sliding'), (4, 'grounded'), (8, 'sprinting')] if c.get(key))
            frames.append((f['realtime'], bones[:72], flags))
        if len(frames) < 2:
            continue
        key = folder.name
        origin = frames[0][0]
        with io.BytesIO() as stream:
            stream.write(struct.pack('<ii', len(frames), 72))
            for time, bones, flags in frames:
                stream.write(struct.pack('<fI', time - origin, flags))
                for bone in bones:
                    stream.write(struct.pack('<7f', *bone['position'], *bone['rotation']))
            write_bytes(destination / (key + '.bytes'), stream.getvalue())
        duration = frames[-1][0] - origin
        entries.append({'key': key, 'label': skin + ' / ' + folder.name.split('-')[1],
                        'variant': int(skin[-1]) - 1, 'frames': len(frames), 'duration': duration,
                        'samplesPerSecond': (len(frames) - 1) / duration, 'boneNames': source_names})
    write_json(RESOURCES / 'replays.json', {'tracks': entries})
    return len(entries)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-assets', type=Path)
    parser.add_argument('--code-only', action='store_true')
    parser.add_argument('--reuse-catalog', action='store_true')
    parser.add_argument('--captures', type=Path, default=Path(r'C:\Apps\Steam\steamapps\common\Mycopunk\BepInEx\MikuGliderDiagnostics\captures'))
    args = parser.parse_args()
    RESOURCES.mkdir(parents=True, exist_ok=True)
    generated = PACKAGE / 'Runtime/Generated'
    generated.mkdir(exist_ok=True)
    allowed = {Path(p).name for p in SHARED}
    for old in generated.glob('*.cs'):
        if old.name not in allowed and old.resolve().parent == generated.resolve():
            old.unlink()
    sources = []
    for relative in SHARED:
        path = ROOT / relative
        text = path.read_text('utf8')
        converted, count = re.subn(r'^namespace ([\w.]+);\s*$', r'namespace \1\n{', text, count=1, flags=re.M)
        if count != 1:
            raise ValueError('Expected one file-scoped namespace: ' + relative)
        write_text(generated / path.name, converted.rstrip() + '\n}\n')
        sources.append({'path': relative, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    if args.reuse_catalog:
        copy_models()
        replays(args.captures)
        catalog_path = RESOURCES / 'catalog.json'
        catalog = json.loads(catalog_path.read_text('utf8'))
        catalog['variants'] = [v for v in catalog['variants'] if v['key'] != 'v3-org']
        write_json(catalog_path, catalog)
    if args.code_only or args.reuse_catalog:
        manifest = json.loads((RESOURCES / 'source-manifest.json').read_text('utf8'))
        manifest['sources'] = sources
        manifest['harness'] = harness_manifest()
        manifest['resources'] = resource_manifest()
        write_json(PACKAGE / 'source-manifest.json', manifest)
        write_json(RESOURCES / 'source-manifest.json', manifest)
        print(json.dumps({'sharedSources': len(sources), 'codeOnly': args.code_only, 'reuseCatalog': args.reuse_catalog}))
        return
    copy_models()
    if args.game_assets:
        game_assets = args.game_assets
    else:
        game_assets = next((p / 'ExportedProject/Assets' for p in (Path.home() / 'Downloads').glob('*/dec')
                            if (p / 'ExportedProject/Assets/MonoBehaviour/Default Animation Set Glider.asset').is_file()), None)
    if game_assets is None:
        raise FileNotFoundError('Pass --game-assets with the AssetRipper Assets directory')
    rigs = {}
    for prefab, name, kind in [('Glider Rig.prefab', 'body-rig', 'body'), ('GliderFirstPersonArms.prefab', 'arms-rig', 'arms')]:
        rigs[kind] = prefab_rig(game_assets / 'GameObject' / prefab)
        write_json(RESOURCES / (name + '.json'), rigs[kind])
    clips = AnimationCatalog(game_assets, rigs, RESOURCES / 'Animations', write_text, write_json).build()
    WeaponCatalog(game_assets, RESOURCES / 'Weapons').build()
    write_json(RESOURCES / 'physics-profile.json', physics_profile(game_assets))
    settings = ROOT / 'simulation/Project/ProjectSettings'
    for name in ('DynamicsManager.asset', 'TimeManager.asset'):
        copy(game_assets.parent / 'ProjectSettings' / name, settings / name)
    write_json(RESOURCES / 'catalog.json', {'variants': [
        {'key': 'v1', 'label': 'Hatsune Miku v1', 'hipRadius': .085, 'thighRadius': .12},
        {'key': 'v2', 'label': 'Hatsune Miku v2', 'hipRadius': .10, 'thighRadius': .14},
        {'key': 'v3', 'label': 'Hatsune Miku v3', 'hipRadius': .09, 'thighRadius': .12}]})
    tracks = replays(args.captures)
    manifest = {'sources': sources, 'harness': harness_manifest(), 'resources': resource_manifest(),
                'unity': '6000.4.12f1', 'animationConfigurations': len(clips)}
    write_json(PACKAGE / 'source-manifest.json', manifest)
    write_json(RESOURCES / 'source-manifest.json', manifest)
    print(json.dumps({'sharedSources': len(sources), 'variants': 3, 'replays': tracks, 'animationConfigurations': len(clips)}))


if __name__ == '__main__':
    main()
