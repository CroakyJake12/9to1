from pathlib import Path
import gzip,hashlib,importlib.util,json,os,re,subprocess,sys,time,zipfile
sys.dont_write_bytecode=True
PREP=Path('/workspace/astra-source/lifecycle-peer-sol61u51/failed65-components66');ROOT=Path('/workspace/astra-consolidated');HEAD=None
assert len(sys.argv)==6, 'Require actual receipt, exact SHA, natural exit0, immutable binding JSON and exact binding SHA.'
receipt=Path(sys.argv[1]);expected_sha=sys.argv[2];natural_exit=int(sys.argv[3]);binding_path=Path(sys.argv[4]);binding_sha=sys.argv[5]
assert natural_exit==1, 'This exact failure-only readback never accepts a successful or incomplete replacement interval.'
def pin(p):
 p=Path(p);h=hashlib.sha256();n=0
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b);n+=len(b)
 return {'path':str(p),'bytes':n,'sha256':h.hexdigest()}
def verify(raw,p):assert len(raw)==p['bytes'] and hashlib.sha256(raw).hexdigest()==p['sha256'],p
EXACT_FAILED_RECEIPT={'path':'/workspace/astra-source/a4-c44-targeted-owning-compiler55/output/C44-TARGETED-OWNING-COMPILER55-RECEIPT.json.gz','bytes':3748669,'sha256':'5fd3ca18e745fa678b7abcf9bf2363117ceb56a00fe1284cd611d49fcff6a451'}
assert pin(binding_path)['sha256']==binding_sha
binding=json.loads(binding_path.read_bytes());assert binding['status']=='ROOT_BOUND_ACTUAL_FAILED65_COMPONENT_INPUTS'
HEAD=binding['head'];assert re.fullmatch(r'[0-9a-f]{40}',HEAD)
assert pin(binding['compilerSourcePeer']['path'])==binding['compilerSourcePeer']
peer=json.loads(Path(binding['compilerSourcePeer']['path']).read_bytes());assert peer['status']=='QUALIFIED_INDEPENDENT_SOURCE_PASS_CURRENT_ROOT_SELECTIVE_COMPILER65_RUNTIME_UNEXECUTED'
assert peer['recipe']==binding['compilerSource'] and peer['executionAuditHelper']==binding['fixtureAuditHelper'] and peer['selfcheck']==binding['currentSourceSelfcheck']
assert pin(binding['currentSourceSelfcheck']['path'])==binding['currentSourceSelfcheck']
sourcecheck=json.loads(Path(binding['currentSourceSelfcheck']['path']).read_bytes());assert sourcecheck['status']=='SOURCE_AND_BOUNDED_CURRENT_ROOT_COMPILER65_SELF_CHECK_PASS_NO_SDK_RUNTIME'
assert sourcecheck['head']==HEAD
assert peer['exactC49TwoTestAssertionsWitness']==sourcecheck['exactC49TwoTestAssertionsWitness'] and sourcecheck['exactC49TwoTestAssertionsWitness']['wholeForwardInverseEqual'] and sourcecheck['exactC49TwoTestAssertionsWitness']['genericMissingCarrierRefusalPreservedForEveryOtherRow']
assert pin(sourcecheck['exactC49TwoTestAssertionsWitness']['ticket']['path'])==sourcecheck['exactC49TwoTestAssertionsWitness']['ticket'] and pin(sourcecheck['exactC49TwoTestAssertionsWitness']['afterSource']['path'])==sourcecheck['exactC49TwoTestAssertionsWitness']['afterSource']
assert peer['preservedActualFailed64Receipt']==sourcecheck['preservedActualFailed64Receipt'] and pin(sourcecheck['preservedActualFailed64Receipt']['path'])==sourcecheck['preservedActualFailed64Receipt'] and sourcecheck['failed64WholeIntervalOrComponentsNotPromoted']

assert binding['receipt']==pin(receipt) and binding['receipt']['sha256']==expected_sha and binding['naturalExitCode']==natural_exit==1
assert binding['verifier']==pin(__file__)
source_proof=Path(binding['sourceProof']['path']);assert pin(source_proof)==binding['sourceProof']
source_proof_value=json.loads(source_proof.read_bytes());assert source_proof_value['successor']==pin(__file__) and source_proof_value['sourceOnly'] and source_proof_value['runtimeVerificationExecuted'] is False
assert pin(receipt)['sha256']==expected_sha
r=json.loads(gzip.decompress(receipt.read_bytes()));assert r['headAtCapture']==r['headAtCompletion']==HEAD and r['exitCode']==natural_exit
assert r['inputsUnchangedAtCompletion'] and not r['changedInputs'] and not r['unavailableInputsAtCompletion']
assert binding['externalCompilerLog'] in binding['actualTerminalPins'] and binding['receipt'] in binding['actualTerminalPins']
for descriptor in binding['actualTerminalPins']:assert pin(descriptor['path'])==descriptor
terminal_body=Path(binding['externalCompilerLog']['path']).read_bytes();assert b'CURRENT_TARGETED65_ORIGINAL_TERMINAL 1 INPUTS_UNCHANGED True RECEIPT ' in terminal_body
assert r['compilerOnly'] and not r['testsExecuted'] and not r['testDiscoveryExecuted'] and not r['modelExecuted'] and not r['consoleExecuted']
ticket=Path(r['ticket']['path']);assert pin(ticket)==r['ticket'];t=json.loads(ticket.read_bytes());assert t['afterHead']==HEAD
assert subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()==HEAD
assert not subprocess.check_output(['git','-C',str(ROOT),'diff','--name-only']) and not subprocess.check_output(['git','-C',str(ROOT),'diff','--cached','--name-only'])
requests=''.join(HEAD+':'+x['target']+'\n' for x in t['afterRows']).encode();data=subprocess.check_output(['git','-C',str(ROOT),'cat-file','--batch'],input=requests);pos=0
for x in t['afterRows']:
 end=data.index(b'\n',pos);parts=data[pos:end].split();assert parts[1]==b'blob';n=int(parts[2]);body=data[end+1:end+1+n];pos=end+2+n;assert data[pos-1:pos]==b'\n'
 verify(body,x['afterPin']);assert (ROOT/x['target']).read_bytes()==body
 if 'gitBlob' in x['afterPin']:assert parts[0].decode()==x['afterPin']['gitBlob']
assert pos==len(data)
assert len(t['afterRows'])>=459 and len({x['target'] for x in t['afterRows']})==len(t['afterRows'])
assert r['ticket']==sourcecheck['ticket']
ROOT_PARENT_TICKET={'path':'/workspace/astra-root-checkpoint45-console-rootnamespace01/CHECKPOINT45-CONSOLE-ROOTNAMESPACE-OWNING-SOURCE-TICKET.json','bytes':389472,'sha256':'a8a2dadc2ca31e5040b91a3a9ba0dbe452bf14294cdd4dfd26503b21624faa11'}
reviewed_chain=[];walk=t;walk_pin=r['ticket'];seen=set()
while walk_pin!=ROOT_PARENT_TICKET:
 assert walk_pin['path'] not in seen;seen.add(walk_pin['path']);assert pin(walk_pin['path'])==walk_pin
 parent_pin=walk['parentTicket'];assert pin(parent_pin['path'])==parent_pin;previous=json.loads(Path(parent_pin['path']).read_bytes())
 assert previous['afterHead']==walk['parentHead']==subprocess.check_output(['git','-C',str(ROOT),'rev-parse',walk['afterHead']+'^'],text=True).strip()
 assert walk['retiredRows']==previous['retiredRows']
 old_rows={x['target']:x['afterPin'] for x in previous['afterRows']};new_rows={x['target']:x['afterPin'] for x in walk['afterRows']};changes={x['target']:x for x in walk['newRows']}
 assert len(changes)==len(walk['newRows']) and set(new_rows)==set(old_rows)|{x['target'] for x in walk['newlyEnrolledRows']}
 for target,descriptor in new_rows.items():
  if target not in changes:assert all(descriptor[k]==old_rows[target][k] for k in ('bytes','sha256'))
 for target,row in changes.items():
  before=row['beforePin'];after=row['afterPin'];assert all(after[k]==new_rows[target][k] for k in ('bytes','sha256'))
  if row['beforeExists']:verify(subprocess.check_output(['git','-C',str(ROOT),'show',walk['parentHead']+':'+target]),before)
  else:assert (before is None or all(before[k]=={'bytes':0,'sha256':hashlib.sha256(b'').hexdigest()}[k] for k in ('bytes','sha256'))) and subprocess.run(['git','-C',str(ROOT),'cat-file','-e',walk['parentHead']+':'+target],stdout=subprocess.PIPE,stderr=subprocess.PIPE).returncode!=0
  verify(subprocess.check_output(['git','-C',str(ROOT),'show',walk['afterHead']+':'+target]),after)
  for descriptor in ([before] if row['beforeExists'] else [])+[after]:
   if descriptor is not None and 'path' in descriptor:assert pin(descriptor['path'])==descriptor
 reviewed_chain.append({'ticket':walk_pin,'head':walk['afterHead'],'newRows':walk['newRows']});walk=previous;walk_pin=parent_pin
assert pin(ROOT_PARENT_TICKET['path'])==ROOT_PARENT_TICKET and walk['afterHead']=='1f2f1778fda757a30b1e29c449c61978614f5875'
for row in t['retiredRows']:
 assert row['afterExists'] is False and not (ROOT/row['target']).exists() and not (ROOT/row['target']).is_symlink()
 absent=subprocess.run(['git','-C',str(ROOT),'cat-file','-e',HEAD+':'+row['target']],stdout=subprocess.PIPE,stderr=subprocess.PIPE);assert absent.returncode!=0
 body=subprocess.check_output(['git','-C',str(ROOT),'show',row['verifiedRetirementHead']+':'+row['owningTarget']]);verify(body,row['conservedOwningAfterPin'])
 verify((ROOT/row['owningTarget']).read_bytes(),row['currentOwningAfterPin'])
assert r['explicitReviewedSourceRetirements']==t['retiredRows']
if r['ticket']=={'path':'/workspace/astra-root-checkpoint49-files-assertions64/OWNING-SOURCE-TICKET.json','bytes':394799,'sha256':'59e6ba1c1ae556289463be7603b529db07e4b5b06ff0cc112f9a8c42de275dc0'}:
 # Exact genuine C49 schema: no synthesized carrier or optional generic gate.
 witness=sourcecheck['exactC49TwoTestAssertionsWitness'];assert witness['ticket']==r['ticket'] and witness['wholeForwardInverseEqual'] and witness['unchangedProductionRowsAndNoNewEnrollments']
 assert 'carriers' not in t and 'readbackDependencies' not in t and t['sourcePeers']==[] and t['newlyEnrolledRows']==[] and len(t['newRows'])==1
 source_packet={'path':'/workspace/astra-source/friday-files-downstream-sol61u62/files64-assert-single04/FILES64-EXACT-TWO-ASSERT-CALL-CORRECTION04-SOURCE-HANDOFF01.json','bytes':4110,'sha256':'66983e81de6ff3637dd775e05f1a2d26043066f4ec7e655f8358df3f46a9f717'}
 assert t['sourceManifests']==[source_packet] and pin(source_packet['path'])==source_packet and pin(t['preIntegrationReceipt']['path'])==t['preIntegrationReceipt']
 target=witness['target'];row=t['newRows'][0];assert row['target']==target and row['beforeExists'] and all(row['beforePin'][key]==witness['before'][key] and row['afterPin'][key]==witness['after'][key] for key in ('bytes','sha256'))
 before_body=subprocess.check_output(['git','-C',str(ROOT),'show',t['parentHead']+':'+target]);after_body=(ROOT/target).read_bytes();forward=before_body
 for edit in witness['exactWholeForwardInverseEdits']:
  previous=edit['before'].encode();replacement=edit['after'].encode();assert forward.count(previous)==edit['count'];forward=forward.replace(previous,replacement,edit['count'])
 assert forward==after_body;inverse=after_body
 for edit in reversed(witness['exactWholeForwardInverseEdits']):
  previous=edit['before'].encode();replacement=edit['after'].encode();assert inverse.count(replacement)==edit['count'];inverse=inverse.replace(replacement,previous,edit['count'])
 assert inverse==before_body and t['afterHead']==witness['afterHead'] and t['parentHead']==witness['parentHead']
else:
 for key in ('carriers','sourcePeers','sourceManifests','readbackDependencies'):
  for descriptor in t[key]:assert pin(descriptor['path'])==descriptor
assert r['exactSelectiveCompilerSource']==binding['compilerSource'] and pin(binding['compilerSource']['path'])==binding['compilerSource']
assert r['exactFixtureExecAuditHelper']==binding['fixtureAuditHelper'] and pin(binding['fixtureAuditHelper']['path'])==binding['fixtureAuditHelper']
assert binding['compilerSource'] in r['inputs'] and binding['fixtureAuditHelper'] in r['inputs']
assert binding['compilerSource']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/c49-compiler65/run-current-root-selective-component-compiler65.py', 'bytes': 186899, 'sha256': 'd9111dff14afabaaaab43c87fd2e4ab8f86733600b3b1e5097814b738d434536'}
assert binding['fixtureAuditHelper']=={'path':'/workspace/astra-source/a4-c45-fixture-audit58-source-helper/fixture-exec-audit58c.py','bytes':27135,'sha256':'8c1dc104eb0a56f844458d6b78f5a85af5ec557093503c3975d7e02641dea039'}
assert binding['compilerSourceProof']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/c49-compiler65/CURRENT-ROOT-COMPILER65-EXACT-FINITE-SOURCE-PORT01.json', 'bytes': 96119, 'sha256': '325e9606f089d18ca70af1968be4963500af43124b5932290d84e64baf119c77'}
assert binding['fixtureAuditSourceProof']=={'path':'/workspace/astra-source/a4-c45-fixture-audit58-source-helper/C45-FIXTURE-AUDIT58C-EXACT-SOURCE-PORT01.json','bytes':20784,'sha256':'f8556ba0435865e18e57d66e53ba6c89b17738db76372d38ac5a5def5d60e9e7'}
for key in ('compilerSourceProof','compilerSourcePeer','fixtureAuditSourceProof'):
 descriptor=binding[key];assert pin(descriptor['path'])==descriptor
assert source_proof_value['sourceOnly'] and source_proof_value['runtimeVerificationExecuted'] is False
assert pin(r['actualC43ParentCompilerReceipt']['path'])==r['actualC43ParentCompilerReceipt']
assert pin(r['actualC43FullClosedReadback']['path'])==r['actualC43FullClosedReadback']
parent=json.loads(gzip.decompress(Path(r['actualC43ParentCompilerReceipt']['path']).read_bytes()))
parent_closed=json.loads(Path(r['actualC43FullClosedReadback']['path']).read_bytes())
assert parent['headAtCapture']==parent['headAtCompletion']==parent_closed['head']=='f7aa74440b43858ed0d5a488576c2d5de566ae46'
assert parent_closed['originalReceipt']==r['actualC43ParentCompilerReceipt'] and parent_closed['inputsUnchanged'] and parent_closed['activeSDKNone']
assert r['actualC43ParentCompilerReceipt']=={'path':'/workspace/astra-source/a4-c43-targeted-owning-compiler46/output/C43-TARGETED-OWNING-COMPILER46-RECEIPT.json.gz','bytes':3286504,'sha256':'826d3fe10ddc5b32933c9240c819a5966db5d20aa585f402e9de6d2adee00a6f'}
assert r['actualC43FullClosedReadback']=={'path':'/workspace/astra-source/a4-c43-targeted-owning-compiler-preparation46/C43-TARGETED46-FULL-CLOSED-CUSTODY-READBACK01.json','bytes':3244130,'sha256':'988d3f50e99eabd9710ddc1d7161e29a5f2005ddb1f3898a959026d76a79dff9'}
assert parent['exitCode']==0 and parent['status']=='ACTUAL_TARGETED_OWNING_COMPILER_PASS' and parent_closed['originalNaturalExitCode']==0
assert r['actualFailedC44ComponentBasis']==EXACT_FAILED_RECEIPT and pin(EXACT_FAILED_RECEIPT['path'])==EXACT_FAILED_RECEIPT
EXACT_FAILED_CUSTODY={'path':'/workspace/astra-source/a4-c44-actual55-failure-repair57/C44-ACTUAL55-FAILED-INTERVAL-FULL-CUSTODY-READBACK57.json','bytes':2640235,'sha256':'97a3f25fa0ab0cacd7dd23ee443494d956767d6b850a4e48d6f68965766cfce3'}
assert r['actualFailedC44ComponentCustody']==EXACT_FAILED_CUSTODY and pin(EXACT_FAILED_CUSTODY['path'])==EXACT_FAILED_CUSTODY
basis=json.loads(gzip.decompress(Path(EXACT_FAILED_RECEIPT['path']).read_bytes()));basis_closed=json.loads(Path(EXACT_FAILED_CUSTODY['path']).read_bytes())
assert basis['headAtCapture']==basis['headAtCompletion']==basis_closed['head']=='2c09519c64906a1fd307dc9bcce558885fd05ec6'
assert basis['exitCode']==basis_closed['originalNaturalExitCode']==1 and r['componentBasisOriginalNaturalExitCode']==0
assert basis['status']=='ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED' and basis_closed['status']=='ACTUAL_C44_TARGETED55_FAILURE_FULL_CUSTODY_READBACK_PASS_NO_COMPILER_RUNTIME_ACCEPTANCE'
assert basis_closed['originalReceipt']==EXACT_FAILED_RECEIPT and basis_closed['inputsUnchanged'] and basis_closed['activeSDKNone'] and basis['inputsUnchangedAtCompletion']
assert basis_closed['failureQualification']['fullCompilerIntervalAccepted'] is False and basis_closed['failureQualification']['runtimeReady'] is False and not basis_closed['actualNormalRuntimeClosures']
assert basis_closed['currentCompiledOutputPins']==basis['outputs'] and r['preservedFailed55NeverClaimedFullSuccessfulInterval'] is True and r['componentBasisIsGenuineFullSuccessfulCurrent59Interval'] is True
assert basis['actualC43ParentCompilerReceipt']==r['actualC43ParentCompilerReceipt'] and basis['actualC43FullClosedReadback']==r['actualC43FullClosedReadback']
assert r['actualFailedC44ComponentSourcePeer']=={'path':'/workspace/astra-source/lifecycle-peer-sol61u51/C44-FAILED-INTERVAL-CUSTODY57C-INDEPENDENT-QUALIFIED-SOURCE-PEER06.json','bytes':9942,'sha256':'baa420fc725570d11c9633dafd5dc79b75cc8e427acebb04c5af862d1d8fe6d0'}
assert pin(r['actualFailedC44ComponentSourcePeer']['path'])==r['actualFailedC44ComponentSourcePeer']
assert pin('/workspace/astra-source/a4-c44-actual55-failure-repair57/verify-failed-interval-custody57c.py')=={'path':'/workspace/astra-source/a4-c44-actual55-failure-repair57/verify-failed-interval-custody57c.py','bytes':52930,'sha256':'b1bbc12efd5481e835f8f45f70bb7ea0f71ccf366c881df0658758b68eddb1d4'}
CURRENT59_RECEIPT={'path':'/workspace/astra-source/a4-c45-targeted-owning-compiler59/output/C45-TARGETED-OWNING-COMPILER59-RECEIPT.json.gz','bytes':3152001,'sha256':'59d56e8b0b5a922b07d8380c6097c741bb55f401d09b73ff3647bef087f46b70'}
CURRENT59_CLOSED={'path':'/workspace/astra-source/c45-closed-verifier-sol61u59/C45-SELECTIVE59-FULL-CLOSED-CUSTODY-READBACK01.json','bytes':11618324,'sha256':'7f218c5318a17c79aa0b013c5b8adc3129ab207abf9a5d0dbe96b3c2a9d2d802'}
assert r['actualCurrent59SuccessfulCompilerReceipt']==CURRENT59_RECEIPT and r['actualCurrent59FullClosedReadback']==CURRENT59_CLOSED
assert pin(CURRENT59_RECEIPT['path'])==CURRENT59_RECEIPT and pin(CURRENT59_CLOSED['path'])==CURRENT59_CLOSED
current59=json.loads(gzip.decompress(Path(CURRENT59_RECEIPT['path']).read_bytes()));current59_closed=json.loads(Path(CURRENT59_CLOSED['path']).read_bytes())
assert current59['status']=='ACTUAL_TARGETED_OWNING_COMPILER_PASS' and current59['exitCode']==current59_closed['originalNaturalExitCode']==0
assert current59['headAtCapture']==current59['headAtCompletion']==current59_closed['head']=='1f2f1778fda757a30b1e29c449c61978614f5875'
assert current59_closed['status']=='ACTUAL_C45_TARGETED59_FULL_CLOSED_CUSTODY_READBACK_PASS' and current59_closed['originalReceipt']==current59_closed['currentCompilerReceipt']==CURRENT59_RECEIPT
assert current59_closed['inputsUnchanged'] and current59_closed['activeSDKNone'] and current59['inputsUnchangedAtCompletion']
assert current59['actualFailedC44ComponentBasis']==EXACT_FAILED_RECEIPT and current59['actualC43ParentCompilerReceipt']==r['actualC43ParentCompilerReceipt']
assert pin(r['actualCurrent59QualifiedSourcePeer']['path'])==r['actualCurrent59QualifiedSourcePeer']
current59_peer=json.loads(Path(r['actualCurrent59QualifiedSourcePeer']['path']).read_bytes());assert current59_peer['status']=='QUALIFIED_INDEPENDENT_SOURCE_PASS_C45_SELECTIVE_COMPONENT59_RUNTIME_UNEXECUTED' and current59_peer['recipe']==current59['exactSelectiveCompilerSource']
parent_outputs={o['module']:o for o in parent['outputs']};basis_outputs={o['module']:o for o in basis['outputs']};current59_outputs={o['module']:o for o in current59['outputs']};current_outputs={o['module']:o for o in r['outputs']}
current_decisions={d['module']:d for d in r['actualReuseDecisions']}
assert len(parent_outputs)==len(basis_outputs)==21 and set(parent_outputs)==set(basis_outputs)
assert len(current_outputs)==len(r['outputs'])==len(current59_outputs)==22 and set(current_outputs)==set(current59_outputs)==set(basis_outputs)|{'Haven.Console.Tests'}
assert len(current_decisions)==len(r['actualReuseDecisions'])==22 and set(current_decisions)==set(current_outputs)
assert r['status']=='ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED' and r['actualPrimaryException']=={'type':'AssertionError','message':''}
assert pin(receipt)=={'path':'/workspace/astra-source/a4-current-targeted-owning-compiler65/output/CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz','bytes':4844261,'sha256':'c9bba5c0ad33af23580ebf2eed255afa6ca731c6541b99d6a4797346fb77c64f'}
assert len(r['actualFreshCscOwners'])==20 and set(r['genuinePriorCompilerReusedCscOwners'])=={'Haven.Core','Haven.PluginFixture'}
assert r['actualDesktopRuntimeClosureManifest'] is None and r['actualPluginFixtureRuntimeClosureManifest'] is None and r['actualOwningTestRuntimeClosureManifests']==[] and r['genuineUnchangedC43TestRuntimeClosureManifests']==[]
MANDATORY_FRESH={'Haven.Application','HavenOS.Dev','Haven.Desktop','Haven.Core.Tests','HavenOS.Dev.Tests','Haven.Desktop.Tests'}
assert set(r['mandatoryFreshOwners'])==MANDATORY_FRESH and set(r['conditionalFreshOwners'])==set(current_outputs)-MANDATORY_FRESH
EXPECTED_FRESH={module for module,d in current_decisions.items() if d['freshCscRequired']};EXPECTED_REUSED=set(current_outputs)-EXPECTED_FRESH
assert MANDATORY_FRESH<=EXPECTED_FRESH and set(r['actualFreshCscOwners'])==EXPECTED_FRESH and len(r['actualFreshCscOwners'])==len(EXPECTED_FRESH)
assert set(r['genuinePriorCompilerReusedCscOwners'])==EXPECTED_REUSED and len(r['genuinePriorCompilerReusedCscOwners'])==len(EXPECTED_REUSED)
assert r['reusedCompilerOutputNotRelabeledCurrent'] is True and {module for module,o in current_outputs.items() if o['compiledInCurrentInterval']}==EXPECTED_FRESH
assert not r['independentTestCompilerFailures'] and not r['desktopCompilerFailures'] and not r['desktopRuntimeOutputFailures'] and not r['owningTestRuntimeOutputFailures']
helper=Path(r['exactResourceCustodyHelper']['path']);registry_path=Path(r['closedLayerArchiveRegistry']['path']);assert pin(helper)==r['exactResourceCustodyHelper'] and pin(registry_path)==r['closedLayerArchiveRegistry']
spec=importlib.util.spec_from_file_location('closed_targeted23_custody',helper);lib=importlib.util.module_from_spec(spec);spec.loader.exec_module(lib);registry=json.loads(registry_path.read_bytes())
def decode(row):
 if row.get('encoding')==lib.SCHEMA:body=lib.decode_archive(row,registry)
 else:
  encoded,_=lib.read_original_gzip(row['archive']['path'],row['archive'],registry);body=gzip.decompress(encoded)
 verify(body,row['original']);return body
archive_readbacks=[];original_paths={};decoded_bytes=0;decoded_archives=set()
for row in [*parent['outputArchives'],*basis['outputArchives'],*current59['outputArchives'],*r['outputArchives']]:
 key=(row['archive']['path'],row['original']['sha256'])
 if key not in decoded_archives:
  body=decode(row)
  if row.get('encoding')==lib.SCHEMA:
   manifest=lib.read_descriptor(row['archive']['path']);encoded=lib.encoded_layer(manifest,registry);verify(encoded,manifest['originalEncodedGzip']);del encoded
  decoded_bytes+=len(body);archive_readbacks.append({'archive':row['archive'],'original':row['original'],'encoding':row.get('encoding','EXACT_GZIP'),'wholeDecodedVerified':True});decoded_archives.add(key)
 for p in row['originalPaths']:original_paths[(p,row['original']['bytes'],row['original']['sha256'])]=row['archive']
for stage in r['stages']:
 body=decode(stage['exactWholeStdoutCustody']);verify(body,stage['actualChildOutputPin'])
 if stage.get('durableRawLog'):assert pin(stage['durableRawLog']['path'])==stage['durableRawLog']
for output in r['outputs']:
 for k in ('output','target','pdb','reference'):
  if output.get(k):
   p=output[k];assert (p['path'],p['bytes'],p['sha256']) in original_paths,('Current genuine compiled output lacks closed whole custody',p)
 if not output['compiledInCurrentInterval']:
  baseline=current59_outputs[output['module']]
  assert output['compiledSourceHead']==baseline['compiledSourceHead'] and output['completeSourceItemResourceOrderEqual'] and output['actualCompilerReferenceAndAnalyzerBytesEqual']
  assert output['reusedFromActualCompilerReceipt']==CURRENT59_RECEIPT and output['reusedFromCurrentParentFullClosedReadback']==CURRENT59_CLOSED and output['reusedReceiptOriginalNaturalExitCode']==0
  assert output['originalComponentCompiledInSuccessful59Interval']==baseline['compiledInCurrentInterval'] and output['componentReuseQualification']=='GENUINE_FULL_SUCCESSFUL_CURRENT59_COMPONENT_AFTER_EXACT_CURRENT_SOURCE_RESOURCE_REF_ANALYZER_EQUALITY'
  assert not current_decisions[output['module']]['freshCscRequired']
  for key in ('output','target','pdb','reference'):assert output.get(key)==baseline.get(key)
  assert r['transparentReusedCompiledSourceHeads'][output['module']]==baseline['compiledSourceHead']
 else:assert output['compiledSourceHead']==HEAD
# Exact current source/resource/property/ref/analyzer/order and upstream readback.
stage_map={stage['stage']:stage for stage in r['stages']};assert len(stage_map)==len(r['stages']) and all(stage['exitCode']==0 for stage in r['stages'])
stage_map_basis={stage['stage']:stage for stage in basis['stages']}
component_plans={plan['module']:plan for plan in r['modulePlans']};basis_plans={plan['module']:plan for plan in basis['modulePlans']};current59_plans={plan['module']:plan for plan in current59['modulePlans']}
assert len(component_plans)==len(r['modulePlans'])==22 and set(component_plans)==set(current_outputs)
source_item_readbacks=[];component_stage_readbacks=[]
for module,plan in component_plans.items():
 assert not plan['missingItems'] and plan['targetFramework']==plan['properties']['TargetFramework']
 assert pin(plan['evaluation']['path'])==plan['evaluation'] and pin(plan['references']['path'])==plan['references']
 evaluated=json.loads(gzip.decompress(Path(plan['evaluation']['path']).read_bytes()));refs=json.loads(gzip.decompress(Path(plan['references']['path']).read_bytes()))
 assert evaluated['Properties']==plan['properties'] and plan['sourceCount']==len(evaluated['Items']['Compile'])
 assert evaluated==json.loads(decode(stage_map[module+'-evaluation01']['exactWholeStdoutCustody'])) and refs==json.loads(decode(stage_map[module+'-references01']['exactWholeStdoutCustody']))
 assert current_outputs[module]['evaluation']==plan['evaluation'] and current_outputs[module]['references']==plan['references']
 assert pin(plan['canonicalProject']['path'])==plan['canonicalProject']
 assert (plan['project']['bytes'],plan['project']['sha256'])==(plan['canonicalProject']['bytes'],plan['canonicalProject']['sha256'])
 observed_items=[]
 for kind,items in evaluated['Items'].items():
  for item in items:
   logical=item.get('FullPath',item['Identity'])
   matching=[x for x in plan['physicalItems'] if x['kind']==kind and x['logical']['path']==logical]
   if kind=='PackageReference' and not matching:continue
   assert len(matching)==1,(module,kind,logical)
   x=matching[0];resolved=Path(x['resolved']['path']);shadow_scope=Path('/dev/shm/astra-framework-owning-metadata-20261006-03/source-root')
   retained=ROOT/resolved.relative_to(shadow_scope) if resolved.is_relative_to(shadow_scope) else resolved
   verify(retained.read_bytes(),x['resolved']);assert (x['logical']['bytes'],x['logical']['sha256'])==(x['resolved']['bytes'],x['resolved']['sha256'])
   observed_items.append(x)
 assert observed_items==plan['physicalItems']
 observed_refs=[]
 for kind,items in refs['Items'].items():
  for item in items:
   logical=item.get('FullPath',item['Identity']);matching=[x for x in plan['actualReferencePins'] if x['kind']==kind and x['input']['path']==logical and x['metadata']==item]
   assert len(matching)==1,(module,kind,logical);observed_refs.append(matching[0])
 assert observed_refs==plan['actualReferencePins']
 d=current_decisions[module];assert d==plan['targetedCompilerDecision'] and d['unreviewedAbiWaiver'] is False
 old=current59_plans[module]
 assert plan['canonicalProject']==old['canonicalProject'] and plan['project']==old['project'] and plan['properties']==old['properties']
 old_items=[(x['kind'],x['logical']['path'],x['logical']['bytes'],x['logical']['sha256']) for x in old['physicalItems']];new_items=[(x['kind'],x['logical']['path'],x['logical']['bytes'],x['logical']['sha256']) for x in plan['physicalItems']]
 old_refs=[(x['kind'],x['input']) for x in old['actualReferencePins']];new_refs=[(x['kind'],x['input']) for x in plan['actualReferencePins']]
 old_csc=[x for x in old_refs if x[0] in ('ReferencePathWithRefAssemblies','Analyzer')];new_csc=[x for x in new_refs if x[0] in ('ReferencePathWithRefAssemblies','Analyzer')]
 assert d['mandatoryFresh']==(module in MANDATORY_FRESH) and d['conditionalFresh']==(module not in MANDATORY_FRESH)
 assert d['completeSourceItemResourceOrderEqual']==(old_items==new_items) and d['actualCompilerRefsAndAnalyzersWholeEqual']==(old_csc==new_csc) and d['allResolvedRefsWholeEqual']==(old_refs==new_refs)
 assert d['freshCscRequired']==(module in MANDATORY_FRESH or old_items!=new_items or old_csc!=new_csc)
 if not d['freshCscRequired']:
  assert old_items==new_items and old_csc==new_csc and module+'-compiler01' not in stage_map and plan['reusedActualSuccessfulCompiler']==CURRENT59_RECEIPT and plan['currentParentFullClosedReadback']==CURRENT59_CLOSED
  assert len(old_refs)==len(new_refs)
  for old_ref,new_ref in zip(old_refs,new_refs):
   if old_ref==new_ref:continue
   assert old_ref[0]==new_ref[0]=='ReferencePath' and old_ref[1]['path']==new_ref[1]['path']
   owner=next(name for name,o in current59_outputs.items() if o.get('target')==old_ref[1]);assert new_ref[1]==current_outputs[owner]['target']
 else:
  assert plan['compilerExitCode']==0 and current_outputs[module]['cscCommandObserved'] is True and current_outputs[module]['compiledSourceHead']==HEAD
  stage=stage_map[module+'-compiler01'];log=decode(stage['exactWholeStdoutCustody']).decode('utf-8')
  assert re.search(r'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\x22]|$)',log) and not re.search(r': error (?:CS|MSB)\d+',log)
  component_stage_readbacks.append({'module':module,'actualSuccessfulCompilerStage':stage,'actualSourceEvaluation':plan['evaluation'],'actualReferenceEvaluation':plan['references'],'originalCompiledSourceHead':HEAD,'wholeGenuineCompiledOutputCustodyVerified':True})
 if module=='Haven.Console.Tests':assert plan['properties']['RootNamespace']=='Haven.Desktop.Tests' and plan['properties']['MSBuildProjectName']==module
 # Every upstream resolver occurrence must have exactly one separately genuine output.
 expected_upstreams=[]
 for owner,base in current59_outputs.items():
  if owner==module:continue
  names={Path(base[key]['path']).name for key in ('output','target','reference') if base.get(key)}
  found=[x['input'] for x in plan['actualReferencePins'] if Path(x['input']['path']).name in names]
  if found:
   current=current_outputs[owner];allowed={current[key]['sha256'] for key in ('output','target','reference') if current.get(key)}
   assert all(x['sha256'] in allowed for x in found)
   expected_upstreams.append({'owner':owner,'inputs':found,'successfulUpstream':current['output']})
 assert expected_upstreams==plan['actualUpstreamReferenceClosure']
 source_item_readbacks.append({'module':module,'evaluation':plan['evaluation'],'references':plan['references'],'sourceResourceOrderCount':len(plan['physicalItems']),'resolvedRefAnalyzerCount':len(plan['actualReferencePins']),'currentUpstreamClosure':expected_upstreams,'completeWholeComparisonsReplayed':True,'genuineReusedComponent':module in EXPECTED_REUSED})
assert len([x for x in r['stages'] if x['stage'].endswith('-compiler01')])==len(EXPECTED_FRESH)
physical_inputs=[];private_inputs=[]
for item in r['inputs']:
 if item['path'].startswith('/dev/shm/'):private_inputs.append(item)
 else:assert pin(item['path'])==item;physical_inputs.append(item)
containers={};member_proofs={}
# All ephemeral logical inputs receive an actual whole retained body readback.
all_archive_rows=[*parent['outputArchives'],*basis['outputArchives'],*current59['outputArchives'],*r['outputArchives']]
archive_index={}
for row in all_archive_rows:
 for path in row['originalPaths']:archive_index[(path,row['original']['bytes'],row['original']['sha256'])]=row
PRIVATE_SCOPE=Path('/dev/shm/astra-framework-owning-metadata-20261006-03')
SHADOW_SCOPE=PRIVATE_SCOPE/'source-root'
NUGET_CONFIG_BODY=b'<configuration><packageSources><clear /></packageSources><packageSourceMapping><clear /></packageSourceMapping><auditSources><clear /></auditSources></configuration>'
def closed_pin_body(descriptor,original_head=None):
 path=Path(descriptor['path']);key=(str(path),descriptor['bytes'],descriptor['sha256'])
 if key in archive_index:body=decode(archive_index[key])
 elif path.is_relative_to(SHADOW_SCOPE):
  relative=str(path.relative_to(SHADOW_SCOPE));source=ROOT/relative
  body=subprocess.check_output(['git','-C',str(ROOT),'show',original_head+':'+relative]) if original_head else source.read_bytes()
 elif original_head and path.is_relative_to(ROOT):body=subprocess.check_output(['git','-C',str(ROOT),'show',original_head+':'+str(path.relative_to(ROOT))])
 elif path==PRIVATE_SCOPE/'NuGet.Config':body=NUGET_CONFIG_BODY
 else:body=path.read_bytes()
 verify(body,descriptor);return body
# This exact historical metadata body is the previously Root-qualified donor.
# Its archive is literal packaging, never a new SDK product or current input promotion.
historical_props_raw={'path': '/workspace/astra-source/c45-closed-verifier-sol61u59/export60/C43-ORIGINAL-Checkpoint37CoherentOwningLayout.props', 'bytes': 2429, 'sha256': 'bcc3f9798b5cad579928eac2d428ea2afc31e903756588e047d28f712d36c4db'}
historical_props_archive={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/current-closed63/EXACT-HISTORICAL-C43-ORIGINAL-PROPS-DONOR01.props.gz', 'bytes': 562, 'sha256': '2e986ba3eaa39dd10ce29998ed063429a3a4fbac9cb66b5036912342f21f03df'}
historical_props_source_proof={'path': '/workspace/astra-source/c45-closed-verifier-sol61u59/export60/C45-ACTUAL59-CLOSED-ORIGINAL-C43-METADATA-DONOR-PROOF01.json', 'bytes': 5668, 'sha256': '3ffd1a85a430372521023eed5cc1406c83c0c65ac05f8a99290057ef2ba60009'}
for descriptor in (historical_props_raw,historical_props_archive,historical_props_source_proof):assert pin(descriptor['path'])==descriptor
historical_props_original=r['generatedOwningLayoutSuccessor']['originalC43Props']
assert (historical_props_original['bytes'],historical_props_original['sha256'])==(historical_props_raw['bytes'],historical_props_raw['sha256'])
historical_props_body=gzip.decompress(Path(historical_props_archive['path']).read_bytes());verify(historical_props_body,historical_props_original);assert historical_props_body==Path(historical_props_raw['path']).read_bytes()
historical_props_row={'stage':'EXACT_HISTORICAL_C43_METADATA_DONOR_NOT_CURRENT_SDK_SUCCESS','original':historical_props_original,'originalPaths':[historical_props_original['path']],'archive':historical_props_archive}
archive_index[(historical_props_original['path'],historical_props_original['bytes'],historical_props_original['sha256'])]=historical_props_row
archive_readbacks.append({'archive':historical_props_archive,'original':historical_props_original,'encoding':'EXACT_GZIP','wholeDecodedVerified':True,'historicalOnlyNoCurrentSDKClaim':True});decoded_bytes+=len(historical_props_body)
del historical_props_body
private_readbacks=[]
for descriptor in private_inputs:
 closed_pin_body(descriptor);private_readbacks.append({'originalLogicalPin':descriptor,'wholeRetainedBodyVerifiedAfterPrivateNamespaceClose':True,'historicalPhysicalPresenceNotClaimed':True})
for descriptor in r['generatedInputPins']:
 if descriptor['path'].startswith(str(PRIVATE_SCOPE)+'/'):closed_pin_body(descriptor)
 else:assert pin(descriptor['path'])==descriptor
for plan in component_plans.values():
 for descriptor in [plan['project'],*[x['input'] for x in plan['actualReferencePins']]]:closed_pin_body(descriptor)
assert r['immutableGitBlobInputsVerifiedAtCompletion'] is True and r['networkFetchAllowed'] is False and r['missingPackages']==[]
for descriptor in r['immutableGitBlobInputs']:
 assert descriptor['repository']==str(ROOT)
 body=subprocess.check_output(['git','-C',str(ROOT),'cat-file','blob',descriptor['objectId']]);verify(body,descriptor)
 assert hashlib.sha1(b'blob '+str(len(body)).encode()+b'\0'+body).hexdigest()==descriptor['objectId']
for package in r['packagePins']:
 for key in ('nupkg','checksum','metadata'):assert pin(package[key]['path'])==package[key]
FAILED58_RECONSTRUCTION_RECEIPT={'path':'/workspace/astra-source/a4-c45-targeted-owning-compiler58/output/C45-TARGETED-OWNING-COMPILER58-RECEIPT.json.gz','bytes':1244701,'sha256':'abfd4211e4d29e405970e95a7537ee54079dd038e9533b3d4726fa6d3746d7a3'}
assert pin(FAILED58_RECONSTRUCTION_RECEIPT['path'])==FAILED58_RECONSTRUCTION_RECEIPT
failed58=json.loads(gzip.decompress(Path(FAILED58_RECONSTRUCTION_RECEIPT['path']).read_bytes()))
assert failed58['exitCode']==1 and failed58['stages']==failed58['outputs']==failed58['actualFreshCscOwners']==failed58['genuinePriorCompilerReusedCscOwners']==[]
assert failed58['actualDesktopRuntimeClosureManifest'] is None and failed58['actualPluginFixtureRuntimeClosureManifest'] is None and failed58['actualOwningTestRuntimeClosureManifests']==[]
assert failed58['headAtCapture']==failed58['headAtCompletion']=='1f2f1778fda757a30b1e29c449c61978614f5875' and failed58['status']=='ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED'
prerequisite_restorations=[row for row in r['restoredActualQualifiedComponentGeneratedProducts'] if row.get('historicalC43PrerequisiteProvenance')]
assert len(prerequisite_restorations)==2
historical_desktop_manifest_pin=parent['actualDesktopRuntimeClosureManifest'];assert pin(historical_desktop_manifest_pin['path'])==historical_desktop_manifest_pin
historical_desktop_manifest=json.loads(gzip.decompress(Path(historical_desktop_manifest_pin['path']).read_bytes()))
assert historical_desktop_manifest['head']==parent['headAtCompletion'] and historical_desktop_manifest['actualRuntimeStage']['exitCode']==0
for row in prerequisite_restorations:
 witness=row['historicalC43PrerequisiteProvenance'];expected=witness['originalRequiredC43Input'];donor=witness['originalC43WholeArchiveDonor']
 assert witness['qualification']=='EXACT_HISTORICAL_C43_NORMAL_PREREQUISITE_RESTORATION_NOT_CURRENT_NORMAL_SUCCESS'
 assert witness['wholeOriginalDependencyConserved'] is True and witness['currentComponentArtifactNotOverwritten'] is True and witness['currentNormalClosureNotClaimed'] is True
 assert witness['originalC43NormalManifest']==historical_desktop_manifest_pin and row['actualRestored']==expected and expected in parent['inputs']
 assert Path(expected['path']).name in ('Haven.deps.json','Haven.runtimeconfig.json') and Path(expected['path']).parent==PRIVATE_SCOPE/'artifacts/bin/Haven.Desktop-checkpoint37-coherent19/debug_net10.0'
 assert donor in parent['outputArchives'] and donor['stage']=='C43-targeted46-final' and expected['path'] in donor['originalPaths']
 item=next(x for x in historical_desktop_manifest['files'] if x['relative']==Path(expected['path']).name)
 assert item['original']==expected and item['restoration']['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE' and item['restoration']['row']['archive']==donor['archive']==row['originalComponentCustody']
 verify(decode(donor),expected)
assert {Path(row['actualRestored']['path']).name for row in prerequisite_restorations}=={'Haven.deps.json','Haven.runtimeconfig.json'}
assert sum(row['actualRestored']['bytes'] for row in prerequisite_restorations)==91810
assert sum(row['actualRestored']['bytes'] for row in r['restoredActualQualifiedComponentGeneratedProducts'])<=182374123
assert r['metadataXmlAuthorityCount']==359
assert pin(r['finalCustody']['path'])==r['finalCustody']
final_custody_value=json.loads(gzip.decompress(Path(r['finalCustody']['path']).read_bytes()));assert final_custody_value['stage']=='current-targeted65-final'
assert all(row in r['outputArchives'] for row in final_custody_value['archives'])
initial_pin=final_custody_value['initial'];assert pin(initial_pin['path'])==initial_pin
initial_value=json.loads(gzip.decompress(Path(initial_pin['path']).read_bytes()));assert initial_value['head']==HEAD and initial_value['ticket']==r['ticket'] and initial_value['compilerOnly']
assert initial_value['sourceRows']==r['sourceRows'] and initial_value['completeInheritedPreferredSourceUnion']==r['completeInheritedPreferredSourceUnion'] and initial_value['historicalCompletePreferredSourceUnion']==r['historicalCompletePreferredSourceUnion']
assert initial_value['exactCurrentControllingSourceOverrides']==r['exactCurrentControllingSourceOverrides'] and initial_value['explicitReviewedSourceRetirements']==r['explicitReviewedSourceRetirements']
# SourceRows is the complete648 actual current source selection. The
# inherited preferred35 union is an additional historical/control declaration,
# not the authority for new current ticket paths outside that inherited set.
current_source_selection={row['target']:row for row in r['sourceRows']}
assert len(current_source_selection)==len(r['sourceRows'])==648
current_ticket_source={row['target']:row['afterPin'] for row in t['afterRows']}
assert len(current_ticket_source)==501 and set(current_ticket_source)<=set(current_source_selection)
for target,descriptor in current_ticket_source.items():assert all(current_source_selection[target][key]==descriptor[key] for key in ('bytes','sha256'))
for target,descriptor in r['completeInheritedPreferredSourceUnion'].items():assert all(current_source_selection[target][key]==descriptor[key] for key in ('bytes','sha256'))
assert len(r['completeInheritedPreferredSourceUnion'])==35
assert not set(current_source_selection)&{row['target'] for row in t['retiredRows']}
current_requests=''.join(HEAD+':'+target+'\n' for target in current_source_selection).encode()
current_blob_batch=subprocess.check_output(['git','-C',str(ROOT),'cat-file','--batch'],input=current_requests);current_position=0
for target,descriptor in current_source_selection.items():
 current_end=current_blob_batch.index(b'\n',current_position);current_parts=current_blob_batch[current_position:current_end].split();assert current_parts[1]==b'blob';current_size=int(current_parts[2]);current_body=current_blob_batch[current_end+1:current_end+1+current_size];current_position=current_end+2+current_size
 assert current_blob_batch[current_position-1:current_position]==b'\n';verify(current_body,descriptor);assert (ROOT/target).read_bytes()==current_body
assert current_position==len(current_blob_batch)
# Inherited preferred pins and every original override continue unchanged below.
for target,descriptor in r['completeInheritedPreferredSourceUnion'].items():
 assert target not in {x['target'] for x in t['retiredRows']}
 body=(ROOT/target).read_bytes();verify(body,descriptor)
for override in r['exactCurrentControllingSourceOverrides']:
 cp=override['controllingSourceTicket'];assert pin(cp['path'])==cp
 controlling=json.loads(Path(cp['path']).read_bytes());assert override['currentWholeSourceOverride'] is True
 reviewed=next(row for row in controlling['newRows'] if row['target']==override['target'])
 if reviewed['beforeExists']:
  old=subprocess.check_output(['git','-C',str(ROOT),'show',controlling['parentHead']+':'+override['target']]);verify(old,override['currentBefore'])
 else:assert subprocess.run(['git','-C',str(ROOT),'cat-file','-e',controlling['parentHead']+':'+override['target']],stdout=subprocess.PIPE,stderr=subprocess.PIPE).returncode!=0 and (override['currentBefore'] is None or (override['currentBefore']['bytes']==0 and override['currentBefore']['sha256']==hashlib.sha256(b'').hexdigest()))
 new=subprocess.check_output(['git','-C',str(ROOT),'show',controlling['afterHead']+':'+override['target']]);verify(new,override['currentAfter'])
 assert any(row['target']==override['target'] and row['beforePin']==override['currentBefore'] and row['afterPin']==override['currentAfter'] for row in controlling['newRows'])
assert len(initial_value['metadataXmlComparison'])==359
for comparison in initial_value['metadataXmlComparison']:
 current=comparison['current'];body=closed_pin_body(current)
 if comparison.get('exactBytes'):
  assert (current['bytes'],current['sha256'])==(comparison['restoredMetadataInput']['bytes'],comparison['restoredMetadataInput']['sha256'])
 elif comparison.get('exactContentCopyToPublishAndExistingIconOnly'):
  assert comparison['relative']=='apps/Home/src/AvaloniaHome/AvaloniaHome.csproj' and comparison==r['reviewedMetadataXmlExceptions'][comparison['relative']]
  assert comparison['wholeForwardInverseSourceVerified'] and comparison['projectPackageReferenceGraphChanged'] is False and comparison['normalOwningRestoreRequired'] is False and comparison['existing22ProjectAssetsAndPropertiesUnchanged'] is True and comparison['externalOwningHomeBuildAndPublishNotClaimed'] is True
  witness=sourcecheck['exactReviewedHomeContentResourceXmlWitness'];assert peer['exactReviewedHomeContentResourceXmlWitness']==witness
  assert {key:comparison['before'][key] for key in ('bytes','sha256')}==witness['before'] and {key:comparison['current'][key] for key in ('bytes','sha256')}==witness['after']
  content_before=b'    <Content Include="..\\..\\..\\..\\9to1 Workspace\\Home\\UI\\Home.cui" Link="UI\\Home.cui" CopyToOutputDirectory="PreserveNewest" />\n'
  content_after=content_before.replace(b' />',b' CopyToPublishDirectory="PreserveNewest" />')
  icon=b'    <EmbeddedResource Include="..\\..\\..\\..\\9to1 Workspace\\shared\\src\\Haven.Desktop\\Assets\\haven.ico" LogicalName="AvaloniaHome.Home.ico" />\n'
  assert body.count(content_after)==body.count(icon)==1;verify(body.replace(content_after,content_before,1).replace(icon,b'',1),comparison['before'])
  assert comparison['originalReviewedSourcePackets']==witness['originalSourcePackets']
  actual_graph=comparison['actualC31MetadataGraphAdmission'];source_graph=sourcecheck['currentC31MetadataGraphAdmissionSourceOnly'];assert peer['currentC31MetadataGraphAdmissionSourceOnly']==source_graph
  assert actual_graph['receipt']==source_graph['receipt'] and pin(actual_graph['receipt']['path'])==actual_graph['receipt']
  assert actual_graph['wholeExactOtherXmlBodies']==source_graph['wholeExactOtherXmlBodies']==358 and actual_graph['exactReviewedNonGraphXmlBodies']==source_graph['exactReviewedNonGraphHomeXmlBodies']==1
  assert actual_graph['wholeOriginalHomeXmlInverseVerifiedAgain'] and actual_graph['projectPackagePropertyGraphUnchanged'] and actual_graph['existing22MetadataProductsUnchanged'] and actual_graph['noOutside22OwningBuildAcceptance']
  assert source_graph['wholeOriginalHomeXmlInverseVerifiedAgain'] and source_graph['allProjectReferencePackageReferencePropertyGraphUnchanged'] and source_graph['existing22GeneratedMetadataAndOwningGraphNotReplaced'] and source_graph['noSDKOrOwningRestoreExecutedBySourcecheck']

  for descriptor in comparison['originalReviewedSourcePackets']:assert pin(descriptor['path'])==descriptor
 elif comparison.get('twoExplicitCompileItemsOnly'):
  resolver=b'    <Compile Include="ModelRouteResolver.CatalogueEligibility.cs" />\n';engines=b'    <Compile Include="InferenceEngines/**/*.cs" />\n'
  assert body.count(resolver)==body.count(engines)==1;verify(body.replace(resolver,b'',1).replace(engines,b'',1),comparison['restoredMetadataInput'])
 else:
  assert comparison['relative'] in r['reviewedMetadataXmlExceptions'] and comparison==initial_value['reviewedMetadataXmlExceptions'][comparison['relative']]
  assert comparison['wholeForwardInverseSourceVerified'] is True
  insertion=b'    <ProjectReference Include="..\\..\\Files\\NativeHost\\HavenOS.Files.NativeHost.csproj" />\n' if comparison['relative']=='9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj' else b'    <Compile Include="../../../Dev/Tests/DeveloperOriginalPreparedConsentTests.cs" Link="Dev/DeveloperOriginalPreparedConsentTests.cs" />\n'
  assert body.count(insertion)==1;verify(body.replace(insertion,b'',1),comparison['before'])
assert set(r['reviewedMetadataXmlExceptions'])=={'9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj','9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj','9to1 Workspace/shared/tests/Haven.Console.Tests/Haven.Console.Tests.csproj','apps/Home/src/AvaloniaHome/AvaloniaHome.csproj'}
console_rel='9to1 Workspace/shared/tests/Haven.Console.Tests/Haven.Console.Tests.csproj'
console_body=(ROOT/console_rel).read_bytes();console_before=subprocess.check_output(['git','-C',str(ROOT),'show',basis['headAtCompletion']+':'+console_rel])
rootnamespace=b'    <RootNamespace>Haven.Desktop.Tests</RootNamespace>\n'
assert console_body.count(rootnamespace)==1 and console_body.replace(rootnamespace,b'',1)==console_before
# Every exact carrier/transport is rebuilt from full original bytes, including
# missing historical gzip paths; lib validates physical literals and Git blobs.
for archive_path,transport in r['actualParentArchiveTransports'].items():
 if transport['kind']=='PHYSICAL_EXACT_ORIGINAL_GZIP':assert transport['physical']['path']==archive_path and pin(archive_path)==transport['physical']
 else:
  descriptor=transport['descriptor'];assert pin(descriptor['path'])==descriptor
  for physical in transport.get('actualPhysicalInputs',[]):assert pin(physical['path'])==physical
  for immutable in transport.get('immutableGitBlobInputs',[]):
   raw=subprocess.check_output(['git','-C',immutable['repository'],'cat-file','blob',immutable['objectId']]);verify(raw,immutable)
# Exact original9 Console fixture test bodies and14 cases are conserved by
# whole physical/Git source and current real evaluation; helper namespace alone changes.
console_source_items=[x for x in component_plans['Haven.Console.Tests']['physicalItems'] if x['kind']=='Compile']
assert len(console_source_items)==9
for item in console_source_items:
 logical=Path(item['logical']['path']);assert logical.is_relative_to(SHADOW_SCOPE)
 relative=str(logical.relative_to(SHADOW_SCOPE));before=subprocess.check_output(['git','-C',str(ROOT),'show',basis['headAtCompletion']+':'+relative]);assert before==(ROOT/relative).read_bytes()
 verify(before,item['logical'])
fixture_source_bodies=[(ROOT/Path(item['logical']['path']).relative_to(SHADOW_SCOPE)).read_bytes() for item in console_source_items]
assert sum(len(re.findall(rb'\[Fact(?:\(|\])',body)) for body in fixture_source_bodies)==4
assert sum(len(re.findall(rb'\[InlineData\(',body)) for body in fixture_source_bodies)==10
assert sum(len(re.findall(rb'\[Theory(?:\(|\])',body)) for body in fixture_source_bodies)==5
# Exact original9 fixture source bodies conserve4facts+10inline cases=14.
# Normal physical namespace admission remains a historical actual observation,
# not a claim that this verifier has reopened the private /dev tmpfs.
assert pin(r['physicalNamespaceAdmission']['path'])==r['physicalNamespaceAdmission']
admission=json.loads(Path(r['physicalNamespaceAdmission']['path']).read_bytes())
assert admission['logicalScope']==str(PRIVATE_SCOPE) and admission['scopeIsDirectPhysicalDirectory'] and admission['beforeCreationEntirePrivateShmEmpty'] and admission['hostSharedScopeExposedOrModified'] is False

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
# Reconstruct the complete original source/input census and current disposition.
historical_readbacks=[]
assert [x['originalC43Input'] for x in r['wholeHistoricalC43InputCurrentDisposition']]==parent['inputs']
for disposition in r['wholeHistoricalC43InputCurrentDisposition']:
 original=disposition['originalC43Input'];status=disposition['status']
 if status=='HISTORICAL_NORMAL_RUNTIME_COPY_FULL_RESTORATION_CUSTODY':
  assert disposition['physicalRestorationRequiredForCompiler'] is False
  item={'original':original,'restoration':disposition['restoration']};runtime_item_readback(item)
 else:
  current=disposition['current'];closed_pin_body(current)
  if status=='WHOLE_ORIGINAL_C43_INPUT_EXACT':assert current==original
  elif status=='EXACT_GENUINE_COMPONENT_ARTIFACT_SUPERSESSION_FROM_SUCCESSFUL_CURRENT59':
   assert disposition['actualSuccessfulCurrent59CompilerReceipt']==CURRENT59_RECEIPT and disposition['actualCurrent59FullClosedReadback']==CURRENT59_CLOSED
   assert disposition['componentOwner'] in current59_outputs
   row=disposition['wholeCurrentArtifactCustody'];assert row in current59['outputArchives'];verify(decode(row),current)
   closed_pin_body(original,parent['headAtCompletion'])
  elif status=='EXACT_REVIEWED_GENERATED_OWNING_LAYOUT_SUCCESSOR':
   assert disposition['generatedOwningLayoutSuccessor']==r['generatedOwningLayoutSuccessor'] and original==r['generatedOwningLayoutSuccessor']['originalC43Props']
   closed_pin_body(original,parent['headAtCompletion'])
  else:
   assert status=='EXACT_REVIEWED_CURRENT_SOURCE_SUPERSESSION'
   correction=disposition['sourceChange'];cp=correction['ticket'];assert pin(cp['path'])==cp
   controlling=json.loads(Path(cp['path']).read_bytes());assert correction['target'] in {x['target'] for x in controlling['newRows']}
   before=subprocess.check_output(['git','-C',str(ROOT),'show',controlling['parentHead']+':'+correction['target']]);verify(before,correction['before'])
   after=subprocess.check_output(['git','-C',str(ROOT),'show',controlling['afterHead']+':'+correction['target']]);verify(after,correction['after'])
   assert current['bytes']==correction['after']['bytes'] and current['sha256']==correction['after']['sha256']
   closed_pin_body(original,parent['headAtCompletion'])
 historical_readbacks.append({'originalC43Input':original,'currentDispositionStatus':status,'wholeOriginalAndCurrentCustodyVerified':True})
# Every parent runtime closure/package file remains wholly reconstructable.
current59_input_readbacks=[]
assert [x['originalCurrent59Input'] for x in r['wholeSuccessfulCurrent59InputCurrentDisposition']]==current59['inputs']
for disposition in r['wholeSuccessfulCurrent59InputCurrentDisposition']:
 original=disposition['originalCurrent59Input'];status=disposition['status']
 if status=='CURRENT59_RETIRED_NORMAL_COPY_WHOLE_RESTORATION_CUSTODY':
  assert disposition['physicalCurrentCompilerInput'] is False;runtime_item_readback({'original':original,'restoration':disposition['restoration']})
 elif status=='CURRENT59_PRIVATE_NONCOMPILER_OBSERVATION_WHOLE_ARCHIVE':
  assert disposition['physicalCurrentCompilerInput'] is False and disposition['wholeOriginalCustody'] in current59['outputArchives'];verify(decode(disposition['wholeOriginalCustody']),original)
 else:
  current=disposition['current'];closed_pin_body(current)
  if status=='WHOLE_ORIGINAL_CURRENT59_INPUT_EXACT':assert current==original
  else:
   assert status=='REVIEWED_EXACT_C46_SOURCE_SUPERSESSION';reviewed=disposition['reviewedSource'];target=reviewed['target']
   assert any(reviewed in x['newRows'] for x in reviewed_chain) and reviewed['beforeExists']
   verify(subprocess.check_output(['git','-C',str(ROOT),'show',current59['headAtCompletion']+':'+target]),original)
   verify((ROOT/target).read_bytes(),current);assert all(current[k]==current_source_selection[target][k] for k in ('bytes','sha256'))
 current59_input_readbacks.append(disposition)
for row in [*current59['actualOwningTestRuntimeClosureManifests'],*current59['genuineUnchangedC43TestRuntimeClosureManifests']]:
 descriptor=row['manifest'];assert pin(descriptor['path'])==descriptor;manifest=json.loads(gzip.decompress(Path(descriptor['path']).read_bytes()));assert manifest['actualRuntimeStage']['exitCode']==0 and manifest['fileCount']==len(manifest['files'])
 for item in manifest['files']:runtime_item_readback(item)
for field in ('actualDesktopRuntimeClosureManifest','actualPluginFixtureRuntimeClosureManifest'):
 descriptor=current59[field];assert pin(descriptor['path'])==descriptor;manifest=json.loads(gzip.decompress(Path(descriptor['path']).read_bytes()));assert manifest['actualRuntimeStage']['exitCode']==0
 for item in manifest['files']:runtime_item_readback(item)
for row in [*parent['actualOwningTestRuntimeClosureManifests'],*parent['genuineUnchangedC42TestRuntimeClosureManifests']]:
 m=row['manifest'];assert pin(m['path'])==m
 value=json.loads(gzip.decompress(Path(m['path']).read_bytes()));assert value['module']==row['module'] and value['actualRuntimeStage']['exitCode']==0 and value['fileCount']==len(value['files'])
 for item in value['files']:runtime_item_readback(item)
for item in r['historicalNormalRuntimePackageCopyRows']:runtime_item_readback(item)

closures=[];runtime_readbacks=[]
manifest_rows=[]
if r['actualDesktopRuntimeClosureManifest']:manifest_rows.append({'module':'Haven.Desktop','manifest':r['actualDesktopRuntimeClosureManifest']})
manifest_rows.extend(r['actualOwningTestRuntimeClosureManifests'])
if r['actualPluginFixtureRuntimeClosureManifest']:manifest_rows.append({'module':'Haven.PluginFixture','manifest':r['actualPluginFixtureRuntimeClosureManifest']})
for row in manifest_rows:
 m=row['manifest'];assert pin(m['path'])==m;v=json.loads(gzip.decompress(Path(m['path']).read_bytes()))
 assert v['head']==HEAD and v['ticket']==r['ticket'] and v['allActualCompiledProductIdentitiesUnchanged'] and v['noSecondCompiler'] and v['actualRuntimeStage']['exitCode']==0
 assert pin(v['dotnet']['path'])==v['dotnet']
 if v.get('restorer'):assert pin(v['restorer']['path'])==v['restorer']
 seen=set();checked=[]
 for item in v['files']:
  relative=Path(item['relative']);assert not relative.is_absolute() and '..' not in relative.parts and str(relative) not in seen;seen.add(str(relative));checked.append(runtime_item_readback(item))
 assert len(checked)==v['fileCount'] and sum(i['original']['bytes'] for i in checked)==v['wholeCopiedBytes']
 provenance=v['actualCompilerSuccessProvenance']
 assert provenance['compiledSourceHead']==current_outputs[row['module']]['compiledSourceHead']
 assert provenance['currentCompilerInvoked']==current_outputs[row['module']]['compiledInCurrentInterval']
 if not provenance['currentCompilerInvoked']:
  assert row['module'] in EXPECTED_REUSED and provenance['actualCurrentParentSuccessfulCompilerReceipt']==CURRENT59_RECEIPT and provenance['actualCurrentParentFullClosedReadback']==CURRENT59_CLOSED and provenance['actualCurrentParentNaturalExitCode']==0
  assert provenance.get('completeCurrentSourceResourceCscRefAnalyzerEquivalence') or provenance.get('currentWholeSourceRefAndCompiledBytesUnchanged')
  origin=current59;origin_pin=CURRENT59_RECEIPT;chain=[];origin_seen=set()
  while True:
   assert origin_pin['path'] not in origin_seen;origin_seen.add(origin_pin['path']);assert pin(origin_pin['path'])==origin_pin
   product=next(x for x in origin['outputs'] if x['module']==row['module']);assert product['compiledSourceHead']==current_outputs[row['module']]['compiledSourceHead'] and all(product.get(k)==current_outputs[row['module']].get(k) for k in ('output','target','pdb','reference'))
   failed=origin_pin==EXACT_FAILED_RECEIPT
   if origin['exitCode']!=0:assert failed and origin['exitCode']==1 and basis_closed['currentCompiledOutputPins']==origin['outputs']
   chain.append({'receipt':origin_pin,'originalReceiptNaturalExitCode':origin['exitCode'],'compiledSourceHead':product['compiledSourceHead'],'compiledInOriginalInterval':product['compiledInCurrentInterval'],'failed55IndividualComponentCustody':EXACT_FAILED_CUSTODY if failed else None,'failedIntervalNotClaimedFullSuccessful':failed})
   if product['compiledInCurrentInterval']:break
   origin_pin=product['reusedFromActualCompilerReceipt'];assert pin(origin_pin['path'])==origin_pin;origin=json.loads(gzip.decompress(Path(origin_pin['path']).read_bytes()));assert origin['inputsUnchangedAtCompletion']
  original_stage=next(x for x in origin['stages'] if x['stage']==row['module']+'-compiler01');assert original_stage['exitCode']==0 and origin['headAtCapture']==origin['headAtCompletion']==product['compiledSourceHead']
  assert provenance['exactQualifiedCompilerReceiptChain']==chain and provenance['actualOriginalCompilerReceipt']==origin_pin and provenance['actualOriginalCompilerStage']==original_stage and provenance['actualOriginalNaturalExitCode']==0
 else:
  assert row['module'] in EXPECTED_FRESH and provenance['compiledSourceHead']==HEAD and provenance['actualCurrentNaturalExitCode']==0 and stage_map[row['module']+'-compiler01']['exitCode']==0
  if row['module']=='Haven.Desktop':assert provenance['actualCurrentCompilerStage']==stage_map[row['module']+'-compiler01'] and provenance['currentParentSuccessfulCompilerReceipt']==CURRENT59_RECEIPT and provenance['currentParentFullClosedReadback']==CURRENT59_CLOSED
  else:assert provenance=={'currentCompilerInvoked':True,'actualCurrentNaturalExitCode':0,'compiledSourceHead':HEAD}
 normal_log=decode(v['actualRuntimeStage']['exactWholeStdoutCustody'])
 if row['module']!='Haven.PluginFixture':assert b'Compilation request ' not in normal_log and not re.search(rb'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\x22]|$)',normal_log)
 names={item['relative'] for item in v['files']};assembly=component_plans[row['module']]['properties']['AssemblyName'];required={assembly+'.dll',assembly+'.deps.json',assembly+'.runtimeconfig.json'}
 if row['module'] not in ('Haven.Desktop','Haven.PluginFixture'):required.add('testhost.dll');assert set(v['requiredActualRuntimeFiles'])==required
 assert required<=names
 closures.append({'module':row['module'],'manifest':m,'files':v['fileCount'],'wholeBytes':v['wholeCopiedBytes'],'actualCompilerSuccessProvenance':provenance,'dotnet':v['dotnet'],'restorer':v.get('restorer'),'actualOutputDirectory':v['actualOriginalOutputDirectory'],'normalOutputFilesFullyReconstructable':True});runtime_readbacks.extend(checked)
qualified_historical=[]
for row in r['genuineUnchangedC43TestRuntimeClosureManifests']:
 assert row in [*parent['actualOwningTestRuntimeClosureManifests'],*parent['genuineUnchangedC42TestRuntimeClosureManifests']]
 m=row['manifest'];assert pin(m['path'])==m
 parent_current_witness=next((x for x in parent_closed['actualNormalRuntimeClosures'] if x['module']==row['module'] and x['manifest']==m),None)
 parent_historical_witness=next((x for x in parent_closed['qualifiedHistoricalC42NormalTestRuntimeClosures'] if x['module']==row['module'] and x['manifest']==m),None)
 assert parent_current_witness is not None or parent_historical_witness is not None
 v=json.loads(gzip.decompress(Path(m['path']).read_bytes()))
 assert v['actualRuntimeStage']['exitCode']==0
 if parent_current_witness is not None:assert v['head']==parent['headAtCompletion'] and v['ticket']==parent['ticket']
 else:assert parent_historical_witness['actualOriginalNormalOutputHead']==v['head'] and parent_historical_witness['currentWholeSourceResourceRefEquivalence']
 assert not current_decisions[row['module']]['freshCscRequired'] and current_decisions[row['module']]['allResolvedRefsWholeEqual']
 assert current_outputs[row['module']]['compiledSourceHead']==v['actualCompilerSuccessProvenance']['compiledSourceHead']
 checked=[]
 for item in v['files']:
  checked.append(runtime_item_readback(item))
  if '/' not in item['relative']:
   matches=[o for o in r['outputs'] if Path(o['target']['path']).name==item['relative']]
   if matches:assert len(matches)==1 and item['original']['bytes']==matches[0]['target']['bytes'] and item['original']['sha256']==matches[0]['target']['sha256']
 assert len(checked)==v['fileCount'] and sum(i['original']['bytes'] for i in checked)==v['wholeCopiedBytes']
 qualified_historical.append({'module':row['module'],'manifest':m,'actualOriginalNormalOutputHead':v['head'],'actualCompiledSourceHead':v['actualCompilerSuccessProvenance']['compiledSourceHead'],'currentWholeSourceResourceRefEquivalence':True,'currentNormalOutputNotInvoked':True,'files':v['fileCount'],'wholeBytes':v['wholeCopiedBytes'],'wholeRestorationReadbacks':checked})
fixture_proofs=[];fixture_audit_readback=None
for proof in r['actualPluginFixtureStageProofs']:
 assert proof['module']=='Haven.PluginFixture' and proof['actualStage']['exitCode']==0 and proof['skipCompilerExecutionOnlyForOriginalFixture'] and proof['actualCurrentCompilerInvoked']==current_outputs['Haven.PluginFixture']['compiledInCurrentInterval']
 assert proof['wholePinsUnchangedBeforeAndAfter'] and proof['genuineSdkCreateAppHostRequested'] and proof['noSourceOrReferenceExcluded']
 command=proof['actualStage']['command'];assert '-p:SkipCompilerExecution=true' in command
 assert '-t:ResolveReferences;_CreateAppHost;GenerateBuildDependencyFile;GenerateBuildRuntimeConfigurationFiles;CopyFilesToOutputDirectory' in command
 assert fixture_audit_readback is None and proof['actualStage']==stage_map['Haven.PluginFixture-normal-fixture-runtime-output01']
 actual_stage=proof['actualStage'];capture=actual_stage['fixtureExecAuditCapture']
 assert actual_stage['actualPidKind']=='OWNED_STRACE_WRAPPER' and actual_stage['capturePipelineFailure'] is None and actual_stage['livePublicationError'] is None
 assert actual_stage['actualNaturalSDKExitNotInferredFromRenderedCommand'] is True
 capture_pin=actual_stage['fixtureExecAuditCaptureReceipt'];assert pin(capture_pin['path'])==capture_pin
 assert json.loads(gzip.decompress(Path(capture_pin['path']).read_bytes()))==capture
 assert capture['forcedTermination'] is None and capture['actualWrapperExitCode']==actual_stage['exitCode']==0 and not capture['captureFaults']
 assert capture['requestedChildCommand']==command and capture['actualPopenCommand']==actual_stage['actualPopenCommand']
 assert capture['actualWrapperPid']==capture['actualWrapperProcessGroup']==actual_stage['pid'] and isinstance(actual_stage['pid'],int) and actual_stage['pid']>0
 assert capture['bothWholeStreamsNaturallyEOF'] and capture['bothWholeStreamsEOF'] and capture['allObservedBytesRetainedWithoutTruncation'] and capture['allWholeStreamsPersisted'] and not capture['unpublishedWholeStreams']
 assert capture['streamCapsBytes']=={'diagnostic':8388608,'audit':1048576} and capture['closedUnixSeconds']>=capture['startedUnixSeconds']
 assert all(isinstance(x,int) and 0<x<=1048576 for x in capture['measuredKernelPipeBytes'].values()) and set(capture['measuredKernelPipeBytes'])=={'diagnostic','audit'}
 audit_row=actual_stage['wholeFixtureExecAuditCustody'];assert audit_row in r['outputArchives'] and audit_row['original']==capture['wholeAuditPin']
 diagnostic=decode(actual_stage['exactWholeStdoutCustody']);verify(diagnostic,capture['wholeDiagnosticPin']);audit=decode(audit_row);verify(audit,capture['wholeAuditPin'])
 assert capture['wholeDiagnosticPin']==actual_stage['actualChildOutputPin']
 assert capture['streamBytes']=={'diagnostic':len(diagnostic),'audit':len(audit)} and len(diagnostic)<=8388608 and len(audit)<=1048576
 assert all(row in r['outputArchives'] for row in actual_stage['fixtureExecAuditFailureReportCustody'])
 for row in actual_stage['fixtureExecAuditFailureReportCustody']:decode(row)
 assert not any('failure-report' in row['stage'] for row in actual_stage['fixtureExecAuditFailureReportCustody'])
 audit_helper=binding['fixtureAuditHelper'];audit_spec=importlib.util.spec_from_file_location('closed_fixture_exec_audit58',audit_helper['path']);audit_lib=importlib.util.module_from_spec(audit_spec);audit_spec.loader.exec_module(audit_lib)
 # Pure readback: qualify() can publish on rejection, so replay the two pure
 # parsers and exact tools check directly without starting or attaching a child.
 exec_proof=audit_lib.parse_exec_audit(audit,command);task_proof=audit_lib.parse_actual_csc_skip(diagnostic);observer_proof=audit_lib.parse_observer_diagnostics(diagnostic,exec_proof);tools=audit_lib.verify_tools()
 assert observer_proof['unknownObserverDiagnosticCount']==0 and observer_proof['observerWarningsErrorsDetachOmissionOrUnknownPrefixesAccepted'] is False
 assert observer_proof['allAttachmentPidsUniqueAndObservedNaturalZero'] is True and observer_proof['noObserverNoticeDoesNotIndependentlyProveTraceCompleteness'] is True
 assert capture['tools']==tools and tools['observer']==audit_helper
 observer={'tools':tools,'diagnosticCapBytes':8388608,'auditCapBytes':1048576,'additionalResidentAllowanceBytes':67108864,'additionalDurableEvidenceAllowanceBytes':67108864,'maxKernelPipeBytesEach':1048576,'readChunkBytes':65536,'sourceOnlyDoesNotCertifyActualExecOrSkip':True}
 assert proof['actualFixtureExecAuditObserver']==r['actualFixtureExecAuditObserver']==observer
 assert '-p:UseSharedCompilation=false' in command and '-v:diagnostic' in command
 expected_status='ACTUAL_FIXTURE_ZERO_COMPILER_EXEC_WITH_EXPLICIT_TASK_SKIP_AND_NATURAL_TRACE_CLOSE' if task_proof['actualTaskSkipWitnessObserved'] else 'ACTUAL_FIXTURE_ZERO_COMPILER_EXEC_NO_CSC_TASK_RENDERED'
 expected_qualification={'status':expected_status,'capture':capture,'actualExecProof':exec_proof,'actualCscTaskSkipProof':task_proof,'actualObserverDiagnosticProof':observer_proof,'actualTaskSkipWitnessObserved':task_proof['actualTaskSkipWitnessObserved'],'noSecondCompilerQualified':True,'allProtectedSourceRefCompilerOutputPinsMustSeparatelyRemainEqual':True,'diagnosticAndExecEvidenceAreDistinct':True,'zeroTaskBranchDoesNotClaimObservedSkipTrue':not task_proof['actualTaskSkipWitnessObserved']}
 expected_qualification=json.loads(json.dumps(expected_qualification))
 assert proof['noSecondCompilerQualified'] is True and proof['actualFixtureExecAuditQualification']==expected_qualification
 qualification_pin=proof['actualFixtureExecAuditQualificationReceipt'];assert qualification_pin==actual_stage['fixtureExecAuditQualificationReceipt'] and pin(qualification_pin['path'])==qualification_pin
 assert json.loads(gzip.decompress(Path(qualification_pin['path']).read_bytes()))==expected_qualification
 expected_protected=[*[x['logical'] for x in component_plans['Haven.PluginFixture']['physicalItems']],*[x['input'] for x in component_plans['Haven.PluginFixture']['actualReferencePins']],*[o[key] for o in r['outputs'] for key in ('output','target','pdb','reference') if o.get(key)]]
 assert proof['wholeProtectedSourceReferenceAndCompilerPins']==proof['wholeProtectedSourceReferenceAndCompilerPinsAfter']==expected_protected
 for descriptor in expected_protected:closed_pin_body(descriptor)
 assert proof['originalCompilerOutput']==current_outputs['Haven.PluginFixture']
 assert r['actualPluginFixtureRuntimeClosureManifest'] is None, 'Actual Fixture exec/Skip qualification passes; poststage provenance assertion still prevents any successful normal manifest.'
 assert exec_proof['otherExecAttemptCount']==exec_proof['compilerExecAttemptCount']==0 and exec_proof['wholeTraceParsedWithoutOmissionOrTruncation'] and all(value==0 for value in exec_proof['allObservedProcessExitCodes'].values())
 fixture_audit_readback={'observer':observer,'captureReceipt':capture_pin,'qualificationReceipt':qualification_pin,'wholeDiagnosticCustody':actual_stage['exactWholeStdoutCustody'],'wholeExecAuditCustody':audit_row,'actualQualification':expected_qualification,'actualProtectedBeforeAfterWholePinCount':len(expected_protected),'wholeOriginalBeforeAfterEqualityAndClosedBodiesReplayed':True}
 del diagnostic,audit

 fixture_proofs.append(proof)
assert len(r['requiredLaterNormalInputsExplicitlyRetained'])==5
for path in r['requiredLaterNormalInputsExplicitlyRetained']:assert any(p[0]==path for p in original_paths)
released=[]
for item in r['releasedNewTaskOwnedRuntimeCopies']:
 assert item['wholeSourceRoundtripBeforeRelease'] and item['onlyNewTaskOwnedCopyLocalOutput'] and item['physicalAtFinal'] is False
 runtime_item_readback(item);released.append({'original':item['original'],'physicallyRetiredInOwnedDurableNormalDirectory':True,'wholeRestorationVerifiedAfterNaturalClose':True})
assert len(released)==len(r['wholeReleasedCopyRestorationRevalidation'])

# Additive allocation55 readback. Original compiler/source/restoration gates above
# remain exact; current normal CopyLocal files have their actual durable paths.
NORMAL_ROOT=Path('/workspace/astra-root-current-normal-output65')
ORIGINAL_SCOPE=Path('/dev/shm/astra-framework-owning-metadata-20261006-03')
CURRENT_OUTPUT_ROOT=Path('/workspace/astra-source/a4-current-targeted-owning-compiler65/output')
NORMAL_PROPERTY_NAMES={'TargetPath','OutDir','OutputPath','IntermediateOutputPath','ProjectAssetsFile','TargetRefPath','AssemblyName','TargetFramework'}
DISK_COMPONENTS={'largestActualParentNormalBytes':964354689,'normalCopyGrowthAndBlockRoundingAllowance':8388608,'retainedActualPluginFixtureNormalBytes':103616,'completeDurableBuildEvidenceEnvelope':35559951+67108864,'tinyGenuinePrerequisiteAliasAllowance':8388608,'finalDiskSpare':134217728}
DISK_TOTAL=sum(DISK_COMPONENTS.values());assert DISK_TOTAL==1218122064==1151013200+67108864
ALIAS_BUDGET=8388608
assert r['namespace']==str(ORIGINAL_SCOPE)
assert receipt.name=='CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz'
assert receipt.parent in (CURRENT_OUTPUT_ROOT,Path('/workspace/astra-source/a4-current-targeted-owning-output-custody65/exact-gzip'))
for output in r['outputs']:
 for key in ('output','target','pdb','reference'):
  if output.get(key):assert Path(output[key]['path']).is_relative_to(ORIGINAL_SCOPE/'artifacts'),('Compiler output allocation must remain original private namespace',output['module'],key)

import xml.etree.ElementTree as ET
owning_layout=r['generatedOwningLayoutSuccessor'];actual_props=owning_layout['actualProps'];props_path=str(ORIGINAL_SCOPE/'Checkpoint37CoherentOwningLayout.props')
assert actual_props['path']==props_path and actual_props in r['inputs']
props_custody=owning_layout['actualPropsCustody'];assert props_custody['original']==actual_props and props_path in props_custody['originalPaths'] and props_custody in r['outputArchives']
actual_props_body=decode(props_custody);verify(actual_props_body,actual_props)
assert owning_layout['exactSource50Props']=={'path':props_path,'bytes':2481,'sha256':'a9a5754608a141cc7adc5a2a5adb0e298067ad3c530f24afeaa5e467948adc0f'}
assert owning_layout['originalC43Props']=={'path':props_path,'bytes':2429,'sha256':'bcc3f9798b5cad579928eac2d428ea2afc31e903756588e047d28f712d36c4db'}
assert owning_layout['originalC43Props'] in parent['inputs']
assert owning_layout['wholeInverseSource50Props'] is True and owning_layout['wholeInverseOriginalC43Props'] is True and owning_layout['onlyNormalOwnerPropertyGroupAndNewConsoleCondition'] is True
props_xml=ET.fromstring(actual_props_body);assert props_xml.tag=='Project'
normal_condition="'$(RootNormalOutputModule)' != '' And '$(RootNormalOutputModule)' == '$(MSBuildProjectName)'"
normal_groups=[group for group in props_xml if group.tag=='PropertyGroup' and group.get('Condition')==normal_condition];assert len(normal_groups)==1 and list(props_xml)[-1] is normal_groups[0]
normal_group=normal_groups[0];assert normal_group.attrib=={'Condition':normal_condition} and [(child.tag,child.attrib,child.text) for child in normal_group]==[('OutDir',{},'$(RootNormalOutputDirectory)'),('OutputPath',{},'$(RootNormalOutputDirectory)')]
props_xml.remove(normal_group);ET.indent(props_xml);source50_props_body=ET.tostring(props_xml,encoding='unicode').encode();verify(source50_props_body,owning_layout['exactSource50Props'])
console_condition="'$(MSBuildProjectName)' == 'Haven.Console.Tests'"
console_groups=[group for group in props_xml if group.tag=='PropertyGroup' and console_condition in group.get('Condition','').split(' Or ')];assert len(console_groups)==1
console_group=console_groups[0];conditions=console_group.attrib['Condition'].split(' Or ');assert conditions.count(console_condition)==1
conditions.remove(console_condition);console_group.attrib['Condition']=' Or '.join(conditions)
ET.indent(props_xml);original43_props_body=ET.tostring(props_xml,encoding='unicode').encode();verify(original43_props_body,owning_layout['originalC43Props'])
generated_layout_dispositions=[entry for entry in r['wholeHistoricalC43InputCurrentDisposition'] if entry['status']=='EXACT_REVIEWED_GENERATED_OWNING_LAYOUT_SUCCESSOR']
assert len(generated_layout_dispositions)==1 and generated_layout_dispositions[0]['originalC43Input']==owning_layout['originalC43Props'] and generated_layout_dispositions[0]['current']==actual_props and generated_layout_dispositions[0]['generatedOwningLayoutSuccessor']==owning_layout
owning_layout_readback={'actualProps':actual_props,'actualPropsCustody':props_custody,'exactSource50Props':owning_layout['exactSource50Props'],'originalC43Props':owning_layout['originalC43Props'],'wholeInverseSource50PropsVerified':True,'wholeInverseOriginalC43PropsVerified':True,'onlyExactNormalOwnerPropertyGroupAndReviewedConsoleConditionAdded':True}

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

allocation=r['durableNormalAllocation']
assert allocation['normalRoot']==str(NORMAL_ROOT)
assert allocation['declaredDiskEnvelope']=={'components':DISK_COMPONENTS,'totalBytes':DISK_TOTAL}
assert allocation['rawResidentRequirementBytes']==534695659==467586795+67108864 and allocation['originalRawRequirementBytes']==1431939948 and allocation['onlyMovedNormalTreeBytes']==964353153
assert allocation['rawResidentRequirementBytes']+allocation['onlyMovedNormalTreeBytes']==allocation['originalRawRequirementBytes']+67108864 and allocation['noCacheCredit'] is True
assert allocation['normalAliasBudgetBytes']==ALIAS_BUDGET
workspace_readback=workspace_filesystem_readback(allocation['workspaceFilesystem'])
normal_root_stat=direct_directory(NORMAL_ROOT);assert normal_root_stat.st_dev==workspace_readback['device']
samples=allocation['capacityMeasurements'];assert samples and samples[0]['label']=='actual-durable-normal-admission'
assert samples[0]['workspaceAvailableBytes']>=DISK_TOTAL
for key in ('normalLogicalBytes','normalFileAllocatedBytes','normalDirectoryAllocatedBytes','totalNormalAllocatedBytes','normalFileCount','retainedFixtureAllocatedBytes'):assert samples[0][key]==0
assert samples[0]['activeFullNormalModules']==[]
assert samples[0]['activeFullNormalAllocatedBytes']=={}
previous_sample_time=0
for sample in samples:
 assert isinstance(sample['atUTCUnixSeconds'],(int,float)) and sample['atUTCUnixSeconds']>=previous_sample_time;previous_sample_time=sample['atUTCUnixSeconds']
 assert sample['workspaceDevice']==workspace_readback['device'] and sample['filesystemType']==workspace_readback['filesystemType']
 for key in ('workspaceAvailableBytes','normalLogicalBytes','normalFileAllocatedBytes','normalDirectoryAllocatedBytes','totalNormalAllocatedBytes','normalFileCount','retainedFixtureAllocatedBytes','durableEvidenceLogicalBytes','durableEvidenceAllocatedBytes'):assert isinstance(sample[key],int) and sample[key]>=0,(sample,key)
 assert sample['durableEvidenceAllocatedBytes']<=DISK_COMPONENTS['completeDurableBuildEvidenceEnvelope']
 assert sample['retainedFixtureAllocatedBytes']<=DISK_COMPONENTS['retainedActualPluginFixtureNormalBytes']+DISK_COMPONENTS['normalCopyGrowthAndBlockRoundingAllowance']
 assert sample['totalNormalAllocatedBytes']<=DISK_COMPONENTS['largestActualParentNormalBytes']+DISK_COMPONENTS['normalCopyGrowthAndBlockRoundingAllowance']+DISK_COMPONENTS['retainedActualPluginFixtureNormalBytes']
 assert sample['totalNormalAllocatedBytes']+sample['durableEvidenceAllocatedBytes']<=DISK_TOTAL-DISK_COMPONENTS['finalDiskSpare']
 assert sample['totalNormalAllocatedBytes']==sample['normalFileAllocatedBytes']+sample['normalDirectoryAllocatedBytes']
 assert len(sample['activeFullNormalModules'])<=1 and len(set(sample['activeFullNormalModules']))==len(sample['activeFullNormalModules'])
 assert set(sample['activeFullNormalModules'])<={'Haven.Desktop','Dulche.Runtime.Tests','Haven.Core.Tests','Haven.Infrastructure.Tests','Haven.Desktop.Tests','HavenOS.Home.Tests','HavenOS.Dev.Tests','HavenOS.Spaces.Tests','Haven.Console.Tests'}
 assert set(sample['activeFullNormalAllocatedBytes'])==set(sample['activeFullNormalModules'])
 for module,allocated in sample['activeFullNormalAllocatedBytes'].items():assert isinstance(allocated,int) and 0<=allocated<=DISK_COMPONENTS['largestActualParentNormalBytes']+DISK_COMPONENTS['normalCopyGrowthAndBlockRoundingAllowance']
 assert sum(sample['activeFullNormalAllocatedBytes'].values())<=sample['totalNormalAllocatedBytes']
 if sample['label'].endswith('-before-normal-stage'):assert sample['workspaceAvailableBytes']>=DISK_TOTAL-DISK_COMPONENTS['retainedActualPluginFixtureNormalBytes']
 else:assert sample['workspaceAvailableBytes']>=DISK_COMPONENTS['finalDiskSpare']
assert allocation['totalBlockAllocationAtCompletion']==samples[-1]['totalNormalAllocatedBytes']
assert samples[-1]['label']=='actual-durable-normal-before-final-receipt'

plans={plan['module']:plan for plan in r['modulePlans']};assert len(plans)==len(r['modulePlans'])
layouts={layout['module']:layout for layout in r['actualDurableNormalLayouts']};assert len(layouts)==len(r['actualDurableNormalLayouts'])
fresh_manifests={};durable_file_items={};durable_layout_readbacks=[]
for manifest_row in manifest_rows:
 module=manifest_row['module'];manifest_pin=manifest_row['manifest']
 manifest=json.loads(gzip.decompress(Path(manifest_pin['path']).read_bytes()));assert module not in fresh_manifests
 assert manifest['actualDurableNormalLayout']==layouts[module]
 assert manifest['actualOriginalOutputDirectory']==str(NORMAL_ROOT/module)
 fresh_manifests[module]={'manifest':manifest,'pin':manifest_pin}
 for item in manifest['files']:
  actual_path=Path(item['original']['path']);assert actual_path==NORMAL_ROOT/module/item['relative'] and actual_path.is_relative_to(NORMAL_ROOT/module)
  assert item['device']==normal_root_stat.st_dev and isinstance(item['inode'],int) and item['inode']>0 and isinstance(item['nlink'],int) and item['nlink']>=1
  if '/' not in item['relative']:
   current_products=[output['target'] for output in r['outputs'] if Path(output['target']['path']).name==item['relative']]
   if current_products:assert len(current_products)==1 and (item['original']['bytes'],item['original']['sha256'])==(current_products[0]['bytes'],current_products[0]['sha256'])
  assert str(actual_path) not in durable_file_items;durable_file_items[str(actual_path)]={'module':module,'item':item,'manifest':manifest_pin}
  if item['restoration']['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE':
   archived=item['restoration']['row'];assert archived['original']==item['original'] and str(actual_path) in archived['originalPaths']
   for key in ('device','inode','nlink'):assert archived[key]==item[key]
for module,layout in layouts.items():
 assert module in plans and layout['normalRoot']==str(NORMAL_ROOT) and layout['targetDirectory']==str(NORMAL_ROOT/module)
 assert module in {'Haven.PluginFixture','Haven.Desktop','Dulche.Runtime.Tests','Haven.Core.Tests','Haven.Infrastructure.Tests','Haven.Desktop.Tests','HavenOS.Home.Tests','HavenOS.Dev.Tests','HavenOS.Spaces.Tests','Haven.Console.Tests'}
 assert layout['originalCompilerProperties']==plans[module]['properties']
 actual=layout['actualEvaluatedProperties'];baseline=layout['originalCompilerProperties'];assert set(actual)==NORMAL_PROPERTY_NAMES
 assert actual['TargetPath']==str(NORMAL_ROOT/module/Path(baseline['TargetPath']).name)
 assert Path(actual['OutDir'])==Path(actual['OutputPath'])==NORMAL_ROOT/module
 for key in ('IntermediateOutputPath','ProjectAssetsFile','TargetRefPath','AssemblyName','TargetFramework'):assert actual[key]==baseline[key],(module,key)
 for key in ('IntermediateOutputPath','TargetRefPath'):assert Path(actual[key]).is_relative_to(ORIGINAL_SCOPE/'artifacts'),(module,key)
 assert Path(actual['ProjectAssetsFile']).is_relative_to(ORIGINAL_SCOPE/'metadata'),(module,'ProjectAssetsFile')
 assert layout['compilerIntermediateAssetsRefIdentityUnchanged'] is True and layout['ownerConditionalOutDirOutputPathOnly'] is True
 flags=layout['exactNormalFlags'];assert isinstance(flags,list) and len(flags)==len(set(flags))
 assert '-p:RootNormalOutputModule='+module in flags and '-p:RootNormalOutputDirectory='+str(NORMAL_ROOT/module)+'/' in flags
 assert len([flag for flag in flags if flag.startswith('-p:RootNormalOutputModule=')])==len([flag for flag in flags if flag.startswith('-p:RootNormalOutputDirectory=')])==1
 assert '-p:BuildProjectReferences=false' in flags and '-p:TargetFramework='+baseline['TargetFramework'] in flags
 directory=layout['directoryObservation'];assert directory['path']==str(NORMAL_ROOT/module) and directory['isDirectPhysicalDirectory'] is True
 directory_stat=direct_directory(directory['path']);assert directory_stat.st_dev==directory['device']==normal_root_stat.st_dev and directory_stat.st_ino==directory['inode']
 assert layout['workspaceFilesystem']==allocation['workspaceFilesystem']
 evaluation_stage=layout['actualEvaluationStage'];assert evaluation_stage in r['stages'] and evaluation_stage['exitCode']==0
 evaluation_command=evaluation_stage['command'];assert evaluation_command[1]=='msbuild' and evaluation_command[2]==plans[module]['project']['path'] and evaluation_command[3:3+len(flags)]==flags
 property_requests=[arg for arg in evaluation_command if arg.startswith('-getProperty:')];assert len(property_requests)==1 and set(property_requests[0].split(':',1)[1].split(','))==NORMAL_PROPERTY_NAMES
 evaluation_pin=layout['actualEvaluationJson'];assert pin(evaluation_pin['path'])==evaluation_pin
 evaluated_body=json.loads(gzip.decompress(Path(evaluation_pin['path']).read_bytes()));stdout_body=json.loads(decode(evaluation_stage['exactWholeStdoutCustody']))
 assert evaluated_body==stdout_body and evaluated_body['Properties']==actual
 normal_name=module+('-normal-runtime-output01' if module=='Haven.Desktop' else '-normal-fixture-runtime-output01' if module=='Haven.PluginFixture' else '-normal-test-runtime-output01')
 normal_stages=[entry for entry in r['stages'] if entry['stage']==normal_name];assert len(normal_stages)<=1
 if normal_stages:
  normal_stage=normal_stages[0];assert normal_stage['command'][1]=='msbuild' and normal_stage['command'][2]==plans[module]['project']['path'] and normal_stage['command'][3:-2]==flags
  assert isinstance(layout['sameFlagsUsedForNormalStage'],bool)
  if not layout['sameFlagsUsedForNormalStage']:assert natural_exit!=0 and module not in fresh_manifests
 else:assert natural_exit!=0 and module not in fresh_manifests and not layout['sameFlagsUsedForNormalStage']
 if module in fresh_manifests:assert layout['sameFlagsUsedForNormalStage'] is True and normal_stages and normal_stages[0]==fresh_manifests[module]['manifest']['actualRuntimeStage'] and normal_stages[0]['exitCode']==0
 else:assert natural_exit!=0
 durable_layout_readbacks.append({'module':module,'targetDirectory':layout['targetDirectory'],'evaluation':evaluation_pin,'actualEvaluationStage':evaluation_stage,'workspaceFilesystem':workspace_readback,'historicalDirectoryObservation':directory,'sameGenuineFlagsAndActualEvaluatedPathVerified':True,'normalStageCaptured':bool(normal_stages),'freshManifest':fresh_manifests.get(module,{}).get('pin')})
normal_property_stage_names={layout['actualEvaluationStage']['stage'] for layout in layouts.values()}|{module+('-normal-runtime-output01' if module=='Haven.Desktop' else '-normal-fixture-runtime-output01' if module=='Haven.PluginFixture' else '-normal-test-runtime-output01') for module in layouts}
for stage in r['stages']:
 if stage['stage'] not in normal_property_stage_names:
  assert not any(arg.startswith(('-p:RootNormalOutputModule=','-p:RootNormalOutputDirectory=')) for arg in stage['command']),('Normal allocation properties escaped ordinary normal stages',stage['stage'])

assert len(r['actualUnqualifiedDurableNormalFileCustody'])==5 and {item['module'] for item in r['actualUnqualifiedDurableNormalFileCustody']}=={'Haven.PluginFixture'}, 'All5 exact partial Fixture files retain failure-only whole custody.'
unqualified_file_items={};unqualified_normal_readbacks=[]
for item in r['actualUnqualifiedDurableNormalFileCustody']:
 assert natural_exit!=0 and item['status']=='UNQUALIFIED_PARTIAL_NORMAL_OUTPUT_WHOLE_CUSTODY_NO_SUCCESS'
 module=item['module'];assert module in layouts and item['sourceNormalLayout']==layouts[module] and module not in fresh_manifests
 relative=Path(item['relative']);assert not relative.is_absolute() and '..' not in relative.parts
 actual_path=Path(item['original']['path']);assert actual_path==NORMAL_ROOT/module/relative and actual_path.is_relative_to(NORMAL_ROOT/module)
 assert str(actual_path) not in durable_file_items and str(actual_path) not in unqualified_file_items
 assert item['device']==normal_root_stat.st_dev and isinstance(item['inode'],int) and item['inode']>0 and isinstance(item['nlink'],int) and item['nlink']>=1
 if item['restoration']['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE':
  archived=item['restoration']['row'];assert archived['original']==item['original'] and str(actual_path) in archived['originalPaths'] and archived in r['outputArchives']
  for key in ('device','inode','nlink'):assert archived[key]==item[key]
 unqualified_file_items[str(actual_path)]={'module':module,'item':item,'successfulNormalClosure':False}
 unqualified_normal_readbacks.append({'module':module,'relative':item['relative'],'status':item['status'],'sourceNormalLayout':item['sourceNormalLayout'],'historicalPhysicalFile':item,'wholeRestorationReadback':runtime_item_readback(item),'successfulNormalClosure':False})
all_durable_file_items={**durable_file_items,**unqualified_file_items}

retired_by_path={row['original']['path']:row for row in r['releasedNewTaskOwnedRuntimeCopies']};assert len(retired_by_path)==len(r['releasedNewTaskOwnedRuntimeCopies'])
for path,row in retired_by_path.items():
 assert path in durable_file_items and row['module']==durable_file_items[path]['module'] and row['module']!='Haven.PluginFixture'
 original=durable_file_items[path]['item'];assert row['actualClosedRuntimeManifest']==durable_file_items[path]['manifest']
 for key in ('original','mode','mtimeNs','restoration','device','inode','nlink'):assert row[key]==original[key]
 assert not Path(path).exists() and row['wholeSourceRoundtripBeforeRelease'] and row['onlyNewTaskOwnedCopyLocalOutput'] and row['physicalAtFinal'] is False
 runtime_item_readback(original)
for path,description in all_durable_file_items.items():
 if path in retired_by_path:continue
 item=description['item'];assert pin(path)==item['original'] and not Path(path).is_symlink() and Path(path).resolve()==Path(path)
 actual_stat=Path(path).stat()
 for key,observed in (('device',actual_stat.st_dev),('inode',actual_stat.st_ino),('nlink',actual_stat.st_nlink)):assert item[key]==observed
 runtime_item_readback(item)
for row in r['wholeReleasedCopyRestorationRevalidation']:
 assert row['original']['path'] in retired_by_path and row['original']==retired_by_path[row['original']['path']]['original'] and row['exactRestorationRevalidated'] and row['physicalAtFinal'] is False
assert {row['original']['path'] for row in r['wholeReleasedCopyRestorationRevalidation']}==set(retired_by_path)

aliases=r['actualGeneratedNormalPrerequisiteAliases'];assert len(aliases)<=4
alias_keys=set();alias_readbacks=[];generated_pins={item['path']:item for item in r['generatedInputPins']};assert len(generated_pins)==len(r['generatedInputPins'])
expected_alias_keys={(module,suffix) for module in ('Haven.Desktop','Haven.PluginFixture') if module in fresh_manifests for suffix in ('.deps.json','.runtimeconfig.json')}
compiler_paths={output[key]['path'] for output in r['outputs'] for key in ('output','target','pdb','reference') if output.get(key)}
for alias_row in aliases:
 module=alias_row['module'];suffix=alias_row['suffix'];key=(module,suffix);assert module in ('Haven.Desktop','Haven.PluginFixture') and suffix in ('.deps.json','.runtimeconfig.json') and key not in alias_keys;alias_keys.add(key)
 if key not in expected_alias_keys:assert natural_exit!=0 and module not in fresh_manifests
 baseline=plans[module]['properties'];name=baseline['AssemblyName']+suffix
 source_pin=alias_row['source'];alias_pin=alias_row['alias'];assert source_pin['path']==str(NORMAL_ROOT/module/name)
 assert source_pin==all_durable_file_items[source_pin['path']]['item']['original']
 assert alias_pin['path']==str(Path(baseline['TargetPath']).parent/name) and alias_pin['path'] in r['requiredLaterNormalInputsExplicitlyRetained'] and alias_pin['path'] not in compiler_paths
 assert (alias_pin['bytes'],alias_pin['sha256'])==(source_pin['bytes'],source_pin['sha256'])
 assert alias_row['wholeSourceBytesEqual'] is True and alias_row['guardedExactOriginalCompilerWrite'] is True and alias_row['generatedInputPinUpdated'] is True and alias_row['physicalAliasAtReceipt'] is True
 assert generated_pins[alias_pin['path']]==alias_pin
 previous=alias_row['previousAliasPin']
 if previous is not None:
  assert previous['path']==alias_pin['path']
  historical_rows=[entry for entry in [*r['outputArchives'],*parent['outputArchives']] if alias_pin['path'] in entry['originalPaths'] and entry['original']['bytes']==previous['bytes'] and entry['original']['sha256']==previous['sha256']]
  assert historical_rows,('Previous generated prerequisite body lacks whole historical custody',previous)
  verify(decode(historical_rows[0]),previous)
 archived=alias_row['aliasArchive'];assert archived['original']==alias_pin and alias_pin['path'] in archived['originalPaths'] and archived in r['outputArchives'];verify(decode(archived),alias_pin)
 assert (alias_pin['path'],alias_pin['bytes'],alias_pin['sha256']) in original_paths
 alias_readbacks.append({'module':module,'suffix':suffix,'durableNormalSource':source_pin,'originalCompilerPrerequisiteAlias':alias_pin,'previousAliasPin':previous,'aliasArchive':archived,'wholeSourceAliasAndGeneratedInputEqualityVerified':True,'physicalAliasAtOriginalReceiptOnly':True})
assert expected_alias_keys<=alias_keys
actual_alias_bytes=sum(row['alias']['bytes'] for row in aliases);assert actual_alias_bytes<=ALIAS_BUDGET and allocation['normalAliasActualBytes']==actual_alias_bytes

normal_files=[];normal_directories=[NORMAL_ROOT]
for path in NORMAL_ROOT.rglob('*'):
 assert path.resolve()==path and not path.is_symlink(),('Durable normal root contains an indirect physical entry',path)
 if path.is_file():normal_files.append(path)
 elif path.is_dir():normal_directories.append(path)
 else:raise AssertionError(('Nonordinary durable normal entry',path))
assert {str(path) for path in normal_files}==set(all_durable_file_items)-set(retired_by_path)
final_sample=samples[-1]
current_normal_logical=sum(path.stat().st_size for path in normal_files)
current_normal_file_allocation=sum(path.stat().st_blocks*512 for path in normal_files)
current_normal_directory_allocation=sum(path.stat().st_blocks*512 for path in normal_directories)
assert final_sample['normalLogicalBytes']==current_normal_logical and final_sample['normalFileAllocatedBytes']==current_normal_file_allocation and final_sample['normalDirectoryAllocatedBytes']==current_normal_directory_allocation and final_sample['normalFileCount']==len(normal_files)
assert final_sample['retainedFixtureAllocatedBytes']==sum(path.stat().st_blocks*512 for path in [*normal_files,*normal_directories] if path.is_relative_to(NORMAL_ROOT/'Haven.PluginFixture'))
assert final_sample['activeFullNormalModules']==sorted({path.relative_to(NORMAL_ROOT).parts[0] for path in normal_files if not path.is_relative_to(NORMAL_ROOT/'Haven.PluginFixture')})
assert final_sample['activeFullNormalAllocatedBytes']=={module:sum(path.stat().st_blocks*512 for path in [*normal_files,*normal_directories] if path.is_relative_to(NORMAL_ROOT/module)) for module in final_sample['activeFullNormalModules']}
durable_available_now=os.statvfs(NORMAL_ROOT).f_bavail*os.statvfs(NORMAL_ROOT).f_frsize;assert durable_available_now>=DISK_COMPONENTS['finalDiskSpare']
final_observation_path=CURRENT_OUTPUT_ROOT/'FINAL-ACTUAL-CAPACITY-OBSERVATION62.json';final_observation=json.loads(final_observation_path.read_bytes())
assert final_observation['receipt']==pin(receipt) and final_observation['declaredDiskEnvelope']==allocation['declaredDiskEnvelope'] and final_observation['diskSpareActuallyObserved'] is True and final_observation['floorActuallyObserved'] is True
after_receipt=final_observation['actualSeparateDiskMeasurement'];assert after_receipt['label']=='actual-durable-normal-after-final-receipt' and after_receipt['atUTCUnixSeconds']>=final_sample['atUTCUnixSeconds']
for key in ('workspaceDevice','filesystemType','normalLogicalBytes','normalFileAllocatedBytes','normalDirectoryAllocatedBytes','totalNormalAllocatedBytes','normalFileCount','activeFullNormalModules','activeFullNormalAllocatedBytes','retainedFixtureAllocatedBytes'):assert after_receipt[key]==final_sample[key]
assert after_receipt['workspaceAvailableBytes']>=DISK_COMPONENTS['finalDiskSpare'] and after_receipt['durableEvidenceAllocatedBytes']<=DISK_COMPONENTS['completeDurableBuildEvidenceEnvelope']
final_observation_blocks=((final_observation_path.stat().st_size+4095)//4096)*4096
assert after_receipt['workspaceAvailableBytes']-final_observation_blocks>=DISK_COMPONENTS['finalDiskSpare'] and after_receipt['durableEvidenceAllocatedBytes']+final_observation_blocks<=DISK_COMPONENTS['completeDurableBuildEvidenceEnvelope']
evidence_roots=[CURRENT_OUTPUT_ROOT,Path('/workspace/astra-source/a4-current-targeted-owning-output-custody65/exact-gzip'),Path('/workspace/astra-source/a4-current-targeted-owning-output-fallback65/exact-gzip')]
current_evidence_allocated=0
for evidence_root in evidence_roots:
 current_evidence_allocated+=direct_directory(evidence_root).st_blocks*512
 for path in evidence_root.rglob('*'):
  assert path.resolve()==path and not path.is_symlink() and (path.is_file() or path.is_dir()),('Durable evidence must retain direct ordinary entries',path)
  current_evidence_allocated+=path.stat().st_blocks*512
assert current_evidence_allocated<=DISK_COMPONENTS['completeDurableBuildEvidenceEnvelope']
assert final_sample['totalNormalAllocatedBytes']+current_evidence_allocated<=DISK_TOTAL-DISK_COMPONENTS['finalDiskSpare']
durable_normal_readback={'normalRoot':str(NORMAL_ROOT),'workspaceFilesystem':workspace_readback,'declaredDiskEnvelope':allocation['declaredDiskEnvelope'],'rawResidentRequirementBytes':allocation['rawResidentRequirementBytes'],'noCacheCredit':True,'capacityMeasurements':samples,'afterFinalReceiptObservation':pin(final_observation_path),'afterFinalReceiptDiskMeasurement':after_receipt,'currentEvidenceAllocatedBytesBeforeClosedReadback':current_evidence_allocated,'currentAvailableBytes':durable_available_now,'finalDiskSpareBytes':DISK_COMPONENTS['finalDiskSpare'],'currentLogicalBytes':current_normal_logical,'currentFileAllocatedBytes':current_normal_file_allocation,'currentDirectoryAllocatedBytes':current_normal_directory_allocation,'totalBlockAllocationAtCompletion':allocation['totalBlockAllocationAtCompletion'],'normalAliasBudgetBytes':ALIAS_BUDGET,'normalAliasActualBytes':actual_alias_bytes,'freshNormalFileCount':len(durable_file_items),'unqualifiedPartialNormalFileCount':len(unqualified_file_items),'physicallyRetiredFileCount':len(retired_by_path),'currentlyRetainedFileCount':len(normal_files),'historicalPhysicalNormalFiles':list(all_durable_file_items.values()),'truthfulDurableNormalPathsDevicesAndRestorationVerified':True}
assert natural_exit==1 and r['status']=='ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED' and MANDATORY_FRESH<=set(r['actualFreshCscOwners'])
assert len(r['outputs'])==22 and closures==manifest_rows==qualified_historical==[] and len(fixture_proofs)==1
assert set(layouts)=={'Haven.PluginFixture'} and len(unqualified_file_items)==5 and len(alias_readbacks)==2
assert fixture_audit_readback['actualQualification']['noSecondCompilerQualified'] and fixture_audit_readback['actualQualification']['actualTaskSkipWitnessObserved']
assert {item['relative'] for item in r['actualUnqualifiedDurableNormalFileCustody']}=={'Haven.PluginFixture','Haven.PluginFixture.dll','Haven.PluginFixture.pdb','Haven.PluginFixture.deps.json','Haven.PluginFixture.runtimeconfig.json'}
active=[]
for entry in Path('/proc').iterdir():
 if not entry.name.isdecimal() or int(entry.name)==os.getpid():continue
 try:argv=[a.decode('utf-8','replace') for a in (entry/'cmdline').read_bytes().split(b'\0') if a]
 except (FileNotFoundError,PermissionError,ProcessLookupError):continue
 if not argv:continue
 base=Path(argv[0]).name
 if (base=='dotnet' and any('astra-tools/dotnet' in a or a.endswith('Haven.dll') for a in argv)) or base in ('csc','llama-server','llama-cli','strata-worker') or (base.startswith('python') and len(argv)>1 and Path(argv[1]).name.startswith(('run-c45-selective-component-compiler','run-c45-targeted-owning-compiler','run-current-root-selective-component-compiler','run-c46-selective-component-compiler'))):active.append({'pid':int(entry.name),'executable':base})
assert not active,active
capacity={str(p):os.statvfs(p).f_bavail*os.statvfs(p).f_frsize for p in (ROOT,Path('/tmp'))};assert sum(capacity.values())>=1048576
value={'status':'ACTUAL_CURRENT65_FAILED_INTERVAL_INDIVIDUAL_COMPONENT_CUSTODY_PASS_NO_NORMAL_OR_RUNTIME_ACCEPTANCE','head':HEAD,'originalNaturalExitCode':natural_exit,'failureCustodyVerifier':pin(__file__),'failureCustodySourceProof':binding['sourceProof'],'fullCompilerIntervalAccepted':False,'runtimeReady':False,'normalClosureAccepted':False,'preservedActualPoststageException':r['actualPrimaryException'],'exactRootInputBinding':pin(binding_path),'originalReceipt':pin(receipt),'currentCompilerReceipt':pin(receipt),'ticket':r['ticket'],'inputsUnchanged':True,'wholeCurrentGitPhysicalSourceRows':len(t['afterRows']),'wholeDurablePhysicalInputReadbacks':len(physical_inputs),'originalPrivateInputPinsRetained':private_inputs,'privateLogicalInputWholeReadbacks':private_readbacks,'archivesWholeDecodedVerified':len(archive_readbacks),'archiveDecodedBytes':decoded_bytes,'archiveReadbacks':archive_readbacks,'actualStageStatusAndWholeStdoutReadbacks':r['stages'],'actualFreshCscOwners':r['actualFreshCscOwners'],'individuallySuccessfulCurrentComponentCompilerReadbacks':component_stage_readbacks,'genuinePriorCompilerReusedCscOwners':r['genuinePriorCompilerReusedCscOwners'],'transparentReusedCompiledSourceHeads':r['transparentReusedCompiledSourceHeads'],'currentWholeSourceResourcePropertyRefAnalyzerUpstreamReadbacks':source_item_readbacks,'actualC43ParentCompilerReceipt':r['actualC43ParentCompilerReceipt'],'actualC43FullClosedReadback':r['actualC43FullClosedReadback'],'actualFailedC44ComponentBasis':EXACT_FAILED_RECEIPT,'actualFailedC44ComponentCustody':EXACT_FAILED_CUSTODY,'actualCurrent59SuccessfulCompilerReceipt':CURRENT59_RECEIPT,'actualCurrent59FullClosedReadback':CURRENT59_CLOSED,'componentBasisOriginalNaturalExitCode':0,'componentBasisIsGenuineFullSuccessfulCurrent59Interval':True,'preservedFailed55NeverClaimedFullSuccessfulInterval':True,'currentReviewedTicketChain':reviewed_chain,'preservedFailed58PreSDKReconstructionReceipt':FAILED58_RECONSTRUCTION_RECEIPT,'failed58PreSDKNoCurrentComponentQualification':True,'actualTwoOriginalC43PrerequisiteRestorationReadbacks':prerequisite_restorations,'originalFailureQualification':basis_closed['failureQualification'],'qualifiedHistoricalC43NormalTestRuntimeClosures':qualified_historical,'actualPluginFixtureStageProofs':fixture_proofs,'actualClosedFixtureExecReadback':fixture_audit_readback,'requiredLaterNormalInputsExplicitlyRetained':r['requiredLaterNormalInputsExplicitlyRetained'],'currentCompiledOutputPins':r['outputs'],'actualNormalRuntimeClosures':closures,'actualNormalRuntimeFileReadbacks':runtime_readbacks,'explicitlyReleasedNewRuntimeOutputReadbacks':released,'generatedOwningLayoutSuccessorReadback':owning_layout_readback,'exactHistoricalC43MetadataDonor':{'raw':historical_props_raw,'archive':historical_props_archive,'sourceProof':historical_props_source_proof,'currentSDKProductNotClaimed':True},'actualDurableNormalAllocationReadback':durable_normal_readback,'actualDurableNormalLayoutReadbacks':durable_layout_readbacks,'actualGeneratedNormalPrerequisiteAliasReadbacks':alias_readbacks,'actualUnqualifiedDurableNormalFileReadbacks':unqualified_normal_readbacks,'wholeHistoricalSourcePropertyRefReadbacks':historical_readbacks,'wholeSuccessfulCurrent59InputReadbacks':current59_input_readbacks,'retiredEphemeralRuntimeFilesNotClaimedPhysicallyPresent':True,'wholePackageContainers':list(containers.values()),'activeSDKNone':True,'activeSDK':None,'modelOrConsoleNone':True,'actualProcessScan':active,'actualAvailableBytes':capacity,'actualRemainingCombinedBytes':sum(capacity.values()),'remainingFloorBytes':1048576,'testsDiscoveryModelsConsoleExecuted':False,'utcUnixSeconds':time.time(),'qualification':'Failure-only custody of the exact naturally1 actual65 interval. All22 individual owning components have genuine successful current20 Csc or separately successful current59 whole-input-qualified2 reuse. The original bare poststage provenance assertion is retained; no global compiler success, normal closure or runtime readiness is claimed. Actual Fixture zero-exec/explicitSkip/observer/fullEOF proof remains qualified independently of its missing normal manifest. All5 partial Fixture files and2 prerequisite aliases remain explicitly unqualified normal output custody. Complete current501 source/Git/reference/resource/property/analyzer/upstream/wholearchive/package/physicaldisk/mount/directory/inode/restoration evidence is reread. No SDK/test/model/Console or producer execution occurs in this verifier.'}
p=PREP/'CURRENT65-FAILED-INTERVAL-INDIVIDUAL-COMPONENT-CUSTODY04.json';assert not p.exists();body=(json.dumps(value,indent=2)+'\n').encode();assert os.statvfs(PREP).f_bavail*os.statvfs(PREP).f_frsize-len(body)>=1048576
closed_body_allocation=((len(body)+4095)//4096)*4096+4096
assert current_evidence_allocated+closed_body_allocation<=DISK_COMPONENTS['completeDurableBuildEvidenceEnvelope']
assert final_sample['totalNormalAllocatedBytes']+current_evidence_allocated+closed_body_allocation<=DISK_TOTAL-DISK_COMPONENTS['finalDiskSpare']
assert os.statvfs(PREP).f_bavail*os.statvfs(PREP).f_frsize-closed_body_allocation>=DISK_COMPONENTS['finalDiskSpare']
with p.open('xb') as f:f.write(body);f.flush();os.fsync(f.fileno())
assert p.read_bytes()==body;p.chmod(0o444);print('CURRENT65_FAILED_INTERVAL_INDIVIDUAL_COMPONENT_CUSTODY',json.dumps(pin(p)),json.dumps({'naturalExitCode':natural_exit,'closures':closures,'archiveBodies':len(archive_readbacks),'decodedBytes':decoded_bytes,'currentSourceRows':len(t['afterRows']),'activeSDKNone':True}),flush=True)
