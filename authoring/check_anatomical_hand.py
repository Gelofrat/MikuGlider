"""Run the experimental hand rig through Unity and Blender MCP, fail closed.

Requires the existing local simulation project, extracted reference assets and
the three diagnostic recordings. Never installs a candidate in assets or game.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'authoring/anatomical-hand'
CASES = [('v1', '20261116-180944-759-d974ee'),
         ('v1', '20261116-162906-805-0aca39'),
         ('v3', '20261116-181646-129-73fa08')]
CAPTURES = Path('C:/Apps/Steam/steamapps/common/Mycopunk/BepInEx/MikuGliderDiagnostics/captures')


def run(command, **kwargs):
    result = subprocess.run(command, cwd=ROOT, text=True, encoding='utf-8',
                            errors='replace', stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=900, **kwargs)
    if result.returncode:
        raise RuntimeError(result.stderr[-3000:] or result.stdout[-3000:])
    return result.stdout


def unity(code, label):
    request = OUTPUT / ('mcp-' + label + '-request.json')
    response = OUTPUT / ('mcp-' + label + '-result.json')
    request.write_text(json.dumps({'tool': 'execute_code', 'arguments': {
        'action': 'execute', 'code': code}}), encoding='utf-8')
    run([str(ROOT / 'tools/unity-mcp-env/Scripts/python.exe'),
         'tools/unity_stdio.py', str(request), str(response)])
    result = json.loads(response.read_text(encoding='utf-8-sig'))['structuredContent']
    if not result.get('success'):
        raise RuntimeError('Unity ' + label + ': ' + json.dumps(result))
    return result['data']['result']


def blender(code):
    env = dict(os.environ, BLENDER_MCP_TIMEOUT='900')
    result = json.loads(run([sys.executable, 'tools/blender_session.py'], input=code, env=env))
    if result.get('status') != 'success':
        raise RuntimeError('Blender: ' + json.dumps(result))
    return result


def fingerprints(captures):
    files = [ROOT / p for p in (
        'src/Animation/AnatomicalHandRig.cs', 'src/Animation/HandKinematics.cs',
        'src/Animation/ModelPoseCorrections.cs', 'src/Assets/HandRigDefinition.cs',
        'src/Assets/ModelCorrections.cs', 'simulation/Package/Editor/LabHandRigChecks.cs',
        'simulation/Package/Editor/LabThumbReplay.cs',
        'authoring/build_anatomical_hand.py', 'authoring/review_anatomical_hand.py',
        'authoring/check_anatomical_hand.py', 'authoring/audit_hand_surface.py',
        'authoring/check_thumb_return.py', 'authoring/thumb_contact.py')]
    for variant, folder in [('v1', 'V1'), ('v3', 'v3')]:
        files += list((ROOT / 'assets' / folder).glob('*.mikumesh'))
        files += [ROOT / 'assets' / folder / 'corrections.json']
        files += [OUTPUT / (variant + suffix) for suffix in (
            '-corrections.json', '-original-corrections.json', '-baseline.json', '-nails.json')]
    hashes = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    for _, capture in CASES:
        hashes['captures/' + capture] = hashlib.sha256((captures / capture / 'frames.jsonl').read_bytes()).hexdigest()
    return hashes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--captures', type=Path, default=CAPTURES)
    parser.add_argument('--prepare', action='store_true', help='Re-export baseline and build Blender candidates first')
    args = parser.parse_args()
    OUTPUT.mkdir(exist_ok=True)
    acceptance = OUTPUT / 'acceptance.json'
    report = {'recordedReplayPassed': False, 'anatomyStatus': 'incomplete'}
    acceptance.write_text(json.dumps(report, indent=2), encoding='utf-8')
    try:
        print('Synchronizing the Unity stand', flush=True)
        run([sys.executable, 'simulation/sync.py', '--code-only'])
        # Force import after copying source files. Compilation can span a domain reload.
        request = OUTPUT / 'mcp-refresh-request.json'
        request.write_text(json.dumps({'tool': 'refresh_unity', 'arguments': {
            'mode': 'force', 'scope': 'all', 'compile': 'request', 'wait_for_ready': True}}), encoding='utf-8')
        run([str(ROOT / 'tools/unity-mcp-env/Scripts/python.exe'), 'tools/unity_stdio.py',
             str(request), str(OUTPUT / 'mcp-refresh-result.json')])
        for attempt in range(30):
            try:
                unity('return UnityEngine.Application.unityVersion;', 'ready')
                break
            except RuntimeError:
                if attempt == 29:
                    raise
                time.sleep(2)
        folder = json.dumps(OUTPUT.as_posix())
        captures = json.dumps(args.captures.resolve().as_posix())
        if args.prepare:
            print('Exporting current baseline; calibrating through Blender MCP', flush=True)
            code = ''
            for variant, side, capture in [('v1', 'left', CASES[0][1]), ('v3', 'right', CASES[2][1])]:
                code += 'Horse.MikuPhysicsLab.Editor.LabThumbReplay.Export(' + ','.join([
                    json.dumps(variant), json.dumps((args.captures / capture).as_posix()), '0',
                    json.dumps((OUTPUT / (variant + '-baseline.json')).as_posix()),
                    'null', 'null', 'true', json.dumps(side)]) + ');'
            unity(code + 'return "Baselines exported";', 'baseline-export')
            blender('import runpy\nn=runpy.run_path(' + repr(str(ROOT / 'authoring/build_anatomical_hand.py')) + ')\n'
                    + 'n["build"].__globals__["CAPTURES"]=n["Path"](' + repr(str(args.captures)) + ')\nn["build"]()\n')
        before = fingerprints(args.captures)
        print('Checking kinematics, schema, rebinds and recorded motion in Unity MCP', flush=True)
        cls = 'Horse.MikuPhysicsLab.Editor.LabHandRigChecks.'
        unity(cls + 'MathChecks(' + json.dumps((OUTPUT / 'math-checks.json').as_posix()) + ');'
              + cls + 'BindingChecks(' + folder + ');'
              + cls + 'ExportCandidates(' + folder + ',' + captures + ');'
              + cls + 'Replay(' + folder + ',' + captures + ');'
              + cls + 'Replay(' + folder + ',' + captures + ',true);'
              + 'return ' + cls + 'SchemaChecks(' + folder + ');', 'checks')
        print('Checking every recorded hand surface in Blender MCP', flush=True)
        blender('import runpy\nn=runpy.run_path(' + repr(str(ROOT / 'authoring/review_anatomical_hand.py')) + ')\n'
                + 'n["replay_surfaces"].__globals__["CAPTURES"]=n["Path"](' + repr(str(args.captures)) + ')\nn["replay_surfaces"](True)\n')
        if before != fingerprints(args.captures):
            raise RuntimeError('Inputs changed while checks were running; results cannot be accepted')
        runtime = json.loads((OUTPUT / 'replay-checks.json').read_text())
        surfaces = json.loads((OUTPUT / 'replay-surfaces.json').read_text())
        candidates = [r for r in surfaces if not r['legacy']]
        if [(r['variant'], r['capture']) for r in candidates] != CASES or len(runtime) != len(CASES):
            raise RuntimeError('Incomplete recording coverage')
        for run_result, surface in zip(runtime, candidates):
            if run_result['frames'] != surface['frames'] or surface['frames'] < 2:
                raise RuntimeError('Incomplete frame coverage')
        passed = all(r['passed'] for r in runtime) and all(r['selfFrames'] == 0 and r['contactFrames'] == 0 for r in candidates)
        report.update(recordedReplayPassed=passed, anatomyStatus='requires_extended_review' if passed else 'failed',
                      inputSha256=before, frames=sum(r['frames'] for r in runtime),
                      runtime=[{k:v for k,v in r.items() if k!='timeline'} for r in runtime],
                      surfaces=[{k:v for k,v in r.items() if k!='rows'} for r in surfaces],
                      limitations=['Only the listed v1 left / Plate Launcher and v3 right / Gunship recordings are covered.',
                                   'Triangle tests exclude tangent, adjacent and sub-tolerance contacts; they are not continuous collision detection.',
                                   'No approval of other weapons, v2, third-person hands, visual anatomy or interrupted live transitions.'])
        acceptance.write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps({k:report[k] for k in ('recordedReplayPassed','anatomyStatus','frames')}), flush=True)
        return 0 if passed else 2
    except Exception as error:
        report['error'] = str(error)
        acceptance.write_text(json.dumps(report, indent=2), encoding='utf-8')
        raise


if __name__ == '__main__':
    sys.exit(main())
