"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import importlib.util
import time
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/picture19-managed';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  if name!='picture-full-test':return subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False).returncode
  process=subprocess.Popen(args,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  witnesses={}
  while process.poll() is None:
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
     if str(library.resolve()) not in maps:continue
     stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split()
     if tail[19]!=observed[pid][1]:continue
     argv=(directory/'cmdline').read_bytes().replace(b'\x00',b' ').decode(errors='replace')
     executable=pathlib.Path(os.readlink(directory/'exe')).resolve()
     dotnetExecutable=pathlib.Path(shutil.which('dotnet')).resolve()
     argvItems=(directory/'cmdline').read_bytes().split(b'\x00')
     expectedHost=(target.parent/'testhost.dll').resolve()
     if executable!=dotnetExecutable or not any(pathlib.Path(x.decode(errors='replace')).resolve()==expectedHost for x in argvItems if x):continue
     if not expectedHost.is_file() or expectedHost.is_symlink():continue
     key=str(pid)+'-'+tail[19]
     witnesses[key]={'pid':pid,'startTimeTicks':tail[19],'argv':argv,'ownedAncestorPid':process.pid,'dotnetExecutable':str(dotnetExecutable),'dotnetExecutableSha256':digest(dotnetExecutable),'testHostDll':str(expectedHost),'testHostSha256':digest(expectedHost),'mappedLibrary':str(library),'librarySha256':digest(library),'maps':maps}
    except (OSError,ValueError,IndexError):continue
   time.sleep(0.05)
  code=process.wait()
  (out/'actual-managed-native-maps.json').write_text(json.dumps({'commit':commit,'run':os.environ.get('GITHUB_RUN_ID'),'owningProcessPid':process.pid,'processExit':code,'witnesses':list(witnesses.values())},indent=2)+'\n')
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
env={'AVALONIA_TELEMETRY_OPTOUT':'1','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_USE_MSBUILD_SERVER':'0'};os.environ.update(env)
base=['-c','Release','-r','linux-x64','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True']
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
toolProjects=['framework/CUI/vendor/Avalonia/src/tools/DevAnalyzers/DevAnalyzers.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CSharp/Avalonia.Analyzers.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.CodeFixes.CSharp/Avalonia.Analyzers.CodeFixes.CSharp.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.Analyzers.VisualBasic/Avalonia.Analyzers.VisualBasic.csproj', 'framework/CUI/vendor/Avalonia/src/tools/DevGenerators/DevGenerators.csproj', 'framework/CUI/vendor/Avalonia/src/tools/Avalonia.DBus.Generators/Avalonia.DBus.Generators.csproj']
for tool in toolProjects:
 code=command(['dotnet','restore',tool,*base[2:],'-p:Configuration=Release','-p:TargetFramework=netstandard2.0','-p:EnableWindowsTargeting=true'],'tool-restore-'+pathlib.Path(tool).stem);verify()
 if code:raise SystemExit(code)
for name,project in entries:
 restoreBefore[name]=restore.snapshot_restore(root,project,toolProjects)
 (out/(name+'-restore-before.json')).write_text(json.dumps(restoreBefore[name],indent=2)+'\n')
 for item in restoreBefore[name]['projects']:
  sourceProject=item['path'];effectiveFramework='netstandard2.0' if sourceProject in toolProjects else 'net10.0';key=hashlib.sha256(sourceProject.encode()).hexdigest()[:16]
  args=['dotnet','msbuild',sourceProject,'-nologo','-m:1','-nr:false','-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource','-getProperty:TargetPath,MSBuildProjectExtensionsPath']
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

compiledTargets={}
def assert_compiled_target_unchanged(name):
 target,closure=compiledTargets[name]
 current=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('compiled dependency output became symlink')
  if path.is_file():current.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 if current!=closure:raise SystemExit('compiled entire pinned output closure changed during execution')
def build_and_pin(name,project):
 code=command(['dotnet','build',project,*base,'-f','net10.0','-p:EnableWindowsTargeting=true','--no-restore','-bl:'+str(out/(name+'-build.binlog'))],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,RuntimeIdentifier,Configuration,OutputPath'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 actualProps=json.loads(query.stdout)['Properties'];target=pathlib.Path(actualProps['TargetPath']).resolve()
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
for file in sorted((root/'artifacts/picture19-managed-build').rglob('*')):
 if file.is_symlink():raise SystemExit('generated input symlink')
 if file.is_file():generated[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
(out/'actual-generated-output-pins.json').write_text(json.dumps(generated,indent=2)+'\n')
for name,project in entries:
 graph=restoreBefore[name]
 for item in graph['projects']:
  sourceProject=item['path'];effectiveFramework='netstandard2.0' if sourceProject in toolProjects else 'net10.0';key=hashlib.sha256(sourceProject.encode()).hexdigest()[:16]
  args=['dotnet','msbuild',sourceProject,'-nologo','-m:1','-nr:false','-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/picture19-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource','-getProperty:TargetPath,MSBuildProjectExtensionsPath']
  if command(args,'postbuild-evaluated-'+key):raise SystemExit('postbuild compiler input query failed')
  data=json.loads((out/('postbuild-evaluated-'+key+'.log')).read_text())
  for items in data.get('Items',{}).values():
   for item in items:
    file=pathlib.Path(item['FullPath']).resolve();expected=admitted.get(str(file),generated.get(str(file),{}).get('sha256'))
    if not file.is_file() or expected!=digest(file):raise SystemExit('unknown postbuild generated/compiler input '+str(file))
verify()
