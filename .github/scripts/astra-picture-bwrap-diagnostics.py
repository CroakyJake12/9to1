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
(out/'sandbox-preconditions.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'status':result['status'],'probeExit':result['genuineSandboxProbe'].get('exit'),'probeError':result['genuineSandboxProbe'].get('error')}))
