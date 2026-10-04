#!/usr/bin/env python3
"""Own only this freshly issued Node/browser family, including detached groups."""
import ctypes
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import sys
import time

base = Path(__file__).resolve().parent
plan_path = base / 'EXACT-8041-WAVE14-EXECUTION-PLAN-03.json'
plan_bytes = plan_path.read_bytes()
plan = json.loads(plan_bytes)
plan_sha256 = hashlib.sha256(plan_bytes).hexdigest()
owner_pid = os.getpid()
output = Path(plan['output'])
control = Path(plan['control'])
temp = Path(plan['tmpdir'])
cache = Path(plan['cache'])
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
receipt = {'scope': 'Fresh owned public Wave8041 sequential browser family only; no account/legacy fixture custody',
           'state': 'PREPARED', 'deadlineFailure': False, 'budgetFailure': False,
           'identityFailures': [], 'ownedFamily': [], 'forcedCleanup': False}
issued = {}
child = None
root_pid = None
root_key = None
kernel_proof = None

def stat(pid):
    try:
        raw = Path(f'/proc/{pid}/stat').read_text()
        fields = raw[raw.rfind(')') + 2:].split()
        return {'pid': pid, 'ppid': int(fields[1]), 'pgrp': int(fields[2]),
                'session': int(fields[3]), 'birth': int(fields[19]), 'state': fields[0]}
    except (OSError, ValueError, IndexError):
        return None

def own_kernel_child_receipt():
    if kernel_proof is None:
        return {'empty': False, 'status': 'VERIFIED_KERNEL_PROOF_UNAVAILABLE'}
    return kernel_proof.kernel_own_child_receipt()

def exited(fd):
    poll = select.poll()
    poll.register(fd, select.POLLIN)
    return bool(poll.poll(0))

def admit(info, relation):
    pid = info['pid']
    key = (pid, info['birth'])
    if key in issued:
        return
    try:
        fd = os.pidfd_open(pid)
    except ProcessLookupError:
        return
    except OSError:
        receipt['identityFailures'].append('Owned descendant pidfd unavailable')
        return
    after = stat(pid)
    if after is None or after['birth'] != info['birth']:
        os.close(fd)
        return
    record = dict(after, relation=relation, fd=fd, signalCount=0, profileBound=None)
    try:
        record['cgroupSha256'] = sha(f'/proc/{pid}/cgroup')
        record['exeBasename'] = Path(os.readlink(f'/proc/{pid}/exe')).name
        if record['exeBasename'] == 'chromium' and record['ppid'] == root_pid:
            # The pinned one-browser Playwright launcher spawns the top browser
            # directly from the held Node root. Chromium descendants need not
            # retain a separate --type argv field or their own profile argument.
            root_now = stat(root_pid)
            if root_key not in issued or root_now is None or root_now['birth'] != root_key[1] or exited(issued[root_key]['fd']):
                receipt['identityFailures'].append('Top browser launcher parent identity unproven')
            else:
                # This exact runner awaits context.close before each of its
                # two restarts. A new top cannot overlap a still-live held top.
                prior_tops = [row for row in issued.values() if row.get('topBrowserLauncher')]
                if any(not exited(row['fd']) for row in prior_tops):
                    receipt['identityFailures'].append('Sequential Wave top browsers overlap')
                if len(prior_tops) >= 3:
                    receipt['identityFailures'].append('Exact Wave runner issued more than three top browsers')
                record['topBrowserLauncher'] = True
                # Inspect locally; never persist argv or environment values.
                args = Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
                profiles = [arg[len(b'--user-data-dir='):] for arg in args if arg.startswith(b'--user-data-dir=')]
                record['profileBound'] = len(profiles) == 1 and Path(os.fsdecode(profiles[0])).resolve() == (output / 'profile').resolve()
                if not record['profileBound']:
                    receipt['identityFailures'].append('Owned top Chromium profile differs from exact Wave output/profile')
    except OSError:
        # A held pidfd can already be exit-ready; absence is not a live identity.
        if not exited(fd):
            receipt['identityFailures'].append('Live owned process metadata unavailable')
    issued[key] = record

def discover():
    table = {}
    for entry in Path('/proc').iterdir():
        if entry.name.isdigit():
            value = stat(int(entry.name))
            if value is not None:
                table[value['pid']] = value
    admitted = {owner_pid} | {row['pid'] for row in issued.values()
                              if row['pid'] in table and table[row['pid']]['birth'] == row['birth']}
    changed = True
    while changed:
        changed = False
        for pid, info in table.items():
            if pid == owner_pid or pid in admitted or info['ppid'] not in admitted:
                continue
            # Only kernel parentage from this owner/root/held descendants;
            # same cgroup, names, ports and numeric PID guesses never admit work.
            admit(info, 'kernel descendant or subreaper adoption')
            if (pid, info['birth']) in issued:
                admitted.add(pid)
                changed = True

def live():
    return [row for row in issued.values() if not exited(row['fd'])]

def reap_adopted():
    for key, row in issued.items():
        pid = row['pid']
        if key == root_key or not exited(row['fd']):
            continue
        current = stat(pid)
        if current and current['birth'] == row['birth'] and current['ppid'] == owner_pid:
            try:
                os.waitpid(pid, os.WNOHANG)
            except ChildProcessError:
                pass

def footprint():
    total = 0
    for root in (output, control, temp, cache):
        if root.exists():
            for folder, _, files in os.walk(root):
                for name in files:
                    try:
                        total += (Path(folder) / name).stat().st_size
                    except OSError:
                        pass
    return total

def signal_held(kind):
    for row in live():
        # Kernel-held actual identity: no PID/group signaling and no legacy family.
        try:
            signal.pidfd_send_signal(row['fd'], kind)
            row['signalCount'] += 1
        except ProcessLookupError:
            pass
        except OSError:
            receipt['identityFailures'].append('Held owned identity could not be signalled')

def verify_pins():
    if sha(plan_path) != plan_sha256:
        raise RuntimeError('Frozen execution plan changed')
    for row in plan['pins']:
        if sha(row['path']) != row['sha256']:
            raise RuntimeError('Frozen execution input pin mismatch')

def birth_disappeared(row):
    current = stat(row['pid'])
    return current is None or current['birth'] != row['birth']

try:
    if os.environ.get('B3_WAVE_GUI_GRANTED') != 'granted':
        raise RuntimeError('Root GUI gate absent')
    verify_pins()
    self_pid = int(Path('/proc/self/stat').read_text().split(' ', 1)[0])
    receipt['procNamespaceAgreement'] = self_pid == owner_pid
    if not receipt['procNamespaceAgreement']:
        raise RuntimeError('Own process and proc namespace disagree; do not launch')
    # Import only AFTER all frozen source pins have been checked.
    spec = importlib.util.spec_from_file_location('owned_child_proof', base / 'owned-kernel-child-proof01.py')
    kernel_proof = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(kernel_proof)
    receipt['prelaunchKernelOwnChildReceipt'] = own_kernel_child_receipt()
    if not receipt['prelaunchKernelOwnChildReceipt']['empty']:
        raise RuntimeError('Actual kernel own-child absence unproven; do not launch')
    if any(root.exists() for root in (output, control, temp, cache)):
        raise RuntimeError('Fresh output/control/profile/cache paths required')
    if shutil.disk_usage('/workspace').free < 384000000:
        raise RuntimeError('Workspace reserve insufficient')
    if not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal'):
        raise RuntimeError('Kernel identity control unavailable; do not launch')
    test_fd = os.pidfd_open(owner_pid)
    os.close(test_fd)
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(36, 1, 0, 0, 0) != 0:  # PR_SET_CHILD_SUBREAPER, own process only.
        raise RuntimeError('Owned detached-family adoption unavailable; do not launch')
    for root in (control, temp, cache):
        root.mkdir(mode=0o700)
    receipt['owner'] = stat(owner_pid)
    receipt['ownerCgroupSha256'] = sha(f'/proc/{owner_pid}/cgroup')
    receipt['planSha256'] = plan_sha256
    environment = os.environ.copy()
    environment.update(TMPDIR=str(temp), XDG_CACHE_HOME=str(cache))
    started = time.monotonic()
    with (control / 'runner-safe-stdout.log').open('xb') as stream:
        child = subprocess.Popen(plan['argv'], env=environment, stdout=stream,
                                 stderr=subprocess.STDOUT, start_new_session=True)
        root_pid = child.pid
        info = stat(root_pid)
        if info is None:
            receipt['identityFailures'].append('Fresh root birth unproven')
            raise RuntimeError('Fresh root identity unavailable')
        root_key = (root_pid, info['birth'])
        admit(info, 'original Popen direct child')
        if root_key not in issued:
            receipt['identityFailures'].append('Fresh root held identity unproven')
            raise RuntimeError('Fresh root pidfd unavailable; custody HOLD')
        receipt['state'] = 'RUNNING'
        while child.poll() is None:
            discover()
            reap_adopted()
            if receipt['identityFailures']:
                break
            if time.monotonic() - started > plan['maximumSeconds']:
                receipt['deadlineFailure'] = True
                break
            if footprint() > plan['maximumOutputAndTemporaryBytes'] or shutil.disk_usage('/workspace').free < 256000000:
                receipt['budgetFailure'] = True
                break
            time.sleep(0.05)
        receipt['runnerExitCode'] = child.poll()
except Exception as error:
    receipt['setupOrControlFailureType'] = type(error).__name__
finally:
    if child is not None:
        # Normal runner already awaits browser/context/server.close. Give that
        # actual cleanup its own bounded observation before any forced control.
        until = time.monotonic() + 3
        while time.monotonic() < until:
            discover()
            reap_adopted()
            if not live():
                break
            time.sleep(0.05)
        if live():
            receipt['forcedCleanup'] = True
            signal_held(signal.SIGTERM)
            until = time.monotonic() + 3
            while time.monotonic() < until:
                discover()
                reap_adopted()
                if not live():
                    break
                time.sleep(0.05)
        if live():
            signal_held(signal.SIGKILL)
            until = time.monotonic() + 3
            while time.monotonic() < until:
                discover()
                reap_adopted()
                if not live():
                    break
                time.sleep(0.05)
        if child.poll() is not None:
            receipt['runnerExitCode'] = child.wait()
        discover()
        reap_adopted()
        receipt['allOwnedKernelIdentitiesExited'] = not live()
        receipt['allOwnedBirthsDisappeared'] = all(birth_disappeared(row) for row in issued.values())
    else:
        receipt['allOwnedKernelIdentitiesExited'] = True
        receipt['allOwnedBirthsDisappeared'] = True
    try:
        verify_pins()
        receipt['finalPinsUnchanged'] = True
    except Exception:
        receipt['finalPinsUnchanged'] = False
    top_browser_rows = [row for row in issued.values() if row.get('topBrowserLauncher')]
    receipt['boundTopBrowserLauncherCount'] = len(top_browser_rows)
    receipt['sequentialBoundTopBrowserLaunchers'] = 1 <= len(top_browser_rows) <= 3 and all(row['profileBound'] is True for row in top_browser_rows)
    if child is not None and not receipt['sequentialBoundTopBrowserLaunchers']:
        receipt['identityFailures'].append('Exact sequential bound Wave top browser launchers were not proven')
    if child is not None and receipt.get('runnerExitCode') == 0 and len(top_browser_rows) != 3:
        receipt['identityFailures'].append('Successful exact Wave14 runner must issue three bound top browsers')
    receipt['ownedFamily'] = [{key: value for key, value in row.items() if key != 'fd'} for row in issued.values()]
    receipt['temporaryAndOutputBytes'] = footprint()
    receipt['recordedUTC'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    receipt['originalChildStillLive'] = child is not None and child.poll() is None
    try:
        receipt['kernelOwnChildReceiptAfterWaitAndReap'] = own_kernel_child_receipt()
        receipt['kernelOwnChildrenEmpty'] = receipt['kernelOwnChildReceiptAfterWaitAndReap']['empty']
    except (OSError, ValueError):
        receipt['kernelOwnChildrenEmpty'] = False
        receipt['identityFailures'].append('Kernel own-child closure proof unavailable')
    receipt['state'] = 'HOLD' if not receipt['kernelOwnChildrenEmpty'] or receipt['identityFailures'] or receipt['originalChildStillLive'] or not receipt['allOwnedKernelIdentitiesExited'] or not receipt['allOwnedBirthsDisappeared'] else 'CLOSED'
    clean = receipt['state'] == 'CLOSED' and not receipt['identityFailures'] and not receipt['forcedCleanup'] and not receipt['deadlineFailure'] and not receipt['budgetFailure'] and receipt['finalPinsUnchanged'] and 'setupOrControlFailureType' not in receipt
    receipt['custodyClean'] = clean
    for row in issued.values():
        os.close(row['fd'])
    if control.exists():
        (control / 'owned-family-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps({key: receipt.get(key) for key in ['state', 'runnerExitCode', 'custodyClean', 'deadlineFailure', 'budgetFailure', 'forcedCleanup', 'allOwnedKernelIdentitiesExited', 'allOwnedBirthsDisappeared', 'setupOrControlFailureType']}))
sys.exit(receipt.get('runnerExitCode', 2) if clean else 2)
