"""Build and observe the original native Forms host in a fresh producer-owned profile.

The owning caller keeps its complete cohorts/guards. This separate build never
reuses a test result as a native UI result, deletes a profile, or grants authority.
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import stat
import subprocess
import sys
import time

sys.dont_write_bytecode = True
PROJECT = '9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj'
SYMBOL = 'ASTRA_FORMS_NATIVE_PROBE'
ENTRY = 'Haven.Desktop.Validation.FormsMathematicsNativeActivationProbe'
MARKER = 'ASTRA_FORMS_ORIGINAL_CLASSIC_MATH_EDIT_SAVE_REOPEN_MARK_OBSERVATION_AND_EXIT_TASK_SETTLED_DRAINS_REQUIRED'

def load(root, name, relative):
    spec = importlib.util.spec_from_file_location(name, root / relative)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def snapshot_output(root, target, digest):
    rows = []
    for path in sorted(target.parent.rglob('*')):
        if path.is_symlink():
            raise ValueError('Original native output contains a symlink')
        if path.is_file():
            rows.append({'path': str(path.relative_to(root)), 'bytes': path.stat().st_size, 'sha256': digest(path)})
    return rows

def run(root, out, cut_paths, digest, command, verify, task_target, assert_task, assert_native, *, metadata_diagnostic=None):
    root, out, task_target = Path(root), Path(out), Path(task_target)
    if metadata_diagnostic is not None:
        original_command = command
        def command(argv, name):
            return metadata_diagnostic.operation(name, argv, lambda: original_command(argv, name))
    def actual_query(argv):
        return subprocess.run(argv, capture_output=True, text=True) if metadata_diagnostic is None else metadata_diagnostic.query('native-direct-property-query', argv)
    def actual_snapshot(label):
        if metadata_diagnostic is None:
            return restore.snapshot_restore(root, PROJECT)
        return metadata_diagnostic.guard(label, lambda: restore.snapshot_restore(root, PROJECT, metadata_diagnostic=metadata_diagnostic))
    def actual_guard(label, guard):
        return guard() if metadata_diagnostic is None else metadata_diagnostic.guard(label, guard)
    if sys.platform != 'linux' or not os.environ.get('DISPLAY'):
        raise RuntimeError('Actual original Linux/Xvfb display required')
    targets = root / '.github/validation/astra-forms-native-probe.targets'
    if not targets.is_file() or targets.is_symlink() or digest(targets) != cut_paths.get(str(targets.relative_to(root))):
        raise ValueError('Original native scoped build targets are not pinned')
    artifact_root = root / 'artifacts/forms-original-native-managed-build'
    if artifact_root.exists():
        raise ValueError('Original native build context must be fresh')
    props = ['-p:RuntimeIdentifiers=linux-x64', '-p:SelfContained=false',
             '-p:AvsSkipBuildingLegacyTargetFrameworks=True', '-p:UseSharedCompilation=false',
             '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=' + str(artifact_root),
             '-p:IncludeProjectNameInArtifactsPaths=true', '-p:EnableWindowsTargeting=true',
             '-p:AvaloniaBuildTasksLocation=' + str(task_target),
             '-p:CustomAfterMicrosoftCommonTargets=' + str(targets)]
    flags = ['--disable-build-servers', '-m:1', '-nr:false']
    restore = load(root, 'forms_native_actual_restore', '.github/scripts/astra-forms-original-native-restore-evidence.py')
    code = command(['dotnet', 'restore', PROJECT, *flags, '-r', 'linux-x64',
                    '-p:Configuration=Release', '-p:TargetFramework=net10.0', *props], 'forms-original-native-restore')
    verify()
    if code:
        raise RuntimeError('Original native Desktop restore failed: ' + str(code))
    # The independent canonical compiler/generator tools retain netstandard2.0;
    # they have their own fresh native-build assets, without retargeting the host task.
    for tool in restore.DEFAULT_TOOLS:
        if tool.endswith('/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'):
            continue
        name = 'forms-original-native-tool-' + Path(tool).stem
        code = command(['dotnet', 'restore', tool, *flags, '-r', 'linux-x64',
                        '-p:Configuration=Release', '-p:TargetFramework=netstandard2.0', *props], name)
        verify()
        if code:
            raise RuntimeError('Original native canonical tool restore failed: ' + tool)
        query = actual_query(['dotnet', 'msbuild', tool, '-nologo', '-m:1', '-nr:false',
                                '-p:Configuration=Release', '-p:TargetFramework=netstandard2.0',
                                '-p:RuntimeIdentifier=linux-x64', *props,
                                '-getProperty:TargetFramework,MSBuildProjectFullPath,ProjectAssetsFile,StartupObject'])
        (out / (name + '.stdout')).write_text(query.stdout)
        (out / (name + '.stderr')).write_text(query.stderr)
        query.check_returncode()
        actual = json.loads(query.stdout)['Properties']
        assets = Path(actual['ProjectAssetsFile'])
        assets = assets if assets.is_absolute() else (root / tool).parent / assets
        if (actual['TargetFramework'] != 'netstandard2.0' or actual['StartupObject'] or
                Path(actual['MSBuildProjectFullPath']).resolve() != (root / tool).resolve() or
                not assets.resolve().is_relative_to(artifact_root) or not assets.is_file() or
                not any(key.split('/')[0] == 'netstandard2.0' for key in json.loads(assets.read_text())['targets'])):
            raise ValueError('Actual original native canonical tool framework/path/entry mismatch')
    before_restore = actual_snapshot('native-restore-before-snapshot')
    (out / 'forms-original-native-restore-before.json').write_text(json.dumps(before_restore, indent=2) + '\n')
    actual_guard('native-original-task-guard', assert_task)
    actual_guard('native-original-desktop-positive-guard', assert_native)
    code = command(['dotnet', 'build', PROJECT, *flags, '--no-restore', '-c', 'Release',
                    '-f', 'net10.0', '-r', 'linux-x64', *props], 'forms-original-native-build')
    verify()
    if code:
        raise RuntimeError('Original native Desktop build failed: ' + str(code))
    query = actual_query(['dotnet', 'msbuild', PROJECT, '-nologo', '-m:1', '-nr:false',
                            '-p:Configuration=Release', '-p:TargetFramework=net10.0',
                            '-p:RuntimeIdentifier=linux-x64', *props,
                            '-getProperty:TargetPath,OutputPath,RuntimeIdentifier,Configuration,TargetFramework,AssemblyName,DefineConstants,StartupObject,AvaloniaBuildTasksLocation'])
    (out / 'forms-original-native-target.stdout').write_text(query.stdout)
    (out / 'forms-original-native-target.stderr').write_text(query.stderr)
    query.check_returncode()
    actual = json.loads(query.stdout)['Properties']
    target = Path(actual['TargetPath']).resolve()
    output = Path(actual['OutputPath'])
    output = (output if output.is_absolute() else (root / PROJECT).parent / output).resolve()
    if (actual['TargetFramework'] != 'net10.0' or actual['RuntimeIdentifier'] != 'linux-x64' or
            actual['Configuration'] != 'Release' or actual['AssemblyName'] != 'Haven' or
            SYMBOL not in actual['DefineConstants'].split(';') or actual['StartupObject'] != ENTRY or
            Path(actual['AvaloniaBuildTasksLocation']).resolve() != task_target.resolve() or
            not output.is_relative_to(artifact_root) or not target.is_relative_to(output) or
            not target.is_file() or target.is_symlink() or target.name != 'Haven.dll'):
        raise ValueError('Actual original native evaluated producer context/target mismatch')
    closure = snapshot_output(root, target, digest)
    (out / 'forms-original-native-compiled.json').write_text(json.dumps({'project': PROJECT,
        'target': str(target.relative_to(root)), 'targetSha256': digest(target), 'properties': actual, 'files': closure}, indent=2) + '\n')
    retained = out / 'compiled/forms-original-native'
    retained.mkdir(parents=True, exist_ok=False)
    for suffix in ('.dll', '.pdb', '.deps.json', '.runtimeconfig.json'):
        candidate = target.with_name(target.stem + suffix)
        if not candidate.is_file() or candidate.is_symlink():
            raise ValueError('Missing actual original native launch closure: ' + suffix)
        shutil.copyfile(candidate, retained / candidate.name)
        if digest(retained / candidate.name) != digest(candidate):
            raise ValueError('Retained actual original native launch closure differs')
    source_proof = load(root, 'forms_native_actual_sources', '.github/scripts/astra-forms-original-native-compiled-sources.py')
    source_proof.verify(root, out, target, cut_paths, digest)
    verify()
    launch_failures = []
    try:
        launch(root, out, target, closure, digest)
    except BaseException as error:
        launch_failures.append(error)
    # Every original post-launch guard runs even if the actual child or drain
    # refuses. Preserve the primary and each independent guard failure.
    try:
        after_restore = actual_snapshot('native-restore-after-snapshot')
        (out / 'forms-original-native-restore-after.json').write_text(json.dumps(after_restore, indent=2) + '\n')
        if after_restore != before_restore:
            raise ValueError('Original native restored output changed during execution')
    except BaseException as error:
        launch_failures.append(error)
    try:
        if snapshot_output(root, target, digest) != closure:
            raise ValueError('Original native entire compiled output changed during execution')
    except BaseException as error:
        launch_failures.append(error)
    for guard in (assert_task, assert_native, verify):
        try:
            actual_guard('native-original-post-launch-' + guard.__name__, guard)
        except BaseException as error:
            launch_failures.append(error)
    if launch_failures:
        raise BaseExceptionGroup('Original native launch and/or original post-launch guards failed.', launch_failures)

def launch(root, out, target, closure, digest):
    producer = load(root, 'forms_native_actual_profile', '.github/scripts/astra-native-probe-profile.py')
    drain = load(root, 'forms_native_original_session', '.github/scripts/astra-original-native-session-drain.py')
    records = out / 'forms-original-native-process-drain'
    records.mkdir(parents=True, exist_ok=False)
    # This native child owns a distinct fresh original-session collector; its
    # exact required launch witness stays false unless the actual drain seals.
    (records / 'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch': True, 'drained': False}) + '\n')
    required = records / 'forms-original-native-required.json'
    if required.exists():
        raise ValueError('Original native required launch witness already exists')
    required.write_text(json.dumps({'drained': False, 'status': 'original native launch pending'}) + '\n')
    profile = producer.prepare()
    environment = os.environ.copy()
    environment.update(profile['environment'])
    if environment.get('HOME') != os.environ.get('HOME'):
        raise ValueError('The original native profile changed HOME')
    handles = []
    directory_rows = []
    witness_descriptor = None
    witness_identity = None
    process = None
    session = None
    launcher_descriptor = None
    failures = []
    map_witness = None
    code = None
    drained = False
    profile_current = False
    dotnet = Path(shutil.which('dotnet')).resolve()
    pinned = {str((root / row['path']).resolve()): row['sha256'] for row in closure}
    skia = target.parent / 'libSkiaSharp.so'
    if str(skia.resolve()) not in pinned or not skia.is_file() or skia.is_symlink():
        raise ValueError('Actual original native Skia payload not in compiled closure')
    producer_stat = drain.OriginalSession.stat(os.getpid())
    if producer_stat is None:
        raise RuntimeError('Actual original native producer identity missing')

    def check_profile():
        if len(handles) != 5 or witness_descriptor is None or witness_identity is None:
            raise RuntimeError('Complete original native profile inode custody was not captured')
        for path, descriptor, expected in handles:
            actual = os.fstat(descriptor)
            current = path.lstat()
            identity = (actual.st_dev, actual.st_ino, actual.st_uid, actual.st_gid, stat.S_IMODE(actual.st_mode))
            present = (current.st_dev, current.st_ino, current.st_uid, current.st_gid, stat.S_IMODE(current.st_mode))
            if identity != expected or present != expected or not stat.S_ISDIR(actual.st_mode) or path.is_symlink():
                raise RuntimeError('Original native profile directory inode/ownership/mode changed')
        witness = profile['root'] / 'producer-witness.json'
        held = os.fstat(witness_descriptor)
        present = witness.lstat()
        held_identity = (held.st_dev, held.st_ino, held.st_uid, held.st_gid, stat.S_IMODE(held.st_mode))
        current_identity = (present.st_dev, present.st_ino, present.st_uid, present.st_gid, stat.S_IMODE(present.st_mode))
        if (held_identity != witness_identity or current_identity != witness_identity or
                not stat.S_ISREG(held.st_mode) or not witness.is_file() or witness.is_symlink() or
                digest(witness) != profile['witnessSha256']):
            raise RuntimeError('Original native profile witness changed')

    try:
        for path in [profile['root']] + [profile['root'] / name for name in ('data', 'config', 'cache', 'haven')]:
            descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
            # Own the opened descriptor before any following observation can
            # fail; original finally closes it even if fstat refuses.
            handles.append((path, descriptor, None))
            value = os.fstat(descriptor)
            identity = (value.st_dev, value.st_ino, value.st_uid, value.st_gid, stat.S_IMODE(value.st_mode))
            handles[-1] = (path, descriptor, identity)
            directory_rows.append({'path': str(path), 'device': value.st_dev, 'inode': value.st_ino,
                                   'uid': value.st_uid, 'gid': value.st_gid, 'mode': stat.S_IMODE(value.st_mode)})
        witness_descriptor = os.open(profile['root'] / 'producer-witness.json', os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
        witness_stat = os.fstat(witness_descriptor)
        witness_identity = (witness_stat.st_dev, witness_stat.st_ino, witness_stat.st_uid, witness_stat.st_gid, stat.S_IMODE(witness_stat.st_mode))
        check_profile()
        with (out / 'forms-original-native.log').open('wb') as log:
            process = subprocess.Popen([str(dotnet), str(target)], cwd=root, env=environment,
                                       stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            # Retain and initialize the actual original session before its
            # independent launcher descriptor observation. Either refusal
            # still attempts the original collector and actual child drains.
            session = drain.OriginalSession.__new__(drain.OriginalSession)
            startup_failures = []
            try:
                session.__init__(process, records)
            except BaseException as error:
                startup_failures.append(error)
            try:
                launcher_descriptor = os.pidfd_open(process.pid, 0)
            except BaseException as error:
                startup_failures.append(error)
            if startup_failures:
                raise BaseExceptionGroup('Original native session and/or launcher identity capture failed.', startup_failures)
            original = session.original
            if original[0] != os.getpid():
                raise RuntimeError('Original native app is not the actual producer direct child')
            deadline = time.monotonic() + 120
            while process.poll() is None:
                session.observe()
                check_profile()
                if time.monotonic() >= deadline:
                    raise TimeoutError('Original native App lifetime did not settle within its bound')
                directory = Path('/proc') / str(process.pid)
                try:
                    before = session.stat(process.pid)
                    raw = (directory / 'maps').read_bytes()
                    if len(raw) > 4 * 1024 * 1024:
                        raise RuntimeError('Original native maps bound exceeded')
                    executable = Path(os.readlink(directory / 'exe')).resolve()
                    arguments = [item.decode() for item in (directory / 'cmdline').read_bytes().split(b'\0') if item]
                    after = session.stat(process.pid)
                    if before is not None and after is not None and before[1:4] == original[1:4] == after[1:4]:
                        maps = raw.decode()
                        if executable == dotnet and arguments == [str(dotnet), str(target)] and str(target) in maps and str(skia) in maps:
                            if digest(target) != pinned[str(target)] or digest(skia) != pinned[str(skia.resolve())]:
                                raise RuntimeError('Original native mapped producer/Skia bytes changed')
                            map_witness = {'pid': process.pid, 'tuple': after, 'producerPid': os.getpid(),
                                           'producerTuple': producer_stat, 'executable': str(dotnet), 'executableSha256': digest(dotnet),
                                           'argv': arguments, 'maps': maps, 'managedSha256': digest(target), 'skiaSha256': digest(skia)}
                except (FileNotFoundError, ProcessLookupError):
                    pass
                time.sleep(.025)
            code = process.wait()
        if code != 0 or (out / 'forms-original-native.log').read_text().splitlines().count(MARKER) != 1:
            raise RuntimeError('Actual original native visible-pending/exit-task observation did not complete exactly once')
        if map_witness is None:
            raise RuntimeError('Actual original direct native app/managed/Skia maps were not observed')
    except BaseException as error:
        failures.append(error)
    finally:
        try:
            if session is not None:
                session.drain()
                drained = True
        except BaseException as error:
            failures.append(error)
        if not drained and process is not None and launcher_descriptor is not None:
            # A failed session collector is never a drain seal. Still stop the
            # kernel-bound original launcher and reap its actual Popen child;
            # unknown surviving descendants keep the outer policy held.
            try:
                if process.poll() is None:
                    signal.pidfd_send_signal(launcher_descriptor, signal.SIGKILL, None, 0)
                process.wait(timeout=5)
            except BaseException as error:
                failures.append(error)
        try:
            if process is not None:
                process.wait(timeout=5)
        except BaseException as error:
            failures.append(error)
        try:
            check_profile()
            profile_current = True
        except BaseException as error:
            failures.append(error)
        for path, descriptor, expected in handles:
            try:
                os.close(descriptor)
            except BaseException as error:
                failures.append(error)
        if launcher_descriptor is not None:
            try:
                os.close(launcher_descriptor)
            except BaseException as error:
                failures.append(error)
        if witness_descriptor is not None:
            try:
                os.close(witness_descriptor)
            except BaseException as error:
                failures.append(error)
        state = {'drained': drained, 'status': 'original native session drained' if drained else 'original native session drain refused',
                 'launcherPid': process.pid if process is not None else None,
                 'profileRetained': str(profile['root']), 'profileOriginalInodesCurrent': profile_current,
                 'originalCallbackTaskSettledMarkerObserved': code == 0 and map_witness is not None and not failures,
                 'failureTypes': [type(error).__name__ for error in failures]}
        try:
            required.write_text(json.dumps(state, indent=2) + '\n')
        except BaseException as error:
            failures.append(error)
        try:
            (out / 'forms-original-native-receipt.json').write_text(json.dumps({
            'status': 'ACTUAL_ORIGINAL_NATIVE_VISIBLE_PENDING_EXIT_TASK_AND_SESSION_OBSERVED' if not failures else 'REFUSED_ORIGINAL_NATIVE_OBSERVATION',
            'processExit': code, 'ownedProcess': map_witness, 'originalProfileDirectories': directory_rows,
            'profileWitnessSha256': profile['witnessSha256'], 'profileOriginalWitnessIdentity': witness_identity, 'state': state,
            'qualification': 'Original Configure<App>/classic window/shared presenter with pending/no decision/no trust/no execution and SAME original App exit task settlement. Original native session drain and retained profile inode custody observed separately. Fresh synthetic profile retained even on success; no cleanup inferred from marker/process exit, no Program.Main bootstrap or unrelated detached callback completion claim.'}, indent=2) + '\n')
        except BaseException as error:
            failures.append(error)
    if failures:
        raise BaseExceptionGroup('Original native Home observation and/or original cleanup failed.', failures)
