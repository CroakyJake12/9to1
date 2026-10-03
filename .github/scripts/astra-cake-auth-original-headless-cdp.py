"""Private test-only original headless-shell producer; no maintained-source edits.

The caller observes this session alongside its Node/Worker session and drains
both before removing any private state. Sampled /proc custody does not establish
that an unseen short-lived descendant could not escape between observations.
"""
import hashlib
import json
import os
import pathlib
import re
import selectors
import shutil
import stat
import subprocess
import time


def digest(path):
    h = hashlib.sha256()
    with pathlib.Path(path).open('rb') as stream:
        while block := stream.read(1024 * 1024):
            h.update(block)
    return h.hexdigest()


def identity(path):
    s = pathlib.Path(path).lstat()
    return {'device': s.st_dev, 'inode': s.st_ino,
            'mode': stat.S_IMODE(s.st_mode), 'type': stat.S_IFMT(s.st_mode)}


def exact_private_directory(path, parent):
    path, parent = pathlib.Path(path), pathlib.Path(parent)
    if not path.is_absolute() or not parent.is_absolute():
        raise ValueError('Explicit absolute private paths required')
    if path.parent != parent or parent.resolve(strict=True) != parent:
        raise ValueError('Private directory must be a direct child of its exact parent')
    if path.exists() or path.is_symlink():
        raise ValueError('Private directory must initially be absent')
    path.mkdir(mode=0o700)
    result = identity(path)
    if result['type'] != stat.S_IFDIR or result['mode'] != 0o700:
        raise ValueError('Private directory is not a real mode0700 directory')
    return path, result


def headless_executable(cache, harness, expected_browsers_sha, expected_exe_sha):
    """Match pinned Playwright1.63.0 registry naming for Ubuntu24.04 x64."""
    cache, harness = pathlib.Path(cache), pathlib.Path(harness)
    if not cache.is_absolute() or cache.resolve(strict=True) != cache:
        raise ValueError('Exact absolute privately installed browser cache required')
    package = harness / 'node_modules/playwright-core'
    if json.loads((package / 'package.json').read_text())['version'] != '1.63.0':
        raise ValueError('Unexpected maintained Playwright version')
    manifest = package / 'browsers.json'
    if digest(manifest) != expected_browsers_sha:
        raise ValueError('Maintained browser descriptor hash changed')
    matches = [r for r in json.loads(manifest.read_text())['browsers']
               if r['name'] == 'chromium-headless-shell']
    if len(matches) != 1 or str(matches[0]['revision']) != '1243':
        raise ValueError('Expected exactly pinned headless-shell revision1243')
    if matches[0].get('revisionOverrides', {}).get('ubuntu24.04-x64'):
        raise ValueError('Unreviewed browser revision override')
    executable = cache / 'chromium_headless_shell-1243/chrome-headless-shell-linux64/chrome-headless-shell'
    if executable.resolve(strict=True) != executable or not executable.is_file():
        raise ValueError('Selected headless executable is not its exact regular file')
    if not os.access(executable, os.X_OK) or digest(executable) != expected_exe_sha:
        raise ValueError('Original inventoried headless executable is not executable or changed')
    return executable


def owned_loopback_listener(pid, port):
    """Require the exact original PID to own the loopback listening socket FD."""
    fds = pathlib.Path('/proc') / str(pid) / 'fd'
    sockets = set()
    for fd in fds.iterdir():
        try:
            target = fd.readlink().as_posix()
        except FileNotFoundError:
            continue
        match = re.fullmatch(r'socket:\[(\d+)\]', target)
        if match:
            sockets.add(match.group(1))
    table = pathlib.Path('/proc') / str(pid) / 'net/tcp'
    matches = []
    for line in table.read_text().splitlines()[1:]:
        fields = line.split()
        address, raw_port = fields[1].split(':')
        if fields[3] == '0A' and address == '0100007F' and int(raw_port, 16) == port:
            matches.append(fields[9])
    if len(matches) != 1 or matches[0] not in sockets:
        raise RuntimeError('Issued CDP listener is not owned by the exact original PID')
    return matches[0]


class OriginalHeadlessCdpBrowser:
    def __init__(self, *, executable, expected_exe_sha, private_parent,
                 profile_path, records, receipt_path, sessions, env,
                 observe_other=lambda: None, timeout=30):
        self.executable = pathlib.Path(executable)
        self.executable_identity = identity(self.executable)
        if self.executable.resolve(strict=True) != self.executable or digest(self.executable) != expected_exe_sha:
            raise ValueError('Original browser source identity changed before launch')
        self.executable_sha = expected_exe_sha
        self.profile, self.profile_identity = exact_private_directory(profile_path, private_parent)
        self.records, self.receipt_path = pathlib.Path(records), pathlib.Path(receipt_path)
        if self.records.exists() or self.records.is_symlink():
            raise ValueError('Original browser session records must be fresh')
        self.records.mkdir(mode=0o700)
        (self.records / 'expected-managed-launch.json').write_text(
            json.dumps({'expectedManagedLaunch': True, 'drained': False}) + '\n')
        if self.receipt_path.exists() or self.receipt_path.is_symlink():
            raise ValueError('Original browser receipt must be fresh')
        self.receipt = {'status': 'original browser launch pending', 'drained': False,
                        'executable': str(self.executable), 'executableSha256': expected_exe_sha,
                        'executableIdentity': self.executable_identity,
                        'profile': str(self.profile), 'profileIdentity': self.profile_identity,
                        'qualification': 'Sampled original session and exact PID/socket custody; no unseen descendant completeness claim'}
        self.save()
        args = [str(self.executable), '--headless', '--no-sandbox',
                '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1',
                '--user-data-dir=' + str(self.profile), '--no-first-run',
                '--no-default-browser-check', 'about:blank']
        self.session = None
        self.process = None
        self.stderr_bytes = 0
        self.stderr_hash = hashlib.sha256()
        self.startup_bytes = bytearray()
        self.endpoint = None
        try:
            self.process = subprocess.Popen(args, env=env, stdin=subprocess.DEVNULL,
                                            stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                                            start_new_session=True)
            self.session = sessions.OriginalSession(self.process, self.records)
            self.receipt['launcherPid'] = self.process.pid
            self.receipt['launcherTuple'] = self.session.original
            self.save()
            os.set_blocking(self.process.stderr.fileno(), False)
            with selectors.DefaultSelector() as selector:
                selector.register(self.process.stderr, selectors.EVENT_READ)
                deadline = time.monotonic() + timeout
                while time.monotonic() < deadline:
                    observe_other()
                    self.observe()
                    if self.process.poll() is not None:
                        raise RuntimeError('Original headless browser exited before CDP admission')
                    for key, _ in selector.select(.05):
                        self.capture_stderr()
                    matches = re.findall(rb'(?:^|\n)DevTools listening on (ws://127\.0\.0\.1:(\d+)/devtools/browser/[a-zA-Z0-9-]+)(?:\r?\n)', self.startup_bytes)
                    if matches:
                        if len(matches) != 1:
                            raise RuntimeError('Original browser issued ambiguous CDP endpoints')
                        self.endpoint = matches[0][0].decode('ascii')
                        port = int(matches[0][1])
                        if not 1 <= port <= 65535:
                            raise RuntimeError('Invalid actual browser listener port')
                        self.observe()
                        socket_inode = owned_loopback_listener(self.process.pid, port)
                        self.receipt.update({'status': 'original browser CDP admitted',
                                             'endpoint': self.endpoint, 'listenerInode': socket_inode,
                                             'startupLogBytes': len(self.startup_bytes),
                                             'startupLogSha256': hashlib.sha256(self.startup_bytes).hexdigest()})
                        self.save()
                        break
                if self.endpoint is None:
                    raise TimeoutError('Original browser did not issue a proven CDP endpoint')
        except BaseException as primary:
            try:
                self.drain()
            except BaseException as cleanup:
                raise BaseExceptionGroup('Browser startup and original-session drain failed', [primary, cleanup])
            raise

    def save(self):
        self.receipt_path.write_text(json.dumps(self.receipt, indent=2) + '\n')

    def capture_stderr(self):
        if self.process.stderr.closed:
            return
        for _ in range(16):
            try:
                block = os.read(self.process.stderr.fileno(), 16384)
            except BlockingIOError:
                return
            if not block:
                return
            self.stderr_bytes += len(block)
            self.stderr_hash.update(block)
            if self.stderr_bytes > 4194304:
                raise RuntimeError('Original browser lifetime log exceeded bounded limit')
            if self.endpoint is None:
                self.startup_bytes.extend(block)
                if len(self.startup_bytes) > 262144:
                    raise RuntimeError('Original browser startup log exceeded bounded limit')

    def observe(self):
        if self.session is None:
            raise RuntimeError('Original browser identity was not captured; preserve private state')
        self.session.observe()
        self.capture_stderr()
        if self.process.poll() is None:
            proc_exe = pathlib.Path('/proc') / str(self.process.pid) / 'exe'
            if proc_exe.resolve(strict=True) != self.executable or identity(self.executable) != self.executable_identity:
                raise RuntimeError('Original browser executable identity changed')

    def drain(self):
        if self.session is None:
            raise RuntimeError('Original browser identity missing; preserve profile and false seal')
        self.session.drain()
        self.process.wait(timeout=5)
        self.capture_stderr()
        self.process.stderr.close()
        if digest(self.executable) != self.executable_sha or identity(self.executable) != self.executable_identity:
            raise RuntimeError('Original browser source changed during execution')
        self.receipt.update({'status': 'original browser observed session drained', 'drained': True,
                             'lifetimeStderrBytes': self.stderr_bytes,
                             'lifetimeStderrSha256': self.stderr_hash.hexdigest()})
        self.save()

    def remove_profile(self, other_records):
        """Only after both independently original sessions have complete drain seals."""
        if self.receipt.get('drained') is not True:
            raise RuntimeError('Original browser is not drained; preserve profile')
        other_records = pathlib.Path(other_records)
        if other_records.resolve(strict=True) == self.records.resolve(strict=True):
            raise RuntimeError('Browser and Node original records must be distinct')
        originals = []
        for records in [self.records, other_records]:
            expected_path = records / 'expected-managed-launch.json'
            if not expected_path.is_file() or expected_path.is_symlink():
                raise RuntimeError('Expected original launch seal is absent; preserve profile')
            expected = json.loads(expected_path.read_text())
            pid = expected.get('launcherPid')
            if (expected.get('expectedManagedLaunch') is not True
                    or expected.get('drained') is not True
                    or type(pid) is not int or pid <= 0
                    or not isinstance(expected.get('launcherStartTicks'), str)):
                raise RuntimeError('Expected original launch seal is incomplete; preserve profile')
            pending_path = records / (str(pid) + '-pending.json')
            if not pending_path.is_file() or pending_path.is_symlink():
                raise RuntimeError('Original identity witness is absent; preserve profile')
            pending = json.loads(pending_path.read_text())
            original = pending.get('launcherTuple')
            observed_root = pending.get('observedMembers', {}).get(str(pid))
            if (pending.get('drained') is not True or pending.get('launcherPid') != pid
                    or not isinstance(original, list) or len(original) != 5
                    or original[1:3] != [pid, pid]
                    or original[3] != expected['launcherStartTicks']
                    or not isinstance(observed_root, list) or len(observed_root) != 5
                    or observed_root[1:4] != original[1:4]):
                raise RuntimeError('Original identity witness disagrees with launch seal; preserve profile')
            originals.append((pid, original[3]))
            witnesses = list(records.glob('*.json'))
            if not witnesses or not all(json.loads(p.read_text()).get('drained') is True for p in witnesses):
                raise RuntimeError('An original session drain witness is incomplete; preserve profile')
        if originals[0] != (self.process.pid, self.session.original[3]) or originals[0] == originals[1]:
            raise RuntimeError('Dual original identity witnesses are not the captured browser and separate Node session')
        if identity(self.profile) != self.profile_identity or self.profile.is_symlink():
            raise RuntimeError('Private browser profile identity changed; refuse removal')
        if not shutil.rmtree.avoids_symlink_attacks:
            raise RuntimeError('Profile removal lacks filesystem symlink protection')
        shutil.rmtree(self.profile)
        if self.profile.exists() or self.profile.is_symlink():
            raise RuntimeError('Private browser profile removal was not observed')
        self.receipt['profileRemovedAfterBothDrains'] = True
        self.save()
