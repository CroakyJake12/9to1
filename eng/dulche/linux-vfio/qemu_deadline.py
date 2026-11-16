"""Independent systemd-timer target: stop only one pinned owned QEMU process.

No GPU access, PCI operation, reboot or name-based process killing. The timer
must use AccuracySec=1ms, RandomizedDelaySec=0 and a fresh unique unit name.
"""
import argparse
import json
import os
from pathlib import Path
import re
import select
import signal
import subprocess
import time


class Refusal(RuntimeError):
    pass


def start_ticks(pid):
    return Path(f'/proc/{pid}/stat').read_text().rsplit(') ', 1)[1].split()[19]


def pin_identity(unit, invocation, pid, start, boot):
    if (not re.fullmatch(r'dulche-v100-proof-[a-z0-9-]+\.service', unit)
            or not re.fullmatch(r'[0-9a-f]{32}', invocation) or pid <= 1
            or not str(start).isdecimal()
            or Path('/proc/sys/kernel/random/boot_id').read_text().strip() != boot):
        raise Refusal('Invalid owned guest identity')
    try:
        fd = os.pidfd_open(pid)
    except ProcessLookupError:
        return None
    try:
        proc = Path(f'/proc/{pid}')
        if (proc.stat().st_uid != os.getuid() or start_ticks(pid) != str(start)
                or (proc / 'exe').resolve(strict=True) != Path('/usr/bin/qemu-system-x86_64')):
            raise Refusal('QEMU process identity changed')
        # Avoid a dependency on D-Bus responsiveness at the emergency deadline.
        environment = (proc / 'environ').read_bytes().split(b'\0')
        if b'INVOCATION_ID=' + invocation.encode() not in environment:
            raise Refusal('QEMU unit invocation changed')
        groups = (proc / 'cgroup').read_text().splitlines()
        if not any(line.startswith('0::') and line.endswith('/' + unit) for line in groups):
            raise Refusal('QEMU containment changed')
        argv = (proc / 'cmdline').read_bytes().split(b'\0')
        if b'-name' not in argv or argv[argv.index(b'-name') + 1] != unit[:-8].encode():
            raise Refusal('QEMU session changed')
        return fd
    except BaseException:
        os.close(fd)
        raise


def stop_pinned(fd):
    if fd is None or select.select([fd], [], [], 0)[0]:
        return True
    try:
        signal.pidfd_send_signal(fd, signal.SIGTERM)
        if select.select([fd], [], [], .2)[0]:
            return True
        signal.pidfd_send_signal(fd, signal.SIGKILL)
        return bool(select.select([fd], [], [], .5)[0])
    except ProcessLookupError:
        return bool(select.select([fd], [], [], 0)[0])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unit', required=True)
    parser.add_argument('--invocation', required=True)
    parser.add_argument('--pid', required=True, type=int)
    parser.add_argument('--start', required=True)
    parser.add_argument('--boot', required=True)
    args = parser.parse_args()
    before = time.monotonic_ns()
    fd = None
    try:
        fd = pin_identity(args.unit, args.invocation, args.pid, args.start, args.boot)
        stopped = stop_pinned(fd)
        print(json.dumps({'event': 'independent_qemu_deadline', 'unit': args.unit,
                          'invocation': args.invocation, 'qemu_stopped': stopped,
                          'elapsed_ns': time.monotonic_ns() - before}), flush=True)
        return 0 if stopped else 2
    except (OSError, Refusal, ValueError, subprocess.SubprocessError) as error:
        print(json.dumps({'event': 'independent_qemu_deadline_refused',
                          'reason': str(error)}), flush=True)
        return 2
    finally:
        if fd is not None:
            os.close(fd)


if __name__ == '__main__':
    raise SystemExit(main())
