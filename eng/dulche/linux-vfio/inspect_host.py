#!/usr/bin/env python3
"""Read kernel/PCI metadata without opening GPU devices or invoking CUDA/NVML.

Does not bind, reset, load modules, boot guests, mount disks, or query nvidia-smi.
An inspection is evidence at one instant, never permission to attach a device.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import platform
import re
import shutil
import stat
import subprocess

SCHEMA = "9to1.dulche.linux-readonly/v1"


def read(path: Path) -> str | None:
    try:
        return path.read_text().strip()
    except (OSError, UnicodeError):
        return None


def pci_devices(sys_root: Path = Path('/sys')) -> list[dict]:
    devices = sys_root / 'bus/pci/devices'
    result = []
    for path in sorted(devices.glob('*')):
        vendor = read(path / 'vendor')
        if vendor != '0x10de':
            continue
        group = path / 'iommu_group'
        physical = path.resolve()
        parent = physical.parent
        siblings = sorted(p.name for p in devices.glob('*')
                          if parent in p.resolve().parents)
        driver = path / 'driver'
        inspection_errors = []
        driver_name = None
        try:
            if not stat.S_ISLNK(driver.lstat().st_mode):
                raise OSError('driver entry is not a symbolic link')
            driver_name = driver.resolve(strict=True).name
            binding_state = 'bound'
        except FileNotFoundError:
            # A dangling driver symlink is unknown, not evidence of no binding.
            binding_state = 'unknown' if driver.is_symlink() else 'unbound'
        except OSError as error:
            binding_state = 'unknown'
            inspection_errors.append(f'driver binding unreadable: {error}')
        if binding_state == 'unknown' and not inspection_errors:
            inspection_errors.append('driver target unavailable')
        result.append({
            'bdf': path.name,
            'sysfs_path': str(physical),
            **{key: read(path / key) for key in (
                'vendor', 'device', 'subsystem_vendor', 'subsystem_device',
                'class', 'boot_vga', 'current_link_width', 'current_link_speed',
                'max_link_width', 'max_link_speed', 'driver_override')},
            'driver': driver_name,
            'binding_state': binding_state,
            'inspection_errors': inspection_errors,
            'iommu_group': group.resolve().name if group.is_symlink() else None,
            'group_members': sorted(p.name for p in (group / 'devices').glob('*')),
            'reset_methods': (read(path / 'reset_method') or '').split(),
            'upstream_bridge': parent.name,
            'reset_domain_members': siblings,
            'reserved_regions': read(group / 'reserved_regions'),
            'runtime_status': read(path / 'power/runtime_status'),
        })
    return result


def command(args: list[str], errors: list[str]) -> str | None:
    """Only callers below supply fixed, read-only commands; no user command input."""
    try:
        value = subprocess.run(args, capture_output=True, text=True, timeout=10,
                               check=False)
        if value.returncode:
            errors.append(f'{args[0]} returned {value.returncode}: {value.stderr[:200]}')
            return None
        return value.stdout.strip()
    except (OSError, subprocess.TimeoutExpired) as error:
        errors.append(f'{args[0]} unavailable: {error}')
        return None


def observed_processes(proc: Path = Path('/proc')) -> list[dict]:
    result = []
    pattern = re.compile(r'llama|strata|thunder|qemu|libvirt|virtqemu|dotnet|haven|'
                         r'ollama|opencode|codex|v100|steam|fortnite|wine|gamescope|'
                         r'Xorg|gnome-shell', re.I)
    for path in proc.glob('[0-9]*'):
        comm = read(path / 'comm')
        try:
            # argv[0] can be rewritten to contain arguments. Read the executable
            # symlink instead, so command-line secrets cannot enter the evidence.
            executable = os.path.basename(os.readlink(path / 'exe'))
            cwd = os.readlink(path / 'cwd')
        except OSError:
            continue
        if pattern.search(comm or '') or pattern.search(executable) or '/9to1' in cwd:
            result.append({'pid': int(path.name), 'comm': comm,
                           'executable': executable, 'cwd': cwd})
    return sorted(result, key=lambda item: item['pid'])


def collect(storage_path: Path) -> dict:
    errors: list[str] = []
    journal = command(['journalctl', '-b', '-k', '--no-pager', '-o', 'cat'], errors)
    relevant_log = [line for line in (journal or '').splitlines()
                    if re.search(r'DMAR|IOMMU|remapping|vfio|NVRM', line)]
    mem = read(Path('/proc/meminfo')) or ''
    available = re.search(r'^MemAvailable:\s+(\d+) kB$', mem, re.M)
    info = {}
    for path in Path('/proc/driver/nvidia/gpus').glob('*/information'):
        # This reads already-registered driver information, not NVML.
        info[path.parent.name] = read(path)
    secure_boot = command(['mokutil', '--sb-state'], errors)
    unsafe_paths = {
        'unsafe_noiommu': '/sys/module/vfio/parameters/enable_unsafe_noiommu_mode',
        'unsafe_interrupts': '/sys/module/vfio_iommu_type1/parameters/allow_unsafe_interrupts',
    }
    return {
        'schema': SCHEMA,
        'observed_at': dt.datetime.now(dt.timezone.utc).isoformat(),
        'scope': 'READ_ONLY_NO_GPU_INITIALIZATION',
        'host': {
            'kernel': platform.release(),
            'boot_id': read(Path('/proc/sys/kernel/random/boot_id')),
            'os_release': read(Path('/etc/os-release')),
            'cmdline': read(Path('/proc/cmdline')),
            'nvidia_driver': read(Path('/proc/driver/nvidia/version')),
            'registered_gpu_information': info,
            'secure_boot': secure_boot,
            'mem_available_bytes': int(available[1]) * 1024 if available else None,
            'storage_path': str(storage_path.resolve()),
            'storage_free_bytes': shutil.disk_usage(storage_path).free,
            'kvm_accessible': os.access('/dev/kvm', os.R_OK | os.W_OK),
            'dmi': {key: read(Path('/sys/class/dmi/id') / key) for key in (
                'board_name', 'board_version', 'bios_version', 'bios_date')},
        },
        'devices': pci_devices(),
        'iommu': {
            'groups_count': len(list(Path('/sys/kernel/iommu_groups').glob('*'))),
            'irq_remapping_enabled': 'Enabled IRQ remapping' in (journal or ''),
            **{key: read(Path(path)) for key, path in unsafe_paths.items()},
            'absent_parameter_meaning': 'Module not loaded or parameter unavailable; not a safety pass.',
            'kernel_evidence': relevant_log,
        },
        'workloads': {
            'processes': observed_processes(),
            'listeners': command(['ss', '-lnt'], errors),
            'limitation': 'Process snapshot is not an exclusive lease; recheck before any future launch.',
        },
        'mounts': command(['findmnt', '-rn', '-o', 'TARGET,SOURCE,FSTYPE,OPTIONS'], errors),
        'errors': errors,
        'unverified': ['V100 UUID', 'V100 temperature', 'physical reset reliability',
                       'current guest initialization', 'dual-GPU execution'],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path,
                        help='Fresh JSON file; refuses to overwrite existing evidence')
    parser.add_argument('--storage-path', type=Path, default=Path.cwd())
    args = parser.parse_args()
    result = collect(args.storage_path)
    # No parent directory creation, no overwriting existing evidence or symlinks.
    fd = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as stream:
        json.dump(result, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    print(f'Read-only inspection saved: {args.output}')


if __name__ == '__main__':
    main()
