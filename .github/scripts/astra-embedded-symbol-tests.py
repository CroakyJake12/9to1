"""Actual retained SDK producer symbol controls; no product suite acceptance."""
import tempfile,hashlib,importlib.util,os,pathlib,struct,unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('actual_pdb',ROOT/'.github/scripts/astra-home-portable-pdb.py');pdb=importlib.util.module_from_spec(spec);spec.loader.exec_module(pdb)
RECEIVING=pathlib.Path(os.environ['ASTRA_SYMBOL_RECEIVING_ROOT']).resolve()
TASK=RECEIVING/'artifacts/root14-host-build-tasks/bin/Avalonia.Build.Tasks/debug/Avalonia.Build.Tasks.dll'
EXTERNAL=RECEIVING/'artifacts/root14-managed-build/bin/Accounts.Specs/debug_linux-x64/Accounts.Specs.dll'
class ActualSymbols(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.dll=TASK.read_bytes();cls.symbols,cls.proof=pdb.actual_symbols(cls.dll)
 def altered(self,offset,data):
  result=bytearray(self.dll);result[offset:offset+len(data)]=data;return bytes(result)
 def test_actual_embedded_existing_source_hashes(self):
  self.assertFalse(TASK.with_suffix('.pdb').exists());self.assertEqual(self.proof['kind'],'embedded-portable-pdb')
  documents=pdb.pdb_documents(self.symbols);self.assertTrue(documents)
  for name,row in documents.items():
   source=pathlib.Path(name.replace('\\','/')).resolve();self.assertTrue(source.is_relative_to(RECEIVING))
   if not source.is_file():continue # Missing generated documents are an explicit separate failing gate below.
   self.assertFalse(source.is_symlink())
   self.assertEqual(hashlib.new(row['hashName'],source.read_bytes()).hexdigest(),row['digest'])
 def test_full_task_source_gate_remains_blocked(self):
  missing=[name for name in pdb.pdb_documents(self.symbols) if not pathlib.Path(name).is_file()]
  prefix=str(RECEIVING/'artifacts/root14-host-build-tasks/obj/Avalonia.Build.Tasks/debug')+'/'
  self.assertEqual(set(missing),{prefix+'DevGenerators/DevGenerators.CompilerDynamicDependenciesGenerator/CompilerDynamicDependenciesAttribute.generated.cs',prefix+'DevGenerators/DevGenerators.EnumMemberDictionaryGenerator/globalAvalonia.Media.KnownColors.cs'})
  with self.assertRaises(FileNotFoundError):pathlib.Path(missing[0]).read_bytes()
 def test_actual_external_source_hashes(self):
  data=EXTERNAL.with_suffix('.pdb').read_bytes();symbols,proof=pdb.actual_symbols(EXTERNAL.read_bytes(),data)
  self.assertEqual(symbols,data);self.assertEqual(proof['kind'],'external-portable-pdb')
  for name,row in pdb.pdb_documents(symbols).items():
   source=pathlib.Path(name.replace('\\','/')).resolve();self.assertTrue(source.is_relative_to(RECEIVING));self.assertEqual(hashlib.new(row['hashName'],source.read_bytes()).hexdigest(),row['digest'])
 def test_missing_external_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(EXTERNAL.read_bytes())
 def test_foreign_external_identity_refused(self):
  with self.assertRaises(AssertionError):pdb.actual_symbols(EXTERNAL.read_bytes(),self.symbols)
 def test_ambiguous_external_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(self.dll,self.symbols)
 def test_invalid_mpdb_signature_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(self.altered(self.proof['origin']['debugRecordOffset'],b'BAD!'))
 def test_declared_bomb_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(self.altered(self.proof['origin']['debugRecordOffset']+4,struct.pack('<I',0xffffffff)))
 def test_declared_size_mismatch_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(self.altered(self.proof['origin']['debugRecordOffset']+4,struct.pack('<I',len(self.symbols)-1)))
 def test_invalid_deflate_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(self.altered(self.proof['origin']['debugRecordOffset']+8,b'\xff\xff\xff\xff'))
 def test_truncated_pe_refused(self):
  with self.assertRaises(ValueError):pdb.actual_symbols(self.dll[:64])
class RetainedSymbolBounds(unittest.TestCase):
 def test_fresh_and_identical_retention(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb';pdb.retain_actual_symbols(path,b'actual',lambda:0,6)
   pdb.retain_actual_symbols(path,b'actual',lambda:6,6);self.assertEqual(path.read_bytes(),b'actual')
 def test_changed_existing_refused(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb';path.write_bytes(b'original')
   with self.assertRaises(RuntimeError):pdb.retain_actual_symbols(path,b'different',lambda:8,100)
   self.assertEqual(path.read_bytes(),b'original')
 def test_symlink_refused(self):
  with tempfile.TemporaryDirectory() as tmp:
   original=pathlib.Path(tmp)/'original';original.write_bytes(b'original');path=pathlib.Path(tmp)/'symbols.portable-pdb';path.symlink_to(original)
   with self.assertRaises(RuntimeError):pdb.retain_actual_symbols(path,b'changed',lambda:8,100)
   self.assertEqual(original.read_bytes(),b'original')
 def test_ancestor_symlink_refused(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=pathlib.Path(tmp);(root/'actual').mkdir();(root/'link').symlink_to(root/'actual',target_is_directory=True)
   with self.assertRaises(RuntimeError):pdb.retain_actual_symbols(root/'link'/'symbols.portable-pdb',b'actual',lambda:0,100)
   self.assertFalse((root/'actual'/'symbols.portable-pdb').exists())
 def test_budget_refused_before_creation(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=pathlib.Path(tmp)/'symbols.portable-pdb'
   with self.assertRaises(RuntimeError):pdb.retain_actual_symbols(path,b'actual',lambda:95,100)
   self.assertFalse(path.exists())
if __name__=='__main__':unittest.main(verbosity=2)
