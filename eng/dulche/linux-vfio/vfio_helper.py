#!/usr/bin/env python3
"""Privileged, single-device helper for the owner's one explicitly approved proof.

Preparation only reads metadata. A separate, published consumed receipt and a
timed bind command are required before kernel writes. Never invoke directly to
probe a GPU. Stdin/stdout belong to the reviewed host controller, not a shell.
"""
from __future__ import annotations
import argparse
from contextlib import ExitStack
import fcntl
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import select
import selectors
import shlex
import signal
import stat
import subprocess
import sys
import time

GPU = '0000:05:00.0'
RTX = '0000:01:00.0'
PROOF = 'v100-tinyllama-one-proof-20261116'
PCI = Path('/sys/bus/pci/devices')
VFIO_DEVICES = Path('/dev/vfio')
AUTHORITY = Path(__file__).resolve().parent / 'authorizations' / PROOF
OWNER_UID = 1000
WORK_NS = 25_000_000_000
TOTAL_NS = 30_000_000_000
LEASE_NS = 750_000_000


class Refusal(RuntimeError):
    pass


class WriteUncertain(Refusal):
    def __init__(self, message: str, process: subprocess.Popen | None = None):
        super().__init__(message)
        self.process = process


class ControlStop(Exception):
    pass


def strict_json(data: bytes) -> dict:
    def pairs(items):
        value = {}
        for key, item in items:
            if key in value:
                raise Refusal('Duplicate JSON control field')
            value[key] = item
        return value
    def nonfinite(_):
        raise Refusal('Non-finite JSON control value')
    try:
        value = json.loads(data, object_pairs_hook=pairs, parse_constant=nonfinite)
    except (ValueError, UnicodeError, RecursionError) as error:
        raise Refusal('Malformed JSON control') from error
    if not isinstance(value, dict):
        raise Refusal('JSON object required')
    return value


def emit(event: str, **fields) -> None:
    data = (json.dumps({'event': event, **fields}, separators=(',', ':')) + '\n').encode()
    # Pipe messages are below PIPE_BUF. Never block teardown on a lost reader.
    try:
        os.write(sys.stdout.fileno(), data)
    except (BlockingIOError, BrokenPipeError):
        pass


def read(path: Path) -> str:
    return path.read_text().strip()


def validate_snapshot(snapshot: dict) -> None:
    expected = {'vendor': '0x10de', 'device': '0x1db4',
                'subsystem_vendor': '0x10de', 'subsystem_device': '0x1214',
                'driver': None, 'override': '(null)', 'group_members': [GPU],
                'reset_domain': [GPU], 'reset_methods': ['bus'],
                'upstream': '0000:00:1c.4', 'rtx_driver': 'nvidia',
                'rtx_device': '0x2c02', 'rtx_boot_vga': '1'}
    for key, value in expected.items():
        if key == 'override' and snapshot.get(key) in ('', '(null)'):
            continue
        if snapshot.get(key) != value:
            raise Refusal('PCI preflight changed: ' + key)
    if not re.fullmatch(r'[0-9]+', snapshot.get('group', '')):
        raise Refusal('Missing IOMMU group')
    if 'Open Kernel Module' not in snapshot.get('nvidia_version', ''):
        raise Refusal('Host open driver not confirmed')
    lines = snapshot.get('reserved_regions', '').splitlines()
    if not lines or any(not re.fullmatch(r'0x[0-9a-f]+ 0x[0-9a-f]+ msi', x) for x in lines):
        raise Refusal('Reserved-region review required')
    if any(int(line.split()[0], 16) > int(line.split()[1], 16) for line in lines):
        raise Refusal('Invalid reserved-region range')
    if any(x in snapshot.get('cmdline', '') for x in (
            'pcie_acs_override', 'iommu=off', 'intremap=off', 'unsafe')):
        raise Refusal('Unsafe kernel override')


def snapshot() -> dict:
    p = PCI / GPU
    group = (p / 'iommu_group').resolve(strict=True)
    parent = p.resolve(strict=True).parent
    driver = p / 'driver'
    try:
        driver.lstat()
    except FileNotFoundError:
        driver_name = None
    else:
        driver_name = driver.resolve(strict=True).name
    return {
        **{key: read(p / key) for key in ('vendor', 'device', 'subsystem_vendor', 'subsystem_device')},
        'driver': driver_name, 'override': read(p / 'driver_override'),
        'group': group.name, 'group_members': sorted(x.name for x in (group / 'devices').iterdir()),
        'reset_domain': sorted(x.name for x in PCI.iterdir() if parent in x.resolve().parents),
        'reset_methods': read(p / 'reset_method').split(), 'upstream': parent.name,
        'rtx_driver': (PCI / RTX / 'driver').resolve(strict=True).name,
        'rtx_device': read(PCI / RTX / 'device'), 'rtx_boot_vga': read(PCI / RTX / 'boot_vga'),
        'reserved_regions': read(group / 'reserved_regions'),
        'nvidia_version': read(Path('/proc/driver/nvidia/version')),
        'cmdline': read(Path('/proc/cmdline')),
    }


def process_start(pid: int) -> str:
    return read(Path(f'/proc/{pid}/stat')).rsplit(') ', 1)[1].split()[19]


def pin_qemu(pid: int, start: str, name: str) -> int:
    if pid <= 1 or not start.isdecimal() or not re.fullmatch(r'dulche-v100-proof-[a-z0-9-]+', name):
        raise Refusal('Invalid owned QEMU identity')
    fd = os.pidfd_open(pid)
    try:
        proc = Path(f'/proc/{pid}')
        if proc.stat().st_uid != OWNER_UID or process_start(pid) != start:
            raise Refusal('QEMU process identity changed')
        if (proc / 'exe').resolve(strict=True) != Path('/usr/bin/qemu-system-x86_64'):
            raise Refusal('Unexpected QEMU executable')
        argv = (proc / 'cmdline').read_bytes().split(b'\0')
        if b'-name' not in argv or argv[argv.index(b'-name') + 1] != name.encode():
            raise Refusal('Unexpected QEMU session name')
        if b'vfio-pci' in b' '.join(argv):
            raise Refusal('QEMU already has a physical assignment')
        return fd
    except BaseException:
        os.close(fd)
        raise


def controller_owner(pid: int, start_ticks: int, session: str) -> dict:
    if type(pid) is not int or pid <= 1 or type(start_ticks) is not int or start_ticks <= 0:
        raise Refusal('Invalid controller process identity')
    if os.getppid() != pid:
        raise Refusal('Helper is not the claimed controller child')
    proc = Path('/proc') / str(pid)
    if proc.stat().st_uid != OWNER_UID or int(process_start(pid)) != start_ticks:
        raise Refusal('Controller PID ownership/start changed')
    boot = read(Path('/proc/sys/kernel/random/boot_id'))
    if not re.fullmatch(r'[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}', boot):
        raise Refusal('Invalid boot identity')
    return {'controller_pid': pid, 'controller_start_ticks': start_ticks,
            'boot_id': boot, 'session': session}


def validate_authority_records(records: dict, raw: dict, receipt_sha: str,
                               manifest_sha: str, owner: dict) -> None:
    schemas = {
        'CONSUMED.json': 'dulche.v100-consumed/v1',
        'PUBLICATION.json': 'dulche.v100-publication/v1',
        'DISPATCH-CLAIMED.json': 'dulche.v100-dispatch-claimed/v1',
        'DISPATCH-PUBLICATION.json': 'dulche.v100-dispatch-publication/v1',
        'EXECUTION-STARTED.json': 'dulche.v100-execution-started/v1',
    }
    if set(records) != set(schemas) or set(raw) != set(schemas):
        raise Refusal('Incomplete one-use authority chain')
    if hashlib.sha256(raw['CONSUMED.json']).hexdigest() != receipt_sha:
        raise Refusal('Consumed receipt changed')
    dispatch_sha = hashlib.sha256(raw['DISPATCH-CLAIMED.json']).hexdigest()
    for name, record in records.items():
        if (record.get('schema') != schemas[name] or record.get('proof_id') != PROOF
                or record.get('manifest_sha256') != manifest_sha):
            raise Refusal('Authority identity/schema mismatch: ' + name)
        if name != 'CONSUMED.json' and record.get('consumed_sha256') != receipt_sha:
            raise Refusal('Authority consume chain mismatch')
    consumed, published = records['CONSUMED.json'], records['PUBLICATION.json']
    dispatch, dispatch_published, execution = (records[name] for name in (
        'DISPATCH-CLAIMED.json', 'DISPATCH-PUBLICATION.json', 'EXECUTION-STARTED.json'))
    if consumed.get('consumed') is not True:
        raise Refusal('Allowance is not consumed')
    for record in (consumed, published, dispatch_published):
        if record.get('repository') != 'CroakyJake12/9to1' or record.get('remote_ref') != 'refs/heads/main':
            raise Refusal('Noncanonical authority repository')
    consume_commit = published.get('consumed_commit_sha')
    dispatch_commit = dispatch_published.get('dispatch_commit_sha')
    if any(type(value) is not str or not re.fullmatch('[0-9a-f]{40}', value)
           for value in (consume_commit, dispatch_commit)):
        raise Refusal('Invalid publication commit identity')
    if (published.get('observed_main_sha') != consume_commit
            or dispatch.get('consumed_commit_sha') != consume_commit
            or dispatch_published.get('consumed_commit_sha') != consume_commit
            or dispatch_published.get('observed_main_sha') != dispatch_commit
            or execution.get('dispatch_commit_sha') != dispatch_commit):
        raise Refusal('Authority publication commit mismatch')
    for record in (dispatch_published, execution):
        if record.get('dispatch_sha256') != dispatch_sha:
            raise Refusal('Canonical dispatch bytes changed')
    for record in (dispatch, dispatch_published, execution):
        candidate = record.get('owner')
        if (candidate != owner or not isinstance(candidate, dict)
                or type(candidate.get('controller_pid')) is not int
                or type(candidate.get('controller_start_ticks')) is not int):
            raise Refusal('Authority controller/session/boot ownership mismatch')
    if dispatch.get('no_retry') is not True or execution.get('no_retry') is not True:
        raise Refusal('One-use execution barrier not present')


def validate_consumed(receipt_sha: str, manifest_sha: str, owner: dict) -> None:
    if not all(type(x) is str and re.fullmatch(r'[0-9a-f]{64}', x) for x in (receipt_sha, manifest_sha)):
        raise Refusal('Invalid receipt hashes')
    records, raw_records = {}, {}
    for name in ('CONSUMED.json', 'PUBLICATION.json', 'DISPATCH-CLAIMED.json',
                 'DISPATCH-PUBLICATION.json', 'EXECUTION-STARTED.json'):
        p = AUTHORITY / name
        fd = os.open(p, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(fd, 'rb') as f:
            info = os.fstat(f.fileno())
            if not stat.S_ISREG(info.st_mode) or info.st_size > 16384:
                raise Refusal('Invalid authority file')
            raw = f.read(16385)
            after = os.fstat(f.fileno())
        if (len(raw) != info.st_size or (info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns)
                != (after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)):
            raise Refusal('Authority record changed while reading')
        raw_records[name], records[name] = raw, strict_json(raw)
    validate_authority_records(records, raw_records, receipt_sha, manifest_sha, owner)


def claim_helper(session: str, receipt_sha: str, qemu_pid: int, start_ns: int) -> None:
    """Additional privileged one-use marker; never a substitute for remote CAS."""
    info = AUTHORITY.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != OWNER_UID:
        raise Refusal('Authority directory is not the expected owned directory')
    payload = (json.dumps({'schema': 'dulche.v100-helper-claimed/v1', 'proof_id': PROOF,
                          'session': session, 'consumed_sha256': receipt_sha,
                          'qemu_pid': qemu_pid, 'start_ns': start_ns}, sort_keys=True)+'\n').encode()
    directory = os.open(AUTHORITY, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        fd = os.open('HELPER-CLAIMED.json', os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     0o444, dir_fd=directory)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.fsync(directory)
    finally:
        os.close(directory)


def bounded_command(argv: list[str], timeout: float = 1.0, max_output: int = 8192) -> bytes:
    """No communicate()/wait() that can hang reaping a kernel-stuck child."""
    process = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT,
                               env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'LANG': 'C'})
    os.set_blocking(process.stdout.fileno(), False)
    output = bytearray()
    eof = False
    end = time.monotonic() + timeout
    try:
        while True:
            while not eof:
                try:
                    chunk = os.read(process.stdout.fileno(), 8192)
                except BlockingIOError:
                    break
                if not chunk:
                    eof = True
                    break
                output.extend(chunk)
                if len(output) > max_output:
                    raise WriteUncertain('Administrative command output exceeded bound', process)
            code = process.poll()
            # Require EOF as well as exit: output can arrive after the previous
            # read but before poll() observes exit. Never return a partial ACL.
            if code is not None and eof:
                if code:
                    raise Refusal(f'{argv[0]} failed: {output[:200]!r}')
                return bytes(output)
            if time.monotonic() >= end:
                raise WriteUncertain(f'Administrative command timed out: {argv[0]} (child {process.pid})', process)
            time.sleep(.005)
    except OSError as error:
        raise WriteUncertain('Administrative command I/O failed: ' + str(error), process) from error
    finally:
        if process.poll() is None:
            try:
                process.kill()
            except ProcessLookupError:
                pass
            process.poll()  # nonblocking; a D-state child remains uncertain
        process.stdout.close()


VFIO_MODULES = {'vfio', 'vfio_pci', 'vfio_pci_core', 'vfio_iommu_type1',
                'vfio_virqfd', 'iommufd', 'irqbypass'}


def module_name(value: str) -> str:
    return value.replace('-', '_')


def validate_module_config(data: bytes) -> None:
    """Review kmod's effective configuration without executing any hook."""
    for line in data.decode('utf-8', errors='strict').splitlines():
        words = shlex.split(line, comments=True)
        if not words:
            continue
        directive = words[0]
        if len(words) < 2:
            continue
        target = module_name(words[1])
        if directive in ('install', 'remove') and target in VFIO_MODULES:
            raise Refusal('VFIO module hook requires review')
        if directive == 'softdep' and target in VFIO_MODULES:
            if any(module_name(word) not in VFIO_MODULES for word in words[2:]
                   if word not in ('pre:', 'post:')):
                raise Refusal('VFIO soft dependency outside the approved module set')
        if directive == 'options' and target in VFIO_MODULES:
            for option in words[2:]:
                if option not in ('enable_unsafe_noiommu_mode=0', 'allow_unsafe_interrupts=0'):
                    raise Refusal('Unreviewed VFIO module option: ' + option)
        if directive == 'alias' and len(words) >= 3:
            destination = module_name(words[2])
            if target in VFIO_MODULES or destination in VFIO_MODULES:
                if target != destination and not (
                        destination == 'vfio' and target in ('devname:vfio/vfio', 'char_major_10_196')):
                    raise Refusal('VFIO module alias requires review')


def validate_dependency_plan(data: bytes) -> None:
    kernel = platform.release()
    roots = (f'/lib/modules/{kernel}/', f'/usr/lib/modules/{kernel}/')
    lines = data.decode('utf-8', errors='strict').splitlines()
    if not lines:
        raise Refusal('Empty VFIO dependency plan')
    for line in lines:
        words = shlex.split(line, comments=True)
        if not words:
            continue
        if words[0] not in ('insmod', 'builtin') or len(words) < 2:
            raise Refusal('VFIO dependency plan contains a hook or parameters')
        if any(option not in ('enable_unsafe_noiommu_mode=0', 'allow_unsafe_interrupts=0')
               for option in words[2:]):
            raise Refusal('VFIO dependency plan contains unreviewed parameters')
        name = words[1]
        if words[0] == 'insmod':
            if not name.startswith(roots) or '..' in Path(name).parts:
                raise Refusal('Module dependency outside the active kernel')
            name = Path(name).name.split('.ko', 1)[0]
        if module_name(name) not in VFIO_MODULES:
            raise Refusal('Unexpected VFIO dependency module: ' + name)


def prepare_module_plan() -> dict:
    # vfio-pci's ids parameter need not be exposed through sysfs. An already
    # loaded instance cannot be attested from current config alone.
    if Path('/sys/module/vfio_pci').exists():
        raise Refusal('Existing vfio-pci module requires separate ownership review')
    config = bounded_command(['/usr/sbin/modprobe', '--showconfig'], 2., 64 * 1024 * 1024)
    validate_module_config(config)
    plans = {}
    for name in ('vfio-pci', 'vfio_iommu_type1'):
        plan = bounded_command(['/usr/sbin/modprobe', '--show-depends', name], .5, 16384)
        validate_dependency_plan(plan)
        plans[name] = plan
    return {'plans': plans, 'config_sha256': hashlib.sha256(config).hexdigest()}


def bounded_sysfs_write(path: Path, value: str, timeout: float = .75) -> None:
    """Keep the independent stopper runnable even if a kernel write hangs.

    A killed child stuck in uninterruptible kernel sleep is reported uncertain;
    this is not a claim that Linux can cancel an arbitrary device operation.
    No retry follows. The owned QEMU process is still stopped independently.
    """
    allowed = {str(PCI / GPU / 'driver_override'): {'vfio-pci\n', '\n'},
               '/sys/bus/pci/drivers/vfio-pci/bind': {GPU + '\n'},
               '/sys/bus/pci/drivers/vfio-pci/unbind': {GPU + '\n'}}
    if value not in allowed.get(str(path), set()):
        raise Refusal('Unapproved sysfs target/value')
    pid = os.fork()
    if pid == 0:
        try:
            path.write_text(value)
        except BaseException:
            os._exit(1)
        os._exit(0)
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        done, status = os.waitpid(pid, os.WNOHANG)
        if done:
            if os.waitstatus_to_exitcode(status) != 0:
                raise Refusal('Scoped sysfs write failed')
            return
        time.sleep(.005)
    os.kill(pid, signal.SIGKILL)
    # A kernel-stuck child may not be reapable. Never wait indefinitely or retry.
    os.waitpid(pid, os.WNOHANG)
    raise WriteUncertain(f'Scoped sysfs write timed out; kernel operation uncertain (child {pid})')


class Binding:
    def __init__(self, group: str):
        self.group = group
        self.override_changed = False
        self.bind_attempted = False
        self.binding_uncertain = False
        self.acl_changed = False
        self.prior_acl = None
        self.node_identity = None
        self.start_ns = None
        self.module_plan = None
        self.acl_pending_process = None
        self.acl_uncertain = False

    def remaining(self, *, cleanup: bool = False) -> float:
        limit = TOTAL_NS if cleanup else 8_000_000_000
        if self.start_ns is None:
            raise Refusal('Scoped device operation deadline expired')
        remaining = (self.start_ns + limit - time.monotonic_ns()) / 1e9
        if remaining <= 0:
            raise Refusal('Scoped device operation deadline expired')
        return remaining

    def check_time(self, *, cleanup: bool = False) -> None:
        self.remaining(cleanup=cleanup)

    def command(self, argv: list[str], timeout: float, *, cleanup: bool = False) -> bytes:
        return bounded_command(argv, min(timeout, self.remaining(cleanup=cleanup)))

    def write(self, path: Path, value: str, *, cleanup: bool = False) -> None:
        self.check_time(cleanup=cleanup)
        try:
            bounded_sysfs_write(path, value, min(.75, self.remaining(cleanup=cleanup)))
        except WriteUncertain:
            self.binding_uncertain = True
            raise

    def bind(self, start_ns: int) -> None:
        self.start_ns = start_ns
        current = snapshot()
        validate_snapshot(current)
        if current['group'] != self.group or (VFIO_DEVICES / self.group).exists():
            raise Refusal('VFIO group identity changed or node already owned')
        if self.module_plan is None or Path('/sys/module/vfio_pci').exists():
            raise Refusal('Fresh reviewed VFIO module plan required')
        # All these operations are inside the single proof timer. No persistent
        # modprobe configuration, id wildcard, boot rule or global blacklist.
        for name in ('vfio-pci', 'vfio_iommu_type1'):
            plan = self.command(['/usr/sbin/modprobe', '--show-depends', name], .5)
            validate_dependency_plan(plan)
            if plan != self.module_plan['plans'][name]:
                raise Refusal('VFIO dependency plan changed after preparation')
        # Compare both plans before changing module-loaded state; otherwise
        # loading the first can legitimately change the second printed plan.
        for name in ('vfio_iommu_type1', 'vfio-pci'):
            # --ignore-install adds defence against a target hook appearing
            # after inspection. Dependencies are restricted by the reviewed plan.
            self.command(['/usr/sbin/modprobe', '--ignore-install', name], .5)
        for p in ('/sys/module/vfio/parameters/enable_unsafe_noiommu_mode',
                  '/sys/module/vfio_iommu_type1/parameters/allow_unsafe_interrupts'):
            if read(Path(p)) not in ('N', '0'):
                raise Refusal('Unsafe/unavailable VFIO mode')
        validate_snapshot(snapshot())
        self.override_changed = True
        self.write(PCI / GPU / 'driver_override', 'vfio-pci\n')
        self.bind_attempted = True
        self.binding_uncertain = True
        try:
            self.write(Path('/sys/bus/pci/drivers/vfio-pci/bind'), GPU + '\n')
        except WriteUncertain:
            raise
        except BaseException:
            self.binding_uncertain = False
            raise
        self.binding_uncertain = False
        if (PCI / GPU / 'driver').resolve(strict=True).name != 'vfio-pci':
            raise Refusal('VFIO binding not confirmed')
        node = VFIO_DEVICES / self.group
        info = node.lstat()
        if not stat.S_ISCHR(info.st_mode):
            raise Refusal('Unexpected VFIO group node')
        self.node_identity = (info.st_dev, info.st_ino, info.st_rdev)
        self.check_time()
        self.prior_acl = self.command(['/usr/bin/getfacl', '-cpn', str(node)], .3).decode().strip()
        if not self.prior_acl or any(not re.fullmatch(r'(user|group|mask|other):[0-9]*:[rwx-]{3}', x)
                                     for x in self.prior_acl.splitlines()):
            raise Refusal('Unexpected VFIO ACL')
        self.acl_changed = True
        try:
            self.command(['/usr/bin/setfacl', '-m', f'u:{OWNER_UID}:rw', str(node)], .3)
        except WriteUncertain as error:
            self.acl_uncertain = True
            self.acl_pending_process = error.process
            raise

    def restore(self, qemu_stopped: bool) -> dict:
        if not qemu_stopped:
            return {'restored': False, 'reason': 'QEMU cessation not confirmed; device left owned'}
        if self.binding_uncertain:
            return {'restored': False, 'reason': 'Binding operation uncertain; no conflicting write or retry attempted'}
        if self.acl_uncertain:
            if self.acl_pending_process is None or self.acl_pending_process.poll() is None:
                return {'restored': False, 'reason': 'ACL mutation still uncertain; no conflicting device/ACL writes'}
            self.acl_uncertain = False  # exact child is now known to have ceased
        if self.override_changed and read(PCI / GPU / 'driver_override') not in ('vfio-pci', '', '(null)'):
            return {'restored': False, 'reason': 'Driver override changed; no foreign device/override write'}
        errors = []
        node = VFIO_DEVICES / self.group
        if self.acl_changed and node.exists():
            try:
                info = node.lstat()
                if (info.st_dev, info.st_ino, info.st_rdev) != self.node_identity:
                    return {'restored': False, 'reason': 'VFIO node replaced; binding custody lost, no following writes'}
                self.check_time(cleanup=True)
                self.command(['/usr/bin/setfacl', '--set', ','.join(self.prior_acl.splitlines()), str(node)], .3, cleanup=True)
            except WriteUncertain as error:
                self.acl_uncertain = True
                self.acl_pending_process = error.process
                return {'restored': False, 'reason': 'ACL restore uncertain; no following device writes', 'errors': [str(error)]}
            except (OSError, Refusal, subprocess.TimeoutExpired) as error:
                errors.append(str(error))
        driver = PCI / GPU / 'driver'
        if self.bind_attempted and driver.is_symlink():
            if driver.resolve().name == 'vfio-pci':
                try:
                    self.write(Path('/sys/bus/pci/drivers/vfio-pci/unbind'), GPU + '\n', cleanup=True)
                except (OSError, Refusal) as error:
                    errors.append(str(error))
            else:
                errors.append('Driver changed unexpectedly; no foreign unbind attempted')
        if self.override_changed and not self.binding_uncertain and not driver.exists():
            try:
                current = read(PCI / GPU / 'driver_override')
                if current == 'vfio-pci':
                    self.write(PCI / GPU / 'driver_override', '\n', cleanup=True)
                elif current not in ('', '(null)'):
                    raise Refusal('Driver override changed; no foreign override clear')
            except (OSError, Refusal) as error:
                errors.append(str(error))
        return {'restored': not errors and not driver.exists(), 'errors': errors}


def stop_qemu(pidfd: int, deadline_ns: int) -> bool:
    if select.select([pidfd], [], [], 0)[0]:
        return True
    try:
        signal.pidfd_send_signal(pidfd, signal.SIGTERM)
    except ProcessLookupError:
        return bool(select.select([pidfd], [], [], 0)[0])
    if select.select([pidfd], [], [], min(.2, max(0., (deadline_ns-time.monotonic_ns())/1e9)))[0]:
        return True
    try:
        signal.pidfd_send_signal(pidfd, signal.SIGKILL)
    except ProcessLookupError:
        return bool(select.select([pidfd], [], [], 0)[0])
    remaining = max(0, min(1., (deadline_ns - time.monotonic_ns()) / 1e9))
    return bool(select.select([pidfd], [], [], remaining)[0])


def validate_control(request: dict, session: str, sequence: int,
                     last_message: int, started: bool, received: int) -> tuple[int, int]:
    if not isinstance(request, dict) or request.get('session') != session:
        raise Refusal('Control session mismatch')
    sent, seq = request.get('sent_ns'), request.get('sequence')
    if (type(sent) is not int or sent < 0 or not 0 <= received - sent <= LEASE_NS
            or started and sent <= last_message
            or type(seq) is not int or seq != sequence + 1):
        raise Refusal('Stale/replayed control message')
    return sent, seq


def finalize(binding: Binding, pidfd: int, start: int | None, reason: str,
             session: str, stop_unbound: bool = False) -> bool:
    """Independent error boundaries preserve stopping, rollback and reporting."""
    if start is None and not stop_unbound:
        emit('not_dispatched', session=session, reason=reason)
        return True
    deadline = start + TOTAL_NS if start is not None else time.monotonic_ns() + 1_500_000_000
    errors = []
    try:
        stopped = stop_qemu(pidfd, deadline)
    except (OSError, ValueError, Refusal) as error:
        stopped = False
        errors.append('QEMU stop failed: ' + str(error))
    try:
        restored = binding.restore(stopped) if start is not None else {
            'restored': True, 'no_device_changes': True}
    except (OSError, ValueError, Refusal) as error:
        restored = {'restored': False, 'reason': 'Rollback failed: ' + str(error)}
    elapsed = time.monotonic_ns() - start if start is not None else 0
    emit('stopped', session=session, reason=reason, qemu_stopped=stopped,
         binding_restored=restored.get('restored') is True, cleanup=restored,
         stop_errors=errors, elapsed_ns=elapsed)
    return stopped and restored.get('restored') is True and time.monotonic_ns() <= deadline


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--qemu-pid', type=int, required=True)
    parser.add_argument('--qemu-start', required=True)
    parser.add_argument('--qemu-name', required=True)
    parser.add_argument('--session', required=True)
    parser.add_argument('--controller-pid', type=int, required=True)
    parser.add_argument('--controller-start', type=int, required=True)
    args = parser.parse_args()
    if os.geteuid() != 0 or not re.fullmatch(r'[0-9a-f]{32}', args.session):
        raise Refusal('Root authorization and exact session identity required')
    with ExitStack() as owned:
        lockfd = os.open('/run/lock/v100-control.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        owned.callback(os.close, lockfd)
        lock_info = os.fstat(lockfd)
        if not stat.S_ISREG(lock_info.st_mode) or lock_info.st_uid != 0:
            raise Refusal('Global VFIO lock must be a root-owned regular file')
        fcntl.flock(lockfd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        controller_owner(args.controller_pid, args.controller_start, args.session)
        controller_fd = os.pidfd_open(args.controller_pid)
        owned.callback(os.close, controller_fd)
        os.set_blocking(sys.stdout.fileno(), False)
        os.set_blocking(sys.stdin.fileno(), False)
        observed = snapshot()
        validate_snapshot(observed)
        pidfd = pin_qemu(args.qemu_pid, args.qemu_start, args.qemu_name)
        owned.callback(os.close, pidfd)
        binding = Binding(observed['group'])
        binding.module_plan = prepare_module_plan()
        start = None
        prepared = time.monotonic_ns()
        last_message = prepared
        sequence = -1
        reason = 'not_armed'
        status = 2
        stop_unbound = False
        buffer = b''
        emit('ready', qemu_pid=args.qemu_pid, session=args.session,
             module_config_sha256=binding.module_plan['config_sha256'])
        try:
            with selectors.DefaultSelector() as selector:
                selector.register(sys.stdin.fileno(), selectors.EVENT_READ)
                while True:
                    now = time.monotonic_ns()
                    if start is not None and now - start >= WORK_NS:
                        reason = 'work_deadline'
                        break
                    if start is None and now - prepared > 180_000_000_000:
                        reason = 'preparation_timeout'
                        break
                    if select.select([controller_fd], [], [], 0)[0]:
                        reason = 'controller_exited'
                        break
                    if select.select([pidfd], [], [], 0)[0]:
                        reason = 'qemu_exited'
                        break
                    for _, _ in selector.select(.025):
                        data = os.read(sys.stdin.fileno(), 4096)
                        if not data:
                            raise Refusal('controller_eof')
                        buffer += data
                        if len(buffer) > 4096:
                            raise Refusal('Oversize control message')
                        while b'\n' in buffer:
                            line, buffer = buffer.split(b'\n', 1)
                            request = strict_json(line)
                            action = request.get('action')
                            received = time.monotonic_ns()
                            last_message, sequence = validate_control(request, args.session, sequence,
                                last_message, start is not None, received)
                            if action == 'bind':
                                if start is not None or set(request) != {'action','session','start_ns','receipt_sha256','manifest_sha256','sent_ns','sequence'}:
                                    raise Refusal('Bind is one-use')
                                stamp = request['start_ns']
                                if type(stamp) is not int or stamp < 0 or not 0 <= received - stamp <= 500_000_000:
                                    raise Refusal('Invalid/old proof start')
                                owner = controller_owner(args.controller_pid, args.controller_start, args.session)
                                validate_consumed(request['receipt_sha256'], request['manifest_sha256'], owner)
                                start = stamp
                                claim_helper(args.session, request['receipt_sha256'], args.qemu_pid, start)
                                binding.bind(start)
                                emit('bound', session=args.session, elapsed_ns=time.monotonic_ns()-start)
                            elif action == 'heartbeat' and start is not None and set(request) == {'action','session','sent_ns','sequence'}:
                                emit('alive', session=args.session)
                            elif action == 'stop' and set(request) == {'action','session','sent_ns','sequence'}:
                                raise ControlStop
                            else:
                                raise Refusal('Invalid control action')
                    if start is not None and time.monotonic_ns() - last_message > LEASE_NS:
                        reason = 'controller_lease_expired'
                        break
        except ControlStop:
            reason, status, stop_unbound = 'controller_stop', 0, True
        except (OSError, ValueError, Refusal, subprocess.TimeoutExpired) as error:
            reason = str(error)
        finally:
            if not finalize(binding, pidfd, start, reason, args.session, stop_unbound):
                status = 2
        return status


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, Refusal) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2)
