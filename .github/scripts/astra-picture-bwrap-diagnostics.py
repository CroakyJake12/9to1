"""Observe genuine hosted sandbox preconditions; never changes policy or decoder sandbox."""
import hashlib,json,os,pathlib,shutil,subprocess,sys
out=pathlib.Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
def probe(argv,name):
 try:
  result=subprocess.run(argv,capture_output=True,timeout=30)
  (out/(name+'.stdout')).write_bytes(result.stdout);(out/(name+'.stderr')).write_bytes(result.stderr)
  return {'argv':argv,'exit':result.returncode,'stdoutSha256':hashlib.sha256(result.stdout).hexdigest(),'stderrSha256':hashlib.sha256(result.stderr).hexdigest()}
 except (OSError,subprocess.TimeoutExpired) as error:
  (out/(name+'.error')).write_text(type(error).__name__+': '+str(error)+'\n')
  return {'argv':argv,'error':type(error).__name__+': '+str(error)}
records=[]
for name in ['/proc/sys/kernel/apparmor_restrict_unprivileged_userns','/proc/sys/kernel/unprivileged_userns_clone','/proc/sys/user/max_user_namespaces','/sys/module/apparmor/parameters/enabled','/etc/apparmor.d/bwrap','/etc/apparmor.d/usr.bin.bwrap','/sys/kernel/security/apparmor/profiles']:
 p=pathlib.Path(name);row={'path':name}
 try:
  data=p.read_bytes();(out/(str(len(records))+'-'+p.name+'.txt')).write_bytes(data);row.update(bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
 except OSError as error:row['error']=type(error).__name__+': '+str(error)
 records.append(row)
binary=shutil.which('bwrap');result={'status':'OBSERVED_DIAGNOSTIC_ONLY_NO_POLICY_CHANGE','uid':os.getuid(),'gid':os.getgid(),'kernel':os.uname().release,'sandboxBinary':binary,'readonlyPreconditions':records}
if binary:
 executable=pathlib.Path(binary).resolve();result['sandboxExecutable']=str(executable);result['sandboxExecutableSha256']=hashlib.sha256(executable.read_bytes()).hexdigest();result['version']=probe([binary,'--version'],'bwrap-version');result['genuineSandboxProbe']=probe([binary,'--unshare-all','--die-with-parent','--ro-bind','/','/','/usr/bin/true'],'bwrap-genuine-userns')
else:result['genuineSandboxProbe']={'error':'bubblewrap missing from hosted PATH'}
result['limits']='Probe results never waive genuine Glycin mandatory bubblewrap or any full owning test. A probe failure remains diagnostic evidence; the complete native suite must still pass its existing checks. No sysctl/AppArmor edits, custom profiles, namespace bypass, donor changes or loader sandbox relaxation.'
# Bounded read-only host policy/labels. These never change kernel or AppArmor policy.
import selectors,time
MAX_DIAGNOSTIC_BYTES=262144
def bounded_readonly(argv,name):
    stdout=bytearray();stderr=bytearray();truncated=False;timedout=False
    try:
        process=subprocess.Popen(argv,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    except OSError as error:
        return {'argv':argv,'error':type(error).__name__+': '+str(error)}
    streams=selectors.DefaultSelector();streams.register(process.stdout,selectors.EVENT_READ,'stdout');streams.register(process.stderr,selectors.EVENT_READ,'stderr');deadline=time.monotonic()+15
    try:
        while streams.get_map():
            remaining=deadline-time.monotonic()
            if remaining<=0:timedout=True;process.kill();break
            for key,_ in streams.select(min(remaining,0.25)):
                data=os.read(key.fileobj.fileno(),65536)
                if not data:streams.unregister(key.fileobj);continue
                target=stdout if key.data=='stdout' else stderr
                available=MAX_DIAGNOSTIC_BYTES-len(target);target.extend(data[:available])
                if len(data)>available:truncated=True;process.kill();break
            if truncated:break
        try:exitcode=process.wait(timeout=2)
        except subprocess.TimeoutExpired:process.kill();exitcode=process.wait(timeout=2)
    finally:
        streams.close();process.stdout.close();process.stderr.close()
        if process.poll()is None:process.kill();process.wait(timeout=2)
    (out/(name+'.stdout')).write_bytes(stdout);(out/(name+'.stderr')).write_bytes(stderr)
    return {'argv':argv,'exit':exitcode,'timedOut':timedout,'truncated':truncated,'maximumBytesPerStream':MAX_DIAGNOSTIC_BYTES,'stdoutBytes':len(stdout),'stderrBytes':len(stderr),'stdoutSha256':hashlib.sha256(stdout).hexdigest(),'stderrSha256':hashlib.sha256(stderr).hexdigest()}
labels=[]
for name in ['/proc/self/attr/current','/proc/1/attr/current','/proc/self/status','/proc/self/uid_map','/proc/self/gid_map','/proc/self/cgroup','/etc/os-release']:
    p=pathlib.Path(name);row={'path':name}
    try:
        with p.open('rb')as stream:data=stream.read(MAX_DIAGNOSTIC_BYTES+1)
        truncated=len(data)>MAX_DIAGNOSTIC_BYTES;data=data[:MAX_DIAGNOSTIC_BYTES];(out/('audit-'+str(len(labels))+'-'+p.name+'.txt')).write_bytes(data);row.update(bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),truncated=truncated)
    except OSError as error:row['error']=type(error).__name__+': '+str(error)
    labels.append(row)
result['readonlyHostLabels']=labels
commands=[(['aa-status','--json'],'apparmor-status-unprivileged'),(['sudo','-n','--','aa-status','--json'],'apparmor-status-readonly-root'),(['sudo','-n','--','journalctl','-k','--no-pager','-n','200','-o','short-iso'],'kernel-audit-readonly'),(['apparmor_parser','--version'],'apparmor-parser-version')]
result['boundedReadonlyHostCommands']=[bounded_readonly(argv,name)for argv,name in commands]
if binary:
    result['genuineSandboxChildLabel']=bounded_readonly([binary,'--unshare-all','--die-with-parent','--ro-bind','/','/','/usr/bin/cat','/proc/self/attr/current'],'bwrap-child-actual-label')
result['auditQualification']='Actual caller/child labels and up to200 kernel messages observed only. Read-only root commands may fail and their actual failure remains evidence. Original true probe and full unchanged mandatory sandbox owning suite remain required. No profile load/parser policy invocation, installation, sysctl write, AppArmor disable, complain mode, capabilities grant, namespace/loader/seccomp bypass.'
(out/'sandbox-preconditions.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'status':result['status'],'probeExit':result['genuineSandboxProbe'].get('exit'),'probeError':result['genuineSandboxProbe'].get('error')}))
