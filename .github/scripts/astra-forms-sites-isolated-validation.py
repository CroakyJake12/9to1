"""Isolated proposed Sites artifact-path validation; execute every original owning control unchanged."""
import argparse, hashlib, json, os, pathlib, shutil, signal, subprocess, sys, time, xml.etree.ElementTree as ET

SITES = '9to1 Workspace/Sites/HavenOS.Sites.csproj'
PROPS = '9to1 Workspace/Sites/Directory.Build.props'
ORIGINAL = '.github/scripts/astra-forms-math01-owning.py'
ORIGINAL_SHA = '8c8df8a95bb74317ffa977ad9b7af7b1aa01d2b60741c5c1208e47ad7cc94fc2'
PROPOSED_SHA = '7708aabec29de94d5e2fd1f0423f04570a643d8590653af0ee3ad43ca15afe9c'
LEGACY_SHA = '98b0e8bb4707c63edf213c7c3219c10a1efccc6862075b785bc54b0bac2e0814'
FIELDS = ('MSBuildProjectFullPath','MSBuildProjectName','ArtifactsProjectName','UseArtifactsOutput',
          'ArtifactsPath','BaseIntermediateOutputPath','MSBuildProjectExtensionsPath',
          'BaseOutputPath','OutputPath','IntermediateOutputPath','CompilerGeneratedFilesOutputPath',
          'EmitCompilerGeneratedFiles','TargetPath','TargetFramework','RuntimeIdentifier','Configuration',
          'AvaloniaBuildTasksLocation')

def sha(data):
    return hashlib.sha256(data).hexdigest()

def require(condition, message):
    if not condition:
        raise RuntimeError(message)

def direct_file(root, relative):
    path = root / relative
    require(path.is_file() and not path.is_symlink() and
            not any(parent.is_symlink() for parent in path.parents), 'Indirect or absent isolated source refused')
    return path

def path_value(value, project_directory):
    path = pathlib.Path(value)
    if not path.is_absolute():
        path = project_directory / path
    require(not path.is_symlink() and not any(parent.is_symlink() for parent in path.parents),
            'Indirect evaluated artifact path refused')
    return path.resolve()

def source_contract(root, cut):
    pins = {row['path']: row for row in cut['files']}
    for name, expected in ((ORIGINAL, ORIGINAL_SHA), (PROPS, PROPOSED_SHA)):
        data = direct_file(root, name).read_bytes()
        pin = pins.get(name)
        require(pin is not None and pin.get('mode', pin.get('gitMode')) == '100644' and
                pin['bytes'] == len(data) and pin['sha256'] == sha(data) == expected,
                'Isolated original producer or proposed whole source-cut fingerprint refused')
    data = direct_file(root, PROPS).read_bytes()
    legacy = data
    condition = b" Condition=\"'$(UseArtifactsOutput)' != 'true'\""
    require(data.count(condition) == 3, 'Isolated proposal must contain the exact three conditions')
    legacy = legacy.replace(condition, b'')
    require(sha(legacy) == LEGACY_SHA, 'Whole legacy Sites source inverse refused')
    xml = ET.fromstring(data)
    require(xml.tag == 'Project' and not xml.attrib, 'Proposed source root refused')
    literals = {
        'BaseIntermediateOutputPath': '$(MSBuildThisFileDirectory)obj\\$(MSBuildProjectName)\\',
        'MSBuildProjectExtensionsPath': '$(MSBuildThisFileDirectory)obj\\$(MSBuildProjectName)\\',
        'BaseOutputPath': '$(MSBuildThisFileDirectory)bin\\$(MSBuildProjectName)\\'}
    for field, literal in literals.items():
        matches = [(group, node) for group in xml for node in group if node.tag == field]
        require(len(matches) == 1 and matches[0][0].tag == 'PropertyGroup' and
                not matches[0][0].attrib and matches[0][1].attrib == {'Condition': "'$(UseArtifactsOutput)' != 'true'"} and
                matches[0][1].text == literal, 'Proposed explicit-true-only source contract refused')
    return {'propsSha256': PROPOSED_SHA, 'legacyWholeInverseSha256': LEGACY_SHA,
            'scope': 'UseArtifactsOutput explicitly true; ArtifactsPath-only opt-in unproved',
            'sharedOwnerAdoption': False}

def settle(errors):
    unique = []
    for error in errors:
        if not any(error is previous for previous in unique):
            unique.append(error)
    if len(unique) == 1:
        raise unique[0]
    if unique:
        raise BaseExceptionGroup('Original owning run and isolated property/evidence checks failed', unique)


# The SAME isolated interpreter child waits for custody before executing dotnet.
# This gate derives from the reviewed SDK09 original guarded-command seam.
ADDED_EXEC_GUARD = "import os,sys;fd=int(sys.argv[1]);token=os.read(fd,1);os.close(fd);assert token==b'G';os.execv(sys.argv[2],sys.argv[2:])"

def persist_added_record(path, value):
    """Retain original write/readback body and SAME descriptor close errors independently."""
    encoded = (json.dumps(value, indent=2) + '\n').encode()
    require(len(encoded) <= 4 * 1024 * 1024, 'Whole new custody record exceeds its bound')
    errors, parent, descriptor, reader = [], None, None, None
    written_identity = None
    try:
        parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        descriptor = os.open(path.name, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW,
                             0o600, dir_fd=parent)
        offset = 0
        while offset < len(encoded):
            written = os.write(descriptor, encoded[offset:])
            require(written > 0, 'Whole original custody record write made no progress')
            offset += written
        os.fsync(descriptor)
    except BaseException as error:
        errors.append(error)
    if descriptor is not None:
        try:
            written_identity = os.fstat(descriptor)
        except BaseException as error:
            errors.append(error)
        try:
            os.close(descriptor)
        except BaseException as error:
            errors.append(error)
    try:
        require(parent is not None, 'Original custody parent was not retained')
        reader = os.open(path.name, os.O_RDONLY | os.O_NOFOLLOW, dir_fd=parent)
        before = os.fstat(reader)
        require(before.st_size == len(encoded), 'Whole original custody record size refused')
        pieces, remaining = [], len(encoded)
        while remaining:
            piece = os.read(reader, remaining)
            require(piece, 'Whole original custody record readback ended early')
            pieces.append(piece)
            remaining -= len(piece)
        require(not os.read(reader, 1), 'Whole original custody record has trailing bytes')
        stable = lambda row: (row.st_dev, row.st_ino, row.st_mode, row.st_size,
                              row.st_mtime_ns, row.st_ctime_ns)
        linked = os.stat(path.name, dir_fd=parent, follow_symlinks=False)
        require(written_identity is not None and b''.join(pieces) == encoded and
                stable(written_identity) == stable(os.fstat(reader)) == stable(before) == stable(linked),
                'Whole original custody record readback/identity refused')
    except BaseException as error:
        errors.append(error)
    if reader is not None:
        try:
            os.close(reader)
        except BaseException as error:
            errors.append(error)
    if parent is not None:
        try:
            os.fsync(parent)
        except BaseException as error:
            errors.append(error)
        try:
            os.close(parent)
        except BaseException as error:
            errors.append(error)
    settle(errors)
    return {'bytes': len(encoded), 'sha256': sha(encoded)}

def settle_unreleased_direct_child(process, module, creator, original):
    """Only before any poll/drain/reap: bind the SAME private unreaped direct child.
    WNOWAIT is direct-child evidence, not a PID/session adoption or a success seal.
    The isolated guard has not been released and cannot begin requested tool work.
    """
    require(process.returncode is None, 'Original guarded child was already reaped')
    require(module.OriginalSession.stat(os.getpid()) is not None and
            module.OriginalSession.stat(os.getpid())[:4] == creator[:4],
            'SAME guarded-query creator changed before fallback')
    observed = os.waitid(os.P_PID, process.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT)
    require(observed is None or observed.si_pid == process.pid,
            'SAME private unreaped direct child unavailable')
    current = module.OriginalSession.stat(process.pid)
    require(original is not None and current is not None and current[:4] == original[:4] and
            current[0] == os.getpid() and current[1:3] == (process.pid, process.pid),
            'SAME original guarded child birth/parent/session changed before fallback')
    # No Popen poll/wait/session.drain or other owner has reaped this private child.
    # If WNOWAIT already sees its exit, keep it unreaped until the exact wait below.
    if observed is None:
        os.kill(process.pid, signal.SIGKILL)
    process.wait(timeout=5)


class AddedQueryScope:
    """Own only newly added queries; earlier original cohort seals are never reused."""
    def __init__(self, namespace):
        self.namespace = namespace
        self.root, self.out = namespace['root'], namespace['out']
        self.module = namespace['formsNative'].load(
            self.root, 'forms_isolated_added_original_session',
            '.github/scripts/astra-original-native-session-drain.py')
        self.creator = self.module.OriginalSession.stat(os.getpid())
        require(self.creator is not None, 'Added-query original creator identity unavailable')
        self.records = self.out / 'sites-isolated-added-query-process-drain'
        self.records.mkdir(mode=0o700, exist_ok=False)
        self.required = self.records / 'sites-isolated-added-work-required.json'
        self.sequence, self.scopes = 0, []
        self.required_witness = None
        self.required_witness = persist_added_record(self.required, {'drained': False, 'creatorPid': os.getpid(),
            'creatorTuple': self.creator, 'status': 'added property work pending'})
        self.guard_python = pathlib.Path(sys.executable).resolve()
        require(self.guard_python.is_file() and not self.guard_python.is_symlink(), 'Actual isolated guard interpreter unavailable')
        self.guard_python_sha = namespace['digest'](self.guard_python)
        self.dotnet = pathlib.Path(shutil.which('dotnet')).resolve()
        require(self.dotnet.is_file() and not self.dotnet.is_symlink(), 'Added-query executable unavailable')
        self.dotnet_sha = namespace['digest'](self.dotnet)

    def query(self, argv, name=None):
        require(self.module.OriginalSession.stat(os.getpid()) is not None and
                    self.module.OriginalSession.stat(os.getpid())[:4] == self.creator[:4],
                'SAME added-query creator changed')
        require(argv and argv[0] == 'dotnet' and self.namespace['digest'](self.dotnet) == self.dotnet_sha,
                'SAME actual added-query executable changed')
        require(self.sequence < 256, 'Added query count exceeds its independent whole bound')
        self.sequence += 1
        records = self.records / format(self.sequence, '04d')
        records.mkdir(mode=0o700, exist_ok=False)
        expected = records / 'expected-managed-launch.json'
        persist_added_record(expected, {'expectedManagedLaunch': True, 'drained': False})
        stdout = self.out / (name + '.log') if name is not None else records / 'stdout.log'
        stderr = None if name is not None else records / 'stderr.log'
        scope = {'path': str(records.relative_to(self.root)), 'argv': list(argv),
                 'creatorPid': os.getpid(), 'creatorTuple': self.creator,
                 'executable': str(self.dotnet), 'executableSha256': self.dotnet_sha,
                 'launcherPid': None, 'launcherTuple': None, 'exitCode': None, 'drained': False}
        # Retain the scope BEFORE any file/process/session observation can fail.
        self.scopes.append(scope)
        handles, errors = [], []
        process = session = launcher_descriptor = read_descriptor = write_descriptor = None
        original_launcher = None
        release_attempted = released = False
        code = None
        try:
            first = stdout.open('xb')
            handles.append(first)
            second = None
            if stderr is not None:
                second = stderr.open('xb')
                handles.append(second)
            require(self.namespace['digest'](self.guard_python) == self.guard_python_sha,
                    'SAME guarded interpreter changed')
            read_descriptor, write_descriptor = os.pipe()
            guarded_argv = [str(self.guard_python), '-I', '-S', '-c', ADDED_EXEC_GUARD,
                            str(read_descriptor), str(self.dotnet), *argv[1:]]
            scope.update({'guardArgv': guarded_argv, 'guardInterpreter': str(self.guard_python),
                          'guardInterpreterSha256': self.guard_python_sha,
                          'guardSourceSha256': sha(ADDED_EXEC_GUARD.encode()),
                          'toolExecGateReleaseAttempted': False, 'toolExecGateReleased': False})
            process = subprocess.Popen(guarded_argv, cwd=self.root,
                stdout=first, stderr=second if second is not None else subprocess.STDOUT,
                start_new_session=True, pass_fds=(read_descriptor,))
            scope['launcherPid'] = process.pid
            # No original poll/wait/drain/reap precedes this direct-child/birth capture.
            startup = []
            try:
                original_launcher = self.module.OriginalSession.stat(process.pid)
                require(original_launcher is not None and original_launcher[0] == os.getpid() and
                        original_launcher[1:3] == (process.pid, process.pid),
                        'SAME unreaped original guard parent/session/birth unavailable')
                scope['launcherTuple'] = original_launcher
            except BaseException as error:
                startup.append(error)
            try:
                launcher_descriptor = os.pidfd_open(process.pid, 0)
            except BaseException as error:
                startup.append(error)
            # Retain the SAME session object even if its persistence prefix refuses.
            try:
                session = self.module.OriginalSession.__new__(self.module.OriginalSession)
                session.__init__(process, records)
                require(original_launcher is not None and session.original[:4] == original_launcher[:4],
                        'SAME original guarded session identity changed')
            except BaseException as error:
                startup.append(error)
            settle(startup)
            os.close(read_descriptor)
            read_descriptor = None
            release_attempted = True
            scope['toolExecGateReleaseAttempted'] = True
            require(os.write(write_descriptor, b'G') == 1, 'SAME original guarded exec handshake refused')
            released = True
            scope['toolExecGateReleased'] = True
            os.close(write_descriptor)
            write_descriptor = None
            deadline = time.monotonic() + 120
            while process.poll() is None:
                session.observe()
                require(time.monotonic() < deadline, 'Added original query exceeded its bounded lifetime')
                for path in (stdout, stderr):
                    if path is not None:
                        require(path.stat().st_size <= 4 * 1024 * 1024,
                                'Whole added query output exceeds its bounded file size')
                time.sleep(.025)
            code = process.wait()
            scope['exitCode'] = code
        except BaseException as error:
            errors.append(error)
        # EOF closes admission before cleanup: no G means no dotnet exec.
        for descriptor in (read_descriptor, write_descriptor):
            if descriptor is not None:
                try:
                    os.close(descriptor)
                except BaseException as error:
                    errors.append(error)
        # On startup refusal, settle the same original child BEFORE a session drain
        # can poll/reap it. Numeric fallback is restricted to an unreleased guard.
        if errors and process is not None:
            try:
                if launcher_descriptor is not None:
                    try:
                        signal.pidfd_send_signal(launcher_descriptor, signal.SIGKILL, None, 0)
                    except ProcessLookupError:
                        pass
                elif not release_attempted:
                    settle_unreleased_direct_child(process, self.module, self.creator, original_launcher)
                else:
                    raise RuntimeError('Released original tool has no retained kernel identity')
            except BaseException as error:
                errors.append(error)
        for handle in reversed(handles):
            try:
                handle.close()
            except BaseException as error:
                errors.append(error)
        try:
            if session is not None:
                session.drain()
                scope['drained'] = True
        except BaseException as error:
            errors.append(error)
        if not scope['drained'] and process is not None and launcher_descriptor is not None:
            try:
                try:
                    signal.pidfd_send_signal(launcher_descriptor, signal.SIGKILL, None, 0)
                except ProcessLookupError:
                    pass
                process.wait(timeout=5)
            except BaseException as error:
                errors.append(error)
        try:
            if process is not None:
                process.wait(timeout=5)
        except BaseException as error:
            errors.append(error)
        if launcher_descriptor is not None:
            try:
                os.close(launcher_descriptor)
            except BaseException as error:
                errors.append(error)
        returned = None
        try:
            require(code is not None and scope['drained'], 'Added original query completion/drain unproved')
            seal = json.loads(expected.read_text())
            require(seal.get('drained') is True and seal.get('launcherPid') == scope['launcherPid'] and
                    seal.get('launcherStartTicks') == scope['launcherTuple'][3],
                    'Added original query expected seal is not its SAME launcher')
            pieces = []
            for path in (stdout, stderr):
                if path is None:
                    pieces.append(None)
                    continue
                raw = direct_file(self.root, str(path.relative_to(self.root))).read_bytes()
                require(len(raw) <= 4 * 1024 * 1024, 'Complete added query output exceeds bound')
                pieces.append(raw.decode())
            returned = subprocess.CompletedProcess(argv, code, pieces[0], pieces[1])
        except BaseException as error:
            errors.append(error)
        try:
            persist_added_record(records / 'original-query.json', scope)
        except BaseException as error:
            errors.append(error)
        settle(errors)
        return returned

    def seal(self):
        errors = []
        try:
            require(self.module.OriginalSession.stat(os.getpid()) is not None and
                    self.module.OriginalSession.stat(os.getpid())[:4] == self.creator[:4],
                    'SAME added query creator unavailable at final seal')
            require(self.scopes and len(self.scopes) == self.sequence and
                    all(row['drained'] for row in self.scopes), 'Added query original scopes remain pending')
            for row in self.scopes:
                records = self.root / row['path']
                expected = json.loads((records / 'expected-managed-launch.json').read_text())
                pending = list(records.glob('*-pending.json'))
                require(len(pending) == 1, 'SAME added original session pending record unavailable')
                actual = json.loads(pending[0].read_text())
                require(expected.get('drained') is True and actual.get('drained') is True and
                        actual.get('launcherPid') == expected.get('launcherPid') == row['launcherPid'] and
                        actual.get('launcherTuple') == list(row['launcherTuple']) and
                        expected.get('launcherStartTicks') == row['launcherTuple'][3],
                        'SAME added original session final birth/seal changed')
            require(self.namespace['digest'](self.dotnet) == self.dotnet_sha,
                    'Added-query actual executable changed before final seal')
        except BaseException as error:
            errors.append(error)
        try:
            self.required_witness = persist_added_record(self.required, {'drained': not errors, 'creatorPid': os.getpid(),
                'creatorTuple': self.creator, 'queries': self.scopes,
                'status': 'added original queries drained' if not errors else 'added query drain refused',
                'qualification': 'Fresh scopes cover only added queries; earlier original native/cohort seals remain separate.'})
        except BaseException as error:
            errors.append(error)
        settle(errors)

def retain_native_witness(namespace):
    root, out = namespace['root'], namespace['out']
    compiled_raw = direct_file(root, str((out / 'forms-original-native-compiled.json').relative_to(root))).read_bytes()
    restore_raw = direct_file(root, str((out / 'forms-original-native-restore-before.json').relative_to(root))).read_bytes()
    require(len(compiled_raw) <= 16 * 1024 * 1024 and len(restore_raw) <= 16 * 1024 * 1024,
            'Whole original native witnesses exceed their admission bound')
    compiled = json.loads(compiled_raw)
    require(compiled['project'] == namespace['formsNative'].PROJECT and compiled['files'],
            'SAME original native producer witness unavailable')
    target = direct_file(root, compiled['target'])
    require(target.name == 'Haven.dll' and target.resolve().is_relative_to(
        root / 'artifacts/forms-original-native-managed-build') and
        namespace['digest'](target) == compiled['targetSha256'],
        'SAME original native target identity differs before added work')
    return {'target': target, 'files': compiled['files'], 'restore': json.loads(restore_raw),
            'compiledWitnessSha256': sha(compiled_raw), 'restoreWitnessSha256': sha(restore_raw)}

def final_native_restore(namespace, witness):
    require(witness is not None, 'SAME original native restore witness was not retained')
    restore = namespace['formsNative'].load(namespace['root'], 'forms_isolated_final_native_restore',
        '.github/scripts/astra-forms-original-native-restore-evidence.py')
    current = restore.snapshot_restore(namespace['root'], namespace['formsNative'].PROJECT,
        metadata_diagnostic=namespace['native_metadata_diagnostic'])
    require(current == witness['restore'], 'SAME original native restore/package baseline changed after added queries')

def final_native_output(namespace, witness):
    require(witness is not None, 'SAME original native compiled witness was not retained')
    current = namespace['formsNative'].snapshot_output(namespace['root'], witness['target'], namespace['digest'])
    require(current == witness['files'], 'SAME original native entire compiled output changed after added queries')

def validate_phase(namespace, phase, artifacts, native, rows):
    root, out = namespace['root'], namespace['out']
    project = direct_file(root, SITES)
    task = namespace['taskTarget']
    require(task.is_file(), 'SAME source-built host task unavailable')
    before_task = namespace['digest'](task)
    props = ['-p:Configuration=Release', '-p:TargetFramework=net10.0',
             '-p:RuntimeIdentifier=linux-x64', '-p:RuntimeIdentifiers=linux-x64',
             '-p:SelfContained=false', '-p:EnableWindowsTargeting=true',
             '-p:AvsSkipBuildingLegacyTargetFrameworks=True', '-p:UseSharedCompilation=false',
             '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=' + str(artifacts),
             '-p:IncludeProjectNameInArtifactsPaths=true', '-p:AvaloniaBuildTasksLocation=' + str(task)]
    if native:
        props.append('-p:CustomAfterMicrosoftCommonTargets=' +
                     str(root / '.github/validation/astra-forms-native-probe.targets'))
    # This extra query observes C's generic emitted-path contract. It does not
    # turn the unchanged original builds into Emit=true builds or prove generated-file bytes.
    props.append('-p:EmitCompilerGeneratedFiles=true')
    argv = ['dotnet', 'msbuild', SITES, '-nologo', '-m:1', '-nr:false', *props,
            '-getProperty:' + ','.join(FIELDS)]
    name = 'sites-isolated-current-' + phase
    diagnostic = namespace['native_metadata_diagnostic']
    code = diagnostic.operation(name, argv, lambda: namespace['addedQueryScope'].query(argv, name).returncode)
    namespace['verify']()
    namespace['assert_task_unchanged']()
    require(code == 0, 'Actual isolated Sites property query failed')
    raw = direct_file(root, str((out / (name + '.log')).relative_to(root))).read_bytes()
    values = json.loads(raw.decode())['Properties']
    require(set(FIELDS).issubset(values), 'Complete current property observation unavailable')
    require(values['UseArtifactsOutput'] == 'true' and values['EmitCompilerGeneratedFiles'] == 'true' and
            values['TargetFramework'] == 'net10.0' and values['RuntimeIdentifier'] == 'linux-x64' and
            values['Configuration'] == 'Release', 'Actual isolated property mode/framework/RID refused')
    require(path_value(values['MSBuildProjectFullPath'], project.parent) == project.resolve(),
            'Actual Sites property project identity refused')
    name = values['MSBuildProjectName']
    require(name == project.stem == values['ArtifactsProjectName'], 'Actual Sites owner name refused')
    owner = artifacts / 'obj' / name
    base = path_value(values['BaseIntermediateOutputPath'], project.parent)
    extensions = path_value(values['MSBuildProjectExtensionsPath'], project.parent)
    intermediate = path_value(values['IntermediateOutputPath'], project.parent)
    generated = path_value(values['CompilerGeneratedFilesOutputPath'], project.parent)
    bin_owner = artifacts / 'bin' / name
    output = path_value(values['OutputPath'], project.parent)
    target = path_value(values['TargetPath'], project.parent)
    require(path_value(values['ArtifactsPath'], project.parent) == artifacts and base == owner and extensions == owner,
            'C generic artifact owner context refused; no legacy alternate or fingerprint bypass')
    require(intermediate.is_relative_to(owner) and intermediate != owner and
            generated.is_relative_to(owner) and generated != owner, 'Actual intermediate/generated owner path refused')
    require(path_value(values['BaseOutputPath'], project.parent) == bin_owner and
            output.is_relative_to(bin_owner) and target.is_relative_to(output), 'Actual physical phase output escaped owner')
    require(path_value(values['AvaloniaBuildTasksLocation'], project.parent) == task.resolve() and
            namespace['digest'](task) == before_task, 'SAME original source-built task changed')
    pair = []
    for file in (target, target.with_suffix('.pdb')):
        require(file.is_file() and not file.is_symlink(), 'Actual Sites physical PE/PDB phase pair missing')
        pair.append({'path': str(file.relative_to(root)), 'bytes': file.stat().st_size,
                     'sha256': namespace['digest'](file)})
    rows.append({'phase': phase, 'argv': argv, 'propertyLogBytes': len(raw), 'propertyLogSha256': sha(raw),
                 'properties': values, 'physicalPair': pair,
                 'queriedGenericEmittedPathContract': True, 'emittedCompilerGeneratedFilesValidated': False,
                 'sameOriginalCompiledSourcePdbAndTaskGuards': 'unchanged original producer must separately pass'})
    # The SAME original full Desktop baseline is checked once after BOTH queries.

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--manifest-sha', required=True)
    args = parser.parse_args()
    root = pathlib.Path.cwd()
    require(args.manifest == '.github/validation/astra-forms-math01-cut.json', 'Unexpected isolated self-cut')
    cut_bytes = direct_file(root, args.manifest).read_bytes()
    require(sha(cut_bytes) == args.manifest_sha, 'Isolated self-cut whole SHA refused')
    source = source_contract(root, json.loads(cut_bytes))
    original = direct_file(root, ORIGINAL).read_bytes()
    namespace = {'__name__': '__main__', '__file__': str(root / ORIGINAL)}
    errors, rows = [], []
    original_completed = False
    try:
        # SAME entire immutable original producer. Its cohorts/tasks/native/restore/drains are untouched.
        exec(compile(original, str(root / ORIGINAL), 'exec'), namespace)
        original_completed = True
    except BaseException as error:
        errors.append(error)
    added, witness = None, None
    added_ready = False
    if not errors:
        try:
            # Retain the SAME original native witnesses BEFORE any added work.
            witness = retain_native_witness(namespace)
        except BaseException as error:
            errors.append(error)
        try:
            # Publish the original owned scope before its constructor callbacks.
            added = AddedQueryScope.__new__(AddedQueryScope)
            added.__init__(namespace)
            evidence = namespace['out'] / 'sites-isolated-after-original-query-evidence'
            evidence.mkdir(mode=0o700, exist_ok=False)
            # A separate bounded AFTER-original diagnostic context retains the
            # SAME original Desktop baseline; original receipts/caps stay exact.
            diagnostic = namespace['metadataModule'].ExactSitesMetadata.__new__(
                namespace['metadataModule'].ExactSitesMetadata)
            diagnostic.__init__(namespace['root'], evidence, namespace['restoredProjects']['desktop'][1])
            diagnostic.query = lambda label, argv: diagnostic.operation(label, argv, lambda: added.query(argv))
            namespace['native_metadata_diagnostic'] = diagnostic
            namespace['addedQueryScope'] = added
            added_ready = True
        except BaseException as error:
            errors.append(error)
        if added_ready:
            for phase, relative, native in (
                    ('managed', 'artifacts/root14-managed-build', False),
                    ('native', 'artifacts/forms-original-native-managed-build', True)):
                try:
                    validate_phase(namespace, phase, root / relative, native, rows)
                except BaseException as error:
                    errors.append(error)
        # Attempt every independent SAME original protection even when an earlier
        # guard/query refuses. The native records are held, never reset/recreated.
        for guard in (
                lambda: namespace['assert_compiled_target_unchanged']('desktop'),
                lambda: final_native_restore(namespace, witness),
                lambda: final_native_output(namespace, witness),
                namespace['assert_task_unchanged'], namespace['verify']):
            try:
                guard()
            except BaseException as error:
                errors.append(error)
        if added is not None:
            try:
                added.seal()
            except BaseException as error:
                errors.append(error)
    query_evidence = None
    if added is not None:
        try:
            require(added.required_witness is not None, 'Whole fresh added-work record not retained')
            query_evidence = {'queries': added.sequence,
                'completeRequiredRecord': {'path': str(added.required.relative_to(root)), **added.required_witness},
                'completePerQueryRecords': str(added.records.relative_to(root)) + '/NNNN/original-query.json',
                'qualification': 'The full required record contains every original query scope/argv; no truncation or replay.'}
        except BaseException as error:
            errors.append(error)
    try:
        out = namespace.get('out', root / 'artifacts/desktop-visible-owning')
        out.mkdir(parents=True, exist_ok=True)
        record = {'expectedCommit': args.expected_commit, 'selfManifestSha256': args.manifest_sha,
                  'sourceProposal': source, 'phases': rows, 'originalOwningCompleted': original_completed,
                  'propertyValidationCompleted': not errors and len(rows) == 2,
                  'sharedCSourceAdopted': False,
                  'addedOriginalQueryEvidence': query_evidence,
                  'heldNativeWitnesses': {key: witness[key] for key in ('compiledWitnessSha256', 'restoreWitnessSha256')} if witness is not None else None,
                  'afterOriginalDiagnosticsContext': 'sites-isolated-after-original-query-evidence; SAME held Desktop baseline; prior original context unchanged',
                  'qualification': 'Extra actual property queries and physical phase pairs only. Existing full original guards remain mandatory; C ownership/rebinding and actual Emit=true generated-source builds remain separate.',
                  'failureTypes': [type(error).__name__ for error in errors]}
        encoded = (json.dumps(record, indent=2) + '\n').encode()
        require(len(encoded) <= 65536, 'Complete isolated property receipt exceeds its whole bound')
        destination = out / 'sites-isolated-artifact-property-validation.json'
        handle = destination.open('xb')
        record_errors = []
        try:
            handle.write(encoded)
            handle.flush()
            __import__('os').fsync(handle.fileno())
        except BaseException as error:
            record_errors.append(error)
        finally:
            try:
                handle.close()
            except BaseException as error:
                record_errors.append(error)
        settle(record_errors)
        require(destination.read_bytes() == encoded, 'Complete isolated property receipt readback refused')
    except BaseException as error:
        errors.append(error)
    settle(errors)

if __name__ == '__main__':
    main()
