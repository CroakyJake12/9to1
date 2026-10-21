"""Acceptance-only fresh XDG contexts and custody of our own new Linux process session.
Never deletes profiles or XDG paths. Never reads another process's stdio descriptors.
"""
from pathlib import Path
import os, subprocess, tempfile, time
_CONTEXTS = {}
_ROOTS = {}

def context(profile):
    key = str(Path(profile).resolve())
    if key not in _CONTEXTS:
        root = Path(tempfile.mkdtemp(prefix='h9-', dir='/tmp'))
        root.chmod(0o700)
        paths = {}
        for kind in ('DATA', 'CONFIG', 'CACHE', 'RUNTIME'):
            path = root / kind.lower()
            path.mkdir(mode=0o700)
            paths['XDG_' + kind + '_HOME' if kind != 'RUNTIME' else 'XDG_RUNTIME_DIR'] = str(path)
        assert len((paths['XDG_RUNTIME_DIR'] + '/haven/llamacpp-worker.sock').encode()) <= 100
        _CONTEXTS[key] = paths
    env = dict(os.environ, **_CONTEXTS[key], HAVEN_DATA_DIR=str(profile), NO_AT_BRIDGE='0')
    env.pop('GH_TOKEN', None)
    env.pop('GITHUB_TOKEN', None)
    return env

def read_process(pid):
    path = Path('/proc') / str(pid)
    try:
        stat = (path / 'stat').read_text().rsplit(')', 1)[1].split()
        status = (path / 'status').read_text().splitlines()
        uid = [int(v) for line in status if line.startswith('Uid:') for v in line.split()[1:]]
        gid = [int(v) for line in status if line.startswith('Gid:') for v in line.split()[1:]]
        argv = (path / 'cmdline').read_bytes()[:131072].split(b'\0')
        return dict(pid=pid, state=stat[0], ppid=int(stat[1]), pgid=int(stat[2]),
                    sid=int(stat[3]), start_ticks=int(stat[19]), uid=uid, gid=gid,
                    argv=[v.decode('utf-8', 'replace') for v in argv if v])
    except (OSError, ValueError, IndexError):
        return None

def start(profile, log):
    process = subprocess.Popen(['/usr/bin/9to1-preview'], env=context(profile),
                               stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    info = read_process(process.pid)
    if not info or info['pgid'] != process.pid or info['sid'] != process.pid:
        process.terminate()
        process.wait(timeout=10)
        raise RuntimeError('New App session identity unavailable; no group cleanup authorized')
    _ROOTS[process.pid] = info['start_ticks']
    return process

def members(process):
    expected = _ROOTS[process.pid]
    root = read_process(process.pid)
    if root and root['start_ticks'] != expected:
        raise RuntimeError('App PID was reused; refusing cleanup')
    found = []
    for path in Path('/proc').iterdir():
        if not path.name.isdecimal():
            continue
        info = read_process(int(path.name))
        if info and (info['pgid'] == process.pid or info['sid'] == process.pid):
            if info['pgid'] != process.pid or info['sid'] != process.pid or info['uid'][1] != os.geteuid():
                raise RuntimeError('Unexpected process group/session/uid; refusing cleanup')
            if info['state'] != 'Z':
                found.append(info)
    return found

def snapshot(process):
    return dict(root_pid=process.pid, root_start_ticks=_ROOTS[process.pid],
                observed_at_monotonic=time.monotonic(), members=members(process))

def normal_exit(process):
    if members(process):
        raise RuntimeError('App exit left live owned session members; normal close FAIL')

def cleanup(process):
    import signal
    before = snapshot(process)
    if before['members']:
        os.killpg(process.pid, signal.SIGTERM)
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline and members(process):
            process.poll()
            time.sleep(.1)
        if members(process):
            os.killpg(process.pid, signal.SIGKILL)
    if process.poll() is None:
        process.wait(timeout=10)
    deadline = time.monotonic() + 3
    while members(process) and time.monotonic() < deadline:
        time.sleep(.1)
    after = snapshot(process)
    if after['members']:
        raise RuntimeError('Owned session remains live after failure cleanup')
    return dict(before=before, after=after, acceptance='FAILURE CLEANUP ONLY; never normal-close PASS')
