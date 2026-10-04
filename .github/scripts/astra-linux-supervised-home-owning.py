"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil,time,importlib.util
import sys
sys.dont_write_bytecode = True

p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/desktop-visible-owning';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 if name=='root' and args[:2]==['dotnet','test']:
  environment={'ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE':'1','COHORT':'owning','GIT_CONFIG_COUNT':'1','GIT_CONFIG_KEY_0':'safe.directory','GIT_CONFIG_VALUE_0':str(root),'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','MSBUILDDISABLENODEREUSE':'1','AVALONIA_TELEMETRY_OPTOUT':'1','ASTRA_ACTUAL_AVALONIA_BUILD_TASKS':str(taskTarget)}
  environment.update({k:os.environ[k] for k in ('GITHUB_WORKSPACE','RUNNER_TEMP','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_ACTIONS','RUNNER_ENVIRONMENT')})
  environment.update({'ASTRA_ROOT_TEST_UID':str(setupReceipt['uid']),'ASTRA_ROOT_TEST_GID':str(setupReceipt['gid']),'ASTRA_ROOT_TEST_USER_HOME':setupReceipt['userHome'],'ASTRA_ROOT_TEST_HOME_STATE_PATH':setupReceipt['homeStatePath'],'ASTRA_ROOT_TEST_RUNTIME_DIRECTORY':setupReceipt['runtimeDirectory'],'ASTRA_ROOT_TEST_PROFILE_TOOL':setupReceipt['profileTool'],'ASTRA_ROOT_TEST_PROFILE_TOOL_MANIFEST_SHA256':setupReceipt['fixtureToolPayloadManifestSha256']})
  environment.update({'ASTRA_SUPERVISED_TEST_UID':str(setupReceipt['uid']),'ASTRA_SUPERVISED_TEST_GID':str(setupReceipt['gid']),'ASTRA_SUPERVISED_TEST_HOME':setupReceipt['userHome'],'ASTRA_NATIVE_ATOMIC_HELPER_PATH':str(nativeHelper),'ASTRA_NATIVE_ATOMIC_HELPER_SHA256':nativeSha})
  if digest(nativeHelper)!=nativeSha or digest(compiler)!=compilerBefore:raise RuntimeError('Actual source-built native helper/compiler changed before root test launch')
  privileged=['sudo','-n','env',*[k+'='+v for k,v in sorted(environment.items())],sys.executable,str(root/'.github/scripts/astra-linux-supervised-home-root-test-process.py'),'--root',str(root),'--expected-commit',commit,'--helper-sha256',digest(root/'.github/scripts/astra_original_native_session_drain.py'),'--output',str(out/'root-process')]
  with (out/'root-wrapper.log').open('wb') as log:result=subprocess.run(privileged,stdout=log,stderr=subprocess.STDOUT,check=False)
  source=out/'root-process/trx/root.trx';destination=out/'trx/root.trx';destination.parent.mkdir(parents=True,exist_ok=True)
  if source.is_file():shutil.copyfile(source,destination)
  return result.returncode
 with (out/(name+'.log')).open('wb') as log:
  if args[:2]==['dotnet','test']:
   records=out/'ordinary-process-drain'/name;records.mkdir(parents=True,exist_ok=False)
   (records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
   spec=importlib.util.spec_from_file_location('ordinary_owned_session',root/'.github/scripts/astra_original_native_session_drain.py');module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
   session=None;primary=None;code=None
   try:
    process=subprocess.Popen(args,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    session=module.OriginalSession(process,records);deadline=time.monotonic()+900
    while process.poll() is None:
     session.observe()
     if time.monotonic()>deadline:raise TimeoutError('bounded ordinary whole-suite deadline')
     time.sleep(.05)
    code=process.wait()
   except BaseException as error:primary=error
   finally:
    try:
     if session is not None:session.drain()
     if not json.loads((records/'expected-managed-launch.json').read_text()).get('drained'):raise RuntimeError('ordinary whole-suite original session cleanup unproved')
    except BaseException as cleanup:
     if primary is not None:raise BaseExceptionGroup('Ordinary test and actual original cleanup both failed',[primary,cleanup])
     raise
   if primary is not None:raise primary
   return code
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-desktop-visible-cut.json':raise SystemExit('unexpected self-manifest path')
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
verify()
controlArgs=[sys.executable,str(root/'.github/scripts/astra_hosted_credential_policy_guard_controls.py'),'--source',str(root/'.github/scripts/astra_hosted_credential_policy_guard.py'),'--source-sha256','c1817be52e8fa16f6280dfe7736e8887d603e2eb1e4ae0578c45ce43a9cce964']
if command(controlArgs,'hosted-credential-policy-inert-controls'):raise SystemExit('Source-bound inert kernel-policy controls failed')
verify();command(['dotnet','--info'],'toolchain');command(['dotnet','workload','list'],'workloads')
env={'AVALONIA_TELEMETRY_OPTOUT':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_USE_MSBUILD_SERVER':'0'};os.environ.update(env)
base=['-c','Release','-r','linux-x64','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True']
artifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/root14-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true']
base+=artifactsProps
for buildRoot in ('root14-managed-build','root14-host-build-tasks','root14-supervised-publish-build'):
 if (root/'artifacts'/buildRoot).exists():raise SystemExit('fresh isolated build root already exists: '+buildRoot)
restoreEntries=['9to1 OS/tests/NineToOne.Os.Shell.Tests/NineToOne.Os.Shell.Tests.csproj', '9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj', '9to1 OS/tests/NineToOne.Os.Supervisor.Root.Tests/NineToOne.Os.Supervisor.Root.Tests.csproj']
for project in restoreEntries:
 code=command(['dotnet','restore',project,*base[2:],'-p:Configuration=Release','-p:TargetFramework=net10.0','-p:EnableWindowsTargeting=true'],'main-restore-'+pathlib.Path(project).stem);verify()
 if code:raise SystemExit(code)
# Actual self-contained package graph restores precede ALL independent host/analyzer bootstraps.
for publishProject in ('9to1 OS/apps/9to1-shell/NineToOne.Os.Shell.csproj','9to1 OS/tests/NineToOne.Os.Supervisor.FixtureTools/NineToOne.Os.Supervisor.FixtureTools.csproj'):
 publishProps=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=true','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/root14-supervised-publish-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:EnableWindowsTargeting=true']
 code=command(['dotnet','restore',publishProject,'--disable-build-servers','-m:1','-nr:false',*publishProps],'publish-restore-'+pathlib.Path(publishProject).stem);verify()
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

# The publish graph uses its own obj tree. Restore the same declared tools there
# after application graph restores, before any frozen input/task/build snapshot.
publishBuildRoot=root/'artifacts/root14-supervised-publish-build'
publishToolRestoreReceipts=[]
for tool in toolProjects:
 publishToolProps=['-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=true','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(publishBuildRoot),'-p:IncludeProjectNameInArtifactsPaths=true','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:EnableWindowsTargeting=true']
 code=command(['dotnet','restore',tool,'--disable-build-servers','-m:1','-nr:false',*publishToolProps],'publish-tool-restore-'+pathlib.Path(tool).stem);verify()
 if code:raise SystemExit(code)
 toolQuery=subprocess.run(['dotnet','msbuild',tool,'-nologo','-m:1','-nr:false',*publishToolProps,'-getProperty:TargetFramework,MSBuildProjectFullPath,ProjectAssetsFile'],capture_output=True,text=True)
 (out/('publish-tool-framework-'+pathlib.Path(tool).stem+'.stdout')).write_text(toolQuery.stdout);(out/('publish-tool-framework-'+pathlib.Path(tool).stem+'.stderr')).write_text(toolQuery.stderr)
 if toolQuery.returncode:raise SystemExit(toolQuery.returncode)
 toolActual=json.loads(toolQuery.stdout)['Properties'];assetPath=pathlib.Path(toolActual['ProjectAssetsFile']);assetPath=assetPath if assetPath.is_absolute() else (root/tool).parent/assetPath;assetPath=assetPath.resolve()
 if toolActual['TargetFramework']!='netstandard2.0' or pathlib.Path(toolActual['MSBuildProjectFullPath']).resolve()!=(root/tool).resolve() or not assetPath.is_relative_to(publishBuildRoot) or assetPath.is_symlink() or not assetPath.is_file():raise SystemExit('actual publish analyzer framework/path mismatch')
 toolAssets=json.loads(assetPath.read_text())
 if pathlib.Path(toolAssets['project']['restore']['projectPath']).resolve()!=(root/tool).resolve() or not any(key.split('/')[0]=='netstandard2.0' for key in toolAssets['targets']):raise SystemExit('actual publish analyzer assets missing exact project or declared netstandard2.0')
 publishToolRestoreReceipts.append({'project':tool,'sourceSha256':digest(root/tool),'framework':toolActual['TargetFramework'],'assetPath':str(assetPath.relative_to(root)),'assetSha256':digest(assetPath),'properties':publishToolProps})
(out/'publish-tool-restores.json').write_text(json.dumps(publishToolRestoreReceipts,indent=2)+'\n')

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

checks=[('os', '9to1 OS/tests/NineToOne.Os.Shell.Tests/NineToOne.Os.Shell.Tests.csproj', None), ('home', '9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj', None), ('root', '9to1 OS/tests/NineToOne.Os.Supervisor.Root.Tests/NineToOne.Os.Supervisor.Root.Tests.csproj', None)]
# Root pins this exact evidence file in the cut; verify names exist in actual immutable source.
provenance=json.loads((root/'.github/validation/astra-linux-supervised-home-owning-classes.json').read_text())
cutPaths={f['path']:f['sha256'] for f in files}
for entry in provenance['classes']:
 path=root/entry['path']
 if entry['path'] not in cutPaths or cutPaths[entry['path']]!=entry['sha256'] or digest(path)!=entry['sha256']:raise SystemExit('owning test source not pinned')
 if not re.search(r'\bclass\s+'+re.escape(entry['class'])+r'\b',path.read_text()):raise SystemExit('owning test class absent')
for dependency in provenance['dependencyPins']:
 if cutPaths.get(dependency['path'])!=dependency['sha256'] or digest(root/dependency['path'])!=dependency['sha256']:raise SystemExit('Models18 existing dependency pin mismatch')
import importlib.util
restoreSpec=importlib.util.spec_from_file_location('root14_restore',root/'.github/scripts/astra-linux-supervised-home-restore-evidence.py')
restore=importlib.util.module_from_spec(restoreSpec);restoreSpec.loader.exec_module(restore)
restoredProjects={}
compiledTargets={} 
def assert_compiled_target_unchanged(name):
 target,closure=compiledTargets[name]
 current=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('compiled dependency output became symlink')
  if path.is_file():current.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 if current!=closure:raise SystemExit('compiled entire pinned output closure changed during execution')
 assert_task_unchanged()
 project,before=restoredProjects[name];after=restore.snapshot_restore(root,project)
 (out/(name+'-restore-after.json')).write_text(json.dumps(after,indent=2)+'\n')
 if before!=after:raise SystemExit('actual restored graph/package payload changed during execution')
def build_and_pin(name,project):
 code=command(['dotnet','build',project,*base,'--no-restore','-f','net10.0','-p:EnableWindowsTargeting=true'],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']+artifactsProps
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,OutputPath,RuntimeIdentifier,Configuration,AvaloniaBuildTasksLocation,DefineConstants,TargetFramework','-getItem:Compile'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 queryData=json.loads(query.stdout);evaluated=queryData['Properties'];target=pathlib.Path(evaluated['TargetPath']).resolve()
 compiledItems=queryData.get('Items',{}).get('Compile',[])
 if not compiledItems:raise SystemExit('actual SDK evaluated Compile entries absent')
 counts={};compileReceipt=[]
 for item in compiledItems:
  raw=item.get('FullPath',item.get('Identity'));source=pathlib.Path(raw);source=source if source.is_absolute() else (root/project).parent/source;source=source.resolve()
  if not source.is_relative_to(root) or source.is_symlink() or not source.is_file():raise SystemExit('actual Compile source escapes/missing/nonregular')
  relative=source.relative_to(root).as_posix();counts[relative]=counts.get(relative,0)+1
  compileReceipt.append({'path':relative,'sha256':digest(source),'bytes':source.stat().st_size,'tracked':relative in cutPaths})
  if relative in cutPaths and digest(source)!=cutPaths[relative]:raise SystemExit('actual tracked Compile source SHA differs from immutable cut')
 directory=pathlib.PurePosixPath(project).parent.as_posix()+'/'
 for entry in provenance['classes']:
  if entry['path'].startswith(directory) and counts.get(entry['path'])!=1:raise SystemExit('whole owning source absent/duplicate in actual evaluated Compile: '+entry['path'])
 (out/(name+'-actual-entry-compile.json')).write_text(json.dumps(compileReceipt,indent=2)+'\n')
 if evaluated['TargetFramework']!='net10.0' or 'HAVEN_WINDOWS_DESKTOP' in {x.strip() for x in re.split('[;,]',evaluated['DefineConstants']) if x.strip()}:raise SystemExit('portable project unexpectedly admits Windows-native symbol/framework')
 if pathlib.Path(evaluated['AvaloniaBuildTasksLocation']).resolve()!=taskTarget:raise SystemExit('consumer UsingTask property differs from pinned source-built task')
 assert_task_unchanged()
 output=pathlib.Path(evaluated['OutputPath']);output=output if output.is_absolute() else (root/project).parent/output;output=output.resolve()
 if evaluated['RuntimeIdentifier']!='linux-x64' or evaluated['Configuration']!='Release' or not output.is_relative_to(root.resolve()) or not target.is_relative_to(output) or not target.is_file() or target.suffix!='.dll':raise SystemExit('missing/unexpected SDK-evaluated Linux Release TargetPath/OutputPath')
 if not target.with_suffix('.pdb').is_file():raise SystemExit('actual SDK compiled owning PDB required')
 pdbPath=target.with_suffix('.pdb')
 pdbSpec=importlib.util.spec_from_file_location('actual_linux_portable_pdb',root/'.github/scripts/astra-linux-portable-pdb.py');pdbModule=importlib.util.module_from_spec(pdbSpec);pdbSpec.loader.exec_module(pdbModule)
 pairIdentity=pdbModule.assert_actual_pair(target.read_bytes(),pdbPath.read_bytes());documents=pdbModule.pdb_documents(pdbPath.read_bytes());trackedDocuments={};generatedIdentities={}
 for document,evidence in documents.items():
  normalized=document.removeprefix(str(root)+'/').removeprefix('/_/')
  if normalized in cutPaths:
   actualFile=root/normalized
   if not actualFile.is_file() or hashlib.new(evidence['hashName'],actualFile.read_bytes()).hexdigest()!=evidence['digest']:raise SystemExit('actual compiled tracked PDB document differs from cut source')
   trackedDocuments[normalized]=dict(evidence,sourceSha256=cutPaths[normalized])
  else:generatedIdentities[document]=evidence
 requiredPdb={entry['path'] for entry in provenance['classes'] if entry['path'].startswith(directory)}
 if not requiredPdb.issubset(trackedDocuments):raise SystemExit('whole required owning112 family source missing actual matching compiled PDB')
 (out/(name+'-actual-matching-pdb.json')).write_text(json.dumps({'identity':pairIdentity,'dllSha256':digest(target),'pdbSha256':digest(pdbPath),'trackedDocuments':trackedDocuments,'untrackedDocumentIdentitiesOnly':generatedIdentities,'requiredWholeSources':sorted(requiredPdb)},indent=2)+'\n')
 closure=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('unexpected compiled output symlink')
  if path.is_file():closure.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 (out/(name+'-compiled.json')).write_text(json.dumps({'target':str(target.relative_to(root)),'targetSha256':digest(target),'files':closure},indent=2)+'\n')
 retained=out/'compiled'/name;retained.mkdir(parents=True,exist_ok=True)
 for extension in ('.dll','.pdb','.deps.json','.runtimeconfig.json'):
  candidate=target.with_name(target.stem+extension)
  if candidate.is_file():shutil.copyfile(candidate,retained/candidate.name)
 restoreBefore=restore.snapshot_restore(root,project)
 (out/(name+'-restore-before.json')).write_text(json.dumps(restoreBefore,indent=2)+'\n')
 restoredProjects[name]=(project,restoreBefore)
 compiledTargets[name]=(target,closure)
 return target
publishSpec=importlib.util.spec_from_file_location('supervised_publish',root/'.github/scripts/astra-linux-supervised-home-publish.py');publishModule=importlib.util.module_from_spec(publishSpec);publishSpec.loader.exec_module(publishModule)
inputSpec=importlib.util.spec_from_file_location('supervised_recursive_inputs',root/'.github/scripts/astra-linux-supervised-home-input-admission.py');inputModule=importlib.util.module_from_spec(inputSpec);inputSpec.loader.exec_module(inputModule)
inputGraphs=[restore.snapshot_restore(root,project) for _,project,_ in checks]
inputGraphs += [restore.snapshot_restore(root,project,publish_context=True) for project in publishModule.ALLOWED.values()]
inputModule.capture(root,out,files,inputGraphs,requiredMaterialized,command,digest,taskTarget,postbuild=False)
verify();assert_task_unchanged()
# Fresh actual maintained native compiler/helper provenance, before signing packages.
compiler=pathlib.Path(shutil.which('cc') or '').resolve()
if not compiler.is_file() or compiler.is_symlink():raise SystemExit('actual maintained native compiler required')
nativeSource=root/'9to1 OS/native/atomic-spawn/atomic-spawn.c'
if digest(nativeSource)!='7b0568a2217319bf9dc9988af94b3c7795db5be2bbe4491dcffdab84c9ca697a':raise SystemExit('exact native05 source required')
nativeDirectory=root/'artifacts/supervised-native-helper';nativeDirectory.mkdir(exist_ok=False)
nativeHelper=nativeDirectory/'atomic-spawn'
compilerBefore=digest(compiler);compileArgs=[str(compiler),'-Wall','-Wextra','-Werror','-O2',str(nativeSource),'-o',str(nativeHelper)]
if command(compileArgs,'fresh-native-helper-compile'):raise SystemExit('actual fresh native helper build failed')
if not nativeHelper.is_file() or nativeHelper.is_symlink() or nativeHelper.read_bytes()[:4]!=b'\x7fELF' or digest(compiler)!=compilerBefore:raise SystemExit('actual fresh native helper/compiler witness failed')
nativeSha=digest(nativeHelper)
command([str(compiler),'--version'],'actual-native-compiler-version');command(['uname','-a'],'actual-native-kernel')
(out/'native-compile-receipt.json').write_text(json.dumps({'source':str(nativeSource.relative_to(root)),'sourceSha256':digest(nativeSource),'compiler':str(compiler),'compilerSha256':compilerBefore,'command':compileArgs,'helperSha256':nativeSha,'helperBytes':nativeHelper.stat().st_size,'qualification':'Actual same hosted freshly compiled helper; no reused local ELF'},indent=2)+'\n')
shutil.copyfile(nativeHelper,out/'atomic-spawn');shutil.copyfile(nativeSource,out/'atomic-spawn.c')
# Isolated explicit root lane; primitive cases are mandatory and separately qualified.
rootEnv={k:os.environ[k] for k in ('GITHUB_WORKSPACE','RUNNER_TEMP','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_ACTIONS','RUNNER_ENVIRONMENT')}
rootEnv.update({'ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE':'1','GIT_CONFIG_COUNT':'1','GIT_CONFIG_KEY_0':'safe.directory','GIT_CONFIG_VALUE_0':str(root)})
caseArgs=['sudo','-n','env',*[k+'='+v for k,v in sorted(rootEnv.items())],sys.executable,str(root/'.github/validation/astra-linux-native-required/run_required_cases.py'),'--helper',str(nativeHelper),'--helper-source',str(nativeSource),'--helper-source-sha256',digest(nativeSource),'--helper-sha256',nativeSha,'--compiler',str(compiler),'--output',str(out/'native-required16')]
if command(caseArgs,'actual-native-required16'):raise SystemExit('actual fresh native16 failed')
nativeResults=json.loads((out/'native-required16/result.json').read_text())
requiredCases={'exec_exit','eof_before_go','eof_after_exec','malformed_go','exec_error','stop','supervisor_death','descriptor_capture_refusal','healthy_lifetime','kernel_forced_ENOSYS','inherited_SIGCHLD_ignored','no_GO_startup_deadline','SCM_RIGHTS_receive_EMFILE_noFD','preexec_death_errorpipe_EOF_is_not_exec','actual_TERM_ignored_then_pidfd_KILL','root_nonparent_ECHILD_but_original_pidfd_poll'}
if nativeResults.get('status')!='ACTUAL_FRESH_NATIVE_REQUIRED16_PASSED' or len(nativeResults['cases'])!=16 or {x['name'] for x in nativeResults['cases']}!=requiredCases or digest(nativeHelper)!=nativeSha or digest(compiler)!=compilerBefore:raise SystemExit('exact all16 original native case witness required')
verify();assert_task_unchanged()
publishedReceipts={}
for publishName in ('shell','fixture-tool'):
 publishedReceipts[publishName]=publishModule.publish_and_pin(root,publishName,out,command,verify,digest,restore,taskTarget,assert_task_unchanged)
def setup_original_root_fixture():
 global setupReceipt
 setupOutput=pathlib.Path(os.environ['RUNNER_TEMP'])/('astra-synthetic-home-setup-'+os.environ['GITHUB_RUN_ID']+'-'+os.environ['GITHUB_RUN_ATTEMPT'])
 setupEnvironment={k:os.environ[k] for k in ('GITHUB_WORKSPACE','RUNNER_TEMP','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_ACTIONS','RUNNER_ENVIRONMENT')}
 setupEnvironment.update({'ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE':'1','DOTNET_EnableDiagnostics':'0'})
 setupArgs=['sudo','-n','env',*[k+'='+v for k,v in sorted(setupEnvironment.items())],sys.executable,str(root/'.github/scripts/astra-linux-supervised-home-synthetic-setup.py'),'--publish',publishedReceipts['shell']['publishDir'],'--fixture-tool',publishedReceipts['fixture-tool']['apphost'],'--expected-product-sha',publishedReceipts['shell']['apphostSha256'],'--expected-tool-sha',publishedReceipts['fixture-tool']['apphostSha256'],'--native-helper',str(nativeHelper),'--expected-helper-sha',nativeSha,'--output',str(setupOutput)]
 code=command(setupArgs,'actual-synthetic-root-setup');verify();assert_task_unchanged()
 if code:raise SystemExit(code)
 receiptRead=subprocess.run(['sudo','-n','cat',str(setupOutput/'setup-receipt.json')],capture_output=True,text=True,check=True);setupReceipt=json.loads(receiptRead.stdout)
 if setupReceipt['status']!='ISOLATED_SYNTHETIC_TEST_ISSUER_SETUP_ONLY_NOT_RUNTIME_ACCEPTANCE':raise SystemExit('synthetic setup status mismatch')
 (out/'synthetic-setup-receipt.json').write_text(json.dumps(setupReceipt,indent=2)+'\n')
results=[]
for name,project,filter_value in checks:
 build_and_pin(name,project)
 if name=="root":setup_original_root_fixture()
 args=['dotnet','test',project,*base,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(out/'trx')]
 if filter_value:args+=['--filter',filter_value]
 args+=['-f','net10.0','-p:EnableWindowsTargeting=true','--no-build','--no-restore']
 inputBefore=inputModule.capture(root,out,files,inputGraphs,requiredMaterialized,command,digest,taskTarget,postbuild=True)
 code=command(args,name);assert_compiled_target_unchanged(name);verify()
 inputAfter=inputModule.capture(root,out,files,inputGraphs,requiredMaterialized,command,digest,taskTarget,postbuild=True)
 if inputBefore!=inputAfter:raise SystemExit('actual recursive SDK/generated compiler inputs changed during runtime')
 record={'suite':name,'argv':args,'processExit':code,'counts':None,'acceptance':'failed'}
 # Require real results and zero failures/skips/not-executed; process exit alone is insufficient.
 try:
  import xml.etree.ElementTree as ET
  trx=list((out/'trx').glob(name+'.trx'))
  if len(trx)!=1:raise ValueError('missing unique test result')
  ns='{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
  tree=ET.parse(trx[0]).getroot();counters=tree.find('.//'+ns+'Counters')
  if counters is None:raise ValueError('missing counters')
  counts={k:int(v) for k,v in counters.attrib.items()};record['counts']=counts
  if counts.get('passed',0)<=0 or counts.get('executed',0)<=0:raise ValueError('no passed/executed tests')
  if counts.get('total')!=counts['executed'] or counts['executed']!=counts['passed']:raise ValueError('not every discovered test executed and passed')
  if any(counts.get(k,0)!=0 for k in ('failed','error','timeout','aborted','inconclusive','notExecuted','notRunnable','skipped')):raise ValueError('failed/skipped/not-executed test count')
  actual=tree.findall('.//'+ns+'UnitTestResult')
  if not actual or any(t.attrib.get('outcome')!='Passed' for t in actual):raise ValueError('non-passed actual test outcome')
  if len(actual)!=counts['executed']:raise ValueError('actual UnitTestResult count differs from executed')
  passedIds={x.attrib.get('testId') for x in actual if x.attrib.get('outcome')=='Passed'}
  observedClasses=set()
  for definition in tree.findall('.//'+ns+'UnitTest'):
   method=definition.find(ns+'TestMethod')
   if method is not None and definition.attrib.get('id') in passedIds:observedClasses.add(method.attrib.get('className',''))
  if filter_value:
   requiredClasses=set()
   for clause in filter_value.split('|'):
    required=clause.removeprefix('FullyQualifiedName~')
    matches={e['namespace']+'.'+e['class'] for e in provenance['classes'] if required in e['namespace']+'.'+e['class']}
    if len(matches)!=1:raise ValueError('filtered class not unique in pinned provenance: '+required)
    requiredClasses.update(matches)
  else:
   # Full suite still must execute every source-inventoried owning class within this project.
   projectDirectory=str(pathlib.PurePosixPath(project).parent)+'/'
   requiredClasses={e['namespace']+'.'+e['class'] for e in provenance['classes'] if e['path'].startswith(projectDirectory)}
  if not requiredClasses.issubset(observedClasses):raise ValueError('exact pinned owning classes missing Passed execution: '+str(sorted(requiredClasses-observedClasses)))
  if code:raise ValueError('nonzero test process exit')
  record['acceptance']='passed-zero-skips'
 except (ValueError,ET.ParseError) as error:record['reason']=str(error)
 results.append(record)
 (out/'receipt.json').write_text(json.dumps({'commit':commit,'manifestSha256':a.manifest_sha,'results':results,'qualification':'Whole112 unfiltered OS/Home/root; actual signed triple/native5/oldProcesspair3/idle/frame mandatory. Synthetic TEST publisher only; no released installed user/physical compositor/Android/defaultHOME acceptance.'},indent=2)+'\n')
 if record['acceptance']!='passed-zero-skips':sys.exit(code or 1)
verify()

for published in publishedReceipts.values():
 directory=pathlib.Path(published['publishDir']);current=[]
 for path in sorted(directory.rglob('*')):
  if path.is_symlink():raise SystemExit('published payload became symlink')
  if path.is_file():current.append({'path':path.relative_to(directory).as_posix(),'bytes':path.stat().st_size,'sha256':digest(path)})
 if current!=published['payload']:raise SystemExit('actual signed published payload changed during owning runtime')
verify();assert_task_unchanged()

if digest(nativeHelper)!=nativeSha or digest(compiler)!=compilerBefore:raise SystemExit('actual native compiler/helper changed during full owning runtime')
