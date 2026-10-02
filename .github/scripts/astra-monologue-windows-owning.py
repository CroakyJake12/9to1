"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
if sys.platform != 'win32' or os.environ.get('COHORT') != 'owning': raise SystemExit('Genuine Windows owning runtime required')
root=pathlib.Path.cwd();out=root/'artifacts/models-vision-windows-owning';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-models-vision-cut.json':raise SystemExit('unexpected self-manifest path')
manifest=root/a.manifest
if not manifest.is_file() or digest(manifest)!=a.manifest_sha:raise SystemExit('manifest pin mismatch')
cut=json.loads(manifest.read_text());files=cut.get('files');links=cut.get('gitlinks');materialized=cut.get('materializedGitlinks')
if not isinstance(files,list) or not files or not isinstance(links,list) or not isinstance(materialized,list):raise SystemExit('cut schema missing files/gitlinks/materializedGitlinks')
requiredMaterialized={'framework/CUI/vendor/Avalonia/external/XamlX': '009d4815470cf4bf71d1adbb633a5d81dcb2bb52', 'framework/CUI/vendor/Avalonia/external/Avalonia.DBus': '864a05282841bf04006890f04d11d60d1a046aa9', '9to1 Workspace/Terminal/Source/libvterm': '934bc2fbf21800ac3458a499df8820ca5fb45fd3'}
# Bind the maintained Git installation's own Bash, never PATH's WSL launcher.
gitExecPath = pathlib.Path(subprocess.check_output(['git', '--exec-path'], text=True).strip()).resolve()
if gitExecPath.name != 'git-core' or gitExecPath.parent.name != 'libexec' or gitExecPath.parent.parent.name != 'mingw64':
 raise SystemExit('Actual Windows Git installation layout unavailable')
gitRoot = gitExecPath.parent.parent.parent
gitBash = gitRoot/'bin/bash.exe'
if gitBash.is_symlink() or not gitBash.is_file():raise SystemExit('Actual maintained Git Bash executable unavailable')
bashVersion = subprocess.run([str(gitBash), '--version'], capture_output=True, text=True)
if bashVersion.returncode or 'GNU bash' not in bashVersion.stdout:raise SystemExit('Actual Git Bash version observation failed')
insideGit = subprocess.run([str(gitBash), '-c', 'git --exec-path'], capture_output=True, text=True)
gitCygpath = gitRoot/'usr/bin/cygpath.exe'
if gitCygpath.is_symlink() or not gitCygpath.is_file():raise SystemExit('Actual Git path conversion executable unavailable')
insideWindowsPath = subprocess.run([str(gitCygpath), '-w', insideGit.stdout.strip()], capture_output=True, text=True)
if insideGit.returncode or insideWindowsPath.returncode or pathlib.Path(insideWindowsPath.stdout.strip()).resolve() != gitExecPath:
 raise SystemExit('Git Bash does not resolve the same actual Git installation')
gitBashSha256 = digest(gitBash)
gitCygpathSha256 = digest(gitCygpath)
(out/'git-bash-toolchain.json').write_text(json.dumps({'gitExecPath':str(gitExecPath), 'bashPath':str(gitBash),
 'bashSha256':gitBashSha256, 'version':bashVersion.stdout, 'insideGitExecPath':insideGit.stdout.strip(), 'cygpathSha256':gitCygpathSha256, 'insideWindowsGitExecPath':insideWindowsPath.stdout.strip()},indent=2)+'\n')
def verify():
 if digest(gitBash)!=gitBashSha256 or digest(gitCygpath)!=gitCygpathSha256:raise SystemExit('Original actual Git shell toolchain changed')
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
 if subprocess.run([str(gitBash),'9to1 Workspace/shared/eng/prepare-cui-source.sh','--check'],check=False).returncode:raise SystemExit('independent clean pinned source check failed')
verify();
voiceProbe = root/'.github/scripts/astra-windows-installed-voice-preflight.ps1'
if command(['powershell.exe','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(voiceProbe)],'actual-installed-windows-voices'):raise SystemExit('Actual WinRT installed voice preflight failed; no skip/fake voice permitted')
command(['dotnet','--info'],'toolchain');command(['dotnet','workload','list'],'workloads')
env={'AVALONIA_TELEMETRY_OPTOUT':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_USE_MSBUILD_SERVER':'0'};os.environ.update(env)
base=['-c','Release','-r','win-x64','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=win-x64','-p:SelfContained=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True']
artifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/monologue-windows-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true']
base+=artifactsProps
restoreEntries=['9to1 Workspace/shared/tests/Haven.Core.Tests/Haven.Core.Tests.csproj', '9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj', '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj', '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj', '9to1 Workspace/shared/tests/Haven.Application.Tests/Haven.Application.Tests.csproj']
def framework(project):
 return 'net10.0-windows10.0.19041.0' if project.endswith('Haven.Desktop.Tests.csproj') else 'net10.0'
# Each main graph is restored immediately before its own build below.
hostArtifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/monologue-windows-host-build-tasks'),'-p:IncludeProjectNameInArtifactsPaths=true']
hostTaskProject='framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
hostTaskRestoreProps=['-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*hostArtifactsProps]
code=command(['dotnet','restore',hostTaskProject,'--disable-build-servers','-m:1','-nr:false',*hostTaskRestoreProps],'avalonia-build-tasks-restore');verify()
if code:raise SystemExit(code)
# Analyzer assets are restored after each main graph; no consumer build restores.
toolProjects=['framework/CUI/vendor/Avalonia/src/tools/DevAnalyzers/DevAnalyzers.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CSharp/Avalonia.Analyzers.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CodeFixes.CSharp/Avalonia.Analyzers.CodeFixes.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.VisualBasic/Avalonia.Analyzers.VisualBasic.csproj', 'framework/CUI/vendor/Avalonia/src/tools/DevGenerators/DevGenerators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.DBus.Generators/Avalonia.DBus.Generators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Generators/Avalonia.Generators.csproj']
def restore_tools(label):
 for tool in toolProjects:
  code=command(['dotnet','restore',tool,*base[2:],'--force-evaluate','-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true'],label+'-tool-restore-'+pathlib.Path(tool).stem);verify()
  if code:raise SystemExit(code)
  toolQuery=subprocess.run(['dotnet','msbuild',tool,'-nologo','-m:1','-nr:false','-p:RuntimeIdentifier=win-x64','-p:RuntimeIdentifiers=win-x64','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True',*artifactsProps,'-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true','-getProperty:TargetFramework,MSBuildProjectFullPath,ProjectAssetsFile'],capture_output=True,text=True)
  (out/(label+'-tool-framework-'+pathlib.Path(tool).stem+'.stdout')).write_text(toolQuery.stdout);(out/(label+'-tool-framework-'+pathlib.Path(tool).stem+'.stderr')).write_text(toolQuery.stderr)
  if toolQuery.returncode:raise SystemExit(toolQuery.returncode)
  toolActual=json.loads(toolQuery.stdout)['Properties'];assetPath=pathlib.Path(toolActual['ProjectAssetsFile']);assetPath=assetPath if assetPath.is_absolute() else (root/tool).parent/assetPath
  if toolActual['TargetFramework']!='netstandard2.0' or pathlib.Path(toolActual['MSBuildProjectFullPath']).resolve()!=(root/tool).resolve() or not assetPath.resolve().is_relative_to(root) or not assetPath.is_file():raise SystemExit('actual restored analyzer framework/path mismatch')
  toolAssets=json.loads(assetPath.read_text())
  if not any(key.split('/')[0]=='netstandard2.0' for key in toolAssets['targets']):raise SystemExit('actual analyzer assets missing declared netstandard2.0')
restore_tools("initial")

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

checks=[('core', '9to1 Workspace/shared/tests/Haven.Core.Tests/Haven.Core.Tests.csproj', 'FullyQualifiedName~Haven.Core.Tests.PolicyAwareModelCatalogueTests|FullyQualifiedName~Haven.Core.Tests.ModelPermissionEvaluatorTests|FullyQualifiedName~Haven.Core.Tests.ModelPersonalityServiceTests|FullyQualifiedName~Haven.Core.Tests.VoiceReactionRuntimeTests|FullyQualifiedName~Haven.Core.Tests.VoiceProfileCatalogTests|FullyQualifiedName~Haven.Core.Tests.CallVoiceLatencyTests|FullyQualifiedName~Haven.Core.Tests.CallCoordinatorTests'), ('home', '9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj', 'FullyQualifiedName~HavenOS.Home.Tests.HomeModelPickerFeatureProviderTests|FullyQualifiedName~HavenOS.Home.Tests.HomeModelPickerRouteEditorTests|FullyQualifiedName~HavenOS.Home.Tests.HomeCompletionAuditRecoveryTests'), ('infrastructure', '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj', 'FullyQualifiedName~Haven.Infrastructure.Tests.HomeModelPickerRegistrationTests|FullyQualifiedName~Haven.Infrastructure.Tests.ModelGovernanceStoresTests|FullyQualifiedName~Haven.Infrastructure.Tests.CallRepositoryTests'), ('desktop', '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj', 'FullyQualifiedName~Haven.Desktop.Tests.WindowsOriginalSpeechContinuationTests|FullyQualifiedName~Haven.Desktop.Tests.WindowsMonologueOriginalPlaybackTests|FullyQualifiedName~Haven.Desktop.Tests.HomeModelPickerCuiSurfaceTests|FullyQualifiedName~Haven.Desktop.Tests.HomeModelPickerOriginalSessionTests|FullyQualifiedName~Haven.Desktop.Tests.VoiceSessionContinuityTests|FullyQualifiedName~Haven.Desktop.Tests.VisionWorkspaceStateStoreTests|FullyQualifiedName~Haven.Desktop.Tests.VisionRegionCropperTests|FullyQualifiedName~Haven.Desktop.Tests.VisionOneToOneTests|FullyQualifiedName~Haven.Desktop.Tests.VisionAnalysisServiceTests|FullyQualifiedName~Haven.Desktop.Tests.CallVoicePreviewControllerTests|FullyQualifiedName~Haven.Desktop.Tests.CallCompletionControllerTests|FullyQualifiedName~Haven.Desktop.Tests.CallMonologueNarrationRouteTests|FullyQualifiedName~Haven.Desktop.Tests.CallSingletonIntegrationTests|FullyQualifiedName~Haven.Desktop.Tests.ExperienceAutomationAndCallTests|FullyQualifiedName~Haven.Desktop.Tests.GlobalCallHavenSceneTests|FullyQualifiedName~Haven.Desktop.Tests.MonologueOriginalPlaybackHostTests'), ('application', '9to1 Workspace/shared/tests/Haven.Application.Tests/Haven.Application.Tests.csproj', 'FullyQualifiedName~Haven.Application.Tests.MultimodalSessionStoreTests|FullyQualifiedName~Haven.Application.Tests.MonologueSessionWorkflowTests|FullyQualifiedName~Haven.Application.Tests.MonologueOriginalPlaybackTests')]
# Root pins this exact evidence file in the cut; verify names exist in actual immutable source.
provenance=json.loads((root/'.github/validation/astra-models-vision-owning-classes.json').read_text())
cutPaths={f['path']:f['sha256'] for f in files}
for entry in provenance['classes']:
 path=root/entry['path']
 if entry['path'] not in cutPaths or cutPaths[entry['path']]!=entry['sha256'] or digest(path)!=entry['sha256']:raise SystemExit('owning test source not pinned')
 if not re.search(r'\bclass\s+'+re.escape(entry['class'])+r'\b',path.read_text()):raise SystemExit('owning test class absent')
for dependency in provenance['dependencyPins']:
 if cutPaths.get(dependency['path'])!=dependency['sha256'] or digest(root/dependency['path'])!=dependency['sha256']:raise SystemExit('Models18 existing dependency pin mismatch')
import importlib.util
restoreSpec=importlib.util.spec_from_file_location('root14_restore',root/'.github/scripts/astra-monologue-windows-restore-evidence.py')
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
 project,before=restoredProjects[name];after=restore.snapshot_restore(root,project,windows_context=True)
 (out/(name+'-restore-after.json')).write_text(json.dumps(after,indent=2)+'\n')
 if before!=after:raise SystemExit('actual restored graph/package payload changed during execution')
def build_and_pin(name,project):
 # Genuine selected graph restore replaces incompatible shared child assets. Force
 # evaluation rather than adopting a prior cohort's cached framework graph.
 code=command(['dotnet','restore',project,*base[2:],'--force-evaluate','-p:Configuration=Release','-p:TargetFramework='+framework(project),'-p:EnableWindowsTargeting=true'],name+'-main-restore');verify()
 if code:raise SystemExit(code)
 restore_tools(name)
 assert_task_unchanged()
 code=command(['dotnet','build',project,*base,'--no-restore','-f',framework(project),'-p:EnableWindowsTargeting=true'],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework='+framework(project),'-p:RuntimeIdentifier=win-x64','-p:RuntimeIdentifiers=win-x64','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']+artifactsProps
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,OutputPath,RuntimeIdentifier,Configuration,AvaloniaBuildTasksLocation,DefineConstants,TargetFramework'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 evaluated=json.loads(query.stdout)['Properties'];target=pathlib.Path(evaluated['TargetPath']).resolve()
 symbols={x.strip() for x in re.split('[;,]',evaluated['DefineConstants']) if x.strip()}
 if evaluated['TargetFramework']!=framework(project) or (('HAVEN_WINDOWS_DESKTOP' in symbols) != (name=='desktop')):raise SystemExit('actual Windows native test framework/symbol mismatch')
 if pathlib.Path(evaluated['AvaloniaBuildTasksLocation']).resolve()!=taskTarget:raise SystemExit('consumer UsingTask property differs from pinned source-built task')
 assert_task_unchanged()
 output=pathlib.Path(evaluated['OutputPath']);output=output if output.is_absolute() else (root/project).parent/output;output=output.resolve()
 if evaluated['RuntimeIdentifier']!='win-x64' or evaluated['Configuration']!='Release' or not output.is_relative_to(root.resolve()) or not target.is_relative_to(output) or not target.is_file() or target.suffix!='.dll':raise SystemExit('missing/unexpected SDK-evaluated Linux Release TargetPath/OutputPath')
 if not target.with_suffix('.pdb').is_file():raise SystemExit('Actual consumer portable PDB absent')
 closure=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('unexpected compiled output symlink')
  if path.is_file():closure.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 (out/(name+'-compiled.json')).write_text(json.dumps({'target':str(target.relative_to(root)),'targetSha256':digest(target),'files':closure},indent=2)+'\n')
 retained=out/'compiled'/name;retained.mkdir(parents=True,exist_ok=True)
 for extension in ('.dll','.pdb','.deps.json','.runtimeconfig.json'):
  candidate=target.with_name(target.stem+extension)
  if candidate.is_file():shutil.copyfile(candidate,retained/candidate.name)
 restoreBefore=restore.snapshot_restore(root,project,windows_context=True)
 (out/(name+'-restore-before.json')).write_text(json.dumps(restoreBefore,indent=2)+'\n')
 restoredProjects[name]=(project,restoreBefore)
 compiledTargets[name]=(target,closure)
 return target
results=[]
for name,project,filter_value in checks:
 build_and_pin(name,project)
 args=['dotnet','test',project,*base,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(out/'trx')]
 if filter_value:args+=['--filter',filter_value]
 args+=['-f',framework(project),'-p:EnableWindowsTargeting=true','--no-build','--no-restore']
 code=command(args,name);assert_compiled_target_unchanged(name);verify()
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
  if name=='desktop':
   nativeMethods={('Haven.Desktop.Tests.WindowsOriginalSpeechContinuationTests', 'Actual_original_handle_stop_cannot_cancel_a_later_native_replacement_utterance'), ('Haven.Desktop.Tests.WindowsMonologueOriginalPlaybackTests', 'Actual_original_native_pause_ack_survives_physical_CAS_cancellation_then_resumes_same_playback_without_synthesis'), ('Haven.Desktop.Tests.WindowsOriginalSpeechContinuationTests', 'Actual_hybrid_preserves_selected_Windows_route_and_disposal_retires_paused_original_without_replay'), ('Haven.Desktop.Tests.WindowsOriginalSpeechContinuationTests', 'Actual_original_player_pauses_at_confirmed_position_resumes_without_replacement_and_retires_on_stop'), ('Haven.Desktop.Tests.WindowsOriginalSpeechContinuationTests', 'Actual_native_disposal_drains_queued_original_requests_without_replacement_playback')}
   observedMethods=set()
   for definition in tree.findall('.//'+ns+'UnitTest'):
    method=definition.find(ns+'TestMethod')
    if method is not None and definition.attrib.get('id') in passedIds:observedMethods.add((method.attrib.get('className',''),method.attrib.get('name','')))
   if not nativeMethods.issubset(observedMethods):raise ValueError('Genuine native five outcomes absent: '+str(sorted(nativeMethods-observedMethods)))
  if code:raise ValueError('nonzero test process exit')
  record['acceptance']='passed-zero-skips'
 except (ValueError,ET.ParseError) as error:record['reason']=str(error)
 results.append(record)
 (out/'receipt.json').write_text(json.dumps({'commit':commit,'manifestSha256':a.manifest_sha,'results':results,'qualification':'Genuine Windows runtime owning lane; native probe observes retained object, Pause/Resume state and physical CAS, not audible output/device acceptance. Portable and regression/resource cohorts remain independently mandatory.'},indent=2)+'\n')
 if record['acceptance']!='passed-zero-skips':sys.exit(code or 1)
verify()
