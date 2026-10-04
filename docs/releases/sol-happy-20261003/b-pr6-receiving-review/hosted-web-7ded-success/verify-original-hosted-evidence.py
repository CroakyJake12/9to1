#!/usr/bin/env python3
"""Read-only verification of original source-bound hosted diagnostics."""
import hashlib
import json
import re
import subprocess
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/global-web-7ded-success')
REPO = Path('/workspace/team-c-integration')
SOURCE = '7ded080066ec4b066519c2fb1848fec5c66fba06'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads((ROOT / path).read_text())


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO)


run = read('run.json')
assert run['headSha'] == SOURCE and run['conclusion'] == 'success'
before_bytes = (ROOT / 'publisher/source-before.json').read_bytes()
assert before_bytes == (ROOT / 'publisher/source-after.json').read_bytes()
assert before_bytes == (ROOT / 'publisher/source-after-final.json').read_bytes()
before = json.loads(before_bytes)
assert len({r['path'] for r in before}) == len(before)
tree = {}
for row in git('ls-tree', '-rz', SOURCE).split(b'\0'):
    if row:
        header, path = row.split(b'\t', 1)
        mode, kind, blob = header.decode().split()
        tree[path.decode()] = (mode, kind, blob)
for row in before:
    mode, kind, blob = tree[row['path']]
    assert kind == 'blob' and blob == row['gitBlob']
    assert mode in ('100644', '100755')
data = subprocess.check_output(['git', 'cat-file', '--batch'], cwd=REPO,
    input=('\n'.join(r['gitBlob'] for r in before) + '\n').encode())
offset = 0
for row in before:
    end = data.index(b'\n', offset)
    blob, kind, size = data[offset:end].decode().split()
    offset = end + 1
    size = int(size)
    body = data[offset:offset + size]
    assert (blob, kind, size, sha(body)) == (row['gitBlob'], 'blob', row['bytes'], row['sha256'])
    offset += size
    assert data[offset:offset + 1] == b'\n'
    offset += 1
assert offset == len(data)

publisher = read('publisher/result.json')
assert publisher['status'] == 'PASS' and publisher['sourceCommit'] == SOURCE
runtime = read('publisher/actual-runtime-and-native-inputs.json')
assert runtime['runtimeVersion'] == '10.0.12'
assert {Path(r['path']).name for r in runtime['nativeLibraries']} == {'libSkiaSharp.a', 'libHarfBuzzSharp.a'}
assert (ROOT / 'publisher/dotnet-version.log').read_text().strip() == '10.0.401'
raw = read('browser/raw/results.json')
result = read('browser/diagnostics/result.json')
counts = {'discovered': 9, 'executed': 9, 'passed': 9, 'failed': 0, 'notRun': 0}
assert result['sourceCommit'] == SOURCE and result['state'] == 'PASS_SCOPED_NINE'
assert raw['counts'] == result['counts'] == counts
assert raw['exit'] == 0 and raw['acceptance'] == 'PASS_SCOPED_NINE'
assert raw['integrity']['state'] == 'PASS' and raw['portRebind']
assert sum(raw['diagnostics']['counts'].values()) == 0
assert len(raw['outcomes']) == 9 and all(r['state'] == 'PASS' for r in raw['outcomes'].values())
assert raw['candidate']['sourceCommit'] == SOURCE
assert raw['candidate']['manifestSHA256'] == publisher['seal']['receiptSha256']
assert raw['candidate']['sourceBeforeSHA256'] == raw['candidate']['sourceAfterSHA256'] == sha(before_bytes)
assert raw['bundleInventoryBefore'] == raw['bundleInventoryAfter']
assert len(raw['bundleInventoryBefore']) == publisher['seal']['fileCount'] == raw['candidate']['assets']
assert sum(r['bytes'] for r in raw['bundleInventoryBefore']) == publisher['seal']['totalBytes'] == raw['candidate']['assetBytes']
assert len(raw['launches']) == len(raw['closedLaunches']) == 2
assert len({r['browserPID'] for r in raw['launches']}) == 2
assert all(r['contextCloseResolved'] for r in raw['closedLaunches'])
assert (ROOT / 'browser/diagnostics/playwright-version.log').read_text().strip() == '1.62.0'
for path, key in [('apps/Web/Tests/PictureBrowser/run-picture-browser.cjs', 'runner'),
                  ('apps/Web/Tests/PictureBrowser/png-oracle.cjs', 'oracle')]:
    actual = sha(git('show', SOURCE + ':' + path))
    assert actual == raw[key + 'SHA256AtStart'] == raw[key + 'SHA256AtEnd']
fixture_bytes = git('show', SOURCE + ':apps/Web/Tests/PictureAdmission/Fixtures/Picture8x6.png')
assert raw['fixture']['sha256'] == sha(fixture_bytes) and raw['fixture']['bytes'] == len(fixture_bytes)
# picture-original.png is the actual browser screenshot, not the 8x6 picker input.

families = []
for label in ['publisher', 'browser/diagnostics']:
    commands = read(label + '/commands.json')
    for command in commands:
        assert command['exit'] == command['exitAfterDrain'] == 0
        assert command['normalEOF'] and command['familyClosed'] and command['finalECHILD']
        assert command['signals'] == [] and command['error'] is None
        assert all(r['gone'] for r in command['births'])
    families.extend(commands)
browser_command = next(c for c in read('browser/diagnostics/commands.json') if c['name'] == 'picture-browser-nine')
assert {r['browserPID'] for r in raw['launches']} <= {r['pid'] for r in browser_command['births']}

trace_rows = []
for path in sorted((ROOT / 'browser/raw').glob('*.zip')):
    with zipfile.ZipFile(path) as archive:
        infos = archive.infolist()
        assert len({i.filename for i in infos}) == len(infos) and archive.testzip() is None
        assert sum(i.file_size for i in infos) < 64 * 1024 * 1024
        assert all(not PurePosixPath(i.filename).is_absolute() and '..' not in PurePosixPath(i.filename).parts for i in infos)
        network = [json.loads(line) for line in archive.read('trace.network').decode().splitlines()]
        urls = sorted({line['snapshot']['request']['url'] for line in network})
        assert all(url.startswith('http://127.0.0.1:18761/') for url in urls)
        headers = [h for line in network for h in line['snapshot']['request']['headers']]
        assert not any(h['name'].lower() in ('authorization', 'cookie') and h['value'] for h in headers)
        assert not any(re.search(r'[?&](?:access_token|refresh_token|id_token|client_secret|code|sig)=', url, re.I) for url in urls)
        trace_rows.append({'path':str(path.relative_to(ROOT)), 'bytes':path.stat().st_size, 'sha256':sha(path.read_bytes()),
                           'members':len(infos), 'uncompressedBytes':sum(i.file_size for i in infos),
                           'networkRequests':len(network), 'allNetworkRequestsToControlledLoopback':True,
                           'authorizationOrCookieHeaders':False})

warning_codes = {}
for log in ['build.log', 'publish.log']:
    text = (ROOT / 'publisher' / log).read_text()
    warning_codes[log] = sorted(set(re.findall(r'warning ([A-Z]+\d+)', text)))
qualification = {
    'state':'ACK_ORIGINAL_HOSTED_SOURCE_BUILD_AND_BROWSER_NINE_VERIFIED',
    'sourceCommit':SOURCE, 'runId':37214417698,
    'publisherArtifactId':11307853142, 'browserArtifactId':11308306040,
    'sourceInputs':len(before), 'allSourceInputsEqualActualGitBodies':True,
    'sourceBeforeAfterFinalByteEqual':True, 'sourceSnapshotSha256':sha(before_bytes),
    'sdk':'10.0.401', 'runtimeVersion':runtime['runtimeVersion'], 'nativeInputs':runtime['nativeLibraries'],
    'publisherSealReported':publisher['seal'], 'publishFileCount':len(raw['bundleInventoryBefore']),
    'publishBytes':sum(r['bytes'] for r in raw['bundleInventoryBefore']),
    'browserCounts':counts, 'actualChromiumVersion':result['actualChromiumVersion'],
    'distinctBrowserProcesses':2, 'closedContexts':2, 'portRebind':True, 'browserDiagnostics':0,
    'verifiedCommandFamilies':len(families), 'allNormalEOF_ECHILD_FamilyClosed_NoSignals':True,
    'actualBrowserPidsIncludedInCustodianBirths':True, 'tracePrivacyAndIntegrity':trace_rows,
    'warningCodes':warning_codes,
    'limitations':[
        'The original workflow uploads diagnostics and controlled traces only; actual sealed publish-manifest.json/wwwroot.zip/seal.json were consumed on runner but not uploaded or read back here.',
        'Published source hashes and reported seal bind the original nine-group browser run; complete publication byte download requires the separate bounded upload proposal.',
        'Warnings remain in the ordinary build/publish logs; this is not a zero-warning claim.',
        'Nine local Picture groups do not establish provider, permission, hardware, accessibility, full native metadata/GPS, whole release or full app parity acceptance.',
        'Final output resource receipt does not establish transient/deleted-byte peak bounds.',
        'Original ce50 failure and all historical native/source/orchestration negatives remain preserved.'
    ]
}
(ROOT / 'verification.json').write_text(json.dumps(qualification, indent=2) + '\n')
print(json.dumps({'state':qualification['state'], 'sourceCommit':SOURCE, 'sourceInputs':len(before),
    'commandFamilies':len(families), 'counts':counts, 'warnings':warning_codes, 'traces':len(trace_rows)}, indent=2))
