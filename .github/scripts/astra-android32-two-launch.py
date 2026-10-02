"""Same verified APK, fresh then retained-data startup. No key/data/pref reset."""
import hashlib,json,os,pathlib,re,shutil,subprocess,sys,time,importlib.util
spec=importlib.util.spec_from_file_location("activity_observation",pathlib.Path(__file__).with_name("astra-android-activity-observation.py"));activity_parser=importlib.util.module_from_spec(spec);spec.loader.exec_module(activity_parser)
root=pathlib.Path.cwd();evidence=root/'artifacts/android-launches';evidence.mkdir(parents=True,exist_ok=True)
smoke=root/'.github/scripts/haven-android-smoke.sh'
sourceRoot=root
verifier=root/'.github/scripts/astra-android29-verify.py'
def sha(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
expected=json.loads((root/'artifacts/apk/build-receipt.json').read_text());apk=root/'artifacts/apk/android29-frozen91-Signed.apk'
if expected['commit']!=os.environ['EXPECTED_COMMIT'] or expected['runId']!=os.environ['GITHUB_RUN_ID']:raise SystemExit('fresh build source/run mismatch')
def verify_inputs():
 if sha(smoke)!='d9f51cf3093bdb0069d6730cc6318722649f026efd42cc5619f8eea7d15d8b7f':raise RuntimeError('original MAIN smoke changed')
 subprocess.run([sys.executable,str(verifier),'source'],check=True)
verify_inputs()
summary={'smokeRun':os.environ['GITHUB_RUN_ID'],'smokeMetadataCommit':os.environ['GITHUB_SHA'],'buildRun':expected['runId'],'buildHead':expected['commit'],'qualification':'Same-APK MAIN startup +source-bound owning reserved-fixture crypto reads only; no real secret export/HOME/widgets/inference/production authority','launches':[]}
def save(): (evidence/'two-launch-receipt.json').write_text(json.dumps(summary,indent=2)+'\n')
def logged(args,path,**kwargs):
 with path.open('wb') as f:return subprocess.run(args,stdout=f,stderr=subprocess.STDOUT,check=False,**kwargs).returncode
def installed_hash(directory,phase):
 result=subprocess.run(['adb','shell','pm','path','com.cakemods.haven'],text=True,capture_output=True)
 (directory/(phase+'-installed-package-path.txt')).write_text(result.stdout+result.stderr)
 paths=[s.removeprefix('package:').strip() for s in result.stdout.splitlines() if s.startswith('package:')]
 if result.returncode or len(paths)!=1 or not paths[0].startswith('/data/app/') or not paths[0].endswith('/base.apk') or any(c.isspace() for c in paths[0]):raise RuntimeError('no unique safe installed base APK')
 with (directory/(phase+'-installed-apk-read.stderr')).open('wb') as err:
  child=subprocess.Popen(['adb','exec-out','cat',paths[0]],stdout=subprocess.PIPE,stderr=err)
  h=hashlib.sha256();count=0
  while data:=child.stdout.read(1024*1024):h.update(data);count+=len(data)
  child.stdout.close();code=child.wait()
 actual={'sha256':h.hexdigest(),'bytes':count,'readExit':code}
 (directory/(phase+'-installed-apk-observation.json')).write_text(json.dumps(actual,indent=2)+'\n')
 if code or actual['sha256']!=expected['apkSha256'] or count!=expected['apkBytes']:raise RuntimeError('installed APK did not match verified build')
def probe_phase(directory,phase,uid):
 probe=directory/'credential-probe';probe.mkdir()
 run=os.environ['GITHUB_RUN_ID'];head=expected['commit']
 if not re.fullmatch(r'[0-9]{1,20}',run) or not re.fullmatch(r'[0-9a-f]{40}',head):raise RuntimeError('invalid bound probe run/head')
 component='com.cakemods.haven.AstraCredentialProbeActivity'
 def text(args,name):
  r=subprocess.run(args,text=True,capture_output=True);(probe/name).write_text(r.stdout+r.stderr)
  if r.returncode:raise RuntimeError('probe observation command failed: '+name)
  return r.stdout
 def actual_pid(name):
  pids=text(['adb','shell','pidof','com.cakemods.haven'],name).split()
  if len(pids)!=1 or not pids[0].isdigit():raise RuntimeError('no unique actual app PID')
  return pids[0]
 def decision(activity):return activity_parser.inspect_activity(activity)
 def main_active(activity):return decision(activity)['mainResumed']
 before=text(['adb','shell','dumpsys','activity','activities'],'activities-before.txt')
 if not main_active(before):raise RuntimeError('actual MAIN activity not resumed before probe')
 pid=actual_pid('pid-before.txt')
 text(['adb','logcat','-c'],'logcat-clear.txt')
 text(['adb','shell','am','start','-W','-n','com.cakemods.haven/'+component,'--es','astra_phase',phase,'--es','astra_run',run,'--es','astra_head',head],'probe-start.txt')
 # Read real source-bound marker AND independently observe component gone/Main resumed.
 # A PASS preceding Finish is insufficient; retain later FAIL/close-failure markers.
 closed=0;deadline=time.monotonic()+60;lines=[];samples=[];sequence=[]
 sampleIndex=0
 while time.monotonic()<deadline:
  log=text(['adb','logcat','-d','-v','threadtime','-s','ASTRA_CREDENTIAL_PROBE:I','*:S'],'probe-logcat.txt')
  lines=[l for l in log.splitlines() if 'ASTRA_CREDENTIAL_PROBE' in l and 'phase=' in l]
  if any('|result=FAIL|' in l for l in lines):raise RuntimeError('actual credential probe failure')
  sampleIndex+=1;filename=f'activities-sample-{sampleIndex:04d}.txt'
  activity=text(['adb','shell','dumpsys','activity','activities'],filename)
  observed=decision(activity);gone=not observed['activeOrUnknownProbe']
  sample={'ordinal':sampleIndex,'path':filename,'sha256':sha(probe/filename),'observedMonotonicNs':time.monotonic_ns(),'decision':observed}
  samples.append(sample)
  (probe/'activity-samples-receipt.json').write_text(json.dumps({'samples':samples,'qualification':'Unique observations; identical SHA may reflect unchanged actual dumps, not overwritten samples'},indent=2)+'\n')
  if lines and gone and observed['mainResumed']:closed+=1;sequence.append(sample)
  else:closed=0;sequence=[]
  if closed>=3:break
  time.sleep(1)
 else:raise RuntimeError('probe completion/closure/Main readiness timeout')
 if len(sequence)!=3 or len({sample['path'] for sample in sequence})!=3:raise RuntimeError('missing unique three closure samples')
 # Final full observations after closure, rather than accepting the first PASS.
 log=text(['adb','logcat','-d','-v','threadtime','-s','ASTRA_CREDENTIAL_PROBE:I','*:S'],'probe-logcat-final.txt')
 lines=[l for l in log.splitlines() if 'ASTRA_CREDENTIAL_PROBE' in l and 'phase=' in l]
 expected_marker=f'phase={phase}|run={run}|head={head}|uid={uid}|positive=2|result=PASS|code=none|complete=true'
 if len(lines)!=1 or not lines[0].endswith(expected_marker):raise RuntimeError('missing/foreign/duplicate/incomplete bound probe marker')
 identity=re.match(r'^\S+\s+\S+\s+(\d+)\s+\d+\s+[A-Z]\s+ASTRA_CREDENTIAL_PROBE\s*:',lines[0])
 if identity is None or identity[1]!=pid or actual_pid('pid-after.txt')!=pid:raise RuntimeError('probe marker/process identity changed')
 final=text(['adb','shell','dumpsys','activity','activities'],'activities-after.txt')
 finalDecision=decision(final)
 if not finalDecision['mainResumed'] or finalDecision['activeOrUnknownProbe']:raise RuntimeError('probe active/unknown or MAIN not resumed')
 current=text(['adb','shell','pm','list','packages','-U','com.cakemods.haven'],'uid-after.txt')
 if re.findall(r'(?m)^package:com\.cakemods\.haven uid:(\d+)\s*$',current)!=[uid]:raise RuntimeError('probe UID changed')
 text(['adb','logcat','-d','-v','threadtime'],'complete-logcat-after.txt')
 logged(['adb','shell','uiautomator','dump','/sdcard/astra-probe-final.xml'],probe/'ui-dump-status.txt')
 logged(['adb','pull','/sdcard/astra-probe-final.xml',str(probe/'final-window.xml')],probe/'ui-pull-status.txt')
 with (probe/'final-screen.png').open('wb') as f:subprocess.run(['adb','exec-out','screencap','-p'],stdout=f,stderr=subprocess.DEVNULL,check=False)
 (probe/'completion-receipt.json').write_text(json.dumps({'phase':phase,'run':run,'head':head,'uid':uid,'pid':pid,'positive':2,'result':'PASS','componentClosed':True,'mainResumed':True,'buildRun':expected['runId'],'buildHead':expected['commit'],'smokeRun':run,'smokeMetadataCommit':os.environ['GITHUB_SHA'],'closureSamples':sequence,'allSamples':samples,'finalObservation':{'path':'activities-after.txt','sha256':sha(probe/'activities-after.txt'),'decision':finalDecision},'qualification':'Actual owning production-store fixture reads; no real secret export/login/HOME/widgets authority'},indent=2)+'\n')

for label in ('first-fresh','second-retained'):
 directory=evidence/label
 if directory.exists():raise SystemExit('evidence directory already exists; will not overwrite prior launch')
 directory.mkdir();record={'launch':label,'status':'started','buildReceipt':expected};summary['launches'].append(record);save()
 try:
  verify_inputs()
  # Revalidate the actual downloaded signature/certificate/source/run receipt BEFORE install.
  buildEnv=dict(os.environ,GITHUB_SHA=expected['commit'],EXPECTED_COMMIT=expected['commit'],EXPECTED_MANIFEST_SHA256=expected['manifestSha256'],GITHUB_RUN_ID=expected['runId'])
  code=logged([sys.executable,str(verifier),'download'],directory/'download-verifier.log',cwd=sourceRoot,env=buildEnv)
  if code:raise RuntimeError('download verification failed')
  verified=json.loads((sourceRoot/'artifacts/smoke/download-verification.json').read_text())
  (root/'artifacts/smoke').mkdir(parents=True,exist_ok=True)
  # Download evidence already belongs to this exact fresh workflow checkout.
  for field in ('commit','manifestSha256','runId','apkSha256','apkBytes','certificateSha256','package','abis','signing'):
   if verified[field]!=expected[field]:raise RuntimeError('relaunch receipt changed: '+field)
  if sha(apk)!=expected['apkSha256']:raise RuntimeError('same APK bytes changed')
  shutil.copytree(root/'artifacts/smoke',directory/'download-evidence')
  package=subprocess.run(['adb','shell','pm','list','packages','com.cakemods.haven'],text=True,capture_output=True)
  (directory/'package-before.txt').write_text(package.stdout+package.stderr)
  if package.returncode:raise RuntimeError('could not observe package before launch')
  present='package:com.cakemods.haven' in package.stdout.splitlines()
  if present!=(label=='second-retained'):raise RuntimeError('fresh/retained package precondition failed')
  if label=='second-retained':
   installed_hash(directory,'before')
   if logged(['adb','shell','am','force-stop','com.cakemods.haven'],directory/'force-stop.txt'):raise RuntimeError('force-stop failed')
  target=directory/'artifacts/android';target.mkdir(parents=True);os.link(apk,target/apk.name)
  if sha(target/apk.name)!=expected['apkSha256']:raise RuntimeError('smoke copy changed')
  # Unchanged smoke uses adb install -r, force-stop, MAIN/LAUNCHER, failure checks and trap evidence.
  with (directory/'complete-smoke.log').open('wb') as log:
   code=subprocess.run(['sh',str(smoke)],cwd=directory,stdout=log,stderr=subprocess.STDOUT,check=False).returncode
  record['smokeExit']=code
  # Even failing smoke retains its independent output; never start second after first failure.
  if code:raise RuntimeError('original startup smoke failed')
  installed_hash(directory,'after')
  uid=subprocess.run(['adb','shell','pm','list','packages','-U','com.cakemods.haven'],text=True,capture_output=True)
  (directory/'package-uid-after.txt').write_text(uid.stdout+uid.stderr)
  ids=re.findall(r'(?m)^package:com\.cakemods\.haven uid:(\d+)\s*$',uid.stdout)
  if uid.returncode or len(ids)!=1:raise RuntimeError('no unique installed package UID')
  if label=='first-fresh':summary['firstPackageUID']=ids[0]
  elif summary['firstPackageUID']!=ids[0]:raise RuntimeError('package UID changed across retained launch')
  record['packageUID']=ids[0]
  probe_phase(directory,'write-read' if label=='first-fresh' else 'read-only',ids[0])
  record['credentialPositiveReads']=2
  logged(['adb','shell','dumpsys','package','com.cakemods.haven'],directory/'package-after.txt')
  logged(['adb','shell','run-as','com.cakemods.haven','id'],directory/'run-as-status.txt')
  verify_inputs()
  record['status']='startup-passed';record['installedApkSha256']=expected['apkSha256'];save()
 except Exception as error:
  # Preserve complete failure observations independently, without changing application data.
  failure=directory/'failure-observations';failure.mkdir(exist_ok=True)
  for name,args in [('logcat.txt',['adb','logcat','-d','-v','threadtime']),('activities.txt',['adb','shell','dumpsys','activity','activities']),('pid.txt',['adb','shell','pidof','com.cakemods.haven']),('package.txt',['adb','shell','dumpsys','package','com.cakemods.haven'])]:
   logged(args,failure/name)
  logged(['adb','shell','uiautomator','dump','/sdcard/astra-probe-failure.xml'],failure/'ui-dump-status.txt')
  logged(['adb','pull','/sdcard/astra-probe-failure.xml',str(failure/'window.xml')],failure/'ui-pull-status.txt')
  with (failure/'screen.png').open('wb') as f:subprocess.run(['adb','exec-out','screencap','-p'],stdout=f,stderr=subprocess.DEVNULL,check=False)
  record['status']='failed';record['failure']=str(error);save()
  if label=='first-fresh':summary['launches'].append({'launch':'second-retained','status':'not-attempted-first-failed'});save()
  raise SystemExit(1)
save()
