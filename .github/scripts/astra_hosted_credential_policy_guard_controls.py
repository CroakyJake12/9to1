"""Pure in-memory source-bound controls. No current kernel/procfs or hosted policy invocation.
Actual kernel, signed child, native pair and original drain acceptance remains the full hosted gate.
"""
import argparse,ast,hashlib,json,pathlib,stat,types,unittest
p=argparse.ArgumentParser();p.add_argument('--source',required=True);p.add_argument('--source-sha256',required=True);a=p.parse_args()
source=pathlib.Path(a.source).read_bytes()
if len(source)>32768 or hashlib.sha256(source).hexdigest()!=a.source_sha256:raise SystemExit('Exact bounded guard source required for inert controls')
tree=ast.parse(source)
if any(isinstance(node,(ast.Import,ast.ImportFrom)) for node in tree.body):
 tree.body=[node for node in tree.body if not isinstance(node,(ast.Import,ast.ImportFrom))]
code=compile(tree,a.source,'exec') # Top-level imports are removed; every operation uses the fake kernel below.

class FakeKernel:
 O_RDONLY=0;O_WRONLY=1;O_RDWR=2;O_DIRECTORY=65536;O_NOFOLLOW=131072;O_CLOEXEC=524288
 SEEK_SET=0
 def __init__(self,value):
  self.value=value;self.next_fd=10;self.fds={};self.opened=[];self.closed=[];self.writes=[]
  self.directory_nlink=2;self.dynamic_directory_nlink=False;self.regular_nlink=1
  self.write_error=None;self.after_error=None;self.close_error=None;self.short_write=False;self.changed_before_write=None;self.writer_admitted=False
  self.path=types.SimpleNamespace(lexists=lambda path:True)
 def open(self,path,flags,dir_fd=None):
  name=str(path)
  if name=='/':full='/'
  elif dir_fd is not None:
   parent=self.fds[dir_fd]['path'];full=parent.rstrip('/')+'/'+name
  else:full=name
  if full not in ('/','/proc','/proc/sys','/proc/sys/fs','/proc/sys/fs/suid_dumpable'):raise AssertionError('Unexpected fake path')
  if full=='/':self.writer_admitted=False
  number=self.next_fd;self.next_fd+=1
  kind='writer' if flags&self.O_WRONLY else ('after' if self.writer_admitted and full.endswith('suid_dumpable') else 'read')
  if kind=='writer':self.writer_admitted=True
  self.fds[number]={'path':full,'flags':flags,'position':0,'kind':kind};self.opened.append(number)
  return number
 def _stat(self,path):
  paths=('/','/proc','/proc/sys','/proc/sys/fs','/proc/sys/fs/suid_dumpable')
  regular=path.endswith('suid_dumpable')
  if not regular and self.dynamic_directory_nlink:self.directory_nlink+=1
  return types.SimpleNamespace(st_dev=17,st_ino=paths.index(path)+100,st_uid=0,st_gid=0,
   st_mode=(stat.S_IFREG|0o644) if path.endswith('suid_dumpable') else (stat.S_IFDIR|0o755),
   st_nlink=self.regular_nlink if regular else self.directory_nlink,st_size=0,st_mtime_ns=1,st_ctime_ns=1)
 def fstat(self,number):return self._stat(self.fds[number]['path'])
 def lstat(self,path):return self._stat(str(path))
 def stat(self,path,dir_fd=None,follow_symlinks=False):
  full=str(path) if dir_fd is None else self.fds[dir_fd]['path'].rstrip('/')+'/'+str(path)
  return self._stat(full)
 def read(self,number,count):
  original=self.fds[number]
  if original['kind']=='after' and self.after_error is not None:raise self.after_error
  position=original['position'];result=self.value[position:position+count];original['position']+=len(result)
  return result
 def lseek(self,number,offset,whence):
  if whence!=self.SEEK_SET or offset!=0:raise AssertionError('Exact fake rewind only')
  if self.changed_before_write is not None:self.value=self.changed_before_write
  self.fds[number]['position']=offset;return offset
 def write(self,number,data):
  if self.fds[number]['kind']!='writer':raise AssertionError('Original admitted writer required')
  self.writes.append(data)
  if self.write_error is not None:raise self.write_error
  self.value=data
  return 1 if self.short_write else len(data)
 def close(self,number):
  if number in self.closed:raise AssertionError('Same original FD closed twice')
  self.closed.append(number)
  if self.close_error is not None and self.fds[number]['kind']=='writer':raise self.close_error

def same_errors(error):
 if isinstance(error,BaseExceptionGroup):
  return [child for member in error.exceptions for child in same_errors(member)]
 return [error]
def rig(value):
 kernel=FakeKernel(value)
 namespace={'__builtins__':__builtins__,'hashlib':hashlib,'json':json,'pathlib':pathlib,'re':__import__('re'),'stat':stat,'os':kernel}
 exec(code,namespace)
 owner=object.__new__(namespace['HostedCredentialPolicyGuard'])
 owner.root=pathlib.Path('/RAM_ONLY/root');owner.output=pathlib.Path('/RAM_ONLY/out');owner.run_id='1';owner.attempt='1'
 owner.output_identity={'dev':1,'inode':2,'uid':0,'gid':0,'mode':stat.S_IFDIR|0o700,'nlink':1}
 owner._context=lambda:{'binding':{'uname':['Linux','inert','kernel','inert','x86_64'],'bootId':'inert','namespaces':{}},'actor':{},'qualification':'INERT_CONTROL_NOT_KERNEL_EVIDENCE'}
 owner.proofs={};owner.persist_error=None
 def persist(phase,data):
  if owner.persist_error is not None and phase=='restoration':raise owner.persist_error
  encoded=json.dumps(data).encode()
  if len(encoded)>16384:raise AssertionError('Actual bounded proof structure exceeded')
  owner.proofs[phase]=json.loads(encoded)
 owner._persist=persist
 owner._original=lambda:owner.proofs['original']
 return kernel,owner,namespace

class Controls(unittest.TestCase):
 def test_exact_original_values_preserved_and_actual_zero_required(self):
  for raw in (b'0\n',b'1\n',b'2\n'):
   with self.subTest(raw=raw):
    kernel,owner,_=rig(raw);owner.protect()
    self.assertEqual(raw.decode(),owner.proofs['original']['originalValue'])
    self.assertEqual(b'0\n',kernel.value)
    self.assertEqual(['attempt','original','protection'],list(owner.proofs))
    self.assertCountEqual(kernel.opened,kernel.closed)
 def test_short_first_read_is_recorded_refused_and_never_mutates(self):
  kernel,owner,_=rig(b'0')
  with self.assertRaises(RuntimeError):owner.protect()
  self.assertEqual([],kernel.writes)
  self.assertEqual('30',owner.proofs['protection']['originalObservation']['before']['firstReadHex'])
  self.assertNotIn('original',owner.proofs)
  self.assertCountEqual(kernel.opened,kernel.closed)
 def test_extra_data_unsupported_value_and_missing_newline_refuse(self):
  for raw in (b'3\n',b'0\nX',b'0',b'',b'0\n\n',b'00\n'):
   with self.subTest(raw=raw):
    kernel,owner,_=rig(raw)
    with self.assertRaises(RuntimeError):owner.protect()
    self.assertEqual([],kernel.writes)
    self.assertCountEqual(kernel.opened,kernel.closed)
 def test_source_bound_original_identity_change_refuses_before_write(self):
  kernel,owner,_=rig(b'1\n');original={};owner._endpoint(original)
  original['leaf']['inode']+=1
  with self.assertRaises(RuntimeError):owner._endpoint({},original,required='1\n',write_value='0\n')
  self.assertEqual([],kernel.writes);self.assertCountEqual(kernel.opened,kernel.closed)
 def test_privileged_value_change_before_write_preserves_current_state(self):
  kernel,owner,_=rig(b'2\n');kernel.changed_before_write=b'1\n'
  with self.assertRaises(RuntimeError):owner.protect()
  self.assertEqual([],kernel.writes);self.assertEqual(b'1\n',kernel.value)
  self.assertEqual('2\n',owner.proofs['original']['originalValue'])
  self.assertCountEqual(kernel.opened,kernel.closed)
 def test_missing_original_marker_after_attempt_cannot_guess_restore(self):
  kernel,owner,_=rig(b'0\n')
  kernel.path.lexists=lambda path:not str(path).endswith('-original.json')
  with self.assertRaises(RuntimeError):owner.restore_after_original_drains(lambda:None)
  self.assertEqual([],kernel.writes)
 def test_pending_original_root_or_userdel_seal_retains_zero_and_same_failure(self):
  for fail_on in (1,2):
   with self.subTest(fail_on=fail_on):
    kernel,owner,_=rig(b'2\n');owner.protect();kernel.writes.clear();calls=[]
    exact=RuntimeError('original pending same task')
    def authenticate():
     calls.append('authenticate')
     if len(calls)==fail_on:raise exact
    with self.assertRaises(RuntimeError) as result:owner.restore_after_original_drains(authenticate)
    self.assertIs(exact,result.exception);self.assertEqual(b'0\n',kernel.value);self.assertEqual([],kernel.writes)
    self.assertCountEqual(kernel.opened,kernel.closed)
 def test_exact_original_value_restored_only_after_both_fresh_seal_checks(self):
  for raw in (b'0\n',b'1\n',b'2\n'):
   with self.subTest(raw=raw):
    kernel,owner,_=rig(raw);owner.protect();kernel.writes.clear();order=[]
    old_write=kernel.write
    def write(number,data):
     order.append('write');return old_write(number,data)
    kernel.write=write
    owner.restore_after_original_drains(lambda:order.append('same-original-drains'))
    self.assertEqual(['same-original-drains','same-original-drains','write'],order)
    self.assertEqual(raw,kernel.value);self.assertEqual([raw],kernel.writes)
    self.assertCountEqual(kernel.opened,kernel.closed)
 def test_write_postread_close_persistence_failures_all_original_objects_retained(self):
  kernel,owner,_=rig(b'1\n');owner.protect()
  primary=OSError('actual original write');observation=RuntimeError('actual original post read')
  close=OSError('actual original FD close');persistence=OSError('actual proof persistence')
  kernel.write_error=primary;kernel.after_error=observation;kernel.close_error=close;owner.persist_error=persistence
  with self.assertRaises(BaseExceptionGroup) as result:owner.restore_after_original_drains(lambda:None)
  actual=same_errors(result.exception)
  for original in (primary,observation,close,persistence):self.assertTrue(any(member is original for member in actual))
  self.assertCountEqual(kernel.opened,kernel.closed)
 def test_partial_sysctl_write_never_claims_protected_success(self):
  kernel,owner,_=rig(b'1\n');kernel.short_write=True
  with self.assertRaises(OSError):owner.protect()
  self.assertEqual('FAILED_RETAIN_ORIGINAL_RECORD_AND_ACTUAL_CURRENT_POLICY',owner.proofs['protection']['status'])
  self.assertEqual(1,owner.proofs['protection']['writeCount'])
  self.assertEqual('0\n',owner.proofs['protection']['after']['value'])
  self.assertCountEqual(kernel.opened,kernel.closed)
 def test_directory_child_and_proc_process_counts_do_not_rebind_identity(self):
  kernel,owner,namespace=rig(b'1\n');original={};owner._endpoint(original)
  directory=namespace['identity'](kernel._stat('/proc'))
  kernel.directory_nlink+=15;kernel.dynamic_directory_nlink=True
  self.assertEqual(namespace['stable'](directory),namespace['stable'](namespace['identity'](kernel._stat('/proc'))))
  owner._endpoint({},original,required='1\n',write_value='0\n')
  self.assertEqual(b'0\n',kernel.value);self.assertCountEqual(kernel.opened,kernel.closed)
 def test_regular_leaf_link_count_remains_required_and_refuses_mutation(self):
  kernel,owner,_=rig(b'1\n');original={};owner._endpoint(original)
  kernel.regular_nlink=2
  with self.assertRaises(RuntimeError):owner._endpoint({},original,required='1\n',write_value='0\n')
  self.assertEqual([],kernel.writes);self.assertCountEqual(kernel.opened,kernel.closed)
 def test_actual_pre_and_post_root_observations_are_distinct_from_native_acceptance(self):
  kernel,owner,_=rig(b'2\n');owner.protect()
  for phase in ('before-root-tests','after-root-drain'):
   self.assertEqual('EXACT_ZERO_KERNEL_POLICY_OBSERVED_NOT_NATIVE_ACCEPTANCE',owner.observe(phase)['status'])
  self.assertEqual(b'0\n',kernel.value);self.assertCountEqual(kernel.opened,kernel.closed)
 def test_later_nonzero_observation_is_refusal_without_repair_or_false_ready(self):
  kernel,owner,_=rig(b'2\n');owner.protect();kernel.value=b'1\n';kernel.writes.clear()
  with self.assertRaises(RuntimeError):owner.observe('before-root-tests')
  self.assertEqual([],kernel.writes);self.assertEqual(b'1\n',kernel.value)
  self.assertEqual('REFUSED_RETAIN_CURRENT_POLICY_AND_ORIGINAL_ERRORS',owner.proofs['before-root-tests']['status'])
  self.assertCountEqual(kernel.opened,kernel.closed)

suite=unittest.defaultTestLoader.loadTestsFromTestCase(Controls)
result=unittest.TextTestRunner(verbosity=1).run(suite)
raise SystemExit(0 if result.wasSuccessful() else 1)
