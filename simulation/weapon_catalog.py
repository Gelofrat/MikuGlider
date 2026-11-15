import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path


def documents(path):
    text = path.read_text('utf-8-sig')
    return {identifier: (int(kind), block) for kind, identifier, block in
            re.findall(r'^--- !u!(\d+) &(-?\d+)\s*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}


def field(block, name):
    match = re.search(r'^  ' + re.escape(name) + r': \{fileID: (-?\d+)\}', block, re.M)
    return match[1] if match else None


def vector(block, name, axes):
    match = re.search(r'^  ' + re.escape(name) + r': \{([^}]+)\}', block, re.M)
    values = dict(re.findall(r'(\w):\s*([^,}]+)', match[1]))
    return [float(values[axis]) for axis in axes]


class WeaponCatalog:
    def __init__(self, assets, destination):
        self.assets = assets
        self.destination = destination
        self.files = {}
        self.sources = {}
        for folder in ('Mesh', 'Material', 'Texture2D', 'MonoBehaviour', 'AnimationClip'):
            for meta in (assets / folder).glob('*.meta'):
                match = re.search(r'^guid: (\w+)', meta.read_text('utf-8-sig'), re.M)
                if match:
                    self.files[match[1]] = meta.with_suffix('')

    def record(self, path):
        self.sources[str(path.relative_to(self.assets)).replace('\\', '/')] = hashlib.sha256(path.read_bytes()).hexdigest()

    def resource(self, guid, folder):
        path = self.files[guid]
        self.record(path)
        target = self.destination / folder / (guid + path.suffix)
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or target.read_bytes() != path.read_bytes():
            shutil.copyfile(path, target)
        return 'Weapons/' + folder + '/' + guid

    def texture(self, guid):
        path = self.files[guid]
        self.record(path)
        text = path.read_text('utf-8-sig')
        match = re.search(r'      _MainTex:\s*\n\s*m_Texture: \{fileID: \d+, guid: (\w+)', text)
        return self.resource(match[1], 'Textures') if match else ''

    def weapon(self, filename):
        path = self.assets / 'GameObject' / filename
        self.record(path)
        docs = documents(path)
        transforms = {key: block for key, (kind, block) in docs.items() if kind == 4}
        objects = {key: re.search(r'^  m_Name: (.*)', block, re.M)[1].strip('"')
                   for key, (kind, block) in docs.items() if kind == 1}
        gun = next(block for kind, block in docs.values() if kind == 114 and field(block, 'gunModel'))
        anchors = {field(gun, 'gunModel'): 'cc_WeaponRoot', field(gun, 'magModel'): 'MagRoot',
                   field(gun, 'mag1Model'): 'Mag1Root'}
        anchors = {key: value for key, value in anchors.items() if key and key != '0'}
        active = {}

        def include(key):
            if key in active:
                return active[key]
            parent = field(transforms[key], 'm_Father')
            active[key] = key in anchors or (parent in transforms and include(parent))
            return active[key]

        def scale(key):
            values = vector(transforms[key], 'm_LocalScale', 'xyz')
            parent = field(transforms[key], 'm_Father')
            return [a * b for a, b in zip(values, scale(parent))] if parent in transforms else values

        included = [key for key in transforms if include(key)]
        indices = {key: index for index, key in enumerate(included)}
        nodes = []
        for key in included:
            block = transforms[key]
            obj = field(block, 'm_GameObject')
            magazine = key in anchors and anchors[key] != 'cc_WeaponRoot'
            node = {'name': objects[obj], 'parent': indices.get(field(block, 'm_Father'), -1),
                    'socket': anchors.get(key, ''),
                    'position': [0, 0, 0] if magazine else vector(block, 'm_LocalPosition', 'xyz'),
                    'rotation': [0, 0, 0, 1] if magazine else vector(block, 'm_LocalRotation', 'xyzw'),
                    'scale': scale(key) if magazine else vector(block, 'm_LocalScale', 'xyz'),
                    'mesh': '', 'textures': []}
            if node['socket']:
                node['parent'] = -1
            mesh_filter = next((value for kind, value in docs.values() if kind == 33 and field(value, 'm_GameObject') == obj), None)
            if mesh_filter:
                mesh = re.search(r'^  m_Mesh: \{fileID: \d+, guid: (\w+)', mesh_filter, re.M)[1]
                # Built-in quads are weapon HUD/reticle surfaces, not exported
                # weapon geometry. Keep their transforms for child nodes.
                if mesh == '0000000000000000e000000000000000':
                    nodes.append(node)
                    continue
                node['mesh'] = self.resource(mesh, 'Meshes')
                render = next(value for kind, value in docs.values() if kind == 23 and field(value, 'm_GameObject') == obj)
                mats = re.search(r'^  m_Materials:\n((?:  -[^\n]*\n)*)', render, re.M)[1]
                node['textures'] = [self.texture(g) for g in re.findall(r'guid: (\w+)', mats)]
            nodes.append(node)
        clip_guids = set(re.findall(r'fileID: 7400000, guid: (\w+)', gun))
        animation_sets = []
        for guid in set(re.findall(r'fileID: 11400000, guid: (\w+)', gun)):
            asset = self.files.get(guid)
            if asset and re.search(r'^  idle:\n    clip:', asset.read_text('utf-8-sig'), re.M):
                self.record(asset)
                animation_sets.append(asset.name)
                clip_guids.update(re.findall(r'fileID: 7400000, guid: (\w+)', asset.read_text('utf-8-sig')))
        return {'key': path.stem.lower().replace(' ', '-'), 'label': path.stem, 'nodes': nodes,
                'clipGuids': sorted(clip_guids), 'animationSets': sorted(animation_sets)}

    def build(self):
        weapons = [self.weapon(name + '.prefab') for name in ('Fast Shotgun', 'Heavy Shotgun', 'Bounce Shotgun', 'Mini Cannon', 'Plate Launcher', 'Wide Gun', 'SMG')]
        result = {'weapons': weapons, 'sources': [{'path': key, 'sha256': value} for key, value in sorted(self.sources.items())],
                  'weaponSocket': 'cc_WeaponRoot', 'magazineSockets': ['MagRoot', 'Mag1Root'],
                  'materialMode': 'Original base color textures with the lab albedo shader',
                  'limitations': ['Weapon skins, procedural camera recoil, sway and muzzle effects are not simulated.',
                                  'Firing and reload timing use the unmodified weapon prefab values; gameplay upgrade modifiers are not simulated.']}
        self.destination.mkdir(parents=True, exist_ok=True)
        (self.destination / 'catalog.json').write_text(json.dumps(result, indent=2), encoding='utf8')
        return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-assets', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    result = WeaponCatalog(args.game_assets, root / 'Package/Resources/MikuPhysicsLab/Weapons').build()
    print(json.dumps({'weapons': [{'key': item['key'], 'meshes': sum(bool(node['mesh']) for node in item['nodes']),
                                 'animationClips': len(item['clipGuids'])} for item in result['weapons']],
                      'sourceFiles': len(result['sources'])}))
