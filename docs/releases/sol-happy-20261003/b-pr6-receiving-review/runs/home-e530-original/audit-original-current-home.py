import hashlib
import json
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile

r = pathlib.Path('/workspace/team-c-resume-evidence/c5-home-e530-current-run')
diag = pathlib.Path('/tmp/team-c-c5-home-e530-original-diagnostics-20261004')
pub = pathlib.Path('/workspace/team-c-c5-home-e530-original-publication-retained-20261004')
src = 'e5305818cb70459610f7957ae58255cff85d07ea'
containers = []
metadata = None
with (pub / 'home-win-x64.zip').open('wb') as archive:
    for number in range(1, 6):
        p = pub / 'containers' / f'home-e530-original-part0{number}.zip'
        with zipfile.ZipFile(p) as z:
            assert z.testzip() is None and len(z.namelist()) == len(set(z.namelist()))
            assert set(z.namelist()) == {'parts-manifest.json', 'home-win-x64.zip.part', 'seal.json', 'publish-manifest.json'}
            current = {n: z.read(n) for n in ['parts-manifest.json', 'seal.json', 'publish-manifest.json']}
            if metadata is None:
                metadata = current
                for n, body in current.items():
                    (pub / n).write_bytes(body)
            assert current == metadata
            parts = json.loads(current['parts-manifest.json'])
            expected = parts['parts'][number - 1]
            assert expected['part'] == number
            body = z.read('home-win-x64.zip.part')
            assert len(body) == expected['bytes'] and hashlib.sha256(body).hexdigest() == expected['sha256']
            archive.write(body)
            containers.append({'part': number, 'path': str(p), 'bytes': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'rawPartBytes': len(body), 'rawPartSha256': expected['sha256']})
seal = json.loads(metadata['seal.json'])
manifest = json.loads(metadata['publish-manifest.json'])
assert src == seal['sourceCommit'] == manifest['sourceCommit'] == parts['sourceCommit']
archive = pub / 'home-win-x64.zip'
assert archive.stat().st_size == seal['archiveBytes'] == manifest['archiveBytes'] == parts['originalArchiveBytes'] == 81553694
assert hashlib.sha256(archive.read_bytes()).hexdigest() == seal['archiveSHA256'] == manifest['archiveSHA256'] == parts['originalArchiveSHA256'] == '3f0580ab570ccb4fc633f3d7600a0c781c1399aced322500c5a816911bc5d227'
assert hashlib.sha256(metadata['publish-manifest.json']).hexdigest() == seal['manifestSHA256']
assert len(manifest['files']) == 299
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None and len(z.namelist()) == len(set(z.namelist()))
    assert {i.filename for i in z.infolist() if not i.is_dir()} == {v['path'] for v in manifest['files']}
    for v in manifest['files']:
        name = pathlib.PurePosixPath(v['path'])
        assert not name.is_absolute() and '..' not in name.parts and '\\' not in v['path']
        hasher = hashlib.sha256()
        length = 0
        with z.open(v['path']) as f:
            while chunk := f.read(1024 * 1024):
                length += len(chunk)
                hasher.update(chunk)
        assert length == v['bytes'] and hasher.hexdigest() == v['sha256']
assert json.loads((diag / 'runtime-before.json').read_text()) == manifest['files']
assert (diag / 'runtime-before.json').read_bytes() == (diag / 'runtime-after.json').read_bytes()
assert (diag / 'owning-runtime-before.json').read_bytes() == (diag / 'owning-runtime-after.json').read_bytes()
before = (diag / 'source-before.json').read_bytes()
assert before == (diag / 'source-after.json').read_bytes()
sources = json.loads(before)
assert len(sources) == 17293 and len(sources) == len({v['path'] for v in sources})
gitlink_roots = {
    'framework/CUI/vendor/Avalonia/external/XamlX': ('/workspace/team-c-c5-b-pr6-receiving/framework/CUI/vendor/Avalonia/external/XamlX', '009d4815470cf4bf71d1adbb633a5d81dcb2bb52'),
    'framework/CUI/vendor/Avalonia/external/Avalonia.DBus': ('/workspace/team-c-c5-b-pr6-receiving/framework/CUI/vendor/Avalonia/external/Avalonia.DBus', '864a05282841bf04006890f04d11d60d1a046aa9')
}
repositories = {'root': ('/workspace/9to1', src), **gitlink_roots}
processes = {k: subprocess.Popen(['git', '-C', repo, 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE) for k, (repo, _) in repositories.items()}
for v in sources:
    if v['kind'] == 'gitlink':
        assert gitlink_roots[v['path']][1] == v['actualCommit'] == v['gitBlob']
        continue
    key = next((p for p in gitlink_roots if v['path'].startswith(p + '/')), 'root')
    path = v['path'] if key == 'root' else v['path'][len(key) + 1:]
    process = processes[key]
    process.stdin.write((repositories[key][1] + ':' + path + '\n').encode())
    process.stdin.flush()
    header = process.stdout.readline().decode().strip().split()
    assert len(header) == 3 and header[1] == 'blob'
    body = process.stdout.read(int(header[2]))
    assert process.stdout.read(1) == b'\n'
    assert header[0] == v['gitBlob'] and len(body) == v['bytes'] and hashlib.sha256(body).hexdigest() == v['sha256'], v['path']
for process in processes.values():
    process.stdin.close()
    assert process.wait() == 0
trx = ET.parse(diag / 'test-results/home-original-six.trx')
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
cases = trx.findall('.//t:UnitTestResult', ns)
assert len(cases) == 6 and all(c.attrib['outcome'] == 'Passed' for c in cases)
result = json.loads((diag / 'result.json').read_text())
assert set(c.attrib['testName'] for c in cases) == set(result['managedCases']['names'])
assert all(c['exitCode'] == 0 for c in result['commands'])
assert result['status'] == 'PASS_ORIGINAL_HOME_MANAGED_SIX_AND_VISIBLE_EXPLICIT_NATIVE_QUIT_SCOPED'
assert result['acceptedNativeShutdownScope'] is True and result.get('openNativeProcess') is None
assert len(result['nativeProcesses']) == 2 and len({v['pid'] for v in result['nativeProcesses']}) == 2
for native in result['nativeProcesses']:
    assert native['phase'] == 'complete' and native['exited'] is True and native['exitCode'] == 0
    assert native['windowTitle'] == '9-1 Home' and native['windowClass'].startswith('Avalonia-')
    assert native['windowVisible'] and native['ownHWND'] and native['foregroundVerified']
    x, y, right, bottom = native['windowBounds']
    assert right > x and bottom > y and native['processBirthUTC']
    assert native['inputEventsSent'] == 6 and native['releaseEventsSent'] == 3
    assert native['processSignals'] == [] and native['forcedCleanup'] is False
    assert native['outputDrained'] and native['originalCuiRootReported']
    stdout = (diag / (native['cohort'] + '-stdout.log')).read_bytes()
    assert b'[9-1 Home] Root: StackPanel - CUI loaded into window' in stdout
    assert (diag / (native['cohort'] + '-stderr.log')).read_bytes() == b''
profile_first = (diag / 'profile-after-first-hashes.json').read_bytes()
assert profile_first == (diag / 'profile-after-second-hashes.json').read_bytes()
profile = json.loads(profile_first)
assert result['sameProfileReadOnlyReopenEquivalent'] and result['persistedDefaultStateExists'] is False
assert profile == [{'path': 'home-core-state.json.lock', 'bytes': 0, 'sha256': hashlib.sha256(b'').hexdigest()}]
warnings = {p.name: sorted(set(re.findall(r'(?:warning|error) ([A-Z]+\d+)', p.read_text()))) for p in diag.glob('*.log')}
summary = {
    'schemaVersion': 1, 'status': 'ACTUAL_ORIGINAL_HOME_MANAGED6_PACKAGE299_AND_TWO_NATIVE_EXPLICIT_QUITS_PASS_SCOPED',
    'sourceCommit': src, 'officialRun': 37231041584, 'officialJob': 111520533075,
    'originalManaged': {'total': 6, 'passed': 6, 'failed': 0, 'skipped': 0, 'names': [c.attrib['testName'] for c in cases], 'normalCommandsAllExit0': True, 'filtersOrSkips': None, 'assertions': None},
    'sourceCustody': {'count': len(sources), 'rootWholeBodies': 16689, 'dependencyWholeBodies': 602, 'exactGitlinks': 2, 'beforeAfterByteEqual': True, 'sourceCatalogSha256': hashlib.sha256(before).hexdigest(), 'allWholeCanonicalGitBodiesVerified': True},
    'owningRuntime': {'files': 232, 'beforeAfterByteEqual': True},
    'package': {'manifest': str(pub / 'publish-manifest.json'), 'manifestSha256': seal['manifestSHA256'], 'seal': str(pub / 'seal.json'), 'sealSha256': hashlib.sha256(metadata['seal.json']).hexdigest(), 'originalReassembledZip': str(archive), 'zipBytes': archive.stat().st_size, 'zipSha256': seal['archiveSHA256'], 'files': 299, 'fileBytes': sum(v['bytes'] for v in manifest['files']), 'allMemberCrcAndExactShaVerified': True, 'allExtractedRuntimeBeforeAfterEqual': True, 'parts': containers, 'originalWholeContainer': {'id': 11313588427, 'bytes': 81273445, 'sha256': '2f81a9b3348ed6d0ddb3bc5149b530097af6c5c96ca3332529b5c8b51be6f5b4', 'acquisition': 'METADATA_ONLY_OVER32MiB; every original payload byte independently verified through five official retained raw parts'}},
    'nativeOriginalProcesses': result['nativeProcesses'],
    'sameActualProfileReadOnlyColdOpen': {'equal': True, 'catalogSha256': hashlib.sha256(profile_first).hexdigest(), 'onlyObservedFile': 'zero-byte home-core-state.json.lock', 'persistedDefaultStateFileExists': False, 'durableStatePersistenceOrConfiguredWriter': 'NOT_RUN'},
    'retainedCompilerWarningCodes': {k: v for k, v in warnings.items() if v},
    'retainedOriginalNegative': {'run': 37228835003, 'source': '4dd3128bf9ec4d35523e5997d9ce6cedc392a003', 'firstConsoleWindowQuit': 'FAIL_UNCHANGED', 'old7eForcedMinus1': 'RETAINED'},
    'qualifications': ['Two actual visible process-owned canonical Home GUI quits settled naturally with zero exit and original stream drains; no harness signal/kill/forced cleanup.', 'Same-profile cold-open observation is limited to unchanged zero-byte lock; no persisted domain state existed, no writer/provider/actor configured.', 'Default sole-window close keeps Core alive by specification; closed-window external quit/reopen/activation has no available original route and remains NOT_RUN.', '299 real package files retained byte-exact locally with distinct current e530 producer tuple; no old4dd substitution. Signing/installed identity/cleanPC/donor/all themes/fullHome acceptance remain NOT_RUN.']
}
target = r / 'actual-original-home-six-two-native-quits-and-package.json'
target.write_text(json.dumps(summary, indent=2) + '\n')
print('Original strict6 PASS; 17293 source rows/299 package members exact; two actual GUI quits exit0/drained/no signals.')
print('Read-only same-profile only zero-byte lock; durable state persistence NOT_RUN.')
print('Receipt SHA256:', hashlib.sha256(target.read_bytes()).hexdigest())
