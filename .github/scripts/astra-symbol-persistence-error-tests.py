"""Actual same-handle persistence fault controls using genuine embedded compiler symbols."""
import errno,hashlib,importlib.util,os,pathlib,tempfile,unittest
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[2]
HELPER=pathlib.Path(os.environ.get('ASTRA_PERSISTENCE_HELPER_PATH',str(ROOT/'.github/scripts/astra-home-portable-pdb.py')))
spec=importlib.util.spec_from_file_location('actual_pdb',HELPER);pdb=importlib.util.module_from_spec(spec);spec.loader.exec_module(pdb)
RECEIVING=pathlib.Path(os.environ['ASTRA_PERSISTENCE_RECEIVING_ROOT'])
PE=RECEIVING/'artifacts/root14-host-build-tasks/bin/Avalonia.Build.Tasks/debug/Avalonia.Build.Tasks.dll'
def errors(error):
 if isinstance(error,BaseExceptionGroup):return [leaf for child in error.exceptions for leaf in errors(child)]
 return [error]
class Persistence(unittest.TestCase):
 @classmethod
 def setUpClass(cls):cls.symbols,cls.origin=pdb.actual_symbols(PE.read_bytes())
 def test_actual_symbols_fresh_exclusive_durable_and_identical(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb';calls=[];real=os.fsync
   def sync(fd):calls.append(os.fstat(fd));real(fd)
   with patch.object(os,'fsync',sync):pdb.retain_actual_symbols(path,self.symbols,lambda:0,len(self.symbols))
   self.assertEqual(len(calls),1);self.assertEqual(path.read_bytes(),self.symbols);self.assertEqual(pdb.assert_actual_pair(PE.read_bytes(),path.read_bytes()),self.origin['identity'])
   with patch.object(os,'fsync',side_effect=AssertionError('Identical existing file must not reopen')):pdb.retain_actual_symbols(path,self.symbols,lambda:len(self.symbols),len(self.symbols))
 def test_native_fsync_and_same_handle_close_failures_preserved(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb';body=[];real=os.fsync
   def fail(fd):
    self.assertEqual(os.fstat(fd).st_ino,path.stat().st_ino)
    os.close(fd)
    try:real(fd)
    except OSError as error:body.append(error);raise
   with patch.object(os,'fsync',fail):
    with self.assertRaises(BaseException) as caught:pdb.retain_actual_symbols(path,self.symbols,lambda:0,len(self.symbols))
   leaves=errors(caught.exception);self.assertEqual(len(body),1);self.assertTrue(any(error is body[0] for error in leaves),'Original native fsync error must retain exact object');self.assertEqual(len(leaves),2);self.assertTrue(all(isinstance(error,OSError) and error.errno==errno.EBADF for error in leaves));self.assertIsNot(leaves[0],leaves[1]);self.assertEqual(path.read_bytes(),self.symbols)
 def test_fsync_only_failure_retains_exact_original(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb';failure=OSError(errno.EIO,'Controlled fsync refusal')
   def fail(fd):self.assertEqual(os.fstat(fd).st_ino,path.stat().st_ino);raise failure
   with patch.object(os,'fsync',fail):
    with self.assertRaises(OSError) as caught:pdb.retain_actual_symbols(path,self.symbols,lambda:0,len(self.symbols))
   self.assertIs(caught.exception,failure);self.assertEqual(path.read_bytes(),self.symbols)
 def test_close_only_native_failure_is_not_success(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb';real=os.fsync
   def sync_then_close(fd):real(fd);os.close(fd)
   with patch.object(os,'fsync',sync_then_close):
    with self.assertRaises(OSError) as caught:pdb.retain_actual_symbols(path,self.symbols,lambda:0,len(self.symbols))
   self.assertEqual(caught.exception.errno,errno.EBADF);self.assertEqual(path.read_bytes(),self.symbols)
if __name__=='__main__':unittest.main(verbosity=2)
