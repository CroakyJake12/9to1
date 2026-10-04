"""TEST-only global kernel policy guard for one Ubuntu24 GitHub-hosted ephemeral job.
No production launch guard changes or namespace-isolation claim. Never import with effects.
"""
import hashlib,json,os,pathlib,re,stat

POLICY='/proc/sys/fs/suid_dumpable'
WORKFLOW='.github/workflows/haven-chat-model-ci.yml'
WORKFLOW_SHA='4b75c88b207b454d48fee2c692fbd106e2c691747c97ade91b8bc1f7ce4dcc8a'
SCOPE='EXACT_UBUNTU24_GITHUB_HOSTED_EPHEMERAL_TEST_GLOBAL_KERNEL_POLICY_NOT_INSTALL_OR_RUNTIME_ACCEPTANCE'
PROOFS={'attempt','original','protection','before-root-tests','after-root-drain','restoration'}

def retain(failures,error):
 if not any(original is error for original in failures):failures.append(error)
def throw(failures):
 if len(failures)==1:raise failures[0]
 if failures:raise BaseExceptionGroup('Original TEST kernel policy operation and independent cleanup failures',failures)
def identity(value):
 return {'dev':value.st_dev,'inode':value.st_ino,'uid':value.st_uid,'gid':value.st_gid,
  'mode':value.st_mode,'nlink':value.st_nlink,'size':value.st_size,'mtimeNs':value.st_mtime_ns,'ctimeNs':value.st_ctime_ns}
def stable(value):
 keys=('dev','inode','uid','gid','mode')
 if stat.S_ISREG(value['mode']):keys+=('nlink',)
 return {key:value[key] for key in keys}
def policy_shape(first,eof):
 # Preserve the production two-byte first-read and EOF predicate, with no short-read waiver.
 if type(first) is not bytes or type(eof) is not bytes or len(first)!=2 or eof!=b'' or first not in (b'0\n',b'1\n',b'2\n'):
  raise RuntimeError('Exact bounded observed kernel policy 0,1,2 plus newline and EOF required')
 return first.decode('ascii')
def namespace_identity(path):
 value=os.stat(path) # Intentional kernel namespace magic-link target identity, never mutation.
 return {'dev':value.st_dev,'inode':value.st_ino,'label':os.readlink(path)}

class HostedCredentialPolicyGuard:
 def __init__(self,root,output,run_id,attempt):
  self.root=pathlib.Path(root);self.output=pathlib.Path(output);self.run_id=run_id;self.attempt=attempt
  if not run_id.isdigit() or not attempt.isdigit() or self.output!=pathlib.Path(os.environ['RUNNER_TEMP']).resolve()/('astra-synthetic-home-setup-'+run_id+'-'+attempt):
   raise RuntimeError('Exact existing hosted setup output and run identity required')
  if self.root!=pathlib.Path(os.environ['GITHUB_WORKSPACE']).resolve() or os.environ.get('GITHUB_RUN_ID')!=run_id or os.environ.get('GITHUB_RUN_ATTEMPT')!=attempt:
   raise RuntimeError('Same hosted root and workflow run identity required')
  self._context() # No policy mutation occurs during construction.
  fd=None;failures=[]
  try:fd=self._output_fd();self.output_identity=stable(identity(os.fstat(fd)))
  except BaseException as error:retain(failures,error)
  finally:
   if fd is not None:
    try:os.close(fd)
    except BaseException as error:retain(failures,error)
  throw(failures)
 def _checked_fixed_read(self,path,cap,proc=False,owner_root=True):
  fd=None;failures=[];payload=None
  try:
   fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC);before=identity(os.fstat(fd))
   if before!=identity(os.lstat(path)) or not stat.S_ISREG(before['mode']) or (owner_root and before['uid']!=0) or before['mode']&0o022:
    raise RuntimeError('Exact fixed-context regular file and reviewed ownership required')
   chunks=[];length=0
   while True:
    part=os.read(fd,min(8192,cap+1-length));length+=len(part)
    if length>cap:raise RuntimeError('Fixed context file bound exceeded')
    if not part:break
    chunks.append(part)
   payload=b''.join(chunks)
   after=identity(os.fstat(fd))
   if before!=after or after!=identity(os.lstat(path)):raise RuntimeError('Fixed context file changed while observed')
   if not proc and len(payload)!=before['size']:raise RuntimeError('Whole regular context file required')
  except BaseException as error:retain(failures,error)
  finally:
   if fd is not None:
    try:os.close(fd)
    except BaseException as error:retain(failures,error)
  throw(failures);return payload
 def _context(self):
  if os.name!='posix' or os.uname().sysname!='Linux' or os.getuid()!=0 or os.geteuid()!=0 or os.getgid()!=0 or os.getegid()!=0 or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('RUNNER_ENVIRONMENT')!='github-hosted' or os.environ.get('ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE')!='1':
   raise RuntimeError('Ubuntu24 GitHub-hosted ephemeral administrator TEST job required before global policy operations')
  workflow=self._checked_fixed_read(self.root/WORKFLOW,32768,owner_root=False)
  # The immutable checkout may belong to the runner; exact whole workflow bytes remain required.
  if hashlib.sha256(workflow).hexdigest()!=WORKFLOW_SHA:raise RuntimeError('Maintained exact Ubuntu24 hosted workflow required')
  release=self._checked_fixed_read('/usr/lib/os-release',4096).decode('utf-8',errors='strict')
  fields={}
  for line in release.splitlines():
   if '=' in line:
    key,value=line.split('=',1);fields[key]=value.strip('"')
  if fields.get('ID')!='ubuntu' or fields.get('VERSION_ID')!='24.04':raise RuntimeError('Actual maintained Ubuntu24 hosted image required')
  boot=self._checked_fixed_read('/proc/sys/kernel/random/boot_id',64,proc=True)
  if re.fullmatch(rb'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\n',boot) is None:
   raise RuntimeError('Exact observed hosted kernel boot identity required')
  namespaces={name:namespace_identity('/proc/self/ns/'+name) for name in ('user','pid','mnt')}
  initial_user=namespace_identity('/proc/1/ns/user')
  if namespaces['user']!=initial_user:raise RuntimeError('Same actual hosted PID1 user namespace required')
  return {'binding':{'uname':list(os.uname()),'bootId':boot.decode('ascii'),'namespaces':namespaces,
    'pid1UserNamespace':initial_user,'workflowSha256':WORKFLOW_SHA,'ubuntuVersion':'24.04'},
   'actor':{'pid':os.getpid(),'ppid':os.getppid(),'uid':os.getuid(),'euid':os.geteuid(),'gid':os.getgid(),'egid':os.getegid(),'sid':os.getsid(0),'pgrp':os.getpgrp()},
   'qualification':'The policy can affect the whole hosted kernel. Namespace identity is not isolation. Unrelated privileged writers can race observations.'}
 def _output_fd(self):
  fd=os.open(self.output,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
  try:
   value=identity(os.fstat(fd))
   if stable(value)!=stable(identity(os.lstat(self.output))) or not stat.S_ISDIR(value['mode']) or value['uid']!=0 or stat.S_IMODE(value['mode'])!=0o700:
    raise RuntimeError('Same private root-owned existing setup output required')
   if hasattr(self,'output_identity') and stable(value)!=self.output_identity:raise RuntimeError('Original setup output identity changed')
  except BaseException as primary:
   try:os.close(fd)
   except BaseException as cleanup:raise BaseExceptionGroup('Original output validation and close failed',[primary,cleanup])
   raise
  return fd
 def _name(self,phase):
  if phase not in PROOFS:raise RuntimeError('Fixed policy proof phase required')
  return 'hosted-credential-policy-'+phase+'.json'
 def _persist(self,phase,data):
  name=self._name(phase);payload=(json.dumps(data,indent=2)+'\n').encode('utf-8')
  if not 0<len(payload)<=16384:raise RuntimeError('Fixed kernel policy evidence bound exceeded')
  directory=None;fd=None;failures=[]
  try:
   directory=self._output_fd()
   fd=os.open(name,os.O_RDWR|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_CLOEXEC,0o600,dir_fd=directory)
   before=identity(os.fstat(fd))
   if not stat.S_ISREG(before['mode']) or before['uid']!=0 or before['nlink']!=1 or stat.S_IMODE(before['mode'])!=0o600:
    raise RuntimeError('Fresh exact private original policy evidence file required')
   offset=0
   while offset<len(payload):
    count=os.write(fd,payload[offset:])
    if count<=0:raise OSError('Original policy evidence write made no progress')
    offset+=count
   os.fsync(fd);os.lseek(fd,0,os.SEEK_SET);readback=b''
   while len(readback)<=16384:
    part=os.read(fd,min(8192,16385-len(readback)))
    if not part:break
    readback+=part
   after=identity(os.fstat(fd))
   if readback!=payload or after!=identity(os.stat(name,dir_fd=directory,follow_symlinks=False)) or after['size']!=len(payload) or stable(before)!=stable(after):
    raise RuntimeError('Same original policy evidence FD/path whole readback required')
   os.fsync(directory)
  except BaseException as error:retain(failures,error)
  finally:
   for original in (fd,directory):
    if original is not None:
     try:os.close(original)
     except BaseException as error:retain(failures,error)
  throw(failures)
 def _original(self):
  name=self._name('original');directory=None;fd=None;failures=[];data=None
  try:
   directory=self._output_fd()
   fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=directory);before=identity(os.fstat(fd))
   if not stat.S_ISREG(before['mode']) or before['uid']!=0 or before['nlink']!=1 or stat.S_IMODE(before['mode'])!=0o600 or not 0<before['size']<=16384:
    raise RuntimeError('Exact original immutable bounded policy marker required')
   payload=b''
   while len(payload)<=16384:
    part=os.read(fd,min(8192,16385-len(payload)))
    if not part:break
    payload+=part
   after=identity(os.fstat(fd))
   if len(payload)!=before['size'] or before!=after or after!=identity(os.stat(name,dir_fd=directory,follow_symlinks=False)):
    raise RuntimeError('Whole same-FD original policy marker required')
   data=json.loads(payload)
   if data.get('schemaVersion')!=1 or data.get('scope')!=SCOPE or data.get('runId')!=self.run_id or data.get('attempt')!=self.attempt or data.get('outputIdentity')!=self.output_identity or data.get('path')!=POLICY or data.get('originalValue') not in ('0\n','1\n','2\n'):
    raise RuntimeError('Same original host/run/fixed policy marker required')
  except BaseException as error:retain(failures,error)
  finally:
   for original in (fd,directory):
    if original is not None:
     try:os.close(original)
     except BaseException as error:retain(failures,error)
  throw(failures);return data
 def _read_policy(self,fd,row):
  first=os.read(fd,4);row['firstReadCount']=len(first);row['firstReadHex']=first.hex()
  eof=os.read(fd,1);row['eofReadCount']=len(eof);row['eofReadHex']=eof.hex()
  row['value']=policy_shape(first,eof);row['exactZeroFirstReadAndEof']=first==b'0\n' and eof==b''
  return row['value']
 def _endpoint(self,proof,expected=None,required=None,write_value=None,reauthenticate=None):
  retained=[];failures=[];read_fd=None;writer=None;proc_parent=None
  try:
   context=self._context();proof['context']=context
   if expected is not None and context['binding']!=expected['context']['binding']:raise RuntimeError('Original hosted kernel/namespace/source context changed')
   proof['directories']=[];parent=None;current=''
   for segment in ('/','proc','sys','fs'):
    current='/' if segment=='/' else current.rstrip('/')+'/'+segment
    fd=os.open(segment,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent);retained.append(fd);parent=fd
    value=identity(os.fstat(fd))
    if stable(value)!=stable(identity(os.lstat(current))) or not stat.S_ISDIR(value['mode']) or value['uid']!=0 or value['gid']!=0 or value['mode']&0o022:
     raise RuntimeError('Fixed root-owned no-follow proc sysctl directory chain required')
    proof['directories'].append({'path':current,'identity':stable(value)})
   proc_parent=parent
   read_fd=os.open('suid_dumpable',os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=proc_parent);retained.append(read_fd)
   leaf=identity(os.fstat(read_fd));proof['leaf']=stable(leaf)
   if leaf!=identity(os.stat('suid_dumpable',dir_fd=proc_parent,follow_symlinks=False)) or not stat.S_ISREG(leaf['mode']) or leaf['uid']!=0 or leaf['gid']!=0 or leaf['nlink']!=1 or leaf['mode']&0o022:
    raise RuntimeError('Exact root-owned proc sysctl descriptor/path required')
   if expected is not None and (proof['directories']!=expected['directories'] or proof['leaf']!=expected['leaf']):
    raise RuntimeError('Original proc sysctl endpoint identity changed')
   proof['before']={};value=self._read_policy(read_fd,proof['before'])
   if required is not None and value!=required:raise RuntimeError('Required recorded policy value changed; retain current state')
   if write_value is not None:
    if write_value not in ('0\n','1\n','2\n'):raise RuntimeError('Only recorded fixed kernel values admitted')
    writer=os.open('suid_dumpable',os.O_WRONLY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=proc_parent);retained.append(writer)
    if stable(identity(os.fstat(writer)))!=proof['leaf'] or identity(os.fstat(writer))!=identity(os.stat('suid_dumpable',dir_fd=proc_parent,follow_symlinks=False)):
     raise RuntimeError('Same actual proc writer FD/path required before mutation')
    os.lseek(read_fd,0,os.SEEK_SET);proof['immediatelyBeforeWrite']={}
    if self._read_policy(read_fd,proof['immediatelyBeforeWrite'])!=required:raise RuntimeError('Current policy changed before fixed write; retain state')
    if reauthenticate is not None:reauthenticate()
    proof['writeAttempted']=True
    try:
     count=os.write(writer,write_value.encode('ascii'));proof['writeCount']=count
     if count!=2:raise OSError('Exact whole sysctl write did not return two bytes')
    except BaseException as error:retain(failures,error)
    # Independently observe resulting actual kernel state even when the write raised.
    try:
     after_fd=os.open('suid_dumpable',os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=proc_parent);retained.append(after_fd)
     if stable(identity(os.fstat(after_fd)))!=proof['leaf']:raise RuntimeError('Original proc endpoint changed after write')
     proof['after']={}
     if self._read_policy(after_fd,proof['after'])!=write_value:raise RuntimeError('Actual kernel policy does not match exact required written value')
    except BaseException as error:retain(failures,error)
   if stable(identity(os.fstat(read_fd)))!=proof['leaf'] or stable(identity(os.stat('suid_dumpable',dir_fd=proc_parent,follow_symlinks=False)))!=proof['leaf']:
    raise RuntimeError('Original proc descriptor/path changed while observed')
   if self._context()['binding']!=context['binding']:raise RuntimeError('Hosted kernel context changed during policy operation')
  except BaseException as error:retain(failures,error)
  finally:
   for original in reversed(retained):
    try:os.close(original)
    except BaseException as error:retain(failures,error)
  # Procfs size/mtime/fsync are not durability evidence; only fresh exact kernel reads certify value.
  throw(failures);return proof
 def protect(self):
  self._persist('attempt',{'schemaVersion':1,'scope':SCOPE,'runId':self.run_id,'attempt':self.attempt,'policy':POLICY,'globalKernelMutationPossible':True})
  original={'schemaVersion':1,'scope':SCOPE,'runId':self.run_id,'attempt':self.attempt,
   'path':POLICY,'outputIdentity':self.output_identity}
  proof={'schemaVersion':1,'scope':SCOPE,'runId':self.run_id,'attempt':self.attempt,'path':POLICY,'status':'PENDING','originalObservation':original}
  failures=[]
  try:
   self._endpoint(original)
   original['originalValue']=original['before']['value']
   self._persist('original',original) # Complete fsynced immutable original record BEFORE any sysctl write.
   checked=self._original()
   self._endpoint(proof,checked,required=checked['originalValue'],write_value='0\n')
   proof['status']='EXACT_ZERO_KERNEL_POLICY_HELD_FOR_ORIGINAL_TEST_CREDENTIAL_JOURNEYS'
  except BaseException as error:retain(failures,error);proof['status']='FAILED_RETAIN_ORIGINAL_RECORD_AND_ACTUAL_CURRENT_POLICY'
  finally:
   try:self._persist('protection',proof)
   except BaseException as error:retain(failures,error)
  throw(failures);return proof
 def observe(self,phase):
  if phase not in ('before-root-tests','after-root-drain'):raise RuntimeError('Exact original root test observation phase required')
  original=self._original();proof={'schemaVersion':1,'scope':SCOPE,'runId':self.run_id,'attempt':self.attempt,'path':POLICY,'status':'PENDING'}
  failures=[]
  try:
   self._endpoint(proof,original,required='0\n')
   proof['status']='EXACT_ZERO_KERNEL_POLICY_OBSERVED_NOT_NATIVE_ACCEPTANCE'
  except BaseException as error:retain(failures,error);proof['status']='REFUSED_RETAIN_CURRENT_POLICY_AND_ORIGINAL_ERRORS'
  finally:
   try:self._persist(phase,proof)
   except BaseException as error:retain(failures,error)
  throw(failures);return proof
 def restore_after_original_drains(self,reauthenticate):
  original_path=self.output/self._name('original')
  if not os.path.lexists(original_path):
   if os.path.lexists(self.output/self._name('attempt')) or os.path.lexists(self.output/self._name('protection')):
    raise RuntimeError('Original policy marker missing after guard attempt; retain current policy')
   return 'not-created-not-required'
  original=self._original();proof={'schemaVersion':1,'scope':SCOPE,'runId':self.run_id,'attempt':self.attempt,'path':POLICY,'status':'PENDING','originalValue':original['originalValue']}
  failures=[]
  try:
   reauthenticate() # Actual current setup/root/userdel records, not a cancellation or timeout.
   self._endpoint(proof,original,required='0\n',write_value=original['originalValue'],reauthenticate=reauthenticate)
   proof['status']='EXACT_OBSERVED_ORIGINAL_KERNEL_POLICY_RESTORED_AFTER_SAME_KNOWN_ORIGINAL_DRAINS'
  except BaseException as error:retain(failures,error);proof['status']='FAILED_RETAIN_CURRENT_POLICY_ORIGINAL_RECORD_AND_INDEPENDENT_ERRORS'
  finally:
   try:self._persist('restoration',proof)
   except BaseException as error:retain(failures,error)
  throw(failures);return proof['status']
