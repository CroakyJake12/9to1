"""Observe actual compiled Sites PE/PDB pairs and every reviewed selected source; no authority."""
import hashlib,importlib.util,json,pathlib,shutil,sys
sys.dont_write_bytecode=True
def verify(root,out,target,suite,cut_paths,digest):
 root=pathlib.Path(root);target=pathlib.Path(target);site='9to1 Workspace/Sites/';shared='9to1 Workspace/shared/'
 assemblies={'HavenOS.Sites':sorted(p for p in cut_paths if p.startswith(site) and p.endswith('.cs') and not p.startswith(site+'Tests/'))}
 if suite=='sites-full':assemblies['HavenOS.Sites.Tests']=sorted(p for p in cut_paths if p.startswith(site+'Tests/') and p.endswith('.cs'))
 elif suite=='files-desktop':
  assemblies.update({'Haven.Core':[shared+'src/Haven.Core/DomainLogic/HavenSurface.cs'],'Haven.Application':[shared+'src/Haven.Application/Modes/BuiltInModeSeed.cs'],'Haven.Desktop':[shared+'src/Haven.Desktop/Views/Pages/Sites/NativeSitesPage.cs',shared+'src/Haven.Desktop/Views/Pages/Sites/SitesHavenScene.cs',shared+'src/Haven.Desktop/Views/Shell/HavenAppRoutePolicy.cs',shared+'src/Haven.Desktop/Interface/Shell/MainView.DocumentWorkspaces.cs'],'Haven.Desktop.Tests':[shared+'tests/Haven.Desktop.Tests/SitesNativeAuthoringConsumerTests.cs',shared+'tests/Haven.Desktop.Tests/HavenAppRoutePolicyTests.cs']})
 else:raise ValueError('Unexpected Sites source-proof suite')
 if len(assemblies['HavenOS.Sites'])<1 or suite=='sites-full' and len(assemblies['HavenOS.Sites.Tests'])!=9:raise ValueError('Complete Sites source inventory absent')
 spec=importlib.util.spec_from_file_location('sites_portable_pdb',root/'.github/scripts/astra-home-portable-pdb.py');parser=importlib.util.module_from_spec(spec);spec.loader.exec_module(parser)
 retained=pathlib.Path(out)/'sites-source-pairs'/suite;retained.mkdir(parents=True,exist_ok=False);records=[]
 for assembly,paths in assemblies.items():
  dll=target.parent/(assembly+'.dll');pdb=target.parent/(assembly+'.pdb')
  if any(not f.is_file() or f.is_symlink() for f in (dll,pdb)):raise ValueError('Missing actual compiled Sites pair: '+assembly)
  dll_bytes,pdb_bytes=dll.read_bytes(),pdb.read_bytes();pair=parser.assert_actual_pair(dll_bytes,pdb_bytes);documents=parser.pdb_documents(pdb_bytes);rows=[]
  for path in paths:
   source=root/path
   if path not in cut_paths or digest(source)!=cut_paths[path]:raise ValueError('Reviewed Sites source not pinned: '+path)
   matches=[(name,row) for name,row in documents.items() if name.replace('\\','/').endswith('/'+path)]
   if len(matches)!=1:raise ValueError('Missing/ambiguous complete Sites PDB document: '+path)
   name,row=matches[0]
   if hashlib.new(row['hashName'],source.read_bytes()).hexdigest()!=row['digest']:raise ValueError('Actual compiled Sites source differs: '+path)
   rows.append({'path':path,'sourceSha256':cut_paths[path],'document':name,**row})
  for file in (dll,pdb):
   dest=retained/file.name;shutil.copyfile(file,dest)
   if digest(dest)!=digest(file):raise ValueError('Retained original Sites pair differs')
  records.append({'assembly':assembly,'dllSha256':digest(dll),'pdbSha256':digest(pdb),'identity':pair,'sources':rows})
 (retained/'receipt.json').write_text(json.dumps({'status':'ACTUAL_COMPLETE_REVIEWED_SITES_SOURCE_DOCUMENTS_MATCH_COMPILED_PAIRS','suite':suite,'pairs':records,'qualification':'Compiled source identity only; whole original Sites/host/native/runtime results and actual browser DOM rendering remain separate.'},indent=2)+'\n')
