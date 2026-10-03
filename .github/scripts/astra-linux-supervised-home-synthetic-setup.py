"""Fresh hosted TEST issuer only. Provision all three public policies before preparation.
No private signing key is retained. Cleanup follows exact original-session exit witnesses.
"""
import argparse,hashlib,json,os,pathlib,pwd,re,subprocess,importlib.util,sys,time,shutil,stat
import sys
sys.dont_write_bytecode = True

p=argparse.ArgumentParser();p.add_argument('--publish');p.add_argument('--fixture-tool');p.add_argument('--native-helper');p.add_argument('--expected-product-sha');p.add_argument('--expected-tool-sha');p.add_argument('--expected-helper-sha');p.add_argument('--output',required=True);p.add_argument('--cleanup',action='store_true');a=p.parse_args()
root=pathlib.Path(os.environ['GITHUB_WORKSPACE']).resolve();temp=pathlib.Path(os.environ['RUNNER_TEMP']).resolve();out=pathlib.Path(a.output).resolve()
if os.name!='posix' or not pathlib.Path('/proc').is_dir() or os.geteuid()!=0 or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('RUNNER_ENVIRONMENT')!='github-hosted' or os.environ.get('ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE')!='1':raise SystemExit('Actual fresh hosted Linux administrator TEST lane required')
runId=os.environ['GITHUB_RUN_ID'];attempt=os.environ['GITHUB_RUN_ATTEMPT']
if not runId.isdigit() or not attempt.isdigit() or out!=temp/('astra-synthetic-home-setup-'+runId+'-'+attempt):raise SystemExit('Exact unique hosted run output required')
helper=root/'.github/scripts/astra_original_native_session_drain.py'
if helper.is_symlink() or not helper.is_file():raise SystemExit('Actual immutable cut original-session helper required')
commands=[]
def digest(p):
 with pathlib.Path(p).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def write(path,data):path.write_text(json.dumps(data,indent=2)+'\n')
def run(argv,name,env=None):
 records=out/'owned-session-drain'/name;records.mkdir(parents=True,exist_ok=False)
 write(records/'expected-managed-launch.json',{'expectedManagedLaunch':True,'drained':False,'scope':'sampled original setup process session'})
 spec=importlib.util.spec_from_file_location('root_setup_owned_session',helper);module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
 session=None;primary=None;code=None
 try:
  with (out/(name+'.stdout')).open('wb') as stdout,(out/(name+'.stderr')).open('wb') as stderr:
   process=subprocess.Popen(argv,stdout=stdout,stderr=stderr,env=env,start_new_session=True);session=module.OriginalSession(process,records);deadline=time.monotonic()+240
   while process.poll() is None:
    session.observe()
    if time.monotonic()>deadline:raise TimeoutError('Original setup subprocess deadline exceeded')
    time.sleep(.02)
   code=process.wait()
 except BaseException as error:primary=error
 finally:
  try:
   if session is not None:session.drain()
   if not json.loads((records/'expected-managed-launch.json').read_text()).get('drained'):raise RuntimeError('Original setup sampled-session cleanup unproved')
  except BaseException as cleanup:
   if primary is not None:raise BaseExceptionGroup('Setup command and original cleanup both failed',[primary,cleanup])
   raise
  commands.append({'name':name,'argv':argv,'exitCode':code,'sampledOriginalSessionDrained':True});write(out/'commands.json',commands)
 if primary is not None:raise primary
 if code:raise RuntimeError('Isolated test setup failed: '+name)
 return (out/(name+'.stdout')).read_text()
protected=['/etc/9to1','/var/lib/9to1','/usr/lib/9to1']
installedPackages=['/usr/lib/9to1/apps/'+appId for appId in ['os.shell','os.installed-application-widget-owner','os.atomic-spawn-supervisor']]
desktops=['/usr/share/applications/9to1-os-shell.desktop','/usr/share/applications/9to1-os-installed-application-widget-owner.desktop','/usr/share/applications/9to1-os-atomic-spawn-supervisor.desktop']
user='astra-home-'+runId+'-'+attempt;runtime='/run/astra-home-owning-'+runId+'-'+attempt
markerPath=out/'protected-state-ownership.json'
if a.cleanup:
 if out.is_symlink() or not out.is_dir() or out.stat().st_uid!=0 or stat.S_IMODE(out.stat().st_mode)!=0o700 or markerPath.is_symlink() or not markerPath.is_file() or markerPath.stat().st_uid!=0:raise SystemExit('Actual root-owned setup state required')
 marker=json.loads(markerPath.read_text());expected={'protectedRoots':protected,'installedPackages':installedPackages,'desktops':desktops,'user':user,'runtime':runtime,'runId':runId,'attempt':attempt}
 if any(marker.get(k)!=v for k,v in expected.items()) or marker.get('protectedRootsAbsent') is not True:raise SystemExit('Exact initial absent ownership marker required; retain state')
 cleanup={'status':'PENDING','drainRequired':True,'removed':[]};write(out/'protected-state-cleanup.json',cleanup)
 try:
  # The enclosing caller has observed its complete known original test/build session drain.
  # Authenticate every setup-command seal and the independently observed root
  # test session, including pending failure records. No false prelaunch seal passes.
  sealRoots=[out/'owned-session-drain',root/'artifacts/desktop-visible-owning/root-process']
  for sealRoot in sealRoots:
   if not sealRoot.exists():continue
   for seal in sealRoot.rglob('expected-managed-launch.json'):
    if seal.is_symlink() or not seal.is_file() or seal.stat().st_uid!=0 or json.loads(seal.read_text()).get('drained') is not True:raise RuntimeError('Original setup/root test witness incomplete; keep protected state')
   for record in sealRoot.rglob('*-pending.json'):
    if record.is_symlink() or json.loads(record.read_text()).get('drained') is not True:raise RuntimeError('Original identity pending; keep protected state')
  write(out/'caller-drain-authorized.json',{'originalCallerSampledSessionDrained':True,'runId':runId,'attempt':attempt})
  authorization=out/'caller-drain-authorized.json'
  if authorization.is_symlink() or not authorization.is_file() or authorization.stat().st_uid!=0 or json.loads(authorization.read_text())!={'originalCallerSampledSessionDrained':True,'runId':runId,'attempt':attempt}:raise RuntimeError('Caller exact original-session drain authorization absent')
  try:entry=pwd.getpwnam(user)
  except KeyError:entry=None
  if entry is not None:
   if entry.pw_uid<=0 or entry.pw_dir!='/home/'+user or marker.get('uid')!=entry.pw_uid or marker.get('gid')!=entry.pw_gid:raise RuntimeError('Fresh exact target-user identity changed; retain state')
   run(['/usr/sbin/userdel','--remove',user],'cleanup-exact-user');cleanup['removed'].append(entry.pw_dir)
  for name in desktops+installedPackages+[runtime]+protected:
   x=pathlib.Path(name)
   if x.is_symlink():raise RuntimeError('Owned protected cleanup root changed to link; retain state')
   if not os.path.lexists(x):continue
   if x.stat().st_uid!=0:raise RuntimeError('Owned protected root owner changed; retain state')
   if name=='/usr/lib/9to1':
    # Remove only the three exact issued package subtrees above. Parent
    # containers must now be empty; never recursively delete the canonical root.
    if not x.is_dir():raise RuntimeError('Canonical package container changed type; retain state')
    apps=x/'apps'
    if apps.is_symlink():raise RuntimeError('Canonical apps container changed to link; retain state')
    if apps.exists():
     if not apps.is_dir() or apps.stat().st_uid!=0:raise RuntimeError('Canonical apps container changed owner/type; retain state')
     apps.rmdir()
    x.rmdir()
   elif x.is_dir():shutil.rmtree(x)
   elif x.is_file():x.unlink()
   else:raise RuntimeError('Unexpected protected root type; retain state')
   cleanup['removed'].append(name)
  if any(os.path.lexists(x) for x in protected+desktops+[runtime]):raise RuntimeError('Created protected state remains')
  cleanup['status']='ACTUAL_INITIAL_ABSENT_HOSTED_TEST_STATE_REMOVED_AFTER_ORIGINAL_CALLER_DRAIN'
 finally:
  write(out/'protected-state-cleanup.json',cleanup)
  retained=root/'artifacts/desktop-visible-owning/synthetic-setup-public-proof'
  if retained.exists():raise RuntimeError('Fresh retained setup proof required')
  shutil.copytree(out,retained)
  for item in retained.rglob('*'):
   if item.is_symlink():raise RuntimeError('Setup public proof contains link')
   os.chmod(item,0o755 if item.is_dir() else 0o644)
  os.chmod(retained,0o755)
 sys.exit(0)
if out.exists():raise SystemExit('Fresh isolated setup output required')
out.mkdir(mode=0o700)
if any(os.path.lexists(x) for x in protected+desktops+[runtime]):raise SystemExit('Preexisting protected installation refuses setup without mutation')
try:pwd.getpwnam(user)
except KeyError:pass
else:raise SystemExit('Preexisting target account refuses setup')
marker={'protectedRoots':protected,'installedPackages':installedPackages,'desktops':desktops,'user':user,'runtime':runtime,'runId':runId,'attempt':attempt,'protectedRootsAbsent':True,'uid':None,'gid':None,'completed':False};write(markerPath,marker)
publish=pathlib.Path(a.publish).resolve();tool=pathlib.Path(a.fixture_tool).resolve();native=pathlib.Path(a.native_helper).resolve();product=publish/'NineToOne.Os.Shell'
for path,expected in [(product,a.expected_product_sha),(tool,a.expected_tool_sha),(native,a.expected_helper_sha)]:
 if not path.is_relative_to(root/'artifacts') or not re.fullmatch('[0-9a-f]{64}',expected or '') or path.is_symlink() or not path.is_file() or digest(path)!=expected:raise SystemExit('Exact fresh compiled artifact pin mismatch')
if native.name!='atomic-spawn' or len(list(native.parent.iterdir()))!=1 or native.read_bytes()[:4]!=b'\x7fELF':raise SystemExit('Sole actual freshly compiled helper ELF required')
env=dict(os.environ,ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE='1');packages=[];trusts=[]
for label,command,payload,envelope,desktop,appId in [('home','--produce-synthetic-home-package',publish,'synthetic-home.9to1-install',desktops[0],'os.shell'),('owner','--produce-synthetic-widget-owner-package',publish,'synthetic-widget-owner.9to1-install',desktops[1],'os.installed-application-widget-owner'),('helper','--produce-synthetic-atomic-helper-package',native.parent,'synthetic-atomic-helper.9to1-install',desktops[2],'os.atomic-spawn-supervisor')]:
 synthetic=out/('synthetic-'+label);run([str(tool),command,str(payload),str(synthetic)],'synthetic-sign-'+label,env)
 paths=[synthetic/'synthetic-test-trust.json',synthetic/envelope,synthetic/pathlib.Path(desktop).name,synthetic/'producer-receipt.json']
 if any(x.is_symlink() or not x.is_file() for x in paths):raise RuntimeError('Actual synthetic producer output absent')
 receipt=json.loads(paths[3].read_text());trust=json.loads(paths[0].read_text())
 if receipt.get('code')!='SyntheticActualPublishedPayloadSigned' or receipt.get('issuer')!='ephemeral isolated hosted TEST ONLY' or receipt.get('appId')!=appId or any(receipt.get(k) is not False for k in ['privateKeyPersisted','releasePublisherAccepted','installedUserAccepted','launched','runtimeAccepted']):raise RuntimeError('Exact synthetic nonrelease/no-private-key scope required')
 if len(trust)!=1 or receipt.get('envelopeSha256','').lower()!=digest(paths[1]) or receipt.get('desktopSha256','').lower()!=digest(paths[2]):raise RuntimeError('Actual producer public policy/envelope/desktop mismatch')
 trusts+=trust;packages.append({'label':label,'payload':str(payload),'envelope':str(paths[1]),'desktop':str(paths[2]),'producer':receipt,'envelopeSha256':digest(paths[1]),'desktopSha256':digest(paths[2])})
if len({x['keyId'] for x in trusts})!=3:raise RuntimeError('Exactly three independent public synthetic policies required')
trustFile=out/'synthetic-three-public-policies.json';write(trustFile,trusts)
run(['/usr/bin/install','-d','-o','root','-g','root','-m','0755','/etc/9to1/identity'],'public-test-policy-directory')
run(['/usr/bin/install','-o','root','-g','root','-m','0644',str(trustFile),'/etc/9to1/identity/publishers.json'],'public-all-three-test-policies-before-preparation')
for package in packages:run([str(product),'--native-enroll-first-install',package['payload'],package['envelope'],package['desktop']],'maintained-first-install-'+package['label'])
if digest('/etc/9to1/identity/publishers.json')!=digest(trustFile):raise RuntimeError('All three protected policies changed during enrollment')
run(['/usr/sbin/useradd','--create-home','--user-group','--shell','/usr/sbin/nologin',user],'fresh-target-user');entry=pwd.getpwnam(user);home=pathlib.Path(entry.pw_dir).resolve()
if entry.pw_uid==0 or entry.pw_gid==0 or home!=pathlib.Path('/home/'+user) or not home.is_dir():raise RuntimeError('Actual fresh nonroot target required')
marker.update(uid=entry.pw_uid,gid=entry.pw_gid);write(markerPath,marker)
clean=['/usr/bin/setpriv','--reuid='+str(entry.pw_uid),'--regid='+str(entry.pw_gid),'--clear-groups','--no-new-privs','--','/usr/bin/env','-i','HOME='+str(home),'PATH=/usr/bin:/bin']
run(clean+['/usr/bin/mkdir','-p',str(home/'.local/share'),str(home/'.config')],'target-user-xdg-directories')
observed=run(clean+['DOTNET_EnableDiagnostics=0',str(tool),'--observe-home-state-path',str(entry.pw_uid)],'actual-target-user-home-path')
pathEvidence=json.loads(observed);state=pathlib.Path(pathEvidence['homeStatePath'])
if pathEvidence.get('code')!='ActualTargetUserHomePathObserved' or pathEvidence.get('observedPrincipal')!='unix-euid:'+str(entry.pw_uid) or any(pathEvidence.get(k) is not False for k in ['wroteHome','grantedActor','installedAccepted']) or not state.is_absolute() or not state.is_relative_to(home):raise RuntimeError('Actual maintained target user no-write Home path observation required')
run(['/usr/bin/install','-d','-o','root','-g','root','-m','0755',runtime],'root-immutable-runtime-directory')
marker['completed']=True;write(markerPath,marker)
receipt={'status':'ISOLATED_SYNTHETIC_TEST_ISSUER_SETUP_ONLY_NOT_RUNTIME_ACCEPTANCE','user':user,'uid':entry.pw_uid,'gid':entry.pw_gid,'userHome':str(home),'homeStatePath':str(state),'profileTool':str(tool),'runtimeDirectory':runtime,'productApphostSha256':digest(product),'fixtureToolApphostSha256':digest(tool),'nativeHelperSha256':digest(native),'publicTrustSha256':digest(trustFile),'packages':packages,'commands':commands,'qualification':'All three synthetic public policies and genuine maintained installed packages before frozen preparations. Runtime/UID/caps/signed/native original actor tests mandatory. No release/user publisher or existing user data accepted.'}
write(out/'setup-receipt.json',receipt)
