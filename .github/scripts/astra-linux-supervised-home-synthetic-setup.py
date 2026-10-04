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
# TEST-only functions inserted into the existing hosted synthetic setup producer.
# No arbitrary directory, recursive chmod, owner change or production trust rule.
hostedDesktopParents=('/usr/share','/usr/share/applications')
desktopParentModesPath=out/'hosted-desktop-parent-original-modes.json'
def desktop_failure(failures,error):
 if not any(original is error for original in failures):failures.append(error)
def desktop_throw(failures):
 if len(failures)==1:raise failures[0]
 if failures:raise BaseExceptionGroup('Original hosted desktop-parent operation and independent cleanup failures',failures)
def desktop_identity(value):
 return {'dev':value.st_dev,'inode':value.st_ino,'uid':value.st_uid,'gid':value.st_gid,
  'mode':value.st_mode,'size':value.st_size,'mtimeNs':value.st_mtime_ns,'ctimeNs':value.st_ctime_ns}
def desktop_same_original_directory(fd,path,original=None):
 descriptor=desktop_identity(os.fstat(fd));observed=desktop_identity(os.lstat(path))
 if descriptor!=observed or not stat.S_ISDIR(descriptor['mode']) or descriptor['uid']!=0 or descriptor['gid']!=0:
  raise RuntimeError('Exact root-owned no-follow hosted desktop-parent descriptor/path required')
 if original is not None and any(descriptor[key]!=original[key] for key in ('dev','inode','uid','gid')):
  raise RuntimeError('Original hosted desktop-parent directory identity changed; retain state')
 return descriptor
def desktop_write_original_modes(marker):
 payload=(json.dumps(marker,indent=2)+'\n').encode('utf-8')
 if len(payload)>65536:raise RuntimeError('Bounded exact hosted desktop-parent marker required')
 fd=None;directory=None;failures=[]
 try:
  fd=os.open(desktopParentModesPath,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_CLOEXEC,0o600)
  info=os.fstat(fd)
  if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_nlink!=1 or stat.S_IMODE(info.st_mode)!=0o600:
   raise RuntimeError('Fresh exact root-owned desktop-parent permission marker required')
  offset=0
  while offset<len(payload):
   count=os.write(fd,payload[offset:])
   if count<=0:raise OSError('Original permission marker write made no progress')
   offset+=count
  os.fsync(fd)
  directory=os.open(out,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
  os.fsync(directory)
 except BaseException as error:desktop_failure(failures,error)
 finally:
  for original in (directory,fd):
   if original is not None:
    try:os.close(original)
    except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures)
def protect_exact_hosted_desktop_parent_modes():
 retained=[];failures=[];proof={'status':'PENDING','runId':runId,'attempt':attempt,'entries':[]}
 try:
  for path in hostedDesktopParents:
   fd=os.open(path,0xB0800);retained.append((path,fd))
   original=desktop_same_original_directory(fd,path)
   if stat.S_IMODE(original['mode']) not in (0o755,0o777):
    raise RuntimeError('Only exact observed hosted desktop-parent modes0755 or0777 are admitted')
   proof['entries'].append({'path':path,'original':original,'originalPermissionMode':stat.S_IMODE(original['mode'])})
  # The complete immutable original permission record is fsynced BEFORE any chmod.
  desktop_write_original_modes({'schemaVersion':1,'runId':runId,'attempt':attempt,'entries':proof['entries'],
   'scope':'EXACT_HOSTED_TEST_DIRECTORY_PERMISSIONS_ONLY_NO_INSTALL_OR_RUNTIME_ACCEPTANCE'})
  for entry,(path,fd) in zip(proof['entries'],retained,strict=True):
   desktop_same_original_directory(fd,path,entry['original'])
   os.fchmod(fd,0o755);os.fsync(fd)
   actual=desktop_same_original_directory(fd,path,entry['original'])
   if stat.S_IMODE(actual['mode'])!=0o755:raise RuntimeError('Exact hosted desktop-parent protection was not applied')
   entry['protected']=actual;entry['protectedPermissionMode']=0o755
  proof['status']='EXACT_ORIGINAL_HOSTED_DESKTOP_PARENTS_PROTECTED_FOR_MANDATORY_NATIVE_TESTS'
 except BaseException as error:desktop_failure(failures,error);proof['status']='FAILED_RETAIN_ORIGINAL_PERMISSION_RECORD'
 finally:
  for path,fd in retained:
   try:os.close(fd)
   except BaseException as error:desktop_failure(failures,error)
  try:write(out/'hosted-desktop-parent-protection.json',proof)
  except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures)
def restore_exact_hosted_desktop_parent_modes_after_original_drains():
 if not os.path.lexists(desktopParentModesPath):
  if os.path.lexists(out/'hosted-desktop-parent-protection.json'):
   raise RuntimeError('Original permission marker missing after protection attempt; retain protection')
  return 'not-created-not-required'
 fd=None;failures=[];marker=None
 try:
  fd=os.open(desktopParentModesPath,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC);before=os.fstat(fd)
  if not stat.S_ISREG(before.st_mode) or before.st_uid!=0 or before.st_nlink!=1 or stat.S_IMODE(before.st_mode)!=0o600 or not 0<before.st_size<=65536:
   raise RuntimeError('Exact immutable root-owned original permission marker required')
  payload=b''
  while len(payload)<=65536:
   part=os.read(fd,min(65537-len(payload),8192))
   if not part:break
   payload+=part
  after=os.fstat(fd)
  if desktop_identity(before)!=desktop_identity(after) or len(payload)!=before.st_size:
   raise RuntimeError('Original permission marker changed while reading; retain state')
  marker=json.loads(payload)
  if marker.get('schemaVersion')!=1 or marker.get('runId')!=runId or marker.get('attempt')!=attempt or marker.get('scope')!='EXACT_HOSTED_TEST_DIRECTORY_PERMISSIONS_ONLY_NO_INSTALL_OR_RUNTIME_ACCEPTANCE' or [row.get('path') for row in marker.get('entries',[])]!=list(hostedDesktopParents):
   raise RuntimeError('Exact run and two original hosted desktop-parent paths required')
  for row in marker['entries']:
   original=row['original']
   if original['uid']!=0 or original['gid']!=0 or not stat.S_ISDIR(original['mode']) or row['originalPermissionMode']!=stat.S_IMODE(original['mode']) or row['originalPermissionMode'] not in (0o755,0o777):
    raise RuntimeError('Exact originally observed root-owner/type/permission metadata required')
 except BaseException as error:desktop_failure(failures,error)
 finally:
  if fd is not None:
   try:os.close(fd)
   except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures)
 proof={'status':'PENDING','runId':runId,'attempt':attempt,'afterOriginalDrains':True,'entries':[],
  'qualification':'Only original permission modes restored; directory inode/owner/type must match. Kernel ctime and directory content timestamps are recorded, not falsely claimed unchanged.'}
 # Restore the child first, keeping its parent protected until the last step.
 for row in reversed(marker['entries']):
  path=row['path'];fd=None;observation={'path':path,'original':row['original'],'originalPermissionMode':row['originalPermissionMode'],'restored':False};proof['entries'].append(observation)
  try:
   fd=os.open(path,0xB0800);actual=desktop_same_original_directory(fd,path,row['original']);observation['beforeRestore']=actual
   if stat.S_IMODE(actual['mode']) not in (0o755,row['originalPermissionMode']):
    raise RuntimeError('Held original hosted directory mode changed; retain state')
   os.fchmod(fd,row['originalPermissionMode']);os.fsync(fd)
   restored=desktop_same_original_directory(fd,path,row['original'])
   if stat.S_IMODE(restored['mode'])!=row['originalPermissionMode']:raise RuntimeError('Original hosted directory permission mode was not restored')
   observation['afterRestore']=restored;observation['restored']=True
  except BaseException as error:desktop_failure(failures,error);observation['failureType']=type(error).__name__[:128]
  finally:
   if fd is not None:
    try:os.close(fd)
    except BaseException as error:desktop_failure(failures,error)
 proof['status']='EXACT_ORIGINAL_DIRECTORY_PERMISSION_MODES_RESTORED_AFTER_ORIGINAL_DRAINS' if not failures else 'FAILED_RETAIN_ORIGINAL_RESTORATION_EVIDENCE'
 try:write(out/'hosted-desktop-parent-permission-restoration.json',proof)
 except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures)
 return 'original-modes-restored-after-original-drains'

def reauthenticate_original_hosted_drains_before_permission_restoration():
 # Later cleanup commands create new original records after the first drain check.
 # Reauthenticate every current original setup/root/cleanup witness before chmod.
 authorization=out/'caller-drain-authorized.json'
 if authorization.is_symlink() or not authorization.is_file() or authorization.stat().st_uid!=0 or json.loads(authorization.read_text())!={'originalCallerSampledSessionDrained':True,'runId':runId,'attempt':attempt}:
  raise RuntimeError('Caller exact original-session drain authorization absent before mode restoration')
 sealRoots=[out/'owned-session-drain',root/'artifacts/desktop-visible-owning/root-process']
 for sealRoot in sealRoots:
  if not os.path.lexists(sealRoot):continue
  if sealRoot.is_symlink() or not sealRoot.is_dir() or sealRoot.stat().st_uid!=0:
   raise RuntimeError('Original drain record directory changed; retain hosted protection')
  required=[]
  if sealRoot==out/'owned-session-drain':
   for commandRecords in sealRoot.iterdir():
    if commandRecords.is_symlink() or not commandRecords.is_dir() or commandRecords.stat().st_uid!=0:
     raise RuntimeError('Original setup/cleanup command record directory changed; retain hosted protection')
    required.append(commandRecords/'expected-managed-launch.json')
  else:required.append(sealRoot/'expected-managed-launch.json')
  for seal in required+list(sealRoot.rglob('expected-managed-launch.json')):
   if seal.is_symlink() or not seal.is_file() or seal.stat().st_uid!=0 or json.loads(seal.read_text()).get('drained') is not True:
    raise RuntimeError('Original setup/root/cleanup command witness incomplete; retain hosted protection')
  for record in sealRoot.rglob('*-pending.json'):
   if record.is_symlink() or not record.is_file() or record.stat().st_uid!=0 or json.loads(record.read_text()).get('drained') is not True:
    raise RuntimeError('Original setup/root/cleanup identity pending; retain hosted protection')

# Fresh TEST-only self-contained fixture payload. No installed actor or trust is issued.
fixtureManifestName='astra-fixture-tool-payload.json'
def fixture_write_once(path,payload,mode):
 fd=None;directory=None;failures=[]
 try:
  fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_CLOEXEC,mode)
  actual=os.fstat(fd)
  if not stat.S_ISREG(actual.st_mode) or actual.st_uid!=0 or actual.st_nlink!=1 or stat.S_IMODE(actual.st_mode)!=mode:
   raise RuntimeError('Fresh original root-owned fixture payload record required')
  offset=0
  while offset<len(payload):
   count=os.write(fd,payload[offset:])
   if count<=0:raise OSError('Original fixture payload record write made no progress')
   offset+=count
  os.fsync(fd)
  directory=os.open(path.parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC);os.fsync(directory)
 except BaseException as error:desktop_failure(failures,error)
 finally:
  for original in (directory,fd):
   if original is not None:
    try:os.close(original)
    except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures)
def fixture_file(path,rootOwned=False,captureBytes=False):
 fd=None;failures=[];result=None
 try:
  fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC|os.O_NONBLOCK);before=os.fstat(fd)
  if not stat.S_ISREG(before.st_mode) or rootOwned and (before.st_nlink!=1 or before.st_uid!=0 or before.st_mode&0o022) or captureBytes and before.st_size>2097152:
   raise RuntimeError('Exact regular no-follow fixture payload file required')
  h=hashlib.sha256();length=0;captured=[]
  while True:
   part=os.read(fd,65536)
   if not part:break
   h.update(part);length+=len(part)
   if captureBytes and length>2097152:raise RuntimeError('Bounded fixture receipt capture required')
   if captureBytes:captured.append(part)
  if desktop_identity(before)!=desktop_identity(os.fstat(fd)) or desktop_identity(before)!=desktop_identity(os.lstat(path)) or length!=before.st_size:
   raise RuntimeError('Original fixture payload file changed while hashing')
  result={'bytes':length,'sha256':h.hexdigest(),'mode':stat.S_IMODE(before.st_mode)}
  if captureBytes:result['content']=b''.join(captured)
 except BaseException as error:desktop_failure(failures,error)
 finally:
  if fd is not None:
   try:os.close(fd)
   except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures);return result
def fixture_table(directory,rootOwned=False,omitManifest=False):
 result=[]
 for path in sorted(directory.rglob('*')):
  actual=os.lstat(path)
  if stat.S_ISLNK(actual.st_mode) or not (stat.S_ISDIR(actual.st_mode) or stat.S_ISREG(actual.st_mode)):
   raise RuntimeError('Fixture payload link or special node refused')
  if rootOwned and (actual.st_uid!=0 or actual.st_mode&0o022):raise RuntimeError('Fixture payload root ownership/protection changed')
  if stat.S_ISDIR(actual.st_mode):continue
  relative=path.relative_to(directory).as_posix()
  if omitManifest and relative==fixtureManifestName:continue
  info=fixture_file(path,rootOwned);result.append({'path':relative,'bytes':info['bytes'],'sha256':info['sha256']})
 return result
def fixture_copy_file(source,destination):
 src=None;dst=None;failures=[]
 try:
  src=os.open(source,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC|os.O_NONBLOCK);before=os.fstat(src)
  if not stat.S_ISREG(before.st_mode):raise RuntimeError('Original compiled fixture source file required')
  dst=os.open(destination,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_CLOEXEC,0o600)
  if os.fstat(dst).st_uid!=0 or not stat.S_ISREG(os.fstat(dst).st_mode):raise RuntimeError('Fresh root-owned fixture copy required')
  while True:
   part=os.read(src,65536)
   if not part:break
   offset=0
   while offset<len(part):
    count=os.write(dst,part[offset:])
    if count<=0:raise OSError('Original fixture copy write made no progress')
    offset+=count
  if desktop_identity(before)!=desktop_identity(os.fstat(src)) or desktop_identity(before)!=desktop_identity(os.lstat(source)):
   raise RuntimeError('Original compiled fixture source changed during copy')
  os.fchmod(dst,0o755 if before.st_mode&0o111 else 0o644);os.fsync(dst)
 except BaseException as error:desktop_failure(failures,error)
 finally:
  for original in (dst,src):
   if original is not None:
    try:os.close(original)
    except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures)
def fixture_stage_manifest():
 ownershipPath=out/'fixture-tool-stage-ownership.json'
 if not os.path.lexists(fixtureToolsRuntime):
  if os.path.lexists(ownershipPath):raise RuntimeError('Original fixture stage disappeared; retain evidence')
  return None
 ownershipInfo=fixture_file(ownershipPath,True,True)
 if ownershipInfo['mode']!=0o600:raise RuntimeError('Original private fixture stage ownership record required')
 ownership=json.loads(ownershipInfo['content']);fd=None;failures=[];manifest=None
 try:
  if ownership.get('schemaVersion')!=1 or ownership.get('runId')!=runId or ownership.get('attempt')!=attempt or ownership.get('stageDirectory')!=fixtureToolsRuntime:
   raise RuntimeError('Exact original fixture stage run/path witness required')
  fd=os.open(fixtureToolsRuntime,0xB0800);actual=desktop_same_original_directory(fd,fixtureToolsRuntime,ownership['directoryIdentity'])
  if stat.S_IMODE(actual['mode'])!=0o755:raise RuntimeError('Original fixture stage directory must stay0755')
  path=pathlib.Path(fixtureToolsRuntime)/fixtureManifestName;info=fixture_file(path,True,True)
  if info['bytes']>2097152 or info['sha256']!=ownership['manifestSha256']:raise RuntimeError('Exact original fixture manifest bytes required')
  manifest=json.loads(info['content'])
  if manifest.get('schemaVersion')!=1 or manifest.get('runId')!=runId or manifest.get('attempt')!=attempt or manifest.get('stageDirectory')!=fixtureToolsRuntime or manifest.get('apphost')!=str(pathlib.Path(fixtureToolsRuntime)/'NineToOne.Os.Supervisor.FixtureTools'):
   raise RuntimeError('Exact source-built fixture stage identity required')
  if fixture_table(pathlib.Path(fixtureToolsRuntime),True,True)!=manifest['files']:raise RuntimeError('Original complete fixture stage payload changed')
 except BaseException as error:desktop_failure(failures,error)
 finally:
  if fd is not None:
   try:os.close(fd)
   except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures);return manifest
def stage_actual_fixture_tool(tool):
 receiptPath=root/'artifacts/desktop-visible-owning/fixture-tool-actual-published-payload.json'
 if receiptPath.is_symlink() or not receiptPath.is_file() or receiptPath.stat().st_size>2097152:raise RuntimeError('Actual bounded fixture publish receipt required')
 receiptBytes=fixture_file(receiptPath,captureBytes=True)['content'];receipt=json.loads(receiptBytes);source=tool.parent
 if source!=root/'artifacts/supervised-published/fixture-tool' or receipt['publishDir']!=str(source) or receipt['apphost']!=str(tool) or receipt['managedDLL']!=str(source/(tool.name+'.dll')) or receipt['apphostSha256']!=a.expected_tool_sha or receipt['project']!='9to1 OS/tests/NineToOne.Os.Supervisor.FixtureTools/NineToOne.Os.Supervisor.FixtureTools.csproj':
  raise RuntimeError('Same actual source-built fixture publication required')
 rows=receipt['payload']
 if not 0<len(rows)<=4096 or len({row['path'] for row in rows})!=len(rows):raise RuntimeError('Exact bounded complete fixture payload table required')
 for row in rows:
  relative=pathlib.PurePosixPath(row['path'])
  if relative.is_absolute() or any(part in ('','.','..') for part in row['path'].split('/')) or '\\' in row['path'] or row['path']==fixtureManifestName or not re.fullmatch('[0-9a-f]{64}',row['sha256']) or row['bytes']<0:
   raise RuntimeError('Exact relative fixture payload rows required')
 if fixture_table(source)!=rows or digest(tool)!=a.expected_tool_sha or digest(pathlib.Path(receipt['managedDLL']))!=receipt['managedSha256'] or digest(pathlib.Path(receipt['managedDLL']).with_suffix('.pdb'))!=receipt['pdbSha256']:
  raise RuntimeError('Original full fixture publication bytes changed before staging')
 fd=None;parents=[];failures=[];apphost=None;manifestSha=None
 try:
  for parent in ('/','/run'):
   original=os.open(parent,0xB0800);parents.append(original);metadata=desktop_same_original_directory(original,parent)
   if metadata['mode']&0o022:raise RuntimeError('Existing root-protected fixture stage parent required')
  if os.path.lexists(fixtureToolsRuntime):raise RuntimeError('Fresh unique fixture stage required')
  pathlib.Path(fixtureToolsRuntime).mkdir(mode=0o700);fd=os.open(fixtureToolsRuntime,0xB0800);original=desktop_same_original_directory(fd,fixtureToolsRuntime)
  apphost=pathlib.Path(fixtureToolsRuntime)/tool.name
  manifest={'schemaVersion':1,'runId':runId,'attempt':attempt,'stageDirectory':fixtureToolsRuntime,'apphost':str(apphost),'sourcePublishReceiptSha256':hashlib.sha256(receiptBytes).hexdigest(),'sourceApphostSha256':a.expected_tool_sha,'managedSha256':receipt['managedSha256'],'pdbSha256':receipt['pdbSha256'],'files':rows,'qualification':'Same TEST-only source-built payload; no installed actor, grant or native acceptance'}
  manifestBytes=(json.dumps(manifest,indent=2)+'\n').encode();manifestSha=hashlib.sha256(manifestBytes).hexdigest()
  if len(manifestBytes)>2097152:raise RuntimeError('Bounded original fixture stage manifest required')
  witness={'schemaVersion':1,'runId':runId,'attempt':attempt,'stageDirectory':fixtureToolsRuntime,'directoryIdentity':original,'manifestSha256':manifestSha}
  fixture_write_once(out/'fixture-tool-stage-ownership.json',(json.dumps(witness,indent=2)+'\n').encode(),0o600)
  for row in rows:
   target=pathlib.Path(fixtureToolsRuntime)/row['path'];target.parent.mkdir(parents=True,exist_ok=True,mode=0o755)
   fixture_copy_file(source/row['path'],target)
  if fixture_table(source)!=rows or hashlib.sha256(fixture_file(receiptPath,captureBytes=True)['content']).hexdigest()!=hashlib.sha256(receiptBytes).hexdigest():raise RuntimeError('Original complete fixture publication changed during staging')
  fixture_write_once(pathlib.Path(fixtureToolsRuntime)/fixtureManifestName,manifestBytes,0o644)
  os.fchmod(fd,0o755);os.fsync(fd);desktop_same_original_directory(fd,fixtureToolsRuntime,original)
  verified=fixture_stage_manifest()
  if verified!=manifest or digest(apphost)!=a.expected_tool_sha:raise RuntimeError('Same complete staged fixture publication required')
  write(out/'staged-fixture-tool-payload.json',manifest)
 except BaseException as error:desktop_failure(failures,error)
 finally:
  for original in [fd]+list(reversed(parents)):
   if original is not None:
    try:os.close(original)
    except BaseException as error:desktop_failure(failures,error)
 desktop_throw(failures);return apphost,manifestSha
def cleanup_exact_fixture_tool_stage_after_original_drains(cleanup):
 reauthenticate_original_hosted_drains_before_permission_restoration()
 manifest=fixture_stage_manifest()
 if manifest is None:return
 directory=pathlib.Path(fixtureToolsRuntime)
 for row in manifest['files']:(directory/row['path']).unlink()
 (directory/fixtureManifestName).unlink()
 for child in sorted((path for path in directory.rglob('*') if path.is_dir()),key=lambda path:len(path.parts),reverse=True):child.rmdir()
 directory.rmdir();cleanup['removed'].append(fixtureToolsRuntime)

# Fixed global-kernel policy is changed only in this exact ephemeral hosted TEST fixture.
credentialPolicyGuard=None
def hosted_credential_policy_guard():
 global credentialPolicyGuard
 if credentialPolicyGuard is None:
  source=root/'.github/scripts/astra_hosted_credential_policy_guard.py'
  captured=fixture_file(source,captureBytes=True)
  if captured['bytes']!=17597 or captured['sha256']!='c1817be52e8fa16f6280dfe7736e8887d603e2eb1e4ae0578c45ce43a9cce964':
   raise RuntimeError('Exact source-bound hosted kernel policy guard required')
  spec=importlib.util.spec_from_file_location('hosted_kernel_policy_guard',source)
  module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module
  exec(compile(captured['content'],str(source),'exec'),module.__dict__)
  credentialPolicyGuard=module.HostedCredentialPolicyGuard(root,out,runId,attempt)
 return credentialPolicyGuard

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
fixtureToolsRuntime='/run/astra-home-fixture-tools-'+runId+'-'+attempt
markerPath=out/'protected-state-ownership.json'
if a.cleanup:
 if out.is_symlink() or not out.is_dir() or out.stat().st_uid!=0 or stat.S_IMODE(out.stat().st_mode)!=0o700 or markerPath.is_symlink() or not markerPath.is_file() or markerPath.stat().st_uid!=0:raise SystemExit('Actual root-owned setup state required')
 marker=json.loads(markerPath.read_text());expected={'protectedRoots':protected,'installedPackages':installedPackages,'desktops':desktops,'user':user,'runtime':runtime,'fixtureToolDirectory':fixtureToolsRuntime,'runId':runId,'attempt':attempt}
 if any(marker.get(k)!=v for k,v in expected.items()) or marker.get('protectedRootsAbsent') is not True:raise SystemExit('Exact initial absent ownership marker required; retain state')
 cleanup={'status':'PENDING','drainRequired':True,'removed':[]};write(out/'protected-state-cleanup.json',cleanup)
 originalDrainsAuthorized=False;originalCleanupFailures=[]
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
  originalDrainsAuthorized=True
  try:entry=pwd.getpwnam(user)
  except KeyError:entry=None
  if entry is not None:
   if entry.pw_uid<=0 or entry.pw_dir!='/home/'+user or marker.get('uid')!=entry.pw_uid or marker.get('gid')!=entry.pw_gid:raise RuntimeError('Fresh exact target-user identity changed; retain state')
   run(['/usr/sbin/userdel','--remove',user],'cleanup-exact-user');cleanup['removed'].append(entry.pw_dir)
  cleanup_exact_fixture_tool_stage_after_original_drains(cleanup)
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
  if any(os.path.lexists(x) for x in protected+desktops+[runtime,fixtureToolsRuntime]):raise RuntimeError('Created protected state remains')
  cleanup['status']='ACTUAL_INITIAL_ABSENT_HOSTED_TEST_STATE_REMOVED_AFTER_ORIGINAL_CALLER_DRAIN'
 except BaseException as error:desktop_failure(originalCleanupFailures,error)
 finally:
  if originalDrainsAuthorized:
   try:
    reauthenticate_original_hosted_drains_before_permission_restoration()
    cleanup['desktopParentPermissionRestoration']=restore_exact_hosted_desktop_parent_modes_after_original_drains()
   except BaseException as error:
    desktop_failure(originalCleanupFailures,error);cleanup['desktopParentPermissionRestoration']='FAILED-RETAINED-EVIDENCE'
  elif os.path.lexists(desktopParentModesPath):
   desktop_failure(originalCleanupFailures,RuntimeError('Original task/session drains unproved; retain hosted directory protection'))
   cleanup['desktopParentPermissionRestoration']='UNPROVED-DRAINS-RETAINED-PROTECTION'
  # Independently reauthenticate CURRENT original setup/root/userdel seals
  # immediately before restoring the actual recorded global kernel value.
  try:
   if originalDrainsAuthorized:
    cleanup['hostedCredentialPolicyRestoration']=hosted_credential_policy_guard().restore_after_original_drains(reauthenticate_original_hosted_drains_before_permission_restoration)
   elif any(os.path.lexists(out/('hosted-credential-policy-'+phase+'.json')) for phase in ('attempt','original','protection')):
    raise RuntimeError('Same original task/session drains unproved; retain current hosted kernel policy')
   else:cleanup['hostedCredentialPolicyRestoration']='not-created-not-required'
  except BaseException as error:
   desktop_failure(originalCleanupFailures,error);cleanup['hostedCredentialPolicyRestoration']='FAILED-OR-UNPROVED-DRAINS-RETAINED-CURRENT-POLICY'
  try:
   write(out/'protected-state-cleanup.json',cleanup)
   retained=root/'artifacts/desktop-visible-owning/synthetic-setup-public-proof'
   if retained.exists():raise RuntimeError('Fresh retained setup proof required')
   shutil.copytree(out,retained)
   for item in retained.rglob('*'):
    if item.is_symlink():raise RuntimeError('Setup public proof contains link')
    os.chmod(item,0o755 if item.is_dir() else 0o644)
   os.chmod(retained,0o755)
  except BaseException as error:desktop_failure(originalCleanupFailures,error)
  desktop_throw(originalCleanupFailures)
 sys.exit(0)
if out.exists():raise SystemExit('Fresh isolated setup output required')
out.mkdir(mode=0o700)
if any(os.path.lexists(x) for x in protected+desktops+[runtime,fixtureToolsRuntime]):raise SystemExit('Preexisting protected installation refuses setup without mutation')
try:pwd.getpwnam(user)
except KeyError:pass
else:raise SystemExit('Preexisting target account refuses setup')
marker={'protectedRoots':protected,'installedPackages':installedPackages,'desktops':desktops,'user':user,'runtime':runtime,'fixtureToolDirectory':fixtureToolsRuntime,'runId':runId,'attempt':attempt,'protectedRootsAbsent':True,'uid':None,'gid':None,'completed':False};write(markerPath,marker)
publish=pathlib.Path(a.publish).resolve();tool=pathlib.Path(a.fixture_tool).resolve();native=pathlib.Path(a.native_helper).resolve();product=publish/'NineToOne.Os.Shell'
for path,expected in [(product,a.expected_product_sha),(tool,a.expected_tool_sha),(native,a.expected_helper_sha)]:
 if not path.is_relative_to(root/'artifacts') or not re.fullmatch('[0-9a-f]{64}',expected or '') or path.is_symlink() or not path.is_file() or digest(path)!=expected:raise SystemExit('Exact fresh compiled artifact pin mismatch')
if native.name!='atomic-spawn' or len(list(native.parent.iterdir()))!=1 or native.read_bytes()[:4]!=b'\x7fELF':raise SystemExit('Sole actual freshly compiled helper ELF required')
hosted_credential_policy_guard().protect()
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
# TEST-only observation. These fixed paths are the three maintained first-install
# directory phases; opening them never grants trust or prepares/chmods any path.
def observe_exact_enrollment_directory_protection():
 import ctypes
 phases=[('signed-install-parent','/usr/lib/9to1/apps'),('signed-desktop-parent','/usr/share/applications'),('installed-receipts','/var/lib/9to1/home/installed-receipts')]
 observations={'status':'DIAGNOSTIC_ONLY_NOT_INSTALL_OR_RUNTIME_ACCEPTANCE','runId':runId,'attempt':attempt,
  'actor':{'pid':os.getpid(),'ppid':os.getppid(),'uid':os.getuid(),'euid':os.geteuid(),'gid':os.getgid(),'egid':os.getegid(),
   'session':os.getsid(0),'processGroup':os.getpgrp()},'openFlags':0x20000|0x80000|0x800|0x10000,
  'statxFlags':0x1000|0x100,'statxRequestedMask':0x7ff,'statxRequiredMask':0x3cb,'phases':[],
  'qualification':'Read-only fixed-path descriptor observations only. Original maintained installer decides protection and may refuse independently.'}
 try:
  libc=ctypes.CDLL(None,use_errno=True);statx=libc.statx
  statx.argtypes=[ctypes.c_int,ctypes.c_char_p,ctypes.c_int,ctypes.c_uint,ctypes.c_void_p];statx.restype=ctypes.c_int
 except Exception as error:
  observations['statxBindingFailureType']=type(error).__name__[:128];statx=None
 def identity(value):
  return {'dev':value.st_dev,'inode':value.st_ino,'uid':value.st_uid,'gid':value.st_gid,'mode':value.st_mode,
   'size':value.st_size,'ctimeNs':value.st_ctime_ns,'mtimeNs':value.st_mtime_ns}
 def path_identity(path):
  try:return {'identity':identity(os.lstat(path))}
  except OSError as error:return {'errno':error.errno}
 def observe(path):
  row={'path':path,'pathBefore':path_identity(path),'fdOpened':False,'descriptorCloseAttempted':False,'descriptorCloseReturned':False};fd=None
  try:
   fd=os.open(path,observations['openFlags']);row['fdOpened']=True;row['fd']=fd
   row['descriptorBefore']=identity(os.fstat(fd))
   if statx is not None:
    buffer=ctypes.create_string_buffer(256);ctypes.set_errno(0)
    result=statx(fd,b'',observations['statxFlags'],observations['statxRequestedMask'],buffer)
    row['statxReturn']=result;row['statxErrno']=ctypes.get_errno() if result else 0
    if result==0:
     raw=buffer.raw;number=lambda offset,size:int.from_bytes(raw[offset:offset+size],sys.byteorder)
     data={'mask':number(0,4),'uid':number(20,4),'mode':number(28,2),'inode':number(32,8),'size':number(40,8),
      'deviceMajor':number(136,4),'deviceMinor':number(140,4)};row['statx']=data
     row['sameProductionDirectoryPredicates']={'requiredMask':data['mask']&0x3cb==0x3cb,'rootOwner':data['uid']==0,
      'noGroupOrOtherWrite':data['mode']&0x12==0,'directoryType':data['mode']&0xf000==0x4000}
     observed=row['descriptorBefore'];row['statxAndOriginalDescriptorIdentityAgree']=data['inode']==observed['inode'] and data['size']==observed['size'] and data['uid']==observed['uid'] and data['mode']==observed['mode'] and data['deviceMajor']==os.major(observed['dev']) and data['deviceMinor']==os.minor(observed['dev'])
   row['descriptorAfter']=identity(os.fstat(fd));row['sameOriginalDescriptorBeforeAfter']=row['descriptorBefore']==row['descriptorAfter']
   row['pathAfter']=path_identity(path)
   row['sameOriginalPathAndDescriptorIdentity']=row['pathBefore'].get('identity')==row['descriptorBefore'] and row['pathAfter'].get('identity')==row['descriptorAfter']
  except OSError as error:row['observationErrno']=error.errno;row['observationFailureType']=type(error).__name__[:128]
  except Exception as error:row['observationFailureType']=type(error).__name__[:128]
  finally:
   if fd is not None:
    row['descriptorCloseAttempted']=True
    try:os.close(fd);row['descriptorCloseReturned']=True
    except OSError as error:row['descriptorCloseErrno']=error.errno;row['descriptorCloseFailureType']=type(error).__name__[:128]
    except Exception as error:row['descriptorCloseFailureType']=type(error).__name__[:128]
  return row
 for name,target in phases:
  parents=[];current=pathlib.Path(target).parent
  while True:
   parents.append(str(current))
   if current==current.parent:break
   current=current.parent
   if len(parents)>=64:raise RuntimeError('Fixed diagnostic path depth exceeded')
  order=parents+[target]
  observations['phases'].append({'phase':name,'target':target,'directoryImmutableCheckOrder':order,'observations':[observe(path) for path in order]})
 if len(json.dumps(observations).encode('utf-8'))>65536:raise RuntimeError('Fixed directory observation bound exceeded')
 write(out/'protected-directory-before-enrollment.json',observations)
protect_exact_hosted_desktop_parent_modes()
observe_exact_enrollment_directory_protection()
for package in packages:run([str(product),'--native-enroll-first-install',package['payload'],package['envelope'],package['desktop']],'maintained-first-install-'+package['label'])
if digest('/etc/9to1/identity/publishers.json')!=digest(trustFile):raise RuntimeError('All three protected policies changed during enrollment')
profileTool,profileManifestSha=stage_actual_fixture_tool(tool)
run(['/usr/sbin/useradd','--create-home','--user-group','--shell','/usr/sbin/nologin',user],'fresh-target-user');entry=pwd.getpwnam(user);home=pathlib.Path(entry.pw_dir).resolve()
if entry.pw_uid==0 or entry.pw_gid==0 or home!=pathlib.Path('/home/'+user) or not home.is_dir():raise RuntimeError('Actual fresh nonroot target required')
marker.update(uid=entry.pw_uid,gid=entry.pw_gid);write(markerPath,marker)
clean=['/usr/bin/setpriv','--reuid='+str(entry.pw_uid),'--regid='+str(entry.pw_gid),'--clear-groups','--no-new-privs','--','/usr/bin/env','-i','HOME='+str(home),'PATH=/usr/bin:/bin']
run(clean+['/usr/bin/mkdir','-p',str(home/'.local/share'),str(home/'.config')],'target-user-xdg-directories')
if fixture_stage_manifest()['sourceApphostSha256']!=a.expected_tool_sha:raise RuntimeError('Same complete staged fixture payload required immediately before actual target user invocation')
observed=run(clean+['DOTNET_EnableDiagnostics=0',str(profileTool),'--observe-home-state-path',str(entry.pw_uid)],'actual-target-user-home-path')
pathEvidence=json.loads(observed);state=pathlib.Path(pathEvidence['homeStatePath'])
if pathEvidence.get('code')!='ActualTargetUserHomePathObserved' or pathEvidence.get('observedPrincipal')!='unix-euid:'+str(entry.pw_uid) or any(pathEvidence.get(k) is not False for k in ['wroteHome','grantedActor','installedAccepted']) or not state.is_absolute() or not state.is_relative_to(home):raise RuntimeError('Actual maintained target user no-write Home path observation required')
run(['/usr/bin/install','-d','-o','root','-g','root','-m','0755',runtime],'root-immutable-runtime-directory')
marker['completed']=True;write(markerPath,marker)
receipt={'status':'ISOLATED_SYNTHETIC_TEST_ISSUER_SETUP_ONLY_NOT_RUNTIME_ACCEPTANCE','user':user,'uid':entry.pw_uid,'gid':entry.pw_gid,'userHome':str(home),'homeStatePath':str(state),'profileTool':str(profileTool),'fixtureToolPayloadManifestSha256':profileManifestSha,'runtimeDirectory':runtime,'productApphostSha256':digest(product),'fixtureToolApphostSha256':digest(tool),'nativeHelperSha256':digest(native),'publicTrustSha256':digest(trustFile),'packages':packages,'commands':commands,'qualification':'All three synthetic public policies and genuine maintained installed packages before frozen preparations. Runtime/UID/caps/signed/native original actor tests mandatory. No release/user publisher or existing user data accepted.'}
write(out/'setup-receipt.json',receipt)
