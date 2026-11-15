import hashlib
import re


def scalar(text, field, default=0.0):
    match = re.search(r'^\s*' + re.escape(field) + r':\s*([^\r\n]+)', text, re.M)
    return float(match[1]) if match else default


def rig_paths(data):
    nodes = data['nodes']

    def path(index):
        node = nodes[index]
        return (path(node['parent']) + '/' + node['name']) if node['parent'] >= 0 else ''

    return {path(index).lstrip('/') for index in range(len(nodes))}


class AnimationCatalog:
    def __init__(self, assets, rigs, destination, write_text, write_json):
        self.assets = assets
        self.destination = destination
        self.write_text = write_text
        self.write_json = write_json
        self.paths = {name: rig_paths(data) for name, data in rigs.items()}
        self.clips = {}
        self.cache = {}
        self.entries = []
        self.unique = {}
        self.skipped = []
        for meta in sorted((assets / 'AnimationClip').glob('*.anim.meta')):
            guid = re.search(r'^guid: (\w+)', meta.read_text('utf-8-sig'), re.M)
            if guid:
                self.clips[guid[1]] = meta.with_suffix('')

    def inspect(self, guid):
        if guid in self.cache:
            return self.cache[guid]
        source = self.clips[guid]
        text = source.read_text('utf-8-sig')
        paths = set(re.findall(r'^    path: ([A-Za-z_][^\r\n]*)$', text, re.M))
        counts = {name: len(paths & allowed) for name, allowed in self.paths.items()}
        rig = max(counts, key=counts.get)
        if counts[rig] == 0:
            rig = 'unresolved'
        info = {'text': text, 'rig': rig, 'paths': paths,
                'duration': scalar(text, 'm_StopTime') - scalar(text, 'm_StartTime'),
                'loop': bool(scalar(text, 'm_LoopTime')), 'hash': hashlib.sha256(source.read_bytes()).hexdigest()}
        self.cache[guid] = info
        return info

    def add(self, guid, label, category, source, settings='', overlay=False, key=None, inferred=False, expected_rig=None, base_key='idle'):
        if guid not in self.clips:
            raise ValueError('Unresolved animation GUID ' + guid + ' in ' + source)
        info = self.inspect(guid)
        rig = expected_rig if info['rig'] == 'unresolved' and expected_rig else info['rig']
        if rig == 'unresolved':
            self.skipped.append({'source': source, 'clip': self.clips[guid].stem, 'reason': 'No matching native rig bindings'})
            return
        speed = scalar(settings, 'speed', 1.0)
        if speed <= 0:
            raise ValueError('Non-positive animation speed in ' + source)
        identity = (guid, speed, overlay, rig)
        if identity in self.unique:
            entry = self.unique[identity]
            entry['aliases'].append(source)
            if not inferred:
                entry['inferredSettings'] = False
            return
        missing = sorted(info['paths'] - self.paths[rig])
        controls = [path for path in missing if path.startswith(('ctrl_', 'CTRL_', 'grp_ctrl', 'cc_', 'IK_'))]
        entry = {'key': key or 'clip-' + str(len(self.entries)).zfill(3), 'resource': guid, 'label': label,
                 'category': category, 'rig': rig, 'source': source, 'sourceGuid': guid, 'sourceHash': info['hash'],
                 'speed': speed, 'fadeDuration': scalar(settings, 'fadeDuration', .2), 'loop': info['loop'],
                 'overlay': overlay, 'baseKey': base_key, 'flight': 'Glide Ability' in self.clips[guid].stem,
                 'authoredDuration': info['duration'], 'matchedBindings': len(info['paths']) - len(missing),
                 'missingPaths': [path for path in missing if path not in controls], 'omittedAuthoringControls': len(controls),
                 'inferredSettings': inferred, 'aliases': [source]}
        self.entries.append(entry)
        self.unique[identity] = entry
        text = re.sub(r'  m_Events:\s*\n.*\Z', '  m_Events: []\n', info['text'], flags=re.S)
        self.write_text(self.destination / (guid + '.anim'), text)

    def keys(self, source, category, require_body=False):
        text = source.read_text('utf-8-sig')
        overlay = bool(scalar(text, '<IsThirdPersonAdditive>k__BackingField'))
        found = False
        for match in re.finditer(r'^  ([^\r\n:]+):\n(.*?)(?=^  \S|\Z)', text, re.M | re.S):
            field, settings = match.groups()
            reference = re.search(r'^    clip: \{fileID: 7400000, guid: (\w+)', settings, re.M)
            if not reference or reference[1] not in self.clips:
                continue
            guid = reference[1]
            if not require_body and 'Glider' not in source.stem and self.inspect(guid)['rig'] != 'arms':
                continue
            found = True
            base = source.stem == 'Default Animation Set Glider'
            key = field if base else None
            name = field.replace('<Animation', '').replace('>k__BackingField', '')
            expected = 'arms' if 'FirstPerson' in field else 'body' if require_body else None
            body_weapon = category == 'weapon' and self.inspect(guid)['rig'] == 'body'
            base_key = field if body_weapon and field in ('idle', 'run', 'jump', 'jumpLoop', 'land', 'slide', 'airJump', 'clamber') else 'idle'
            self.add(guid, source.stem + ' / ' + name, category, source.name + ':' + field, settings,
                     (overlay and 'ThirdPerson' in field) or body_weapon,
                     key, expected_rig=expected, base_key=base_key)
            walking = re.search(r'^    walkingClip: \{fileID: 7400000, guid: (\w+)', settings, re.M)
            if walking and walking[1] != guid:
                speed = scalar(settings, 'speed', 1) * scalar(settings, 'walkSpeedMultiplier', 1)
                walk_settings = re.sub(r'^    speed: [^\n]+', '    speed: ' + str(speed), settings, flags=re.M)
                self.add(walking[1], source.stem + ' / ' + name + ' walking', category, source.name + ':' + field + ':walking',
                         walk_settings, body_weapon, 'walk' if base and field == 'run' else None, base_key='walk' if body_weapon else 'idle')
        if category == 'emote' and not found:
            self.skipped.append({'source': source.name, 'reason': 'Voice-only quip; no animation'})

    def weapon_actions(self):
        assets = {}
        for meta in (self.assets / 'MonoBehaviour').glob('*.asset.meta'):
            match = re.search(r'^guid: (\w+)', meta.read_text('utf-8-sig'), re.M)
            if match:
                assets[match[1]] = meta.with_suffix('')
        for name in ('Fast Shotgun', 'Heavy Shotgun', 'Bounce Shotgun', 'Mini Cannon', 'Plate Launcher', 'Wide Gun', 'SMG'):
            source = self.assets / 'GameObject' / (name + '.prefab')
            text = source.read_text('utf-8-sig')

            def baseline(field, fallback=None):
                match = re.search(r'^  ' + re.escape(field) + r': \{fileID: 11400000, guid: (\w+)', text, re.M)
                if not match:
                    return fallback
                asset = assets[match[1]]
                contents = asset.read_text('utf-8-sig')
                guid = re.search(r'^  idle:\n    clip: \{fileID: 7400000, guid: (\w+)', contents, re.M)[1]
                if not any(entry['rig'] == 'arms' and entry['sourceGuid'] == guid for entry in self.entries):
                    self.keys(asset, 'weapon')
                return next(entry['key'] for entry in self.entries if entry['rig'] == 'arms' and entry['sourceGuid'] == guid)

            idle = baseline('<AnimationSet>k__BackingField')
            aim = baseline('aimAnimationSet', idle)
            actions = []
            for field in ('reloadAnimation', 'inspectAnimation'):
                match = re.search(r'^  ' + field + r':\n(.*?)(?=^  \S|\Z)', text, re.M | re.S)
                if match:
                    actions.append((field.replace('Animation', ''), match[1], False))
            recoil = re.search(r'^  recoilAnimations:\n(.*?)(?=^  [^ -]|\Z)', text, re.M | re.S)
            if recoil:
                for index, match in enumerate(re.finditer(r'^  - clip: (.*?)(?=^  - clip:|\Z)', recoil[1], re.M | re.S)):
                    actions.append(('fire-' + str(index + 1), '    clip: ' + match[1], True))
            for action, settings, additive in actions:
                clip = re.search(r'^    clip: \{fileID: 7400000, guid: (\w+)', settings, re.M)
                if not clip:
                    continue
                variants = [(clip[1], action, idle)]
                if additive:
                    aimed = re.search(r'^    walkingClip: \{fileID: 7400000, guid: (\w+)', settings, re.M)
                    if aimed and aimed[1] != clip[1]:
                        variants.append((aimed[1], action + '-aim', aim))
                for guid, label, base in variants:
                    info = self.inspect(guid)
                    speed = scalar(settings, 'speed', 1)
                    if action == 'reload':
                        speed = info['duration'] / scalar(text, 'reloadDuration', info['duration'])
                    multiplier = scalar(text, 'fireAnimationSpeedMultiplier')
                    if additive and multiplier > 0:
                        speed = info['duration'] / scalar(text, 'fireInterval', info['duration']) * multiplier
                    key = 'weapon-' + name.lower().replace(' ', '-') + '-' + label
                    native_settings = re.sub(r'^    speed: [^\n]+', '    speed: ' + str(speed), settings, flags=re.M)
                    if additive:
                        native_settings = re.sub(r'^    fadeDuration: [^\n]+', '    fadeDuration: 0', native_settings, flags=re.M)
                    self.add(guid, name + ' / ' + label, 'weapon-action', source.name + ':' + label,
                             native_settings, additive, key, expected_rig='arms', base_key=base)
                    entry = self.unique[(guid, speed, additive, 'arms')]
                    entry['additive'] = additive
                    entry['loop'] = False
                    entry['weaponKey'] = name.lower().replace(' ', '-')

    def build(self):
        self.destination.mkdir(exist_ok=True)
        folder = self.assets / 'MonoBehaviour'
        self.keys(folder / 'Default Animation Set Glider.asset', 'movement', True)
        self.keys(folder / 'Default Animation Set Glider Crouching.asset', 'crouch', True)
        for source in sorted(folder.glob('*.asset')):
            if 'Glider' in source.stem and source.stem.startswith('Default Animation Set'):
                continue
            if 'Animation Set' in source.stem:
                self.keys(source, 'weapon', 'Glider' in source.stem)
            elif 'Glider' in source.stem:
                text = source.read_text('utf-8-sig')
                if 'guid: 3d111fd81dda0b302e2060952c20d04d' in text and re.search(r'^  character: 3$', text, re.M):
                    self.keys(source, 'emote', True)
        character = (folder / 'Glider.asset').read_text('utf-8-sig')
        for field, guid in re.findall(r'^  (MeleeAnimation\w+): \{fileID: 7400000, guid: (\w+)', character, re.M):
            self.add(guid, 'Glider / ' + field, 'melee', 'Glider.asset:' + field)
        referenced = {e['sourceGuid'] for e in self.entries}
        for guid, source in sorted(self.clips.items(), key=lambda pair: pair[1].name):
            if 'glider' in source.stem.lower() and guid not in referenced:
                self.add(guid, source.stem, 'additional', source.name, inferred=True)
        self.weapon_actions()
        self.write_json(self.destination.parent / 'animations.json', {'clips': self.entries})
        self.write_json(self.destination.parent / 'animation-coverage.json', {'clips': len(self.entries),
            'distinctResources': len({e['sourceGuid'] for e in self.entries}), 'skipped': self.skipped})
        return self.entries


def physics_profile(assets):
    settings = assets.parent / 'ProjectSettings'
    time = (settings / 'TimeManager.asset').read_text('utf-8-sig')
    physics = (settings / 'DynamicsManager.asset').read_text('utf-8-sig')
    count = scalar(time, 'm_Count')
    rate = scalar(time, 'm_Numerator') / scalar(time, 'm_Denominator', 1)
    value = {'fixedDeltaTime': count / rate, 'maximumDeltaTime': scalar(time, 'Maximum Allowed Timestep'),
             'maximumParticleDeltaTime': scalar(time, 'Maximum Particle Timestep')}
    for field in ['DefaultContactOffset', 'DefaultSolverIterations', 'DefaultSolverVelocityIterations', 'BounceThreshold',
                  'SleepThreshold', 'DefaultMaxDepenetrationVelocity', 'AutoSyncTransforms', 'QueriesHitBackfaces',
                  'QueriesHitTriggers', 'ClothInterCollisionDistance', 'ClothInterCollisionStiffness',
                  'ClothInterCollisionSettingsToggle', 'BroadphaseType', 'SimulationMode']:
        value[field[0].lower() + field[1:]] = scalar(physics, 'm_' + field)
    for field in ['Gravity', 'ClothGravity']:
        vector = re.search(r'^  m_' + field + r': \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}', physics, re.M)
        value[field[0].lower() + field[1:]] = {'x': float(vector[1]), 'y': float(vector[2]), 'z': float(vector[3])}
    value['sourceHashes'] = [{'path': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
                             for path in [settings / 'TimeManager.asset', settings / 'DynamicsManager.asset']]
    quality = (settings / 'QualitySettings.asset').read_text('utf-8-sig')
    value['sourceHashes'].append({'path': 'QualitySettings.asset', 'sha256': hashlib.sha256((settings / 'QualitySettings.asset').read_bytes()).hexdigest()})
    index = int(scalar(quality, 'm_CurrentQuality'))
    levels = re.split(r'^  - serializedVersion:', quality, flags=re.M)[1:]
    value['skinWeights'] = scalar(levels[index], 'skinWeights', 4)
    value['qualitySource'] = re.search(r'^    name: (.*)$', levels[index], re.M)[1]
    return value
