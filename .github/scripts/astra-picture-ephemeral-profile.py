"""Dedicated ephemeral hosted official profile correction; never local/production activation."""
import argparse,hashlib,json,os,pathlib,subprocess,sys
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);args=p.parse_args()
root=pathlib.Path.cwd();manifest=root/args.manifest
sha=lambda b:hashlib.sha256(b).hexdigest()
assert os.environ.get('GITHUB_ACTIONS')=='true' and os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted'
assert subprocess.check_output(['git','rev-parse','HEAD']).decode().strip()==args.expected_commit and os.environ['GITHUB_SHA']==args.expected_commit
assert sha(manifest.read_bytes())==args.manifest_sha
cut=json.loads(manifest.read_text());known={r['path']:r for r in cut['files']};actualFiles=set();actualLinks={}
for item in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
 if not item:continue
 h,name=item.split(b'\t',1);mode,kind,oid=h.decode().split();name=name.decode()
 if mode=='160000':actualLinks[name]=oid
 else:
  assert mode in ('100644','100755') and kind=='blob';actualFiles.add(name)
assert actualFiles==set(known)|{args.manifest} and actualLinks=={r['path']:r['commit']for r in cut['gitlinks']} and len(actualLinks)==22
for name,row in known.items():
 rel=pathlib.PurePosixPath(name);assert not rel.is_absolute() and '..'not in rel.parts
 f=root/name;assert f.is_file() and not f.is_symlink() and sha(f.read_bytes())==row['sha256']
out=root/'artifacts/picture19-managed/profile-correction';out.mkdir(parents=True,exist_ok=True)
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


records=[]
def run(argv,name,required=True):
 r=bounded_readonly(argv,name);records.append(r)
 (out/'commands.json').write_text(json.dumps(records,indent=2)+'\n')
 if required:assert r.get('exit')==0 and not r.get('timedOut') and not r.get('truncated'),name
 return r
def profiles(name):
 run(['sudo','-n','--','cat','/sys/kernel/security/apparmor/profiles'],name)
 return set((out/(name+'.stdout')).read_text().splitlines())
def restriction():
 value=pathlib.Path('/proc/sys/kernel/apparmor_restrict_unprivileged_userns').read_text().strip();assert value=='1';return value
before=profiles('profiles-before');assert not any(x.split(' (',1)[0] in ('bwrap','unpriv_bwrap')or x.startswith(('bwrap//','unpriv_bwrap//'))for x in before)
for f in ['/etc/apparmor.d/local/bwrap-userns-restrict','/etc/apparmor.d/local/unpriv_bwrap']:assert not pathlib.Path(f).exists() and not pathlib.Path(f).is_symlink()
assert restriction()=='1';run(['apparmor_parser','--version'],'actual-parser-version');run(['bwrap','--version'],'actual-bwrap-version')
run(['uname','-a'],'actual-kernel');run(['dpkg-query','-W','-f=${Package} ${Version} ${Architecture}\n','apparmor','bubblewrap'],'actual-packages')
profile=root/'.github/validation/picture-bwrap-upstream-abi4.profile';assert sha(profile.read_bytes())=='a964037f6cf0df1099f14226b037eaedde6237c86e715188e93eb460b30be859'
run(['apparmor_parser','--skip-kernel-load','--skip-cache',str(profile)],'exact-abi4-dry-parse')
run(['apparmor_parser','--preprocess','--skip-kernel-load','--skip-cache',str(profile)],'exact-profile-preprocessed')
loaded=False;code=1
try:
 # Mark cleanup ownership before the load call so partial load failures still remove only our introduced names.
 loaded=True
 run(['sudo','-n','--','apparmor_parser','--add','--skip-cache',str(profile)],'load-exact-two-profiles')
 after=profiles('profiles-after-load');assert after-before=={'bwrap (enforce)','unpriv_bwrap (enforce)'} and before<=after
 assert restriction()=='1'
 run(['/usr/bin/bwrap','--unshare-all','--die-with-parent','--ro-bind','/','/','/usr/bin/true'],'same-genuine-true-probe')
 run(['/usr/bin/bwrap','--unshare-all','--die-with-parent','--ro-bind','/','/','--proc','/proc','/bin/sh','-c','printf "%s\\n" "$$"; cat /proc/$$/attr/current; cat /proc/$$/status'],'actual-restricted-child')
 child=(out/'actual-restricted-child.stdout').read_text();childPid=int(child.splitlines()[0]);label=child.splitlines()[1]
 assert label=='bwrap//&unpriv_bwrap (enforce)'
 status={line.split(':',1)[0]:line.split(':',1)[1].strip()for line in child.splitlines()[2:]if ':'in line}
 assert int(status['Pid'])==childPid
 assert status['NoNewPrivs']=='1' and all(int(status[n],16)==0 for n in ['CapInh','CapPrm','CapEff','CapAmb'])
 (out/'policy-precondition-receipt.json').write_text(json.dumps({'profileSha256':sha(profile.read_bytes()),'childPid':childPid,'childLabel':label,'childStatus':status,'globalUsernsRestriction':restriction(),'qualification':'Environment correction only; acceptance requires all unchanged mandatory native101 suites and full guard success'},indent=2)+'\n')
 code=subprocess.run([sys.executable,'.github/scripts/astra-picture19-hosted-managed.py','--expected-commit',args.expected_commit,'--manifest',args.manifest,'--manifest-sha',args.manifest_sha]).returncode
finally:
 if loaded:
  run(['sudo','-n','--','apparmor_parser','--remove','--skip-cache',str(profile)],'remove-only-introduced-profiles')
 final=profiles('profiles-after-cleanup');assert final==before and restriction()=='1'
 (out/'cleanup-receipt.json').write_text(json.dumps({'introducedNames':['bwrap','unpriv_bwrap'],'loadedProfilesRestored':True,'restrictionPreserved':True,'fullSuiteExit':code},indent=2)+'\n')
sys.exit(code)
