"""Actual isolated Linux product/helper publication; trusted caller supplies immutable guards."""
import pathlib,json,shutil
ALLOWED={"shell":"9to1 OS/apps/9to1-shell/NineToOne.Os.Shell.csproj","fixture-tool":"9to1 OS/tests/NineToOne.Os.Supervisor.FixtureTools/NineToOne.Os.Supervisor.FixtureTools.csproj"}
def publish_and_pin(root,name,out,command,verify,digest,restore,task_target,assert_task_unchanged):
 if name not in ALLOWED:raise ValueError('only actual product or fixture tool may publish')
 root=root.resolve();project=ALLOWED[name];artifacts=root/'artifacts/root14-supervised-publish-build';published=root/'artifacts/supervised-published'/name
 if published.exists():raise ValueError('publication directory must be fresh')
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=true','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-p:IncludeProjectNameInArtifactsPaths=true','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:EnableWindowsTargeting=true','-p:AvaloniaBuildTasksLocation='+str(task_target)]
 before=restore.snapshot_restore(root,project,publish_context=True)
 (out/(name+'-publish-restore-before.json')).write_text(json.dumps(before,indent=2)+'\n')
 code=command(['dotnet','build',project,'--no-restore','--disable-build-servers','-m:1','-nr:false',*props],name+'-publish-build');verify();assert_task_unchanged()
 if code:raise RuntimeError('actual self-contained build failed '+name)
 code=command(['dotnet','publish',project,'--no-build','--no-restore','--disable-build-servers','-m:1','-nr:false',*props,'-p:PublishDir='+str(published)+'/'],name+'-publish');verify();assert_task_unchanged()
 if code:raise RuntimeError('actual self-contained publish failed '+name)
 query=out/(name+'-publish-query.json')
 code=command(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-p:PublishDir='+str(published)+'/','-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,SelfContained,Configuration,PublishDir,AvaloniaBuildTasksLocation','-getItem:Compile'],name+'-publish-query')
 if code:raise RuntimeError('actual publish producer query failed')
 evaluated=json.loads((out/(name+'-publish-query.log')).read_text());query.write_text(json.dumps(evaluated,indent=2)+'\n');p=evaluated['Properties']
 if p['TargetFramework']!='net10.0' or p['RuntimeIdentifier']!='linux-x64' or p['Configuration']!='Release' or p['SelfContained'].lower()!='true':raise ValueError('published SDK context mismatch')
 if pathlib.Path(p['PublishDir']).resolve()!=published or pathlib.Path(p['AvaloniaBuildTasksLocation']).resolve()!=task_target:raise ValueError('published output/task provenance mismatch')
 target=pathlib.Path(p['TargetPath']).resolve();dll=published/target.name;apphost=dll.with_suffix('');pdb=dll.with_suffix('.pdb')
 if not all(x.is_file() and not x.is_symlink() for x in (target,dll,apphost,pdb)):raise ValueError('actual published apphost/DLL/PDB missing')
 if not apphost.read_bytes().startswith(b'\x7fELF') or digest(target)!=digest(dll):raise ValueError('actual ELF apphost/compiled DLL publication mismatch')
 inventory=[]
 for path in sorted(published.rglob('*')):
  if path.is_symlink():raise ValueError('published payload symlink refused')
  if path.is_file():inventory.append({'path':path.relative_to(published).as_posix(),'bytes':path.stat().st_size,'sha256':digest(path)})
 if not inventory:raise ValueError('actual published payload empty')
 after=restore.snapshot_restore(root,project,publish_context=True)
 (out/(name+'-publish-restore-after.json')).write_text(json.dumps(after,indent=2)+'\n')
 if before!=after:raise ValueError('actual publish restore graph/packages changed')
 verify();assert_task_unchanged()
 receipt={'project':project,'publishDir':str(published),'apphost':str(apphost),'apphostSha256':digest(apphost),'managedDLL':str(dll),'managedSha256':digest(dll),'pdbSha256':digest(pdb),'payload':inventory,'evaluatedCompile':evaluated.get('Items',{}).get('Compile'),'syntheticIssuerOnly':True,'releasePublisherAccepted':False,'installedUserAccepted':False,'runtimeAccepted':False}
 (out/(name+'-actual-published-payload.json')).write_text(json.dumps(receipt,indent=2)+'\n')
 return receipt
