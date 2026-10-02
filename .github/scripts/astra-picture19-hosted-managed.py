"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import importlib.util
import time
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/picture19-managed';out.mkdir(parents=True,exist_ok=True)
import sys
sys.path.insert(0,str(pathlib.Path(".github/scripts").resolve()))
from astra_original_native_session_drain import OriginalSession

def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  if name!='picture-full-test':return subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False).returncode
  process=subprocess.Popen(args,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  session=OriginalSession(process,os.environ['ASTRA_NATIVE_SESSION_DRAIN_RECORDS'])
  launcherStartTicks=session.original[3]
  def original_chain(pid,observed):
   chain=[];visited=set()
   for _ in range(128):
    if pid in visited or pid not in observed:return None
    visited.add(pid);directory=pathlib.Path('/proc')/str(pid);stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split()
    if (int(tail[1]),tail[19])!=observed[pid]:return None
    chain.append({'pid':pid,'parent':int(tail[1]),'startTicks':tail[19]})
    if pid==process.pid:return chain if tail[19]==launcherStartTicks else None
    pid=int(tail[1])
   return None
  try:
   witnesses={}
   observedTopology={}
   while process.poll() is None:
    session.observe()
    observed={}
    for directory in pathlib.Path('/proc').iterdir():
     if not directory.name.isdigit():continue
     try:
      stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split();observed[int(directory.name)]=(int(tail[1]),tail[19])
     except (OSError,ValueError,IndexError):continue
    owned={process.pid}
    for _ in range(128):
     new={pid for pid,(parent,_) in observed.items() if parent in owned}
     if new.issubset(owned):break
     owned.update(new)
    for pid in owned:
     if pid not in observed:continue
     directory=pathlib.Path('/proc')/str(pid)
     try:
      maps=(directory/'maps').read_text()
      topologyStat=(directory/'stat').read_text();topologyTail=topologyStat[topologyStat.rfind(')')+2:].split()
      if (int(topologyTail[1]),topologyTail[19])!=observed[pid]:continue
      topologyKey=str(pid)+'-'+topologyTail[19]
      mapped=str(library.resolve()) in maps
      if topologyKey not in observedTopology and len(observedTopology)>=2048:
       raise SystemExit('owned process topology diagnostic exceeds explicit 2048 record bound')
      topology={'pid':pid,'parentPid':observed[pid][0],'startTimeTicks':topologyTail[19],'ownedAncestorPid':process.pid,'executable':os.readlink(directory/'exe'),'cwd':os.readlink(directory/'cwd'),'rawArgv':(directory/'cmdline').read_bytes().split(b'\x00'),'freshLibraryMapped':mapped}
      topology['rawArgv']=[x.decode(errors='replace') for x in topology['rawArgv']]
      if mapped:topology['rawMaps']=maps
      prior=observedTopology.get(topologyKey)
      if prior is None or mapped:observedTopology[topologyKey]=topology
      if not mapped:continue
      stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split()
      if (int(tail[1]),tail[19])!=observed[pid]:continue
      argv=(directory/'cmdline').read_bytes().replace(b'\x00',b' ').decode(errors='replace')
      executable=pathlib.Path(os.readlink(directory/'exe')).resolve()
      dotnetExecutable=pathlib.Path(shutil.which('dotnet')).resolve()
      argvItems=(directory/'cmdline').read_bytes().split(b'\x00')
      expectedHost=(target.parent/'testhost.dll').resolve()
      if not expectedHost.is_file() or expectedHost.is_symlink():continue
      legacyHost=executable==dotnetExecutable and any(pathlib.Path(x.decode(errors='replace')).resolve()==expectedHost for x in argvItems if x)
      actualAppHost=False;appHostParent=None
      if executable==pathlib.Path(appHostBinding['apphost']) and argvItems and pathlib.Path(argvItems[0].decode(errors='strict')).resolve()==executable and str(target.resolve()) in maps:
       if digest(executable)!=appHostBinding['apphostSha256'] or digest(target)!=appHostBinding['managedDllSha256']:raise SystemExit('exact compiled test host or managed assembly changed during owned observation')
       parentPid=observed[pid][0]
       if parentPid in owned and parentPid in observed:
        parentDirectory=pathlib.Path('/proc')/str(parentPid);parentStat=(parentDirectory/'stat').read_text();parentTail=parentStat[parentStat.rfind(')')+2:].split()
        if (int(parentTail[1]),parentTail[19])!=observed[parentPid]:continue
        parentExe=pathlib.Path(os.readlink(parentDirectory/'exe')).resolve();parentArgv=(parentDirectory/'cmdline').read_bytes().split(b'\x00')
        parentAfter=(parentDirectory/'stat').read_text();parentAfterTail=parentAfter[parentAfter.rfind(')')+2:].split()
        if (int(parentAfterTail[1]),parentAfterTail[19])!=observed[parentPid]:continue
        if parentExe==dotnetExecutable and any(pathlib.Path(x.decode(errors='replace')).resolve()==expectedHost for x in parentArgv if x):
         actualAppHost=True;appHostParent={'pid':parentPid,'startTimeTicks':parentTail[19],'exe':str(parentExe),'exeSha256':digest(parentExe),'rawArgv':[x.decode(errors='replace') for x in parentArgv],'testHostDll':str(expectedHost),'testHostSha256':digest(expectedHost)}
      if not legacyHost and not actualAppHost:continue
      chain=original_chain(pid,observed)
      if chain is None:continue
      key=str(pid)+'-'+tail[19]
      witnesses[key]={'pid':pid,'startTimeTicks':tail[19],'argv':argv,'ownedAncestorPid':process.pid,'launcherStartTicks':launcherStartTicks,'verifiedOriginalChain':chain,'dotnetExecutable':str(dotnetExecutable),'dotnetExecutableSha256':digest(dotnetExecutable),'testHostDll':str(expectedHost),'testHostSha256':digest(expectedHost),'admission':'exact-sdk-produced-xunit-apphost' if actualAppHost else 'original-dotnet-testhost','apphostBinding':appHostBinding if actualAppHost else None,'apphostParent':appHostParent,'mappedLibrary':str(library),'librarySha256':digest(library),'maps':maps}
     except (OSError,ValueError,IndexError):continue
    time.sleep(0.05)
   code=process.wait()
  finally:
   session.drain()
  (out/'actual-managed-native-maps.json').write_text(json.dumps({'commit':commit,'run':os.environ.get('GITHUB_RUN_ID'),'owningProcessPid':process.pid,'processExit':code,'witnesses':list(witnesses.values())},indent=2)+'\n')
  (out/'actual-owned-managed-process-topology.json').write_text(json.dumps({'commit':commit,'run':os.environ.get('GITHUB_RUN_ID'),'owningProcessPid':process.pid,'processExit':code,'originalDotnetWitnessPreserved':True,'exactProducedXunitApphostAdmission':True,'observed':list(observedTopology.values())},indent=2)+'\n')
  if not witnesses:raise SystemExit('no actual owned managed test child fresh Glycin map observed')
 return code
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-picture19-cut.json':raise SystemExit('unexpected self-manifest path')
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
# Actual immutable Glycin donor inherits RUST_LOG through clearenv/setenv; diagnostics only.
os.environ['RUST_LOG']='glycin=debug,glycin_core=debug,glycin_utils=debug'
# Genuine sandbox observation only: full mandatory Glycin suite remains unchanged.
code=command([sys.executable,'.github/scripts/astra-picture-bwrap-diagnostics.py',str(out/'sandbox-diagnostics')],'sandbox-diagnostics');verify()
if code:raise SystemExit(code)
env={'AVALONIA_TELEMETRY_OPTOUT':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_USE_MSBUILD_SERVER':'0'};os.environ.update(env)
base=['-c','Release','-r','linux-x64','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True']
artifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true']
checks=[('picture-full','9to1 Workspace/Picture/Tests/HavenOS.Images.Tests.csproj','')]
# Root pins this exact evidence file in the cut; verify names exist in actual immutable source.
provenance=json.loads((root/'.github/validation/astra-picture19-test-classes.json').read_text())
cutPaths={f['path']:f['sha256'] for f in files}
for entry in provenance['classes']:
 path=root/entry['path']
 if entry['path'] not in cutPaths or cutPaths[entry['path']]!=entry['sha256'] or digest(path)!=entry['sha256']:raise SystemExit('owning test source not pinned')
 if not re.search(r'\bclass\s+'+re.escape(entry['class'])+r'\b',path.read_text()):raise SystemExit('owning test class absent')
for dependency in provenance['dependencyPins']:
 if cutPaths.get(dependency['path'])!=dependency['sha256'] or digest(root/dependency['path'])!=dependency['sha256']:raise SystemExit('Models18 existing dependency pin mismatch')
donor='9to1 Workspace/Picture/Source/glycin'
if subprocess.check_output(['git','-C',donor,'rev-parse','HEAD'],text=True).strip()!='84bed7782d1ae4486068a9ffbde691290c119909':raise SystemExit('Glycin donor pin mismatch')
if subprocess.check_output(['git','-C',donor,'status','--porcelain','--untracked-files=no']):raise SystemExit('Glycin tracked source dirty')
for tool in ('rustc','cargo'):
 version=subprocess.check_output([tool,'--version'],text=True);(out/(tool+'-version.txt')).write_text(version)
 if not version.startswith(tool+' 1.98.1 '):raise SystemExit('unqualified Rust toolchain')

lock=root/donor/'Cargo.lock';lockSha=digest(lock)
if lockSha!='e9d07e520c5159c92dc17842e9b35c4b955915b114a8e48fd0a8f17e31b6480f':raise SystemExit('Glycin Cargo.lock pin mismatch')
os.environ['CARGO_TARGET_DIR']=str(pathlib.Path(os.environ['RUNNER_TEMP'])/'astra-picture19-fresh-glycin')
fresh=pathlib.Path(os.environ['CARGO_TARGET_DIR'])
if fresh.exists():raise SystemExit('native target not fresh')
args=['cargo','build','--locked','--release','--manifest-path',str(root/donor/'Cargo.toml'),'-p','libglycin','-p','glycin-image-rs']
if command(args,'glycin-build'):raise SystemExit('fresh Glycin build failed')
verify()
if digest(lock)!=lockSha:raise SystemExit('native lock changed')
library=fresh/'release/libglycin.so';loader=fresh/'release/glycin-image-rs'
for p in (library,loader):
 if p.is_symlink() or not p.is_file():raise SystemExit('fresh native output missing')
 nativeOut=out/'native';nativeOut.mkdir(exist_ok=True);shutil.copyfile(p,nativeOut/p.name)
 if digest(nativeOut/p.name)!=digest(p):raise SystemExit('native retained copy mismatch')
 for tool,args in [('readelf',['readelf','-d',str(p)]),('nm',['nm','-D','--defined-only',str(p)]),('ldd',['ldd',str(p)])]:
  if command(args,tool+'-'+p.name):raise SystemExit('ELF dependency inspection failed')
  if tool=='ldd' and 'not found' in (out/(tool+'-'+p.name+'.log')).read_text():raise SystemExit('unresolved native dependency')
config=root/donor/'glycin-loaders/glycin-image-rs/glycin-image-rs.conf'
if digest(config)!='58996dfbde91483ae3ab4c17d3323390e0e1c689398dd824027520371083d824':raise SystemExit('loader config source pin')
data=pathlib.Path(os.environ['RUNNER_TEMP'])/'astra-picture19-glycin-data'
if data.exists():raise SystemExit('private loader config directory not fresh')
conf=data/'glycin-loaders/2+/conf.d';conf.mkdir(parents=True);generated=conf/'glycin-image-rs.conf';generated.write_text(config.read_text().replace('@EXEC@',str(loader)))
os.environ['GLYCIN_DATA_DIR']=str(data);os.environ['LD_LIBRARY_PATH']=str(fresh/'release')+(':'+os.environ['LD_LIBRARY_PATH'] if os.environ.get('LD_LIBRARY_PATH') else '')
for key in os.environ:
 if key.startswith('GLYCIN_') and ('DISABLE' in key or 'SANDBOX' in key):raise SystemExit('sandbox downgrade env refused')
bwrap=pathlib.Path(shutil.which('bwrap') or '')
if not bwrap.is_file():raise SystemExit('genuine bubblewrap unavailable')
# Resolve exactly the real managed glycin imports against freshly compiled native symbols.
decoder=(root/'9to1 Workspace/Picture/PictureGlycinDecoder.cs').read_text()
required=set(re.findall(r'\[DllImport\(Glycin[^\n]+\] internal static extern \w+ (gly_\w+)\(',decoder))
exports=(out/'nm-libglycin.so.log').read_text()
if not required or any(not re.search(r'\b'+re.escape(x)+r'$',exports,re.M) for x in required):raise SystemExit('fresh library missing managed ABI exports')
probe=out/'fresh-glycin-loaded.py';probe.write_text("import ctypes,os,json,hashlib,pathlib\np=pathlib.Path("+repr(str(library))+ ");lib=ctypes.CDLL(str(p));symbols="+repr(sorted(required))+"\nfor name in symbols:getattr(lib,name)\nmaps=pathlib.Path('/proc/self/maps').read_text();assert str(p.resolve()) in maps\nprint(json.dumps({'pid':os.getpid(),'library':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'symbols':symbols,'actualMaps':maps}))\n")
if command([sys.executable,str(probe)],'actual-native-loaded'):raise SystemExit('fresh Glycin load failed')
(out/'native-environment.json').write_text(json.dumps({'library':str(library),'librarySha256':digest(library),'loader':str(loader),'loaderSha256':digest(loader),'bwrap':str(bwrap),'bwrapSha256':digest(bwrap),'configSourceSha256':digest(config),'privateConfigSha256':digest(generated),'GLYCIN_DATA_DIR':str(data),'LD_LIBRARY_PATH':os.environ['LD_LIBRARY_PATH'],'sandboxSelectorRequired':1},indent=2)+'\n')
import importlib.util
spec=importlib.util.spec_from_file_location('restore',root/'.github/scripts/astra-picture19-restore-evidence.py');restore=importlib.util.module_from_spec(spec);spec.loader.exec_module(restore)
restoreBefore={}
entries=[(name,project) for name,project,_ in checks]
for name,project in entries:
 code=command(['dotnet','restore',project,*base[2:],'-p:Configuration=Release','-p:TargetFramework=net10.0','-p:EnableWindowsTargeting=true'],name+'-restore');verify()
 if code:raise SystemExit(code)
hostArtifactsProps=['-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-host-build-tasks'),'-p:IncludeProjectNameInArtifactsPaths=true']
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
taskProject=hostTaskProject
toolProjects.append(taskProject)
for name,project in entries:
 restoreBefore[name]=restore.snapshot_restore(root,project,toolProjects,bootstrap=True)
 (out/(name+'-restore-before.json')).write_text(json.dumps(restoreBefore[name],indent=2)+'\n')
 for item in restoreBefore[name]['projects']:
  sourceProject=item['path'];effectiveFramework=item['effectiveFramework'];key=hashlib.sha256(sourceProject.encode()).hexdigest()[:16]
  args=['dotnet','msbuild',sourceProject,'-nologo','-m:1','-nr:false','-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource,MicroComIdl','-getProperty:TargetPath,MSBuildProjectExtensionsPath,MicroComGeneratorMSBuildDll,UseLocalMicroComBuild']
  if item.get('hostContext'):
   args=[v for v in args if not v.startswith(('-p:RuntimeIdentifier=','-p:RuntimeIdentifiers=','-p:SelfContained=','-p:ArtifactsPath='))]+['-p:ArtifactsPath='+str(root/'artifacts/picture19-host-build-tasks')]
  key+=('-host' if item.get('hostContext') else '-consumer')
  if command(args,'evaluated-'+key):raise SystemExit('actual evaluated source/generator graph failed')

# Bind actual evaluated source/generator paths to pinned source, exact materialized donors,
# or completely hashed restored package/SDK payloads; unknown physical input denies.
sdkRoot=pathlib.Path(shutil.which('dotnet')).resolve().parent
sdkFiles={}
for prefix in ('sdk','packs'):
 for file in sorted((sdkRoot/prefix).rglob('*')):
  if file.is_symlink():raise SystemExit('SDK input symlink requires explicit admission')
  if file.is_file():sdkFiles[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
(out/'sdk-input-pins.json').write_text(json.dumps(sdkFiles,indent=2)+'\n')
admitted={str((root/x['path']).resolve()):x['sha256'] for x in files}
for graph in restoreBefore.values():
 for package in graph['packages'].values():
  for file in package['files']:admitted[str((pathlib.Path(package['root'])/file['path']).resolve())]=file['sha256']
for file,evidence in sdkFiles.items():admitted[file]=evidence['sha256']
for donorPath in list(requiredMaterialized)+[donor]:
 for record in subprocess.check_output(['git','-C',donorPath,'ls-files','-z']).split(b'\0'):
  if record:
   file=root/donorPath/record.decode()
   if file.is_file() and not file.is_symlink():admitted[str(file.resolve())]=digest(file)
for log in out.glob('evaluated-*.log'):
 data=json.loads(log.read_text())
 for items in data.get('Items',{}).values():
  for item in items:
   value=item.get('FullPath')
   if not value:raise SystemExit('evaluated input missing full path')
   file=pathlib.Path(value).resolve()
   if not file.is_file() or admitted.get(str(file))!=digest(file):raise SystemExit('unknown or changed evaluated compiler/generator input: '+str(file))
(out/'evaluated-input-admission.json').write_text(json.dumps({'admittedPaths':len(admitted),'actualQueries':len(list(out.glob('evaluated-*.log'))),'qualification':'Actual physical inputs hashed; no publisher certificate trust inferred'},indent=2)+'\n')

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

# Admit the fresh host output only after bootstrap restored/input admission completed.
for name,project in entries:
 afterTaskRestore=restore.snapshot_restore(root,project,toolProjects)
 (out/(name+'-restore-after-task-build.json')).write_text(json.dumps(afterTaskRestore,indent=2)+'\n')
 if afterTaskRestore!=restoreBefore[name]:raise SystemExit('restored graph changed during isolated host compilation')

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
 code=command(['dotnet','build',project,*base,'-f','net10.0','-p:EnableWindowsTargeting=true','--no-restore','-bl:'+str(out/(name+'-build.binlog'))],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']+[actualTaskProperty]
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,RuntimeIdentifier,Configuration,OutputPath,AvaloniaBuildTasksLocation,AssemblyName,OutputType,UseAppHost,TargetExt'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 actualProps=json.loads(query.stdout)['Properties'];target=pathlib.Path(actualProps['TargetPath']).resolve()
 if pathlib.Path(actualProps['AvaloniaBuildTasksLocation']).resolve()!=taskTarget:raise SystemExit('consumer UsingTask property differs from pinned source-built task')
 assert_task_unchanged()
 outputPath=pathlib.Path(actualProps['OutputPath']);outputPath=outputPath if outputPath.is_absolute() else (root/project).parent/outputPath;outputPath=outputPath.resolve()
 if actualProps['RuntimeIdentifier']!='linux-x64' or actualProps['Configuration']!='Release' or not target.is_relative_to(outputPath) or not target.is_relative_to(root.resolve()) or not target.is_file() or target.suffix!='.dll':raise SystemExit('missing/unexpected actual Release Linux TargetPath/OutputPath')
 closure=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('unexpected compiled output symlink')
  if path.is_file():closure.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 (out/(name+'-compiled.json')).write_text(json.dumps({'target':str(target.relative_to(root)),'targetSha256':digest(target),'files':closure},indent=2)+'\n')
 retained=out/'compiled'/name;retained.mkdir(parents=True,exist_ok=True)
 for extension in ('.dll','.pdb','.deps.json','.runtimeconfig.json'):
  candidate=target.with_name(target.stem+extension)
  if candidate.is_file():shutil.copyfile(candidate,retained/candidate.name)
 global appHostBinding
 expectedAppHost=target.parent/actualProps['AssemblyName']
 if actualProps['OutputType']!='Exe' or actualProps['UseAppHost'].lower()!='true' or actualProps['TargetExt']!='.dll' or actualProps['AssemblyName']!=target.stem:raise SystemExit('actual test project is not the SDK-generated Linux apphost/managed assembly pair')
 if expectedAppHost.is_symlink() or not expectedAppHost.is_file():raise SystemExit('exact SDK-produced test apphost missing')
 hostBytes=expectedAppHost.read_bytes()
 if hostBytes[:4]!=b'\x7fELF' or hostBytes.count(target.name.encode()+b'\0')!=1:raise SystemExit('actual ELF apphost does not bind exact compiled managed test assembly')
 runtimeConfig=target.with_name(target.stem+'.runtimeconfig.json');deps=target.with_name(target.stem+'.deps.json')
 if not runtimeConfig.is_file() or not deps.is_file() or json.loads(runtimeConfig.read_text())['runtimeOptions']['tfm']!='net10.0':raise SystemExit('actual managed test runtime metadata missing/mismatched')
 pins={e['path']:e['sha256'] for e in closure}
 for required in [expectedAppHost,target,runtimeConfig,deps]:
  if pins.get(str(required.relative_to(root)))!=digest(required):raise SystemExit('actual apphost startup input absent from pinned compiled closure')
 appHostBinding={'apphost':str(expectedAppHost.resolve()),'apphostSha256':digest(expectedAppHost),'managedDll':str(target.resolve()),'managedDllSha256':digest(target),'runtimeConfig':str(runtimeConfig),'runtimeConfigSha256':digest(runtimeConfig),'deps':str(deps),'depsSha256':digest(deps),'project':project,'sdkProperties':actualProps}
 (out/(name+'-apphost-binding.json')).write_text(json.dumps(appHostBinding,indent=2)+'\n')
 shutil.copyfile(expectedAppHost,retained/expectedAppHost.name)
 compiledTargets[name]=(target,closure)
 return target
name,project,filter_value=checks[0];target=build_and_pin(name,project)
args=['dotnet','test',project,*base,'-f','net10.0','-p:EnableWindowsTargeting=true','--no-build','--no-restore','--logger','trx;LogFileName=picture-full.trx','--results-directory',str(out/'trx')]
code=command(args,'picture-full-test');assert_compiled_target_unchanged(name);verify()
restoreAfter=restore.snapshot_restore(root,project,toolProjects)
(out/'picture-full-restore-after.json').write_text(json.dumps(restoreAfter,indent=2)+'\n')
if restoreBefore[name]!=restoreAfter:raise SystemExit('restored inputs changed')
import xml.etree.ElementTree as ET
trx=list((out/'trx').glob('picture-full.trx'))
if len(trx)!=1:raise SystemExit('missing unique actualTRX')
ns='{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}';tree=ET.parse(trx[0]).getroot();counters=tree.find('.//'+ns+'Counters')
if counters is None:raise SystemExit('missing counters')
counts={k:int(v) for k,v in counters.attrib.items()};actual=tree.findall('.//'+ns+'UnitTestResult')
if code or counts.get('passed',0)<=0 or counts.get('total')!=counts.get('executed') or counts.get('passed')!=counts.get('executed') or len(actual)!=counts.get('executed') or any(x.attrib.get('outcome')!='Passed' for x in actual) or any(counts.get(k,0) for k in ('failed','error','timeout','aborted','inconclusive','notExecuted','notRunnable','skipped')):raise SystemExit('full Picture suite not allPassed zeroSkip')
ids={x.attrib['testId'] for x in actual};observed=set()
for definition in tree.findall('.//'+ns+'UnitTest'):
 method=definition.find(ns+'TestMethod')
 if method is not None and definition.attrib.get('id') in ids:observed.add(method.attrib.get('className',''))
required={x['namespace']+'.'+x['class'] for x in provenance['classes']}
if not required.issubset(observed):raise SystemExit('actual owning classes not executed: '+str(required-observed))
if digest(lock)!=lockSha or digest(library)!=digest(out/'native/libglycin.so') or digest(loader)!=digest(out/'native/glycin-image-rs'):raise SystemExit('native bytes changed')
if subprocess.check_output(['git','-C',donor,'status','--porcelain','--untracked-files=no']):raise SystemExit('native source changed')
(out/'receipt.json').write_text(json.dumps({'commit':commit,'counts':counts,'actualClasses':sorted(observed),'scope':'Fresh realGlycin mandatoryBWRAP/full Picture native/headless/Home suite only; no full nativeOS journey or highres/HDR parity'},indent=2)+'\n')
verify()

# Capture postbuild generated/compiler-source inputs separately; initial item query is not complete generated-input proof.
generated={}
for generatedRoot in (root/'artifacts/picture19-managed-build',root/'artifacts/picture19-host-build-tasks'):
 for file in sorted(generatedRoot.rglob('*')):
  if file.is_symlink():raise SystemExit('generated input symlink')
  if file.is_file():generated[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
(out/'actual-generated-output-pins.json').write_text(json.dumps(generated,indent=2)+'\n')
for name,project in entries:
 graph=restoreBefore[name]
 for item in graph['projects']:
  sourceProject=item['path'];effectiveFramework=item['effectiveFramework'];key=hashlib.sha256(sourceProject.encode()).hexdigest()[:16]
  args=['dotnet','msbuild',sourceProject,'-nologo','-m:1','-nr:false','-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource,MicroComIdl','-getProperty:TargetPath,MSBuildProjectExtensionsPath,MicroComGeneratorMSBuildDll,UseLocalMicroComBuild']+[actualTaskProperty]
  if item.get('hostContext'):
   args=[v for v in args if not v.startswith(('-p:RuntimeIdentifier=','-p:RuntimeIdentifiers=','-p:SelfContained=','-p:ArtifactsPath='))]+['-p:ArtifactsPath='+str(root/'artifacts/picture19-host-build-tasks')]
  key+=('-host' if item.get('hostContext') else '-consumer')
  if command(args,'postbuild-evaluated-'+key):raise SystemExit('postbuild compiler input query failed')
  data=json.loads((out/('postbuild-evaluated-'+key+'.log')).read_text())
  # Complete exact maintained donor MicroCom map; evaluated items must equal these source-declared pairs.
  microComProjects={
   'framework/CUI/vendor/Avalonia/src/Avalonia.Native/Avalonia.Native.csproj':[('avn.idl','Interop.Generated.cs')],
   'framework/CUI/vendor/Avalonia/src/Windows/Avalonia.Win32/Avalonia.Win32.csproj':[('WinRT/winrt.idl','WinRT/WinRT.Generated.cs'),('Win32Com/win32.idl','Win32Com/Win32.Generated.cs'),('DirectX/directx.idl','DirectX/directx.Generated.cs'),('DComposition/dcomp.idl','DComposition/DComp.Generated.cs')]
  }
  projectKey=pathlib.Path(sourceProject).resolve().relative_to(root).as_posix()
  if projectKey in microComProjects:
   nativeProject=root/projectKey
   assert admitted[str(nativeProject.resolve())]==digest(nativeProject)
   props=data['Properties'];assert props.get('UseLocalMicroComBuild','').lower()!='true'
   idls=data['Items'].get('MicroComIdl',[]);expectedPairs=microComProjects[projectKey]
   actualPairs=[(pathlib.Path(i['FullPath']).resolve().relative_to(nativeProject.parent).as_posix(),i['CSharpInteropPath'].replace(chr(92),'/'))for i in idls]
   assert len(actualPairs)==len(set(actualPairs)) and sorted(actualPairs)==sorted(expectedPairs)
   for entry in idls:
    idl=pathlib.Path(entry['FullPath']).resolve();output=(nativeProject.parent/entry['CSharpInteropPath'].replace(chr(92),'/')).resolve()
    assert (idl.relative_to(nativeProject.parent).as_posix(),output.relative_to(nativeProject.parent).as_posix()) in expectedPairs
    assert not idl.is_symlink() and not output.is_symlink() and idl.is_file() and output.is_file()
    assert admitted[str(idl)]==digest(idl)
    generator=pathlib.Path(props['MicroComGeneratorMSBuildDll']).resolve()
    assert generator.as_posix().lower().endswith('/microcom.codegenerator.msbuild/0.11.4/tools/netstandard2.0/microcom.codegenerator.msbuild.dll')
    assert admitted[str(generator)]==digest(generator)
    packageRoot=generator.parents[2];targets=packageRoot/'build/MicroCom.CodeGenerator.MSBuild.targets'
    assert admitted[str(targets.resolve())]==digest(targets)
    imported=root/'framework/CUI/vendor/Avalonia/build/MicroCOM.props';assert admitted[str(imported.resolve())]==digest(imported)
    compilePaths=[pathlib.Path(x['FullPath']).resolve()for x in data['Items']['Compile']];assert compilePaths.count(output)==1
    generated[str(output)]={'bytes':output.stat().st_size,'sha256':digest(output),'producer':'MicroCom.CodeGenerator.MSBuild/0.11.4 GenerateMicroComItems → UpdateMicroComCompileItems','projectSha256':digest(nativeProject),'idlSha256':digest(idl),'generatorSha256':digest(generator),'targetsSha256':digest(targets),'importSha256':digest(imported)}
    (out/'actual-generated-output-pins.json').write_text(json.dumps(generated,indent=2)+'\n')
  for items in data.get('Items',{}).values():
   for item in items:
    file=pathlib.Path(item['FullPath']).resolve();expected=admitted.get(str(file),generated.get(str(file),{}).get('sha256'))
    if not file.is_file() or expected!=digest(file):raise SystemExit('unknown postbuild generated/compiler input '+str(file))
verify()
