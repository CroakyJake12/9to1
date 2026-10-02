"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);p.add_argument('--prepare-owning-only',action='store_true');a=p.parse_args()
root=pathlib.Path.cwd();out=root/('artifacts/games-normals-owning/native-runtime' if a.prepare_owning_only else 'artifacts/games-normals-native');out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-games-normals-cut.json':raise SystemExit('unexpected self-manifest path')
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

import urllib.request,zipfile,base64
pin=json.loads((root/'.github/validation/games-godot472-pin.json').read_text());nativeDir=out/'official-engine';nativeDir.mkdir()
def download(url,path,maximum):
 with urllib.request.urlopen(url,timeout=60)as response,path.open('wb')as stream:
  count=0
  while data:=response.read(1024*1024):
   count+=len(data)
   if count>maximum:raise SystemExit('official archive exceeds selected bounded length')
   stream.write(data)
 return count
archive=nativeDir/'Godot_v4.7.2-stable_mono_linux_x86_64.zip';sums=nativeDir/'SHA512-SUMS.txt'
assert download(pin['archiveUrl'],archive,pin['archiveBytes'])==pin['archiveBytes']
assert digest(archive)==pin['archiveSha256'] and hashlib.sha512(archive.read_bytes()).hexdigest()==pin['archiveSha512']
download(pin['officialSumsUrl'],sums,1048576)
assert digest(sums)==pin['officialSumsSha256'] and (pin['archiveSha512']+'  '+archive.name) in sums.read_text()
engineRoot=root/'artifacts/games-normal-engine'
with zipfile.ZipFile(archive)as z:
 for entry in z.infolist():
  rel=pathlib.PurePosixPath(entry.filename)
  if rel.is_absolute()or '..'in rel.parts or (entry.external_attr>>16)&0o170000==0o120000:raise SystemExit('unsafe official archive entry')
 z.extractall(engineRoot)
engine=engineRoot/'Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64'
assert digest(engine)==pin['executableSha256'];engine.chmod(0o755)
engineClosure=[{'path':str(p.relative_to(engineRoot)),'bytes':p.stat().st_size,'sha256':digest(p)}for p in sorted(engineRoot.rglob('*'))if p.is_file()]
(out/'engine-full-closure.json').write_text(json.dumps({'sourceCommit':pin['officialSourceCommit'],'archiveSha256':digest(archive),'files':engineClosure},indent=2)+'\n')
if command([str(engine),'--version'],'engine-version'):raise SystemExit('genuine engine version failed')
module=root/'9to1 Workspace/Games/Runtime';project=module/'HavenOS.Games.Runtime.csproj'
# Exact extracted official bundle supplies the source required by the retained package-source mapping.
bundleArchives=list(engineRoot.rglob('Godot.NET.Sdk.4.7.2.nupkg'))
if not bundleArchives:bundleArchives=list(engineRoot.rglob('godot.net.sdk.4.7.2.nupkg'))
assert len(bundleArchives)==1
bundleSource=bundleArchives[0].parent.resolve();assert bundleSource.is_relative_to(engineRoot.resolve())
os.environ['ASTRA_GODOT_NUPKG_SOURCE']=str(bundleSource)
(out/'actual-godot-package-source.json').write_text(json.dumps({'directory':str(bundleSource),'sdkArchiveSha256':digest(bundleArchives[0]),'packages':[{'name':p.name,'sha256':digest(p)}for p in sorted(bundleSource.glob('*.nupkg'))]},indent=2)+'\n')

if command([str(engine),'--headless','--editor','--path',str(module),'--import','--quit'],'runtime-import'):raise SystemExit('full runtime import failed')
verify()
props=['-p:Configuration=Debug','-p:TargetFramework=net10.0','-p:UseSharedCompilation=false']
if command(['dotnet','restore',str(project),'--disable-build-servers','-m:1','-nr:false',*props],'runtime-restore'):raise SystemExit('actual full Runtime restore failed')
if command(['dotnet','build',str(project),'--no-restore','--disable-build-servers','-m:1','-nr:false',*props],'runtime-build'):raise SystemExit('actual full Runtime build failed')
verify()
query=subprocess.run(['dotnet','msbuild',str(project),'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,OutputPath,ProjectAssetsFile,Configuration,TargetFramework','-getItem:Compile'],capture_output=True,text=True)
(out/'runtime-compiled-source-query.stdout').write_text(query.stdout);(out/'runtime-compiled-source-query.stderr').write_text(query.stderr)
if query.returncode:raise SystemExit(query.returncode)
evaluated=json.loads(query.stdout);properties=evaluated['Properties'];target=pathlib.Path(properties['TargetPath']).resolve()
assert properties['Configuration']=='Debug'and properties['TargetFramework']=='net10.0'and target.name=='HavenOS.Games.Runtime.dll'and target.is_file()and target.is_relative_to(module/'.godot/mono/temp/bin/Debug')
compiledSources={pathlib.Path(i['FullPath']).resolve()for i in evaluated['Items']['Compile']}
for required in ['CanonicalTriangleMesher.cs','CanonicalSceneDriver.cs','Tests/CanonicalMeshNormalsWitness.cs']:assert(module/required).resolve()in compiledSources
assets=pathlib.Path(properties['ProjectAssetsFile']);assetsBefore=digest(assets);(out/'runtime-project.assets.json').write_bytes(assets.read_bytes());packages=json.loads(assets.read_text());packagePayload=[];hashInputs=[];hashExpected={};(out/'official-packages').mkdir(exist_ok=True)
for package,library in packages['libraries'].items():
 if library['type']!='package':continue
 folder=next((pathlib.Path(f)/library['path']for f in packages['packageFolders']if(pathlib.Path(f)/library['path']).is_dir()),None);assert folder is not None
 originals=list(folder.glob('*.nupkg'));assert len(originals)==1
 raw=originals[0].read_bytes();actual512=base64.b64encode(hashlib.sha512(raw).digest()).decode();assert actual512==pathlib.Path(str(originals[0])+'.sha512').read_text().strip()
 metadata=json.loads((folder/'.nupkg.metadata').read_text());assert metadata['contentHash']==library['sha512']
 packageId,packageVersion=package.rsplit('/',1);hashInputs.append({'Archive':str(originals[0]),'PackageId':packageId,'PackageVersion':packageVersion});hashExpected[str(originals[0])]=metadata['contentHash']
 packageArchive=out/'official-packages';packageArchive.mkdir(exist_ok=True);shutil.copyfile(originals[0],packageArchive/originals[0].name)
 for f in sorted(folder.rglob('*')):
  if f.is_file():packagePayload.append({'package':package,'path':str(f.relative_to(folder)),'bytes':f.stat().st_size,'sha256':digest(f)})
sdk=pathlib.Path.home()/'.nuget/packages/godot.net.sdk/4.7.2';assert sdk.is_dir()
sdkArchives=list(sdk.glob('*.nupkg'));assert len(sdkArchives)==1
assert base64.b64encode(hashlib.sha512(sdkArchives[0].read_bytes()).digest()).decode()==pathlib.Path(str(sdkArchives[0])+'.sha512').read_text().strip()
shutil.copyfile(sdkArchives[0],out/'official-packages'/sdkArchives[0].name)
hashInputs.append({'Archive':str(sdkArchives[0]),'PackageId':'Godot.NET.Sdk','PackageVersion':'4.7.2'});hashExpected[str(sdkArchives[0])]=json.loads((sdk/'.nupkg.metadata').read_text())['contentHash']
helper=root/'.github/validation/NuGet.ContentHash.Validation/Astra.NuGet.ContentHash.Validation.csproj'
helperProps=['-p:Configuration=Release','-p:UseSharedCompilation=false']
if command(['dotnet','restore',str(helper),'--disable-build-servers','-m:1','-nr:false',*helperProps],'content-validator-restore'):raise SystemExit('maintained content validator restore failed')
if command(['dotnet','build',str(helper),'--no-restore','--disable-build-servers','-m:1','-nr:false',*helperProps],'content-validator-build'):raise SystemExit('maintained content validator build failed')
hq=subprocess.run(['dotnet','msbuild',str(helper),'-nologo','-m:1','-nr:false',*helperProps,'-getProperty:TargetPath,MSBuildBinPath,ProjectAssetsFile','-getItem:Compile,ReferencePath'],capture_output=True,text=True)
(out/'content-validator-query.stdout').write_text(hq.stdout);(out/'content-validator-query.stderr').write_text(hq.stderr);assert hq.returncode==0
he=json.loads(hq.stdout);ht=pathlib.Path(he['Properties']['TargetPath']);assert ht.is_file()
assert (helper.parent/'Program.cs').resolve() in {pathlib.Path(i['FullPath']).resolve()for i in he['Items']['Compile']}
hsdk=pathlib.Path(he['Properties']['MSBuildBinPath']);sdkReader=hsdk/'NuGet.Packaging.dll';assert sdkReader.is_file() and digest(ht.parent/'NuGet.Packaging.dll')==digest(sdkReader)
sdkSupportNames=['NuGet.Packaging','NuGet.Frameworks','NuGet.Common','NuGet.Versioning','NuGet.Configuration','Newtonsoft.Json','System.Security.Cryptography.Pkcs','System.Security.Cryptography.ProtectedData']
for n in sdkSupportNames:assert digest(ht.parent/(n+'.dll'))==digest(hsdk/(n+'.dll'))
(out/'actual-sdk-dotnet.deps.json').write_bytes((hsdk/'dotnet.deps.json').read_bytes())
helperBefore=[{'path':str(p),'sha256':digest(p)}for p in sorted(ht.parent.rglob('*'))if p.is_file()]
(out/'content-validator-compiled.json').write_text(json.dumps({'compiled':helperBefore,'sdkNuGet':[{'path':str(hsdk/(n+'.dll')),'sha256':digest(hsdk/(n+'.dll'))}for n in sdkSupportNames],'helperAssetsSha256':digest(pathlib.Path(he['Properties']['ProjectAssetsFile']))},indent=2)+'\n')
hi=out/'content-validator-input.json';hi.write_text(json.dumps(hashInputs,indent=2)+'\n')
if command(['dotnet',str(ht),str(hi)],'maintained-content-hashes'):raise SystemExit('actual maintained content hash execution failed')
hr=json.loads((out/'maintained-content-hashes.log').read_text());assert len(hr['Results'])==len(hashInputs)
for r in hr['Results']:assert r['ContentHash']==hashExpected[r['Archive']]
assert digest(pathlib.Path(hr['NuGetAssembly']))==digest(sdkReader)
assert helperBefore==[{'path':str(p),'sha256':digest(p)}for p in sorted(ht.parent.rglob('*'))if p.is_file()]
(out/'content-hash-qualification.json').write_text(json.dumps({'rawArchive':'independent physical SHA512 equals .nupkg.sha512','content':'same archive maintained NuGet GetContentHash equals restore metadata and assets content identity','signerTrust':'not established'},indent=2)+'\n')

for f in sorted(sdk.rglob('*')):
 if f.is_file():packagePayload.append({'package':'Godot.NET.Sdk/4.7.2','path':str(f.relative_to(sdk)),'bytes':f.stat().st_size,'sha256':digest(f)})
(out/'runtime-package-sdk-full-payload.json').write_text(json.dumps(packagePayload,indent=2)+'\n')
def compiledClosure():return[{'path':str(p.relative_to(root)),'bytes':p.stat().st_size,'sha256':digest(p)}for p in sorted(target.parent.rglob('*'))if p.is_file()]
before=compiledClosure();(out/'runtime-compiled-before.json').write_text(json.dumps(before,indent=2)+'\n');retained=out/'compiled';retained.mkdir()
for f in target.parent.iterdir():
 if f.is_file():shutil.copyfile(f,retained/f.name)
if a.prepare_owning_only:
 verify()
 (out/'actual-owning-runtime-prepared.json').write_text(json.dumps({'engine':str(engine),'module':str(module),'compiled':before,'engineFiles':engineClosure,'engineRoot':str(engineRoot),'assets':str(assets),'assetsSha256':assetsBefore,'qualification':'Exact official runtime/module prepared only; no normals witness or owning tests acceptance'},indent=2)+'\n')
 sys.exit(0)
with (out/'actual-canonical-normals.log').open('wb')as log:
 process=subprocess.run([str(engine),'--headless','--path',str(module),'res://Tests/CanonicalMeshNormalsWitness.tscn'],stdout=log,stderr=subprocess.STDOUT,timeout=180)
code=process.returncode
verify();after=compiledClosure();assert before==after and assetsBefore==digest(assets)and digest(engine)==pin['executableSha256']
engineAfter=[{'path':str(p.relative_to(engineRoot)),'bytes':p.stat().st_size,'sha256':digest(p)}for p in sorted(engineRoot.rglob('*'))if p.is_file()]
assert engineAfter==engineClosure
(out/'engine-full-closure-after.json').write_text(json.dumps(engineAfter,indent=2)+'\n')
(out/'runtime-compiled-after.json').write_text(json.dumps(after,indent=2)+'\n')
lines=(out/'actual-canonical-normals.log').read_text(errors='replace').splitlines();markers=[json.loads(line.split('ASTRA_GAMES_CANONICAL_NORMALS=',1)[1])for line in lines if line.startswith('ASTRA_GAMES_CANONICAL_NORMALS=')]
assert code==0 and len(markers)==1
witness=markers[0];assert witness['success']is True and witness['actualEngine'].startswith('4.7.2')and witness['cases']==['actual-normal-and-identity','winding-reversal','degenerate-refusal-source-unchanged']and len(witness['normals'])==3
(out/'native-owning-receipt.json').write_text(json.dumps({'commit':commit,'manifestSha256':a.manifest_sha,'engineSourceCommit':pin['officialSourceCommit'],'engineSha256':digest(engine),'witness':witness,'qualification':'Isolated genuine native normals witness only; not whole Games installed donor, default activation or production acceptance'},indent=2)+'\n')
