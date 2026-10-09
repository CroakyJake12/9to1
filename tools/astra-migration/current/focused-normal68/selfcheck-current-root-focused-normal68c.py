"""Root-only bounded exact current no-Csc recovery source admission.
No helper/recipe import, SDK/producer/test/model/Console execution.
Root argv: ticket SHA HEAD actual-failed65-custody SHA NEW-report.
"""
from pathlib import Path
import ast,gzip,hashlib,json,os,pathlib,subprocess,sys
P=Path('/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68');ROOT=Path('/workspace/astra-consolidated')
assert len(sys.argv)==7,'Require Root ticket/SHA/head, actual component custody path/SHA and NEW exclusive report.'
ticket=Path(sys.argv[1]);ticket_sha=sys.argv[2];HEAD=sys.argv[3];custody_path=Path(sys.argv[4]);custody_sha=sys.argv[5];destination=Path(sys.argv[6])
assert destination.parent==P and not destination.exists() and not destination.is_symlink()
def held(p,sha=None,gz=False):
 p=Path(p);b=p.read_bytes();v={'path':str(p),'bytes':len(b),'sha256':hashlib.sha256(b).hexdigest()}
 if sha is not None:assert v['sha256']==sha
 return json.loads(gzip.decompress(b) if gz else b),v
def pin(p):
 p=Path(p);b=p.read_bytes();return {'path':str(p),'bytes':len(b),'sha256':hashlib.sha256(b).hexdigest()}
recipe=Path('/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68/run-current-root-focused-normal68.py');rp=pin(recipe);assert rp=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68/run-current-root-focused-normal68.py', 'bytes': 203536, 'sha256': 'd4c32093bde7737b2bd32b60ecb3924770283f7c413f7133c5cf361e721354f2'}
prior,priorpin=held('/workspace/astra-source/lifecycle-peer-sol61u51/c49-compiler65/CURRENT-ROOT-COMPILER65-BOUNDED-SOURCE-SELFCHECK01.json','909965855553d3df1c56bb303f620af59c57faf44780c19c973e5bc31a0d331c')
assert priorpin['bytes']==51042 and prior['status']=='SOURCE_AND_BOUNDED_CURRENT_ROOT_COMPILER65_SELF_CHECK_PASS_NO_SDK_RUNTIME' and prior['head']==HEAD and not prior['sdkTestsModelsConsoleInvoked']
assert prior['recipe']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/c49-compiler65/run-current-root-selective-component-compiler65.py', 'bytes': 186899, 'sha256': 'd9111dff14afabaaaab43c87fd2e4ab8f86733600b3b1e5097814b738d434536'} and prior['wholeForwardInversePreservesRecipe59'] and prior['existingOwners']==22
for descriptor in prior['recipeProofChain']:assert pin(descriptor['path'])==descriptor
proofpins=[{'path': '/workspace/astra-source/lifecycle-peer-sol61u51/normal-recovery66/CURRENT-NORMAL-RECOVERY66-EXACT-FINITE-SOURCE-PORT01.json', 'bytes': 27593, 'sha256': '1181e719dcedbc5977b2ae79973a3ee2b11c32f4b5ceabb5e3974dbcba065bf7'}, {'path': '/workspace/astra-source/lifecycle-peer-sol61u51/normal-recovery66/CURRENT-NORMAL-RECOVERY66B-EXACT-TARGET-RECONSTRUCTION-SOURCE-PORT02.json', 'bytes': 3296, 'sha256': 'b09c2a25275b955a9aca9ef623e0e1999b57832eb9f4cda98c95c696303fed1a'}, {'path': '/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68/FOCUSED-NORMAL68-EXACT-FINITE-SOURCE-PORT01.json', 'bytes': 20224, 'sha256': '07143b293e31c55dbd91aa3f59c496fe5855ac7819a6e8460272338f4886e668'}];cursor=Path(prior['recipe']['path']).read_text();original=cursor
for descriptor in proofpins:
 proof,actualpin=held(descriptor['path'],descriptor['sha256']);assert actualpin==descriptor and proof['sourceOnly'] and not (proof['sdkOrHelperOrRuntimeMainExecuted'] if descriptor==proofpins[-1] else proof['runtimeVerificationExecuted']) and proof['wholeForwardInverseEqual']
 assert pin(proof['predecessor']['path'])==proof['predecessor'] and Path(proof['predecessor']['path']).read_text()==cursor
 forward=cursor
 for edit in proof['exactEdits']:
  assert forward.count(edit['before'])==edit['count'];forward=forward.replace(edit['before'],edit['after'],edit['count'])
 assert forward==Path(proof['successor']['path']).read_text();inverse=forward
 for edit in reversed(proof['exactEdits']):
  assert inverse.count(edit['after'])==edit['count'];inverse=inverse.replace(edit['after'],edit['before'],edit['count'])
 assert inverse==cursor;cursor=forward
assert cursor==recipe.read_text()
first=json.loads(Path(proofpins[0]['path']).read_bytes())
class Normalize(ast.NodeTransformer):
 def visit_Constant(self,node):
  if isinstance(node.value,str):
   value=node.value
   for a,b in reversed(json.loads(Path(proofpins[-1]['path']).read_bytes())['currentJobPorts']):value=value.replace(b,a)
   for edit in reversed(first['exactEdits'][:9]):value=value.replace(edit['after'],edit['before'])
   return ast.copy_location(ast.Constant(value=value),node)
  return node
oldfunc={n.name:ast.dump(n,include_attributes=False) for n in ast.parse(original).body if isinstance(n,ast.FunctionDef)}
newfunc={n.name:ast.dump(n,include_attributes=False) for n in Normalize().visit(ast.parse(cursor)).body if isinstance(n,ast.FunctionDef)}
changed=sorted(name for name in oldfunc if oldfunc[name]!=newfunc[name]);assert len(oldfunc)==36 and changed==['current_parent_compiler_origin','preserve_actual_desktop_runtime_output','preserve_actual_owning_test_runtime_output','release_closed_runtime_copies']
assert set(newfunc)-set(oldfunc)=={'restore_qualified66_executable_prerequisites'}
t,tp=held(ticket,ticket_sha);assert tp==prior['ticket'] and t['afterHead']==HEAD
assert subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()==HEAD
assert subprocess.check_output(['git','-C',str(ROOT),'symbolic-ref','--short','HEAD'],text=True).strip()=='integration/astra-consolidated-staging-20261005'
assert not subprocess.check_output(['git','-C',str(ROOT),'status','--porcelain=v1','--untracked-files=no'],text=True)
requests=''.join(HEAD+':'+row['target']+'\n' for row in t['afterRows']).encode();batch=subprocess.check_output(['git','-C',str(ROOT),'cat-file','--batch'],input=requests);position=0
for row in t['afterRows']:
 end=batch.index(b'\n',position);parts=batch[position:end].split();assert parts[1]==b'blob';size=int(parts[2]);raw=batch[end+1:end+1+size];position=end+2+size;assert batch[position-1:position]==b'\n'
 assert {k:row['afterPin'][k] for k in ('bytes','sha256')}=={'bytes':size,'sha256':hashlib.sha256(raw).hexdigest()} and (ROOT/row['target']).read_bytes()==raw
 if 'gitBlob' in row['afterPin']:assert row['afterPin']['gitBlob']==parts[0].decode()
assert position==len(batch) and len(t['afterRows'])==501 and t['retiredRows']
for descriptor in prior['exactReadOnlyTicketInputs']:assert pin(descriptor['path'])==descriptor
custody,cp=held(custody_path,custody_sha)
assert custody['status']=='ACTUAL_CURRENT65_FAILED_INTERVAL_INDIVIDUAL_COMPONENT_CUSTODY_PASS_NO_NORMAL_OR_RUNTIME_ACCEPTANCE'
assert custody['head']==HEAD and custody['ticket']==tp and custody['originalNaturalExitCode']==1
assert custody['inputsUnchanged'] and custody['activeSDKNone'] and custody['activeSDK'] is None and custody['modelOrConsoleNone']
assert custody['fullCompilerIntervalAccepted'] is False and custody['runtimeReady'] is False and custody['normalClosureAccepted'] is False and custody['actualNormalRuntimeClosures']==[]
assert custody['failureCustodyVerifier']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/failed65-components66/verify-failed65-individual-components66d.py', 'bytes': 107525, 'sha256': '143b1c96147e4d35634c38b9d5ba88f7986d91645b80b681a18d1dabc99c2c54'} and custody['failureCustodySourceProof']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/failed65-components66/FAILED65-COMPONENT66D-EXACT-ORIGINAL-OBSERVATION-SOURCE-PORT04.json', 'bytes': 2090, 'sha256': 'a9484c01f5d0901d59f3a8b55dc2afb93fea9322ac3a5a54da35be5ce0d3a998'}
for key in ('failureCustodyVerifier','failureCustodySourceProof','exactRootInputBinding'):assert pin(custody[key]['path'])==custody[key]
actual,actualpin=held('/workspace/astra-source/a4-current-targeted-owning-compiler65/output/CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz','c9bba5c0ad33af23580ebf2eed255afa6ca731c6541b99d6a4797346fb77c64f',True)
assert actualpin['bytes']==4844261 and custody['originalReceipt']==custody['currentCompilerReceipt']==actualpin and custody['currentCompiledOutputPins']==actual['outputs']
assert actual['headAtCapture']==actual['headAtCompletion']==HEAD and actual['ticket']==tp and actual['exitCode']==1 and actual['actualPrimaryException']=={'type':'AssertionError','message':''}
assert actual['inputsUnchangedAtCompletion'] and len(actual['outputs'])==len(actual['modulePlans'])==22 and all(stage['exitCode']==0 for stage in actual['stages'])
assert len(actual['actualFreshCscOwners'])==20 and set(actual['genuinePriorCompilerReusedCscOwners'])=={'Haven.Core','Haven.PluginFixture'}
assert not any(actual[key] for key in ('independentTestCompilerFailures','desktopCompilerFailures','desktopRuntimeOutputFailures','owningTestRuntimeOutputFailures'))
logical_scope=Path(actual['namespace']);private={item['path']:item for item in actual['inputs']+actual['generatedInputPins'] if item['path'].startswith(str(logical_scope/'artifacts')+'/') or item['path'].startswith(str(logical_scope/'metadata')+'/')}
assert len(private)==363 and sum(item['bytes'] for item in private.values())==73292512
for output in actual['outputs']:
 for key in ('output','target','pdb','reference'):
  if output.get(key):private[output[key]['path']]=output[key]
 if output.get('pdb'):private[str(Path(output['target']['path']).with_suffix('.pdb'))]={**output['pdb'],'path':str(Path(output['target']['path']).with_suffix('.pdb'))}
index={}
for row in actual['outputArchives']:
 for path in row['originalPaths']:
  if path in private and all(row['original'][key]==private[path][key] for key in ('bytes','sha256')):index[path]=row
final,finalpin=held(actual['finalCustody']['path'],actual['finalCustody']['sha256'],True);assert finalpin==actual['finalCustody'] and final['stage']=='current-targeted65-final'
for row in final['archives']:
 for path in row['originalPaths']:
  if path.startswith(str(logical_scope/'artifacts/obj')+'/'):index[path]=row
assert set(private)<=set(index) and len(private)==439 and sum(item['bytes'] for item in private.values())==145972784
assert len(index)==634 and sum(row['original']['bytes'] for row in index.values())==156162902<=182374123
for plan in actual['modulePlans']:
 for row in plan['actualReferencePins']:
  descriptor=row['input']
  if descriptor['path'].startswith(str(logical_scope/'artifacts')+'/'):assert all(index[descriptor['path']]['original'][key]==descriptor[key] for key in ('bytes','sha256'))
# Pure provenance function replay against actual original rows; never import
# or execute recipe top-level, helper, SDK or normal target.
function=next(node for node in ast.parse(cursor).body if isinstance(node,ast.FunctionDef) and node.name=='current_parent_compiler_origin')
current59,c59pin=held(prior['actualCurrent59SuccessfulCompilerReceipt']['path'],prior['actualCurrent59SuccessfulCompilerReceipt']['sha256'],True)
closed59,closed59pin=held(prior['actualCurrent59FullClosedReadback']['path'],prior['actualCurrent59FullClosedReadback']['sha256'])
assert closed59['originalNaturalExitCode']==0 and closed59['currentCompilerReceipt']==c59pin and closed59['inputsUnchanged'] and closed59['activeSDKNone']
ns={'old_outputs':{row['module']:row for row in actual['outputs']},'pin':pin,'recovery_component_custody_path':custody_path,'recovery_original_receipt_path':Path(actualpin['path']),'recovery_original':actual,'head':HEAD,'component_basis':current59,'component_basis_path':Path(c59pin['path']),'component_closed':closed59,'component_closed_path':Path(closed59pin['path']),'enroll':lambda path:pin(path),'json':json,'gzip':gzip,'pathlib':pathlib}
exec(compile(ast.Module(body=[function],type_ignores=[]),str(recipe)+'::pure-component-provenance','exec'),ns)
provenance=[]
for row in actual['outputs']:
 borrowed={**row,'compiledInCurrentInterval':False,'reusedFromActualIndividualComponentCustody':cp,'completeSourceItemResourceOrderEqual':True,'actualCompilerReferenceAndAnalyzerBytesEqual':True}
 result=ns['current_parent_compiler_origin'](row['module'],borrowed);assert result['originalCompilerIntervalNaturalExitCode']==1 and result['failed65FullIntervalNeverPromoted'];provenance.append({'module':row['module'],'originalComponentCompiledInActual65Interval':result['originalComponentCompiledInActual65Interval'],'actualOriginalNaturalExitCode':result['actualOriginalNaturalExitCode']})
assert len(provenance)==22 and sum(row['originalComponentCompiledInActual65Interval'] for row in provenance)==20
assert "assert not need_fresh and old_items==new_items and old_refs==new_refs" in cursor and "assert compiled_now==[] and len(outputs)==22" in cursor
assert "raw_resident_requirement=8388608+182374123+8388608+2*134217728+67108864" in cursor

# Read every new prerequisite access against the genuine actual66 schema;
# no helper/recipe import, SDK, subprocess child or reconstruction occurs.
parent66,parent66pin=held('/workspace/astra-source/a4-current-targeted-owning-compiler66/output/CURRENT-TARGETED-OWNING-COMPILER66-RECEIPT.json.gz','d7f0d94403dbd4efc96973a0471475ab9b1bc811f90715720e12f0e0d3d53d23',True)
assert parent66pin['bytes']==3255894 and parent66['exitCode']==1 and parent66['headAtCapture']==parent66['headAtCompletion']==HEAD and parent66['ticket']==tp
assert parent66['inputsUnchangedAtCompletion'] and parent66['actualFreshCscOwners']==[] and len(parent66['outputs'])==22 and not parent66['actualOwningTestRuntimeClosureManifests'] and not parent66['releasedNewTaskOwnedRuntimeCopies']
assert parent66['actualFailed65ComponentCompilerReceipt']==actualpin and parent66['actualFailed65IndividualComponentCustody']==cp and all(row['exitCode']==0 for row in parent66['stages'])
assert parent66['actualPrimaryException']['type']=='AssertionError' and 'assert compiled_now and all' in parent66['actualPrimaryException']['actualTraceback']
desktop67,desktop67pin=held('/workspace/astra-source/lifecycle-peer-sol61u51/desktop-component67/CURRENT66-FAILED-INTERVAL-DESKTOP-NORMAL-COMPONENT-CUSTODY67.json','facdfed6e1171ef4c322cd0482e4840942396f57efe99f62fb6bcb0da9549788')
assert desktop67pin['bytes']==254480 and desktop67['originalReceipt']==desktop67['currentCompilerReceipt']==parent66pin and desktop67['head']==HEAD and desktop67['ticket']==tp
assert desktop67['acceptedDesktopNormalComponent'] and desktop67['inputsUnchanged'] and desktop67['original66NaturalExitCode']==1 and all(desktop67[k] is False for k in ('fullCompilerIntervalAccepted','fullNormalCollectionAccepted','runtimeReady','testsAccepted'))
prerequisite_rows=[]
for owner,key in [('Haven.Desktop','actualDesktopRuntimeClosureManifest'),('Haven.PluginFixture','actualPluginFixtureRuntimeClosureManifest')]:
 manifest,manifestpin=held(parent66[key]['path'],parent66[key]['sha256'],True);assert manifestpin==parent66[key] and manifest['head']==HEAD and manifest['ticket']==tp and manifest['actualRuntimeStage']['exitCode']==0 and manifest['noSecondCompiler']
 if owner=='Haven.Desktop':
  assert manifestpin==desktop67['actualDesktopRuntimeClosureManifest'];selected=[row for row in manifest['files'] if row['relative'] in ('Haven.deps.json','Haven.runtimeconfig.json')];assert len(selected)==2
 else:
  assert manifest['fileCount']==5 and manifest['wholeCopiedBytes']==103616 and manifest['wholeProtectedSourceReferenceAndCompilerPins']==manifest['wholeProtectedSourceReferenceAndCompilerPinsAfter']
  audit=manifest['actualFixtureExecAuditQualification'];capture=manifest['actualRuntimeStage']['fixtureExecAuditCapture']
  assert audit['status']=='ACTUAL_FIXTURE_ZERO_COMPILER_EXEC_WITH_EXPLICIT_TASK_SKIP_AND_NATURAL_TRACE_CLOSE' and audit['noSecondCompilerQualified'] and audit['actualTaskSkipWitnessObserved'] and audit['actualExecProof']['compilerExecAttemptCount']==0 and audit['actualExecProof']['otherExecAttemptCount']==0
  assert capture['actualWrapperExitCode']==0 and capture['bothWholeStreamsNaturallyEOF'] and capture['allObservedBytesRetainedWithoutTruncation'] and capture['allWholeStreamsPersisted'] and not capture['captureFaults'] and not capture['forcedTermination']
  assert pin(manifest['actualFixtureExecAuditQualificationReceipt']['path'])==manifest['actualFixtureExecAuditQualificationReceipt'];selected=manifest['files']
 for row in selected:
  assert row['restoration']['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE';donor=row['restoration']['row'];assert pin(donor['archive']['path'])==donor['archive']
  raw=gzip.decompress(Path(donor['archive']['path']).read_bytes());assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
  prerequisite_rows.append({'module':owner,'relative':row['relative'],'bytes':len(raw),'originalManifest':manifestpin,'wholeArchiveReadback':True})
  if owner=='Haven.PluginFixture' and row['relative']=='Haven.PluginFixture':prerequisite_rows.append({'module':owner,'relative':'obj/apphost','bytes':len(raw),'originalManifest':manifestpin,'wholeArchiveReadback':True})
assert len(prerequisite_rows)==8 and sum(row['bytes'] for row in prerequisite_rows)==273682 and 156162902+273682<=182374123
assert "required_current_normal_modules={'Haven.Console.Tests','Haven.Core.Tests','HavenOS.Dev.Tests'}" in cursor
assert "restore_qualified66_executable_prerequisites()" in cursor and " preserve_original_plugin_fixture_runtime(fixture_plan)" not in cursor
assert "assert compiled_now and all" not in cursor

future=[Path('/workspace/astra-source/a4-current-targeted-owning-compiler68/output'),Path('/workspace/astra-source/a4-current-targeted-owning-output-custody68'),Path('/workspace/astra-source/a4-current-targeted-owning-output-fallback68'),Path('/workspace/astra-root-current-normal-output68')]
assert not future[0].parent.exists() and all(not path.exists() and not path.is_symlink() for path in future)
value=dict(prior)
value.update(status='SOURCE_AND_BOUNDED_CURRENT_ROOT_FOCUSED_NORMAL68_SELF_CHECK_PASS_NO_SDK_RUNTIME',recipe=rp,selfcheckSource=pin(__file__),recipeProof=proofpins[-1],recipeProofChain=prior['recipeProofChain']+proofpins,priorActual65SourceSelfcheck=priorpin,actualFailed65ComponentCompilerReceipt=actualpin,actualFailed65IndividualComponentCustody=cp,failed65OriginalNaturalExitCode=1,failed65FullIntervalNeverPromoted=True,noNewCscRequested=True,all22OriginalComponentProvenancePureReadbacks=provenance,wholeFiniteRecoveryForwardInversePreservesQualified65=True,all33OtherFunctionsPreservedNormalizedAst=False,all32Original65FunctionsPreservedNormalizedAst=True,all35Original66FunctionsPreservedNormalizedAst=True,current65WholePrivateReconstructionPaths=634,current65WholePrivateReconstructionBytes=156162902,mandatoryFreshOwners=[],original65MandatoryFreshOwners=prior['mandatoryFreshOwners'],futureRootsAbsent=[str(path) for path in future],actualFixtureNoSecondCompilerEstablished=False,sdkTestsModelsConsoleInvoked=False,focusedNormalModules=['Haven.Console.Tests','Haven.Core.Tests','HavenOS.Dev.Tests'],preservedActualFailed66Receipt=parent66pin,actualQualifiedDesktopComponent67=desktop67pin,exactPrior66PrerequisiteWholeArchiveReadbacks=prerequisite_rows,completeAllEightTestNormalCollectionAccepted=False,qualification='Bounded source-only exact recovery admission. Failed65 remains aggregate1 with no accepted normal outputs. Actual recovery freshly evaluates all22 source/resource/ref/analyzer/upstream/property inputs and must exact-equal the genuine successful component origins before new normal SDK targets. No Csc/test/model/Console or authority acceptance is established by this sourcecheck.')
body=(json.dumps(value,indent=2)+'\n').encode()
with destination.open('xb') as stream:stream.write(body);stream.flush();os.fsync(stream.fileno())
destination.chmod(0o444);print(json.dumps(pin(destination)))
