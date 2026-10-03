"""Additive whole joint SDK gate; old native cohorts remain independently mandatory."""
import argparse, base64, hashlib, importlib.util, json, os, pathlib, re, shutil
import signal, stat, subprocess, sys, time, types

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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--manifest-sha', required=True)
    args = parser.parse_args()
    root = P.cwd().resolve()
    out = root / 'artifacts/desktop-visible-owning/joint-sdk'
    if out.exists() or out.is_symlink(): raise RuntimeError('Fresh whole owning evidence directory required')
    out.mkdir(parents=True, mode=0o700)
    if args.manifest != '.github/validation/astra-desktop-visible-cut.json':
        raise RuntimeError('Exact self manifest path required')
    cut_path = root / args.manifest
    if not re.fullmatch('[0-9a-f]{40}', args.expected_commit) or digest(cut_path) != args.manifest_sha:
        raise RuntimeError('Exact externally issued head/cut pin required')
    cut = json.loads(cut_path.read_text())
    catalog_path = root / '.github/validation/astra-joint-native-sdk01-sources.json'
    catalog = json.loads(catalog_path.read_text())
    if catalog['normalCommit'] != cut['currentNormalCommit'] or catalog['sourceStatus'] != 'JOINT05_REVIEWED_SOURCE_ONLY_UNCOMPILED_UNRUN':
        raise RuntimeError('Exact current normal and reviewed joint05 required')
    paths = {row['path']: row for row in cut['files']}
    if len(paths) != len(cut['files']) or args.manifest in paths:
        raise RuntimeError('Complete cut duplicate/self file refused')
    selected = catalog['sources'] + catalog['helpers']
    selected += [{'path': '.github/scripts/astra-joint-native-sdk01-owning.py', 'sha256': digest(__file__)},
                 {'path': str(catalog_path.relative_to(root)), 'sha256': digest(catalog_path)}]
    for row in selected:
        if paths.get(row['path'], {}).get('sha256') != row['sha256']:
            raise RuntimeError('Selected source/helper missing from actual complete cut: ' + row['path'])
    for row in catalog['baselineGuards']:
        if paths.get(row['path'],{}).get('sha256') != row['sha256']:
            raise RuntimeError('Inherited original cohort/helper guard changed: ' + row['path'])
    if catalog['requiredSdkConfigurations'] != ['Debug','Release']:
        raise RuntimeError('Both ordered independent configurations required')
    observations_source_qualification = catalog['qualification']
    helper = root / '.github/scripts/astra-joint-sdk-original-session-drain.py'
    guard = load_module(helper, 'joint_original_session')
    OriginalSession = guard.OriginalSession
    if not callable(getattr(os, 'pidfd_open', None)) or not callable(getattr(signal, 'pidfd_send_signal', None)):
        raise RuntimeError('Linux original creator pidfd required')
    observations = {'status': 'WHOLE_JOINT_SDK_PENDING', 'commands': [], 'configurations': [],
                    'head': args.expected_commit, 'cutSha256': args.manifest_sha,
                    'qualification': 'Whole Linux SDK/headless joint suites only; no ordinary installed Home/bootstrap/actor/provider/Agent-route/native product acceptance'}

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
        if digest(cut_path) != args.manifest_sha: raise RuntimeError('Issued original cut changed')
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
        if head != args.expected_commit or os.environ.get('GITHUB_SHA', head) != head:
            raise RuntimeError('Actual original exact head mismatch')
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



    native_metadata_number = native_verify_number = native_state_number = 0
    fresh_native = native_locks = None
    def assert_native_unchanged():
        nonlocal native_state_number
        if fresh_native is None or native_locks is None: raise RuntimeError('Original native preparation unavailable')
        for path, expected in fresh_native + native_locks:
            if path.is_symlink() or not path.is_file() or digest(path) != expected:
                raise RuntimeError('Original fresh native library/loader/source lock changed')
        for row in catalog['nativeDonors']:
            native_state_number += 1
            actual = command('native-state-head-' + str(native_state_number),
                ['/usr/bin/git','-C',row['path'],'rev-parse','HEAD'],deadline=60).strip()
            dirty = command('native-state-clean-' + str(native_state_number),
                ['/usr/bin/git','-C',row['path'],'status','--porcelain','--untracked-files=no'],deadline=60).strip()
            if actual != row['commit'] or dirty: raise RuntimeError('Original renderer donor changed')

    def run_console_original(suite, target, phase):
        harness = catalog['consoleHarness']
        if suite['project'] != harness['project']: raise RuntimeError('Exact mandatory original console project required')
        for row in harness['sources']:
            source = root / row['path']
            if paths.get(row['path'],{}).get('sha256') != row['sha256'] or digest(source) != row['sha256']:
                raise RuntimeError('Original full console source differs from issued cut')
            if row['path'] not in selected_compile_sources or row['path'] not in owned_pdb_sources:
                raise RuntimeError('Original full console source must be compiled and emitted in actual PDB')
        domain = (root / harness['sources'][1]['path']).read_text()
        body = domain.split('public static async Task RunAllAsync()',1)[1].split('\n\t}',1)[0]
        calls = [{'method':name,'awaited':bool(awaited)} for awaited,name in re.findall(r'^\s*(await\s+)?(\w+)\(\);\s*$',body,re.M)]
        if calls != harness['orderedDomainCalls'] or len(calls) != 22:
            raise RuntimeError('Complete original console RunAllAsync call sequence differs')
        output = command(phase + '-whole-console-' + suite['name'],['dotnet',str(target)],deadline=1200)
        if output.splitlines().count(harness['successMarker']) != 1:
            raise RuntimeError('Unique actual whole console/interprocess success marker required')
        proof = {'project':suite['project'],'configuration':phase,'kind':'original-console-harness',
            'actualZeroArgumentEntry':['dotnet',str(target)],'originalDomainCalls':calls,
            'originalInterprocessCall':'FilesInterprocessTests.RunAsync','actualSuccessMarker':harness['successMarker'],
            'originalSourceRows':harness['sources'],
            'qualification':'Exit zero and source-bound final marker prove the complete sequential original harness returned. No per-case TRX/timing fabricated; actual descendant drain, physical assembly/source/restore guards separately mandatory.'}
        save(out / phase / (suite['name'] + '-whole-console-proof.json'),proof)
        return proof

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

    pdb = load_module(root / '.github/scripts/astra-home-portable-pdb.py', 'joint_actual_pdb')
    restore = load_module(root / '.github/scripts/astra-joint-sdk-restore-evidence.py', 'joint_actual_restore')
    real_subprocess = subprocess
    query_number = 0
    primary = None; cleanup = []
    try:
        os.environ.update({'AVALONIA_TELEMETRY_OPTOUT': '1', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1',
            'DOTNET_CLI_USE_MSBUILD_SERVER': '0', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
            'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'MSBUILDDISABLENODEREUSE': '1', 'COHORT': 'owning'})
        verify_whole('before')
        command('toolchain', ['dotnet', '--info'], deadline=60)
        command('workloads', ['dotnet', 'workload', 'list'], deadline=60)

        visual = out / 'actual-visual-capture'
        visual.mkdir()
        os.environ['HAVEN_VISUAL_CAPTURE_DIR'] = str(visual)
        command('original-fontconfig',['fc-match','-f','%{file}\n','sans'],deadline=60)
        command('original-font-packages',['dpkg-query','-W','fontconfig','libfontconfig1','fonts-dejavu-core'],deadline=60)
        native = load_module(root / '.github/scripts/astra-files-native-prerequisites.py','joint_original_native_prerequisites')
        def native_check_output(argv, **kwargs):
            nonlocal native_metadata_number
            if set(kwargs) - {'text'} or ('text' in kwargs and kwargs['text'] is not True):
                raise RuntimeError('Unexpected original native metadata query API')
            native_metadata_number += 1
            output = command('native-original-metadata-' + str(native_metadata_number),argv,deadline=60)
            return output if kwargs.get('text') is True else output.encode()
        native.subprocess = types.SimpleNamespace(check_output=native_check_output)
        def native_command(argv,name):
            command('native-' + name,argv,native_log_name=name)
            return 0
        def native_verify():
            nonlocal native_verify_number
            native_verify_number += 1
            verify_whole('native-after-' + str(native_verify_number))
        fresh_native,native_locks = native.prepare(root,out,digest,native_command,native_verify)
        assert_native_unchanged()
        save(out / 'original-native-output-pins.json',{'fresh':[{'path':str(path),'sha256':expected} for path,expected in fresh_native],
            'locks':[{'path':str(path),'sha256':expected} for path,expected in native_locks],
            'qualification':'Unchanged maintained renderer prepare body; same fresh source-built libraries/loader/config and ordinary sandbox. Full original native acceptance remains independent.'})

        entries = catalog['entryProjects']
        if catalog['originalCommandTemplates'] != catalog['frozenCommandTemplates'] or len(entries) != 11:
            raise RuntimeError('Exact inherited22 templates/11 entries required')
        for configuration in catalog['requiredSdkConfigurations']:
            if configuration not in ('Debug','Release'): raise RuntimeError('Unsupported configuration')
            phase = configuration.lower()
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
                    '-getProperty:MSBuildProjectFullPath,MSBuildProjectName,AssemblyName,TargetPath,OutputPath,Configuration,TargetFramework,ProjectAssetsFile,MSBuildProjectExtensionsPath,AvaloniaBuildTasksLocation,IsTestProject,OutputType']
                if items: argv.append('-getItem:Compile,ProjectReference')
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
            # Compile every actual suite graph before inspecting producer outputs.
            for suite in entries:
                command(phase + '-build-' + suite['name'], ['dotnet', 'build', suite['project'], '--no-restore', '--disable-build-servers', *managed_props, '-p:TargetFramework=net10.0'])
            graph_snapshots = []
            compiled = []
            owned_pdb_sources = set()
            selected_compile_sources = set()
            whole_test_proofs = []
            whole_console_proofs = []
            for suite in entries:
                name = suite['name']
                before = restore.snapshot_restore(root, suite['project'], extra_projects=[TASK_PROJECT], evidence_cohort='owning', evidence_output=out,
                    managed_artifacts=managed, host_artifacts=host)
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
                    target = P(values['TargetPath']).resolve(); pair = [target, target.with_suffix('.pdb')]
                    if not target.is_relative_to(root) or any(file.is_symlink() or not file.is_file() for file in pair):
                        raise RuntimeError('Complete actual first-party project physical PE/PDB missing: ' + project)
                    identity = pdb.assert_actual_pair(pair[0].read_bytes(), pair[1].read_bytes())
                    documents = pdb.pdb_documents(pair[1].read_bytes())
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
                    compiled.append({'suite': suite['name'], 'project': project, 'evaluated': evaluated,
                        'physicalAssembly': values['AssemblyName'], 'identity': identity, 'pairs': [retained(file, out / phase / 'compiled') for file in pair],
                        'compileInputs': inputs, 'allPdbDocuments': proof_docs})
                suite_evaluated = query(suite['project'], [*managed_props, '-p:TargetFramework=net10.0'], name + '-entry')
                values = suite_evaluated['Properties']
                is_test = str(values.get('IsTestProject','')).lower() == 'true'
                output_kind = str(values.get('OutputType','')).lower()
                if (suite['kind'] == 'test') != is_test or (suite['kind'] in ('console','executable') and output_kind != 'exe'):
                    raise RuntimeError('Actual evaluated mandatory entry kind differs')
                target = P(values['TargetPath']).resolve()
                closure_before = regular_closure(target.parent)
                if suite['kind'] == 'test':
                    discovery = discover_original(suite, managed_props, phase)
                    results_directory = out / phase / 'trx' / suite['name']
                    command(phase + '-whole-' + suite['name'], ['dotnet','test',suite['project'],'--no-build','--no-restore','--nologo',
                        '--logger','trx;LogFileName=' + suite['name'] + '.trx','--results-directory',str(results_directory),
                        *managed_props,'-p:TargetFramework=net10.0'],deadline=1200)
                    whole_test_proofs.append(verify_trx(suite,phase,discovery))
                elif suite['kind'] == 'console':
                    whole_console_proofs.append(run_console_original(suite,target,phase))
                elif suite['kind'] != 'executable': raise RuntimeError('Unknown mandatory entry kind')
                assert_native_unchanged()
                if regular_closure(target.parent) != closure_before: raise RuntimeError('Actual complete compiled suite output changed during original run')
                after = restore.snapshot_restore(root, suite['project'], extra_projects=[TASK_PROJECT], evidence_cohort='owning', evidence_output=out,
                    managed_artifacts=managed, host_artifacts=host)
                save(out / phase / (name + '-restore-after.json'), after)
                if after != before: raise RuntimeError('Complete restored graph/package evidence changed during original suite')
                if regular_closure(task_target.parent) != task_before: raise RuntimeError('Source-built task output changed during original suite')
                save(out / phase / (name + '-compiled-closure.json'), closure_before)
            required_cs = {row['path'] for row in catalog['sources'] if row['path'].endswith('.cs')}
            missing_compile = required_cs - selected_compile_sources
            missing_pdb = required_cs - set(catalog['typeOnlyPdbExceptions']) - owned_pdb_sources
            if missing_compile or missing_pdb: raise RuntimeError('Whole selected source coverage refused: compile=' + str(sorted(missing_compile)) + ' pdb=' + str(sorted(missing_pdb)))
            if len(whole_test_proofs) != 8 or len(whole_console_proofs) != 1: raise RuntimeError('All eight complete xUnit projects and original complete Files console required')
            save(out / phase / 'whole-console-proof.json',whole_console_proofs)
            save(out / phase / 'whole-test-proof.json',whole_test_proofs)
            save(out / phase / 'complete-compiled-source-pairs.json', compiled)
            save(out / phase / 'source-built-task-closure.json', {'evaluated': task_evaluated, 'retained': task_retained,
                'before': task_before, 'after': regular_closure(task_target.parent)})
            observations['configurations'].append({'configuration': configuration, 'wholeEntries': [suite['name'] for suite in entries], 'fullTestProjects':8, 'fullConsoleHarnesses':1, 'requiredSourceCSharp':len(required_cs), 'typeOnlyPdbExceptions':sorted(required_cs - owned_pdb_sources),
                'completeCompiledProjects': len(compiled), 'sourceBuiltTasksRetained': True, 'allOriginalSuitesExitedZero': True})
            verify_whole(phase + '-after')
        observations['status'] = 'ACTUAL_WHOLE_JOINT_DEBUG_RELEASE_SDK_PASS_NATIVE_ACCEPTANCE_PENDING'
    except BaseException as error: primary = error
    finally:
        collect(cleanup, protect_selected)
        accepted_bytes = collect(cleanup, budget)
        observations['acceptedEvidenceBytes'] = accepted_bytes
        observations['primaryType'] = None if primary is None else type(primary).__name__
        observations['cleanupTypes'] = [type(error).__name__ for error in cleanup]
        if primary is not None or cleanup: observations['status'] = 'ACTUAL_WHOLE_JOINT_SDK_REFUSED_OR_FAILED'
        collect(cleanup, lambda: save(out / 'receipt.json', observations))
    fail(primary, cleanup)


if __name__ == '__main__': main()
