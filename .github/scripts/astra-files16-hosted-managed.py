"""Branch-only managed follow-up. Root supplies the immutable cut manifest; no Git writes."""
import argparse,hashlib,json,os,pathlib,re,subprocess,sys,shutil
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/files16-managed';out.mkdir(parents=True,exist_ok=True)
def digest(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def command(args,name):
 with (out/(name+'.log')).open('wb') as log:
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 return result.returncode
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if len(a.expected_commit)!=40 or any(c not in '0123456789abcdef' for c in a.expected_commit) or commit!=a.expected_commit or os.environ.get('GITHUB_SHA',commit)!=commit:raise SystemExit('immutable commit mismatch')
if a.manifest!='.github/validation/astra-files16-cut.json':raise SystemExit('unexpected self-manifest path')
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
checks=[('data-core', '9to1 Workspace/shared/tests/Haven.Core.Tests/Haven.Core.Tests.csproj', 'FullyQualifiedName~Haven.Core.Tests.DataRecordCreationTests|FullyQualifiedName~Haven.Core.Tests.DataRecordCreationProjectionTests|FullyQualifiedName~Haven.Core.Tests.FormDataRecordUpdateProjectionTests|FullyQualifiedName~Haven.Core.Tests.FormMarkingTests|FullyQualifiedName~Haven.Core.Tests.FormProjectRuntimeTests|FullyQualifiedName~Haven.Core.Tests.FormTableInputTests'), ('data-infrastructure', '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj', 'FullyQualifiedName~Haven.Infrastructure.Tests.DataLocalStoreAuthorityTests|FullyQualifiedName~Haven.Infrastructure.Tests.HomeDataConversationRegistrationTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormAuthoringWorkflowTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormDataPreparationTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormGraphReferenceTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormLocalStoreAuthorityTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormPublicationOriginatingActorTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormPublicationRegistrationTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormPublicationTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormResponseSessionTests|FullyQualifiedName~Haven.Infrastructure.Tests.FormsSubmissionStoreTests'), ('forms-cui-full', '9to1 Workspace/Forms/Tests/CUI/HavenOS.Forms.CUI.Tests.csproj', None), ('desktop-owning', '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj', 'FullyQualifiedName~Haven.Desktop.Tests.NativeFilesMediaOriginalStoreTests|FullyQualifiedName~Haven.Desktop.Tests.FilesNativeChildFolderReadTests|FullyQualifiedName~Haven.Desktop.Tests.FilesCompatibilityPackageContentSourceTests|FullyQualifiedName~Haven.Desktop.Tests.FilesNativeBrowserSurfaceTests|FullyQualifiedName~Haven.Desktop.Tests.FilesNativeBrowserAuthorityTests|FullyQualifiedName~Haven.Desktop.Tests.FilesExistingStoreHostAuthorityTests|FullyQualifiedName~Haven.Desktop.Tests.DataPageTests|FullyQualifiedName~Haven.Desktop.Tests.DataRecordDocumentWorkspaceHostTests|FullyQualifiedName~Haven.Desktop.Tests.DataRelationshipDocumentWorkspaceHostTests|FullyQualifiedName~Haven.Desktop.Tests.FormsNativeWorkspaceHostTests'), ('os-package', '9to1 OS/tests/NineToOne.Os.Shell.Tests/NineToOne.Os.Shell.Tests.csproj', 'FullyQualifiedName~NineToOne.Os.Shell.Tests.CompatibilityPackageInspectorTests')]
# Root pins this exact evidence file in the cut; verify names exist in actual immutable source.
provenance=json.loads((root/'.github/validation/astra-files16-test-classes.json').read_text())
cutPaths={f['path']:f['sha256'] for f in files}
for entry in provenance['classes']:
 path=root/entry['path']
 if entry['path'] not in cutPaths or cutPaths[entry['path']]!=entry['sha256'] or digest(path)!=entry['sha256']:raise SystemExit('owning test source not pinned')
 if not re.search(r'\bclass\s+'+re.escape(entry['class'])+r'\b',path.read_text()):raise SystemExit('owning test class absent')
compiledTargets={}
def assert_compiled_target_unchanged(name):
 target,closure=compiledTargets[name]
 current=[]
 for path in sorted(target.parent.rglob('*')):
  if path.is_symlink():raise SystemExit('compiled dependency output became symlink')
  if path.is_file():current.append({'path':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':digest(path)})
 if current!=closure:raise SystemExit('compiled entire pinned output closure changed during execution')
def build_and_pin(name,project):
 code=command(['dotnet','build',project,*base,'-f','net10.0','-p:EnableWindowsTargeting=true'],name+'-build');verify()
 if code:raise SystemExit(code)
 props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']
 query=subprocess.run(['dotnet','msbuild',project,'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath'],capture_output=True,text=True)
 (out/(name+'-target-path.stdout')).write_text(query.stdout);(out/(name+'-target-path.stderr')).write_text(query.stderr)
 if query.returncode:raise SystemExit(query.returncode)
 target=pathlib.Path(query.stdout.strip()).resolve()
 if not target.is_relative_to(root.resolve()) or not target.is_file() or target.suffix!='.dll' or 'linux-x64' not in target.parts:raise SystemExit('missing/unexpected actual Linux TargetPath')
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
harness=provenance['consoleHarness'];harnessPath=root/harness['path']
if cutPaths.get(harness['path'])!=harness['sha256'] or digest(harnessPath)!=harness['sha256']:raise SystemExit('actual console/interprocess harness not pinned')
consoleProject='9to1 Workspace/Files/CUI.Tests/HavenOS.Files.CUI.Tests.csproj'
target=build_and_pin('cui-domain',consoleProject)
code=command(['dotnet',str(target)],'cui-domain');assert_compiled_target_unchanged('cui-domain');verify()
consoleLog=(out/'cui-domain.log').read_text()
console={'suite':'cui-domain','processExit':code,'targetSha256':digest(target),'acceptance':'passed-full-console-harness' if code==0 and consoleLog.splitlines().count(harness['requiredMessage'])==1 else 'failed','qualification':'Actual full domain/interprocess console completion, no fabricated TRX test count'}
(out/'console-receipt.json').write_text(json.dumps(console,indent=2)+'\n')
if console['acceptance']=='failed':raise SystemExit(code or 1)
results=[]
for name,project,filter_value in checks:
 build_and_pin(name,project)
 args=['dotnet','test',project,*base,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(out/'trx')]
 if filter_value:args+=['--filter',filter_value]
 args+=['-f','net10.0','-p:EnableWindowsTargeting=true','--no-build','--no-restore']
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
  if code:raise ValueError('nonzero test process exit')
  record['acceptance']='passed-zero-skips'
 except (ValueError,ET.ParseError) as error:record['reason']=str(error)
 results.append(record)
 (out/'receipt.json').write_text(json.dumps({'commit':commit,'manifestSha256':a.manifest_sha,'results':results,'qualification':'Bounded managed follow-up only; no GUI/provider/login/WPE/package/Android acceptance'},indent=2)+'\n')
 if record['acceptance']!='passed-zero-skips':sys.exit(code or 1)
verify()
