"""Actual canonical SDK owner-root controls, not product acceptance."""
import copy,hashlib,importlib.util,json,os,pathlib,tempfile,unittest
ROOT=pathlib.Path(__file__).resolve().parents[2];EVIDENCE=pathlib.Path(os.environ['ASTRA_CONTEXT_EVIDENCE_ROOT']);RECEIVING=pathlib.Path(os.environ['ASTRA_CONTEXT_RECEIVING_ROOT']).resolve()
spec=importlib.util.spec_from_file_location('actual_pdb',ROOT/'.github/scripts/astra-home-portable-pdb.py');pdb=importlib.util.module_from_spec(spec);spec.loader.exec_module(pdb)
def values(index=0):return json.loads((EVIDENCE/'generated-context-actual'/('project-'+str(index)+'.log')).read_text())['Properties']
class ActualOwnerRoot(unittest.TestCase):
 def test_actual_configured_sibling_before_refusal_fixed_valid(self):
  v=values();intermediate=pathlib.Path(v['IntermediateOutputPath']).resolve();generated=pathlib.Path(v['CompilerGeneratedFilesOutputPath']).resolve();self.assertFalse(generated.is_relative_to(intermediate))
  proof=pdb.assert_generated_context(v,RECEIVING/'artifacts/root14-managed-build');self.assertEqual(proof['projectOwnerRoot'],str(pathlib.Path(v['MSBuildProjectExtensionsPath']).resolve()))
 def test_actual_default_host_output_valid(self):
  pdb.assert_generated_context(values(1),RECEIVING/'artifacts/root14-host-build-tasks')
 def test_all_actual_base_and_task_symbol_source_hashes(self):
  for index in range(2):
   v=values(index);target=pathlib.Path(v['TargetPath']);external=target.with_suffix('.pdb');symbols,_=pdb.actual_symbols(target.read_bytes(),external.read_bytes() if external.is_file() else None);docs=pdb.pdb_documents(symbols);self.assertTrue(docs)
   for name,row in docs.items():
    source=pathlib.Path(name).resolve();self.assertTrue(source.is_relative_to(RECEIVING));self.assertFalse(source.is_symlink());self.assertEqual(hashlib.new(row['hashName'],source.read_bytes()).hexdigest(),row['digest'])
 def refuse(self,key,value):
  v=values();v[key]=value
  with self.assertRaises(RuntimeError):pdb.assert_generated_context(v,RECEIVING/'artifacts/root14-managed-build')
 def test_foreign_project_owner_refused(self):self.refuse('MSBuildProjectExtensionsPath',str(RECEIVING/'artifacts/root14-managed-build/obj/Foreign'))
 def test_wrong_host_managed_artifacts_refused(self):self.refuse('ArtifactsPath',str(RECEIVING/'artifacts/root14-host-build-tasks'))
 def test_foreign_project_name_refused(self):self.refuse('ArtifactsProjectName','Foreign')
 def test_generated_path_escape_refused(self):self.refuse('CompilerGeneratedFilesOutputPath',str(RECEIVING/'artifacts/root14-managed-build/obj/Foreign/GeneratedFiles'))
 def test_relative_generated_path_refused(self):self.refuse('CompilerGeneratedFilesOutputPath','GeneratedFiles')
 def test_emission_disabled_refused(self):self.refuse('EmitCompilerGeneratedFiles','false')
 def test_generated_symlink_refused(self):
  with tempfile.TemporaryDirectory() as tmp:
   p=pathlib.Path(tmp);(p/'real').mkdir();(p/'link').symlink_to(p/'real',target_is_directory=True);self.refuse('CompilerGeneratedFilesOutputPath',str(p/'link'))
if __name__=='__main__':unittest.main(verbosity=2)
