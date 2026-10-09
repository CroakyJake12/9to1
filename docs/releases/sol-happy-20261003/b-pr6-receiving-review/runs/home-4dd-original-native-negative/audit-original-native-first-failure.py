import hashlib
import json
import pathlib
import subprocess
import xml.etree.ElementTree as ET
import zipfile

r = pathlib.Path('/tmp/team-c-c5-home-current-windows-4dd-20261004')
diag = r / 'diagnostics'
src = '4dd3128bf9ec4d35523e5997d9ce6cedc392a003'
pub = r / 'original-publication'
pub.mkdir(exist_ok=True)
containers = []
metadata = None
with (pub / 'home-win-x64.zip').open('wb') as archive:
    for number in range(1, 6):
        p = r / 'containers' / f'home-4dd-original-part0{number}.zip'
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
assert archive.stat().st_size == seal['archiveBytes'] == manifest['archiveBytes'] == parts['originalArchiveBytes'] == 81553586
assert hashlib.sha256(archive.read_bytes()).hexdigest() == seal['archiveSHA256'] == manifest['archiveSHA256'] == parts['originalArchiveSHA256']
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
assert result['status'] == 'FAIL_OR_INCOMPLETE' and result['acceptedNativeShutdownScope'] is False
assert len(result['nativeProcesses']) == 1
native = result['nativeProcesses'][0]
assert native['phase'] == 'original-process-exit' and native['exited'] is False and native['exitCode'] is None
assert native['windowTitle'].endswith('AvaloniaHome.exe') and native['windowTitle'] != '9-1 Home'
assert native['inputEventsSent'] == 6 and native['releaseEventsSent'] == 3
assert native['processSignals'] == [] and native['forcedCleanup'] is False
stdout = (diag / 'native-first-stdout.log').read_bytes()
assert b'[9-1 Home] Root: StackPanel - CUI loaded into window' in stdout
assert (diag / 'native-first-stderr.log').read_bytes() == b''
summary = {
    'schemaVersion': 1, 'status': 'ORIGINAL_HOME_MANAGED6_AND_PACKAGE_PASS_NATIVE_FIRST_CONTROL_FAILURE_RETAINED',
    'sourceCommit': src, 'officialRun': 37228835003, 'officialJob': 111514012449,
    'originalManaged': {'total': 6, 'passed': 6, 'failed': 0, 'skipped': 0, 'names': [c.attrib['testName'] for c in cases], 'normalCommandsAllExit0': True, 'filtersOrSkips': None, 'assertions': None},
    'sourceCustody': {'count': len(sources), 'rootWholeBodies': 16689, 'dependencyWholeBodies': 602, 'exactGitlinks': 2, 'beforeAfterByteEqual': True, 'sourceCatalogSha256': hashlib.sha256(before).hexdigest(), 'allWholeCanonicalGitBodiesVerified': True},
    'owningRuntime': {'files': 232, 'beforeAfterByteEqual': True},
    'package': {'manifest': str(pub / 'publish-manifest.json'), 'manifestSha256': seal['manifestSHA256'], 'seal': str(pub / 'seal.json'), 'sealSha256': hashlib.sha256(metadata['seal.json']).hexdigest(), 'originalReassembledZip': str(archive), 'zipBytes': archive.stat().st_size, 'zipSha256': seal['archiveSHA256'], 'files': 299, 'fileBytes': sum(v['bytes'] for v in manifest['files']), 'allMemberCrcAndExactShaVerified': True, 'parts': containers, 'originalWholeContainer': {'id': 11312789339, 'bytes': 81272829, 'sha256': '93633d862e1d97b93af3a27e556528a0794ab40a8054fa7235dbe47805b49e01', 'acquisition': 'METADATA_ONLY_OVER32MiB; every original payload byte independently verified through five official retained raw parts'}},
    'nativeFirstOriginal': native,
    'mechanism': 'Observed first owned visible HWND title was executable path, not exact source-defined 9-1 Home GUI; unchanged harness accepted Process.MainWindowHandle before canonical GUI identity. CUI root actually loaded in stdout. This is a native harness control selection failure, not evidence that product GUI CtrlShiftQ failed.',
    'stdoutWitness': {'bytes': len(stdout), 'sha256': hashlib.sha256(stdout).hexdigest(), 'actualCanonicalRootPrinted': True, 'actualMarkerTransport': 'ASCII hyphen for authored em-dash'},
    'qualifications': ['Native first process did not settle inside45s and remained open at harness result; no harness kill/signal/forced cleanup or success inferred. Subsequent GitHub job orphan cleanup is not natural process settlement proof.', 'Second native process/default-profile read-only reopening NOT_RUN.', 'Root-owned exact current GUI selection successor/source peer and necessary new Windows control remain separate.', '299 package files retained byte-exact locally, no installed identity/signing/cleanPC/donor/fullHome acceptance.']
}
target = r / 'actual-first-native-control-failure.json'
target.write_text(json.dumps(summary, indent=2) + '\n')
print('Original strict6 PASS; 17293 source rows/299 package members exact; original first native control FAIL retained.')
print('Receipt SHA256:', hashlib.sha256(target.read_bytes()).hexdigest())
