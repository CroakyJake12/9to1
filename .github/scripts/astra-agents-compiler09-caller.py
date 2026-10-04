"""Original isolated compiler child; unchanged maintained caller and strict inner custody."""
import hashlib,json,os,pathlib,re,signal,stat,subprocess,sys,time
from astra_mail_original_caller_session import MailCallerSession
P=pathlib.Path
def digest(path):return hashlib.sha256(P(path).read_bytes()).hexdigest()
def add(errors,error):
 if not any(error is prior for prior in errors):errors.append(error)
def collect(errors,operation):
 try:return operation()
 except BaseException as error:add(errors,error);return None
def fail(primary,errors):
 actual=[]
 for error in ([primary] if primary is not None else [])+errors:add(actual,error)
 if len(actual)>1:raise BaseExceptionGroup('Original SDK child and independent custody failures',actual)
 if actual:raise actual[0]
class JointCallerSession(MailCallerSession):
 def native_receipt(self,pid,expected):
  root=self.nativeRecords
  candidates=list(root.glob('*/'+str(pid)+'-pending.json'))
  if root.is_symlink() or len(candidates)!=1 or candidates[0].parent.is_symlink():return None
  if not re.fullmatch('[a-zA-Z0-9_.-]+',candidates[0].parent.name):return None
  try:
   self.nativeRecords=candidates[0].parent
   return super().native_receipt(pid,expected)
  finally:self.nativeRecords=root
 def inner_drained(self):
  # Preserve every original observed kernel birth/member disappearance fence.
  if not super().inner_drained():return False
  root=self.nativeRecords
  try:
   if root.is_symlink() or not root.is_dir():return False
   directories=list(root.iterdir())
   if not directories:return False
   for records in directories:
    if records.is_symlink() or not records.is_dir() or not re.fullmatch('[a-zA-Z0-9_.-]+',records.name):return False
    marker_path=records/'expected-managed-launch.json'
    if marker_path.is_symlink() or not marker_path.is_file() or marker_path.stat().st_size>1024*1024:return False
    marker=json.loads(marker_path.read_text())
    if not isinstance(marker,dict) or marker.get('expectedManagedLaunch') is not True or marker.get('drained') is not True:return False
    pid=marker.get('launcherPid');birth=marker.get('launcherStartTicks')
    if type(pid) is not int or pid<=0 or not isinstance(birth,str) or not birth.isdigit():return False
    pending=records/(str(pid)+'-pending.json')
    entries=list(records.iterdir())
    if any(p.is_symlink() or not p.is_file() for p in entries) or {p.name for p in entries}!={'expected-managed-launch.json',pending.name}:return False
    if not pending.is_file() or pending.stat().st_size>1024*1024:return False
    row=json.loads(pending.read_text())
    if not isinstance(row,dict):return False
    expected=row.get('launcherTuple')
    if not isinstance(expected,list) or len(expected)!=5 or type(expected[0]) is not int or expected[0]!=self.root or expected[1:4]!=[pid,pid,birth]:return False
    receipt=self.native_receipt(pid,expected)
    if receipt is None or receipt.get('drained') is not True:return False
    for member,value in receipt['observedMembers'].items():
     if not member.isdigit() or int(member)<=0 or not isinstance(value,list) or len(value)!=5 or value[1:3]!=[pid,pid] or not isinstance(value[3],str) or not value[3].isdigit():return False
     actual=self.stat(int(member))
     if actual is not None and actual[3]==value[3]:return False
   return True
  except (OSError,ValueError,TypeError,AttributeError):return False
 def write(self,drained):
  # The unchanged owner first records its original outer and inner proofs.
  super().write(drained)
  if type(drained) is not bool:raise RuntimeError('Original caller drain argument must be boolean')
  def signature(info):
   return (info.st_dev,info.st_ino,info.st_mode,info.st_size,info.st_mtime_ns,info.st_ctime_ns)
  def original_record(path):
   info=path.lstat()
   if not stat.S_ISREG(info.st_mode) or info.st_size>1024*1024:raise RuntimeError('Original caller record must be bounded and regular')
   data=path.read_bytes()
   if signature(path.lstat())!=signature(info) or len(data)!=info.st_size:raise RuntimeError('Original caller record changed during admission')
   row=json.loads(data)
   if not isinstance(row,dict):raise RuntimeError('Original caller record object required')
   return info,row
  evidence=self.records/'inner-native-delegation.json'
  info,state=original_record(evidence)
  _,pending=original_record(self.file)
  _,marker=original_record(self.records/'expected-managed-launch.json')
  attempts=json.loads(json.dumps(self.innerAttempts,sort_keys=True))
  members=json.loads(json.dumps(self.members,sort_keys=True))
  expected_pending={'launcherPid':self.root,'launcherTuple':list(self.original),'observedMembers':members,'drained':drained}
  expected_marker={'expectedManagedLaunch':True,'drained':True,'launcherPid':self.root,'launcherStartTicks':self.original[3]}
  if pending!=expected_pending or type(pending.get('drained')) is not bool:raise RuntimeError('Same original outer caller proof differs')
  if marker.get('expectedManagedLaunch') is not True or type(marker.get('drained')) is not bool:raise RuntimeError('Original outer launch marker refused')
  if set(marker) not in ({'expectedManagedLaunch','drained'},set(expected_marker)):raise RuntimeError('Original outer launch marker fields differ')
  if 'launcherPid' in marker and (type(marker['launcherPid']) is not int or marker['launcherPid']!=self.root or marker['launcherStartTicks']!=self.original[3]):raise RuntimeError('Original outer launch identity differs')
  if drained and marker!=expected_marker:raise RuntimeError('Same original final outer drain not proven')
  if set(state)!={'observedInnerAttempts','allInnerRecordedDrainsProven','qualification'} or state['observedInnerAttempts']!=attempts or type(state['allInnerRecordedDrainsProven']) is not bool or state['qualification']!='Sampled original descendant/kernel birth identity and exact inner owner records; no atomic/unobserved-escape or production authority claim':raise RuntimeError('Same original inner delegation snapshot differs')
  proven=state['allInnerRecordedDrainsProven']
  if drained and not proven:raise RuntimeError('Same original final inner drain not proven')
  state['drained']=drained and proven
  data=json.dumps(state,sort_keys=True)+'\n'
  handle=fd=None;primary=None;cleanup=[]
  try:
   fd=os.open(evidence,os.O_WRONLY|os.O_NOFOLLOW)
   if signature(os.fstat(fd))!=signature(info):raise RuntimeError('Same original delegation inode/content changed before rewrite')
   handle=os.fdopen(fd,'w');fd=None
   handle.seek(0);handle.truncate(0)
   if handle.write(data)!=len(data):raise RuntimeError('Original delegation complete write refused')
   handle.flush();os.fsync(handle.fileno())
  except BaseException as error:primary=error
  finally:
   if handle is not None:collect(cleanup,handle.close)
   if fd is not None:collect(cleanup,lambda:os.close(fd))
  fail(primary,cleanup)
  actual=evidence.lstat()
  if (actual.st_dev,actual.st_ino,actual.st_mode)!=(info.st_dev,info.st_ino,info.st_mode) or evidence.read_bytes()!=data.encode():raise RuntimeError('Same original delegation readback differs')
def run_sdk(root,out,records,expected_commit,manifest,manifest_sha,configuration,compiler_branch,comparison,comparison_root):
 lane=comparison+'-'+compiler_branch+'-'+configuration.lower()
 if configuration not in ('Debug','Release') or compiler_branch not in ('v2','v3') or comparison not in ('baseline','corrected'):raise RuntimeError('Exact compiler lane required')
 root=P(root);out=P(out);records=P(records);cut_path=root/manifest
 if manifest!='.github/validation/astra-desktop-visible-cut.json' or digest(cut_path)!=manifest_sha:
  raise RuntimeError('Original exact issued cut required')
 pins={row['path']:row for row in json.loads(cut_path.read_text())['files']}
 guarded=['.github/scripts/astra-agents-compiler09-caller.py','.github/validation/astra-agents-compiler09-sources.json',
  '.github/scripts/astra-agents-compiler09-original.py','.github/scripts/astra_mail_original_caller_session.py',
  '.github/scripts/astra_original_native_session_drain.py']
 def current():
  if digest(cut_path)!=manifest_sha:raise RuntimeError('Original cut changed')
  for relative in guarded:
   path=root/relative
   if path.is_symlink() or not path.is_file() or digest(path)!=pins.get(relative,{}).get('sha256'):
    raise RuntimeError('Original wrapper/custody/helper source changed: '+relative)
 current()
 argv=[sys.executable,'.github/scripts/astra-agents-compiler09-original.py','--expected-commit',expected_commit,
  '--manifest',manifest,'--manifest-sha',manifest_sha,'--configuration',configuration,'--compiler-branch',compiler_branch,
  '--comparison',comparison,'--comparison-root',str(comparison_root)]
 process=session=output=creator=read_fd=write_fd=None;primary=None;cleanup=[];started=time.monotonic()
 try:
  output=(out/'original-sdk-caller.log').open('xb')
  read_fd,write_fd=os.pipe()
  gate="import os,sys;fd=int(sys.argv[1]);token=os.read(fd,1);os.close(fd);assert token==b'G';os.execv(sys.argv[2],sys.argv[2:])"
  process=subprocess.Popen([sys.executable,'-I','-c',gate,str(read_fd),*argv],cwd=root,stdout=output,stderr=subprocess.STDOUT,
   start_new_session=True,pass_fds=(read_fd,))
  creator=os.pidfd_open(process.pid,0)
  session=JointCallerSession.__new__(JointCallerSession)
  JointCallerSession.__init__(session,process,records,root/'artifacts/agents-compiler-verification'/lane/'original-process-drains')
  os.close(read_fd);read_fd=None
  if os.write(write_fd,b'G')!=1:raise RuntimeError('Original SDK caller guarded exec refused')
  os.close(write_fd);write_fd=None
  while process.poll() is None:
   session.observe();current()
   if (out/'original-sdk-caller.log').stat().st_size>16*1024*1024:raise RuntimeError('Original SDK caller log bound')
   if time.monotonic()-started>=10800:raise TimeoutError('Original complete SDK child deadline exceeded')
   time.sleep(.04)
  if process.wait()!=0:raise RuntimeError('Original whole SDK child failed')
 except BaseException as error:primary=error
 finally:
  if output is not None:collect(cleanup,output.close)
  for fd in (read_fd,write_fd):
   if fd is not None:collect(cleanup,lambda fd=fd:os.close(fd))
  if session is not None:collect(cleanup,session.drain)
  elif process is not None:add(cleanup,RuntimeError('Original SDK child capture incomplete; false seal retained'))
  if creator is not None:
   def kill_original():
    try:signal.pidfd_send_signal(creator,signal.SIGKILL)
    except ProcessLookupError:pass
   collect(cleanup,kill_original)
  elif process is not None:collect(cleanup,process.kill)
  if process is not None:collect(cleanup,lambda:process.wait(timeout=10))
  if creator is not None:collect(cleanup,lambda:os.close(creator))
  seal=collect(cleanup,lambda:json.loads((records/'expected-managed-launch.json').read_text()))
  if not isinstance(seal,dict) or seal.get('drained') is not True:
   add(cleanup,RuntimeError('Original SDK caller/inner drains unproven; retain introduced profiles'))
  collect(cleanup,current)
  evidence={'expectedCommit':expected_commit,'issuedCutSha256':manifest_sha,'argv':argv,
   'exitCode':None if process is None else process.returncode,'originalPidfdCaptured':creator is not None,
   'originalSessionCaptured':session is not None,'seal':seal,'primaryType':None if primary is None else type(primary).__name__,
   'cleanupTypes':[type(error).__name__ for error in cleanup],
   'qualification':'Original isolated compiler caller/inner birth, tuple and disappearance seals; no tests, full SDK, native, installed or authority acceptance.'}
  data=(json.dumps(evidence,sort_keys=True,indent=2)+'\n').encode()
  handle=None;write_primary=None;write_cleanup=[]
  try:
   handle=(out/'original-sdk-caller-custody.json').open('xb');handle.write(data);handle.flush();os.fsync(handle.fileno())
  except BaseException as error:write_primary=error
  finally:
   if handle is not None:collect(write_cleanup,handle.close)
  if write_primary is not None:add(cleanup,write_primary)
  for error in write_cleanup:add(cleanup,error)
 fail(primary,cleanup)
 return 0

if __name__=='__main__':
 import argparse
 parser=argparse.ArgumentParser()
 for flag in ('expected-commit','manifest','manifest-sha','configuration','compiler-branch','comparison','comparison-root'):parser.add_argument('--'+flag,required=True)
 args=parser.parse_args()
 if os.environ.get('GITHUB_REPOSITORY')!='CroakyJake12/9to1' or os.environ.get('GITHUB_EVENT_NAME')!='workflow_dispatch' or os.environ.get('GITHUB_REF')!='refs/heads/validation/astra-agents-compiler09-isolated-original-20261004':raise RuntimeError('Exact isolated compiler workflow required')
 root=P.cwd().resolve()
 lane=args.comparison+'-'+args.compiler_branch+'-'+args.configuration.lower()
 if not re.fullmatch('(baseline-v2|corrected-v[23])-(debug|release)',lane):raise RuntimeError('Exact six compiler lanes required')
 out=root/'artifacts/agents-compiler-verification'/(lane+'-caller')
 if out.exists() or out.is_symlink():raise RuntimeError('Fresh original compiler caller evidence required')
 out.mkdir(mode=0o700,parents=True)
 records=out/'original-session';records.mkdir(mode=0o700)
 # Keep the original conservative false marker before the guarded child can exist.
 marker={'expectedManagedLaunch':True,'drained':False,'classification':'Original isolated compiler child, never a test/native pass'}
 handle=None;primary=None;cleanup=[]
 try:
  handle=(records/'expected-managed-launch.json').open('xb')
  data=(json.dumps(marker,sort_keys=True)+'\n').encode('utf-8')
  if handle.write(data)!=len(data):raise RuntimeError('Original unlaunched marker fullwrite refused')
  handle.flush();os.fsync(handle.fileno())
 except BaseException as error:primary=error
 finally:
  if handle is not None:collect(cleanup,handle.close)
 fail(primary,cleanup)
 run_sdk(root,out,records,args.expected_commit,args.manifest,args.manifest_sha,args.configuration,args.compiler_branch,args.comparison,args.comparison_root)
