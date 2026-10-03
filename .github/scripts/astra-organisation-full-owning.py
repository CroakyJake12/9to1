"""One isolated exact-source SDK gate. No Git mutation, credential or native UI claim."""
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
    if len(all_errors) > 1: raise BaseExceptionGroup('Original SDK/Web and independent custody failures', all_errors)
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
    out = root / 'artifacts/desktop-visible-owning'
    if out.exists() or out.is_symlink(): raise RuntimeError('Fresh whole owning evidence directory required')
    out.mkdir(parents=True, mode=0o700)
    if args.manifest != '.github/validation/astra-organisation-full-owning-cut.json':
        raise RuntimeError('Exact self manifest path required')
    cut_path = root / args.manifest
    if not re.fullmatch('[0-9a-f]{40}', args.expected_commit) or digest(cut_path) != args.manifest_sha:
        raise RuntimeError('Exact externally issued head/cut pin required')
    cut = json.loads(cut_path.read_text())
    catalog_path = root / '.github/validation/astra-organisation-owning-sources.json'
    catalog = json.loads(catalog_path.read_text())
    if catalog['normalCommit'] != cut['currentNormalCommit'] or catalog['sourceStatus'] != 'PRIVATE_SOURCE04_UNCOMPILED_UNRUN':
        raise RuntimeError('Exact current normal and reviewed source04 required')
    paths = {row['path']: row for row in cut['files']}
    if len(paths) != len(cut['files']) or args.manifest in paths:
        raise RuntimeError('Complete cut duplicate/self file refused')
    selected = catalog['sources'] + catalog['helpers']
    selected += [{'path': '.github/scripts/astra-organisation-full-owning.py', 'sha256': digest(__file__)},
                 {'path': str(catalog_path.relative_to(root)), 'sha256': digest(catalog_path)}]
    for row in selected:
        if paths.get(row['path'], {}).get('sha256') != row['sha256']:
            raise RuntimeError('Selected source/helper missing from actual complete cut: ' + row['path'])
    helper = root / '.github/scripts/astra-original-native-session-drain.py'
    guard = load_module(helper, 'organisation_original_session')
    OriginalSession = guard.OriginalSession
    OriginalSession.enroll_subreaper()
    if not callable(getattr(os, 'pidfd_open', None)) or not callable(getattr(signal, 'pidfd_send_signal', None)):
        raise RuntimeError('Linux original creator pidfd required')
    observations = {'status': 'WHOLE_SDK_WEB_PENDING', 'commands': [], 'configurations': [],
                    'head': args.expected_commit, 'cutSha256': args.manifest_sha,
                    'qualification': 'Synthetic original local CAKE sessions and canonical SDK/Web harnesses only; no remote credential, native UI, billing-provider, whole Admin or BIZ acceptance'}

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

    def command(name, argv, *, deadline=1200, extra_env=None):
        if not re.fullmatch('[a-zA-Z0-9_.-]+', name) or any(row['name'] == name for row in observations['commands']):
            raise RuntimeError('Unique ordinary original command name required')
        protect_selected(); budget()
        records = out / 'original-process-drains' / name
        records.mkdir(mode=0o700, parents=True)
        save(records / 'expected-managed-launch.json', {'expectedManagedLaunch': True, 'drained': False,
             'classification': 'Original SDK metadata/build or complete console harness process'})
        log = out / 'logs' / (name + '.log'); log.parent.mkdir(exist_ok=True)
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

    pdb = load_module(root / '.github/scripts/astra-home-portable-pdb.py', 'organisation_actual_pdb')
    restore = load_module(root / '.github/scripts/astra-auth-restore-evidence.py', 'organisation_actual_restore')
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
        entries = catalog['wholeSuites']
        for configuration in ('Debug', 'Release'):
            phase = configuration.lower()
            managed = root / 'artifacts/root14-managed-build'; host = root / 'artifacts/root14-host-build-tasks'
            common = ['-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:AvsSkipBuildingLegacyTargetFrameworks=True',
                      '-p:EnableWindowsTargeting=true', '-p:EmitCompilerGeneratedFiles=true', '-p:Configuration=' + configuration]
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
                    '-getProperty:MSBuildProjectFullPath,MSBuildProjectName,AssemblyName,TargetPath,OutputPath,Configuration,TargetFramework,ProjectAssetsFile,MSBuildProjectExtensionsPath,AvaloniaBuildTasksLocation,EmitCompilerGeneratedFiles,IntermediateOutputPath,CompilerGeneratedFilesOutputPath,ArtifactsPath,ArtifactsProjectName,BaseIntermediateOutputPath']
                if items:
                    # These actual SDK targets populate generated compiler inputs before CoreCompile.
                    # They do not invoke CoreCompile; every resulting source is still PE/PDB/hash-bound below.
                    argv.extend(['-target:GenerateTargetFrameworkMonikerAttribute,GenerateAssemblyInfo', '-getItem:Compile'])
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
                actual.insert(-1, '-p:EmitCompilerGeneratedFiles=true')
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
                    generated_context = pdb.assert_generated_context(values, host if project_row['hostContext'] else managed)
                    target = P(values['TargetPath']).resolve(); external = target.with_suffix('.pdb')
                    if not target.is_relative_to(root) or target.is_symlink() or not target.is_file() or external.is_symlink():
                        raise RuntimeError('Complete actual first-party physical PE/symbol input invalid: ' + project)
                    symbols, symbol_proof = pdb.actual_symbols(target.read_bytes(), external.read_bytes() if external.is_file() else None)
                    pair = [target]
                    if symbol_proof['kind'] == 'external-portable-pdb':
                        pair.append(external)
                    else:
                        extracted = out / phase / 'embedded-symbols' / (digest(target) + '.portable-pdb')
                        pdb.retain_actual_symbols(extracted, symbols, budget, MAX_EVIDENCE)
                        symbol_proof['retainedExtractedSymbols'] = str(extracted.relative_to(out))
                    identity = symbol_proof['identity']
                    documents = pdb.pdb_documents(symbols)
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
                        if relative in {f['path'] for f in catalog['sources']} and not matches:
                            raise RuntimeError('Owned source04 must appear in actual original PDB: ' + relative)
                        if relative not in paths: row['retainedOriginalSource'] = retained(source, out / phase / 'original-generated-or-materialized-sources')
                        inputs.append(row)
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
                        'physicalAssembly': values['AssemblyName'], 'identity': identity, 'symbols': symbol_proof, 'generatedOutputContext': generated_context, 'pairs': [retained(file, out / phase / 'compiled') for file in pair],
                        'compileInputs': inputs, 'allPdbDocuments': proof_docs})
                suite_evaluated = query(suite['project'], [*managed_props, '-p:TargetFramework=net10.0'], name + '-entry')
                target = P(suite_evaluated['Properties']['TargetPath']).resolve()
                closure_before = regular_closure(target.parent)
                output = command(phase + '-whole-' + name, ['dotnet', str(target)], deadline=1200)
                for marker in suite['requiredPassMarkers']:
                    if output.count(marker) != 1: raise RuntimeError('Entire original console harness marker missing/duplicated: ' + marker)
                if regular_closure(target.parent) != closure_before: raise RuntimeError('Actual complete compiled suite output changed during original run')
                after = restore.snapshot_restore(root, suite['project'], extra_projects=[TASK_PROJECT], evidence_cohort='owning', evidence_output=out,
                    managed_artifacts=managed, host_artifacts=host)
                save(out / phase / (name + '-restore-after.json'), after)
                if after != before: raise RuntimeError('Complete restored graph/package evidence changed during original suite')
                if regular_closure(task_target.parent) != task_before: raise RuntimeError('Source-built task output changed during original suite')
                save(out / phase / (name + '-compiled-closure.json'), closure_before)
            missing_owned = {row['path'] for row in catalog['sources']} - owned_pdb_sources
            if missing_owned: raise RuntimeError('Owned source04 missing from whole actual emitted PDB coverage: ' + str(sorted(missing_owned)))
            save(out / phase / 'complete-compiled-source-pairs.json', compiled)
            save(out / phase / 'source-built-task-closure.json', {'evaluated': task_evaluated, 'retained': task_retained,
                'before': task_before, 'after': regular_closure(task_target.parent)})
            observations['configurations'].append({'configuration': configuration, 'wholeSuites': [suite['name'] for suite in entries],
                'completeCompiledProjects': len(compiled), 'sourceBuiltTasksRetained': True, 'allOriginalSuitesExitedZero': True})
            verify_whole(phase + '-after')
        observations['status'] = 'ACTUAL_WHOLE_ACCOUNTS_ORGPOOLS_WEB_DEBUG_RELEASE_SDK_PASS'
    except BaseException as error: primary = error
    finally:
        collect(cleanup, protect_selected)
        accepted_bytes = collect(cleanup, budget)
        observations['acceptedEvidenceBytes'] = accepted_bytes
        observations['primaryType'] = None if primary is None else type(primary).__name__
        observations['cleanupTypes'] = [type(error).__name__ for error in cleanup]
        if primary is not None or cleanup: observations['status'] = 'ACTUAL_WHOLE_SDK_WEB_REFUSED_OR_FAILED'
        collect(cleanup, lambda: save(out / 'receipt.json', observations))
    fail(primary, cleanup)


if __name__ == '__main__': main()
