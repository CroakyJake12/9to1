"""One fixed proof identity, append-only local receipts, and approved cold phase.

This module never launches a process, calls Git, or touches a GPU. Root must
freshly inspect canonical GitHub main before consumption, commit/push the exact
consume bytes, then supply authenticated remote readback to record_publication.
Every runner/OS must consult that same remote record. Local file modes and Git
history are tamper-evidence, not protection against an owner rewriting history
or modifying this code. A copied checkout is not a new allowance.

The public API has no alternative authority directory or proof-ID parameter.
Tests patch the private directory constant; that is not a production interface.
Any partial/existing consumed or dispatch record refuses reuse. There is no
delete, reset, refund, retry or hardware execution API.
"""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat

from proof_safety import Refusal, SafetyPolicy, WORK_NS, TOTAL_NS

PROOF_ID = 'v100-tinyllama-one-proof-20261116'
REPOSITORY = 'CroakyJake12/9to1'
REMOTE_REF = 'refs/heads/main'
MODEL_SHA256 = '9fecc3b3cd76bba89d504f29b616eedf7da85b96540e490ca5824d3f7d2776a0'
CONSUMED_RELATIVE_PATH = f'eng/dulche/linux-vfio/authorizations/{PROOF_ID}/CONSUMED.json'
_AUTHORITY_DIRECTORY = Path(__file__).resolve().parent / 'authorizations' / PROOF_ID
_GPU_UUID = re.compile(r'GPU-[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}')
COLD_NS = 8_000_000_000
POLICY = {
    'allowances': 1, 'cold_start_seconds': 8, 'work_cutoff_seconds': 25,
    'total_seconds': 30, 'sample_interval_seconds': 0.25,
    'stale_after_first_sample_seconds': 0.75, 'v100_start_max_c': 40,
    'v100_stop_c': 55, 'v100_rise_stop_c': 10, 'rtx5080_stop_c': 75,
    'retry': False,
}


def _hex(value: str, length: int, label: str) -> str:
    if type(value) is not str or not re.fullmatch('[0-9a-f]{' + str(length) + '}', value):
        raise Refusal('invalid_' + label)
    return value


def _uuid(value: str) -> str:
    if type(value) is not str or not _GPU_UUID.fullmatch(value):
        raise Refusal('invalid_gpu_uuid')
    return value


def _directory(create: bool = False) -> int:
    path = _AUTHORITY_DIRECTORY
    if create:
        for current in (path.parent, path):
            try:
                current.mkdir(mode=0o700)
            except FileExistsError:
                pass
            info = current.lstat()
            if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid():
                raise Refusal('authority_directory_not_owned_regular_directory')
            parent = os.open(current.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                os.fsync(parent)
            finally:
                os.close(parent)
    if path.parent.is_symlink():
        raise Refusal('authority_parent_symlink')
    return os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)


def _write_once(name: str, record: dict) -> str:
    payload = (json.dumps(record, sort_keys=True, separators=(',', ':'), allow_nan=False) + '\n').encode()
    directory = _directory(create=True)
    try:
        try:
            fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                         0o400, dir_fd=directory)
        except FileExistsError as error:
            raise Refusal('record_already_exists:' + name) from error
        try:
            view = memoryview(payload)
            while view:
                count = os.write(fd, view)
                if count <= 0:
                    raise OSError('short authority write')
                view = view[count:]
            os.fsync(fd)
        finally:
            os.close(fd)
            # Also retain the created directory entry on a failed partial write.
            os.fsync(directory)
    finally:
        os.close(directory)
    return hashlib.sha256(payload).hexdigest()


def _read(name: str) -> tuple[bytes, dict]:
    directory = _directory()
    try:
        fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
    finally:
        os.close(directory)
    with os.fdopen(fd, 'rb') as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_size > 65536:
            raise Refusal('invalid_authority_file')
        data = stream.read(65537)
        after = os.fstat(stream.fileno())
    if (len(data) != before.st_size or
            (before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns) !=
            (after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)):
        raise Refusal('authority_changed_during_read')

    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise Refusal('duplicate_authority_field')
            value[key] = item
        return value

    try:
        record = json.loads(data, object_pairs_hook=unique,
                            parse_constant=lambda value: (_ for _ in ()).throw(Refusal('nonfinite_authority')))
    except (ValueError, UnicodeError) as error:
        raise Refusal('partial_or_invalid_authority') from error
    if not isinstance(record, dict):
        raise Refusal('invalid_authority_record')
    return data, record


def consume(*, manifest_sha256: str, source_commit: str,
            authorization_sha256: str, host_gpu_uuid: str) -> dict:
    """Spend this one allowance before ANY VFIO/GPU operation, never refund.

    Root must first confirm the canonical remote has no consumed record. The
    authorization hash names the retained current owner direction, including
    the eight-second cold-phase amendment; it does not mint a second allowance.
    The physical timer starts later, immediately before first device contact,
    after consume publication has completed. Git network latency is not GPU work.
    """
    record = {
        'schema': 'dulche.v100-consumed/v1', 'proof_id': PROOF_ID, 'consumed': True,
        'repository': REPOSITORY, 'remote_ref': REMOTE_REF,
        'canonical_consumed_path': CONSUMED_RELATIVE_PATH,
        'manifest_sha256': _hex(manifest_sha256, 64, 'manifest_sha256'),
        'source_commit': _hex(source_commit, 40, 'source_commit'),
        'authorization_sha256': _hex(authorization_sha256, 64, 'authorization_sha256'),
        'host_gpu_uuid': _uuid(host_gpu_uuid), 'model_sha256': MODEL_SHA256,
        'guest_pci_id': '10de:1db4', 'guest_subsystem_id': '10de:1214',
        'guest_gpu_uuid': None, 'policy': dict(POLICY),
        'consumed_at': dt.datetime.now(dt.timezone.utc).isoformat(),
        'existing_global_allowance_reused': True,
    }
    digest = _write_once('CONSUMED.json', record)
    return {'path': str(_AUTHORITY_DIRECTORY / 'CONSUMED.json'), 'sha256': digest, 'record': record}


def read_consumed() -> tuple[bytes, dict]:
    data, record = _read('CONSUMED.json')
    for key, expected in (
        ('schema', 'dulche.v100-consumed/v1'), ('proof_id', PROOF_ID),
        ('repository', REPOSITORY), ('remote_ref', REMOTE_REF),
        ('canonical_consumed_path', CONSUMED_RELATIVE_PATH), ('model_sha256', MODEL_SHA256),
        ('guest_pci_id', '10de:1db4'), ('guest_subsystem_id', '10de:1214'),
        ('policy', POLICY),
    ):
        if record.get(key) != expected:
            raise Refusal('consumed_record_mismatch:' + key)
    if record.get('consumed') is not True or record.get('existing_global_allowance_reused') is not True:
        raise Refusal('consumed_record_not_spent')
    for key in ('manifest_sha256', 'authorization_sha256'):
        _hex(record.get(key), 64, key)
    _hex(record.get('source_commit'), 40, 'source_commit')
    _uuid(record.get('host_gpu_uuid'))
    if record.get('guest_gpu_uuid') is not None:
        raise Refusal('consumed_receipt_must_not_be_rewritten_for_discovered_uuid')
    return data, record


def record_publication(*, consumed_commit_sha: str, observed_main_sha: str,
                       remote_consumed_bytes: bytes) -> dict:
    """Retain root's authenticated canonical-main readback; performs no Git I/O.

    Caller must obtain these bytes from CroakyJake12/9to1 at the immutable commit
    and independently observe that commit as current main after push. Supplying
    invented local values is not publication. Readback mismatch refuses dispatch.
    """
    commit = _hex(consumed_commit_sha, 40, 'published_commit')
    if observed_main_sha != commit:
        raise Refusal('published_commit_not_observed_canonical_main')
    data, record = read_consumed()
    if type(remote_consumed_bytes) is not bytes or remote_consumed_bytes != data:
        raise Refusal('published_consume_bytes_mismatch')
    receipt = {
        'schema': 'dulche.v100-publication/v1', 'proof_id': PROOF_ID,
        'repository': REPOSITORY, 'remote_ref': REMOTE_REF,
        'consumed_commit_sha': commit, 'observed_main_sha': commit,
        'consumed_sha256': hashlib.sha256(data).hexdigest(),
        'manifest_sha256': record['manifest_sha256'],
        'canonical_consumed_path': CONSUMED_RELATIVE_PATH,
        'verified_at': dt.datetime.now(dt.timezone.utc).isoformat(),
    }
    digest = _write_once('PUBLICATION.json', receipt)
    return {'path': str(_AUTHORITY_DIRECTORY / 'PUBLICATION.json'), 'sha256': digest, 'record': receipt}


def process_identity(pid: int | None = None) -> dict:
    """Read-only kernel identity; PID reuse or another boot cannot reuse a claim."""
    pid = os.getpid() if pid is None else pid
    if type(pid) is not int or pid <= 1:
        raise Refusal('invalid_controller_pid')
    directory = Path('/proc') / str(pid)
    if directory.stat().st_uid != os.getuid():
        raise Refusal('controller_owned_by_another_user')
    value = (directory / 'stat').read_text()
    start_ticks = int(value.rsplit(')', 1)[1].split()[19])
    boot_id = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    if not re.fullmatch('[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}', boot_id):
        raise Refusal('invalid_boot_id')
    return {'controller_pid': pid, 'controller_start_ticks': start_ticks, 'boot_id': boot_id}


def claim_dispatch(*, observed_main_sha: str, remote_consumed_bytes: bytes,
                   remote_dispatch_absent: bool, owner_session: str,
                   owner_boot_id: str, controller_pid: int,
                   controller_start_ticks: int) -> dict:
    """Prepare one claim, which MUST itself be CAS-published before execution.

    Root freshly checks canonical remote DISPATCH-CLAIMED is absent. A normal
    non-force push from the observed main arbitrates concurrent checkouts; a
    losing push refuses execution. Copying local files is never that arbitration.
    """
    if remote_dispatch_absent is not True:
        raise Refusal('canonical_dispatch_already_exists_or_not_checked')
    if type(owner_session) is not str or not re.fullmatch('[0-9a-f]{32}', owner_session):
        raise Refusal('invalid_owner_session')
    owner = {'controller_pid': controller_pid, 'controller_start_ticks': controller_start_ticks,
             'boot_id': owner_boot_id}
    if owner != process_identity(controller_pid):
        raise Refusal('controller_identity_changed')
    owner['session'] = owner_session
    data, consumed = read_consumed()
    _, published = _read('PUBLICATION.json')
    expected = {
        'schema': 'dulche.v100-publication/v1', 'proof_id': PROOF_ID,
        'repository': REPOSITORY, 'remote_ref': REMOTE_REF,
        'consumed_sha256': hashlib.sha256(data).hexdigest(),
        'manifest_sha256': consumed['manifest_sha256'],
        'canonical_consumed_path': CONSUMED_RELATIVE_PATH,
    }
    if any(published.get(key) != value for key, value in expected.items()):
        raise Refusal('publication_receipt_mismatch')
    commit = _hex(published.get('consumed_commit_sha'), 40, 'published_commit')
    if published.get('observed_main_sha') != commit or observed_main_sha != commit:
        raise Refusal('canonical_main_changed_or_unverified')
    if type(remote_consumed_bytes) is not bytes or remote_consumed_bytes != data:
        raise Refusal('fresh_remote_consume_bytes_mismatch')
    receipt = {
        'schema': 'dulche.v100-dispatch-claimed/v1', 'proof_id': PROOF_ID,
        'consumed_sha256': expected['consumed_sha256'], 'manifest_sha256': consumed['manifest_sha256'],
        'consumed_commit_sha': commit, 'claimed_at': dt.datetime.now(dt.timezone.utc).isoformat(),
        'no_retry': True, 'owner': owner,
    }
    digest = _write_once('DISPATCH-CLAIMED.json', receipt)
    return {'path': str(_AUTHORITY_DIRECTORY / 'DISPATCH-CLAIMED.json'), 'sha256': digest, 'record': receipt}


def _dispatch_record() -> tuple[bytes, dict, bytes, dict]:
    consumed_bytes, consumed = read_consumed()
    dispatch_bytes, dispatch = _read('DISPATCH-CLAIMED.json')
    if (dispatch.get('schema') != 'dulche.v100-dispatch-claimed/v1'
            or dispatch.get('proof_id') != PROOF_ID or dispatch.get('no_retry') is not True
            or dispatch.get('consumed_sha256') != hashlib.sha256(consumed_bytes).hexdigest()
            or dispatch.get('manifest_sha256') != consumed['manifest_sha256']):
        raise Refusal('dispatch_record_mismatch')
    owner = dispatch.get('owner')
    if (not isinstance(owner, dict)
            or set(owner) != {'controller_pid', 'controller_start_ticks', 'boot_id', 'session'}
            or type(owner['session']) is not str or not re.fullmatch('[0-9a-f]{32}', owner['session'])):
        raise Refusal('dispatch_owner_invalid')
    return consumed_bytes, consumed, dispatch_bytes, dispatch


def record_dispatch_publication(*, dispatch_commit_sha: str, observed_main_sha: str,
                                remote_consumed_bytes: bytes, remote_dispatch_bytes: bytes) -> dict:
    """Record authenticated readback after normal CAS push of the owned claim."""
    commit = _hex(dispatch_commit_sha, 40, 'dispatch_commit')
    if observed_main_sha != commit:
        raise Refusal('dispatch_commit_not_observed_canonical_main')
    consumed_bytes, consumed, dispatch_bytes, dispatch = _dispatch_record()
    if (type(remote_consumed_bytes) is not bytes or remote_consumed_bytes != consumed_bytes
            or type(remote_dispatch_bytes) is not bytes or remote_dispatch_bytes != dispatch_bytes):
        raise Refusal('published_dispatch_bytes_mismatch')
    record = {
        'schema': 'dulche.v100-dispatch-publication/v1', 'proof_id': PROOF_ID,
        'repository': REPOSITORY, 'remote_ref': REMOTE_REF,
        'consumed_sha256': hashlib.sha256(consumed_bytes).hexdigest(),
        'dispatch_sha256': hashlib.sha256(dispatch_bytes).hexdigest(),
        'manifest_sha256': consumed['manifest_sha256'],
        'consumed_commit_sha': dispatch['consumed_commit_sha'],
        'dispatch_commit_sha': commit, 'observed_main_sha': commit,
        'owner': dispatch['owner'], 'verified_at': dt.datetime.now(dt.timezone.utc).isoformat(),
    }
    digest = _write_once('DISPATCH-PUBLICATION.json', record)
    return {'path': str(_AUTHORITY_DIRECTORY / 'DISPATCH-PUBLICATION.json'), 'sha256': digest, 'record': record}


def authorize_local_execution(*, owner_session: str, observed_main_sha: str,
                              remote_consumed_bytes: bytes, remote_dispatch_bytes: bytes) -> dict:
    """One final barrier in the exact claimed process, after fresh remote readback.

    This method must run inside the controller whose kernel identity was placed
    in the globally published claim. Downloading a claim into another checkout,
    process or OS cannot authorize that process. Root's privileged helper also
    verifies its real parent against the same owner tuple before binding.
    """
    consumed_bytes, consumed, dispatch_bytes, dispatch = _dispatch_record()
    _, publication = _read('DISPATCH-PUBLICATION.json')
    owner = {**process_identity(), 'session': owner_session}
    if dispatch['owner'] != owner:
        raise Refusal('execution_process_is_not_claimed_owner')
    expected = {
        'schema': 'dulche.v100-dispatch-publication/v1', 'proof_id': PROOF_ID,
        'repository': REPOSITORY, 'remote_ref': REMOTE_REF,
        'consumed_sha256': hashlib.sha256(consumed_bytes).hexdigest(),
        'dispatch_sha256': hashlib.sha256(dispatch_bytes).hexdigest(),
        'manifest_sha256': consumed['manifest_sha256'], 'owner': owner,
        'consumed_commit_sha': dispatch['consumed_commit_sha'],
    }
    if any(publication.get(key) != value for key, value in expected.items()):
        raise Refusal('dispatch_publication_mismatch')
    commit = _hex(publication.get('dispatch_commit_sha'), 40, 'dispatch_commit')
    if publication.get('observed_main_sha') != commit or observed_main_sha != commit:
        raise Refusal('dispatch_main_changed_or_unverified')
    if (type(remote_consumed_bytes) is not bytes or remote_consumed_bytes != consumed_bytes
            or type(remote_dispatch_bytes) is not bytes or remote_dispatch_bytes != dispatch_bytes):
        raise Refusal('fresh_dispatch_readback_mismatch')
    record = {'schema': 'dulche.v100-execution-started/v1', 'proof_id': PROOF_ID,
              'consumed_sha256': expected['consumed_sha256'],
              'dispatch_sha256': expected['dispatch_sha256'], 'manifest_sha256': consumed['manifest_sha256'],
              'dispatch_commit_sha': commit, 'owner': owner,
              'authorized_at': dt.datetime.now(dt.timezone.utc).isoformat(), 'no_retry': True}
    digest = _write_once('EXECUTION-STARTED.json', record)
    return {'path': str(_AUTHORITY_DIRECTORY / 'EXECUTION-STARTED.json'), 'sha256': digest, 'record': record}


class ColdStartPolicy(SafetyPolicy):
    """Only the approved cold-phase delay changes the inherited strict policy.

    First valid V100 data must arrive within eight seconds of first physical
    contact, before inference. All deadlines use the original monotonic start.
    The first UUID must be separately pinned from observed PCI identity, then
    appended to run evidence rather than rewriting the consumed record.
    """
    def __init__(self, session: str, start_ns: int, host_gpu_uuid: str):
        super().__init__(session, start_ns,
                         {'v100': 'UNRESOLVED_V100_UUID', 'rtx5080': _uuid(host_gpu_uuid)})
        self.guest_identity_pinned = False

    def pin_guest_identity(self, *, uuid: str, pci_id: str, subsystem_id: str) -> None:
        if self.stopped:
            raise Refusal(self.stopped)
        if self.guest_identity_pinned:
            self.refuse('guest_identity_already_pinned')
        if pci_id != '10de:1db4' or subsystem_id != '10de:1214':
            self.refuse('unexpected_guest_pci_identity')
        try:
            identity = _uuid(uuid)
        except Refusal as error:
            self.refuse(str(error))
        if identity == self.identities['rtx5080']:
            self.refuse('guest_identity_matches_host')
        self.identities['v100'] = identity
        self.guest_identity_pinned = True

    def tick(self, now_ns: int) -> None:
        if self.last_sample_ns is not None:
            return super().tick(now_ns)
        if self.stopped:
            raise Refusal(self.stopped)
        if type(now_ns) is not int or now_ns < self.last_tick_ns:
            self.refuse('monotonic_clock_invalid')
        self.last_tick_ns = now_ns
        if now_ns - self.start_ns >= WORK_NS:
            self.refuse('work_deadline')
        if now_ns - self.start_ns > COLD_NS:
            self.refuse('cold_start_deadline')

    def accept(self, sample: object, now_ns: int) -> None:
        if not self.guest_identity_pinned:
            self.refuse('guest_identity_unpinned')
        super().accept(sample, now_ns)

    def inference_permitted(self, now_ns: int) -> bool:
        self.tick(now_ns)
        return self.guest_identity_pinned and self.initial_v100 is not None

    def teardown_remaining_ns(self, now_ns: int) -> int:
        if type(now_ns) is not int or now_ns < self.last_tick_ns:
            raise Refusal('monotonic_clock_invalid')
        return max(0, self.start_ns + TOTAL_NS - now_ns)
