from pathlib import Path
import hashlib,json,os,subprocess,sys,time
ROOT=Path('/workspace/astra-consolidated');PREP=Path('/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68')
recipe=PREP/'run-current-root-focused-normal68.py'
assert len(sys.argv)==5,'Require exact independent receiving source peer and Root actual sourcecheck path/SHA pairs.'
peer=Path(sys.argv[1]);peer_sha=sys.argv[2];selfcheck=Path(sys.argv[3]);selfcheck_sha=sys.argv[4]
def pin(p):
 p=Path(p);h=hashlib.sha256();n=0
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b);n+=len(b)
 return {'path':str(p),'bytes':n,'sha256':h.hexdigest()}
assert pin(recipe)=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68/run-current-root-focused-normal68.py', 'bytes': 203536, 'sha256': 'd4c32093bde7737b2bd32b60ecb3924770283f7c413f7133c5cf361e721354f2'}
assert selfcheck==PREP/'CURRENT-ROOT-FOCUSED-NORMAL68-BOUNDED-SOURCE-SELFCHECK01.json' and pin(selfcheck)['sha256']==selfcheck_sha
assert selfcheck.parent==PREP and pin(selfcheck)['sha256']==selfcheck_sha and pin(peer)['sha256']==peer_sha
c=json.loads(selfcheck.read_bytes());r=json.loads(peer.read_bytes())
assert c['status']=='SOURCE_AND_BOUNDED_CURRENT_ROOT_FOCUSED_NORMAL68_SELF_CHECK_PASS_NO_SDK_RUNTIME' and c['recipe']==pin(recipe)
ticket=Path(c['ticket']['path']);HEAD=c['head'];assert pin(ticket)==c['ticket'];s=json.loads(ticket.read_bytes())
assert s['afterHead']==HEAD
assert r['status']=='QUALIFIED_INDEPENDENT_SOURCE_PASS_CURRENT_ROOT_FOCUSED_NORMAL68_RUNTIME_UNEXECUTED' and r['recipe']==pin(recipe) and r['selfcheck']==pin(selfcheck) and r['strictLauncher']==pin(Path(__file__))
assert pin(c['selfcheckSource']['path'])==c['selfcheckSource']=={'path': '/workspace/astra-source/lifecycle-peer-sol61u51/focused-normal68/selfcheck-current-root-focused-normal68c.py', 'bytes': 19552, 'sha256': '227f871eb525cc24a440f249673c3d4872fabbb2f1dc2fc81f3e3389c86ed7c2'}
assert r['selfcheckSource']==c['selfcheckSource'] and c['wholeForwardInversePreservesRecipe59'] and c['existingOwners']==22 and c['newAppProjectsNotInventedIntoExisting22']
assert c['wholeFiniteRecoveryForwardInversePreservesQualified65'] and c['all32Original65FunctionsPreservedNormalizedAst'] and c['all35Original66FunctionsPreservedNormalizedAst'] and c['all33OtherFunctionsPreservedNormalizedAst'] is False and c['noNewCscRequested'] and c['mandatoryFreshOwners']==[]
assert c['failed65OriginalNaturalExitCode']==1 and c['failed65FullIntervalNeverPromoted']
assert c['current65WholePrivateReconstructionPaths']==634 and c['current65WholePrivateReconstructionBytes']==156162902<c['originalRawReconstructionAllowanceBytes']==182374123
assert r['actualFailed65ComponentCompilerReceipt']==c['actualFailed65ComponentCompilerReceipt'] and r['actualFailed65IndividualComponentCustody']==c['actualFailed65IndividualComponentCustody']
assert c['focusedNormalModules']==['Haven.Console.Tests','Haven.Core.Tests','HavenOS.Dev.Tests'] and c['completeAllEightTestNormalCollectionAccepted'] is False
assert r['preservedActualFailed66Receipt']==c['preservedActualFailed66Receipt'] and r['actualQualifiedDesktopComponent67']==c['actualQualifiedDesktopComponent67']
assert pin(c['preservedActualFailed66Receipt']['path'])==c['preservedActualFailed66Receipt'] and pin(c['actualQualifiedDesktopComponent67']['path'])==c['actualQualifiedDesktopComponent67']
assert len(c['exactPrior66PrerequisiteWholeArchiveReadbacks'])==8 and sum(row['bytes'] for row in c['exactPrior66PrerequisiteWholeArchiveReadbacks'])==273682
assert 156162902+273682<=182374123
custody_path=Path(c['actualFailed65IndividualComponentCustody']['path']);custody_pin=pin(custody_path);assert custody_pin==c['actualFailed65IndividualComponentCustody']
custody=json.loads(custody_path.read_bytes());assert custody['status']=='ACTUAL_CURRENT65_FAILED_INTERVAL_INDIVIDUAL_COMPONENT_CUSTODY_PASS_NO_NORMAL_OR_RUNTIME_ACCEPTANCE' and custody['head']==HEAD and custody['ticket']==pin(ticket)
assert custody['originalNaturalExitCode']==1 and custody['fullCompilerIntervalAccepted'] is False and custody['runtimeReady'] is False and custody['normalClosureAccepted'] is False and custody['actualNormalRuntimeClosures']==[]
assert custody['inputsUnchanged'] and custody['activeSDKNone'] and custody['activeSDK'] is None and custody['modelOrConsoleNone']
assert custody['originalReceipt']==custody['currentCompilerReceipt']==c['actualFailed65ComponentCompilerReceipt'] and len(custody['currentCompiledOutputPins'])==22 and len(custody['actualFreshCscOwners'])==20 and set(custody['genuinePriorCompilerReusedCscOwners'])=={'Haven.Core','Haven.PluginFixture'}
for key in ('failureCustodyVerifier','failureCustodySourceProof','exactRootInputBinding'):assert pin(custody[key]['path'])==custody[key]
assert pin(c['actualFailed65ComponentCompilerReceipt']['path'])==c['actualFailed65ComponentCompilerReceipt']=={'path':'/workspace/astra-source/a4-current-targeted-owning-compiler65/output/CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz','bytes':4844261,'sha256':'c9bba5c0ad33af23580ebf2eed255afa6ca731c6541b99d6a4797346fb77c64f'}

assert c['requiredRawResidentBudgetBytes']==534695659 and c['declaredDiskEnvelope']['totalBytes']==1218122064
for proof in c['recipeProofChain']:assert pin(proof['path'])==proof
assert r['recipeProofChain']==c['recipeProofChain']
assert r['exactC49TwoTestAssertionsWitness']==c['exactC49TwoTestAssertionsWitness'] and c['exactC49TwoTestAssertionsWitness']['wholeForwardInverseEqual'] and c['exactC49TwoTestAssertionsWitness']['genericMissingCarrierRefusalPreservedForEveryOtherRow']
assert pin(c['exactC49TwoTestAssertionsWitness']['ticket']['path'])==c['exactC49TwoTestAssertionsWitness']['ticket'] and pin(c['exactC49TwoTestAssertionsWitness']['afterSource']['path'])==c['exactC49TwoTestAssertionsWitness']['afterSource']
assert r['preservedActualFailed64Receipt']==c['preservedActualFailed64Receipt'] and pin(c['preservedActualFailed64Receipt']['path'])==c['preservedActualFailed64Receipt'] and c['failed64WholeIntervalOrComponentsNotPromoted']

assert c['exactReviewedHomeContentResourceXmlWitness']['wholeByteInverseEqual'] and c['exactReviewedHomeContentResourceXmlWitness']['allGraphPackageProjectReferencePropertyNodesUnchanged']
assert c['exactReviewedHomeContentResourceXmlWitness']['existing22OwnerAdded'] is False and c['exactReviewedHomeContentResourceXmlWitness']['outside22HomeBuildNotClaimed'] is True
assert r['exactReviewedHomeContentResourceXmlWitness']==c['exactReviewedHomeContentResourceXmlWitness']
graph_admission=c['currentC31MetadataGraphAdmissionSourceOnly'];assert graph_admission['wholeExactOtherXmlBodies']==358 and graph_admission['exactReviewedNonGraphHomeXmlBodies']==1
assert graph_admission['wholeOriginalHomeXmlInverseVerifiedAgain'] and graph_admission['allProjectReferencePackageReferencePropertyGraphUnchanged'] and graph_admission['existing22GeneratedMetadataAndOwningGraphNotReplaced'] and graph_admission['noSDKOrOwningRestoreExecutedBySourcecheck']
assert r['currentC31MetadataGraphAdmissionSourceOnly']==graph_admission
assert pin(graph_admission['receipt']['path'])==graph_admission['receipt']


assert r['executionAuditHelper']==c['executionAuditHelper'] and r['pureSyntheticParserSelfcheck']==c['pureSyntheticParserSelfcheck'] and r['actualRootFdPipeOwnTrueCapability']==c['actualRootFdPipeOwnTrueCapability']
assert pin(c['executionAuditHelper']['path'])==c['executionAuditHelper']
for name in ('pureSyntheticParserSelfcheck','actualRootFdPipeOwnTrueCapability','executionAuditHelperProof'):
 assert pin(c[name]['path'])==c[name]
assert not c['actualFixtureNoSecondCompilerEstablished'] and not c['sdkTestsModelsConsoleInvoked']
prior59=Path(c['qualifiedActual59SourceSelfcheck']['path']);assert pin(prior59)==c['qualifiedActual59SourceSelfcheck']=={'path':str(prior59),'bytes':13737,'sha256':'46fc99a1f7321053e9d558f9f8dadca7e108271a9598e23d9c915dae1a929ac4'}
c59=json.loads(prior59.read_bytes())
assert c59['historicalRecipe50']=={'path':'/workspace/astra-source/a4-c44-targeted-owning-compiler-preparation50/run-c44-targeted-owning-compiler50.py','bytes':124950,'sha256':'b0ce0768c3ca697baa73c7ab49bd6f0edfee1ea5a16f1dbdd346374f4862eb3e'}
actual59=Path(c['actualCurrent59SuccessfulCompilerReceipt']['path']);closed59=Path(c['actualCurrent59FullClosedReadback']['path'])
assert pin(actual59)==c['actualCurrent59SuccessfulCompilerReceipt']=={'path':str(actual59),'bytes':3152001,'sha256':'59d56e8b0b5a922b07d8380c6097c741bb55f401d09b73ff3647bef087f46b70'}
assert pin(closed59)==c['actualCurrent59FullClosedReadback']=={'path':str(closed59),'bytes':11618324,'sha256':'7f218c5318a17c79aa0b013c5b8adc3129ab207abf9a5d0dbe96b3c2a9d2d802'}
assert r['actualCurrent59SuccessfulCompilerReceipt']==pin(actual59) and r['actualCurrent59FullClosedReadback']==pin(closed59)
current59_closed=json.loads(closed59.read_bytes())
assert current59_closed['status']=='ACTUAL_C45_TARGETED59_FULL_CLOSED_CUSTODY_READBACK_PASS' and current59_closed['originalNaturalExitCode']==0 and current59_closed['currentCompilerReceipt']==pin(actual59)
assert current59_closed['inputsUnchanged'] and current59_closed['activeSDKNone'] and current59_closed['activeSDK'] is None and current59_closed['modelOrConsoleNone'] and len(current59_closed['currentCompiledOutputPins'])==22
assert c['current59WholePrivateReconstructionPaths']==654 and c['current59WholePrivateReconstructionBytes']==155491577<c['originalRawReconstructionAllowanceBytes']==182374123
assert c['lateBoundExactRootSourceChain'][0]['afterHead']=='45fc9b3d1de7539ecd00b14bf50a4cf9eb811c9e' and c['lateBoundExactRootSourceChain'][-1]['afterHead']==HEAD
for descriptor in c['exactReadOnlyTicketInputs']:assert pin(descriptor['path'])==descriptor
failed58=Path('/workspace/astra-source/a4-c45-targeted-owning-compiler58/output/C45-TARGETED-OWNING-COMPILER58-RECEIPT.json.gz')
donorcheck=Path('/workspace/astra-source/c45-closed-verifier-sol61u59/ACTUAL-ROOT-FAILED58-TWO-PREREQUISITE-DONORS-CHECK01.json')
assert pin(failed58)==c59['preservedActualFailed58Receipt']=={'path':str(failed58),'bytes':1244701,'sha256':'abfd4211e4d29e405970e95a7537ee54079dd038e9533b3d4726fa6d3746d7a3'}
assert pin(donorcheck)==c59['actualRootTwoPrerequisiteDonorCheck']=={'path':str(donorcheck),'bytes':6884,'sha256':'7c0f9626c39853d09b7f311c7842b7e27f9ef747a3182fb2047253900c16165c'}
assert c59['twoOriginalPrerequisiteBytesRestoredBeforeUnchangedGuard']==91810
assert r['preservedActualFailed58Receipt']==pin(failed58) and r['actualRootTwoPrerequisiteDonorCheck']==pin(donorcheck)

assert subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()==HEAD
assert subprocess.check_output(['git','-C',str(ROOT),'symbolic-ref','--short','HEAD'],text=True).strip()=='integration/astra-consolidated-staging-20261005'
assert not subprocess.check_output(['git','-C',str(ROOT),'diff','--name-only']) and not subprocess.check_output(['git','-C',str(ROOT),'diff','--cached','--name-only'])
rows=s['afterRows'];assert len(rows)==c['completeOwningSourceRows']==c['wholeCurrentGitPhysicalRowsVerified'] and len(rows)>=459
requests=''.join(HEAD+':'+row['target']+'\n' for row in rows).encode();b=subprocess.check_output(['git','-C',str(ROOT),'cat-file','--batch'],input=requests);pos=0
for row in rows:
 end=b.index(b'\n',pos);parts=b[pos:end].split();assert parts[1]==b'blob';size=int(parts[2]);body=b[end+1:end+1+size];pos=end+2+size;assert b[pos-1:pos]==b'\n';actual={'bytes':len(body),'sha256':hashlib.sha256(body).hexdigest()};assert actual=={k:row['afterPin'][k] for k in ('bytes','sha256')} and (ROOT/row['target']).read_bytes()==body
 if 'gitBlob' in row['afterPin']:assert parts[0].decode()==row['afterPin']['gitBlob']
assert pos==len(b)
closed=Path('/workspace/astra-source/a4-c43-targeted-owning-compiler-preparation46/C43-TARGETED46-FULL-CLOSED-CUSTODY-READBACK01.json')
symbols=Path('/workspace/astra-source/a4-c37-exact-runtime-symbol-restoration01/C37-EXACT-SEVEN-RUNTIME-SYMBOLS-RESTORATION-RECEIPT01.json')
assert pin(closed)=={'path':str(closed),'bytes':3244130,'sha256':'988d3f50e99eabd9710ddc1d7161e29a5f2005ddb1f3898a959026d76a79dff9'}
assert pin(symbols)=={'path':str(symbols),'bytes':5878,'sha256':'0aead1b5fc8888024dfce911f37bd37bd69075d989f792f97f96a79713f72730'}
component_closed=Path('/workspace/astra-source/a4-c44-actual55-failure-repair57/C44-ACTUAL55-FAILED-INTERVAL-FULL-CUSTODY-READBACK57.json')
assert pin(component_closed)=={'path':str(component_closed),'bytes':2640235,'sha256':'97a3f25fa0ab0cacd7dd23ee443494d956767d6b850a4e48d6f68965766cfce3'}
component=json.loads(component_closed.read_bytes())
assert component['status']=='ACTUAL_C44_TARGETED55_FAILURE_FULL_CUSTODY_READBACK_PASS_NO_COMPILER_RUNTIME_ACCEPTANCE' and component['originalNaturalExitCode']==1 and component['head']=='2c09519c64906a1fd307dc9bcce558885fd05ec6'
assert component['inputsUnchanged'] and component['activeSDKNone'] and component['failureQualification']['fullCompilerIntervalAccepted'] is False and component['failureQualification']['runtimeReady'] is False
assert len(component['currentCompiledOutputPins'])==21 and not component['actualNormalRuntimeClosures'] and c59['actualFailedC44ComponentCustody']==pin(component_closed) and current59_closed['actualFailedC44ComponentCustody']==pin(component_closed)
fd_capability=json.loads(Path(c['actualRootFdPipeOwnTrueCapability']['path']).read_bytes())
assert not fd_capability['sdkCompilerOrFixtureExecuted'] and not fd_capability['qualifyExecuted'] and fd_capability['originalWrapperIndependentlyJoined'] and fd_capability['capture']['actualWrapperExitCode']==0 and not fd_capability['capture']['captureFaults'] and fd_capability['capture']['forcedTermination'] is None and fd_capability['capture']['bothWholeStreamsNaturallyEOF']
for descriptor in fd_capability['capture']['tools'].values():assert pin(descriptor['path'])==descriptor
cr=json.loads(closed.read_bytes());sr=json.loads(symbols.read_bytes());assert cr['head']=='f7aa74440b43858ed0d5a488576c2d5de566ae46' and cr['inputsUnchanged'] and cr['activeSDKNone'] and cr['activeSDK'] is None
assert sr['head']=='cacd6375bb6dfc99340a8d9bac878a2994a5ea19' and sr['onlySevenSymbolsRestored'] and len(sr['symbols'])==7
for symbol in sr['symbols']:assert pin(symbol['path'])=={k:symbol[k] for k in ('path','bytes','sha256')}
active=[]
for entry in Path('/proc').iterdir():
 if not entry.name.isdecimal() or int(entry.name)==os.getpid():continue
 try:argv=[a.decode('utf-8','replace') for a in (entry/'cmdline').read_bytes().split(b'\0') if a]
 except (FileNotFoundError,PermissionError,ProcessLookupError):continue
 if not argv:continue
 base=Path(argv[0]).name
 sdk=base in ('dotnet','csc','csc.dll') and any('astra-tools/dotnet' in a or 'Roslyn/bincore' in a for a in argv)
 orchestrator=base.startswith('python') and len(argv)>1 and (Path(argv[1]).name.startswith('run-checkpoint') or Path(argv[1]).name.startswith(('run-exact-c37-desktop-runtime-targets','run-c39-targeted-owning-compiler','run-c40-targeted-owning-compiler','run-c41-targeted-owning-compiler','run-c42-targeted-owning-compiler','run-c43-targeted-owning-compiler','run-c44-targeted-owning-compiler','run-c45-selective-component-compiler','run-current-root-selective-component-compiler','run-current-root-normal-closure-recovery','run-current-root-focused-normal')))
 model=base in ('llama-server','llama-cli','strata-worker','strata_worker')
 console=base=='dotnet' and any(a.endswith('Haven.dll') for a in argv)
 if sdk or orchestrator or model or console:active.append({'pid':int(entry.name),'executable':base,'kind':'sdk' if sdk or orchestrator else 'model' if model else 'console'})
assert not active,('Preserve existing healthy work; no overlapping actual runtime targets',active)
future=[Path('/workspace/astra-source/a4-current-targeted-owning-compiler68/output'),Path('/workspace/astra-source/a4-current-targeted-owning-output-custody68'),Path('/workspace/astra-source/a4-current-targeted-owning-output-fallback68'),Path('/workspace/astra-root-current-normal-output68')]
assert not future[0].parent.exists() and not future[0].parent.is_symlink()
assert all(not p.exists() and not p.is_symlink() for p in future)
available={str(p):os.statvfs(p).f_bavail*os.statvfs(p).f_frsize for p in (ROOT,Path('/tmp'))};assert sum(available.values())>=35559951+67108864 and available[str(ROOT)]>=34511375+67108864
normal_root=future[-1];assert normal_root.parent==Path('/workspace') and normal_root.parent.resolve()==normal_root.parent
workspace=normal_root.parent;workspace_stat=workspace.stat();assert workspace_stat.st_dev==ROOT.stat().st_dev
candidate_mounts=[]
for line in Path('/proc/self/mountinfo').read_text().splitlines():
 fields=line.split();mountpoint=Path(fields[4])
 if workspace==mountpoint or workspace.is_relative_to(mountpoint):candidate_mounts.append((len(mountpoint.parts),line,fields))
assert candidate_mounts
_,workspace_mount,workspace_fields=max(candidate_mounts,key=lambda x:x[0]);workspace_fstype=workspace_fields[workspace_fields.index('-')+1]
assert workspace_fstype not in ('tmpfs','ramfs','devtmpfs'),('Normal output requires ordinary non-tmpfs workspace backing',workspace_mount)
disk_components={'largestActualParentNormalBytes':964354689,'normalCopyGrowthAndBlockRoundingAllowance':8388608,'retainedActualPluginFixtureNormalBytes':103616,'completeDurableBuildEvidenceEnvelope':35559951+67108864,'tinyGenuinePrerequisiteAliasAllowance':8388608,'finalDiskSpare':134217728}
required_disk_envelope=sum(disk_components.values());assert required_disk_envelope==1218122064==1151013200+67108864
assert c['declaredDiskEnvelope']=={'components':disk_components,'totalBytes':required_disk_envelope}
actual_workspace_free=os.statvfs(workspace).f_bavail*os.statvfs(workspace).f_frsize
assert actual_workspace_free>=required_disk_envelope,('Fresh whole real workspace disk envelope including native normal outputs, retained fixture, evidence and final spare',actual_workspace_free,required_disk_envelope)
workspace_filesystem={'path':str(workspace),'device':workspace_stat.st_dev,'filesystemType':workspace_fstype,'mountPoint':workspace_fields[4],'mountInfo':workspace_mount}
mount=[x for x in Path('/proc/self/mountinfo').read_text().splitlines() if x.split()[4]=='/dev'];dev=Path('/dev').stat();shm=Path('/dev/shm').stat()
assert mount and ' - tmpfs ' in mount[-1] and mount[-1].split()[5].split(',')[0]=='rw' and dev.st_dev==shm.st_dev and not list(Path('/dev/shm').iterdir())
memory_max=int(Path('/sys/fs/cgroup/memory.max').read_text());memory_current=int(Path('/sys/fs/cgroup/memory.current').read_text());memory_headroom=memory_max-memory_current
required_memory_headroom=8388608+182374123+8388608+2*134217728+67108864
assert required_memory_headroom==534695659==1431939948-964353153+67108864
assert memory_headroom>=required_memory_headroom,('Actual complete resident reconstruction + both original8MiB margins + working128MiB + spare128MiB + additional64MiB audit; only full normal CopyLocal tree is separately disk admitted',memory_headroom,required_memory_headroom)
proof={'status':'ACTUAL_CURRENT_ROOT_FOCUSED_NORMAL68_LAUNCH_ADMISSION_PASS','head':HEAD,'ticket':pin(ticket),'recipe':pin(recipe),'selfcheck':pin(selfcheck),'independentSourcePeer':pin(peer),'originalActualClosedC43Interval':pin(closed),'actualFailedC44ComponentCustody':pin(component_closed),'preservedActualFailed58Receipt':pin(failed58),'actualRootTwoPrerequisiteDonorCheck':pin(donorcheck),'twoOriginalHistoricalPrerequisiteBytes':91810,'wholeReconstructionWithinOriginalAllowance':156436584,'actualCurrent59SuccessfulCompilerReceipt':pin(actual59),'actualCurrent59FullClosedReadback':pin(closed59),'current59BasisFullSuccessKeptDistinctFromHistoricalFailed55':True,'executionAuditHelper':c['executionAuditHelper'],'executionAuditHelperProof':c['executionAuditHelperProof'],'pureSyntheticParserSelfcheck':c['pureSyntheticParserSelfcheck'],'actualRootFdPipeOwnTrueCapability':c['actualRootFdPipeOwnTrueCapability'],'actualFixtureNoSecondCompilerEstablished':False,'actualSevenSymbolsRestoration':pin(symbols),'wholeCurrentGitPhysicalSourceRows':len(rows),'newRows':len(s['newRows']),'trackedSourceClean':True,'actualProcessScan':active,'activeSDKNone':True,'modelOrConsoleNone':True,'actualFreeBytes':available,'combinedAvailableBytes':sum(available.values()),'minimumAdmissionBytes':35559951+67108864,'allNewDurableEvidenceRootsOnWorkspace':True,'remainingFloorBytes':1048576,'actualInvocationPid':os.getpid(),'actualMountNamespace':os.readlink('/proc/self/ns/mnt'),'actualPrivateDevDevice':dev.st_dev,'actualPrivateShmInode':shm.st_ino,'actualPrivateShmEntirelyEmptyBeforeRecipe':True,'actualMountInfo':mount,'futureRootsAbsent':[str(p) for p in future],'utcUnixSeconds':time.time(),'runtimeTargetsOnly':True,'csharpCompilerRequested':False,'normalClosureRecoveryOnly':True,'focusedNormalModules':c['focusedNormalModules'],'completeAllEightTestNormalCollectionAccepted':False,'preservedActualFailed66Receipt':c['preservedActualFailed66Receipt'],'actualQualifiedDesktopComponent67':c['actualQualifiedDesktopComponent67'],'actualFailed65ComponentCompilerReceipt':c['actualFailed65ComponentCompilerReceipt'],'actualFailed65IndividualComponentCustody':custody_pin,'failed65OriginalNaturalExitCode':1,'failed65FullIntervalNeverPromoted':True,'current65WholePrivateReconstructionPaths':634,'current65WholePrivateReconstructionBytes':156162902,'mandatoryFreshCscOwners':c['mandatoryFreshOwners'],'conditionalFreshCscOwners':[],'anySourceResourceRefAnalyzerDifferenceRefusesRecovery':True,'existingOwningCompilerCount':22,'newAppProjectsNotInventedIntoExisting22':True,'observedMemoryMax':memory_max,'observedMemoryCurrent':memory_current,'observedMemoryHeadroomBytes':memory_headroom,'requiredResidentMemoryHeadroomBytes':required_memory_headroom,'originalRawRequirementBytes':1431939948,'onlyMovedNormalTreeBytes':964353153,'noCacheCredit':True,'actualWorkspaceFilesystem':workspace_filesystem,'actualSeparateWorkspaceDiskFreeBytes':actual_workspace_free,'declaredDiskEnvelope':{'components':disk_components,'totalBytes':required_disk_envelope},'historicalRecipe50':c59['historicalRecipe50'],'historicalSelfcheck50':c59['historicalSelfcheck50'],'recipeProofChain':c['recipeProofChain'],'testsModelsConsoleRequested':False}
p=PREP/'CURRENT-ROOT-ACTUAL-FOCUSED-NORMAL68-LAUNCH-ADMISSION01.json';assert not p.exists();data=(json.dumps(proof,indent=2)+'\n').encode();assert os.statvfs(PREP).f_bavail*os.statvfs(PREP).f_frsize-len(data)>=1048576
with p.open('xb') as f:f.write(data);f.flush();os.fsync(f.fileno())
assert p.read_bytes()==data
assert os.statvfs(workspace).f_bavail*os.statvfs(workspace).f_frsize>=required_disk_envelope,('Actual disk admission after durable admission evidence write',required_disk_envelope)
print('CURRENT_ROOT_FOCUSED_NORMAL68_ACTUAL_LAUNCH_ADMISSION',json.dumps(pin(p)),json.dumps(proof),flush=True)
future[0].parent.mkdir(mode=0o700)
assert future[0].parent.resolve()==future[0].parent and not future[0].parent.is_symlink()
os.execv(sys.executable,[sys.executable,str(recipe),str(ticket),pin(ticket)['sha256'],HEAD,str(custody_path),custody_pin['sha256']])
