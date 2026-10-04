"""Isolated whole original Home owning validation; all432 original plusfive new cases mandatory."""
import argparse, base64, hashlib, importlib.util, json, os, pathlib, re, shutil
import signal, stat, struct, subprocess, sys, time, types, zlib

sys.dont_write_bytecode = True
P = pathlib.Path
EXEC_GUARD = "import os,sys;fd=int(sys.argv[1]);token=os.read(fd,1);os.close(fd);assert token==b'G';os.execvp(sys.argv[2],sys.argv[2:])"
MAX_EVIDENCE = 256 * 1024 * 1024
MAX_LOG = 16 * 1024 * 1024
TOOLS = [
    'framework/CUI/vendor/Avalonia/src/tools/DevAnalyzers/DevAnalyzers.csproj',
    'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CSharp/Avalonia.Analyzers.CSharp.csproj',
    'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CodeFixes.CSharp/Avalonia.Analyzers.CodeFixes.CSharp.csproj',
    'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.VisualBasic/Avalonia.Analyzers.VisualBasic.csproj',
    'framework/CUI/vendor/Avalonia/src/tools/DevGenerators/DevGenerators.csproj',
    'framework/CUI/vendor/Avalonia/src/tools/Avalonia.DBus.Generators/Avalonia.DBus.Generators.csproj',
    'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Generators/Avalonia.Generators.csproj',
]
TASK_PROJECT = 'framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'


def digest(path):
    with P(path).open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()


def add(errors, error):
    if not any(error is prior for prior in errors): errors.append(error)


def collect(errors, operation):
    try: return operation()
    except BaseException as error: add(errors, error); return None


def fail(primary, errors):
    all_errors = []
    for error in ([primary] if primary is not None else []) + errors: add(all_errors, error)
    if len(all_errors) > 1: raise BaseExceptionGroup('Original joint SDK and independent custody failures', all_errors)
    if all_errors: raise all_errors[0]


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def physical_symbols(target, debug_type, parser):
    """Bind symbols to this original physical PE and the evaluated producer."""
    if target.is_symlink() or not target.is_file():
        raise RuntimeError('Original physical PE must be regular')
    image = target.read_bytes()

    def bounds(offset, amount):
        if offset < 0 or amount < 0 or offset > len(image) or amount > len(image) - offset:
            raise RuntimeError('Original PE symbol range outside complete image')

    bounds(0, 64)
    if image[:2] != b'MZ': raise RuntimeError('Original PE DOS signature refused')
    pe = struct.unpack_from('<I', image, 60)[0]
    bounds(pe, 24)
    if pe < 64 or image[pe:pe + 4] != b'PE\0\0':
        raise RuntimeError('Original PE signature refused')
    sections = struct.unpack_from('<H', image, pe + 6)[0]
    optional_size = struct.unpack_from('<H', image, pe + 20)[0]
    optional = pe + 24
    bounds(optional, optional_size)
    if optional_size < 2: raise RuntimeError('Original PE optional header missing')
    magic = struct.unpack_from('<H', image, optional)[0]
    if magic not in (0x10b, 0x20b): raise RuntimeError('Original PE optional header kind refused')
    directories = 96 if magic == 0x10b else 112
    if optional_size < directories + 56 or not sections:
        raise RuntimeError('Original PE debug directory/header missing')
    if struct.unpack_from('<I', image, optional + directories - 4)[0] < 7:
        raise RuntimeError('Original PE debug directory absent')
    headers = struct.unpack_from('<I', image, optional + 60)[0]
    bounds(0, headers)
    section_table = optional + optional_size
    bounds(section_table, sections * 40)

    def rva_offset(value, amount):
        matches = []
        if value < headers and amount <= headers - value:
            bounds(value, amount); matches.append(value)
        for index in range(sections):
            row = section_table + index * 40
            virtual_size, address, raw_size, raw = struct.unpack_from('<IIII', image, row + 8)
            delta = value - address
            if 0 <= delta < max(virtual_size, raw_size) and delta <= raw_size and amount <= raw_size - delta:
                offset = raw + delta
                bounds(offset, amount); matches.append(offset)
        if len(matches) != 1: raise RuntimeError('Original PE debug RVA missing/ambiguous')
        return matches[0]

    debug_rva, debug_size = struct.unpack_from('<II', image, optional + directories + 48)
    if not debug_rva or not debug_size or debug_size % 28:
        raise RuntimeError('Original PE debug directory absent/malformed')
    debug_at = rva_offset(debug_rva, debug_size)
    embedded = []
    for index in range(debug_size // 28):
        row = debug_at + index * 28
        kind, amount, address, pointer = struct.unpack_from('<IIII', image, row + 12)
        bounds(pointer, amount)
        if address and rva_offset(address, amount) != pointer:
            raise RuntimeError('Original PE debug RVA/file pointer differ')
        if kind == 17:
            major, minor = struct.unpack_from('<HH', image, row + 8)
            if major < 0x0100 or minor != 0x0100:
                raise RuntimeError('Original embedded portable PDB version refused')
            embedded.append((row, amount, address, pointer))
    producer = str(debug_type).strip().lower()
    sidecar = target.with_suffix('.pdb')
    physical = [{'path': str(target), 'bytes': len(image), 'sha256': hashlib.sha256(image).hexdigest()}]
    if producer == 'embedded':
        if len(embedded) != 1:
            raise RuntimeError('Evaluated embedded producer requires exactly one original MPDB record')
        row, amount, address, pointer = embedded[0]
        if amount <= 8 or image[pointer:pointer + 4] != b'MPDB':
            raise RuntimeError('Original embedded portable PDB header refused')
        expanded_size = struct.unpack_from('<I', image, pointer + 4)[0]
        if not 0 < expanded_size <= MAX_EVIDENCE:
            raise RuntimeError('Original embedded PDB advertised expansion bound refused')
        compressed = image[pointer + 8:pointer + amount]
        decoder = zlib.decompressobj(-15)
        symbols = decoder.decompress(compressed, expanded_size + 1)
        if len(symbols) != expanded_size or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
            raise RuntimeError('Original embedded PDB exact bounded deflate stream refused')
        if sidecar.exists() or sidecar.is_symlink():
            raise RuntimeError('Evaluated embedded producer has a conflicting external symbol path')
        pair = [target]
        storage = {'kind': 'embedded-portable-pdb', 'evaluatedDebugType': debug_type,
            'originalDebugDirectoryFileOffset': debug_at, 'originalDebugDirectoryBytes': debug_size,
            'originalDebugRecordFileOffset': row,
            'originalEmbeddedMajorVersion': struct.unpack_from('<H', image, row + 8)[0],
            'originalEmbeddedMinorVersion': struct.unpack_from('<H', image, row + 10)[0],
            'originalRecordType': 17, 'originalRecordFileOffset': pointer,
            'originalRecordRva': address, 'originalRecordBytes': amount,
            'codec': 'MPDB/raw-deflate', 'advertisedExpandedBytes': expanded_size,
            'compressedBytes': len(compressed), 'compressedSha256': hashlib.sha256(compressed).hexdigest(),
            'qualification': 'Symbols are decoded only from the SAME retained complete original PE. No external PDB path is fabricated; offsets, codec, bounds and hashes permit independent lossless replay.'}
    elif producer in ('portable', 'full', 'pdbonly'):
        if embedded or sidecar.is_symlink() or not sidecar.is_file():
            raise RuntimeError('Evaluated external producer requires the original regular external PDB pair')
        symbols = sidecar.read_bytes()
        pair = [target, sidecar]
        physical.append({'path': str(sidecar), 'bytes': len(symbols), 'sha256': hashlib.sha256(symbols).hexdigest()})
        storage = {'kind': 'external-portable-pdb', 'evaluatedDebugType': debug_type}
    else:
        raise RuntimeError('Evaluated original symbol producer missing/unsupported')
    identity = parser.assert_actual_pair(image, symbols)
    documents = parser.pdb_documents(symbols)
    storage.update({'physicalFiles': physical, 'portablePdbBytes': len(symbols),
        'portablePdbSha256': hashlib.sha256(symbols).hexdigest(), 'identity': identity})
    return pair, identity, documents, storage


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--manifest-sha', required=True)
    parser.add_argument('--configuration', choices=['Debug', 'Release'], required=True)
    args = parser.parse_args()
    control = root = P.cwd().resolve()
    source_commit = args.expected_commit
    source_cut_sha = args.manifest_sha
    lane = 'home-' + args.configuration.lower()
    out = control / 'artifacts/home437-validation' / lane
    if out.exists() or out.is_symlink(): raise RuntimeError('Fresh whole owning evidence directory required')
    out.mkdir(parents=True, mode=0o700)
    if args.manifest != '.github/validation/astra-desktop-visible-cut.json':
        raise RuntimeError('Exact self manifest path required')
    control_cut_path = cut_path = root / args.manifest
    if not re.fullmatch('[0-9a-f]{40}', args.expected_commit) or digest(cut_path) != source_cut_sha:
        raise RuntimeError('Exact issued original Home head/cut pins required')
    control_cut = cut = json.loads(cut_path.read_text())
    control_paths = paths = {row['path']: row for row in cut['files']}
    if len(paths) != len(cut['files']) or args.manifest in paths:
        raise RuntimeError('Complete cut duplicate/self file refused')
    home_catalog_path = catalog_path = root / '.github/validation/astra-home437-sources.json'
    home_catalog = catalog = json.loads(catalog_path.read_text())
    if catalog['normalCommit'] != cut['currentNormalCommit'] or catalog['sourceStatus'] != 'JOINT05_REVIEWED_SOURCE_ONLY_UNCOMPILED_UNRUN':
        raise RuntimeError('Exact current normal and reviewed source required')
    combined = catalog['sources'] + catalog['helpers'] + catalog['homeValidation']['fullHomeSources']
    selected_map = {}
    for row in combined:
        if row['path'] in selected_map and selected_map[row['path']]['sha256'] != row['sha256']:
            raise RuntimeError('Exact Home source observations disagree')
        selected_map[row['path']] = row
    selected = list(selected_map.values()) + [
        {'path': '.github/scripts/astra-home437-original.py', 'sha256': digest(__file__)},
        {'path': str(catalog_path.relative_to(root)), 'sha256': digest(catalog_path)}]
    owned = [{'path': '.github/scripts/astra-home437-original.py', 'sha256': digest(__file__)},
             {'path': str(catalog_path.relative_to(control)), 'sha256': digest(catalog_path)},
             catalog['homeValidation']['caller']]
    if len(catalog['entryProjects']) != 11 or len(catalog['owningPrerequisiteIndices']) != 38 or catalog['originalCommandTemplates'] != catalog['frozenCommandTemplates']:
        raise RuntimeError('Current eleven entries,38 prerequisites and original templates must remain pinned')
    if len(catalog['homeValidation']['fullHomeSources']) != 71:
        raise RuntimeError('All seventy original Home test sources and one original successor fixture required')
    for row in owned + selected + catalog['baselineGuards']:
        if paths.get(row['path'], {}).get('sha256') != row['sha256']:
            raise RuntimeError('Selected source/helper missing from actual complete cut: ' + row['path'])
    if catalog['requiredSdkConfigurations'] != ['Debug','Release']:
        raise RuntimeError('Both ordered independent configurations required')
    observations_source_qualification = catalog['qualification']
    helper = root / '.github/scripts/astra-joint-sdk-original-session-drain.py'
    guard = load_module(helper, 'joint_original_session')
    OriginalSession = guard.OriginalSession
    if not callable(getattr(os, 'pidfd_open', None)) or not callable(getattr(signal, 'pidfd_send_signal', None)):
        raise RuntimeError('Linux original creator pidfd required')
    observations = {'status': 'WHOLE_HOME437_PENDING', 'commands': [], 'configurations': [],
                    'controlHead': args.expected_commit, 'controlCutSha256': args.manifest_sha,
                    'sourceHead': source_commit, 'sourceCutSha256': source_cut_sha, 'lane': lane,
                    'qualification': 'Whole unfiltered Home test/build/reference/source/symbol evidence only. No full SDK, GUI/native installed/bootstrap/authority or other suite acceptance.'}

    def budget():
        total = 0
        for path in out.rglob('*'):
            info = path.lstat()
            if stat.S_ISLNK(info.st_mode): raise RuntimeError('Evidence symlink refused')
            if stat.S_ISREG(info.st_mode): total += info.st_size
        if total > MAX_EVIDENCE: raise RuntimeError('Whole retained evidence budget refused')
        return total

    def save(path, value):
        data = (json.dumps(value, sort_keys=True, indent=2) + '\n').encode()
        old = path.stat().st_size if path.exists() else 0
        if path.is_symlink() or budget() - old + len(data) > MAX_EVIDENCE:
            raise RuntimeError('Bounded regular receipt required')
        path.parent.mkdir(parents=True, exist_ok=True)
        handle = None; primary = None; cleanup = []
        try:
            handle = path.open('wb')
            handle.write(data); handle.flush(); os.fsync(handle.fileno())
        except BaseException as error: primary = error
        finally:
            if handle is not None: collect(cleanup, handle.close)
        fail(primary, cleanup)

    def protect_selected():
        if digest(cut_path) != source_cut_sha or digest(control_cut_path) != args.manifest_sha: raise RuntimeError('Issued original comparison/control cut changed')
        for row in owned:
            path = control / row['path']
            if path.is_symlink() or not path.is_file() or digest(path) != row['sha256']:
                raise RuntimeError('Original Home adapter source changed')
        for row in selected:
            path = root / row['path']
            if path.is_symlink() or not path.is_file() or digest(path) != row['sha256']:
                raise RuntimeError('Selected original source changed: ' + row['path'])

    def command(name, argv, *, deadline=1200, extra_env=None, native_log_name=None):
        if not re.fullmatch('[a-zA-Z0-9_.-]+', name) or any(row['name'] == name for row in observations['commands']):
            raise RuntimeError('Unique ordinary original command name required')
        protect_selected(); budget()
        records = out / 'original-process-drains' / name
        records.mkdir(mode=0o700, parents=True)
        save(records / 'expected-managed-launch.json', {'expectedManagedLaunch': True, 'drained': False,
             'classification': 'Original SDK metadata/build/discovery or complete test process'})
        if native_log_name is not None and not re.fullmatch('[a-zA-Z0-9_.-]+', native_log_name): raise RuntimeError('Exact original native log name required')
        log = out / (native_log_name + '.log') if native_log_name is not None else out / 'logs' / (name + '.log')
        log.parent.mkdir(exist_ok=True)
        process = session = output = creator = read_fd = write_fd = None
        primary = None; cleanup = []; started = time.monotonic()
        try:
            output = log.open('xb')
            read_fd, write_fd = os.pipe()
            process = subprocess.Popen([sys.executable, '-I', '-c', EXEC_GUARD, str(read_fd), *argv],
                cwd=root, env={**os.environ, **(extra_env or {})}, stdout=output,
                stderr=subprocess.STDOUT, start_new_session=True, pass_fds=(read_fd,))
            creator = os.pidfd_open(process.pid, 0)
            # Keep this same object even if an original initializer receipt write fails.
            session = OriginalSession.__new__(OriginalSession)
            OriginalSession.__init__(session, process, records)
            os.close(read_fd); read_fd = None
            if os.write(write_fd, b'G') != 1: raise RuntimeError('Original guarded exec handshake refused')
            os.close(write_fd); write_fd = None
            while process.poll() is None:
                session.observe(); budget()
                if log.stat().st_size > MAX_LOG: raise RuntimeError('Original command log bound exceeded')
                if time.monotonic() - started >= deadline: raise TimeoutError('Original command deadline exceeded: ' + name)
                time.sleep(.04)
            if process.wait() != 0: raise RuntimeError('Original whole command failed: ' + name)
        except BaseException as error: primary = error
        finally:
            if output is not None: collect(cleanup, output.close)
            for fd in (read_fd, write_fd):
                if fd is not None: collect(cleanup, lambda fd=fd: os.close(fd))
            if session is not None: collect(cleanup, session.drain)
            elif process is not None: add(cleanup, RuntimeError('Original session was not captured; no true seal'))
            if creator is not None:
                def kill_original():
                    try: signal.pidfd_send_signal(creator, signal.SIGKILL)
                    except ProcessLookupError: pass
                collect(cleanup, kill_original)
            elif process is not None: collect(cleanup, process.kill)
            if process is not None: collect(cleanup, lambda: process.wait(timeout=10))
            if creator is not None: collect(cleanup, lambda: os.close(creator))
            seal = collect(cleanup, lambda: json.loads((records / 'expected-managed-launch.json').read_text()))
            if not isinstance(seal, dict) or seal.get('drained') is not True:
                add(cleanup, RuntimeError('Same complete original session drain not proven'))
            row = {'name': name, 'argv': argv, 'extraEnvironment': extra_env or {},
                   'creatorPidfdCaptured': creator is not None, 'originalSessionCaptured': session is not None,
                   'exitCode': None if process is None else process.returncode,
                   'seconds': time.monotonic() - started, 'primaryType': None if primary is None else type(primary).__name__,
                   'seal': seal, 'log': str(log.relative_to(out))}
            row['logBytes'] = collect(cleanup, lambda: log.stat().st_size)
            row['logSha256'] = collect(cleanup, lambda: digest(log))
            row['cleanupTypes'] = [type(error).__name__ for error in cleanup]
            observations['commands'].append(row)
            collect(cleanup, lambda: save(out / 'commands.json', observations['commands']))
            collect(cleanup, protect_selected); collect(cleanup, budget)
        fail(primary, cleanup)
        if log.stat().st_size > MAX_LOG: raise RuntimeError('Final original log exceeded accepted bound')
        return log.read_text()

    def verify_whole(label):
        head = command(label + '-head', ['/usr/bin/git', 'rev-parse', 'HEAD'], deadline=60).strip()
        if head != source_commit or os.environ.get('GITHUB_SHA') != args.expected_commit:
            raise RuntimeError('Actual original source/control head mismatch')
        tree = command(label + '-tree', ['/usr/bin/git', 'ls-tree', '-rz', 'HEAD'], deadline=60)
        actual_files = {}; actual_links = {}
        for entry in tree.split('\0'):
            if not entry: continue
            header, name = entry.split('\t', 1); mode, kind, oid = header.split()
            if mode == '160000': actual_links[name] = oid
            elif mode in {'100644', '100755'} and kind == 'blob': actual_files[name] = (mode, oid)
            else: raise RuntimeError('Unexpected original tracked type')
        if actual_files.pop(args.manifest, None) is None: raise RuntimeError('Self cut is not tracked')
        expected = {}
        for name, row in paths.items():
            relative = P(name)
            if relative.is_absolute() or '..' in relative.parts or str(relative) != name:
                raise RuntimeError('Unsafe whole cut path')
            source = root / name
            if source.is_symlink() or not source.is_file(): raise RuntimeError('Whole tracked source not regular')
            data = source.read_bytes()
            blob = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
            mode = row.get('mode', row.get('gitMode'))
            if len(data) != row['bytes'] or hashlib.sha256(data).hexdigest() != row['sha256'] or blob != row['object']:
                raise RuntimeError('Complete actual tracked byte/blob mismatch: ' + name)
            expected[name] = (mode, blob)
        if actual_files != expected or actual_links != {r['path']: r['commit'] for r in cut['gitlinks']} or len(actual_links) != 22:
            raise RuntimeError('Complete tracked map/22 gitlinks mismatch')
        if cut['materializedGitlinks'] != catalog['materializedGitlinks']:
            raise RuntimeError('Exact three source-built dependency pins required')
        for i, row in enumerate(cut['materializedGitlinks']):
            actual = command(label + '-dependency-' + str(i), ['/usr/bin/git', '-C', row['path'], 'rev-parse', 'HEAD'], deadline=60).strip()
            dirty = command(label + '-dependency-clean-' + str(i), ['/usr/bin/git', '-C', row['path'], 'status', '--porcelain', '--untracked-files=no'], deadline=60).strip()
            if actual != row['commit'] or dirty: raise RuntimeError('Materialized source pin/dirt mismatch')
        command(label + '-source-preparation-check', ['/usr/bin/bash', '9to1 Workspace/shared/eng/prepare-cui-source.sh', '--check'], deadline=60)

    def verify_control_whole(label):
        head = command(label + '-head', ['/usr/bin/git', '-C', str(control), 'rev-parse', 'HEAD'], deadline=60).strip()
        if head != args.expected_commit or os.environ.get('GITHUB_SHA') != args.expected_commit:
            raise RuntimeError('Actual original source/control head mismatch')
        tree = command(label + '-tree', ['/usr/bin/git', '-C', str(control), 'ls-tree', '-rz', 'HEAD'], deadline=60)
        actual_files = {}; actual_links = {}
        for entry in tree.split('\0'):
            if not entry: continue
            header, name = entry.split('\t', 1); mode, kind, oid = header.split()
            if mode == '160000': actual_links[name] = oid
            elif mode in {'100644', '100755'} and kind == 'blob': actual_files[name] = (mode, oid)
            else: raise RuntimeError('Unexpected original tracked type')
        if actual_files.pop(args.manifest, None) is None: raise RuntimeError('Self cut is not tracked')
        expected = {}
        for name, row in control_paths.items():
            relative = P(name)
            if relative.is_absolute() or '..' in relative.parts or str(relative) != name:
                raise RuntimeError('Unsafe whole cut path')
            source = control / name
            if source.is_symlink() or not source.is_file(): raise RuntimeError('Whole tracked source not regular')
            data = source.read_bytes()
            blob = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
            mode = row.get('mode', row.get('gitMode'))
            if len(data) != row['bytes'] or hashlib.sha256(data).hexdigest() != row['sha256'] or blob != row['object']:
                raise RuntimeError('Complete actual tracked byte/blob mismatch: ' + name)
            expected[name] = (mode, blob)
        if actual_files != expected or actual_links != {r['path']: r['commit'] for r in control_cut['gitlinks']} or len(actual_links) != 22:
            raise RuntimeError('Complete tracked map/22 gitlinks mismatch')
        if control_cut['materializedGitlinks'] != home_catalog['materializedGitlinks']:
            raise RuntimeError('Exact three source-built dependency pins required')
        for i, row in enumerate(control_cut['materializedGitlinks']):
            actual = command(label + '-dependency-' + str(i), ['/usr/bin/git', '-C', str(control / row['path']), 'rev-parse', 'HEAD'], deadline=60).strip()
            dirty = command(label + '-dependency-clean-' + str(i), ['/usr/bin/git', '-C', str(control / row['path']), 'status', '--porcelain', '--untracked-files=no'], deadline=60).strip()
            if actual != row['commit'] or dirty: raise RuntimeError('Materialized source pin/dirt mismatch')
        command(label + '-source-preparation-check', ['/usr/bin/bash', str(control / '9to1 Workspace/shared/eng/prepare-cui-source.sh'), '--check'], deadline=60)

    def retained(file, directory):
        file = P(file).resolve()
        if not file.is_relative_to(root) or file.is_symlink() or not file.is_file(): raise RuntimeError('Regular actual output under root required')
        target = directory / file.relative_to(root)
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            if digest(target) != digest(file): raise RuntimeError('Retained original path changed')
        else:
            if budget() + file.stat().st_size > MAX_EVIDENCE: raise RuntimeError('Original retained output budget refused')
            shutil.copyfile(file, target)
            handle = None; primary = None; cleanup = []
            try:
                handle = target.open('rb')
                os.fsync(handle.fileno())
            except BaseException as error: primary = error
            finally:
                if handle is not None: collect(cleanup, handle.close)
            fail(primary, cleanup)
        if digest(target) != digest(file): raise RuntimeError('Original retained copy differs')
        return {'path': str(file.relative_to(root)), 'bytes': file.stat().st_size, 'sha256': digest(file),
                'retained': str(target.relative_to(out))}

    def regular_closure(directory):
        rows = []
        for file in sorted(directory.rglob('*')):
            if file.is_symlink(): raise RuntimeError('Actual compiled output symlink refused')
            if file.is_file(): rows.append({'path': str(file.relative_to(root)), 'bytes': file.stat().st_size, 'sha256': digest(file)})
        return rows



    inventory = load_module(root / '.github/scripts/astra-joint-sdk-test-inventory.py', 'joint_source_inventory')

    def verify_trx(suite, phase, discovery):
        import collections, xml.etree.ElementTree as ET
        directory = out / phase / 'trx' / suite['name']
        files = list(directory.glob('*.trx'))
        if len(files) != 1: raise RuntimeError('One complete original TRX required')
        tree = ET.parse(files[0]).getroot()
        ns = '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
        counters = tree.find('.//' + ns + 'Counters')
        if counters is None: raise RuntimeError('Original whole TRX counters missing')
        counts = {key: int(value) for key, value in counters.attrib.items()}
        results = tree.findall('.//' + ns + 'UnitTestResult')
        if not results or counts.get('total') != len(results) or counts.get('executed') != len(results) or counts.get('passed') != len(results):
            raise RuntimeError('Every discovered original test must execute and pass')
        if any(counts.get(key, 0) for key in ('failed','error','timeout','aborted','inconclusive','notExecuted','notRunnable','skipped')) or any(row.get('outcome') != 'Passed' for row in results):
            raise RuntimeError('Whole project failure/skip/nonexecution refused')
        if collections.Counter(discovery) != collections.Counter(row.get('testName') for row in results):
            raise RuntimeError('Whole original discovery and TRX case multiplicities differ')
        definitions = {}
        for definition in tree.findall('.//' + ns + 'UnitTest'):
            method = definition.find(ns + 'TestMethod')
            if method is None or definition.get('id') in definitions: raise RuntimeError('Original test definition missing/duplicate')
            definitions[definition.get('id')] = (method.get('className'), method.get('name'))
        passed = collections.Counter()
        for result in results:
            if result.get('testId') not in definitions: raise RuntimeError('Actual result lacks original method definition')
            passed[definitions[result.get('testId')]] += 1
        required = []
        project_directory = str(P(suite['project']).parent) + '/'
        for fixture in catalog['testFixtures']:
            if not fixture['path'].startswith(project_directory): continue
            actual = inventory.fixture_inventory((root / fixture['path']).read_text())
            if actual != fixture['methods']: raise RuntimeError('Whole selected source method minimum changed')
            required.extend(actual)
        for row in required:
            if passed[(row['fullClass'],row['method'])] < row['minimumPassedCases']:
                raise RuntimeError('Declared original method/case minimum missing: ' + row['fullClass'] + '.' + row['method'])
        proof = {'project':suite['project'],'configuration':phase,'counts':counts,'discoveryCases':len(discovery),
                 'requiredSourceMethods':len(required),'passedMethods':[{'fullClass':c,'method':m,'cases':n} for (c,m),n in sorted(passed.items())],
                 'originalTrx':retained(files[0],out / phase / 'original-trx')}
        save(out / phase / (suite['name'] + '-whole-test-proof.json'),proof)
        return proof

    def discover_original(suite, props, phase):
        output = command(phase + '-discovery-' + suite['name'], ['dotnet','test',suite['project'],'--no-build','--no-restore','--nologo','--list-tests',*props,'-p:TargetFramework=net10.0'])
        marker = 'The following Tests are available:'
        if output.count(marker) != 1: raise RuntimeError('Unique complete SDK discovery heading required')
        tail = output.split(marker,1)[1].splitlines()
        names = [line[4:] for line in tail if line.startswith('    ') and line[4:].strip()]
        if not names or any(name != name.strip() for name in names): raise RuntimeError('Ordinary exact whole discovery names required')
        save(out / phase / (suite['name'] + '-original-discovery.json'),names)
        return names

    def full_home_fixture_inventory(source):
        """Bind every attributed original method to its lexical enclosing test class."""
        code = inventory.masked(source)
        rows = inventory.fixture_inventory(source)
        namespaces = re.findall(r'\bnamespace\s+([A-Za-z_][\w.]*)\s*[;{]', code)
        if len(namespaces) != 1:
            raise RuntimeError('Exact original Home namespace required')
        stack = []; ranges = {}
        for offset, character in enumerate(code):
            if character == '{': stack.append(offset)
            elif character == '}':
                if not stack: raise RuntimeError('Original Home lexical closing brace has no owner')
                ranges[stack.pop()] = offset
        if stack: raise RuntimeError('Original Home lexical class body incomplete')
        classes = []
        for declaration in re.finditer(r'\bclass\s+([A-Za-z_]\w*)', code):
            opening = code.find('{', declaration.end())
            semicolon = code.find(';', declaration.end())
            if opening < 0 or (semicolon >= 0 and semicolon < opening):
                continue
            if opening not in ranges:
                raise RuntimeError('Original Home class body has no exact lexical boundary')
            classes.append((opening, ranges[opening], declaration.group(1)))
        pattern = re.compile(r'((?:\s*\[[^]\n]*\]\s*)+)public\s+(?:(?:async|static|new|virtual|override|sealed)\s+)*(?:Task(?:<[^>]+>)?|ValueTask(?:<[^>]+>)?|void)\s+([A-Za-z_]\w*)\s*\(', re.M)
        methods = []
        for declaration in pattern.finditer(code):
            if not re.search(r'\[\s*(?:[\w.]+\.)?(?:Fact|Theory)(?:Attribute)?\b', declaration.group(1)):
                continue
            owners = sorted((item for item in classes if item[0] < declaration.start(2) < item[1]), key=lambda item: item[0])
            if not owners or len({item[0] for item in owners}) != len(owners):
                raise RuntimeError('Attributed original Home method has no unique lexical class')
            if any(not (outer[0] < inner[0] < inner[1] < outer[1]) for outer, inner in zip(owners, owners[1:])):
                raise RuntimeError('Original Home enclosing class ownership ambiguous')
            methods.append((declaration.group(2), namespaces[0] + '.' + '+'.join(item[2] for item in owners)))
        if len(methods) != len(rows) or any(row['method'] != method for row, (method, _) in zip(rows, methods)):
            raise RuntimeError('Whole original Home lexical method inventory differs')
        return [dict(row, fullClass=owner) for row, (_, owner) in zip(rows, methods)]

    def verify_complete_home_cases(suite, phase, discovery):
        import collections, xml.etree.ElementTree as ET
        contract = catalog['homeValidation']
        original = contract['originalRuntimeCases']
        compressed = base64.b64decode(original['zlibBase64'], validate=True)
        decoder = zlib.decompressobj()
        encoded = decoder.decompress(compressed, 1024 * 1024 + 1)
        if not decoder.eof or decoder.unused_data or decoder.unconsumed_tail or len(encoded) != original['decodedBytes'] or hashlib.sha256(encoded).hexdigest() != original['decodedSha256']:
            raise RuntimeError('Complete original Home runtime inventory bounded identity differs')
        cases = json.loads(encoded)
        if len(cases) != 432: raise RuntimeError('Whole original432 Home outcomes required')
        expected = cases + contract['newRuntimeCases']
        if len(expected) != 437 or len({(r['displayName'],r['class'],r['method']) for r in expected}) != 437:
            raise RuntimeError('Whole437 original and five meaningful Home outcomes required')
        if collections.Counter(discovery) != collections.Counter(r['displayName'] for r in expected):
            raise RuntimeError('Whole original432 plus new5 actual Home discovery differs')
        directory = out / phase / 'trx' / suite['name']
        files = list(directory.glob('*.trx'))
        if len(files) != 1: raise RuntimeError('One whole Home TRX required')
        document = ET.parse(files[0]).getroot()
        ns = '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
        definitions = {}
        for row in document.findall('.//' + ns + 'UnitTest'):
            method = row.find(ns + 'TestMethod')
            if method is None or row.get('id') in definitions: raise RuntimeError('Home test definition duplicate/missing')
            definitions[row.get('id')] = (method.get('className'),method.get('name'))
        actual = []
        for row in document.findall('.//' + ns + 'UnitTestResult'):
            if row.get('testId') not in definitions or row.get('outcome') != 'Passed':
                raise RuntimeError('Actual original Home result unavailable/not passed')
            full_class,method = definitions[row.get('testId')]
            actual.append((row.get('testName'),full_class,method))
        expected_rows = [(r['displayName'],r['class'],r['method']) for r in expected]
        if collections.Counter(actual) != collections.Counter(expected_rows):
            raise RuntimeError('Whole Home case identity multiplicities differ')
        if len({(c,m) for _,c,m in actual}) != 334:
            raise RuntimeError('Every original330 plus new4 Home method must execute')
        source_methods = []
        for fixture in contract['fullHomeSources']:
            actual_methods = full_home_fixture_inventory((root / fixture['path']).read_text())
            if actual_methods != fixture['methods']: raise RuntimeError('Whole original Home source method inventory changed')
            source_methods.extend(actual_methods)
        expected_methods = collections.Counter((c,m) for _,c,m in expected_rows)
        if len(source_methods) != 334 or sum(r['minimumPassedCases'] for r in source_methods) != 437 or len({(r['fullClass'],r['method']) for r in source_methods}) != 334:
            raise RuntimeError('Full71 Home source334methods437minima differs')
        if {(r['fullClass'],r['method']):r['minimumPassedCases'] for r in source_methods} != dict(expected_methods):
            raise RuntimeError('Every physical Home source case and actual original runtime identity must agree')
        proof = {'originalRuntimeCases':432,'newRuntimeCases':5,'totalRuntimeCases':437,'allSourceFiles':71,'allMethods':334,
            'allPassed':True,'allCaseIdentities':[{'displayName':n,'class':c,'method':m} for n,c,m in sorted(actual)],
            'originalInventoryPin':{k:v for k,v in original.items() if k!='zlibBase64'},
            'qualification':'One complete unfiltered maintained Home project only; original432 cases plusfive newsource outcomes. No full SDK, other cohort or installed/native acceptance.'}
        save(out / phase / 'whole-home437-identity-proof.json',proof)
        return proof

    restore_output = root / 'artifacts/desktop-visible-owning'

    def snapshot_original_restore(suite, phase, stage, managed, host):
        """Preserve the unchanged helper context and whole pre/post diagnostic bodies."""
        primary = None; cleanup = []; snapshot = None; pre_rows = None; pre_retained = None
        def capture_diagnostics(observation):
            if restore_output.is_symlink() or not restore_output.is_dir() or restore_output.resolve() != root / 'artifacts/desktop-visible-owning':
                raise RuntimeError('Same original restore diagnostic context changed')
            rows = regular_closure(restore_output)
            retained_rows = []; capture_errors = []
            for row in rows:
                def retain_one(row=row):
                    retained_rows.append(retained(root / row['path'],
                        out / phase / (suite['name'] + '-' + stage + '-' + observation + '-original-restore-diagnostics')))
                collect(capture_errors, retain_one)
            def verify_same_rows():
                if regular_closure(restore_output) != rows:
                    raise RuntimeError('Complete original restore diagnostics changed during retention')
            collect(capture_errors, verify_same_rows)
            fail(None, capture_errors)
            return rows, retained_rows
        try:
            pre_rows, pre_retained = capture_diagnostics('pre-call')
            snapshot = restore.snapshot_restore(root, suite['project'], extra_projects=[TASK_PROJECT],
                evidence_cohort='owning', evidence_output=restore_output,
                managed_artifacts=managed, host_artifacts=host)
        except BaseException as error: primary = error
        finally:
            def retain_diagnostics():
                post_rows, post_retained = capture_diagnostics('post-call')
                if snapshot is not None and not post_rows:
                    raise RuntimeError('Original successful restore diagnostic evidence unavailable')
                before_by_path = {row['path']: row for row in (pre_rows or [])}
                after_by_path = {row['path']: row for row in post_rows}
                save(out / phase / (suite['name'] + '-' + stage + '-restore-diagnostic-witness.json'),
                    {'originalEvidenceContext': str(restore_output), 'originalContextRoot': str(root),
                     'cohort': 'owning', 'preCallInventoryCaptured': pre_rows is not None,
                     'completePreCallFiles': pre_rows, 'retainedPreCallFiles': pre_retained,
                     'completePostCallFiles': post_rows, 'retainedPostCallFiles': post_retained,
                     'identicalPreexistingPaths': sorted(path for path in before_by_path if before_by_path[path] == after_by_path.get(path)),
                     'newOrChangedPostCallPaths': sorted(path for path in after_by_path if after_by_path[path] != before_by_path.get(path)),
                     'removedPreCallPaths': sorted(set(before_by_path) - set(after_by_path)),
                     'helperReturnedSnapshot': snapshot is not None,
                     'qualification': 'Whole diagnostic bodies retained before and after this unchanged helper call. Identical preexisting rows are not attributed as fresh query stdout. Helper/current query/graph/package/source guards remain unchanged; refusal never becomes a restore PASS.'})
            collect(cleanup, retain_diagnostics)
            collect(cleanup, protect_selected)
            collect(cleanup, budget)
        fail(primary, cleanup)
        return snapshot

    pdb = load_module(root / '.github/scripts/astra-home-portable-pdb.py', 'joint_actual_pdb')
    restore = load_module(root / '.github/scripts/astra-joint-sdk-restore-evidence.py', 'joint_actual_restore')
    real_subprocess = subprocess
    query_number = 0
    primary = None; cleanup = []
    try:
        os.environ.update({'AVALONIA_TELEMETRY_OPTOUT': '1', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1',
            'DOTNET_CLI_USE_MSBUILD_SERVER': '0', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
            'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'MSBUILDDISABLENODEREUSE': '1', 'COHORT': 'owning'})
        if restore_output.exists() or restore_output.is_symlink():
            raise RuntimeError('Fresh unchanged restore evidence context required for this checkout')
        restore_output.mkdir(parents=True, mode=0o700)
        verify_control_whole('control-before')
        verify_whole('comparison-before')
        command('toolchain', ['dotnet', '--info'], deadline=60)
        command('workloads', ['dotnet', 'workload', 'list'], deadline=60)

        entries = [{'name': 'home-full', 'project': '9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj', 'kind': 'test'}]
        for configuration in [args.configuration]:
            if configuration not in ('Debug','Release'): raise RuntimeError('Unsupported configuration')
            phase = configuration.lower() + '-home'
            managed = root / 'artifacts/root14-managed-build'; host = root / 'artifacts/root14-host-build-tasks'
            common = ['-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:AvsSkipBuildingLegacyTargetFrameworks=True',
                      '-p:EnableWindowsTargeting=true', '-p:Configuration=' + configuration]
            output_props = ['-p:UseArtifactsOutput=true', '-p:ArtifactsPath=' + str(managed), '-p:IncludeProjectNameInArtifactsPaths=true']
            managed_props = [*common, *output_props, '-p:RuntimeIdentifier=linux-x64', '-p:RuntimeIdentifiers=linux-x64', '-p:SelfContained=false']
            host_props = [*common, '-p:TargetFramework=netstandard2.0', '-p:UseArtifactsOutput=true',
                          '-p:ArtifactsPath=' + str(host), '-p:IncludeProjectNameInArtifactsPaths=true']
            for suite in entries:
                command(phase + '-restore-' + suite['name'], ['dotnet', 'restore', suite['project'], '--disable-build-servers', *managed_props, '-p:TargetFramework=net10.0'])
            command(phase + '-restore-source-tasks', ['dotnet', 'restore', TASK_PROJECT, '--disable-build-servers', *host_props])
            for index, project in enumerate(TOOLS):
                command(phase + '-restore-analyzer-' + str(index), ['dotnet', 'restore', project, '--disable-build-servers', *managed_props, '-p:TargetFramework=netstandard2.0'])
            command(phase + '-build-source-tasks', ['dotnet', 'build', TASK_PROJECT, '--no-restore', '--disable-build-servers', *host_props])
            def query(project, props, label, items=False):
                argv = ['dotnet', 'msbuild', project, '-nologo', *props,
                    '-getProperty:MSBuildProjectFullPath,MSBuildProjectName,AssemblyName,TargetPath,OutputPath,Configuration,TargetFramework,ProjectAssetsFile,MSBuildProjectExtensionsPath,AvaloniaBuildTasksLocation,IsTestProject,OutputType,DebugType,DefineConstants,LangVersion']
                if items: argv += ['-target:ResolveReferences', '-getItem:Compile,ProjectReference,ReferencePath']
                return json.loads(command(phase + '-evaluate-' + label, argv))
            task_evaluated = query(TASK_PROJECT, host_props, 'source-tasks', True)
            task_target = P(task_evaluated['Properties']['TargetPath']).resolve()
            if task_evaluated['Properties']['Configuration'] != configuration or task_evaluated['Properties']['TargetFramework'] != 'netstandard2.0' or not task_target.is_relative_to(root):
                raise RuntimeError('Source-built task exact configuration/framework/root mismatch')
            task_before = regular_closure(task_target.parent)
            task_retained = [retained(root / row['path'], out / phase / 'original-source-tasks') for row in task_before]
            task_property = '-p:AvaloniaBuildTasksLocation=' + str(task_target)
            managed_props.append(task_property)
            os.environ['ASTRA_ACTUAL_AVALONIA_BUILD_TASKS'] = str(task_target)

            # The unchanged restore helper issues only metadata queries. This per-module
            # synchronous adapter substitutes exactly one configuration literal, records
            # actual argv, and routes each SAME query through original process custody.
            restore_queries = {}
            def restored_run(argv, *, capture_output, text):
                nonlocal query_number
                if capture_output is not True or text is not True or argv[:2] != ['dotnet', 'msbuild'] or argv.count('-p:Configuration=Release') != 1:
                    raise RuntimeError('Unexpected unchanged restore metadata API')
                actual = [('-p:Configuration=' + configuration) if value == '-p:Configuration=Release' else value for value in argv]
                query_number += 1
                stdout = command(phase + '-restore-metadata-' + str(query_number), actual)
                evaluated = json.loads(stdout)['Properties']
                if evaluated['Configuration'] != configuration: raise RuntimeError('Actual restore metadata configuration differs')
                query_key = (str(P(evaluated['MSBuildProjectFullPath']).resolve()),
                    next(value for value in actual if value.startswith('-p:ArtifactsPath=')))
                restore_queries[query_key] = actual
                return real_subprocess.CompletedProcess(actual, 0, stdout, '')
            restore.subprocess = types.SimpleNamespace(run=restored_run)
            # Preserve genuine evaluated reference/Compile/marker evidence even when the later original build refuses.
            for suite in entries:
                prebuild = query(suite['project'], [*managed_props, '-p:TargetFramework=net10.0'], suite['name'] + '-prebuild', True)
                save(out / phase / (suite['name'] + '-prebuild-evaluation.json'), prebuild)
                command(phase + '-build-' + suite['name'], ['dotnet', 'build', suite['project'], '--no-restore', '--disable-build-servers', *managed_props, '-p:TargetFramework=net10.0'])
            graph_snapshots = []
            compiled = []
            owned_pdb_sources = set()
            selected_compile_sources = set()
            home_proofs = []
            whole_test_proofs = []
            for suite in entries:
                name = suite['name']
                before = snapshot_original_restore(suite, phase, 'before', managed, host)
                graph_snapshots.append((suite, before))
                save(out / phase / (name + '-restore-before.json'), before)
                seen = set()
                for project_row in before['projects']:
                    project = project_row['path']
                    key = (project, project_row['hostContext'])
                    if key in seen: continue
                    seen.add(key)
                    query_key = (str((root / project).resolve()),
                        '-p:ArtifactsPath=' + str(host if project_row['hostContext'] else managed))
                    actual_restore_query = restore_queries[query_key]
                    props = actual_restore_query[4:-1]
                    evaluated = query(project, props, name + '-' + str(len(seen)), True)
                    values = evaluated['Properties']
                    if P(values['MSBuildProjectFullPath']).resolve() != (root / project).resolve() or values['Configuration'] != configuration:
                        raise RuntimeError('Actual compiled project/configuration mismatch')
                    if P(values['AvaloniaBuildTasksLocation']).resolve() != task_target:
                        raise RuntimeError('Actual original consumer build-task location differs')
                    target = P(values['TargetPath']).resolve()
                    if not target.is_relative_to(root):
                        raise RuntimeError('Complete actual first-party project physical PE outside root: ' + project)
                    pair, identity, documents, symbol_storage = physical_symbols(target, values['DebugType'], pdb)
                    inputs = []
                    for item in evaluated.get('Items', {}).get('Compile', []):
                        source = P(item.get('FullPath', ''))
                        if not source.is_absolute(): source = (root / project).parent / item['Identity']
                        source = source.resolve()
                        if not source.is_relative_to(root) or source.is_symlink() or not source.is_file():
                            raise RuntimeError('Actual evaluated compile source unavailable/foreign')
                        relative = str(source.relative_to(root)); matches = [key for key in documents if key.replace('\\', '/').endswith('/' + relative)]
                        row = {'path': relative, 'bytes': source.stat().st_size, 'sha256': digest(source), 'pdbDocument': matches}
                        if relative in paths and row['sha256'] != paths[relative]['sha256']:
                            raise RuntimeError('Actual evaluated tracked source differs')
                        if relative in {f['path'] for f in catalog['sources']} and relative not in catalog['typeOnlyPdbExceptions'] and not matches:
                            raise RuntimeError('Selected behavioural source must appear in actual original PDB: ' + relative)
                        if relative not in paths: row['retainedOriginalSource'] = retained(source, out / phase / 'original-generated-or-materialized-sources')
                        inputs.append(row)
                        selected_compile_sources.add(relative)
                    proof_docs = []
                    for name_in_pdb, row in documents.items():
                        source = P(name_in_pdb.replace('\\', '/'))
                        if not source.is_absolute(): source = root / source
                        source = source.resolve()
                        # Deterministic source paths may use a mapped root. Bind to
                        # exactly one full evaluated original path, never a basename.
                        if not source.is_relative_to(root) or not source.is_file():
                            matches = [root / item['path'] for item in inputs
                                if name_in_pdb.replace('\\', '/').endswith('/' + item['path'])]
                            if len(matches) != 1: raise RuntimeError('Mapped actual PDB document ambiguous/missing')
                            source = matches[0]
                        if not source.is_relative_to(root) or source.is_symlink() or not source.is_file() or hashlib.new(row['hashName'], source.read_bytes()).hexdigest() != row['digest']:
                            raise RuntimeError('Complete actual PDB document missing/different/foreign: ' + name_in_pdb)
                        relative = str(source.relative_to(root))
                        owned_pdb_sources.add(relative)
                        source_pin = None if relative in paths else retained(source, out / phase / 'original-generated-or-materialized-sources')
                        proof_docs.append({'document': name_in_pdb, 'source': relative, 'retainedOriginalSource': source_pin, **row})
                    if not inputs or not proof_docs: raise RuntimeError('Actual complete source/PDB evidence empty')
                    original_pairs = [retained(file, out / phase / 'compiled') for file in pair]
                    retained_identity = [{'path': str(root / row['path']), 'bytes': row['bytes'], 'sha256': row['sha256']} for row in original_pairs]
                    if retained_identity != symbol_storage['physicalFiles']:
                        raise RuntimeError('Retained original physical PE/symbol bytes differ from parsed originals')
                    compiled.append({'suite': suite['name'], 'project': project, 'evaluated': evaluated,
                        'physicalAssembly': values['AssemblyName'], 'identity': identity, 'pairs': original_pairs,
                        'symbolStorage': symbol_storage, 'compileInputs': inputs, 'allPdbDocuments': proof_docs})
                suite_evaluated = query(suite['project'], [*managed_props, '-p:TargetFramework=net10.0'], name + '-entry', True)
                values = suite_evaluated['Properties']
                if str(values.get('IsTestProject','')).lower() != 'true' or values['TargetFramework'] != 'net10.0' or values['LangVersion'] not in ('14','14.0'):
                    raise RuntimeError('Actual Home project kind/framework/language differs')
                target = P(values['TargetPath']).resolve()
                closure_before = regular_closure(target.parent)
                references = []
                for reference in suite_evaluated.get('Items', {}).get('ReferencePath', []):
                    source = P(reference.get('FullPath', reference['Identity']))
                    if not source.is_absolute(): source = (root / suite['project']).parent / source
                    if source.is_symlink() or not source.is_file():
                        raise RuntimeError('Actual resolved reference is missing/indirect; no alias admitted')
                    source = source.resolve()
                    physical = {'path': str(source), 'bytes': source.stat().st_size, 'sha256': digest(source), 'evaluated': reference}
                    if source.is_relative_to(root): physical['retainedOriginalReference'] = retained(source, out / phase / 'resolved-project-references')
                    references.append(physical)
                if not references: raise RuntimeError('Actual physical resolved references unavailable')
                entry_rows = [row for row in compiled if row['suite'] == suite['name'] and row['project'] == suite['project']]
                if len(entry_rows) != 1: raise RuntimeError('Exact original Home entry physical proof required')
                branch_sources = {row['path'] for row in entry_rows[0]['compileInputs']}
                branch_documents = {row['source'] for row in entry_rows[0]['allPdbDocuments']}
                required_sources = {row['path'] for row in catalog['homeValidation']['fullHomeSources']}
                if required_sources - branch_sources or required_sources - branch_documents:
                    raise RuntimeError('Every exact affected source must compile and appear in THIS branch PDB')
                proof = {'project': suite['project'], 'configuration': configuration, 'actualEvaluation': suite_evaluated,
                    'resolvedReferences': references, 'requiredFullHomeSources': sorted(required_sources),
                    'qualification': 'Complete actual Home dependency graph source/PE-PDB proof; all test outcomes must independently execute.'}
                home_proofs.append(proof)
                save(out / phase / (suite['name'] + '-owning-source-proof.json'), proof)
                discovery = discover_original(suite,managed_props,phase)
                results_directory = out / phase / 'trx' / suite['name']
                command(phase + '-whole-' + suite['name'],
                    ['dotnet','test',suite['project'],'--no-build','--no-restore','--nologo',
                     '--logger','trx;LogFileName=' + suite['name'] + '.trx',
                     '--results-directory',str(results_directory),*managed_props,'-p:TargetFramework=net10.0'],
                    deadline=1200)
                whole_test_proofs.append({'wholeOriginalTrx':verify_trx(suite,phase,discovery),
                    'completeHomeCaseIdentity':verify_complete_home_cases(suite,phase,discovery)})
                if regular_closure(target.parent) != closure_before:
                    raise RuntimeError('Actual complete Home entry output changed during proof')
                for reference in references:
                    physical = P(reference['path'])
                    if physical.is_symlink() or not physical.is_file() or physical.stat().st_size != reference['bytes'] or digest(physical) != reference['sha256']:
                        raise RuntimeError('Same actual resolved Home reference changed during proof')
                after = snapshot_original_restore(suite, phase, 'after', managed, host)
                save(out / phase / (name + '-restore-after.json'), after)
                if after != before: raise RuntimeError('Complete restored graph/package evidence changed during original suite')
                if regular_closure(task_target.parent) != task_before: raise RuntimeError('Source-built task output changed during original suite')
                save(out / phase / (name + '-compiled-closure.json'), closure_before)
            if len(home_proofs) != 1 or len(whole_test_proofs) != 1: raise RuntimeError('Exactly one complete whole Home437 project required for this lane')
            required_production = {row['path'] for row in catalog['homeValidation']['correctiveSources'] if '/Source/' in row['path']}
            if required_production - selected_compile_sources or required_production - owned_pdb_sources:
                raise RuntimeError('Both corrected Home productions must appear in actual evaluated Compile and genuine original PDB')
            save(out / phase / 'whole-home-test-proof.json',whole_test_proofs)
            save(out / phase / 'complete-compiled-source-pairs.json', compiled)
            save(out / phase / 'source-built-task-closure.json', {'evaluated': task_evaluated, 'retained': task_retained,
                'before': task_before, 'after': regular_closure(task_target.parent)})
            observations['configurations'].append({'configuration': configuration, 'project': entries[0]['project'],
                'completeCompiledProjects': len(compiled), 'sourceBuiltTasksRetained': True,
                'originalBuildExitedZero': True, 'wholeHomeCases':437,'wholeHomeMethods':334,
                'originalHomeCasesRetained':432,'newSourceCases':5,'otherTestsRun':0,'nativeRun':0})
            verify_whole(phase + '-comparison-after')
            verify_control_whole(phase + '-control-after')
        observations['status'] = 'ACTUAL_WHOLE_HOME437_LANE_PASS_OTHER_SDK_NATIVE_UNACCEPTED'
    except BaseException as error: primary = error
    finally:
        collect(cleanup, protect_selected)
        accepted_bytes = collect(cleanup, budget)
        observations['acceptedEvidenceBytes'] = accepted_bytes
        observations['primaryType'] = None if primary is None else type(primary).__name__
        observations['cleanupTypes'] = [type(error).__name__ for error in cleanup]
        if primary is not None or cleanup: observations['status'] = 'ACTUAL_WHOLE_HOME437_REFUSED_OR_FAILED'
        collect(cleanup, lambda: save(out / 'receipt.json', observations))
    fail(primary, cleanup)


if __name__ == '__main__': main()
