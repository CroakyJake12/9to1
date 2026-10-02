"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/android14-sdk';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-android14-cut.json':raise SystemExit('unexpected self-manifest path')
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
sdkProject='9-1 OS (Android)/src/Haven.Android/Haven.Android.csproj'
sdkBase=['--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:AndroidKeyStore=false']
sdkBase+=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/root14-android-sdk-build'),'-p:IncludeProjectNameInArtifactsPaths=true']
code=command(['dotnet','restore',sdkProject,*sdkBase,'-p:Configuration=Debug'],'android-sdk-restore');verify()
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
spec=importlib.util.spec_from_file_location('actual_restore',root/'.github/scripts/astra-auth-restore-evidence.py');restore=importlib.util.module_from_spec(spec);spec.loader.exec_module(restore)
restoreBefore=restore.snapshot_restore(root,sdkProject,sdk_context=True)
(out/'android-sdk-restore-before.json').write_text(json.dumps(restoreBefore,indent=2)+'\n')
code=command(['dotnet','build',sdkProject,'-c','Debug','-f','net10.0-android','--no-restore',*sdkBase,actualTaskProperty],'android-sdk-build');verify()
if code:raise SystemExit(code)
props=['-p:Configuration=Debug','-p:TargetFramework=net10.0-android',*[arg for arg in sdkBase if arg!='--disable-build-servers'],actualTaskProperty]
query=subprocess.run(['dotnet','msbuild',sdkProject,'-nologo',*props,'-getProperty:TargetFramework,MSBuildProjectFullPath,TargetPath,OutputPath,IntermediateOutputPath,DefineConstants,AvaloniaBuildTasksLocation,AndroidPackageFormat,RuntimeIdentifiers','-getItem:Compile,ProjectReference'],capture_output=True,text=True)
(out/'android-sdk-evaluated.stdout').write_text(query.stdout);(out/'android-sdk-evaluated.stderr').write_text(query.stderr)
if query.returncode:raise SystemExit(query.returncode)
value=json.loads(query.stdout);evaluated=value['Properties']
if evaluated['TargetFramework']!='net10.0-android' or pathlib.Path(evaluated['MSBuildProjectFullPath']).resolve()!=root/sdkProject:raise SystemExit('actual Android project/framework mismatch')
if {x.strip() for x in re.split('[;,]',evaluated['DefineConstants']) if x.strip()} & {'ASTRA_ANDROID_NATIVE_INPUT_PROBE','ASTRA_ANDROID_CONTEXT_PROBE'}:raise SystemExit('normal SDK diagnostic define leaked')
if pathlib.Path(evaluated['AvaloniaBuildTasksLocation']).resolve()!=taskTarget:raise SystemExit('SDK actual source task differs')
intermediate=pathlib.Path(evaluated['IntermediateOutputPath']);intermediate=intermediate if intermediate.is_absolute() else (root/sdkProject).parent/intermediate;intermediate=intermediate.resolve()
cutPaths={f['path']:f['sha256'] for f in files};compileEvidence=[]
for item in value['Items']['Compile']:
 path=pathlib.Path(item.get('FullPath') or item['Identity']);path=path if path.is_absolute() else (root/sdkProject).parent/path;path=path.resolve()
 if not path.is_relative_to(root) or not path.is_file():raise SystemExit('SDK compile input missing/outside checked root')
 relative=str(path.relative_to(root));actualSha=digest(path)
 if relative in cutPaths:
  if actualSha!=cutPaths[relative]:raise SystemExit('SDK tracked compile source mismatch')
 elif not path.is_relative_to(intermediate):raise SystemExit('SDK nontracked compile input lacks actual generated-intermediate binding')
 compileEvidence.append({'path':relative,'sha256':actualSha,'bytes':path.stat().st_size,'tracked':relative in cutPaths})
required=['9-1 OS (Android)/src/Haven.Android/AndroidLauncherWidgetBindings.cs','9-1 OS (Android)/src/Haven.Android/HavenLauncherActivity.Settings.cs','9-1 OS (Android)/src/Haven.Android/WindowsNaturalSpeechOutputService.Android.cs','9to1 Workspace/shared/src/Haven.Desktop/Services/HybridNaturalSpeechOutputService.cs']
for requiredPath in required:
 if sum(row['path']==requiredPath and row['tracked'] for row in compileEvidence)!=1:raise SystemExit('actual SDK required phase/adapter/consumer source not uniquely tracked and compiled')
(out/'android-sdk-compile.json').write_text(json.dumps({'project':sdkProject,'defines':evaluated['DefineConstants'],'files':compileEvidence},indent=2)+'\n')
target=pathlib.Path(evaluated['TargetPath']).resolve()
if not target.is_relative_to(root) or not target.is_file() or target.suffix!='.dll':raise SystemExit('actual Android target missing')
closure=[{'path':str(p.relative_to(root)),'bytes':p.stat().st_size,'sha256':digest(p)} for p in sorted(target.parent.rglob('*')) if p.is_file()]
(out/'android-sdk-compiled.json').write_text(json.dumps({'target':str(target.relative_to(root)),'files':closure},indent=2)+'\n')
retained=out/'compiled'/'android-sdk';retained.mkdir(parents=True,exist_ok=True)
for ext in ['.dll','.pdb']:
 path=target.with_suffix(ext)
 if not path.is_file():raise SystemExit('SDK actual target/PDB missing')
 shutil.copyfile(path,retained/path.name)
restoreAfter=restore.snapshot_restore(root,sdkProject,sdk_context=True);(out/'android-sdk-restore-after.json').write_text(json.dumps(restoreAfter,indent=2)+'\n')
if restoreAfter!=restoreBefore:raise SystemExit('SDK actual restored graph changed during no-restore build')
assert_task_unchanged();verify()
for row in closure:
 if digest(root/row['path'])!=row['sha256']:raise SystemExit('SDK compiled closure changed after build')
(out/'android-sdk-success.json').write_text(json.dumps({'status':'ACTUAL_NORMAL_ANDROID_SDK_COMPILE_SUCCESS','commit':commit,'framework':evaluated['TargetFramework'],'project':sdkProject,'deviceRuntime':False,'apkInstalled':False,'HomeRoleOrWidgetInputAcceptance':False},indent=2)+'\n')
