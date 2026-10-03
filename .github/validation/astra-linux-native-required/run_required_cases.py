import array, hashlib, json, os, select, signal, socket, struct, subprocess, tempfile, time
BIN=None
WRAPPER=None
OUTPUT=None
FMT='<IHHQ32sIIQ'
results=[]
def case(name, command, action):
 with tempfile.TemporaryDirectory(prefix='w6-spawn-',dir=str(OUTPUT)) as directory:
  path=directory+'/control';server=socket.socket(socket.AF_UNIX,socket.SOCK_SEQPACKET);server.settimeout(20);server.bind(path);server.listen(1);nonce=os.urandom(32)
  helper=None;conn=None;received=[];original=None;primary=None
  try:
   helper=subprocess.Popen(([WRAPPER] if WRAPPER else [])+[BIN,path,nonce.hex(),*command],env={'PATH':'/usr/bin:/bin','LANG':'C'},stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
   conn,_=server.accept();conn.settimeout(15)
   cred=struct.unpack('3i',conn.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12));assert cred[0]==helper.pid and cred[1]==os.getuid()
   raw,anc,flags,_=conn.recvmsg(64,socket.CMSG_SPACE(4),socket.MSG_CMSG_CLOEXEC)
   for level,kind,data in anc:
    if (level,kind)==(socket.SOL_SOCKET,socket.SCM_RIGHTS):
     fds=array.array('i');fds.frombytes(data);received.extend(fds)
   fields=struct.unpack(FMT,raw)
   assert fields[:4]==(0x31505341,1,1,1) and fields[4]==nonce and fields[5]>0 and fields[6]==0 and fields[7]>0 and not (flags & (socket.MSG_TRUNC|socket.MSG_CTRUNC))
   assert len(anc)==1 and anc[0][:2]==(socket.SOL_SOCKET,socket.SCM_RIGHTS) and len(received)==1
   original=received[0]
   PACKETS[conn]=(nonce,fields[5],fields[7],2)
   detail=action(conn,nonce,original,helper)
   assert select.select([original],[],[],15)[0], 'original pidfd did not become exited'
   rc=helper.wait(timeout=15)
   assert rc==({'eof_before_go':72,'eof_after_exec':72,'malformed_go':72,'exec_error':72,'descriptor_capture_refusal':72,'no_GO_startup_deadline':72,'supervisor_death':-9}.get(name,0))
   results.append({'name':name,'helperExit':rc,'originalPidfdExited':True,'detail':detail})
  except BaseException as failure:
   primary=failure
   raise
  finally:
   if conn is not None:PACKETS.pop(conn,None);conn.close()
   server.close()
   try:
    # EOF asks the native supervisor to drain even when descriptor validation failed.
    # A verified original descriptor remains usable if the supervisor already died.
    if original is not None and not select.select([original],[],[],0)[0]:
     try:signal.pidfd_send_signal(original,signal.SIGKILL)
     except ProcessLookupError:pass
    if helper is not None:
     cleanup_rc=helper.wait(timeout=25)
     if original is None:assert cleanup_rc in (71,72), 'unissued child cleanup unproven'
    if original is not None:assert select.select([original],[],[],1)[0], 'original child exit unproven during cleanup'
   except BaseException as cleanup_failure:
    if primary is not None:raise BaseExceptionGroup("primary test failure and original-child cleanup failure", [primary, cleanup_failure])
    raise
   finally:
    for fd in received:os.close(fd)
    if helper is not None and helper.stderr is not None:helper.stderr.close()
def send(c,n,kind,seq):c.send(struct.pack(FMT,0x31505341,1,kind,seq,n,0,0,0))
def go(c,n):send(c,n,2,1)
PACKETS={}
def recv(c):
 raw,anc,flags,_=c.recvmsg(64,socket.CMSG_SPACE(16),socket.MSG_CMSG_CLOEXEC)
 unexpected=[]
 for level,kind,data in anc:
  if (level,kind)==(socket.SOL_SOCKET,socket.SCM_RIGHTS):
   rights=array.array('i');rights.frombytes(data);unexpected.extend(rights)
 try:
  assert not anc and not flags & (socket.MSG_TRUNC|socket.MSG_CTRUNC)
  f=struct.unpack(FMT,raw);nonce,pid,ticks,seq=PACKETS[c]
  assert f[:2]==(0x31505341,1) and f[3]==(3 if f[2]==5 else seq) and f[4]==nonce and f[5]==pid and f[7]==ticks
  assert f[2] in (3,4,5)
  if f[2]==3:assert f[6]==0
  if f[2]==4:assert f[6]>0
  if f[2]==5:assert f[6]>>16 in (1,2,3) and f[6]&65535<=255
  PACKETS[c]=(nonce,pid,ticks,seq+1)
  return f
 finally:
  for fd in unexpected:os.close(fd)
def positive(c,n,fd,h):
 go(c,n);assert recv(c)[2]==3;last=recv(c);assert last[2]==5 and last[6]==(1<<16);return 'genuine exec and exact waitid exit 0'
def eof_pre(c,n,fd,h):c.close();return 'root EOF before GO'
def eof_post(c,n,fd,h):go(c,n);assert recv(c)[2]==3;c.close();return 'root EOF after exec'
def malformed(c,n,fd,h):c.send(b'bad');return 'malformed GO denied'
def exec_fail(c,n,fd,h):go(c,n);f=recv(c);assert f[2]==4 and f[6]==2;assert recv(c)[2]==5;return 'genuine ENOENT exec handshake and reap'
def stop(c,n,fd,h):go(c,n);assert recv(c)[2]==3;send(c,n,6,2);assert recv(c)[2]==5;return 'authenticated STOP drains original pidfd'
def helper_dies(c,n,fd,h):
 go(c,n);assert recv(c)[2]==3;wrapper_fd=os.pidfd_open(h.pid);signal.pidfd_send_signal(wrapper_fd,signal.SIGKILL);os.close(wrapper_fd);h.wait(timeout=5);assert select.select([fd],[],[],5)[0];return 'PDEATHSIG plus root original pidfd retained'

def capture_refusal(c,n,fd,h):
 c.close();return 'root accepted descriptor then refused capture/GO; helper drained'

def healthy(c,n,fd,h):
 go(c,n);assert recv(c)[2]==3;time.sleep(11);assert not select.select([fd],[],[],0)[0] and h.poll() is None;send(c,n,6,2);assert recv(c)[2]==5;return 'healthy child alive beyond 10s startup deadline then exact STOP'

def no_go(c,n,fd,h):
 begin=time.monotonic();assert select.select([fd],[],[],13)[0];elapsed=time.monotonic()-begin;assert 8<=elapsed<=13;last=recv(c);assert last[2]==5;return {'startupNoGOSeconds':elapsed,'exactWaitidExitPacket':True}

def preexec_death(c,n,fd,h):
 pid=int(next(line.split(':',1)[1] for line in open('/proc/self/fdinfo/'+str(fd)) if line.startswith('Pid:')))
 signal.pidfd_send_signal(fd,signal.SIGSTOP)
 deadline=time.monotonic()+5
 while True:
  state=open('/proc/'+str(pid)+'/stat').read().rsplit(')',1)[1].strip().split()[0]
  if state=='T':break
  assert time.monotonic()<deadline;time.sleep(.001)
 def pipes():
  out=set()
  for f in os.listdir('/proc/'+str(h.pid)+'/fd'):
   if int(f)>=3:
    try:
     if os.readlink('/proc/'+str(h.pid)+'/fd/'+f).startswith('pipe:'):out.add(f)
    except FileNotFoundError:pass
  return out
 before=pipes();assert len(before)==2
 go(c,n)
 deadline=time.monotonic()+5
 while len(pipes())!=1:
  assert time.monotonic()<deadline;time.sleep(.001)
 assert open('/proc/'+str(pid)+'/stat').read().rsplit(')',1)[1].strip().split()[0]=='T'
 signal.pidfd_send_signal(fd,signal.SIGKILL)
 first=recv(c);last=recv(c);assert first[2]==3 and last[2]==5 and last[6]>>16==2 and last[6]&65535==9
 return {'childStoppedBeforeGO':True,'nativeReleasePipeClosedBeforeKill':True,'neverContinuedBeforeKill':True,'STARTEDWasEOFOnly':True,'exactOriginalExitSignal':9}

def escalation(c,n,fd,h):
 go(c,n);assert recv(c)[2]==3
 pid=int(next(line.split(':',1)[1] for line in open('/proc/self/fdinfo/'+str(fd)) if line.startswith('Pid:')))
 deadline=time.monotonic()+5
 while True:
  ignored=int(next(line.split(':',1)[1].strip() for line in open('/proc/'+str(pid)+'/status') if line.startswith('SigIgn:')),16)
  if ignored & (1<<(signal.SIGTERM-1)):break
  assert time.monotonic()<deadline;time.sleep(.001)
 begin=time.monotonic();send(c,n,6,2);last=recv(c);elapsed=time.monotonic()-begin
 assert last[2]==5 and last[6]>>16==2 and last[6]&65535==9 and 9<=elapsed<=15
 return {'actualTargetSIGTERMIgnoreObserved':True,'helperSIGKILLWaitidSignal':9,'elapsedSeconds':elapsed}

def nonparent(c,n,fd,h):
 go(c,n);assert recv(c)[2]==3;assert not select.select([fd],[],[],0)[0]
 try:os.waitid(os.P_PIDFD,fd,os.WEXITED|os.WNOHANG)
 except ChildProcessError as e:assert e.errno==10
 else:raise AssertionError('root incorrectly treated as child parent')
 send(c,n,6,2);last=recv(c);assert last[2]==5 and select.select([fd],[],[],5)[0]
 try:os.waitid(os.P_PIDFD,fd,os.WEXITED|os.WNOHANG)
 except ChildProcessError as e:assert e.errno==10
 else:raise AssertionError('root incorrectly claimed child reap')
 return {'rootWaitidECHILDAliveAndAfterHelperReap':True,'rootOriginalPidfdExitPoll':True,'nativeParentWaitidReceipt':True}

def exceptional_case(name, wrapper=None, emfile=False):
 import resource
 with tempfile.TemporaryDirectory(prefix='w6-special-',dir=str(OUTPUT)) as directory:
  server=socket.socket(socket.AF_UNIX,socket.SOCK_SEQPACKET);server.settimeout(20)
  path=directory+'/control';server.bind(path);server.listen(1);nonce=os.urandom(32)
  helper=None;conn=None;fill=[];rights=[];primary=None;old=resource.getrlimit(resource.RLIMIT_NOFILE)
  try:
   helper=subprocess.Popen(([wrapper] if wrapper else [])+[BIN,path,nonce.hex(),'/usr/bin/sleep','60'],env={'PATH':'/usr/bin:/bin','LANG':'C'},stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   conn,_=server.accept();conn.settimeout(15)
   assert struct.unpack('3i',conn.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))[:2]==(helper.pid,os.getuid())
   begin=time.monotonic()
   if emfile:
    resource.setrlimit(resource.RLIMIT_NOFILE,(32,old[1]))
    while True:
     try:fill.append(os.open('/dev/null',os.O_RDONLY|os.O_CLOEXEC))
     except OSError as e:assert e.errno==24;break
   raw,anc,flags,_=conn.recvmsg(64,socket.CMSG_SPACE(4),socket.MSG_CMSG_CLOEXEC)
   for level,kind,data in anc:
    if (level,kind)==(socket.SOL_SOCKET,socket.SCM_RIGHTS):
     fds=array.array('i');fds.frombytes(data);rights.extend(fds)
   f=struct.unpack(FMT,raw)
   assert f[:2]==(0x31505341,1) and f[4]==nonce and not flags & socket.MSG_TRUNC
   if emfile:
    assert f[2:4]==(1,1) and f[5]>0 and f[6]==0 and f[7]>0 and flags & socket.MSG_CTRUNC and not anc
    # Restore this test process's limit before any receipt or cleanup work.
    resource.setrlimit(resource.RLIMIT_NOFILE,old)
    for fd in fill:os.close(fd)
    fill=[]
    PACKETS[conn]=(nonce,f[5],f[7],3)
    last=recv(conn);assert last[2]==5
    assert helper.wait(timeout=5)==72
    results.append({'name':name,'helperExit':72,'nativeOriginalWaitidExitReceipt':True,'receivedDescriptors':0,'msgCtrunc':True,'elapsedSeconds':time.monotonic()-begin})
   else:
    assert f[2:4]==(4,2) and f[5]==0 and f[6]==38 and f[7]==0 and not anc and not flags & (socket.MSG_TRUNC|socket.MSG_CTRUNC)
    assert helper.wait(timeout=5)==71
    results.append({'name':name,'helperExit':71,'noPidfdOrChildIssued':True})
  except BaseException as failure:primary=failure;raise
  finally:
   cleanup=[]
   try:resource.setrlimit(resource.RLIMIT_NOFILE,old)
   except BaseException as e:cleanup.append(e)
   for fd in fill+rights:
    try:os.close(fd)
    except BaseException as e:cleanup.append(e)
   if conn is not None:PACKETS.pop(conn,None);conn.close()
   server.close()
   try:
    if helper is not None:
     rc=helper.wait(timeout=25)
     assert rc in (71,72), 'special-case native child drain not proven'
   except BaseException as e:cleanup.append(e)
   if cleanup:
    raise BaseExceptionGroup('special case primary/drain failure',([primary] if primary else [])+cleanup)

def main():
 import argparse,platform
 from pathlib import Path
 p=argparse.ArgumentParser()
 p.add_argument('--helper',required=True);p.add_argument('--helper-source',required=True)
 p.add_argument('--helper-source-sha256',required=True);p.add_argument('--helper-sha256',required=True)
 p.add_argument('--compiler',required=True);p.add_argument('--output',required=True)
 args=p.parse_args()
 assert os.getuid()==0,'fresh authorized Ubuntu root lane required; no provisioning performed'
 def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
 global BIN,OUTPUT,WRAPPER
 BIN=str(Path(args.helper).resolve(strict=True));source=Path(args.helper_source).resolve(strict=True)
 assert sha(source)==args.helper_source_sha256=='7b0568a2217319bf9dc9988af94b3c7795db5be2bbe4491dcffdab84c9ca697a'
 assert sha(BIN)==args.helper_sha256 and Path(BIN).read_bytes()[:4]==b'\x7fELF'
 compiler=Path(args.compiler).resolve(strict=True);assert compiler.is_absolute() and compiler.is_file()
 OUTPUT=Path(args.output).resolve();OUTPUT.mkdir(parents=True,exist_ok=False)
 own=Path(__file__).resolve().parent
 provenance={'status':'PENDING','helper':BIN,'helperSha256':args.helper_sha256,'nativeSourceSha256':args.helper_source_sha256,'kernel':platform.uname()._asdict(),'compiler':str(compiler),'compilerSha256':sha(compiler),'compilerVersion':subprocess.check_output([str(compiler),'--version'],text=True),'fixtures':[]}
 (OUTPUT/'result.json').write_text(json.dumps(provenance,indent=2)+'\n')
 built={}
 for name in ('deny-clone3','ignore-child-signal','ignore-term'):
  src=own/(name+'.c');target=OUTPUT/name;cmd=[str(compiler),'-Wall','-Wextra','-Werror','-O2',str(src),'-o',str(target)]
  subprocess.run(cmd,check=True,timeout=60)
  built[name]=str(target);provenance['fixtures'].append({'source':str(src),'sourceSha256':sha(src),'binary':str(target),'binarySha256':sha(target),'command':cmd})
 try:
  case('exec_exit',['/usr/bin/true'],positive)
  case('eof_before_go',['/usr/bin/sleep','60'],eof_pre)
  case('eof_after_exec',['/usr/bin/sleep','60'],eof_post)
  case('malformed_go',['/usr/bin/sleep','60'],malformed)
  case('exec_error',['/nonexistent/worker6-native-target'],exec_fail)
  case('stop',['/usr/bin/sleep','60'],stop)
  case('supervisor_death',['/usr/bin/sleep','60'],helper_dies)
  case('descriptor_capture_refusal',['/usr/bin/sleep','60'],capture_refusal)
  case('healthy_lifetime',['/usr/bin/sleep','60'],healthy)
  exceptional_case('kernel_forced_ENOSYS',built['deny-clone3'])
  WRAPPER=built['ignore-child-signal']
  try:case('inherited_SIGCHLD_ignored',['/usr/bin/true'],positive)
  finally:WRAPPER=None
  case('no_GO_startup_deadline',['/usr/bin/sleep','60'],no_go)
  exceptional_case('SCM_RIGHTS_receive_EMFILE_noFD',emfile=True)
  case('preexec_death_errorpipe_EOF_is_not_exec',['/usr/bin/sleep','60'],preexec_death)
  case('actual_TERM_ignored_then_pidfd_KILL',[built['ignore-term']],escalation)
  case('root_nonparent_ECHILD_but_original_pidfd_poll',['/usr/bin/sleep','60'],nonparent)
  assert len(results)==16 and len({r['name'] for r in results})==16
  assert sha(BIN)==args.helper_sha256 and sha(source)==args.helper_source_sha256
  provenance['status']='ACTUAL_FRESH_NATIVE_REQUIRED16_PASSED'
 except BaseException:
  provenance['status']='FAILED';raise
 finally:
  provenance['cases']=results
  provenance['qualification']='Direct original child protocol only; STARTED is errorpipe EOF, not exec or signed authority. Root polls received pidfd and cannot reap; EMFILE has only native parent waitid proof. Same-UID supervisor death does not prove credential-drop PDEATHSIG. Separate actual signed triple/UID-drop/native session owning proof mandatory.'
  (OUTPUT/'result.json').write_text(json.dumps(provenance,indent=2)+'\n')
if __name__=='__main__':main()
