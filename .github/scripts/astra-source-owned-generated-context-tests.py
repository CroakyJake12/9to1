"""Actual maintained Sites compiler-output contract controls; no full SDK acceptance."""
import copy,hashlib,importlib.util,json,os,pathlib,tempfile,unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]
RECEIVING=pathlib.Path(os.environ['ASTRA_SOURCE_OWNER_RECEIVING_ROOT'])
INVENTORY=pathlib.Path(os.environ['ASTRA_SOURCE_OWNER_OUTPUT_INVENTORY'])
spec=importlib.util.spec_from_file_location('actual_pdb',ROOT/'.github/scripts/astra-home-portable-pdb.py');pdb=importlib.util.module_from_spec(spec);spec.loader.exec_module(pdb)
VALUES=json.loads((RECEIVING/'artifacts/desktop-visible-owning/logs/debug-evaluate-web-9.log').read_text())['Properties']
CUT=json.loads((RECEIVING/'.github/validation/astra-organisation-full-owning-cut.json').read_text())
ARTIFACTS=RECEIVING/'artifacts/root14-managed-build'
def contract():return {'root':RECEIVING,'files':copy.deepcopy(CUT['files'])}
class SourceOwned(unittest.TestCase):
 def refuse(self,changes,owner=None):
  values=copy.deepcopy(VALUES);values.update(changes)
  with self.assertRaises(RuntimeError):pdb.assert_generated_context(values,ARTIFACTS,contract() if owner is None else owner)
 def test_original_without_source_contract_still_refused(self):
  with self.assertRaises(RuntimeError):pdb.assert_generated_context(VALUES,ARTIFACTS)
 def test_actual_cut_pinned_sites_owner_accepted(self):
  result=pdb.assert_generated_context(VALUES,ARTIFACTS,contract());self.assertEqual(result['projectOwnerRoot'],str(RECEIVING/'9to1 Workspace/Sites/obj/HavenOS.Sites'));self.assertEqual(result['sourceOwnedContract']['props'],'9to1 Workspace/Sites/Directory.Build.props')
 def test_actual_physical_sites_pe_symbols_all_source_hashes(self):
  target=pathlib.Path(VALUES['TargetPath']);external=target.with_suffix('.pdb');symbols,proof=pdb.actual_symbols(target.read_bytes(),external.read_bytes());self.assertEqual(pdb.assert_actual_pair(target.read_bytes(),symbols),proof['identity'])
  docs=pdb.pdb_documents(symbols);self.assertTrue(docs)
  for name,row in docs.items():
   source=pathlib.Path(name);self.assertTrue(source.is_absolute());self.assertTrue(source.resolve().is_relative_to(RECEIVING));self.assertFalse(source.is_symlink());self.assertEqual(hashlib.new(row['hashName'],source.read_bytes()).hexdigest(),row['digest'])
 def test_all_actual_compiler_inputs_exist(self):
  query=json.loads((RECEIVING/'artifacts/desktop-visible-owning/logs/debug-evaluate-web-9.log').read_text());inputs=query['Items']['Compile'];self.assertTrue(inputs)
  for item in inputs:self.assertTrue(pathlib.Path(item['FullPath']).is_file())
 def test_all_original_actual_output_bytes_unchanged(self):
  rows=json.loads(INVENTORY.read_text())['files'];self.assertTrue(rows)
  for row in rows:
   data=(RECEIVING/row['path']).read_bytes();self.assertEqual(len(data),row['bytes']);self.assertEqual(hashlib.sha256(data).hexdigest(),row['sha256'])
 def test_arbitrary_agreeing_base_refused(self):
  foreign=str(RECEIVING/'arbitrary/obj/HavenOS.Sites');self.refuse({'BaseIntermediateOutputPath':foreign,'MSBuildProjectExtensionsPath':foreign,'IntermediateOutputPath':foreign+'/debug','CompilerGeneratedFilesOutputPath':foreign+'/debug/generated'})
 def test_foreign_project_refused(self):self.refuse({'MSBuildProjectFullPath':str(RECEIVING/'9to1 Workspace/Home/HavenOS.Home.csproj')})
 def test_foreign_project_name_refused(self):self.refuse({'ArtifactsProjectName':'Foreign','MSBuildProjectName':'Foreign'})
 def test_foreign_artifacts_refused(self):self.refuse({'ArtifactsPath':str(RECEIVING/'artifacts/root14-host-build-tasks')})
 def test_relative_base_refused(self):self.refuse({'BaseIntermediateOutputPath':'obj/HavenOS.Sites'})
 def test_escaped_generated_refused(self):self.refuse({'CompilerGeneratedFilesOutputPath':str(RECEIVING/'9to1 Workspace/Sites/obj/Foreign/generated')})
 def test_escaped_intermediate_refused(self):self.refuse({'IntermediateOutputPath':str(RECEIVING/'9to1 Workspace/Sites/obj/Foreign/debug')})
 def test_emission_disabled_refused(self):self.refuse({'EmitCompilerGeneratedFiles':'false'})
 def test_missing_props_pin_refused(self):
  owner=contract();owner['files']=[row for row in owner['files'] if row['path']!='9to1 Workspace/Sites/Directory.Build.props'];self.refuse({},owner)
 def test_wrong_props_pin_refused(self):
  owner=contract();next(row for row in owner['files'] if row['path']=='9to1 Workspace/Sites/Directory.Build.props')['sha256']='0'*64;self.refuse({},owner)
 def test_wrong_project_pin_refused(self):
  owner=contract();next(row for row in owner['files'] if row['path']=='9to1 Workspace/Sites/HavenOS.Sites.csproj')['bytes']+=1;self.refuse({},owner)
 def test_raw_owner_symlink_refused(self):
  base=pathlib.Path(VALUES['BaseIntermediateOutputPath'])
  with tempfile.TemporaryDirectory(dir=base.parent) as tmp:
   link=pathlib.Path(tmp)/'link';link.symlink_to(base,target_is_directory=True);self.refuse({'BaseIntermediateOutputPath':str(link)})
if __name__=='__main__':unittest.main(verbosity=2)
