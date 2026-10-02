"""Isolated read-only Picture host preconditions, never test acceptance or policy loading."""
import argparse,datetime,hashlib,json,os,pathlib,shutil,subprocess,sys
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);args=p.parse_args()
root=pathlib.Path.cwd();manifest=root/args.manifest
sha=lambda data:hashlib.sha256(data).hexdigest()
assert sha(manifest.read_bytes())==args.manifest_sha
assert subprocess.check_output(['git','rev-parse','HEAD']).decode().strip()==args.expected_commit
cut=json.loads(manifest.read_text());out=root/'artifacts/picture-preconditions';out.mkdir(parents=True,exist_ok=True)
# Check entire tracked byte closure and every exact source gitlink without SDK/install/build.
for row in cut['files']:
    path=root/row['path'];assert path.is_file() and sha(path.read_bytes())==row['sha256'],row['path']
actual=[]
for entry in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
    if not entry:continue
    header,name=entry.split(b'\t',1);mode,kind,oid=header.decode().split()
    if mode=='160000':actual.append({'path':name.decode(),'commit':oid})
assert sorted(actual,key=lambda x:x['path'])==sorted(cut['gitlinks'],key=lambda x:x['path']) and len(actual)==22
import selectors,time
MAX_DIAGNOSTIC_BYTES=262144
def bounded_readonly(argv,name):
    stdout=bytearray();stderr=bytearray();truncated=False;timedout=False
    try:
        started=time.time_ns(); process=subprocess.Popen(argv,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=dict(os.environ,TZ='UTC')); pid=process.pid
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
    return {'argv':argv,'exit':exitcode,'pid':pid,'startedUnixNs':started,'finishedUnixNs':time.time_ns(),'timedOut':timedout,'truncated':truncated,'maximumBytesPerStream':MAX_DIAGNOSTIC_BYTES,'stdoutBytes':len(stdout),'stderrBytes':len(stderr),'stdoutSha256':hashlib.sha256(stdout).hexdigest(),'stderrSha256':hashlib.sha256(stderr).hexdigest()}

result={'status':'READONLY_PRECONDITION_OBSERVATION_ONLY_NOT_NATIVE_ACCEPTANCE','commit':args.expected_commit,'manifestSha256':args.manifest_sha,'uid':os.getuid(),'kernel':os.uname().release,'commands':[]}
binary=shutil.which('bwrap')
if not binary:raise SystemExit('matching hosted bubblewrap missing; no substituted sandbox')
result['bwrapExecutableSha256']=sha(pathlib.Path(binary).resolve().read_bytes())
result['commands'].append(bounded_readonly([binary,'--version'],'bwrap-version'))
# Keep exactly original namespace/sandbox command. Record true PID/start/end then immediate ring capture.
result['commands'].append(bounded_readonly([binary,'--unshare-all','--die-with-parent','--ro-bind','/','/','/usr/bin/true'],'bwrap-genuine-userns'))
since=datetime.datetime.fromtimestamp(result['commands'][-1]['startedUnixNs']/1e9-2,datetime.timezone.utc).strftime('%Y-%m-%d %H:%M:%S')
result['commands'].append(bounded_readonly(['sudo','-n','--','dmesg','--time-format','iso','--since',since],'kernel-ring-immediate-readonly'))
result['commands'].append(bounded_readonly(['sudo','-n','--','aa-status','--json'],'apparmor-status-readonly'))
result['commands'].append(bounded_readonly(['apparmor_parser','--version'],'parser-version'))
result['commands'].append(bounded_readonly(['dmesg','--version'],'dmesg-version'))
result['timeEnvironment']={'commandTZ':'UTC','sinceFormat':'%Y-%m-%d %H:%M:%S','sourceUnixNs':result['commands'][1]['startedUnixNs'],'since':since}
profile=root/'.github/validation/picture-bwrap-upstream-abi4.profile'
result['profileSha256']=sha(profile.read_bytes())
# -Q: never load into kernel; -K: neither read nor write caches. Parsing may fail and is evidence.
result['commands'].append(bounded_readonly(['apparmor_parser','--skip-kernel-load','--skip-cache',str(profile)],'profile-abi4-dry-run'))
result['commands'].append(bounded_readonly(['dpkg-query','-W','-f=${Package} ${Version} ${Architecture}\n','bubblewrap','apparmor','apparmor-utils'],'official-package-versions'))
result['observations']=[]
paths=['/proc/self/attr/current','/proc/self/status','/proc/sys/kernel/apparmor_restrict_unprivileged_userns','/proc/sys/kernel/unprivileged_userns_clone','/sys/module/apparmor/parameters/enabled','/sys/kernel/security/apparmor/profiles','/etc/os-release']
features=pathlib.Path('/sys/kernel/security/apparmor/features')
if features.is_dir():
    entries=sorted(p for p in features.rglob('*') if p.is_file() and not p.is_symlink());assert len(entries)<=512
    paths += [str(p) for p in entries]
for i,name in enumerate(paths):
    record={'path':name}
    try:
        with open(name,'rb') as stream:data=stream.read(MAX_DIAGNOSTIC_BYTES+1)
        record.update(truncated=len(data)>MAX_DIAGNOSTIC_BYTES);data=data[:MAX_DIAGNOSTIC_BYTES];(out/('observation-%04d.bin'%i)).write_bytes(data);record.update(bytes=len(data),sha256=sha(data))
    except OSError as error:record['error']=str(error)
    result['observations'].append(record)
result['limits']='Read-only precondition evidence only; original full Picture101/e4 product, mandatory bwrap/Glycin/namespace/seccomp tests unchanged and unexecuted here. No policy activation/load/cache/profile install/sysctl change/security relaxation. Failed or truncated observations remain explicit, not acceptance.'
(out/'preconditions.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'status':result['status'],'trueProbe':result['commands'][1]}))
