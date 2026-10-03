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
required=('ASTRA_ROOT_TEST_UID','ASTRA_ROOT_TEST_GID','ASTRA_ROOT_TEST_USER_HOME','ASTRA_ROOT_TEST_HOME_STATE_PATH','ASTRA_ROOT_TEST_RUNTIME_DIRECTORY','ASTRA_ROOT_TEST_PROFILE_TOOL','ASTRA_SUPERVISED_TEST_UID','ASTRA_SUPERVISED_TEST_GID','ASTRA_SUPERVISED_TEST_HOME','ASTRA_NATIVE_ATOMIC_HELPER_PATH','ASTRA_NATIVE_ATOMIC_HELPER_SHA256')
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
try:
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
 try:
  if session is not None:session.drain()
  if not json.loads(seal.read_text()).get('drained'):raise RuntimeError('reviewed helper did not seal actual cleanup')
 except BaseException as cleanup:
  if primary is not None:raise BaseExceptionGroup('Root whole test and actual original cleanup both failed',[primary,cleanup])
  raise
 finally:(out/'root-process-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
if primary is not None:raise primary
if not json.loads(seal.read_text()).get('drained'):raise SystemExit('original root-test cleanup unproven')
raise SystemExit(receipt.get('exitCode',1))
