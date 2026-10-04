#!/usr/bin/env python3
"""Evidence-only two sequential actual native processes. No build/install/browser."""
import ast, stat as stat_module, ctypes, datetime, hashlib, importlib.util, json, os, select, shutil, signal, subprocess, sys, time
from pathlib import Path
sys.dont_write_bytecode = True
base=Path(__file__).resolve().parent
plan_path=base/'home-public-flag-native2-owned-execution-plan03.json'
plan_bytes=plan_path.read_bytes();plan=json.loads(plan_bytes);plan_sha256=hashlib.sha256(plan_bytes).hexdigest()
owner_pid=os.getpid();output=Path(plan['output']);control=output/'control';temp=output/'tmp';cache=output/'font-cache'
sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
receipt={'state':'PREPARED','scope':'Independent proposal Home native20x2 only, no shared adoption/browser/AT', 'identityFailures':[],'forcedCleanup':False,'deadlineFailure':False,'budgetFailure':False,'cases':[]}
issued={};root_keys=set();child=None;kernel_proof=None
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
        if key in root_keys or not exited(row['fd']):
            continue
        current = stat(pid)
        if current and current['birth'] == row['birth'] and current['ppid'] == owner_pid:
            try:
                os.waitpid(pid, os.WNOHANG)
            except ChildProcessError:
                pass

def footprint():
    logical=allocated=0
    if output.exists():
        for folder,dirs,files in os.walk(output):
            for name in files:
                p=Path(folder)/name
                try:info=p.lstat()
                except FileNotFoundError:continue
                # Use the captured mode: native font cache can atomically rename
                # or remove a temporary file between lstat and a second lookup.
                if not stat_module.S_ISREG(info.st_mode):
                    # Default CLR IPC is metadata-only inside the fresh owned TMP.
                    # Admit only exact names of already held original native roots.
                    identity=None
                    for pid,birth in root_keys:
                        names={f'clr-debug-pipe-{pid}-{birth}-in',f'clr-debug-pipe-{pid}-{birth}-out'}
                        fifo=stat_module.S_ISFIFO(info.st_mode) and p.name in names
                        socket=stat_module.S_ISSOCK(info.st_mode) and p.name==f'dotnet-diagnostic-{pid}-{birth}-socket'
                        if p.parent==temp and info.st_uid==os.getuid() and (fifo or socket):identity=(pid,birth);break
                    if identity is None:
                        receipt['unexpectedOutputEntry']={'relativePath':str(p.relative_to(output)),'capturedMode':oct(info.st_mode),'isSymlink':stat_module.S_ISLNK(info.st_mode),'isSocket':stat_module.S_ISSOCK(info.st_mode)}
                        raise RuntimeError('Unexpected special output entry')
                    ipc=receipt.setdefault('observedOwnedClrIPC',{})
                    ipc[str(p.relative_to(output))]={'pid':identity[0],'birth':identity[1],'capturedMode':oct(info.st_mode),'logicalBytes':info.st_size,'allocatedBytes':info.st_blocks*512}
                logical+=info.st_size;allocated+=info.st_blocks*512
    return {'logical':logical,'allocated':allocated}

def check_resources():
    receipt['phase']='resource-inspection'
    size=footprint();free={v:shutil.disk_usage(v).free for v in ['/workspace','/tmp']}
    receipt['finalMeasuredFootprint']=size;receipt['finalMeasuredFree']=free
    if max(size.values())>plan['maximumOutputBytes'] or min(free.values())<plan['minimumFreeBytes']:
        receipt['budgetFailure']=True
    return not receipt['budgetFailure']

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

def finish_family():
    # Allow normal process-owned disposal first; forcing is always a failing verdict.
    for kind in [None,signal.SIGTERM,signal.SIGKILL]:
        if kind is not None and live():
            receipt['forcedCleanup']=True;signal_held(kind)
        until=time.monotonic()+3
        while time.monotonic()<until:
            discover();reap_adopted()
            if not live():break
            time.sleep(.05)
        if not live():break
    if child is not None and child.poll() is not None:receipt['lastObservedNativeExitCode']=child.wait()
    discover();reap_adopted()
    proof=own_kernel_child_receipt()
    ipc_gone=all(not os.path.lexists(output/name) for name in receipt.get('observedOwnedClrIPC',{}))
    closure={'allHeldExit':not live(),'allBirthsGone':all(birth_disappeared(row) for row in issued.values()),'kernel':proof,'observedOwnClrIPCGone':ipc_gone}
    receipt['lastFamilyClosure']=closure
    return closure['allHeldExit'] and closure['allBirthsGone'] and proof['empty'] and closure['observedOwnClrIPCGone']

try:
    if os.environ.get('B6_HOME_NATIVE_RUNTIME_GRANTED')!='granted':raise RuntimeError('Root native runtime grant absent')
    verify_pins()
    if output.exists():raise RuntimeError('Fresh unique output required')
    if int(Path('/proc/self/stat').read_text().split(' ',1)[0])!=owner_pid:raise RuntimeError('Proc namespace mismatch')
    spec=importlib.util.spec_from_file_location('ownedproof',plan['kernelProofPath']);kernel_proof=importlib.util.module_from_spec(spec);spec.loader.exec_module(kernel_proof)
    receipt['prelaunchKernel']=own_kernel_child_receipt()
    if not receipt['prelaunchKernel']['empty']:raise RuntimeError('Pre-existing own children or proof unavailable')
    if min(shutil.disk_usage(v).free for v in ['/workspace','/tmp'])<plan['minimumFreeBytes']:raise RuntimeError('Reserve unavailable')
    if ctypes.CDLL(None,use_errno=True).prctl(36,1,0,0,0)!=0:raise RuntimeError('Subreaper unavailable')
    for p in [output,control,temp,cache,output/'cli-home']:p.mkdir(mode=0o700,parents=True,exist_ok=False)
    for case in plan['commands']:
        verify_pins();started=time.monotonic()
        row={'name':case['name'],'status':'NOT_RUN'};receipt['cases'].append(row)
        env=os.environ.copy();env.update(case['environment']);env['PYTHONDONTWRITEBYTECODE']='1'
        with Path(case['log']).open('xb') as stream:
            child=subprocess.Popen(case['argv'],env=env,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
            info=stat(child.pid)
            if info is None or info['ppid']!=owner_pid:receipt['identityFailures'].append('Original native root parentage unproven');raise RuntimeError('Native root identity unavailable')
            root_keys.add((child.pid,info['birth']));admit(info,'original owned Popen direct child')
            if (child.pid,info['birth']) not in issued:receipt['identityFailures'].append('Original native held FD unproven');raise RuntimeError('Native root pidfd unavailable')
            row['nativeRoot']={k:v for k,v in info.items() if k in ['pid','ppid','birth']};row['status']='RUNNING'
            while child.poll() is None:
                discover();reap_adopted()
                if receipt['identityFailures'] or not check_resources():break
                if time.monotonic()-started>case['maximumSeconds']:receipt['deadlineFailure']=True;break
                time.sleep(.05)
            row['exitCode']=child.poll()
            row['closure']=finish_family()
            row['exitCode']=child.poll()
        check_resources();verify_pins()
        if not row['closure'] or receipt['forcedCleanup'] or receipt['identityFailures'] or receipt['deadlineFailure'] or receipt['budgetFailure']:
            row['status']='HOLD';raise RuntimeError('Independent native custody failed')
        if row['exitCode']!=0:row['status']='FAIL';raise RuntimeError('Actual native fixture failed')
        raw=Path(case['argv'][3]);report=json.loads(raw.read_text())
        # Exact original report shape is inspected from preserved actual native outputs.
        checks=report['checks'];counts={'executed':len(checks),'passed':sum(c['passed'] is True for c in checks),'failed':sum(c['passed'] is not True for c in checks)}
        if counts['passed']!=20 or counts['failed']!=0 or [c['name'] for c in checks]!=plan['exact20CheckNames'] or report['declaredSurfaceConstraint'] is not True or report['diagnosticPropertyMutation'] is not False:
            row['status']='FAIL';raise RuntimeError('Native original20 oracle or flag evidence failed')
        row['status']='PASS';row['rawSHA256']=sha(raw);row['counts']=counts
    receipt['state']='PASS'
except Exception as error:
    receipt['failureType']=type(error).__name__;receipt['state']='FAIL'
    if str(error) in ['Unexpected special output entry','Independent native custody failed','Actual native fixture failed','Native original20 oracle or flag evidence failed']:receipt['fixedFailureDetail']=str(error)
finally:
    try:
        if child is not None:finish_family()
        verify_pins();receipt['finalPinsUnchanged']=True
    except Exception as error:
        receipt['finalizationFailureType']=type(error).__name__;receipt['finalPinsUnchanged']=False
    try:check_resources()
    except Exception as error:receipt['resourceFailureType']=type(error).__name__;receipt['budgetFailure']=True
    receipt['ownedFamily']=[{k:v for k,v in row.items() if k!='fd'} for row in issued.values()]
    receipt['finalKernel']=own_kernel_child_receipt()
    if receipt['state']=='PASS' and (receipt['forcedCleanup'] or receipt['identityFailures'] or receipt['deadlineFailure'] or receipt['budgetFailure'] or not receipt['finalPinsUnchanged'] or not receipt['finalKernel']['empty']):receipt['state']='HOLD'
    for row in issued.values():os.close(row['fd'])
    receipt['recordedUTC']=datetime.datetime.now(datetime.timezone.utc).isoformat();receipt['planSHA256']=plan_sha256
    if output.exists():(output/'independent-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps({k:receipt.get(k) for k in ['state','failureType','forcedCleanup','budgetFailure','deadlineFailure','finalPinsUnchanged']}))
sys.exit(0 if receipt['state']=='PASS' else 2)
