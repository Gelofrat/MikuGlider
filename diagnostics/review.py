import argparse
import json
import gzip
import math
import re
from pathlib import Path


def records(path):
    if not path.exists():
        return []
    result = []
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        try:
            result.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return result


def build(directory):
    directory = directory.resolve(strict=True)
    frames = records(directory / 'frames.jsonl')
    images = records(directory / 'images.jsonl')
    summaries = []
    for frame in frames:
        cloth = frame.get('cloth') or {}
        geometry = cloth.get('geometry') or {}
        character = frame.get('character') or {}
        renderers = character.get('renderers') or []
        summaries.append({
            'sample': frame['sample'],
            'unityFrame': frame['unityFrame'],
            'realtime': frame['realtime'],
            'skin': cloth.get('skin') or character.get('skinMesh'),
            'motion': cloth.get('motion') or character.get('motion'),
            'state': cloth.get('state'),
            'coordinatesReliable': cloth.get('coordinatesReliable', False),
            'geometryReliable': geometry.get('surfaceReliable', False),
            'checkedEdges': geometry.get('checkedEdges', 0),
            'totalEdges': geometry.get('totalEdges', 0),
            'geometryRealtime': geometry.get('sampledAt', -1),
            'geometryMilliseconds': geometry.get('analysisMilliseconds', 0),
            'marker': frame.get('marker'),
            'issues': cloth.get('issues', 'None'),
            'penetration': cloth.get('maximumPenetration', 0),
            'speed': cloth.get('maximumRelativeSpeed', 0),
            'acceleration': cloth.get('maximumRelativeAcceleration', 0),
            'bodyCrossings': geometry.get('bodyCrossings', 0),
            'coatCrossings': geometry.get('coatCrossings', 0),
            'selfCrossings': geometry.get('selfCrossings', 0),
            'diagnosticMilliseconds': frame.get('diagnosticMilliseconds', 0),
            'perspective': character.get('perspective'),
            'weapon': character.get('weapon'),
            'clip': next(iter(character.get('animations') or [{}])).get('clip') or cloth.get('clip'),
            'capturePhase': character.get('phase'),
            'geometryPath': character.get('geometryPath'),
            'captureError': character.get('error'),
            'rendererCount': len(renderers),
            'missingGeometry': sum(bool(r.get('error')) or r.get('geometryStatus') in ('vertex_budget', 'writer_rejected', 'metadata_rejected', 'unreadable_mesh') for r in renderers),
            'estimatedMissedSamples': frame.get('estimatedMissedSamples', 0),
            'droppedJobs': frame.get('droppedJobs', 0),
            'skippedImages': frame.get('skippedImages', 0),
            'sampleInterval': frame.get('sampleInterval', 0),
            'characterMilliseconds': character.get('captureMilliseconds', 0),
            'transitions': frame.get('transitions') or []
        })
    try:
        previous = json.loads((directory / 'summary.json').read_text(encoding='utf8'))
    except (OSError, json.JSONDecodeError):
        previous = {}
    data = {
        'schema': 2,
        'stopReason': 'recovered_from_jsonl',
        'droppedJobs': previous.get('droppedJobs'),
        'skippedImages': previous.get('skippedImages'),
        'suppressedEvents': previous.get('suppressedEvents'),
        'writtenBytes': sum(p.stat().st_size for p in directory.rglob('*') if p.is_file()),
        'frames': summaries,
        'images': images,
        'events': records(directory / 'events.jsonl')
    }
    encoded = json.dumps(data, ensure_ascii=True, separators=(',', ':')).replace('<', '\\u003c').replace('>', '\\u003e').replace('&', '\\u0026')
    template = Path(__file__).with_name('viewer.html').read_text(encoding='utf8')
    (directory / 'summary.json').write_text(encoded, encoding='utf8')
    (directory / 'index.html').write_text(template.replace('__CAPTURE_DATA__', encoded), encoding='utf8')
    print(f'Recovered {len(frames)} telemetry samples and {len(images)} image records: {directory / "index.html"}')


def local_file(directory, relative):
    path = (directory / relative).resolve(strict=True)
    if not path.is_relative_to(directory.resolve()):
        raise ValueError('Capture file escapes the session directory')
    return path


def read_archive(path):
    with gzip.open(path, 'rt', encoding='utf8') as stream:
        return json.load(stream)


def point(matrix, vertex):
    return [sum(matrix[row * 4 + column] * vertex[column] for column in range(3)) + matrix[row * 4 + 3] for row in range(3)]


def normal(matrix, vector):
    a, b, c, d, e, f, g, h, i = (matrix[n] for n in (0, 1, 2, 4, 5, 6, 8, 9, 10))
    cofactor = (e*i-f*h, f*g-d*i, d*h-e*g, c*h-b*i, a*i-c*g, b*g-a*h, b*f-c*e, c*d-a*f, a*e-b*d)
    determinant = a * cofactor[0] + b * cofactor[1] + c * cofactor[2]
    if abs(determinant) < 1e-12:
        raise ValueError('Singular renderer transform')
    value = [sum(cofactor[row * 3 + column] * vector[column] for column in range(3)) / determinant for row in range(3)]
    length = math.sqrt(sum(v*v for v in value))
    return [v / length for v in value] if length > 1e-12 else [0, 1, 0]


def export_obj(directory, sample):
    directory = directory.resolve(strict=True)
    frame = next((f for f in records(directory / 'frames.jsonl') if f['sample'] == sample), None)
    relative = (frame or {}).get('character', {}).get('geometryPath')
    if not relative:
        raise ValueError(f'Sample {sample} has no geometry snapshot. Choose a sample with geometryPath, or record F8.')
    pose = read_archive(local_file(directory, relative))
    if pose.get('unityFrame') != frame['unityFrame'] or pose.get('sample') != sample:
        raise ValueError('Geometry/frame identity mismatch')
    output = directory / 'exports' / f'sample-{sample:06d}'
    output.mkdir(parents=True, exist_ok=True)
    for surface in pose['surfaces']:
        binding = surface['binding']
        asset = read_archive(local_file(directory, f'renderers/{binding}.json.gz'))
        if len(surface['vertices']) != len(asset['vertices']):
            raise ValueError('Topology does not match the captured pose')
        label = re.sub(r'[^a-zA-Z0-9_-]', '_', surface['role'] + '-' + binding)
        vertices = surface['vertices']
        matrix = surface['localToWorld']
        uv = asset.get('uv') or []
        normals = surface.get('normals') or []
        if any(not isinstance(value, (int, float)) or not math.isfinite(value) for row in vertices for value in row):
            raise ValueError('Non-finite geometry must be inspected in JSON')
        with (output / f'{label}.obj').open('w', encoding='utf8') as stream:
            stream.write(f'o {label}\n')
            for vertex in vertices:
                stream.write('v ' + ' '.join(format(v, '.9g') for v in point(matrix, vertex)) + '\n')
            if len(uv) == len(vertices):
                for value in uv:
                    stream.write('vt ' + ' '.join(format(v, '.9g') for v in value) + '\n')
            if len(normals) == len(vertices):
                for value in normals:
                    stream.write('vn ' + ' '.join(format(v, '.9g') for v in normal(matrix, value)) + '\n')
            for submesh, triangles in enumerate(asset['submeshes']):
                stream.write(f'g material_{submesh}\n')
                if len(triangles) % 3 or any(index < 0 or index >= len(vertices) for index in triangles):
                    raise ValueError('Invalid triangle indices')
                for offset in range(0, len(triangles), 3):
                    indices = [index + 1 for index in triangles[offset:offset+3]]
                    def corner(index):
                        tex = str(index) if len(uv) == len(vertices) else ''
                        norm = str(index) if len(normals) == len(vertices) else ''
                        return f'{index}/{tex}/{norm}' if norm else f'{index}/{tex}' if tex else str(index)
                    stream.write('f ' + ' '.join(corner(index) for index in indices) + '\n')
    (output / 'capture.json').write_text(json.dumps({'sample': sample, 'unityFrame': frame['unityFrame'], 'camera': frame['character'].get('camera'), 'space': 'Unity world metres, left-handed, Y up; preserve axes on import', 'textures': 'Metadata only; texture pixels and shaders are not exported.'}, ensure_ascii=False, indent=2), encoding='utf8')
    print(f'Exported {len(pose["surfaces"])} surfaces: {output}')
    return output


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Rebuild a local viewer after an interrupted diagnostic recording.')
    parser.add_argument('capture', type=Path)
    parser.add_argument('--export-obj', type=int, metavar='SAMPLE', help='Export a recorded mesh snapshot in Unity world coordinates')
    args = parser.parse_args()
    if args.export_obj is None:
        build(args.capture)
    else:
        export_obj(args.capture, args.export_obj)
