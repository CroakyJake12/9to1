#!/usr/bin/env python3
"""Create an inert Linux GPU integration review plan. Never launches anything.

Input is retained read-only inspection evidence, not live admission authority.
Even a complete-looking input cannot enable execution in this preparation tool.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import tempfile

HERE = Path(__file__).resolve().parent
GIB = 1024 ** 3
MODEL_BYTES = 668788096
MODEL_SHA256 = '9fecc3b3cd76bba89d504f29b616eedf7da85b96540e490ca5824d3f7d2776a0'
COMMIT = 'b1b770d90d94cad9e9ea5b993bdbc383a522fb28'
BDF = re.compile(r'^[0-9a-f]{4}:[0-9a-f]{2}:[0-9a-f]{2}\.[0-7]$')


class Refusal(ValueError):
    pass


def read_regular_bytes(path: Path, maximum: int = 4 * 1024 * 1024) -> bytes:
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_size > maximum:
            raise Refusal('Evidence must be a bounded regular file')
        data = stream.read(maximum + 1)
        after = os.fstat(stream.fileno())
        if len(data) > maximum or before.st_size != len(data) or (
                before.st_mtime_ns, before.st_ctime_ns, before.st_size) != (
                after.st_mtime_ns, after.st_ctime_ns, after.st_size):
            raise Refusal('Evidence changed while reading')
    return data


def parse_json(data: bytes) -> dict:
    """Bounded ordinary JSON only; no command evaluation or template expansion."""
    def reject_constant(value: str) -> None:
        raise Refusal('Non-finite JSON number: ' + value)

    def unique_object(pairs: list) -> dict:
        result = {}
        for key, value in pairs:
            if key in result:
                raise Refusal('Duplicate JSON field: ' + key)
            result[key] = value
        return result

    try:
        value = json.loads(data, parse_constant=reject_constant, object_pairs_hook=unique_object)
    except (ValueError, UnicodeError) as error:
        raise Refusal(f'Invalid JSON: {error}') from error
    if not isinstance(value, dict):
        raise Refusal('JSON input must be an object')
    return value


def json_file(path: Path) -> dict:
    return parse_json(read_regular_bytes(path))


def hash_regular_file(path: Path, expected_size: int) -> str:
    """No symlink leaf, no special file, bounded hash; refuse concurrent changes."""
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_size != expected_size:
            raise Refusal('Model must be a regular file of the exact retained size')
        digest = hashlib.sha256()
        remaining = expected_size
        while remaining:
            chunk = stream.read(min(1024 * 1024, remaining))
            if not chunk:
                raise Refusal('Model changed or became truncated while hashing')
            digest.update(chunk)
            remaining -= len(chunk)
        after = os.fstat(stream.fileno())
        if ((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns)
                != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)):
            raise Refusal('Model changed while hashing')
        return digest.hexdigest()


def model_evidence(model: Path | None, receipt: Path | None) -> dict:
    result = {'expected_bytes': MODEL_BYTES, 'expected_sha256': MODEL_SHA256,
              'authority': 'docs/development/dulche-linux-handoff-2026-11-16.md',
              'verified': False, 'status': 'MODEL_AND_ORIGINAL_RECEIPT_NOT_AVAILABLE'}
    if model is None and receipt is None:
        return result
    if model is None or receipt is None:
        raise Refusal('Supply both model and download receipt, or neither')
    # Receipt normalization is deliberate: import/review the original evidence,
    # retain its hash separately, and never silently guess a receipt field.
    receipt_bytes = read_regular_bytes(receipt)
    data = parse_json(receipt_bytes)
    if data.get('bytes') != MODEL_BYTES or data.get('sha256') != MODEL_SHA256:
        raise Refusal('Receipt differs from the corrected authoritative model identity')
    if not isinstance(data.get('source_receipt_sha256'), str) or not re.fullmatch(
            r'[0-9a-f]{64}', data['source_receipt_sha256']):
        raise Refusal('Normalized receipt must pin the reviewed original receipt hash')
    original = data.get('source_receipt_path')
    if not isinstance(original, str) or not Path(original).is_absolute():
        raise Refusal('Normalized receipt must identify an absolute original receipt path')
    original_bytes = read_regular_bytes(Path(original))
    if hashlib.sha256(original_bytes).hexdigest() != data['source_receipt_sha256']:
        raise Refusal('Original receipt bytes differ from the normalized evidence pin')
    digest = hash_regular_file(model, MODEL_BYTES)
    if digest != MODEL_SHA256:
        raise Refusal('Actual model hash differs from the authoritative identity')
    result.update(verified=True, status='BYTES_MATCH_RETAINED_IDENTITY',
                  path=str(model.absolute()), receipt_path=str(receipt.absolute()),
                  receipt_sha256=hashlib.sha256(receipt_bytes).hexdigest(),
                  original_receipt_sha256=data['source_receipt_sha256'],
                  original_receipt_review='Human/content review still required; hash matching is not authorization.')
    return result


def device(data: dict, pci_id: str) -> dict:
    devices = data.get('devices')
    if not isinstance(devices, list) or any(not isinstance(item, dict) for item in devices):
        raise Refusal('Inspection devices must be an array of objects')
    matches = [item for item in devices if item.get('vendor') == '0x10de'
               and item.get('device') == pci_id]
    if len(matches) != 1:
        raise Refusal(f'Expected exactly one NVIDIA {pci_id}, found {len(matches)}')
    value = matches[0]
    if not isinstance(value.get('bdf'), str) or not BDF.fullmatch(value['bdf']):
        raise Refusal('Malformed inspected PCI address')
    if not isinstance(value.get('upstream_bridge'), str) or not BDF.fullmatch(value['upstream_bridge']):
        raise Refusal('Missing or malformed upstream bridge address')
    for key in ('group_members', 'reset_domain_members'):
        members = value.get(key)
        if not isinstance(members, list) or not members or any(
                not isinstance(member, str) or not BDF.fullmatch(member) for member in members):
            raise Refusal(f'Missing or malformed {key}')
    return value


def make_plan(data: dict, model: dict, now: dt.datetime | None = None) -> dict:
    if data.get('schema') != '9to1.dulche.linux-readonly/v1':
        raise Refusal('Unsupported inspection schema')
    if data.get('scope') != 'READ_ONLY_NO_GPU_INITIALIZATION':
        raise Refusal('Inspection does not declare the required read-only scope')
    host, iommu = data.get('host'), data.get('iommu')
    if not isinstance(host, dict) or not isinstance(iommu, dict):
        raise Refusal('Missing host or IOMMU observations')
    v100, rtx = device(data, '0x1db4'), device(data, '0x2c02')
    now = now or dt.datetime.now(dt.timezone.utc)
    try:
        observed = dt.datetime.fromisoformat(data['observed_at'])
        if observed.tzinfo is None:
            raise ValueError('timezone missing')
        age = (now - observed).total_seconds()
    except (ValueError, KeyError, TypeError) as error:
        raise Refusal('Inspection timestamp must be timezone-aware ISO 8601') from error
    blockers = []

    def require(ok: bool, code: str) -> None:
        if not ok:
            blockers.append(code)

    require(0 <= age <= 300, 'INSPECTION_NOT_CURRENT')
    require(v100.get('subsystem_vendor') == '0x10de'
            and v100.get('subsystem_device') == '0x1214', 'V100_SUBSYSTEM_MISMATCH')
    require(v100.get('driver') is None and v100.get('binding_state') == 'unbound'
            and v100.get('inspection_errors') == [], 'V100_UNBOUND_STATE_UNCONFIRMED')
    require(v100.get('driver_override') in ('', '(null)'), 'V100_OVERRIDE_NOT_EMPTY')
    require(isinstance(v100.get('iommu_group'), str)
            and v100['iommu_group'].isdigit(), 'V100_IOMMU_GROUP_MISSING')
    require(v100['group_members'] == [v100['bdf']], 'V100_GROUP_NOT_EXCLUSIVE')
    require(v100['reset_domain_members'] == [v100['bdf']], 'RESET_DOMAIN_HAS_OTHER_DEVICES')
    regions = v100.get('reserved_regions')
    valid_regions = isinstance(regions, str) and bool(regions.strip())
    if valid_regions:
        for line in regions.splitlines():
            match = re.fullmatch(r'(0x[0-9a-fA-F]+)\s+(0x[0-9a-fA-F]+)\s+msi', line.strip())
            if match is None or not 0 <= int(match[1], 16) <= int(match[2], 16) <= 2 ** 64 - 1:
                valid_regions = False
                break
    require(valid_regions, 'RESERVED_REGIONS_REQUIRE_REVIEW')
    require(v100.get('reset_methods') == ['bus'], 'RESET_METHOD_REQUIRES_REVIEW')
    require(v100.get('upstream_bridge') != rtx.get('upstream_bridge'), 'GPU_ROOT_PORT_NOT_SEPARATE')
    require(rtx.get('driver') == 'nvidia' and rtx.get('boot_vga') == '1', 'RTX_HOST_BINDING_CHANGED')
    driver = host.get('nvidia_driver')
    require(isinstance(driver, str) and 'Open Kernel' in driver, 'HOST_OPEN_DRIVER_UNCONFIRMED')
    require(iommu.get('irq_remapping_enabled') is True, 'IRQ_REMAPPING_UNCONFIRMED')
    # Unloaded modules are not proof of safe eventual parameters.
    for key in ('unsafe_noiommu', 'unsafe_interrupts'):
        require(iommu.get(key) in ('N', '0'), key.upper() + '_MUST_BE_VERIFIED_FALSE')
    cmdline = host.get('cmdline')
    if not isinstance(cmdline, str):
        raise Refusal('Kernel command line observation must be text')
    require(not any(flag in cmdline for flag in (
        'pcie_acs_override', 'enable_unsafe_noiommu_mode=1', 'allow_unsafe_interrupts=1',
        'intremap=off', 'iommu=off', 'intel_iommu=off')),
        'UNSAFE_KERNEL_OVERRIDE_PRESENT')
    require(host.get('kvm_accessible') is True, 'KVM_ACCESS_UNCONFIRMED')
    for key, minimum, code in (
            ('mem_available_bytes', 27 * GIB, 'HOST_MEMORY_BUDGET_INSUFFICIENT'),
            ('storage_free_bytes', 32 * GIB, 'STORAGE_RESERVE_INSUFFICIENT')):
        number = host.get(key)
        require(type(number) is int and minimum <= number <= 2 ** 63 - 1, code)
    require(model.get('verified') is True, 'MODEL_AND_ORIGINAL_RECEIPT_NOT_VERIFIED')
    require(not data.get('errors'), 'INSPECTION_HAS_READ_ERRORS')
    blockers.extend([
        'EXPLICIT_SYSTEM_CHANGE_APPROVAL_REQUIRED',
        'ORIGINAL_WINDOWS_AUTHORIZATION_AND_DOWNLOAD_RECEIPT_REVIEW_REQUIRED',
        'GLOBAL_ONE_USE_AUTHORITY_AND_WINDOWS_RECEIPT_NOT_RECONCILED',
        'GUEST_V100_UUID_AND_FRESH_TELEMETRY_NOT_ESTABLISHED',
        'PRE_INITIALIZATION_TEMPERATURE_BASELINE_NOT_ESTABLISHED',
        'INDEPENDENT_ACTUAL_GUEST_STOP_PATH_NOT_PROVEN',
        'PINNED_CUDA_BINARIES_NOT_BUILT_OR_VERIFIED',
        'CURRENT_GUEST_DRIVER_AND_RESET_RECOVERY_NOT_PROVEN',
        'DULCHE_TYPED_RPC_PROFILE_NOT_IMPLEMENTED',
    ])
    return {
        'schema': '9to1.dulche.linux-preparation/v1',
        'generated_at': now.isoformat(), 'inspection_age_seconds': age,
        'status': 'PREPARED_DISABLED', 'execution_enabled': False,
        'allowance_consumed_by_preparation': False,
        'host_identity': {'boot_id': host.get('boot_id'), 'kernel': host.get('kernel'),
                          'nvidia_driver': host.get('nvidia_driver'),
                          'identity_meaning': 'Retained inspection snapshot, not a current launch lease.'},
        'blockers': blockers, 'model': model,
        'devices': {'host_rtx5080': rtx, 'guest_v100_candidate': v100},
        'backend_lock': json_file(HERE / 'backend-lock.json'),
        'guest': json_file(HERE / 'templates/guest-plan.json'),
        'transport': json_file(HERE / 'templates/transport-plan.json'),
        'storage': {
            'owner': 'Existing Dulche modelctl and shared per-model lifetime lease',
            'canonical_root_policy': '$XDG_DATA_HOME/haven/models (default ~/.local/share/haven/models)',
            'environment_rule': 'Use the same explicit XDG_DATA_HOME for modelctl and broker. HAVEN_MODEL_ROOT alone is insufficient because broker does not honor it.',
            'guest_model_filesystem': None, 'guest_rpc_tensor_cache': False,
            'checkpoints': 'Retain existing Training/custody immutable outputs and provenance; no migration or deletion.',
            'training_root_candidate': '$XDG_DATA_HOME/haven/training-artifacts via existing ArtifactStore artifact_root; separate retained runs, not a claimed unified catalogue.',
            'import': 'Later verified import through existing owner; no copying or downloading by this tool.',
        },
        'resources': {'guest_ram_bytes': 4 * GIB, 'qemu_memory_max_bytes': 6 * GIB,
                      'host_worker_memory_max_bytes': 4 * GIB, 'guard_reserve_bytes': GIB,
                      'host_headroom_floor_bytes': 16 * GIB, 'pids_max': 64,
                      'guest_vcpus': 2, 'inference_threads': 2, 'parallel_requests': 1,
                      'swap_max_bytes': 0, 'disk_reserve_bytes': 20 * GIB,
                      'candidate_disk_budget_bytes': 12 * GIB,
                      'status': 'PROPOSED_NOT_ENFORCED'},
        'safety': {'work_cutoff_seconds': 25, 'total_seconds': 30,
                   'sampling_seconds': 0.25, 'stale_after_seconds': 0.75,
                   'v100_start_max_c': 40, 'v100_stop_c': 55, 'v100_rise_stop_c': 10,
                   'rtx5080_stop_c': 75, 'automatic_retry': False,
                   'clock_start': 'Before first V100 binding/attachment/driver initialization/GPU dispatch, whichever is earliest.',
                   'baseline_gate': 'No uncounted initialization to obtain a baseline. Without independently reliable starting temperature and stopping, refuse proof.',
                   'sustained_v100_work': 'PROHIBITED_PENDING_CONFIRMED_COOLING'},
        'integration': {
            'owner': 'Existing InferenceEngineDispatcher and configured llama.cpp provider',
            'public_facing_transport': 'Existing owned Unix socket, no public listener',
            'required_change': 'Explicit typed RPC candidate profile through existing launch planner; preserve model and training leases.',
            'existing_service': 'AF_UNIX-only restriction needs narrow reviewed candidate-profile change for loopback RPC.',
            'strata': 'No claim of Thunder/Strata RPC interoperability or distributed training.',
        },
    }


def write_fresh(path: Path, value: dict) -> None:
    payload = (json.dumps(value, indent=2, allow_nan=False) + '\n').encode()
    # Publish complete bytes with a same-filesystem, atomic, no-clobber link.
    fd, temporary_name = tempfile.mkstemp(prefix='.dulche-preparation-', dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path)
        directory_fd = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
    finally:
        # This is only our unpublished staging link, never retained evidence.
        temporary.unlink()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inspection', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--model', type=Path)
    parser.add_argument('--download-receipt', type=Path)
    args = parser.parse_args()
    try:
        inspection_bytes = read_regular_bytes(args.inspection)
        plan = make_plan(parse_json(inspection_bytes), model_evidence(args.model, args.download_receipt))
        plan['inspection_sha256'] = hashlib.sha256(inspection_bytes).hexdigest()
        write_fresh(args.output, plan)
    except (OSError, Refusal) as error:
        parser.exit(2, f'Preparation refused: {error}\n')
    print(f'Prepared disabled review plan: {args.output}; {len(plan["blockers"])} unresolved gates.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
