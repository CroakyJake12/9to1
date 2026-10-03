"""Original guarded SDK child; maintained caller and inner session custody."""
import hashlib,json,os,pathlib,re,signal,subprocess,sys,time
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
def run_sdk(root,out,records,expected_commit,manifest,manifest_sha):
 root=P(root);out=P(out);records=P(records);cut_path=root/manifest
 if manifest!='.github/validation/astra-desktop-visible-cut.json' or digest(cut_path)!=manifest_sha:
  raise RuntimeError('Original exact issued cut required')
 pins={row['path']:row for row in json.loads(cut_path.read_text())['files']}
 guarded=['.github/scripts/astra_joint_sdk_profile_caller.py','.github/scripts/astra-joint-sdk-ephemeral-profile.py',
  '.github/scripts/astra-joint-native-sdk01-owning.py','.github/scripts/astra_mail_original_caller_session.py',
  '.github/scripts/astra_original_native_session_drain.py']
 def current():
  if digest(cut_path)!=manifest_sha:raise RuntimeError('Original cut changed')
  for relative in guarded:
   path=root/relative
   if path.is_symlink() or not path.is_file() or digest(path)!=pins.get(relative,{}).get('sha256'):
    raise RuntimeError('Original wrapper/custody/helper source changed: '+relative)
 current()
 argv=[sys.executable,'.github/scripts/astra-joint-native-sdk01-owning.py','--expected-commit',expected_commit,
  '--manifest',manifest,'--manifest-sha',manifest_sha]
 process=session=output=creator=read_fd=write_fd=None;primary=None;cleanup=[];started=time.monotonic()
 try:
  output=(out/'original-sdk-caller.log').open('xb')
  read_fd,write_fd=os.pipe()
  gate="import os,sys;fd=int(sys.argv[1]);token=os.read(fd,1);os.close(fd);assert token==b'G';os.execv(sys.argv[2],sys.argv[2:])"
  process=subprocess.Popen([sys.executable,'-I','-c',gate,str(read_fd),*argv],cwd=root,stdout=output,stderr=subprocess.STDOUT,
   start_new_session=True,pass_fds=(read_fd,))
  creator=os.pidfd_open(process.pid,0)
  session=JointCallerSession.__new__(JointCallerSession)
  JointCallerSession.__init__(session,process,records,root/'artifacts/desktop-visible-owning/joint-sdk/original-process-drains')
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
   'qualification':'Observed exact original caller/inner birth, tuple and disappearance seals; no atomic/unobserved-escape or partial-matrix/native acceptance.'}
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
