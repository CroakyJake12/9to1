"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/root14-accounts';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-root14-cut.json':raise SystemExit('unexpected self-manifest path')
manifest=root/a.manifest
if not manifest.is_file() or digest(manifest)!=a.manifest_sha:raise SystemExit('manifest pin mismatch')
cut=json.loads(manifest.read_text());files=cut.get('files');links=cut.get('gitlinks');materialized=cut.get('materializedGitlinks')
if not isinstance(files,list) or not files or not isinstance(links,list) or not isinstance(materialized,list):raise SystemExit('cut schema missing files/gitlinks/materializedGitlinks')
requiredMaterialized={'framework/CUI/vendor/Avalonia/external/XamlX': '009d4815470cf4bf71d1adbb633a5d81dcb2bb52', 'framework/CUI/vendor/Avalonia/external/Avalonia.DBus': '864a05282841bf04006890f04d11d60d1a046aa9', '9to1 Workspace/Terminal/Source/libvterm': '934bc2fbf21800ac3458a499df8820ca5fb45fd3'}
def verify():
 # One batched immutable tree read, rather than9075 per-file Git subprocesses.
 actualFiles={};actualLinks={}
 for item in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
  if not item:continue
  header,path=item.split(b'\t',1);mode,kind,oid=header.decode().split();path=path.decode()
  if mode=='160000':actualLinks[path]=oid
  elif mode in ('100644','100755') and kind=='blob':actualFiles[path]=(mode,oid)
  else:raise SystemExit('unexpected tracked tree mode/type: '+path)
 # The exact externallySHA-pinned manifest is excluded from its own regular-file map.
 if a.manifest not in actualFiles:raise SystemExit('self-manifest not tracked in actual HEAD')
 del actualFiles[a.manifest]
 expectedFiles={}
 for f in files:
  relative=pathlib.PurePosixPath(f['path']);name=str(relative)
  if relative.is_absolute() or '..' in relative.parts or name in expectedFiles or name==a.manifest:raise SystemExit('unsafe/duplicate/self cut path')
  mode=f.get('mode',f.get('gitMode'))
  if 'mode' in f and 'gitMode' in f and f['mode']!=f['gitMode']:raise SystemExit('conflicting cut modes')
  if mode not in ('100644','100755'):raise SystemExit('invalid regularfile mode')
  path=root/name
  if path.is_symlink() or not path.is_file():raise SystemExit('source file missing/notregular: '+name)
  size=path.stat().st_size;sha256=hashlib.sha256();blob=hashlib.sha1();blob.update(('blob '+str(size)+'\0').encode());count=0
  with path.open('rb') as stream:
   while data:=stream.read(1024*1024):sha256.update(data);blob.update(data);count+=len(data)
  if count!=size or count!=f.get('bytes',count) or sha256.hexdigest()!=f['sha256']:raise SystemExit('source SHA/size mismatch: '+name)
  # Verify a supplied root blobOID, and always bind actual filebytes to the GitHEAD blob.
  supplied=f.get('object')
  if supplied is not None and (not re.fullmatch('[0-9a-f]{40}',supplied) or supplied!=blob.hexdigest()):raise SystemExit('source blobOID mismatch: '+name)
  expectedFiles[name]=(mode,blob.hexdigest())
 if actualFiles!=expectedFiles:raise SystemExit('complete regularfile path/mode/blobHEAD map mismatch')
 if digest(manifest)!=a.manifest_sha:raise SystemExit('self-manifest changed during lifetime')
 expectedLinks={}
 for link in links:
  relative=pathlib.PurePosixPath(link['path']);pin=link['commit']
  if relative.is_absolute() or '..' in relative.parts or str(relative) in expectedLinks or not re.fullmatch('[0-9a-f]{40}',pin):raise SystemExit('unsafe/duplicate/invalid gitlink pin')
  expectedLinks[str(relative)]=pin
 if len(expectedLinks)!=22 or actualLinks!=expectedLinks:raise SystemExit('complete22 tracked gitlink pin set mismatch')
 actualMaterialized={}
 for link in materialized:
  if link['path'] in actualMaterialized:raise SystemExit('duplicate materialized gitlink')
  actualMaterialized[link['path']]=link['commit']
 if actualMaterialized!=requiredMaterialized:raise SystemExit('materialized subset must be exact3 reviewed source dependencies')
 for path,pin in actualMaterialized.items():
  if expectedLinks.get(path)!=pin:raise SystemExit('materialized pin not in complete tracked set')
  actualTop=pathlib.Path(subprocess.check_output(['git','-C',path,'rev-parse','--show-toplevel'],text=True).strip()).resolve()
  if actualTop!=(root/path).resolve() or subprocess.check_output(['git','-C',path,'rev-parse','HEAD'],text=True).strip()!=pin:raise SystemExit('actual materialized dependency checkout mismatch')
  if subprocess.check_output(['git','-C',path,'status','--porcelain','--untracked-files=no'],text=True).strip():raise SystemExit('tracked materialized dependency dirt')
 # Independent existing repository check validates all three trackedclean pins/header inputs;
 # --check never updates clones or rewrites tracked source.
 if subprocess.run(['bash','9to1 Workspace/shared/eng/prepare-cui-source.sh','--check'],check=False).returncode:raise SystemExit('independent clean pinned source check failed')
verify();command(['dotnet','--info'],'toolchain');command(['dotnet','workload','list'],'workloads')
env={'AVALONIA_TELEMETRY_OPTOUT':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_USE_MSBUILD_SERVER':'0'};os.environ.update(env)
base=['-c','Release','-r','linux-x64','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True']
artifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/root14-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true']
base+=artifactsProps
restoreEntries=['9to1 Workspace/Validation/Accounts.PublicClient.Specs/NineToOne.Accounts.PublicClient.Specs.csproj', '9to1 Workspace/Accounts/Remote/Tests/NineToOne.Accounts.Remote.Specs.csproj', '9to1 Workspace/Web/NineToOne.Web.csproj']
for project in restoreEntries:
 code=command(['dotnet','restore',project,*base[2:],'-p:Configuration=Release','-p:TargetFramework=net10.0','-p:EnableWindowsTargeting=true'],'main-restore-'+pathlib.Path(project).stem);verify()
 if code:raise SystemExit(code)
hostArtifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/root14-host-build-tasks'),'-p:IncludeProjectNameInArtifactsPaths=true']
hostTaskProject='framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
hostTaskRestoreProps=['-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*hostArtifactsProps]
code=command(['dotnet','restore',hostTaskProject,'--disable-build-servers','-m:1','-nr:false',*hostTaskRestoreProps],'avalonia-build-tasks-restore');verify()
if code:raise SystemExit(code)
# These six netstandard assets are written LAST; no subsequent build may restore.
toolProjects=['framework/CUI/vendor/Avalonia/src/tools/DevAnalyzers/DevAnalyzers.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CSharp/Avalonia.Analyzers.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CodeFixes.CSharp/Avalonia.Analyzers.CodeFixes.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.VisualBasic/Avalonia.Analyzers.VisualBasic.csproj', 'framework/CUI/vendor/Avalonia/src/tools/DevGenerators/DevGenerators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.DBus.Generators/Avalonia.DBus.Generators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Generators/Avalonia.Generators.csproj']
for tool in toolProjects:
 code=command(['dotnet','restore',tool,*base[2:],'-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true'],'tool-restore-'+pathlib.Path(tool).stem);verify()
 if code:raise SystemExit(code)
 toolQuery=subprocess.run(['dotnet','msbuild',tool,'-nologo','-m:1','-nr:false','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*artifactsProps,'-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true','-getProperty:TargetFramework,MSBuildProjectFullPath,ProjectAssetsFile'],capture_output=True,text=True)
 (out/('tool-framework-'+pathlib.Path(tool).stem+'.stdout')).write_text(toolQuery.stdout);(out/('tool-framework-'+pathlib.Path(tool).stem+'.stderr')).write_text(toolQuery.stderr)
 if toolQuery.returncode:raise SystemExit(toolQuery.returncode)
 toolActual=json.loads(toolQuery.stdout)['Properties'];assetPath=pathlib.Path(toolActual['ProjectAssetsFile']);assetPath=assetPath if assetPath.is_absolute() else (root/tool).parent/assetPath
 if toolActual['TargetFramework']!='netstandard2.0' or pathlib.Path(toolActual['MSBuildProjectFullPath']).resolve()!=(root/tool).resolve() or not assetPath.resolve().is_relative_to(root) or not assetPath.is_file():raise SystemExit('actual restored analyzer framework/path mismatch')
 toolAssets=json.loads(assetPath.read_text())
 if not any(key.split('/')[0]=='netstandard2.0' for key in toolAssets['targets']):raise SystemExit('actual analyzer assets missing declared netstandard2.0')

# Source-built genuine build-host task; its ProjectReference removes application RID.
taskProject='framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
taskProps=['-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*hostArtifactsProps]
code=command(['dotnet','build',taskProject,'--no-restore','-c','Release','-f','netstandard2.0','--disable-build-servers','-m:1','-nr:false',*taskProps],'avalonia-build-tasks-build');verify()
if code:raise SystemExit(code)
taskQuery=subprocess.run(['dotnet','msbuild',taskProject,'-nologo','-m:1','-nr:false',*taskProps,'-getProperty:TargetPath,OutputPath,TargetFramework,Configuration'],capture_output=True,text=True)
(out/'avalonia-build-tasks-target.stdout').write_text(taskQuery.stdout);(out/'avalonia-build-tasks-target.stderr').write_text(taskQuery.stderr)
if taskQuery.returncode:raise SystemExit(taskQuery.returncode)
taskEvaluated=json.loads(taskQuery.stdout)['Properties'];taskTarget=pathlib.Path(taskEvaluated['TargetPath']).resolve()
taskOutput=pathlib.Path(taskEvaluated['OutputPath']);taskOutput=taskOutput if taskOutput.is_absolute() else (root/taskProject).parent/taskOutput;taskOutput=taskOutput.resolve()
if taskEvaluated['TargetFramework']!='netstandard2.0' or taskEvaluated['Configuration']!='Release' or not taskOutput.is_relative_to(root) or not taskTarget.is_relative_to(taskOutput) or not taskTarget.is_file():raise SystemExit('invalid actual source-built host task output')
def task_snapshot():
 result=[]
 for file in sorted(taskTarget.parent.rglob('*')):
  if file.is_symlink():raise SystemExit('build task output symlink')
  if file.is_file():result.append({'path':str(file.relative_to(root)),'bytes':file.stat().st_size,'sha256':digest(file)})
 return result
taskBefore=task_snapshot();(out/'avalonia-build-tasks-compiled-before.json').write_text(json.dumps({'project':taskProject,'target':str(taskTarget.relative_to(root)),'files':taskBefore},indent=2)+'\n')
retainedTasks=out/'compiled'/'avalonia-build-tasks';retainedTasks.mkdir(parents=True,exist_ok=True)
for file in sorted(taskTarget.parent.rglob('*')):
 if file.is_file():
  destination=retainedTasks/file.relative_to(taskTarget.parent);destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(file,destination)
  if digest(destination)!=digest(file):raise SystemExit('retained actual build task closure mismatch')
actualTaskProperty='-p:AvaloniaBuildTasksLocation='+str(taskTarget)
base.append(actualTaskProperty);artifactsProps.append(actualTaskProperty);os.environ['ASTRA_ACTUAL_AVALONIA_BUILD_TASKS']=str(taskTarget)
def assert_task_unchanged():
 after=task_snapshot();(out/'avalonia-build-tasks-compiled-after.json').write_text(json.dumps({'project':taskProject,'target':str(taskTarget.relative_to(root)),'files':after},indent=2)+'\n')
 if after!=taskBefore:raise SystemExit('actual source-built task closure changed')

import importlib.util
helper=root/'.github/scripts/astra-auth-restore-evidence.py'
spec=importlib.util.spec_from_file_location('restore_evidence',helper);restore=importlib.util.module_from_spec(spec);spec.loader.exec_module(restore)
compiledTargets={}
def assert_compiled_target_unchanged(name):
 target,closure=compiledTargets[name]
 current=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('compiled dependency output became symlink')
  if path.is_file():current.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 if current!=closure:raise SystemExit('compiled entire pinned output closure changed during execution')
 assert_task_unchanged()
def build_and_pin(name,project):
 code=command(['dotnet','build',project,*base,'--no-restore','-f','net10.0','-p:EnableWindowsTargeting=true'],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']+artifactsProps
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,OutputPath,RuntimeIdentifier,Configuration,AvaloniaBuildTasksLocation'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 evaluated=json.loads(query.stdout)['Properties'];target=pathlib.Path(evaluated['TargetPath']).resolve()
 if pathlib.Path(evaluated['AvaloniaBuildTasksLocation']).resolve()!=taskTarget:raise SystemExit('consumer UsingTask property differs from pinned source-built task')
 assert_task_unchanged()
 output=pathlib.Path(evaluated['OutputPath']);output=output if output.is_absolute() else (root/project).parent/output;output=output.resolve()
 if evaluated['RuntimeIdentifier']!='linux-x64' or evaluated['Configuration']!='Release' or not output.is_relative_to(root.resolve()) or not target.is_relative_to(output) or not target.is_file() or target.suffix!='.dll':raise SystemExit('missing/unexpected SDK-evaluated Linux Release TargetPath/OutputPath')
 closure=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('unexpected compiled output symlink')
  if path.is_file():closure.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 (out/(name+'-compiled.json')).write_text(json.dumps({'target':str(target.relative_to(root)),'targetSha256':digest(target),'files':closure},indent=2)+'\n')
 retained=out/'compiled'/name;retained.mkdir(parents=True,exist_ok=True)
 for extension in ('.dll','.pdb','.deps.json','.runtimeconfig.json'):
  candidate=target.with_name(target.stem+extension)
  if candidate.is_file():shutil.copyfile(candidate,retained/candidate.name)
 for stem in ('NineToOne.Accounts.Remote','NineToOne.Web'):
  for extension in ('.dll','.pdb','.deps.json','.runtimeconfig.json'):
   candidate=target.parent/(stem+extension)
   if candidate.is_file():
    destination=retained/candidate.name;shutil.copyfile(candidate,destination)
    if digest(destination)!=digest(candidate):raise SystemExit('production dependency retained SHA mismatch')
 if name in ('accounts-library-web','actual-web-full-build'):
  for extension in ('.dll','.pdb'):
   candidate=retained/('NineToOne.Accounts.Remote'+extension)
   if not candidate.is_file() or candidate.stat().st_size==0:raise SystemExit('required actual Remote primary bytes not retained')
 compiledTargets[name]=(target,closure)
 return target

verify()
nodeCode=command(['node','9to1 Workspace/Validation/Accounts.Login.Dom/login-submit.spec.mjs'],'dom-submit')
verify()
if nodeCode:raise SystemExit(nodeCode)
nodeReceipt=json.loads((out/'dom-submit.log').read_text())
if nodeReceipt.get('passed')!=4 or nodeReceipt.get('failed')!=0:raise SystemExit('DOM assertion receipt mismatch')
checks=[('legacy-handler','9to1 Workspace/Validation/Accounts.PublicClient.Specs/NineToOne.Accounts.PublicClient.Specs.csproj',None),('accounts-library-web','9to1 Workspace/Accounts/Remote/Tests/NineToOne.Accounts.Remote.Specs.csproj','PASS ASTRA_ACCOUNTS_REMOTE_LIBRARY_WEB_SYNTHETIC_ALL zero-skips')]
results=[]
for name,project,marker in checks:
 target=build_and_pin(name,project)
 restoreBefore=restore.snapshot_restore(root,project)
 expectedIdentityModel={'Microsoft.IdentityModel.Abstractions','Microsoft.IdentityModel.JsonWebTokens','Microsoft.IdentityModel.Logging','Microsoft.IdentityModel.Tokens'}
 actualIdentityModel={key.rsplit('/',1)[0]:key.rsplit('/',1)[1] for key in restoreBefore['packages'] if key.rsplit('/',1)[0] in expectedIdentityModel}
 if name=='accounts-library-web' and actualIdentityModel!={key:'8.14.0' for key in expectedIdentityModel}:raise SystemExit('actual maintained IdentityModel package version set mismatch')
 (out/(name+'-restore-before.json')).write_text(json.dumps(restoreBefore,indent=2)+'\n')
 code=command(['dotnet',str(target)],name+'-execute');assert_compiled_target_unchanged(name);verify()
 restoreAfter=restore.snapshot_restore(root,project)
 (out/(name+'-restore-after.json')).write_text(json.dumps(restoreAfter,indent=2)+'\n')
 if restoreBefore!=restoreAfter:raise SystemExit('restored graph/package payload changed during actualtest')
 log=(out/(name+'-execute.log')).read_text()
 if marker is None:marker='CAKE public HTTP client controlled-handler fixtures passed; no real login/server/principal acceptance.'
 passed=code==0 and log.count(marker)==1
 results.append({'name':name,'exit':code,'marker':marker,'acceptance':passed,'scope':'Synthetic maintainedJOSE/controlledtransport/reducer or legacyhandler only; controlledWorkerAPIclientandmaintainedsyntheticJOSE, no real HTTP700/login/currentrevocation'})
 (out/'receipt.json').write_text(json.dumps({'commit':commit,'results':results,'node':nodeReceipt},indent=2)+'\n')
 if not passed:raise SystemExit(code or 1)
verify()

webTarget=build_and_pin('actual-web-full-build','9to1 Workspace/Web/NineToOne.Web.csproj')
webRestoreBefore=restore.snapshot_restore(root,'9to1 Workspace/Web/NineToOne.Web.csproj')
webIdentityModel={key.rsplit('/',1)[0]:key.rsplit('/',1)[1] for key in webRestoreBefore['packages'] if key.rsplit('/',1)[0] in expectedIdentityModel}
if webIdentityModel!={key:'8.14.0' for key in expectedIdentityModel}:raise SystemExit('actual fullWeb maintained IdentityModel package version mismatch')
assert_compiled_target_unchanged('actual-web-full-build');verify()
webRestoreAfter=restore.snapshot_restore(root,'9to1 Workspace/Web/NineToOne.Web.csproj')
if webRestoreBefore!=webRestoreAfter:raise SystemExit('actual Web restored closure changed')
(out/'actual-web-full-build-restore-before.json').write_text(json.dumps(webRestoreBefore,indent=2)+'\n')
(out/'actual-web-full-build-restore-after.json').write_text(json.dumps(webRestoreAfter,indent=2)+'\n')
(out/'actual-web-restore.json').write_text(json.dumps(webRestoreBefore,indent=2)+'\n')
(out/'actual-web-build-receipt.json').write_text(json.dumps({'target':str(webTarget.relative_to(root)),'sha256':digest(webTarget),'scope':'Actual fullWeb compile only; no mountedhost/browser/backend/Homeauthority'},indent=2)+'\n')
