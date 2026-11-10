import hashlib
import json
import struct
import subprocess
import tomllib
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / 'thunderstore'
manifest = json.loads((PACKAGE / 'manifest.json').read_text('utf8'))
config = tomllib.loads((PACKAGE / 'thunderstore.toml').read_text('utf8'))
version = ET.parse(ROOT / 'MikuGlider.csproj').findtext('./PropertyGroup/Version')
assert manifest['version_number'] == config['package']['versionNumber'] == version
assert manifest['name'] == config['package']['name']
assert manifest['description'] == config['package']['description']
assert manifest['website_url'] == config['package']['websiteUrl']
assert manifest['dependencies'] == [f'{key}-{value}' for key, value in config['package']['dependencies'].items()]
assert len(manifest['description']) <= 250
assert not (PACKAGE / 'CHANGELOG.md').read_text('utf8').strip()
icon = (PACKAGE / 'icon.png').read_bytes()
assert icon[:8] == b'\x89PNG\r\n\x1a\n'
assert struct.unpack('>II', icon[16:24]) == (256, 256)
subprocess.run(['dotnet', 'build', str(ROOT / 'MikuGlider.csproj'), '-c', 'Release', '--nologo'], cwd=ROOT, check=True)
entries = {
    'manifest.json': PACKAGE / 'manifest.json',
    'README.md': PACKAGE / 'README.md',
    'icon.png': PACKAGE / 'icon.png',
    'CHANGELOG.md': PACKAGE / 'CHANGELOG.md',
    'BepInEx/plugins/MikuGlider.dll': ROOT / 'bin/Release/MikuGlider.dll',
}
release = ROOT / 'release'
release.mkdir(exist_ok=True)
target = release / f'MikuGlider-{version}-Thunderstore.zip'
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name, source in entries.items():
        archive.write(source, name)
with zipfile.ZipFile(target) as archive:
    assert archive.testzip() is None
    assert set(archive.namelist()) == set(entries)
    for name, source in entries.items():
        assert archive.read(name) == source.read_bytes()
report = {
    'package': target.name,
    'version': version,
    'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
    'bytes': target.stat().st_size,
    'files': {name: hashlib.sha256(source.read_bytes()).hexdigest() for name, source in entries.items()},
    'iconSize': [256, 256],
    'changelogEmpty': True,
}
(release / f'{version}-thunderstore-validation.json').write_text(json.dumps(report, indent=2), encoding='utf8')
print(json.dumps(report, indent=2))
