#!/usr/bin/env python3
"""Seal actual Root-published Windows files; no SDK, app, model or deployment execution."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import zipfile

def require(condition, message):
    if not condition:
        raise RuntimeError(message)

def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()

def safe_name(name):
    require(isinstance(name, str) and name and '\\' not in name and '\0' not in name
            and not name.startswith('/') and not re.match(r'^[A-Za-z]:', name)
            and all(part not in ('', '.', '..') for part in name.split('/')),
            'Unsafe relative member name')
    return name

def pin(path):
    require(path.is_file() and not path.is_symlink(), 'Regular input file required: ' + str(path))
    return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': digest(path)}

def verify_bound(row):
    require(set(row) == {'path', 'bytes', 'sha256'}, 'Whole exact input pin required')
    path = Path(row['path'])
    require(path.is_absolute() and pin(path) == row, 'Bound input bytes differ')
    return path

def read_bound(row):
    return json.loads(verify_bound(row).read_bytes())

def inventory(root):
    require(root.is_absolute() and root.is_dir() and not root.is_symlink(), 'Real publication directory required')
    rows = []
    for path in sorted(root.rglob('*')):
        require(not path.is_symlink(), 'Publication links rejected')
        if path.is_dir():
            continue
        require(path.is_file(), 'Unsupported publication entry')
        name = safe_name(path.relative_to(root).as_posix())
        lower = name.lower()
        require(not any(part in {'.git', '.nuget', 'profiles', 'node_modules', 'obj'} for part in lower.split('/')),
                'Private/cache directory is not a publication member')
        require(Path(lower).suffix not in {'.gguf', '.safetensors', '.onnx', '.pt', '.pth', '.tflite',
                                         '.pfx', '.p12', '.pem', '.key', '.nupkg'}
                and Path(lower).name not in {'secrets.json', 'token-cache.json', 'provider-secrets.json'}
                and not Path(lower).name.startswith('.env'), 'Private/model input is not a package member')
        rows.append({'path': name, 'bytes': path.stat().st_size, 'sha256': digest(path)})
    require(rows, 'Empty publication rejected')
    require(len({row['path'].casefold() for row in rows}) == len(rows), 'Windows case alias in publication')
    return rows

def require_windows_x64(path):
    with path.open('rb') as stream:
        require(stream.read(2) == b'MZ', 'Actual Windows executable header absent')
        stream.seek(0x3c)
        offset = stream.read(4)
        require(len(offset) == 4, 'Truncated Windows executable header')
        stream.seek(struct.unpack('<I', offset)[0])
        require(stream.read(4) == b'PE\0\0' and stream.read(2) == b'\x64\x86',
                'Actual Windows AMD64 PE input required')

def verify_publication(spec, expected_commit, source_binding):
    receipt = read_bound(spec['receipt'])
    app = spec['app']
    require(app in ('Write', 'Present'), 'Exact owning application required')
    require(receipt['status'] == 'ACTUAL_ORDINARY_SDK_PUBLISH_COMPLETE', 'Actual ordinary publication receipt required')
    require(receipt['sourceCommit'] == receipt['sourceCommitAfter'] == expected_commit,
            'Current publication source revision differs')
    require(receipt['sourceBindingSha256'] == source_binding, 'Current shared source binding differs')
    require(receipt['sdkVersion'] == '10.0.401' and receipt['targetFramework'] == 'net10.0'
            and receipt['runtimeIdentifier'] == 'win-x64' and receipt['selfContained'] is True,
            'Actual Windows SDK/framework/runtime selection differs')
    require(receipt['naturalExitCode'] == 0 and receipt['forcedStop'] is False
            and receipt['wholeStdoutCaptureComplete'] is True, 'Publication did not complete naturally')
    require(receipt['sourceInputsBefore'] and receipt['sourceInputsBefore'] == receipt['sourceInputsAfter'],
            'Whole physical/Git source input before/after comparison required')
    require(all(isinstance(row['bytes'], int) and row['bytes'] >= 0
                and re.fullmatch(r'[a-f0-9]{64}', row['sha256'])
                and re.fullmatch(r'[a-f0-9]{40}', row['gitBlob'])
                and safe_name(row['path']) for row in receipt['sourceInputsBefore']),
            'Exact physical/Git source rows required')
    require(len({row['path'] for row in receipt['sourceInputsBefore']}) == len(receipt['sourceInputsBefore']),
            'Duplicate physical/Git source row')
    require(hashlib.sha256(json.dumps(receipt['sourceInputsBefore'], sort_keys=True,
            separators=(',', ':')).encode()).hexdigest() == source_binding,
            'Current shared source binding does not cover the whole source rows')
    require(receipt['changedSourceInputs'] == receipt['trackedChangesAfter'] == [], 'Publication source changed')
    project = '9to1 Workspace/' + app + '/HavenOS.' + app + '.csproj'
    command = receipt['argv']
    require(isinstance(command, list) and len(command) >= 3 and command[1] == 'publish'
            and command[2].replace('\\', '/').endswith(project), 'Actual owning entrypoint publish command differs')
    require('--self-contained' in command and command[command.index('--self-contained') + 1] == 'true'
            and '-r' in command and command[command.index('-r') + 1] == 'win-x64', 'Actual Windows publish arguments differ')
    verify_bound(spec['externalLog'])
    require(receipt['wholeExternalLog'] == spec['externalLog'], 'Whole actual external log binding differs')
    root = Path(receipt['publishRoot'])
    rows = inventory(root)
    require(rows == receipt['publishFiles'] and len(rows) == receipt['fileCount']
            and sum(row['bytes'] for row in rows) == receipt['totalBytes'], 'Whole publication files differ')
    names = {row['path'] for row in rows}
    require({'HavenOS.' + app + '.exe', 'HavenOS.' + app + '.dll', 'HavenOS.' + app + '.deps.json',
             'HavenOS.' + app + '.runtimeconfig.json', 'Haven.dll', 'coreclr.dll', 'hostfxr.dll',
             'hostpolicy.dll'}.issubset(names), 'Actual standalone self-contained app closure absent')
    runtime = json.loads((root / ('HavenOS.' + app + '.runtimeconfig.json')).read_bytes())['runtimeOptions']
    require(runtime['tfm'] == 'net10.0' and not runtime.get('framework') and not runtime.get('frameworks'),
            'Expected actual self-contained net10.0 runtime configuration')
    core = [row for row in runtime.get('includedFrameworks', []) if row['name'] == 'Microsoft.NETCore.App']
    require(len(core) == 1 and core[0]['version'] == '10.0.12', 'Actual included Core runtime10.0.12 required')
    for name in ('HavenOS.' + app + '.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll'):
        require_windows_x64(root / name)
    return root, rows

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input-bind', type=Path, required=True)
    parser.add_argument('--input-bind-sha256', required=True)
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    require(re.fullmatch(r'[a-f0-9]{40}', args.expected_commit), 'Independent exact source commit required')
    require(re.fullmatch(r'[a-f0-9]{64}', args.input_bind_sha256), 'Independent whole binding hash required')
    require(pin(args.input_bind)['sha256'] == args.input_bind_sha256, 'Input binding bytes differ')
    binding = json.loads(args.input_bind.read_bytes())
    require(binding['sourceCommit'] == args.expected_commit and binding['runtimeAcceptanceClaimed'] is False,
            'Binding source differs or claims unsupported runtime acceptance')
    source_binding = binding['sourceBindingSha256']
    require(re.fullmatch(r'[a-f0-9]{64}', source_binding), 'Exact current source binding required')
    require(len(binding['publications']) == 2 and {x['app'] for x in binding['publications']} == {'Write', 'Present'},
            'Exactly two actual app publications required')
    sources, union, publications, shared, original_outputs = {}, {}, [], [], []
    for spec in binding['publications']:
        root, rows = verify_publication(spec, args.expected_commit, source_binding)
        original_outputs.append((root, rows, spec))
        publications.append({'app': spec['app'], 'receipt': spec['receipt'], 'externalLog': spec['externalLog'],
                             'fileCount': len(rows), 'totalBytes': sum(x['bytes'] for x in rows)})
        for row in rows:
            name = row['path']
            if name in union:
                require(union[name] == row, 'Shared publication filename has different bytes: ' + name)
                shared.append(row)
            else:
                require(name.casefold() not in {value.casefold() for value in union},
                        'Windows case alias between publications')
                union[name] = row
                sources[name] = root / name
    notices = read_bound(binding['licensingCompanionReceipt'])
    require(notices['status'] == 'REVIEWED_EXACT_DISTRIBUTION_LICENSE_COMPANION'
            and notices['sourceCommit'] == args.expected_commit, 'Current reviewed distribution notices required')
    notice_root = Path(notices['noticeRoot'])
    notice_rows = inventory(notice_root)
    require(notice_rows == notices['files'], 'Whole license/notice companion differs')
    notice_names = {row['path'] for row in notice_rows}
    binaries = {row['sha256'] for row in union.values() if Path(row['path']).suffix.lower() in ('.dll', '.exe')}
    mappings = notices['licensedBinaryMappings']
    require(binaries == {row['binarySha256'] for row in mappings}, 'Every actual native/managed binary needs a reviewed notice mapping')
    require(all(row['noticePaths'] and set(row['noticePaths']).issubset(notice_names) for row in mappings),
            'Referenced binary notices absent')
    for row in notice_rows:
        name = 'notices/' + row['path']
        require(name not in union, 'Notice filename collides with publication')
        require(name.casefold() not in {value.casefold() for value in union}, 'Windows case alias in notices')
        union[name] = dict(row, path=name)
        sources[name] = notice_root / row['path']
    rows = [union[name] for name in sorted(union)]
    output = args.output.absolute()
    require(not output.exists(), 'Fresh output required')
    output.mkdir(parents=True, exist_ok=False)
    archive_path = output / ('Write-Present-win-x64-' + args.expected_commit[:12] + '.zip')
    with zipfile.ZipFile(archive_path, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for row in rows:
            path = sources[row['path']]
            require(path.stat().st_size == row['bytes'] and digest(path) == row['sha256'], 'Member changed before packaging')
            archive.write(path, row['path'])
    with zipfile.ZipFile(archive_path) as archive:
        require(archive.testzip() is None and archive.namelist() == [row['path'] for row in rows], 'Full ZIP membership/CRC differs')
        for row in rows:
            value = hashlib.sha256(); count = 0
            with archive.open(row['path']) as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    count += len(block); value.update(block)
            require(count == row['bytes'] and value.hexdigest() == row['sha256'], 'Whole archived member differs')
            path = sources[row['path']]
            require(path.stat().st_size == row['bytes'] and digest(path) == row['sha256'], 'Original member changed during seal')
    for root, original_rows, spec in original_outputs:
        require(inventory(root) == original_rows, 'Whole original publication changed during seal')
        verify_bound(spec['receipt']); verify_bound(spec['externalLog'])
    require(inventory(notice_root) == notice_rows, 'Whole notice companion changed during seal')
    verify_bound(binding['licensingCompanionReceipt'])
    require(pin(args.input_bind)['sha256'] == args.input_bind_sha256, 'Whole input binding changed during seal')
    receipt = {'status': 'EXACT_WINDOWS_WRITE_PRESENT_TRANSPORT_SEAL', 'sourceCommit': args.expected_commit,
               'inputBinding': pin(args.input_bind), 'sourceBindingSha256': source_binding,
               'publications': publications, 'licensingCompanionReceipt': binding['licensingCompanionReceipt'],
               'sharedByteIdenticalMembers': shared, 'members': rows, 'fileCount': len(rows),
               'totalBytes': sum(row['bytes'] for row in rows), 'archive': pin(archive_path),
               'wholeMembershipCrcHashAndOriginalBeforeAfterQualified': True,
               'sdkExecutedByThisSealer': False, 'runtimeAcceptanceClaimed': False,
               'signingClaimed': False, 'deploymentClaimed': False, 'modelWeightsOrCredentialsIncluded': False}
    receipt_path = output / 'WRITE-PRESENT-WINDOWS-TRANSPORT-SEAL62.json'
    with receipt_path.open('x') as stream:
        json.dump(receipt, stream, indent=2); stream.write('\n')
    print(json.dumps(pin(receipt_path)))

if __name__ == '__main__':
    main()
