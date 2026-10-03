"""Bounded Linux pidfd cleanup of an observed, privately started test session.
Caller must enroll as a Linux subreaper BEFORE launch, then enter immediately
after Popen(start_new_session=True), call observe()
while sampling, and drain() in finally. The policy wrapper checks every state
record is drained before removing introduced profiles.
"""
import os,pathlib,json,time,signal,ctypes
class OriginalSession:
 @staticmethod
 def enroll_subreaper():
  if not callable(getattr(os,'waitid',None)) or not hasattr(os,'P_PIDFD'):
   raise RuntimeError('Linux kernel-owned pidfd child wait required')
  libc=ctypes.CDLL(None,use_errno=True)
  if libc.prctl(36,1,0,0,0)!=0:raise OSError(ctypes.get_errno(),'Original child subreaper enrollment failed')
  OriginalSession.require_subreaper()
 @staticmethod
 def require_subreaper():
  value=ctypes.c_int();libc=ctypes.CDLL(None,use_errno=True)
  if libc.prctl(37,ctypes.byref(value),0,0,0)!=0:raise OSError(ctypes.get_errno(),'Original child subreaper observation failed')
  if value.value!=1:raise RuntimeError('Original child subreaper must enroll before launch')
 def __init__(self,process,records):
  self.require_subreaper();self.owner=os.getpid();self.ownerBirth=self.stat(self.owner)[3];self.reaped={}
  self.process=process;self.root=process.pid;self.records=pathlib.Path(records);self.records.mkdir(parents=True,exist_ok=True)
  self.file=self.records/(str(self.root)+'-pending.json');self.file.write_text(json.dumps({'launcherPid':self.root,'drained':False,'status':'identity capture pending'})+'\n')
  self.original=self.stat(self.root)
  if self.original is None or self.original[0]!=self.owner or self.original[1:3]!=(self.root,self.root):raise RuntimeError('Original launcher is not its own session/group')
  self.members={self.root:self.original};self.write(False)
 @staticmethod
 def stat(pid):
  try:
   s=(pathlib.Path('/proc')/str(pid)/'stat').read_text();t=s[s.rfind(')')+2:].split();return int(t[1]),int(t[2]),int(t[3]),t[19],t[0]
  except (OSError,ValueError,IndexError):return None
 def write(self,drained):
  self.file.write_text(json.dumps({'launcherPid':self.root,'launcherTuple':self.original,'observedMembers':self.members,'drained':drained,'subreaperOwnerPid':self.owner,'subreaperOwnerStartTicks':self.ownerBirth,'reapedOwnedChildren':self.reaped},sort_keys=True)+'\n')
  if drained:
   expected=self.records/'expected-managed-launch.json'
   assert expected.is_file() and json.loads(expected.read_text()).get('expectedManagedLaunch') is True
   expected.write_text(json.dumps({'expectedManagedLaunch':True,'drained':True,'launcherPid':self.root,'launcherStartTicks':self.original[3]})+'\n')
 def observe(self):
  current=self.stat(self.root)
  if current is not None and current[3]!=self.original[3]:raise RuntimeError('Original launcher PID reused')
  for pid,expected in list(self.members.items()):
   actual=self.stat(pid)
   if actual is not None and actual[3]==expected[3] and actual[1:3]!=(self.root,self.root):
    raise RuntimeError('Authenticated original descendant escaped session/group; preserving policy')
  rows={}
  for p in pathlib.Path('/proc').iterdir():
   if not p.name.isdigit():continue
   row=self.stat(int(p.name))
   if row and row[0]==self.owner and int(p.name)!=self.root and row[1:3]!=(self.root,self.root):
    raise RuntimeError('Subreaper-owned child outside original session; preserving policy')
   if row and row[2]==self.root:rows[int(p.name)]=row
   elif row and row[0] in self.members:
    parent=self.stat(row[0])
    if parent is not None and parent[3]==self.members[row[0]][3]:raise RuntimeError('Original descendant changed session; preserving policy')
  # The session cannot be adopted solely by its reused numeric ID.
  if rows and not any(pid in self.members and row[3]==self.members[pid][3] for pid,row in rows.items()):raise RuntimeError('Original session has no surviving identity anchor')
  for pid,row in rows.items():
   if row[1]!=self.root:raise RuntimeError('Original session member changed process group')
   self.members[pid]=row
  self.write(False);return rows
 def send(self,rows,sig):
  for pid,expected in rows.items():
   if expected[4]=='Z':continue
   if self.stat(pid)!=expected:continue
   try:fd=os.pidfd_open(pid,0)
   except ProcessLookupError:continue
   try:
    actual=self.stat(pid)
    if actual is None:continue
    if actual[1:4]!=expected[1:4]:raise RuntimeError('Session member identity changed before signal')
    signal.pidfd_send_signal(fd,sig,None,0)
   finally:os.close(fd)
 def reap_owned_zombies(self,rows):
  self.require_subreaper()
  if self.stat(self.owner)[3]!=self.ownerBirth:raise RuntimeError('Original subreaper owner identity changed')
  for pid,row in rows.items():
   if pid==self.root or row[4]!='Z':continue
   expected=self.members.get(pid)
   if expected is None or row[1:4]!=expected[1:4] or row[1:3]!=(self.root,self.root):
    raise RuntimeError('Unbound original zombie identity refused')
   actual=self.stat(pid)
   if actual is None:continue
   if actual[1:4]!=expected[1:4]:raise RuntimeError('Original zombie birth/session changed')
   if actual[0]!=self.owner:continue
   if actual[4]!='Z':raise RuntimeError('Original zombie observation changed')
   fd=os.pidfd_open(pid,0)
   try:
    actual=self.stat(pid)
    if actual is None or actual[0]!=self.owner or actual[1:4]!=expected[1:4] or actual[4]!='Z':
     raise RuntimeError('Kernel-owned original child identity changed before wait')
    result=os.waitid(os.P_PIDFD,fd,os.WEXITED|os.WNOHANG)
    if result is None or result.si_pid!=pid:raise RuntimeError('Kernel-owned original child reap not proven')
    self.reaped[pid]={'originalTuple':expected,'observedTuple':actual,'waitPid':result.si_pid,
      'waitCode':result.si_code,'waitStatus':result.si_status,'ownerPid':self.owner,'ownerStartTicks':self.ownerBirth}
    self.write(False)
   finally:os.close(fd)
 def drain(self):
  for sig in (signal.SIGTERM,signal.SIGKILL):
   rows=self.observe();self.reap_owned_zombies(rows);rows=self.observe();self.send(rows,sig);deadline=time.monotonic()+5
   while time.monotonic()<deadline:
    self.process.poll();rows=self.observe();self.reap_owned_zombies(rows);rows=self.observe()
    if not rows:self.write(True);return
    time.sleep(.05)
  # Zombies still count as present: fail closed rather than infer disappearance.
  raise RuntimeError('Original test session did not disappear; keep introduced policy loaded')
