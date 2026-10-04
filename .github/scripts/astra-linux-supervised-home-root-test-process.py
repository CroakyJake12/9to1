"""Isolated hosted synthetic root-test process; caller must prove full source/build closure."""
import argparse,hashlib,json,os,pathlib,subprocess,sys,time
from importlib.util import spec_from_file_location,module_from_spec
import sys
sys.dont_write_bytecode = True

p=argparse.ArgumentParser()
p.add_argument('--root',required=True);p.add_argument('--expected-commit',required=True);p.add_argument('--helper-sha256',required=True);p.add_argument('--output',required=True)
a=p.parse_args();root=pathlib.Path(a.root).resolve();out=pathlib.Path(a.output).resolve()
if os.name!='posix' or os.geteuid()!=0 or os.environ.get('ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE')!='1':raise SystemExit('explicit isolated synthetic administrator fixture required')
if not out.is_relative_to(root/'artifacts') or out.exists():raise SystemExit('fresh source-bound evidence directory required')
if subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'],text=True).strip()!=a.expected_commit:raise SystemExit('exact immutable commit required')
helper=root/'.github/scripts/astra_original_native_session_drain.py'
if helper.is_symlink() or hashlib.sha256(helper.read_bytes()).hexdigest()!=a.helper_sha256:raise SystemExit('exact owned-process helper required')
required=('ASTRA_ROOT_TEST_UID','ASTRA_ROOT_TEST_GID','ASTRA_ROOT_TEST_USER_HOME','ASTRA_ROOT_TEST_HOME_STATE_PATH','ASTRA_ROOT_TEST_RUNTIME_DIRECTORY','ASTRA_ROOT_TEST_PROFILE_TOOL','ASTRA_ROOT_TEST_PROFILE_TOOL_MANIFEST_SHA256','ASTRA_SUPERVISED_TEST_UID','ASTRA_SUPERVISED_TEST_GID','ASTRA_SUPERVISED_TEST_HOME','ASTRA_NATIVE_ATOMIC_HELPER_PATH','ASTRA_NATIVE_ATOMIC_HELPER_SHA256')
if any(not os.environ.get(k) for k in required):raise SystemExit('actual configured root fixture environment incomplete')
if int(os.environ['ASTRA_ROOT_TEST_UID'])<=0 or int(os.environ['ASTRA_ROOT_TEST_GID'])<=0:raise SystemExit('target fixture must be actual non-root user')
for primitive,paired in [('ASTRA_SUPERVISED_TEST_UID','ASTRA_ROOT_TEST_UID'),('ASTRA_SUPERVISED_TEST_GID','ASTRA_ROOT_TEST_GID'),('ASTRA_SUPERVISED_TEST_HOME','ASTRA_ROOT_TEST_USER_HOME')]:
 if os.environ[primitive]!=os.environ[paired]:raise SystemExit('Primitive and signed paired target identity disagree')
nativeHelper=pathlib.Path(os.environ['ASTRA_NATIVE_ATOMIC_HELPER_PATH'])
nativeSha=os.environ['ASTRA_NATIVE_ATOMIC_HELPER_SHA256']
if nativeHelper!=root/'artifacts/supervised-native-helper/atomic-spawn' or nativeHelper.is_symlink() or not nativeHelper.is_file() or hashlib.sha256(nativeHelper.read_bytes()).hexdigest()!=nativeSha:raise SystemExit('Actual caller-compiled native helper identity mismatch')
project=root/'9to1 OS/tests/NineToOne.Os.Supervisor.Root.Tests/NineToOne.Os.Supervisor.Root.Tests.csproj'
if project.is_symlink() or not project.is_file():raise SystemExit('actual separate root-test project missing')
out.mkdir(parents=True);seal=out/'expected-managed-launch.json';seal.write_text(json.dumps({'expectedManagedLaunch':True,'drained':False,'scope':'sampled original root-test session only'})+'\n')
spec=spec_from_file_location('root_owned_session',helper);module=module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
task=pathlib.Path(os.environ['ASTRA_ACTUAL_AVALONIA_BUILD_TASKS']).resolve()
if not task.is_relative_to(root/'artifacts') or task.is_symlink() or not task.is_file():raise SystemExit('actual caller-pinned source task required')
args=['dotnet','test',str(project),'-c','Release','-r','linux-x64','-f','net10.0','--no-build','--no-restore','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/root14-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:AvaloniaBuildTasksLocation='+str(task),'--logger','trx;LogFileName=root.trx','--results-directory',str(out/'trx')]
receipt={'argv':args,'administratorUid':os.geteuid(),'syntheticIssuerOnly':True,'releasePublisherAccepted':False,'installedUserAccepted':False,'nativeHelper':{'path':str(nativeHelper),'sha256':nativeSha},'targetUid':int(os.environ['ASTRA_SUPERVISED_TEST_UID']),'targetGid':int(os.environ['ASTRA_SUPERVISED_TEST_GID']),'targetUserHome':os.environ['ASTRA_SUPERVISED_TEST_HOME']}
process=None;session=None;primary=None
policyGuard=None
policyFailures=[]
def retain_policy_failure(error):
 if not any(original is error for original in policyFailures):policyFailures.append(error)
def load_actual_policy_guard():
 source=root/'.github/scripts/astra_hosted_credential_policy_guard.py'
 fd=None;loadFailures=[];captured=None
 try:
  fd=os.open(source,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC);before=os.fstat(fd)
  if not __import__('stat').S_ISREG(before.st_mode) or before.st_size!=17597:
   raise RuntimeError('Exact complete policy guard source required')
  chunks=[];length=0
  while length<=17597:
   part=os.read(fd,min(8192,17597+1-length));length+=len(part)
   if length>17597:raise RuntimeError('Bounded whole policy source capture required')
   if not part:break
   chunks.append(part)
  captured=b''.join(chunks)
  sourceIdentity=lambda value:(value.st_dev,value.st_ino,value.st_mode,value.st_uid,value.st_gid,value.st_nlink,value.st_size,value.st_mtime_ns,value.st_ctime_ns)
  if len(captured)!=17597 or hashlib.sha256(captured).hexdigest()!='c1817be52e8fa16f6280dfe7736e8887d603e2eb1e4ae0578c45ce43a9cce964' or sourceIdentity(before)!=sourceIdentity(os.fstat(fd)) or sourceIdentity(before)!=sourceIdentity(os.lstat(source)):
   raise RuntimeError('Same source FD/path and complete guard bytes required')
 except BaseException as error:loadFailures.append(error)
 finally:
  if fd is not None:
   try:os.close(fd)
   except BaseException as error:
    if not any(original is error for original in loadFailures):loadFailures.append(error)
 if len(loadFailures)==1:raise loadFailures[0]
 if loadFailures:raise BaseExceptionGroup('Original policy source and independent source close failed',loadFailures)
 spec=spec_from_file_location('root_test_hosted_kernel_policy_guard',source)
 policy=module_from_spec(spec);sys.modules[spec.name]=policy
 exec(compile(captured,str(source),'exec'),policy.__dict__)
 setup=pathlib.Path(os.environ['RUNNER_TEMP']).resolve()/('astra-synthetic-home-setup-'+os.environ['GITHUB_RUN_ID']+'-'+os.environ['GITHUB_RUN_ATTEMPT'])
 return policy.HostedCredentialPolicyGuard(root,setup,os.environ['GITHUB_RUN_ID'],os.environ['GITHUB_RUN_ATTEMPT'])
try:
 policyGuard=load_actual_policy_guard()
 receipt['hostedCredentialPolicyBeforeRootTests']=policyGuard.observe('before-root-tests')['status']
 with (out/'root-test.log').open('wb') as log:
  process=subprocess.Popen(args,cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  session=module.OriginalSession(process,out)
  deadline=time.monotonic()+900
  while process.poll() is None:
   session.observe()
   if time.monotonic()>deadline:raise TimeoutError('bounded root-test deadline')
   time.sleep(.1)
  receipt['exitCode']=process.returncode
except BaseException as error:primary=error
finally:
 originalRootDrained=False
 try:
  if session is not None:session.drain()
  if not json.loads(seal.read_text()).get('drained'):raise RuntimeError('reviewed helper did not seal actual cleanup')
  originalRootDrained=True
 except BaseException as cleanup:retain_policy_failure(cleanup)
 if originalRootDrained and policyGuard is not None:
  try:receipt['hostedCredentialPolicyAfterOriginalRootDrain']=policyGuard.observe('after-root-drain')['status']
  except BaseException as observation:retain_policy_failure(observation)
 try:(out/'root-process-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
 except BaseException as persistence:retain_policy_failure(persistence)
 if primary is not None and not any(original is primary for original in policyFailures):policyFailures.insert(0,primary)
 if len(policyFailures)==1:raise policyFailures[0]
 if policyFailures:raise BaseExceptionGroup('Root whole test and independent original drain/kernel observation/receipt failures',policyFailures)
if primary is not None:raise primary
if not json.loads(seal.read_text()).get('drained'):raise SystemExit('original root-test cleanup unproven')
raise SystemExit(receipt.get('exitCode',1))
