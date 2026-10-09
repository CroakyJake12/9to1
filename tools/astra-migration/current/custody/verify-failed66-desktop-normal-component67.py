"""Root-only bounded four-normal custody for focused54 receiving.
This is not a full compiler/historical/deep acceptance verifier. Original65
aggregate1 is retained. Root argv: actual66 receipt SHA observed0, exact whole
Root log SHA, independently received recovery-source peer SHA.
"""
from pathlib import Path
import gzip,hashlib,importlib.util,json,os,re,subprocess,sys,time,zipfile,xml.etree.ElementTree as ET
sys.dont_write_bytecode=True
P=Path('/workspace/astra-source/lifecycle-peer-sol61u51/desktop-component67');ROOT=Path('/workspace/astra-consolidated');SCOPE=Path('/dev/shm/astra-framework-owning-metadata-20261006-03');NORMAL_ROOT=Path('/workspace/astra-root-current-normal-output66')
assert len(sys.argv)==8,'Root requires actual66 receipt/SHA, independently observed natural0, whole external log/SHA and qualified recovery-source peer/SHA.'
receipt=Path(sys.argv[1]);receipt_sha=sys.argv[2];natural=int(sys.argv[3]);log=Path(sys.argv[4]);log_sha=sys.argv[5];peer_path=Path(sys.argv[6]);peer_sha=sys.argv[7]
assert natural==1
def pin(p):
 p=Path(p);h=hashlib.sha256();n=0
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b);n+=len(b)
 return {'path':str(p),'bytes':n,'sha256':h.hexdigest()}
def verify(raw,p):assert len(raw)==p['bytes'] and hashlib.sha256(raw).hexdigest()==p['sha256'],p
assert pin(receipt)['sha256']==receipt_sha and pin(log)['sha256']==log_sha and pin(peer_path)['sha256']==peer_sha
r=json.loads(gzip.decompress(receipt.read_bytes()));HEAD=r['headAtCapture'];assert HEAD==r['headAtCompletion'] and r['exitCode']==1 and r['status']=='ACTUAL_NORMAL_CLOSURE_RECOVERY66_FAILURE_PRESERVED'
assert receipt==Path('/workspace/astra-source/a4-current-targeted-owning-compiler66/output/CURRENT-TARGETED-OWNING-COMPILER66-RECEIPT.json.gz')
terminal_prefix=b'CURRENT_TARGETED66_ORIGINAL_TERMINAL 1 INPUTS_UNCHANGED True RECEIPT '
terminals=[line for line in log.read_bytes().splitlines() if line.startswith(terminal_prefix)];assert len(terminals)==1 and json.loads(terminals[0][len(terminal_prefix):])==pin(receipt)
peer=json.loads(peer_path.read_bytes());assert peer['status']=='QUALIFIED_INDEPENDENT_SOURCE_PASS_CURRENT_ROOT_NORMAL_RECOVERY66_RUNTIME_UNEXECUTED'
assert peer['recipe']==r['exactSelectiveCompilerSource']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/normal-recovery66/run-current-root-normal-closure-recovery66b.py', 'bytes': 194693, 'sha256': '08133af2be944d46e4086fbd2e1e5e1882642f270cba261e670f3c0833b58c80'} and pin(peer['recipe']['path'])==peer['recipe']
sourcecheck_pin=peer['selfcheck'];assert pin(sourcecheck_pin['path'])==sourcecheck_pin;sourcecheck=json.loads(Path(sourcecheck_pin['path']).read_bytes())
assert sourcecheck['status']=='SOURCE_AND_BOUNDED_CURRENT_ROOT_NORMAL_RECOVERY66_SELF_CHECK_PASS_NO_SDK_RUNTIME' and sourcecheck['head']==HEAD and sourcecheck['ticket']==r['ticket'] and sourcecheck['recipe']==peer['recipe']
assert sourcecheck['all33OtherFunctionsPreservedNormalizedAst'] and sourcecheck['noNewCscRequested'] and not sourcecheck['sdkTestsModelsConsoleInvoked']
assert r['normalClosureRecoveryOnly'] and r['compilerRequested'] is False and r['compilerOnly'] is False
assert not any(r[key] for key in ('actualFreshCscOwners','changedInputs','unavailableInputsAtCompletion','independentTestCompilerFailures','desktopCompilerFailures','desktopRuntimeOutputFailures','owningTestRuntimeOutputFailures','testsExecuted','testDiscoveryExecuted','modelExecuted','consoleExecuted'))
assert r['inputsUnchangedAtCompletion'] and r['componentBasisOriginalNaturalExitCode']==1 and r['componentBasisIsQualifiedFailed65IndividualComponents'] and r['preservedFailed65NeverClaimedFullSuccessfulInterval']
custody_pin=r['actualFailed65IndividualComponentCustody'];assert custody_pin==sourcecheck['actualFailed65IndividualComponentCustody']==peer['actualFailed65IndividualComponentCustody']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/failed65-components66/CURRENT65-FAILED-INTERVAL-INDIVIDUAL-COMPONENT-CUSTODY04.json', 'bytes': 13676396, 'sha256': 'b5f2b2a99acd1f5c77380916e269ee3926611bbc42627854c70880a158511da8'} and pin(custody_pin['path'])==custody_pin
custody=json.loads(Path(custody_pin['path']).read_bytes());assert custody['status']=='ACTUAL_CURRENT65_FAILED_INTERVAL_INDIVIDUAL_COMPONENT_CUSTODY_PASS_NO_NORMAL_OR_RUNTIME_ACCEPTANCE'
assert custody['originalNaturalExitCode']==1 and not custody['fullCompilerIntervalAccepted'] and not custody['normalClosureAccepted'] and not custody['runtimeReady'] and not custody['actualNormalRuntimeClosures'] and custody['head']==HEAD
original_pin=r['actualFailed65ComponentCompilerReceipt'];assert original_pin==custody['originalReceipt']==custody['currentCompilerReceipt']==sourcecheck['actualFailed65ComponentCompilerReceipt']=={'path': '/workspace/astra-source/a4-current-targeted-owning-compiler65/output/CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz', 'bytes': 4844261, 'sha256': 'c9bba5c0ad33af23580ebf2eed255afa6ca731c6541b99d6a4797346fb77c64f'} and pin(original_pin['path'])==original_pin
original=json.loads(gzip.decompress(Path(original_pin['path']).read_bytes()));assert original['exitCode']==1 and original['inputsUnchangedAtCompletion'] and original['headAtCapture']==original['headAtCompletion']==HEAD
assert len(original['actualFreshCscOwners'])==20 and set(original['genuinePriorCompilerReusedCscOwners'])=={'Haven.Core','Haven.PluginFixture'}
assert r['actualFreshCscOwners']==[] and len(r['outputs'])==len(r['modulePlans'])==len(r['actualReuseDecisions'])==22 and set(r['genuinePriorCompilerReusedCscOwners'])=={output['module'] for output in original['outputs']}
assert not r['genuineUnchangedC43TestRuntimeClosureManifests']
ticket=Path(r['ticket']['path']);assert pin(ticket)==r['ticket']==custody['ticket'];t=json.loads(ticket.read_bytes());assert t['afterHead']==HEAD and len(t['afterRows'])==501
assert subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()==HEAD
assert not subprocess.check_output(['git','-C',str(ROOT),'status','--porcelain=v1','--untracked-files=no'],text=True)
assert r['sourceRows']==original['sourceRows'] and r['exactCurrentControllingSourceOverrides']==original['exactCurrentControllingSourceOverrides'] and r['explicitReviewedSourceRetirements']==original['explicitReviewedSourceRetirements']
for row in r['sourceRows']:verify((ROOT/row['target']).read_bytes(),row)
assert len(r['sourceRows'])==648 and len({row['target'] for row in r['sourceRows']})==648
helper=Path(r['exactResourceCustodyHelper']['path']);registry_path=Path(r['closedLayerArchiveRegistry']['path']);assert pin(helper)==r['exactResourceCustodyHelper'] and pin(registry_path)==r['closedLayerArchiveRegistry']
spec=importlib.util.spec_from_file_location('focused67_custody',helper);lib=importlib.util.module_from_spec(spec);spec.loader.exec_module(lib);registry=json.loads(registry_path.read_bytes())
def decode(row):
 if row.get('encoding')==lib.SCHEMA:body=lib.decode_archive(row,registry)
 else:
  encoded,_=lib.read_original_gzip(row['archive']['path'],row['archive'],registry);body=gzip.decompress(encoded)
 verify(body,row['original']);return body
def runtime_item_readback(item):
 src=item['restoration']
 if src['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':
  cp=src['container']
  if cp['path'] not in containers:assert pin(cp['path'])==cp;containers[cp['path']]=cp
  else:assert containers[cp['path']]==cp
  key=(cp['sha256'],src['member'],item['original']['bytes'],item['original']['sha256'])
  if key not in member_proofs:
   with zipfile.ZipFile(cp['path']) as z:
    info=z.getinfo(src['member']);assert info.file_size==src['memberBytes']==item['original']['bytes'] and info.CRC==src['memberCRC32']
    h=hashlib.sha256();count=0
    with z.open(info) as stream:
     for chunk in iter(lambda:stream.read(65536),b''):h.update(chunk);count+=len(chunk)
   assert count==item['original']['bytes'] and h.hexdigest()==src['memberSha256']==item['original']['sha256'];member_proofs[key]=True
 else:assert src['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE';verify(decode(src['row']),item['original'])
 return {'original':item['original'],'sourceKind':src['kind'],'wholeRestorationVerified':True}
def direct_directory(path):
 path=Path(path);assert path.is_absolute() and path.resolve()==path and path.is_dir() and not path.is_symlink(),path
 for parent_path in path.parents:assert not parent_path.is_symlink(),parent_path
 return path.stat()
def mount_fields(line):
 fields=line.split();separator=fields.index('-')
 def unescape(value):
  for encoded,decoded in (('\\040',' '),('\\011','\t'),('\\012','\n'),('\\134','\\')):value=value.replace(encoded,decoded)
  return value
 return {'device':fields[2],'mountPoint':unescape(fields[4]),'filesystemType':fields[separator+1]}
def workspace_filesystem_readback(witness):
 assert witness['path']=='/workspace'
 workspace_stat=direct_directory('/workspace');assert workspace_stat.st_dev==witness['device']
 observed=mount_fields(witness['mountInfo'])
 assert observed['device']==str(os.major(workspace_stat.st_dev))+':'+str(os.minor(workspace_stat.st_dev))
 assert observed['filesystemType']==witness['filesystemType'] and observed['mountPoint']==witness['mountPoint']
 assert witness['filesystemType'] not in ('tmpfs','ramfs','devtmpfs'),witness
 candidates=[]
 for line in Path('/proc/self/mountinfo').read_text().splitlines():
  fields=mount_fields(line);mount=Path(fields['mountPoint'])
  if Path('/workspace').is_relative_to(mount):candidates.append((len(mount.parts),fields))
 assert candidates
 current=max(candidates,key=lambda item:item[0])[1]
 assert current==observed,('Workspace backing filesystem changed',current,observed)
 return {'path':'/workspace','device':workspace_stat.st_dev,'filesystemType':current['filesystemType'],'mountPoint':current['mountPoint'],'currentPhysicalBackingVerified':True,'historicalMountInfo':witness['mountInfo']}
containers={};member_proofs={};plans={p['module']:p for p in r['modulePlans']};oldplans={p['module']:p for p in original['modulePlans']};outputs={o['module']:o for o in r['outputs']};oldoutputs={o['module']:o for o in original['outputs']};decisions={d['module']:d for d in r['actualReuseDecisions']};stages={stage['stage']:stage for stage in r['stages']}
assert len(stages)==len(r['stages']) and all(stage['exitCode']==0 for stage in stages.values()) and not any(name.endswith('-compiler01') for name in stages)
archive_index={}
for row in r['outputArchives']:
 for path in row['originalPaths']:archive_index[(path,row['original']['bytes'],row['original']['sha256'])]=row
readbacks=[]
for module,plan in plans.items():
 old=oldplans[module];output=outputs[module];origin=oldoutputs[module];decision=decisions[module]
 assert not plan['missingItems'] and plan['properties']==old['properties'] and plan['canonicalProject']==old['canonicalProject'] and plan['project']==old['project']
 assert plan['physicalItems']==old['physicalItems'] and plan['actualUpstreamReferenceClosure']==old['actualUpstreamReferenceClosure']
 assert [(ref['kind'],ref['input']) for ref in plan['actualReferencePins']]==[(ref['kind'],ref['input']) for ref in old['actualReferencePins']]
 for before_ref,current_ref in zip(old['actualReferencePins'],plan['actualReferencePins']):assert {key:value for key,value in before_ref['metadata'].items() if key!='AccessedTime'}=={key:value for key,value in current_ref['metadata'].items() if key!='AccessedTime'}
 assert decision==plan['targetedCompilerDecision'] and not decision['freshCscRequired'] and decision['completeSourceItemResourceOrderEqual'] and decision['actualCompilerRefsAndAnalyzersWholeEqual'] and decision['allResolvedRefsWholeEqual'] and decision['unreviewedAbiWaiver'] is False
 assert not output['compiledInCurrentInterval'] and output['originalComponentCompiledInActual65Interval']==origin['compiledInCurrentInterval'] and output['compiledSourceHead']==origin['compiledSourceHead']
 assert output['reusedFromActualCompilerReceipt']==original_pin and output['reusedFromActualIndividualComponentCustody']==custody_pin and output['reusedReceiptOriginalNaturalExitCode']==1 and output['completeSourceItemResourceOrderEqual'] and output['actualCompilerReferenceAndAnalyzerBytesEqual']
 for key in ('output','target','pdb','reference'):
  assert output.get(key)==origin.get(key)
  if output.get(key):descriptor=output[key];verify(decode(archive_index[(descriptor['path'],descriptor['bytes'],descriptor['sha256'])]),descriptor)
 for field,suffix in (('evaluation','-evaluation01'),('references','-references01')):
  descriptor=plan[field];assert pin(descriptor['path'])==descriptor and output[field]==descriptor
  body=json.loads(gzip.decompress(Path(descriptor['path']).read_bytes()));assert body==json.loads(decode(stages[module+suffix]['exactWholeStdoutCustody']))
 evaluated=json.loads(gzip.decompress(Path(plan['evaluation']['path']).read_bytes()));assert evaluated['Properties']==plan['properties']
 readbacks.append({'module':module,'original65CompiledInInterval':origin['compiledInCurrentInterval'],'originalCompiledSourceHead':origin['compiledSourceHead'],'wholeCurrentSourceResourcePropertiesRefsAnalyzerUpstreamEqual':True,'newCscInvoked':False})
required={'Haven.Desktop'}
assert r['actualOwningTestRuntimeClosureManifests']==[] and r['releasedNewTaskOwnedRuntimeCopies']==[] and r['wholeReleasedCopyRestorationRevalidation']==[]
assert pin(receipt)=={'path':str(receipt),'bytes':3255894,'sha256':'d7f0d94403dbd4efc96973a0471475ab9b1bc811f90715720e12f0e0d3d53d23'}
assert r['actualPrimaryException']['type']=='AssertionError' and r['actualPrimaryException']['message']=='' and "assert compiled_now and all(q['compilerDecisionComplete'] for q in runtime_queue)" in r['actualPrimaryException']['actualTraceback']
manifest_rows=[{'module':'Haven.Desktop','manifest':r['actualDesktopRuntimeClosureManifest']},*r['actualOwningTestRuntimeClosureManifests']]
manifest_rows=[row for row in manifest_rows if row['module'] in required];assert {row['module'] for row in manifest_rows}==required and len(manifest_rows)==1
normal_readbacks=[];layout_readbacks=[];file_readbacks=[];layout_map={layout['module']:layout for layout in r['actualDurableNormalLayouts']};released={row['original']['path']:row for row in r['releasedNewTaskOwnedRuntimeCopies']}
workspace=workspace_filesystem_readback(r['durableNormalAllocation']['workspaceFilesystem']);assert direct_directory(NORMAL_ROOT).st_dev==workspace['device']
for row in manifest_rows:
 module=row['module'];mp=row['manifest'];assert pin(mp['path'])==mp;v=json.loads(gzip.decompress(Path(mp['path']).read_bytes()));layout=layout_map[module];plan=plans[module]
 expected_status='ACTUAL_NORMAL_DESKTOP_RUNTIME_OUTPUT_CLOSURE_PASS' if module=='Haven.Desktop' else 'ACTUAL_NORMAL_OWNING_TEST_RUNTIME_OUTPUT_CLOSURE_PASS'
 assert v['status']==expected_status and v['head']==HEAD and v['ticket']==r['ticket'] and v['allActualCompiledProductIdentitiesUnchanged'] and v['noSecondCompiler'] and v['actualRuntimeStage']['exitCode']==0
 assert v['actualDurableNormalLayout']==layout and v['actualOriginalOutputDirectory']==layout['targetDirectory']==str(NORMAL_ROOT/module)
 assert layout['originalCompilerProperties']==plan['properties'] and layout['compilerIntermediateAssetsRefIdentityUnchanged'] and layout['ownerConditionalOutDirOutputPathOnly'] and layout['sameFlagsUsedForNormalStage']
 assert layout['actualEvaluatedProperties']['TargetPath']==str(NORMAL_ROOT/module/Path(plan['properties']['TargetPath']).name)
 assert json.loads(gzip.decompress(Path(layout['actualEvaluationJson']['path']).read_bytes()))==json.loads(decode(layout['actualEvaluationStage']['exactWholeStdoutCustody']))
 assert v['actualRuntimeStage']==stages[module+('-normal-runtime-output01' if module=='Haven.Desktop' else '-normal-test-runtime-output01')]
 normal_log=decode(v['actualRuntimeStage']['exactWholeStdoutCustody']);assert b'Compilation request ' not in normal_log and not re.search(rb'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\x22]|$)',normal_log)
 provenance=v['actualCompilerSuccessProvenance'];assert provenance['currentCompilerInvoked'] is False and provenance['compiledSourceHead']==HEAD and provenance['originalComponentCompiledInActual65Interval'] is True and provenance['originalCompilerIntervalNaturalExitCode']==1 and provenance['failed65FullIntervalNeverPromoted'] and provenance['actualQualifiedComponentCustody']==custody_pin
 original_stage=next(stage for stage in original['stages'] if stage['stage']==module+'-compiler01');assert provenance['actualOriginalCompilerStage']==original_stage and provenance['actualOriginalNaturalExitCode']==0 and provenance['actualOriginalCompilerReceipt']==original_pin
 assert v['fileCount']==len(v['files']) and len({item['relative'] for item in v['files']})==v['fileCount']
 required_files={plan['properties']['AssemblyName']+suffix for suffix in ('.dll','.deps.json','.runtimeconfig.json')}
 if module!='Haven.Desktop':required_files.add('testhost.dll');assert not v['testsExecuted'] and not v['testDiscoveryExecuted'] and v['actualCompiledAssembly']==outputs[module]['output']
 assert required_files<={item['relative'] for item in v['files']}
 for item in v['files']:
  path=Path(item['original']['path']);assert path==NORMAL_ROOT/module/item['relative'] and path.is_relative_to(NORMAL_ROOT/module)
  assert item['device']==workspace['device'] and isinstance(item['inode'],int) and item['inode']>0 and isinstance(item['nlink'],int) and item['nlink']>0
  proof=runtime_item_readback(item);assert path.is_file() and path.resolve()==path and not path.is_symlink()
  st=path.stat();assert st.st_dev==item['device'] and st.st_ino==item['inode'] and st.st_nlink==item['nlink'] and st.st_mtime_ns==item['mtimeNs'] and st.st_mode==item['mode'];assert pin(path)==item['original']
  file_readbacks.append({**proof,'physicalAtComponentQualification':True,'device':st.st_dev,'inode':st.st_ino,'nlink':st.st_nlink,'mode':st.st_mode,'mtimeNs':st.st_mtime_ns})
 assert sum(item['original']['bytes'] for item in v['files'])==v['wholeCopiedBytes']
 normal_readbacks.append({'module':module,'manifest':mp,'files':v['fileCount'],'wholeBytes':v['wholeCopiedBytes'],'actualCompilerSuccessProvenance':provenance,'normalOutputFilesFullyReconstructable':True})
 layout_readbacks.append({'module':module,'freshManifest':mp,'targetDirectory':layout['targetDirectory'],'actualDurableLayout':layout})
assert len(file_readbacks)==264 and len(normal_readbacks)==1 and normal_readbacks[0]['wholeBytes']==944837269
normal_manifest=r['actualDesktopRuntimeClosureManifest'];assert normal_manifest=={'path':'/workspace/astra-source/a4-current-targeted-owning-compiler66/output/Haven.Desktop-NORMAL-RUNTIME-OUTPUT-CLOSURE01.json.gz','bytes':36885,'sha256':'d6b32d8406172b0e2728af0c49d9b86d09f5b382763e6a661e5c9099607214fe'}
normal_directory=NORMAL_ROOT/'Haven.Desktop';assert normal_directory.resolve()==normal_directory and not normal_directory.is_symlink()
physical_paths=[path for path in normal_directory.rglob('*') if path.is_file()];assert {str(path) for path in physical_paths}=={item['original']['path'] for item in file_readbacks}
physical_directories=[normal_directory,*[path for path in normal_directory.rglob('*') if path.is_dir()]]
directory_observations=[]
for path in physical_directories:
 st=direct_directory(path);assert st.st_dev==workspace['device'];directory_observations.append({'path':str(path),'device':st.st_dev,'inode':st.st_ino,'mode':st.st_mode})
# Replay exact generated props two-step inverse with its current archive only.
layout=r['generatedOwningLayoutSuccessor'];props_body=decode(layout['actualPropsCustody']);verify(props_body,layout['actualProps']);xml=ET.fromstring(props_body)
normal_group=next(group for group in xml if group.get('Condition')=="'$(RootNormalOutputModule)' != '' And '$(RootNormalOutputModule)' == '$(MSBuildProjectName)'");xml.remove(normal_group);ET.indent(xml);verify(ET.tostring(xml,encoding='unicode').encode(),layout['exactSource50Props'])
console="'$(MSBuildProjectName)' == 'Haven.Console.Tests'";group=next(group for group in xml if console in group.get('Condition','').split(' Or '));conditions=group.attrib['Condition'].split(' Or ');assert conditions.count(console)==1;conditions.remove(console);group.attrib['Condition']=' Or '.join(conditions);ET.indent(xml);verify(ET.tostring(xml,encoding='unicode').encode(),layout['originalC43Props'])
observation_path=receipt.parent/'FINAL-ACTUAL-CAPACITY-OBSERVATION62.json';observation=json.loads(observation_path.read_bytes());assert observation['receipt']==pin(receipt) and observation['diskSpareActuallyObserved'] and observation['floorActuallyObserved']
active=[]
for entry in Path('/proc').iterdir():
 if not entry.name.isdecimal() or int(entry.name)==os.getpid():continue
 try:argv=[part.decode('utf-8','replace') for part in (entry/'cmdline').read_bytes().split(b'\0') if part]
 except OSError:continue
 if argv and Path(argv[0]).name in ('dotnet','csc','csc.dll','llama-server','llama-cli'):active.append({'pid':int(entry.name),'argv':argv})
assert not active,('Bounded runtime receiving requires SDK/model/Console idle',active)
value={'status':'ACTUAL_CURRENT66_FAILED_INTERVAL_DESKTOP_NORMAL_COMPONENT_CUSTODY67_PASS_NO_FULL_INTERVAL_ACCEPTANCE','head':HEAD,'ticket':r['ticket'],'originalNaturalExitCode':1,'original66NaturalExitCode':1,'original66FullIntervalNeverPromoted':True,'acceptedDesktopNormalComponent':True,'fullCompilerIntervalAccepted':False,'fullNormalCollectionAccepted':False,'runtimeReady':False,'testsAccepted':False,'originalReceipt':pin(receipt),'currentCompilerReceipt':pin(receipt),'activeSDKNone':True,'activeSDK':None,'modelOrConsoleNone':True,'actualProcessScan':active,'inputsUnchanged':True,'currentCompiledOutputPins':r['outputs'],'actualFreshCscOwners':[],'genuinePriorCompilerReusedCscOwners':r['genuinePriorCompilerReusedCscOwners'],'componentBasisOriginalNaturalExitCode':1,'componentBasisIsQualifiedFailed65IndividualComponents':True,'preservedFailed65NeverClaimedFullSuccessfulInterval':True,'actualFailed65ComponentCompilerReceipt':original_pin,'actualFailed65IndividualComponentCustody':custody_pin,'actualCurrent59SuccessfulCompilerReceipt':r['actualCurrent59SuccessfulCompilerReceipt'],'actualCurrent59FullClosedReadback':r['actualCurrent59FullClosedReadback'],'actualFailedC44ComponentCustody':r['actualFailedC44ComponentCustody'],'preservedFailed55NeverClaimedFullSuccessfulInterval':True,'actualNormalRuntimeClosures':normal_readbacks,'qualifiedHistoricalC43NormalTestRuntimeClosures':[],'actualDurableNormalLayoutReadbacks':layout_readbacks,'actualDurableNormalAllocationReadback':{'normalRoot':str(NORMAL_ROOT),'workspaceFilesystem':workspace,'truthfulDurableNormalPathsDevicesAndRestorationVerified':True,'scope':'ONLY_DESKTOP_NORMAL_COMPONENT_AND_CURRENT22_COMPONENT_IDENTITY','originalAfterReceiptObservation':pin(observation_path)},'generatedOwningLayoutSuccessorReadback':{'actualProps':layout['actualProps'],'wholeInverseOriginalC43PropsVerified':True,'wholeInverseSource50PropsVerified':True},'retiredEphemeralRuntimeFilesNotClaimedPhysicallyPresent':False,'noNormalCopiesRetiredAtQualification':True,'actualDesktopRuntimeClosureManifest':normal_manifest,'actualDesktopCompilerSuccessProvenance':normal_readbacks[0]['actualCompilerSuccessProvenance'],'actualDesktopRuntimeStage':v['actualRuntimeStage'],'physicalDesktopDirectoryObservations':directory_observations,'wholeCurrent22ComponentReadbacks':readbacks,'qualifiedDesktopNormalFileReadbacks':file_readbacks,'qualifiedDesktopNormalRawBytes':944837269,'qualifiedDesktopNormalFileCount':264,'exactSourcePeer':pin(peer_path),'exactWholeRootLog':pin(log),'componentQualificationSource':pin(__file__),'componentQualificationSourceProof':pin(P/'CURRENT66-DESKTOP-COMPONENT67-EXACT-FINITE-SOURCE-PROOF01.json'),'testsModelsConsoleExecuted':False,'fullHistoricalCompilerDeepAcceptanceClaimed':False,'qualification':'Desktop normal component only from genuine actual66 SDK normal-stage0 and all22 freshly evaluated current source/ref/resource/property/analyzer/upstream/output equivalence. Original65 and66 aggregate1 remain failures; no complete normal collection, tests/runtime/GUI/model/authority or deep acceptance is claimed. All264 genuine Desktop files totaling944837269 bytes are physically compared and fully restored from exact archives/NUPKGs before separate safe task-owned retirement. No SDK/model/test is executed by this readback.'}
out=P/'CURRENT66-FAILED-INTERVAL-DESKTOP-NORMAL-COMPONENT-CUSTODY67.json';assert not out.exists();body=(json.dumps(value,indent=2)+'\n').encode();assert os.statvfs(P).f_bavail*os.statvfs(P).f_frsize-len(body)>=1048576
with out.open('xb') as stream:stream.write(body);stream.flush();os.fsync(stream.fileno())
out.chmod(0o444);print(json.dumps(pin(out)))
