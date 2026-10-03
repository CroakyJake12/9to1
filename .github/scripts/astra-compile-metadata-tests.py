"""Real SDK generated-input metadata controls; no original product suite count."""
import hashlib,importlib.util,json,os,pathlib,unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]
RECEIVING=pathlib.Path(os.environ['ASTRA_METADATA_RECEIVING_ROOT']).resolve()
EVIDENCE=pathlib.Path(os.environ['ASTRA_METADATA_EVIDENCE_ROOT']).resolve()
spec=importlib.util.spec_from_file_location('actual_pdb',ROOT/'.github/scripts/astra-home-portable-pdb.py');pdb=importlib.util.module_from_spec(spec);spec.loader.exec_module(pdb)
def metadata(phase,index):
 raw=(EVIDENCE/('compile-metadata-'+phase)/('project-'+str(index)+'.log')).read_text()
 return json.loads(raw[raw.index('{'):])
class ActualCompilerMetadata(unittest.TestCase):
 def test_original_package_input_refusal(self):
  self.assertEqual(metadata('before',0)['Items']['Compile'],[])
 def test_real_package_input_matches_original_symbol(self):
  actual=metadata('fixed2',0);items=actual['Items']['Compile'];self.assertEqual(len(items),1)
  source=pathlib.Path(items[0]['FullPath']).resolve();target=pathlib.Path(actual['Properties']['TargetPath'])
  symbols,_=pdb.actual_symbols(target.read_bytes(),target.with_suffix('.pdb').read_bytes());docs=pdb.pdb_documents(symbols)
  self.assertEqual(set(docs),{str(source)});self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(),docs[str(source)]['digest'])
 def test_all_three_contexts_original_sources_and_symbols(self):
  for index in range(3):
   before=metadata('before',index);after=metadata('fixed2',index);self.assertEqual(before['Properties'],after['Properties'])
   initial={item['FullPath'] for item in before['Items']['Compile']};actual={item['FullPath'] for item in after['Items']['Compile']};self.assertTrue(initial<=actual);self.assertTrue(actual)
   for name in actual:
    source=pathlib.Path(name).resolve();self.assertTrue(source.is_relative_to(RECEIVING));self.assertTrue(source.is_file());self.assertFalse(source.is_symlink())
   target=pathlib.Path(after['Properties']['TargetPath']);symbols,_=pdb.actual_symbols(target.read_bytes(),target.with_suffix('.pdb').read_bytes())
   for name,row in pdb.pdb_documents(symbols).items():
    source=pathlib.Path(name).resolve();self.assertTrue(source.is_relative_to(RECEIVING));self.assertEqual(hashlib.new(row['hashName'],source.read_bytes()).hexdigest(),row['digest'])
 def test_all_actual_outputs_remain_byte_identical(self):
  before=json.loads((EVIDENCE/'compile-metadata-before-output-closure.json').read_text());after=json.loads((EVIDENCE/'compile-metadata-fixed2-output-closure.json').read_text());self.assertEqual(len(before),2480);self.assertEqual(before,after)
  for row in after:
   data=(RECEIVING/row['path']).read_bytes();self.assertEqual(len(data),row['bytes']);self.assertEqual(hashlib.sha256(data).hexdigest(),row['sha256'])
 def test_wrong_target_order_preserved_as_negative(self):
  self.assertEqual(len(metadata('fixed',0)['Items']['Compile']),2)
  delta=json.loads((EVIDENCE/'compile-metadata-output-delta.json').read_text());self.assertEqual(len(delta),1);self.assertIsNone(delta[0]['before'])
  self.assertEqual(pathlib.Path(delta[0]['path']).name,'.NETCoreApp,Version=v10.0.AssemblyAttributes.cs')
  self.assertEqual(hashlib.sha256((EVIDENCE/'compile-metadata-fixed/new-uncompiled-TFM-attribute.generated.cs').read_bytes()).hexdigest(),delta[0]['after']['sha256'])
if __name__=='__main__':unittest.main(verbosity=2)
