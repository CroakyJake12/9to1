#!/usr/bin/env python3
"""Verify observed actual current Mail source, native runtime and original TRX."""
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE = '89cd4e4641e482658a0e24be9b90e0a8324fd4a7'
REPO = Path('/workspace/c1-mail-original-local-controls')
OUTPUT = Path('/tmp/team-c-c5-mail-original-89cd-20261004')
DIAG = OUTPUT / 'diagnostics'
RECEIPTS = Path('/workspace/team-c-resume-evidence/c5-mail-original-89cd')


def sha(body):
    return hashlib.sha256(body).hexdigest()


def read(name):
    return json.loads((DIAG / name).read_text())


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO)


assert git('rev-parse', 'HEAD').decode().strip() == SOURCE
assert git('status', '--porcelain') == b''
before_bytes = (DIAG / 'source-pins-before.json').read_bytes()
assert before_bytes == (DIAG / 'source-pins-after.json').read_bytes()
before = json.loads(before_bytes)
assert len(before) == len({r['path'] for r in before}) == 489
tree = {}
for record in git('ls-tree', '-rz', SOURCE).split(b'\0'):
    if record:
        head, path = record.split(b'\t', 1)
        mode, kind, blob = head.decode().split()
        tree[path.decode()] = (mode, kind, blob)
data = subprocess.check_output(['git', 'cat-file', '--batch'], cwd=REPO,
    input=('\n'.join(tree[r['path']][2] for r in before) + '\n').encode())
offset = 0
for row in before:
    mode, kind, blob = tree[row['path']]
    assert kind == 'blob' and mode in ('100644', '100755')
    end = data.index(b'\n', offset)
    actual_blob, actual_kind, size = data[offset:end].decode().split()
    offset = end + 1
    size = int(size)
    body = data[offset:offset + size]
    assert actual_blob == blob and actual_kind == kind
    assert len(body) == row['bytes'] and sha(body) == row['sha256']
    offset += size
    assert data[offset:offset + 1] == b'\n'
    offset += 1
assert offset == len(data)

result = read('result.json')
assert result['sourceCommit'] == SOURCE and result['status'] == 'PASS' and result['accepted']
assert result['originalFact'] == {'executed':1, 'passed':1, 'failed':0, 'skipped':0}
assert result['distinctNativeScenarios'] == 3 and result['nativePhaseExecutions'] == 6
assert read('original-trx-gate.json')['accepted'] and read('discovery-gate.json')['accepted']
trx_path = OUTPUT / 'test-results/mail-original-draft.trx'
trx = ET.parse(trx_path).getroot()
ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = trx.findall('.//t:UnitTestResult', ns)
assert len(tests) == 1 and tests[0].attrib['outcome'] == 'Passed'

commands = read('commands.json')
assert len(commands) == 16
for command in commands:
    assert command['exit'] == command['exitAfterDrain'] == 0
    assert command['normalEOF'] and command['familyClosed'] and command['finalECHILD']
    assert command['signals'] == [] and command['error'] is None
    assert all(b['gone'] for b in command['births'])
native = [c for c in commands if c['name'] in ('native-seed', 'native-verify')]
assert len(native) == 2 and all(len(c['births']) == 1 for c in native)
assert native[0]['births'][0]['pid'] != native[1]['births'][0]['pid']
runtime_before_bytes = (DIAG / 'runtime-before.json').read_bytes()
assert runtime_before_bytes == (DIAG / 'runtime-after.json').read_bytes()
runtime = json.loads(runtime_before_bytes)
target = Path(read('target-properties.json')['TargetPath'])
assert len(runtime) == 11
for row in runtime:
    path = target.parent / row['path']
    assert path.resolve().is_relative_to(target.parent.resolve()) and not path.is_symlink()
    body = path.read_bytes()
    assert len(body) == row['bytes'] and sha(body) == row['sha256']
assert target.read_bytes()[:2] == b'MZ'
assert target.with_suffix('').read_bytes()[:5] == b'\x7fELF\x02'
for log in ['build.log', 'build-probe.log']:
    text = (DIAG / log).read_text()
    assert 'Build succeeded.' in text and '0 Warning(s)' in text and '0 Error(s)' in text
phases = {}
for phase in ['seed', 'verify']:
    value = read(phase + '-results.json')
    assert (value['declared'], value['executed'], value['passed'], value['failed'], value['notRun'], value['exitCode']) == (3,3,3,0,0,0)
    assert all(r['state'] == 'PASS' and r['assertions'] > 0 for r in value['outcomes'])
    phases[phase] = {'executed':3, 'passed':3, 'failed':0, 'notRun':0,
        'assertions':sum(r['assertions'] for r in value['outcomes']),
        'processId':next(c for c in native if c['name'] == 'native-' + phase)['births'][0]['pid']}
receipt = {
    'state':'ACK_FIRST_ACTUAL_MAINTAINED_MAIL_LOCAL_DURABLE_CONTROLS_VERIFIED',
    'sourceCommit':SOURCE, 'driverExitCode':0,
    'sourceInputs':489, 'allSourceBodiesEqualActualGit':True,
    'sourceBeforeAfterExact':True, 'sourceSnapshotSha256':sha(before_bytes),
    'originalFact':{'actualName':tests[0].attrib['testName'], 'discovered':1, 'executed':1, 'passed':1, 'failed':0, 'skipped':0,
                    'trxBytes':trx_path.stat().st_size, 'trxSha256':sha(trx_path.read_bytes())},
    'distinctNativeScenarios':3, 'nativePhaseExecutions':6, 'phases':phases,
    'nativeRuntimeFiles':11, 'nativeRuntimeBeforeAfterAndActualBodiesEqual':True,
    'runtimeSnapshotSha256':sha(runtime_before_bytes),
    'actualSourceBuilds':2, 'buildWarnings':0, 'buildErrors':0,
    'commandFamilies':16, 'allNormalEOF_ECHILD_FamilyClosed_NoSignals':True,
    'qualifications':[
        'Actual unchanged FileMailDraftStore and original Fact are source linked against whole current Haven.Application/Core/CUI.AI projects; no source/backend/validator substitution.',
        'Three distinct local storage scenarios execute in seed then separate fresh verify process. The six phase executions are not six distinct scenarios.',
        'No provider/send/browser/Windows/Mail full parity/revision-CAS/backend authority acceptance is established.',
        'Controlled draft and attachment bytes, fixture identity receipt, native binaries/packages/caches remain private task outputs and are not published.',
        'Original Space-key failure and held renderer/owner-factory gates remain preserved separately.'
    ]
}
(RECEIPTS / 'verification.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps({k:receipt[k] for k in ['state','sourceCommit','sourceInputs','originalFact','phases','commandFamilies']},indent=2))
