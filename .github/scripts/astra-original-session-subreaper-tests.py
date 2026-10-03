"""Real Linux custody controls with protected acquisition and independent cleanup."""
import ctypes,importlib.util,json,os,pathlib,signal,subprocess,sys,tempfile,time,unittest,shutil
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[2]
HELPER=pathlib.Path(os.environ.get('ASTRA_SUBREAPER_HELPER_PATH',str(ROOT/'.github/scripts/astra-original-native-session-drain.py')))
SPEC=importlib.util.spec_from_file_location('original_session',HELPER);MODULE=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(MODULE);Session=MODULE.OriginalSession

def collect(errors,action):
 try:action()
 except BaseException as error:errors.append(error)
def finish(primary,cleanup):
 errors=([primary] if primary is not None else [])+cleanup
 if len(errors)==1:raise errors[0]
 if errors:raise BaseExceptionGroup('Original fixture and independent cleanup failures',errors)
def flatten(error):
 return [leaf for item in error.exceptions for leaf in flatten(item)] if isinstance(error,BaseExceptionGroup) else [error]
def read_flag():
 value=ctypes.c_int();libc=ctypes.CDLL(None,use_errno=True);assert libc.prctl(37,ctypes.byref(value),0,0,0)==0;return value.value
def keep_receipts(records,name,extra=None):
 configured=os.environ.get('ASTRA_SUBREAPER_TEST_EVIDENCE')
 if configured is None:return
 target=pathlib.Path(configured)/name;assert not target.exists();target.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(records,target)
 if extra is not None:(target/'negative-observation.json').write_text(json.dumps(extra,indent=2)+'\n')
def until(action):
 deadline=time.monotonic()+5
 while True:
  result=action()
  if result:return result
  if time.monotonic()>=deadline:raise RuntimeError('Fixture condition not proven before deadline')
  time.sleep(.01)

class Fixture:
 def __init__(self,name):
  self.name=name;self.root=None;self.records=None;self.readfd=None;self.writefd=None;self.process=None;self.creator=None;self.session=None;self.ready=False;self.child=None;self.row=None;self.foreign=None;self.foreignfd=None;self.release=False;self.cleanup=[];self.primary=None;self.gone=False;self.proofPhase=False
 def close(self,field):
  fd=getattr(self,field);setattr(self,field,None)
  if fd is not None:
   os.close(fd)
   if self.name.startswith('setup-close') and field in ('readfd','creator'):os.close(fd) # actual EBADF after same-handle close
 def reap_escape(self):
  if self.child is None or self.row is None:raise RuntimeError('Escaped original fixture identity not captured; retain data')
  fd=os.pidfd_open(self.child)
  primary=None;cleanup=[]
  try:
   actual=Session.stat(self.child);assert actual==self.row and actual[0]==os.getpid() and actual[4]=='Z'
   result=os.waitid(os.P_PIDFD,fd,os.WEXITED|os.WNOHANG);assert result is not None and result.si_pid==self.child
  except BaseException as error:primary=error
  finally:collect(cleanup,lambda:os.close(fd))
  finish(primary,cleanup)
 def settle_process(self):
  if self.process is None:return
  if self.process.poll() is None and self.creator is not None:signal.pidfd_send_signal(self.creator,signal.SIGKILL)
  self.process.wait(timeout=5) # authoritative Popen child wait, not a timeout-based exit inference
 def settle_foreign(self):
  if self.foreign is None:return
  if self.foreign.poll() is None:
   if self.foreignfd is not None:signal.pidfd_send_signal(self.foreignfd,signal.SIGKILL)
  self.foreign.wait(timeout=5)
 def prove_disappearance(self):
  self.gone=False;self.proofPhase=True
  try:self.gone=self.process is not None and self.process.returncode is not None and Session.stat(self.process.pid) is None
  finally:self.proofPhase=False
 def run(self,body):
  try:
   self.root=pathlib.Path(tempfile.mkdtemp(prefix='original-custody-'));self.records=self.root/'records';self.records.mkdir()
   (self.records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
   self.readfd,self.writefd=os.pipe()
   code="import os,sys,time,pathlib;r=pathlib.Path(sys.argv[1]);pid=os.fork();(r/'child').write_text(str(pid)) if pid else None\nif pid==0 and sys.argv[2]=='escape':os.setsid();os._exit(23)\nif pid==0 and sys.argv[2]=='not-owned':os._exit(23)\nif sys.argv[2]=='escape':os._exit(0)\nwhile not (r/'release').exists():time.sleep(.01)\nos._exit(23 if pid==0 else 0)"
   guard="import os,sys;fd=int(sys.argv[1]);assert os.read(fd,1)==b'G';os.close(fd);os.execv(sys.argv[2],sys.argv[2:])"
   self.process=subprocess.Popen([sys.executable,'-I','-c',guard,str(self.readfd),sys.executable,'-I','-c',code,str(self.root),self.name],start_new_session=True,pass_fds=(self.readfd,))
   if self.name=='setup-acquire':raise RuntimeError('Injected setup failure after actual Popen before creator pidfd')
   self.creator=os.pidfd_open(self.process.pid);self.session=Session.__new__(Session);Session.__init__(self.session,self.process,self.records);self.ready=True
   self.close('readfd');os.write(self.writefd,b'G');self.release=True;self.close('writefd')
   body(self)
  except BaseException as error:self.primary=error
  finally:
   # Every original acquisition has its own collector; none can skip a later cleanup.
   collect(self.cleanup,lambda:self.close('readfd'));collect(self.cleanup,lambda:self.close('writefd'))
   if self.root is not None:collect(self.cleanup,lambda:(self.root/'release').touch())
   collect(self.cleanup,self.settle_foreign)
   collect(self.cleanup,self.settle_process)
   if self.ready:
    if self.name=='escape':collect(self.cleanup,self.reap_escape) # preserves original false SDK seal
    else:collect(self.cleanup,self.session.drain)
   collect(self.cleanup,lambda:self.close('foreignfd'));collect(self.cleanup,lambda:self.close('creator'))
   collect(self.cleanup,self.prove_disappearance)
   if self.records is not None:
    summary={'scenario':self.name,'launcherReleased':self.release,'creatorWaitProven':self.gone,'primary':repr(self.primary),'cleanup':[repr(x) for x in self.cleanup],'retainedData':str(self.root),'sessionInitialized':self.ready}
    collect(self.cleanup,lambda:keep_receipts(self.records,self.name+'-final',summary))
   # Refused/ambiguous fixture data remains available. Never delete on a mere timeout.
   if self.primary is None and not self.cleanup and self.ready and self.name!='escape' and self.gone:
    collect(self.cleanup,lambda:shutil.rmtree(self.root))
  finish(self.primary,self.cleanup)

class BodyFailure(RuntimeError):pass

def body(f):
 session=f.session
 if f.name=='escape':
  f.process.wait(timeout=5);f.child=int((f.root/'child').read_text());f.row=until(lambda: row if (row:=Session.stat(f.child)) and row[0]==os.getpid() and row[4]=='Z' else None)
  assert f.child not in session.members and f.row[1:3]!=(session.root,session.root)
  try:session.drain()
  except RuntimeError:pass
  else:raise AssertionError('Escaped original child accepted')
  assert json.loads((f.records/'expected-managed-launch.json').read_text())['drained'] is False
  assert Session.stat(f.child)==f.row and not session.reaped;keep_receipts(f.records,'escape',{'originalSealDrained':False,'escapedOwnedTuple':f.row});return
 until(lambda:(f.root/'child').exists());f.child=int((f.root/'child').read_text());rows=session.observe();assert f.child in rows
 if f.name=='live':session.reap_owned_zombies(rows);assert session.stat(f.child) is not None and not session.reaped
 if f.name=='send-close':
  real_close=os.close;original_body=BodyFailure('Original actual child signal failure')
  def failing_signal(fd,*args):real_close(fd);raise original_body
  with patch.object(signal,'pidfd_send_signal',failing_signal):
   try:session.send({f.child:rows[f.child]},signal.SIGTERM)
   except BaseException as error:errors=flatten(error)
   else:raise AssertionError('Injected signal failures lost')
  assert any(x is original_body for x in errors) and any(isinstance(x,OSError) and x.errno==9 for x in errors),'Original signal/close failure replaced'
  assert session.stat(f.child)[4]!='Z';keep_receipts(f.records,'send-close-negative',{'errors':[repr(x) for x in errors],'originalSealDrained':False})

 if f.name=='not-owned':
  until(lambda: row if (row:=session.stat(f.child)) and row[4]=='Z' else None);rows=session.observe();assert rows[f.child][0]==f.process.pid
  session.reap_owned_zombies(rows);assert session.stat(f.child)[4]=='Z' and not session.reaped
 (f.root/'release').touch()
 if f.name=='creator':
  row=until(lambda: row if (row:=session.stat(f.process.pid)) and row[4]=='Z' else None);session.reap_owned_zombies({f.process.pid:row});assert session.stat(f.process.pid)[4]=='Z' and f.process.pid not in session.reaped
 f.process.wait(timeout=5);f.row=until(lambda: row if (row:=session.observe().get(f.child)) and row[0]==os.getpid() and row[4]=='Z' else None);rows=session.observe()
 if f.name=='birth-mismatch':
  bad=dict(rows);wrong=list(f.row);wrong[3]='wrong-birth';bad[f.child]=tuple(wrong)
  try:session.reap_owned_zombies(bad)
  except RuntimeError:pass
  else:raise AssertionError('Changed birth accepted')
  assert session.stat(f.child)[4]=='Z' and not session.reaped
 if f.name=='foreign':
  f.foreign=subprocess.Popen([sys.executable,'-I','-c','import os;os._exit(0)'],start_new_session=True);f.foreignfd=os.pidfd_open(f.foreign.pid)
  foreign_row=until(lambda: row if (row:=session.stat(f.foreign.pid)) and row[4]=='Z' else None)
  try:session.reap_owned_zombies({f.foreign.pid:foreign_row})
  except RuntimeError:pass
  else:raise AssertionError('Foreign session accepted')
  assert session.stat(f.foreign.pid)[4]=='Z' and f.foreign.pid not in session.reaped
  try:session.observe()
  except RuntimeError:pass
  else:raise AssertionError('Ambiguous foreign owner accepted')
  keep_receipts(f.records,'foreign-refusal',{'originalSealDrained':False,'foreignTuple':foreign_row});f.foreign.wait(timeout=5)
 if f.name in ('wait-close','write-close'):
  real_close=os.close;real_wait=os.waitid;captured=[];original_body=BodyFailure('Original actual child '+('wait' if f.name=='wait-close' else 'receipt')+' failure')
  def failing_wait(selector,fd,flags):
   captured.append(fd)
   if f.name=='wait-close':real_close(fd);raise original_body
   return real_wait(selector,fd,flags)
  def failing_write(drained):
   if f.child in session.reaped:raise original_body
  def failing_close(fd):
   real_close(fd)
   if f.name=='write-close' and fd in captured:real_close(fd) # real same-handle EBADF
  with patch.object(os,'waitid',failing_wait),patch.object(os,'close',failing_close):
   if f.name=='write-close':
    with patch.object(session,'write',failing_write):
     try:session.reap_owned_zombies(rows)
     except BaseException as error:errors=flatten(error)
     else:raise AssertionError('Injected original failures lost')
   else:
    try:session.reap_owned_zombies(rows)
    except BaseException as error:errors=flatten(error)
    else:raise AssertionError('Injected original failures lost')
  assert any(x is original_body for x in errors) and any(isinstance(x,OSError) and x.errno==9 for x in errors),'Original body/close failure was replaced'
  keep_receipts(f.records,f.name+'-negative',{'errors':[repr(x) for x in errors],'originalSealDrained':False})
 session.drain();seal=json.loads((f.records/'expected-managed-launch.json').read_text());assert seal['drained'] is True
 assert session.stat(f.child) is None and f.child in session.reaped;receipt=session.reaped[f.child];assert receipt['waitStatus']==23 and receipt['waitPid']==f.child and receipt['ownerPid']==os.getpid()
 assert receipt['originalTuple'][1:4]==receipt['observedTuple'][1:4];keep_receipts(f.records,f.name)

def scenario(name):
 if name=='unenrolled':
  assert read_flag()==0
  try:Session.require_subreaper()
  except RuntimeError:
   configured=os.environ.get('ASTRA_SUBREAPER_TEST_EVIDENCE')
   if configured:
    p=pathlib.Path(configured)/name;p.mkdir(parents=True);(p/'negative-observation.json').write_text(json.dumps({'subreaperFlag':0,'refused':True})+'\n')
   return
  raise AssertionError('Unenrolled owner accepted')
 Session.enroll_subreaper();assert read_flag()==1;fixture=Fixture(name)
 if name in ('setup-close','setup-close-proof','setup-acquire'):
  proof_failure=BodyFailure('Injected final disappearance fact read failure');real_stat=Session.stat
  def proof_stat(pid):
   if name=='setup-close-proof' and fixture.proofPhase and pid==fixture.process.pid:
    real_stat(pid);raise proof_failure
   return real_stat(pid)
  with patch.object(Session,'stat',staticmethod(proof_stat)):
   try:fixture.run(body)
   except BaseException as error:errors=flatten(error)
   else:raise AssertionError('Setup failure not retained')
  assert fixture.process.returncode is not None and Session.stat(fixture.process.pid) is None
  if name.startswith('setup-close'):assert errors[0] is fixture.primary and len([x for x in errors if isinstance(x,OSError) and x.errno==9])==2
  else:assert any('Injected setup failure' in str(x) for x in errors)
  if name=='setup-close-proof':assert any(x is proof_failure for x in errors) and fixture.gone is False
  assert not fixture.release and fixture.root.exists();return
 fixture.run(body)

class Controls(unittest.TestCase):
 def run_scenario(self,name):
  result=subprocess.run([sys.executable,'-I',__file__,'--scenario',name],capture_output=True,text=True,timeout=25);print(result.stdout,end='');self.assertEqual(result.returncode,0,result.stderr)
 def test_kernel_owned_original_orphan_is_reaped(self):self.run_scenario('orphan')
 def test_foreign_session_is_not_reaped(self):self.run_scenario('foreign')
 def test_changed_birth_is_not_reaped(self):self.run_scenario('birth-mismatch')
 def test_live_original_child_is_not_reaped(self):self.run_scenario('live')
 def test_original_non_owned_child_is_not_reaped(self):self.run_scenario('not-owned')
 def test_creator_remains_owned_by_popen(self):self.run_scenario('creator')
 def test_unobserved_escaped_child_refuses_custody(self):self.run_scenario('escape')
 def test_unenrolled_owner_is_refused(self):self.run_scenario('unenrolled')
 def test_actual_signal_and_close_failures_both_retained(self):self.run_scenario('send-close')
 def test_actual_wait_and_close_failures_both_retained(self):self.run_scenario('wait-close')
 def test_actual_receipt_and_close_failures_both_retained(self):self.run_scenario('write-close')
 def test_actual_setup_and_creator_close_failures_both_retained(self):self.run_scenario('setup-close')
 def test_actual_final_proof_failure_retains_setup_and_close_errors(self):self.run_scenario('setup-close-proof')
 def test_actual_created_child_settled_after_acquisition_failure(self):self.run_scenario('setup-acquire')
if __name__=='__main__':
 if len(sys.argv)==3 and sys.argv[1]=='--scenario':scenario(sys.argv[2])
 else:unittest.main(verbosity=2)
