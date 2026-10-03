"""Real Linux child/session custody controls; no SDK or product acceptance."""
import ctypes, importlib.util, json, os, pathlib, signal, subprocess, sys, tempfile, time, unittest, shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location('original_session',ROOT/'.github/scripts/astra-original-native-session-drain.py')
MODULE=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(MODULE)
Session=MODULE.OriginalSession

def read_flag():
 value=ctypes.c_int();libc=ctypes.CDLL(None,use_errno=True)
 assert libc.prctl(37,ctypes.byref(value),0,0,0)==0
 return value.value

def keep_receipts(records,name,extra=None):
 configured=os.environ.get('ASTRA_SUBREAPER_TEST_EVIDENCE')
 if configured is None:return
 target=pathlib.Path(configured)/name
 assert not target.exists();target.parent.mkdir(parents=True,exist_ok=True)
 shutil.copytree(records,target)
 if extra is not None:(target/'negative-observation.json').write_text(json.dumps(extra,indent=2)+'\n')

def scenario(name):
 if name=='unenrolled':
  assert read_flag()==0
  try:Session.require_subreaper()
  except RuntimeError:
   configured=os.environ.get('ASTRA_SUBREAPER_TEST_EVIDENCE')
   if configured is not None:
    target=pathlib.Path(configured)/'unenrolled';target.mkdir(parents=True);(target/'negative-observation.json').write_text(json.dumps({'subreaperFlag':0,'refused':True})+'\n')
   return
  raise AssertionError('Unenrolled owner accepted')
 Session.enroll_subreaper();assert read_flag()==1
 if name=='escape':
  with tempfile.TemporaryDirectory(prefix='original-escape-') as directory:
   root=pathlib.Path(directory);records=root/'records';records.mkdir()
   (records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False}))
   readfd,writefd=os.pipe()
   code="import os,sys,pathlib;r=pathlib.Path(sys.argv[1]);pid=os.fork()\nif pid==0:os.setsid();os._exit(23)\n(r/'child').write_text(str(pid));os._exit(0)"
   guard="import os,sys;fd=int(sys.argv[1]);assert os.read(fd,1)==b'G';os.close(fd);os.execv(sys.argv[2],sys.argv[2:])"
   process=subprocess.Popen([sys.executable,'-I','-c',guard,str(readfd),sys.executable,'-I','-c',code,str(root)],start_new_session=True,pass_fds=(readfd,))
   creator=os.pidfd_open(process.pid);session=Session(process,records);os.close(readfd);os.write(writefd,b'G');os.close(writefd)
   try:
    process.wait(timeout=5);child=int((root/'child').read_text());deadline=time.monotonic()+5
    while True:
     row=Session.stat(child)
     if row and row[0]==os.getpid() and row[4]=='Z':break
     assert time.monotonic()<deadline;time.sleep(.01)
    assert child not in session.members and row[1:3]!=(session.root,session.root)
    try:session.drain()
    except RuntimeError:pass
    else:raise AssertionError('Unobserved escaped original child accepted')
    assert json.loads((records/'expected-managed-launch.json').read_text())['drained'] is False
    assert Session.stat(child)==row and not session.reaped
    keep_receipts(records,name,{'originalSealDrained':False,'escapedOwnedTuple':row,'kernelOwnerPid':os.getpid()})
    print(json.dumps({'scenario':name,'originalSealDrained':False,'escapedOwnedTuple':row}))
   finally:
    # Fixture-only cleanup, kernel-owned child via stable pidfd. The refusal above
    # remains the observed outcome; production helper never reaps this foreign SID.
    fd=os.pidfd_open(child)
    try:
     assert Session.stat(child)==row and row[0]==os.getpid() and row[4]=='Z'
     result=os.waitid(os.P_PIDFD,fd,os.WEXITED|os.WNOHANG);assert result.si_pid==child
    finally:os.close(fd);os.close(creator)
  return
 with tempfile.TemporaryDirectory(prefix='original-subreaper-') as directory:
  root=pathlib.Path(directory);records=root/'records';records.mkdir()
  (records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False}))
  readfd,writefd=os.pipe()
  code="import os,sys,time,pathlib;r=pathlib.Path(sys.argv[1]);pid=os.fork();(r/'child').write_text(str(pid)) if pid else None\nif pid==0 and sys.argv[2]=='not-owned':os._exit(23)\nwhile not (r/'release').exists():time.sleep(.01)\nos._exit(23 if pid==0 else 0)"
  guard="import os,sys;fd=int(sys.argv[1]);assert os.read(fd,1)==b'G';os.close(fd);os.execv(sys.argv[2],sys.argv[2:])"
  process=subprocess.Popen([sys.executable,'-I','-c',guard,str(readfd),sys.executable,'-I','-c',code,str(root),name],start_new_session=True,pass_fds=(readfd,))
  creator=os.pidfd_open(process.pid);session=Session.__new__(Session);foreign=None;primary=None
  try:
   Session.__init__(session,process,records);os.close(readfd);readfd=None;os.write(writefd,b'G');os.close(writefd);writefd=None
   deadline=time.monotonic()+5
   while not (root/'child').exists():
    session.observe();assert time.monotonic()<deadline;time.sleep(.01)
   child=int((root/'child').read_text());rows=session.observe();assert child in rows
   if name=='live':
    session.reap_owned_zombies(rows);assert session.stat(child) is not None and not session.reaped
   if name=='not-owned':
    deadline=time.monotonic()+5
    while session.stat(child)[4]!='Z':assert time.monotonic()<deadline;time.sleep(.01)
    rows=session.observe();assert rows[child][0]==process.pid
    session.reap_owned_zombies(rows)
    assert session.stat(child)[4]=='Z' and not session.reaped
   (root/'release').touch()
   if name=='creator':
    deadline=time.monotonic()+5
    while session.stat(process.pid)[4]!='Z':assert time.monotonic()<deadline;time.sleep(.01)
    creator_row=session.stat(process.pid);session.reap_owned_zombies({process.pid:creator_row})
    assert session.stat(process.pid)[4]=='Z' and process.pid not in session.reaped
   process.wait(timeout=5)
   deadline=time.monotonic()+5
   while True:
    rows=session.observe();row=rows.get(child)
    if row and row[0]==os.getpid() and row[4]=='Z':break
    assert time.monotonic()<deadline;time.sleep(.01)
   if name=='birth-mismatch':
    bad=dict(rows);wrong=list(row);wrong[3]='wrong-birth';bad[child]=tuple(wrong)
    try:session.reap_owned_zombies(bad)
    except RuntimeError:pass
    else:raise AssertionError('Changed birth accepted')
    assert session.stat(child)[4]=='Z' and not session.reaped
   if name=='foreign':
    foreign=subprocess.Popen([sys.executable,'-I','-c','import os;os._exit(0)'],start_new_session=True)
    deadline=time.monotonic()+5
    while session.stat(foreign.pid)[4]!='Z':assert time.monotonic()<deadline;time.sleep(.01)
    foreign_row=session.stat(foreign.pid)
    try:session.reap_owned_zombies({foreign.pid:foreign_row})
    except RuntimeError:pass
    else:raise AssertionError('Foreign session accepted')
    assert session.stat(foreign.pid)[4]=='Z' and foreign.pid not in session.reaped
    try:session.observe()
    except RuntimeError:pass
    else:raise AssertionError('Ambiguous owned foreign session accepted as complete custody')
    assert json.loads((records/'expected-managed-launch.json').read_text())['drained'] is False
    keep_receipts(records,'foreign-refusal',{'originalSealDrained':False,'foreignTuple':foreign_row,'foreignNotReaped':True})
    foreign.wait(timeout=5);foreign=None
   session.drain();seal=json.loads((records/'expected-managed-launch.json').read_text());assert seal['drained'] is True
   assert session.stat(child) is None and child in session.reaped
   receipt=session.reaped[child];assert receipt['waitStatus']==23 and receipt['waitPid']==child and receipt['ownerPid']==os.getpid()
   assert receipt['originalTuple'][1:4]==receipt['observedTuple'][1:4]
   keep_receipts(records,name)
   print(json.dumps({'scenario':name,'seal':seal,'reapedOwnedChild':receipt},sort_keys=True))
  except BaseException as error:primary=error
  finally:
   for fd in (readfd,writefd):
    if fd is not None:os.close(fd)
   (root/'release').touch()
   try:
    if process.poll() is None:signal.pidfd_send_signal(creator,signal.SIGKILL)
    process.wait(timeout=5)
    session.drain()
    if foreign is not None:foreign.wait(timeout=5)
   except BaseException as cleanup:
    if primary is not None:raise BaseExceptionGroup('Original scenario and cleanup failures',[primary,cleanup])
    raise
   finally:os.close(creator)
  if primary is not None:raise primary

class Controls(unittest.TestCase):
 def run_scenario(self,name):
  result=subprocess.run([sys.executable,'-I',__file__,'--scenario',name],capture_output=True,text=True,timeout=25)
  print(result.stdout,end='');self.assertEqual(result.returncode,0,result.stderr)
 def test_kernel_owned_original_orphan_is_reaped(self):self.run_scenario('orphan')
 def test_foreign_session_is_not_reaped(self):self.run_scenario('foreign')
 def test_changed_birth_is_not_reaped(self):self.run_scenario('birth-mismatch')
 def test_live_original_child_is_not_reaped(self):self.run_scenario('live')
 def test_original_non_owned_child_is_not_reaped(self):self.run_scenario('not-owned')
 def test_creator_remains_owned_by_popen(self):self.run_scenario('creator')
 def test_unobserved_escaped_child_refuses_custody(self):self.run_scenario('escape')
 def test_unenrolled_owner_is_refused(self):self.run_scenario('unenrolled')

if __name__=='__main__':
 if len(sys.argv)==3 and sys.argv[1]=='--scenario':scenario(sys.argv[2])
 else:unittest.main(verbosity=2)
