#!/usr/bin/env python3
"""Fixture-only Linux ancestry/subreaper custody. Never signal a numeric PID/group."""
import ctypes
import json
import os
import select
import signal
import sys
import time


def stat(pid):
    try:
        with open(f'/proc/{pid}/stat', encoding='ascii') as stream:
            raw = stream.read()
    except FileNotFoundError:
        return None
    fields = raw[raw.rfind(')') + 2:].split()
    return dict(pid=int(pid), state=fields[0], ppid=int(fields[1]), pgid=int(fields[2]),
                sid=int(fields[3]), birth=int(fields[19]))


def identity(row):
    return row['pid'], row['birth'], row['pgid'], row['sid']


def enroll():
    if sys.platform != 'linux' or not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal'):
        raise RuntimeError('Linux pidfd/subreaper authority unavailable')
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(36, 1, 0, 0, 0) != 0:
        raise RuntimeError('Subreaper enrollment unavailable')
    value = ctypes.c_int()
    if libc.prctl(37, ctypes.byref(value), 0, 0, 0) != 0 or value.value != 1:
        raise RuntimeError('Subreaper enrollment unverified')
    fd = os.pidfd_open(os.getpid())
    try:
        signal.pidfd_send_signal(fd, 0)
    finally:
        os.close(fd)


class Custody:
    def __init__(self):
        enroll()
        self.owner = stat(os.getpid())
        self.records = {}
        self.reaped = []
        self.signals = []

    def owner_current(self):
        current = stat(self.owner['pid'])
        if current is None or identity(current) != identity(self.owner):
            raise RuntimeError('Custodian creator identity lost; no signals authorized')

    def observe(self):
        self.owner_current()
        rows = {}
        for item in os.listdir('/proc'):
            if item.isdigit():
                row = stat(int(item))
                if row is not None:
                    rows[row['pid']] = row
        # Actual genealogy, including escaped-session descendants adopted by this subreaper.
        owned = {self.owner['pid']}
        while True:
            additions = {pid for pid, row in rows.items() if row['ppid'] in owned}
            if additions <= owned:
                break
            owned |= additions
        for pid in owned - {self.owner['pid']}:
            row = rows[pid]
            old = self.records.get(pid)
            if old:
                if identity(row) != identity(old['row']):
                    raise RuntimeError('Observed descendant identity changed; no further signals authorized')
                continue
            fd = os.pidfd_open(pid)
            after = stat(pid)
            if after is None or identity(after) != identity(row) or after['ppid'] != row['ppid']:
                os.close(fd)
                raise RuntimeError('Descendant changed while binding pidfd; fixture held')
            # fdinfo binds the kernel handle to the observed process before admitting authority.
            with open(f'/proc/self/fdinfo/{fd}', encoding='ascii') as stream:
                info = stream.read().splitlines()
            bound = [line.split(':', 1)[1].strip() for line in info if line.startswith('Pid:')]
            if bound != [str(pid)]:
                os.close(fd)
                raise RuntimeError('pidfd binding mismatch; fixture held')
            self.records[pid] = {'row': after, 'fd': fd, 'reaped': False}
        return rows, owned

    def signal_owned(self, sig):
        rows, owned = self.observe()
        for pid, record in self.records.items():
            if record['reaped']:
                continue
            row = rows.get(pid)
            if row is None:
                # A disappeared descendant is exited; pidfd cannot target a reused numeric PID.
                continue
            if identity(row) != identity(record['row']) or pid not in owned:
                raise RuntimeError('Original descendant ownership/birth lost; fixture held')
            if row['state'] in ('Z', 'X'):
                continue  # No signals to exited processes; still require kernel reaping below.
            # Recheck immediately; numeric lookup is only validation. Signal uses the stable kernel fd.
            after = stat(pid)
            if after is None:
                continue
            if identity(after) != identity(record['row']) or after['ppid'] != row['ppid']:
                raise RuntimeError('Pre-signal identity/creator changed; fixture held')
            try:
                signal.pidfd_send_signal(record['fd'], sig)
                self.signals.append({'pid': pid, 'birth': row['birth'], 'signal': int(sig)})
            except ProcessLookupError:
                pass

    def reap(self):
        self.observe()
        for pid, record in self.records.items():
            if record['reaped']:
                continue
            try:
                result = os.waitid(os.P_PIDFD, record['fd'], os.WEXITED | os.WNOHANG)
            except ChildProcessError:
                continue  # Live original parent still owns this descendant; subreaper adopts after exit.
            if result is not None:
                record['reaped'] = True
                self.reaped.append({'pid': pid, 'birth': record['row']['birth'], 'code': result.si_code, 'status': result.si_status})
        try:
            pending = os.waitid(os.P_ALL, 0, os.WEXITED | os.WNOHANG | os.WNOWAIT)
        except ChildProcessError:
            return True  # Kernel ECHILD: no creator-owned/adopted descendants remain.
        return False

    def drain(self, timeout=8):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            self.signal_owned(signal.SIGTERM if deadline - time.monotonic() > timeout / 2 else signal.SIGKILL)
            if self.reap():
                # Kernel-owned descendants reaped; require original session/birth records also gone.
                for record in self.records.values():
                    current = stat(record['row']['pid'])
                    if current is not None and identity(current) == identity(record['row']):
                        raise RuntimeError('Original process still present after ECHILD; fixture held')
                return {'strictReaped': True, 'originalsDisappeared': True, 'reaped': self.reaped, 'signals': self.signals}
            time.sleep(.05)
        raise RuntimeError('Owned descendant drain timeout; fixture state retained')


def main():
    if sys.argv[1:] == ['--check']:
        enroll()
        print('Linux pidfd/subreaper available')
        return
    if len(sys.argv) < 2:
        raise RuntimeError('Missing controlled child command')
    custody = Custody()
    stopping = False
    def stop(_sig=None, _frame=None):
        nonlocal stopping
        stopping = True
    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)
    # posix_spawn has no Popen destructor/poll path that could wait on a reused numeric PID.
    child_pid = os.posix_spawn(sys.argv[1], sys.argv[1:], os.environ, setsid=True,
                               file_actions=[(os.POSIX_SPAWN_OPEN, 0, '/dev/null', os.O_RDONLY, 0)])
    custody.observe()
    if child_pid not in custody.records:
        raise RuntimeError('Original creator child not captured; fixture held')
    while not stopping:
        custody.observe()
        if select.select([sys.stdin], [], [], .05)[0]:
            command = sys.stdin.readline()
            if command in ('stop\n', ''):
                stopping = True
            else:
                raise RuntimeError('Unknown custody command; fixture held')
    receipt = custody.drain()
    os.write(3, (json.dumps(receipt) + '\n').encode())


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(f'Fixture custody HELD: {type(error).__name__}: {error}', file=sys.stderr)
        sys.exit(1)
