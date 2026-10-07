import importlib.util,base64,gzip,hashlib,json,os,pathlib,re,subprocess,sys,time,xml.etree.ElementTree as ET
repo=pathlib.Path('/workspace/astra-consolidated')
scope=pathlib.Path('/dev/shm/astra-framework-owning-metadata-20261006-03')
out=pathlib.Path('/workspace/astra-source/a4-current-targeted-owning-compiler65/output')
sdk=pathlib.Path('/workspace/astra-tools/dotnet-10.0.401/dotnet')
cache=pathlib.Path('/workspace/astra-tools/nuget')
assert len(sys.argv)==4, 'Require Root exact ticket, ticket SHA and frozen source head before any namespace/compiler launch.'
ticket=pathlib.Path(sys.argv[1]);expected_ticket_sha=sys.argv[2];expected_head=sys.argv[3]
parent_ticket=pathlib.Path('/tmp/astra-canonical-source-checkpoint12-20261006.json')
metadata_receipt=pathlib.Path('/tmp/astra-framework-owning-metadata-20261006-03/PHYSICAL-OWNING-METADATA-RECEIPT03.json.gz')
metadata_custody=pathlib.Path('/tmp/astra-framework-owning-metadata-20261006-03/final03-metadata-custody03.json.gz')
# Expected corrected head and full receipt SHA are supplied by Root's exact admission ticket.
floor=1048576
capacity_measurements=[]
new_durable_allowance=34511375+67108864 # Conserved measured prior durable envelope plus8MiB additional bounded new owning Console metadata/runtime custody allowance; no admission reduction.
minimum_admission=new_durable_allowance+floor

# Only normal CopyLocal trees move to ordinary disk. All compiler/source/ref
# logical paths, original raw reconstruction and both resident margins remain.
normal_root=pathlib.Path('/workspace/astra-root-current-normal-output65')
disk_envelope_components={'largestActualParentNormalBytes':964354689,'normalCopyGrowthAndBlockRoundingAllowance':8388608,'retainedActualPluginFixtureNormalBytes':103616,'completeDurableBuildEvidenceEnvelope':35559951+67108864,'tinyGenuinePrerequisiteAliasAllowance':8388608,'finalDiskSpare':134217728}
disk_envelope={'components':disk_envelope_components,'totalBytes':sum(disk_envelope_components.values())}
assert disk_envelope['totalBytes']==1218122064==1151013200+67108864
original_raw_requirement=1431939948;only_moved_normal_tree=964353153
# The bounded fixture execution audit adds64MiB resident reserve without
# reducing either original8MiB term, reconstruction, working or spare term.
raw_resident_requirement=8388608+182374123+8388608+2*134217728+67108864
assert raw_resident_requirement==534695659==original_raw_requirement-only_moved_normal_tree+67108864
normal_alias_budget=8388608;normal_alias_rows=[];durable_normal_layouts=[];disk_capacity_measurements=[];unqualified_durable_normal_files=[]

started=time.time();code=0;stages=[];plans=[];outputs=[];archives=[];evaluations=[];known={};generated={};source_membership={};archive_by_sha={};restored=[];missing=[];package_pins=[]
# The tool default sandbox supplies a NEW private /dev tmpfs. Do not expose host /dev/shm.
mount_info=pathlib.Path('/proc/self/mountinfo').read_text().splitlines()
visible_dev=[row for row in mount_info if row.split()[4]=='/dev']
assert visible_dev and visible_dev[-1].split()[5].split(',')[0]=='rw' and ' - tmpfs ' in visible_dev[-1]
private_dev=pathlib.Path('/dev').stat();private_shm=scope.parent.stat()
assert private_dev.st_dev==private_shm.st_dev and list(scope.parent.iterdir())==[],('Require exclusive empty tool-private /dev tmpfs',mount_info)
assert not scope.exists() and not scope.is_symlink(),scope
namespace_suffix=str(time.time_ns())+'-'+str(os.getpid())
namespace_guard=scope.parent/('astra-current-'+expected_head[:8]+'-targeted65-'+namespace_suffix)
assert not namespace_guard.exists();namespace_guard.mkdir(mode=0o700)
scope.mkdir();out.mkdir(exist_ok=False);shadow=scope/'source-root';shadow.mkdir();(scope/'task-temp').mkdir();(scope/'cli-home').mkdir()
namespace_admission={'status':'ACTUAL_EXCLUSIVE_TOOL_DEFAULT_PRIVATE_TMPFS_ADMISSION','head':expected_head,'actualMountNamespace':os.readlink('/proc/self/ns/mnt'),'mountInfo':visible_dev,'privateDevice':private_shm.st_dev,'privateRootInode':private_shm.st_ino,'uniqueGuardPath':str(namespace_guard),'uniqueGuardInode':namespace_guard.stat().st_ino,'logicalScope':str(scope),'actualScopeDevice':scope.stat().st_dev,'actualScopeInode':scope.stat().st_ino,'scopeIsDirectPhysicalDirectory':scope.resolve()==scope and not scope.is_symlink(),'beforeCreationEntirePrivateShmEmpty':True,'hostSharedScopeExposedOrModified':False,'qualification':'This direct physical scope is new within the tool-default private /dev tmpfs, identified by actual mount namespace/device/inode and exclusive current-C44-head-suffix guard. Logical SDK/project/assets/resource paths remain exact. No global mount or shared prior scope changes.'}
assert namespace_admission['scopeIsDirectPhysicalDirectory']
namespace_receipt=out/'PHYSICAL-PRIVATE-NAMESPACE-ADMISSION55.json'
namespace_data=(json.dumps(namespace_admission,indent=2)+'\n').encode()
assert sum(os.statvfs(p).f_bavail*os.statvfs(p).f_frsize for p in (repo,pathlib.Path('/tmp')))-len(namespace_data)>=floor
with namespace_receipt.open('xb') as f:f.write(namespace_data);f.flush();os.fsync(f.fileno())
assert namespace_receipt.read_bytes()==namespace_data
print('CURRENT_CHECKPOINT_TARGETED65_PRIVATE_NAMESPACE',json.dumps(namespace_admission),flush=True)
def pin(path):
 p=pathlib.Path(path);h=hashlib.sha256();n=0
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b);n+=len(b)
 return {'path':str(p),'bytes':n,'sha256':h.hexdigest()}
def free(p):
 s=os.statvfs(p);return s.f_bavail*s.f_frsize
def combined_free():return free(repo)+free(pathlib.Path('/tmp'))
def capacity_sample(label):
 return {'label':label,'atUTCUnixSeconds':time.time(),'workspaceAvailableBytes':free(repo),'tmpAvailableBytes':free(pathlib.Path('/tmp')),'combinedAvailableBytes':combined_free()}

def workspace_filesystem(path):
 p=pathlib.Path(path);assert p.is_absolute() and p.resolve()==p and not p.is_symlink()
 st=p.stat();candidates=[]
 for row in pathlib.Path('/proc/self/mountinfo').read_text().splitlines():
  fields=row.split();mount=pathlib.Path(fields[4])
  if p==mount or p.is_relative_to(mount):candidates.append((len(mount.parts),row,fields))
 assert candidates
 _,row,fields=max(candidates,key=lambda x:x[0]);fs_type=fields[fields.index('-')+1]
 assert fs_type not in ('tmpfs','ramfs','devtmpfs'),('Normal output requires real non-tmpfs workspace filesystem',p,row)
 assert st.st_dev==repo.stat().st_dev==pathlib.Path('/workspace').stat().st_dev
 return {'path':str(pathlib.Path('/workspace')),'device':st.st_dev,'filesystemType':fs_type,'mountPoint':fields[4],'mountInfo':row}

def allocation_tree_observation(root):
 logical=allocated=directories=count=0
 if root.exists():
  assert root.is_dir() and root.resolve()==root and not root.is_symlink()
  directories=root.stat().st_blocks*512
  for path in root.rglob('*'):
   assert not path.is_symlink() and path.resolve()==path,('Allocation inventory requires direct physical entries',path)
   st=path.stat()
   if path.is_file():logical+=st.st_size;allocated+=st.st_blocks*512;count+=1
   elif path.is_dir():directories+=st.st_blocks*512
   else:raise AssertionError(('Unexpected allocation entry',path))
 return {'logicalBytes':logical,'fileAllocatedBytes':allocated,'directoryAllocatedBytes':directories,'totalAllocatedBytes':allocated+directories,'fileCount':count}

def disk_capacity_sample(label):
 fs=workspace_filesystem(normal_root if normal_root.exists() else normal_root.parent)
 normal=allocation_tree_observation(normal_root);fixture=allocation_tree_observation(normal_root/'Haven.PluginFixture')
 active=[]
 if normal_root.exists():
  for d in normal_root.iterdir():
   assert d.is_dir() and d.resolve()==d and not d.is_symlink()
   if d.name!='Haven.PluginFixture' and allocation_tree_observation(d)['fileCount']:active.append(d.name)
 evidence=[allocation_tree_observation(out)]
 for name in ('archive_root','archive_fallback'):
  if name in globals():evidence.append(allocation_tree_observation(globals()[name]))
 sample={'label':label,'atUTCUnixSeconds':time.time(),'workspaceAvailableBytes':free(repo),'workspaceDevice':fs['device'],'filesystemType':fs['filesystemType'],'normalLogicalBytes':normal['logicalBytes'],'normalFileAllocatedBytes':normal['fileAllocatedBytes'],'normalDirectoryAllocatedBytes':normal['directoryAllocatedBytes'],'totalNormalAllocatedBytes':normal['totalAllocatedBytes'],'normalFileCount':normal['fileCount'],'activeFullNormalAllocatedBytes':{module:allocation_tree_observation(normal_root/module)['totalAllocatedBytes'] for module in active},'activeFullNormalModules':sorted(active),'retainedFixtureAllocatedBytes':fixture['totalAllocatedBytes'],'durableEvidenceLogicalBytes':sum(x['logicalBytes'] for x in evidence),'durableEvidenceAllocatedBytes':sum(x['totalAllocatedBytes'] for x in evidence)}
 disk_capacity_measurements.append(sample);return sample

def validate_disk_sample(sample,additional_evidence_bytes=0):
 growth=disk_envelope_components['normalCopyGrowthAndBlockRoundingAllowance'];largest=disk_envelope_components['largestActualParentNormalBytes'];fixture=disk_envelope_components['retainedActualPluginFixtureNormalBytes'];spare=disk_envelope_components['finalDiskSpare']
 assert sample['workspaceAvailableBytes']-additional_evidence_bytes>=spare,('Actual whole disk final spare',sample,additional_evidence_bytes)
 assert sample['durableEvidenceAllocatedBytes']+additional_evidence_bytes<=disk_envelope_components['completeDurableBuildEvidenceEnvelope'],('Actual whole evidence allocation exceeds declared envelope',sample,additional_evidence_bytes)
 assert sample['totalNormalAllocatedBytes']<=largest+growth+fixture,('Actual whole normal tree/fixture blocks exceed declared normal envelope',sample)
 assert sample['totalNormalAllocatedBytes']+sample['durableEvidenceAllocatedBytes']+additional_evidence_bytes<=disk_envelope['totalBytes']-spare,('Actual whole normal/evidence peak exceeds declared envelope',sample,additional_evidence_bytes)
 assert len(sample['activeFullNormalModules'])<=1,('Only one naturally settled full normal module may occupy disk',sample)
 for module in sample['activeFullNormalModules']:
  actual=allocation_tree_observation(normal_root/module)
  assert sample['activeFullNormalAllocatedBytes'][module]==actual['totalAllocatedBytes']
  assert actual['totalAllocatedBytes']<=largest+growth,('Actual active full normal module exceeds declared maximum plus block growth',module,actual)
 return sample

def admit_durable_normal_allocation():
 assert normal_root.parent==pathlib.Path('/workspace') and not normal_root.exists() and not normal_root.is_symlink()
 before=disk_capacity_sample('actual-durable-normal-admission')
 assert before['workspaceAvailableBytes']>=disk_envelope['totalBytes'],('Fresh whole real disk normal allocation envelope',before,disk_envelope)
 normal_root.mkdir(mode=0o700)
 assert normal_root.resolve()==normal_root and normal_root.stat().st_dev==before['workspaceDevice']
 return workspace_filesystem(normal_root)

def prepare_durable_normal_layout(module,project,flags,plan):
 assert plan['module']==module and module in {'Haven.Desktop','Haven.PluginFixture'}|test_module_names
 assert not any(x.startswith(('-p:RootNormalOutputModule=','-p:RootNormalOutputDirectory=','-p:OutDir=','-p:OutputPath=')) for x in flags)
 assert normal_root.exists() and normal_root.resolve()==normal_root
 before=disk_capacity_sample(module+'-before-normal-stage')
 assert not before['activeFullNormalModules'],('Previous full normal tree must be closed and retired',before)
 required=disk_envelope['totalBytes']-disk_envelope_components['retainedActualPluginFixtureNormalBytes']
 assert before['workspaceAvailableBytes']>=required,('Fresh conservative normal tree/evidence/spare disk headroom',before,required)
 d=normal_root/module;assert d.parent==normal_root and not d.exists() and not d.is_symlink();d.mkdir(mode=0o700)
 normal_flags=[*flags,'-p:RootNormalOutputModule='+module,'-p:RootNormalOutputDirectory='+str(d)+'/']
 names='TargetPath,OutDir,OutputPath,IntermediateOutputPath,ProjectAssetsFile,TargetRefPath,AssemblyName,TargetFramework'
 label=module+'-normal-layout-evaluation01'
 status=stage(label,[str(sdk),'msbuild',str(shadow/project),*normal_flags,'-getProperty:'+names,'-v:quiet'])
 assert status==0,('Actual normal output layout evaluation failed',module,status)
 obj=json.loads(stage_log(label).read_text());props_actual=obj['Properties'];assert set(props_actual)==set(names.split(','))
 original=plan['properties']
 for k in ('IntermediateOutputPath','ProjectAssetsFile','TargetRefPath','AssemblyName','TargetFramework'):assert props_actual[k]==original[k],('Normal output altered compiler/assets/ref identity',module,k,props_actual[k],original[k])
 target=pathlib.Path(props_actual['TargetPath'])
 assert target==d/pathlib.Path(original['TargetPath']).name and target.parent==d and target.resolve(strict=False)==target
 assert pathlib.Path(props_actual['OutDir'])==pathlib.Path(props_actual['OutputPath'])==d and d.resolve()==d
 fs=workspace_filesystem(d);st=d.stat();actual_json=save(label+'.json.gz',obj);enroll(actual_json['path'])
 witness={'module':module,'normalRoot':str(normal_root),'targetDirectory':str(d),'actualEvaluatedProperties':props_actual,'originalCompilerProperties':original.copy(),'exactNormalFlags':normal_flags,'actualEvaluationStage':stages[-1],'actualEvaluationJson':actual_json,'directoryObservation':{'path':str(d),'device':st.st_dev,'inode':st.st_ino,'isDirectPhysicalDirectory':True},'workspaceFilesystem':fs,'sameFlagsUsedForNormalStage':False,'compilerIntermediateAssetsRefIdentityUnchanged':True,'ownerConditionalOutDirOutputPathOnly':True}
 durable_normal_layouts.append(witness)
 return normal_flags,d,witness

def record_durable_normal_stage(module):
 witness=next(x for x in durable_normal_layouts if x['module']==module)
 command=stages[-1]['command'];flags=witness['exactNormalFlags']
 assert command[3:3+len(flags)]==flags and stages[-1]['stage'].startswith(module+'-normal-') and '-getProperty:' not in ' '.join(command)
 witness['sameFlagsUsedForNormalStage']=True
 sample=validate_disk_sample(disk_capacity_sample(module+'-after-normal-stage'))
 assert sample['workspaceAvailableBytes']>=disk_envelope_components['finalDiskSpare'],('Actual disk spare after genuine normal stage',sample)
 assert sample['durableEvidenceAllocatedBytes']<=disk_envelope_components['completeDurableBuildEvidenceEnvelope'],('Actual durable evidence exceeds admitted envelope',sample)
 assert sample['retainedFixtureAllocatedBytes']<=disk_envelope_components['retainedActualPluginFixtureNormalBytes']+disk_envelope_components['normalCopyGrowthAndBlockRoundingAllowance']
 return sample

def preserve_genuine_normal_prerequisite_aliases(module,target_dir,plan):
 assert module in ('Haven.Desktop','Haven.PluginFixture')
 original_dir=pathlib.Path(plan['properties']['TargetPath']).parent
 assert original_dir.is_relative_to(scope/'artifacts/bin') and original_dir.resolve()==original_dir
 assembly_name=plan['properties']['AssemblyName'];protected={o[k]['path'] for o in outputs for k in ('output','target','pdb','reference') if o.get(k)}
 for suffix in ('.deps.json','.runtimeconfig.json'):
  source=target_dir/(assembly_name+suffix);source_pin=pin(source);alias=original_dir/source.name
  assert source.resolve()==source and not source.is_symlink() and alias.resolve()==alias and not alias.is_symlink()
  assert str(alias) in required_later_normal_inputs and str(alias) not in protected
  assert sum(x['alias']['bytes'] for x in normal_alias_rows)+source_pin['bytes']<=normal_alias_budget
  previous=pin(alias) if alias.exists() else None
  raw=source.read_bytes();temporary=alias.with_name(alias.name+'.normal55-new-write');assert not temporary.exists() and not temporary.is_symlink()
  # A tmpfs alias preserves only exact SDK-generated prerequisite JSON bytes;
  # no compiled assembly, reference, PDB, apphost or source is rewritten.
  with temporary.open('xb') as f:f.write(raw);f.flush();os.fsync(f.fileno())
  assert temporary.read_bytes()==raw;os.chmod(temporary,source.stat().st_mode&0o7777);os.utime(temporary,ns=(source.stat().st_atime_ns,source.stat().st_mtime_ns));os.replace(temporary,alias)
  alias_pin=pin(alias);assert {k:source_pin[k] for k in ('bytes','sha256')}=={k:alias_pin[k] for k in ('bytes','sha256')}
  known.pop(str(alias),None);enroll(alias,False);row=custody_group([alias],module+'-genuine-normal-prerequisite-alias55')
  assert read_archive(row)==raw and pin(source)==source_pin
  normal_alias_rows.append({'module':module,'suffix':suffix,'source':source_pin,'alias':alias_pin,'previousAliasPin':previous,'aliasArchive':row,'wholeSourceBytesEqual':True,'guardedExactOriginalCompilerWrite':True,'generatedInputPinUpdated':True,'physicalAliasAtReceipt':True})

def guarded_write(path,data,replace_existing=False):
 p=pathlib.Path(path);rounded=((len(data)+4095)//4096)*4096
 before=capacity_sample('before:'+str(p));capacity_measurements.append(before)
 assert free(repo)-rounded>=disk_envelope_components['finalDiskSpare'],('Conserved actual workspace disk spare for durable evidence',p,rounded)
 assert before['combinedAvailableBytes']-rounded>=floor and free(p.parent)>=rounded,('Actual durable capacity floor',str(p),len(data),before)
 temporary=p.with_name(p.name+'.new-write') if replace_existing else p
 with temporary.open('xb') as f:f.write(data);f.flush();os.fsync(f.fileno())
 assert temporary.read_bytes()==data
 if replace_existing:os.replace(temporary,p)
 after=capacity_sample('after:'+str(p));capacity_measurements.append(after)
 assert after['combinedAvailableBytes']>=floor,('Actual post-write capacity floor',str(p),after)
 return pin(p)
def save(name,obj):
 raw=json.dumps(obj,separators=(',',':'),ensure_ascii=False).encode();data=gzip.compress(raw,9,mtime=0)
 new_root=pathlib.Path('/workspace/astra-source/a4-current-targeted-owning-output-custody65/exact-gzip');new_root.mkdir(parents=True,exist_ok=True)
 destination=max([out,archive_root if 'archive_root' in globals() else new_root],key=free)
 assert combined_free()-len(data)>=floor and free(destination)>=len(data),('Durable combined output capacity',name,len(data),free(out))
 p=destination/name
 guarded_write(p,data)
 assert gzip.decompress(p.read_bytes())==raw
 return pin(p)
def enroll(p,immutable=True):
 p=pathlib.Path(p)
 if p.is_file():
  actual=pin(p)
  if immutable:known.setdefault(str(p),actual)
  else:generated[str(p)]=actual
  if p.is_symlink() or p.resolve()!=p:
   q=p.resolve()
   if q.is_file() and q.is_relative_to(repo):known.setdefault(str(q),pin(q))
  return actual
# This observer is source-pinned before any SDK stage. Importing defines pure
# source APIs only; Root alone later invokes the owned-child launch/capture.
sys.dont_write_bytecode=True
fixture_audit_path=pathlib.Path('/workspace/astra-source/a4-c45-fixture-audit58-source-helper/fixture-exec-audit58c.py')
assert pin(fixture_audit_path)=={'path':str(fixture_audit_path),'bytes':27135,'sha256':'8c1dc104eb0a56f844458d6b78f5a85af5ec557093503c3975d7e02641dea039'}
fixture_spec=importlib.util.spec_from_file_location('exact_fixture_exec_audit58c',fixture_audit_path)
fixture_audit=importlib.util.module_from_spec(fixture_spec);fixture_spec.loader.exec_module(fixture_audit)
fixture_audit_tools=fixture_audit.verify_tools()
assert fixture_audit.DIAGNOSTIC_LIMIT==8388608 and fixture_audit.AUDIT_LIMIT==1048576
assert fixture_audit.ADDITIONAL_RESIDENT_ALLOWANCE==fixture_audit.ADDITIONAL_DURABLE_EVIDENCE_ALLOWANCE==67108864
for descriptor in fixture_audit_tools.values():assert pin(descriptor['path'])==descriptor;enroll(descriptor['path'])
fixture_exec_audit_observer={'tools':fixture_audit_tools,'diagnosticCapBytes':8388608,'auditCapBytes':1048576,'additionalResidentAllowanceBytes':67108864,'additionalDurableEvidenceAllowanceBytes':67108864,'maxKernelPipeBytesEach':1048576,'readChunkBytes':65536,'sourceOnlyDoesNotCertifyActualExecOrSkip':True}
exact_selective_compiler_source=enroll(pathlib.Path(__file__))
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip();assert head==expected_head
assert pin(ticket)['sha256']==expected_ticket_sha
assert pin(parent_ticket)['sha256']=='f1dc088469784fb959ed7d4a3d244bafc71938ea1c959a1a2360de7c50c55df9'
parent_selection=json.loads(parent_ticket.read_text());supplement=json.loads(ticket.read_text());assert supplement['afterHead']==head
assert supplement['parentHead']==subprocess.check_output(['git','rev-parse',head+'^'],cwd=repo,text=True).strip()
assert subprocess.run(['git','merge-base','--is-ancestor','060746ec558c0da82a6653bb531d2794638c1db5',head],cwd=repo,check=False).returncode==0
parent_successful_receipt=pathlib.Path('/workspace/astra-source/a4-framework-checkpoint23-resource02-owning-output-custody01/exact-gzip/CHECKPOINT23-RESOURCE02-OWNING-COMPILER-RECEIPT01.json.gz')
assert pin(parent_successful_receipt)['sha256']=='0ec200b5ea7eed53c70f9ec3a75039ac2d88c64a1590770abd3c941bd7f5b0e5'
parent_successful=json.loads(gzip.decompress(parent_successful_receipt.read_bytes()));enroll(parent_successful_receipt)
reader_path=pathlib.Path('/tmp/astra-a4-strata-request-consumer05-20261006/read-c24-receipt-exact-custody.py')
assert pin(reader_path)['sha256']=='f9be612feadad483c4c3f77e2726ad8595157899e737150955368633a3ce3fee'
reader_spec=importlib.util.spec_from_file_location('exact_c24_reader',reader_path);reader=importlib.util.module_from_spec(reader_spec);reader_spec.loader.exec_module(reader)
c24,c24_transport=reader.exact_c24_receipt();assert c24['exitCode']==0 and c24['inputsUnchangedAtCompletion'] and len(c24['outputs'])==20
enroll(reader_path);enroll(reader.ZIP)
custody_helper=pathlib.Path('/workspace/astra-source/a4-resource-subrange-output-custody01-20261006/exact-resource-subrange-custody02.py')
closed_layer_registry=pathlib.Path('/workspace/astra-source/a4-resource-subrange-output-custody01-20261006/CLOSED-DLL-AND-RESOURCE-LAYER-REGISTRY02.json')
assert pin(closed_layer_registry)['sha256']=='d7b117d1d919bcdd9dbc95cefd9e241f02ab4194a3b2bbf7c4793c32ccc2bfa4'
assert pin(custody_helper)['sha256']=='0d650d3f4be643adbaa60e1ee748389be7c869deb67feb959700f3c80fd732b2'
custody_spec=importlib.util.spec_from_file_location('exact_resource_custody',custody_helper);custody_library=importlib.util.module_from_spec(custody_spec);custody_spec.loader.exec_module(custody_library)
enroll(custody_helper);enroll(closed_layer_registry)
closed_layers=json.loads(closed_layer_registry.read_bytes());archive_transports={};archive_encodings={}
immutable_git_blob_inputs={}
def enroll_manifest_inputs(m):
 provenance=custody_library.manifest_input_provenance(m,closed_layers)
 for actual in provenance['actualPhysicalInputs']:
  assert pin(actual['path'])==actual;enroll(actual['path'])
 for original in provenance['immutableGitBlobInputs']:
  segment={'gitBlobObjectId':original['objectId'],'bytes':original['bytes'],'sha256':original['sha256']}
  custody_library.original_git_blob(original['repository'],segment)
  immutable_git_blob_inputs[original['objectId']]=original
 return provenance
for descriptor in closed_layers.values():
 assert pin(descriptor['path'])==descriptor;enroll(descriptor['path']);m=custody_library.read_descriptor(descriptor['path'])
 enroll_manifest_inputs(m)
def read_archive(row):
 if row.get('encoding')==custody_library.SCHEMA:
  assert pin(row['archive']['path'])==row['archive'];enroll(row['archive']['path']);m=custody_library.read_descriptor(row['archive']['path'])
  provenance=enroll_manifest_inputs(m)
  raw=custody_library.decode_archive(row,closed_layers)
  archive_transports[row['archive']['path']]={'kind':custody_library.SCHEMA,'descriptor':row['archive'],**provenance}
 else:
  encoded,transport=custody_library.read_original_gzip(row['archive']['path'],row['archive'],closed_layers)
  archive_transports[row['archive']['path']]=transport
  if transport['kind']=='PHYSICAL_EXACT_ORIGINAL_GZIP':enroll(row['archive']['path'])
  else:
   enroll(transport['descriptor']['path'])
   enroll_manifest_inputs(custody_library.read_descriptor(transport['descriptor']['path']))
  raw=gzip.decompress(encoded)
 assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
 return raw
resource_anchor=next(a for a in parent_successful['outputArchives'] if a['original']['path'].endswith('/Avalonia/resources'))
assert resource_anchor['original']['sha256']=='d74b0fed6e22d8a77f9c4e576f1718ea550b087a945bd404e8c47ce5a94f5497'
assert len(read_archive(resource_anchor))==resource_anchor['original']['bytes']
c25_ticket=pathlib.Path('/workspace/astra-source/root-checkpoint25-strata-core20-20261006-01/CHECKPOINT25-STRATA-OWNING-SOURCE-TICKET01.json')
c25=json.loads(c25_ticket.read_bytes());assert c25['afterHead']=='060746ec558c0da82a6653bb531d2794638c1db5';enroll(c25_ticket)
# Exact existing byte custody index only: fresh current outputs/resources are still generated and whole-hashed.
# Matching regenerated bytes may reuse an immutable validated gzip; this never grants old-resource currentness.
validated_parent_archive_bytes=[];validated_archive_paths=set()
for row in [*parent_successful['outputArchives'],*c24['outputArchives']]:
 a=pathlib.Path(row['archive']['path'])
 if str(a) in validated_archive_paths:continue
 raw=read_archive(row)
 assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
 archive_by_sha[row['original']['sha256']]=row['archive'];validated_archive_paths.add(str(a))
 validated_parent_archive_bytes.append({'originalBytes':row['original']['bytes'],'originalSha256':row['original']['sha256'],'archive':row['archive']})

closed_reference_custody=pathlib.Path('/workspace/astra-source/a4-framework-checkpoint20-owning-output-custody01/closed-reference-json01/C20-CLOSED-REFERENCE-JSON-EXACT-CUSTODY01.json')
assert pin(closed_reference_custody)['sha256']=='5e4c5ab9a301a47a75ad19e4412d3369e176d9b352805d49e5b4ac313391b29a'
enroll(closed_reference_custody);closed_json=json.loads(closed_reference_custody.read_bytes())
closed_evaluation_custody=pathlib.Path('/workspace/astra-source/a4-framework-checkpoint20-owning-output-custody01/closed-evaluation-json02/C20-CLOSED-EVALUATION-JSON-EXACT-CUSTODY01.json')
assert pin(closed_evaluation_custody)['sha256']=='c5dddee0e73b586f70052136f2b49f08e6674701dedbdb47e67772c7de5d47d0'
enroll(closed_evaluation_custody);closed_evaluations=json.loads(closed_evaluation_custody.read_bytes())
for row in closed_evaluations['rows']:
 a=pathlib.Path(row['archive']['path']);assert pin(a)==row['archive'];enroll(a)
 raw=gzip.decompress(a.read_bytes());assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
 assert row['supportedNaturalEvaluationStage']['log']==row['original']
for row in closed_json['rows']:
 a=pathlib.Path(row['archive']['path']);assert pin(a)==row['archive'];enroll(a)
 raw=gzip.decompress(a.read_bytes());assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
 assert row['supportedNaturalReferenceStage']['log']==row['original']
# C20 raw reference log bodies are receiving evidence, not actual SDK inputs. Their exact archive is validated;
# current SAME binary/reference inputs are restored and fresh-evaluated below before every actual compiler.
assert parent_successful['exitCode']==1 and parent_successful['inputsUnchangedAtCompletion']
# Every historical successful product requires exact fresh whole source/order/ref/analyzer equality before any permitted reuse.
reuse_originals={o['module']:(parent_successful_receipt,parent_successful,next(p for p in parent_successful['modulePlans'] if p['module']==o['module']),o) for o in parent_successful['outputs']}
old_inputs={x['path']:x for x in parent_successful['inputs']}
# Current nineteen-owner parent conserved all inputs at actual completion. No legacy unknown-at-run substitution.
assert len(reuse_originals)==19 and 'Haven.Desktop' in reuse_originals
reuse_modules=set(reuse_originals)
# Core and PluginFixture alone are eligible for exact source/reference reuse; every affected owner and seven genuine test roots are fresh.
reuse_modules={'Haven.Core','Haven.PluginFixture'}
core_output=next(o for o in c24['outputs'] if o['module']=='Haven.Core')
assert all(core_output[k]==reuse_originals['Haven.Core'][3][k] for k in ('output','target','pdb','reference'))
output_owners={o[k]['path']:name for name,(_,_,_,o) in reuse_originals.items() for k in ('output','target','reference') if o.get(k)}
dependencies={name:{output_owners[x['input']['path']] for x in plan['actualReferencePins'] if x['input']['path'] in output_owners}-{name} for name,(_,_,plan,_) in reuse_originals.items()}
combined={row['target']:row for row in c24['sourceRows']}
for source_row in [*c25['afterRows'],*supplement.get('finalRows',supplement.get('afterRows',[]))]:
 row=dict(source_row)
 if 'afterPin' in row:row['bytes']=row['afterPin']['bytes'];row['sha256']=row['afterPin']['sha256']
 combined[row['target']]=row
consolidation_packet=pathlib.Path('/tmp/astra-data-c29-continuation-custody-consolidation01/TASK-CANONICAL-CONTINUATION-CUSTODY-CONSOLIDATION01.json.gz')
assert pin(consolidation_packet)['sha256']=='a691d62730ae9d97cbf76efb32da2295a7b8b79616e6a1c053f450dd9860afcf';enroll(consolidation_packet)
consolidation=json.loads(gzip.decompress(consolidation_packet.read_bytes()));assert len(consolidation['endpoints'])==2
retirement_rows=supplement['retiredRows'];assert len(retirement_rows)==1
retired=retirement_rows[0];old_target=retired['target'];new_target=retired['owningTarget']
assert retired['retirementRule']=='EXACT_REVIEWED_WHOLE_BODY_MOVED_TO_CANONICAL_CHAT_OWNER' and not retired['afterExists']
assert old_target=='9to1 Workspace/shared/src/Haven.Application/Execution/CanonicalContinuationProcessCustody.cs' and new_target=='9to1 Workspace/shared/src/Haven.Application/Chat/CanonicalContinuationProcessCustody.cs'
removed=next(x for x in consolidation['endpoints'] if x['target']==old_target);moved=next(x for x in consolidation['endpoints'] if x['target']==new_target)
assert not removed['afterPresent'] and removed['after']=='' and removed['afterBytes']==0 and removed['afterSha256'] is None
assert moved['afterPresent'] and moved['after']==removed['before']
assert retired['beforePin']==retired['conservedOwningAfterPin']=={'bytes':moved['afterBytes'],'sha256':moved['afterSha256']}
assert not (repo/old_target).exists() and subprocess.run(['git','show',head+':'+old_target],cwd=repo,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode!=0
assert subprocess.check_output(['git','show',consolidation['currentRoot']['head']+':'+old_target],cwd=repo)==removed['before'].encode()
historical_retirement_head='58e0ccd98ae5f8785caac9ed7fedb52df4ea93f3'
assert retired['verifiedRetirementHead']==historical_retirement_head
assert subprocess.check_output(['git','show',historical_retirement_head+':'+new_target],cwd=repo)==moved['after'].encode()
assert subprocess.run(['git','show',historical_retirement_head+':'+old_target],cwd=repo,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode!=0
current_custody_correction=pathlib.Path('/tmp/astra-a3-c30-application-custody-compiler01/C30-APPLICATION-THREE-DIAGNOSTIC-CUSTODY-PREREQUISITE01.json.gz')
assert pin(current_custody_correction)=={'path':str(current_custody_correction),'bytes':4123,'sha256':'565b131d6426ed5e350bc7e53394e6c3007daf81d6f6744c47c99d86e5db73da'};enroll(current_custody_correction)
correction=json.loads(gzip.decompress(current_custody_correction.read_bytes()))
assert correction['receivingCurrentHead']==historical_retirement_head and correction['currentRoot']==str(repo)
correction_rows={x['target']:x for x in correction['endpoints']}
assert set(correction_rows)=={new_target,'9to1 Workspace/shared/src/Haven.Application/Execution/TaskRunColdSourceIo.cs'}
for corrected_target,corrected in correction_rows.items():
 before=corrected['before'].encode();after=corrected['after'].encode()
 assert len(before)==corrected['beforeBytes'] and hashlib.sha256(before).hexdigest()==corrected['beforeSha256']
 assert len(after)==corrected['afterBytes'] and hashlib.sha256(after).hexdigest()==corrected['afterSha256']
 assert subprocess.check_output(['git','show',historical_retirement_head+':'+corrected_target],cwd=repo)==before
 assert (repo/corrected_target).read_bytes()==after
 assert combined[corrected_target]['bytes']==corrected['afterBytes'] and combined[corrected_target]['sha256']==corrected['afterSha256']
current_owner=correction_rows[new_target]
assert current_owner['before']==moved['after']
assert retired['currentOwningAfterPin']=={'bytes':current_owner['afterBytes'],'sha256':current_owner['afterSha256']}
assert current_owner['afterBytes']==5847 and current_owner['afterSha256']=='1a6e101eba6a86e356e5ffbdca203b5a1d48df81776e59569db731ca32adfde6'
# conservedOwningAfterPin is the historical exact C30 owner, not a false current whole-body equality.
if old_target in combined:
 assert combined[old_target]['bytes']==removed['beforeBytes'] and combined[old_target]['sha256']==removed['beforeSha256'];combined.pop(old_target)
selection={'afterHead':head,'finalRows':list(combined.values())}
for p in [ticket,sdk,pathlib.Path(__file__),metadata_receipt,metadata_custody,namespace_receipt]:enroll(p)
selected={}
for row in selection['finalRows']:
 p=repo/row['target'];v=enroll(p);assert v['bytes']==row['bytes'] and v['sha256']==row['sha256'],row['target'];selected[row['target']]=row
# Complete inherited shipping/control declarations are part of the controlling current graph,
# even when a later corrective packet repeats only changed rows.
preferred_complete_carriers=[
 ('/workspace/astra-source/data-cold-invoked05/TASK-COLD-ACCEPTED-BOUNDARY05-SOURCE-PROPOSAL01.json.gz','abd4c81579c5add5da28eb83bdbc4bb602b29c20d325a540267762efda37b2f7'),
 ('/workspace/astra-source/data-application-compiler-prerequisite01/TASK-COLD05-COMPILER-COALESCED-SOURCE06.json.gz','80cfc7ff36d6064ea2b9cfe27deee2c96c5c1c40671515da028c9afdeffb1c35'),
 ('/workspace/astra-source/a5-local-selected-route-original01/TASK-ACTUAL-SCOPED-LOCAL-SELECTED-ROUTE01.json.gz','41383c2766546dfd873e0256b0e14df8c0acc1cdf1f64e05bd7296389cf07a0f'),
 ('/tmp/astra-a5-local-selected-route02/TASK-SCOPED-LOCAL-ROUTE-AND-INFERENCE-DEEP-CATALOGUE02.json.gz','e4765bbad55bcc6031bd12cc64538a2cb83119135e8783b53746dbfa3d7a2b30')]
complete_preferred_union={}
for carrier,expected_sha in preferred_complete_carriers:
 packet=pathlib.Path(carrier);assert pin(packet)['sha256']==expected_sha;enroll(packet)
 for row in json.loads(gzip.decompress(packet.read_bytes()))['endpoints']:
  target=row['target'];target=str(pathlib.Path(target).relative_to(repo)) if target.startswith('/') else target
  after=row['after'].encode();assert len(after)==row['afterBytes'] and hashlib.sha256(after).hexdigest()==row['afterSha256']
  complete_preferred_union[target]={'bytes':row['afterBytes'],'sha256':row['afterSha256'],'carrier':pin(packet)}
assert len(complete_preferred_union)==35
historical_preferred_union=dict(complete_preferred_union)
assert complete_preferred_union[old_target]['bytes']==removed['beforeBytes'] and complete_preferred_union[old_target]['sha256']==removed['beforeSha256']
complete_preferred_union.pop(old_target)
complete_preferred_union[new_target]={'bytes':moved['afterBytes'],'sha256':moved['afterSha256'],'carrier':pin(consolidation_packet),'relocatedFromExplicitRetiredTarget':old_target,'historicalVerifiedRetirementHead':historical_retirement_head}
for corrected_target,corrected in correction_rows.items():
 assert corrected_target in complete_preferred_union
 prior=complete_preferred_union[corrected_target]
 assert prior['bytes']==corrected['beforeBytes'] and prior['sha256']==corrected['beforeSha256'],('Correction must conserve entire current inherited precursor',corrected_target,prior)
 complete_preferred_union[corrected_target]={'bytes':corrected['afterBytes'],'sha256':corrected['afterSha256'],'carrier':pin(current_custody_correction),'exactHistoricalPrecursor':prior,'currentExplicitWholeSourceCorrection':True}
assert len(complete_preferred_union)==35
current_ticket_overrides=[]
# The complete C32 whole46, C33 whole12, C34 whole13, C35 whole2 and C36 whole12 overrides remain operative after their original failed intervals.
# Authenticate the exact immediate parent ticket, then apply its reviewed whole changes and current successor changes in order.
inherited_current_ticket=pathlib.Path('/tmp/astra-root-checkpoint32-local-critical-source01/CHECKPOINT32-LOCAL-CRITICAL-SOURCE-OWNING-SOURCE-TICKET.json')
assert pin(inherited_current_ticket)=={'path':str(inherited_current_ticket),'bytes':360539,'sha256':'301dc5640098864479a23e703edeb1090340c3c68d6772d2501bc35bb9b14018'};enroll(inherited_current_ticket)
inherited_current=json.loads(inherited_current_ticket.read_bytes())
assert inherited_current['afterHead']=='58def917a45e2a8a54e088557fe2d03bbff5db32'
inherited_successor_ticket=pathlib.Path('/tmp/astra-root-checkpoint33-compile-ready-components01/CHECKPOINT33-COMPILE-READY-COMPONENTS-OWNING-SOURCE-TICKET.json')
assert pin(inherited_successor_ticket)=={'path':str(inherited_successor_ticket),'bytes':322988,'sha256':'c74588218c4a75e4446973a7a54488ed2006740b091b5fdacef578bc19b4f3b9'};enroll(inherited_successor_ticket)
inherited_successor=json.loads(inherited_successor_ticket.read_bytes())
assert inherited_successor['parentHead']==inherited_current['afterHead'] and inherited_successor['parentTicket']==pin(inherited_current_ticket)
assert inherited_successor['afterHead']=='64b6530484340ee6bf1697b61ec5ba3bd1f34093'
inherited_latest_ticket=pathlib.Path('/tmp/astra-root-checkpoint34-fresh-project-command-execution01/CHECKPOINT34-FRESH-PROJECT-COMMAND-EXECUTION-OWNING-SOURCE-TICKET.json')
assert pin(inherited_latest_ticket)=={'path':str(inherited_latest_ticket),'bytes':334519,'sha256':'70f252d2e9ba075507d5e89b67f653cac6f84d523e5366ff0efca3bbdd695ad3'};enroll(inherited_latest_ticket)
inherited_latest=json.loads(inherited_latest_ticket.read_bytes())
assert inherited_latest['parentHead']==inherited_successor['afterHead'] and inherited_latest['parentTicket']==pin(inherited_successor_ticket)
assert inherited_latest['afterHead']=='130227d21362dd18f8149f9113b6c781c4221eae'
inherited_final_ticket=pathlib.Path('/tmp/astra-root-checkpoint35-exact-home-compiler-sites01/CHECKPOINT35-EXACT-HOME-COMPILER-SITES-OWNING-SOURCE-TICKET.json')
assert pin(inherited_final_ticket)=={'path':str(inherited_final_ticket),'bytes':298841,'sha256':'ab6e45b77adb86543ba28d566a097a87ddef2f3479caa8cab31f85e1f1682da3'};enroll(inherited_final_ticket)
inherited_final=json.loads(inherited_final_ticket.read_bytes())
assert inherited_final['parentHead']==inherited_latest['afterHead'] and inherited_final['parentTicket']==pin(inherited_latest_ticket)
assert inherited_final['afterHead']=='bc5a7bb079f9c343b33682bec048e622e05c789e'
inherited_infra_ticket=pathlib.Path('/tmp/astra-root-checkpoint36-exact-infrastructure-compiler-sites01/CHECKPOINT36-EXACT-INFRASTRUCTURE-COMPILER-SITES-OWNING-SOURCE-TICKET.json')
assert pin(inherited_infra_ticket)=={'path':str(inherited_infra_ticket),'bytes':316135,'sha256':'eb327401dc3d60b446e7ec5b1718456e82952f534b8696fc9d59e46ace826439'};enroll(inherited_infra_ticket)
inherited_infra=json.loads(inherited_infra_ticket.read_bytes())
assert inherited_infra['parentHead']==inherited_final['afterHead'] and inherited_infra['parentTicket']==pin(inherited_final_ticket)
assert inherited_infra['afterHead']=='c14c961b22b99581095236b2f7e0b3e1a16ecd64'
inherited_product_ticket=pathlib.Path('/workspace/astra-root-checkpoint37-exact-desktop-test-compiler-sites01/CHECKPOINT37-EXACT-DESKTOP-TEST-COMPILER-SITES-OWNING-SOURCE-TICKET.json')
assert pin(inherited_product_ticket)=={'path':str(inherited_product_ticket),'bytes':310034,'sha256':'0e668d7b5992254ca70268662a427901607ba00ba85d4c4b299854b2ae0edd05'};enroll(inherited_product_ticket)
inherited_product=json.loads(inherited_product_ticket.read_bytes())
assert inherited_product['parentHead']==inherited_infra['afterHead'] and inherited_product['parentTicket']==pin(inherited_infra_ticket)
assert inherited_product['afterHead']=='cacd6375bb6dfc99340a8d9bac878a2994a5ea19'
inherited_tests_ticket=pathlib.Path('/workspace/astra-root-checkpoint38-test-only-compiler-fresh-home01/CHECKPOINT38-TEST-ONLY-COMPILER-FRESH-HOME-OWNING-SOURCE-TICKET.json')
assert pin(inherited_tests_ticket)=={'path':str(inherited_tests_ticket),'bytes':336848,'sha256':'4c878d6191d838f54def3c6b14c76a47ac7bf3a74a5dcfb77063a1af6e0242cb'};enroll(inherited_tests_ticket)
inherited_tests=json.loads(inherited_tests_ticket.read_bytes())
assert inherited_tests['parentHead']==inherited_product['afterHead'] and inherited_tests['parentTicket']==pin(inherited_product_ticket)
assert inherited_tests['afterHead']=='bda5ced1306fdd22e1db858eede1b38ef5573fde'
inherited_planner_ticket=pathlib.Path('/workspace/astra-root-checkpoint39-cloudflare-paired-owner-startup01/CHECKPOINT39-CLOUDFLARE-PAIRED-OWNER-STARTUP-OWNING-SOURCE-TICKET.json')
assert pin(inherited_planner_ticket)=={'path':str(inherited_planner_ticket),'bytes':298624,'sha256':'cb2d8f396017887fb41ce6dc363af28231a7f125aeeb18df331e74fe5f830410'};enroll(inherited_planner_ticket)
inherited_planner=json.loads(inherited_planner_ticket.read_bytes())
assert inherited_planner['parentHead']==inherited_tests['afterHead'] and inherited_planner['parentTicket']==pin(inherited_tests_ticket)
assert inherited_planner['afterHead']=='f6a0c758eebfde161e65b77b6b5dee15979e788a'
assert len(inherited_planner['newRows'])==1 and inherited_planner['newRows'][0]['target']=='9to1 Workspace/shared/src/Haven.Infrastructure/DI/CloudflareTaskToolServiceCollectionExtensions.cs'
inherited_console_ticket=pathlib.Path('/workspace/astra-root-checkpoint40-maintained-planner-console01/CHECKPOINT40-MAINTAINED-PLANNER-CONSOLE-OWNING-SOURCE-TICKET.json')
assert pin(inherited_console_ticket)=={'path':str(inherited_console_ticket),'bytes':299749,'sha256':'cb6236accca617ef24299a2f9e5c1b4a1936dd7f8e83455291dbbb5b5411ca2e'};enroll(inherited_console_ticket)
inherited_console=json.loads(inherited_console_ticket.read_bytes())
assert inherited_console['parentHead']==inherited_planner['afterHead'] and inherited_console['parentTicket']==pin(inherited_planner_ticket)
assert inherited_console['afterHead']=='adbbdde0daf318ebf3c09a9a246eced39286e787'
assert len(inherited_console['newRows'])==1 and inherited_console['newRows'][0]['target']=='9to1 Workspace/shared/src/Haven.Desktop/Services/OriginalLocalTaskConsole.cs'
inherited_writer_ticket=pathlib.Path('/workspace/astra-root-checkpoint41-final-writer-forwarding01/CHECKPOINT41-FINAL-WRITER-FORWARDING-OWNING-SOURCE-TICKET.json')
assert pin(inherited_writer_ticket)=={'path':str(inherited_writer_ticket),'bytes':298173,'sha256':'d8fd09be725385f7d2f6f3c0acbd9e08e4734bdcdae3ba7a365f0dcc795034a5'};enroll(inherited_writer_ticket)
inherited_writer=json.loads(inherited_writer_ticket.read_bytes())
assert inherited_writer['parentHead']==inherited_console['afterHead'] and inherited_writer['parentTicket']==pin(inherited_console_ticket)
assert inherited_writer['afterHead']=='e1048151b473fc2f3dc4d2d8a47055742f1409c9'
assert len(inherited_writer['newRows'])==1 and inherited_writer['newRows'][0]['target']=='9to1 Workspace/shared/src/Haven.Infrastructure/Database/Maintenance/CrashRecoveryCoordinator.cs'
assert inherited_writer['newRows'][0]['afterPin']=={'bytes':3299,'sha256':'d6b8526572b9a0006f16c1f7cb7723416ef1cb0542533b3c01896355793164f0'}
inherited_c42_ticket=pathlib.Path('/workspace/astra-root-checkpoint42-strata-journal01/CHECKPOINT42-STRATA-JOURNAL-OWNING-SOURCE-TICKET.json')
assert pin(inherited_c42_ticket)=={'path':str(inherited_c42_ticket),'bytes':390540,'sha256':'24ce1979713a426bcbf86550429201e5eeb9edec5d3e34d2055a5d128ca4daf7'};enroll(inherited_c42_ticket)
inherited_c42=json.loads(inherited_c42_ticket.read_bytes())
assert inherited_c42['parentHead']==inherited_writer['afterHead'] and inherited_c42['parentTicket']==pin(inherited_writer_ticket)
assert inherited_c42['afterHead']=='eceae0c72ab571afd4b08978457dc17699fa9029'
expected_inherited_c42_after_pins={'9to1 Workspace/Home/Source/Home/Core/FileHomeCoreStateStore.OriginalClaimedRead.cs': {'bytes': 3788, 'sha256': '09d4d6acb3acaa9199d44430a19078f8eeace531d86c42b2ada45d8a5a593619'}, '9to1 Workspace/shared/docs/architecture/system-map.md': {'bytes': 11762, 'sha256': '0895e2940c8ebd6e305a5c001f71c2ea62a19f7732765c0cb953f1e9dd7293b7'}, '9to1 Workspace/shared/src/Haven.Desktop/Services/OriginalLocalTaskConsole.cs': {'bytes': 54301, 'sha256': '856b2adb30886d43a37e0595b887049226b96ade3bc0422adcc4f1170dc8ca77'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Providers/Strata/HomeApprovedStrataDeveloperArtifactSource.cs': {'bytes': 27670, 'sha256': '31a7898a9d497d8aec5c8f9d9572d24c11d2300e2185e7d4875ad709834be688'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Providers/Strata/StrataDeveloperArtifactRegistration.cs': {'bytes': 2171, 'sha256': 'a7b6ccbfcc570031fa5ebcf4254771e4c081bfeb672a52d503000f27663ed8cc'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Providers/Strata/StrataDeveloperArtifactSelection.cs': {'bytes': 11467, 'sha256': '435fca1efe7d150aea3e845d7719c9cc73ff818b4ea6263bf5a15ba40087c037'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Providers/Strata/StrataNativeArtifactSource.cs': {'bytes': 47076, 'sha256': '20de9d010b2c1e285c4dc604ce8163afdf246187219d72bdfb882a8143d8be64'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Providers/Strata/StrataProtectedArtifactContracts.cs': {'bytes': 3567, 'sha256': '8765420af60f1f0fe416d38f7ad6305eba4da237e5dc1f8a6eb75991e9678b77'}, '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/StrataDeveloperArtifactAuthorityTests.cs': {'bytes': 16926, 'sha256': 'f190e311e185d7a902d89359a607942d095aab1bbdd29d22207c6dae4a15d257'}, '9to1 Workspace/Home/Source/Home/Core/HomeDeveloperProjectSetupJournal.cs': {'bytes': 27833, 'sha256': '2af61250dfb8d1abb5808df7db71f8bb4802a8d89af8181d5e46c83ed555d039'}, '9to1 Workspace/Home/Tests/HomeDeveloperProjectSetupJoinGuardTests.cs': {'bytes': 7042, 'sha256': 'e979bfdb9399127bc59730f0ed6990cd93330fb4650c01ba59c23220d2ff835f'}}
assert len(inherited_c42['newRows'])==11 and {row['target']:row['afterPin'] for row in inherited_c42['newRows']}==expected_inherited_c42_after_pins
assert len(inherited_c42['afterRows'])==451 and {row['target'] for row in inherited_c42['afterRows']}=={row['target'] for row in inherited_writer['afterRows']}|set(expected_inherited_c42_after_pins)
inherited_c43_ticket=pathlib.Path('/workspace/astra-root-checkpoint43-leaf-planner01/CHECKPOINT43-LEAF-PLANNER-OWNING-SOURCE-TICKET.json')
assert pin(inherited_c43_ticket)=={'path': '/workspace/astra-root-checkpoint43-leaf-planner01/CHECKPOINT43-LEAF-PLANNER-OWNING-SOURCE-TICKET.json', 'bytes': 383373, 'sha256': '804e3c5880e545c7bf346a2becbd64989f949c8e1bb69c1219ef692f697f1261'};enroll(inherited_c43_ticket)
inherited_c43=json.loads(inherited_c43_ticket.read_bytes())
assert inherited_c43['parentHead']==inherited_c42['afterHead'] and inherited_c43['parentTicket']==pin(inherited_c42_ticket)
assert inherited_c43['afterHead']=='f7aa74440b43858ed0d5a488576c2d5de566ae46'
expected_inherited_c43_after_pins={'9to1 Workspace/shared/src/Haven.Infrastructure/Workspace/WorkspaceToolService.DeveloperDirectories.cs': {'bytes': 21930, 'sha256': '18b1b95a5c255b07c7aa2acc168bd71e0abab4c4160fbb5f6b7455e6b7d70564'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Workspace/WorkspaceToolService.DeveloperDirectoryRegistration.cs': {'bytes': 16607, 'sha256': 'e8dc68a28aefe517c0a0880930b493e94a37fc10e879dc647e8d6188f6e9404e'}, '9to1 Workspace/shared/src/Haven.Infrastructure/Workspace/WorkspaceToolService.DeveloperFileRegistration.cs': {'bytes': 20485, 'sha256': '7fe2021b34eaa1053766d463b3ecdaf8f07d49e0e7731540e94bd04665764290'}, '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/DeveloperOriginalDirectoryObservationTests.cs': {'bytes': 20064, 'sha256': 'd51a9cc10355defd53105b1810037f10bb88b361ec01ff2547d2ce05b59c76d0'}, '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/DeveloperOriginalLeafPreparationJoinTests.cs': {'bytes': 12773, 'sha256': '019b6437d7082213cd853365729a322914c92bc5727eea3a8f8b7425a686fb2b'}, '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/CloudflareLocalDomainRegistrationTests.cs': {'bytes': 14177, 'sha256': '16dc75f71d4818a0b850acd8b4cd788567d094eeb8e866f161ba1c394be3bc44'}}
assert len(inherited_c43['newRows'])==6 and {r['target']:r['afterPin'] for r in inherited_c43['newRows']}==expected_inherited_c43_after_pins
assert len(inherited_c43['afterRows'])==452 and {r['target'] for r in inherited_c43['afterRows']}=={r['target'] for r in inherited_c42['afterRows']}|set(expected_inherited_c43_after_pins)
inherited_c44_ticket=pathlib.Path('/workspace/astra-root-checkpoint44-strata-console01/CHECKPOINT44-STRATA-CONSOLE-OWNING-SOURCE-TICKET.json')
assert pin(inherited_c44_ticket)=={'path':str(inherited_c44_ticket),'bytes':411464,'sha256':'fcc7cf644de1752eeddc6dbda5cd1953bc7fdfba39f5fca5ef43b0730e0013de'};enroll(inherited_c44_ticket)
inherited_c44=json.loads(inherited_c44_ticket.read_bytes())
assert inherited_c44['parentHead']==inherited_c43['afterHead'] and inherited_c44['parentTicket']==pin(inherited_c43_ticket) and inherited_c44['afterHead']=='2c09519c64906a1fd307dc9bcce558885fd05ec6'
assert len(inherited_c44['newRows'])==15 and len(inherited_c44['afterRows'])==457
assert {r['target'] for r in inherited_c44['afterRows']}=={r['target'] for r in inherited_c43['afterRows']}|{r['target'] for r in inherited_c44['newRows']}
inherited_c45_ticket=pathlib.Path('/workspace/astra-root-checkpoint45-console-rootnamespace01/CHECKPOINT45-CONSOLE-ROOTNAMESPACE-OWNING-SOURCE-TICKET.json')
assert pin(inherited_c45_ticket)=={'path':str(inherited_c45_ticket),'bytes':389472,'sha256':'a8a2dadc2ca31e5040b91a3a9ba0dbe452bf14294cdd4dfd26503b21624faa11'};enroll(inherited_c45_ticket)
inherited_c45=json.loads(inherited_c45_ticket.read_bytes())
assert inherited_c45['parentHead']==inherited_c44['afterHead'] and inherited_c45['parentTicket']==pin(inherited_c44_ticket) and inherited_c45['afterHead']=='1f2f1778fda757a30b1e29c449c61978614f5875'
assert len(inherited_c45['newRows'])==1 and {r['target']:r['afterPin'] for r in inherited_c45['newRows']}=={'9to1 Workspace/shared/tests/Haven.Console.Tests/Haven.Console.Tests.csproj':{'bytes':2375,'sha256':'0cafecf5046119c2c177f29cd6cf7e9ddb2a2cdacebec21ffe85e6418e2688cc'}} and inherited_c45['newlyEnrolledRows']==[]
assert len(inherited_c45['afterRows'])==457 and {r['target'] for r in inherited_c45['afterRows']}=={r['target'] for r in inherited_c44['afterRows']}
assert inherited_c45['retiredRows']==inherited_c44['retiredRows'] and inherited_c45['closedSdkInterval']==inherited_c44['closedSdkInterval']
inherited_c46_ticket=pathlib.Path('/workspace/astra-root-checkpoint46-safety-dev62/RECEIVING-TICKET02.json')
assert pin(inherited_c46_ticket)=={'path':str(inherited_c46_ticket),'bytes':383282,'sha256':'8139819fbb4edec8cc4e54bac0d981a03d348a1499637b1dac620ce34ce2f47e'};enroll(inherited_c46_ticket)
inherited_c46=json.loads(inherited_c46_ticket.read_bytes())
expected_c46_after_pins={'9to1 Workspace/shared/src/Haven.Desktop/Services/OriginalLocalTaskConsole.DeveloperSetup.cs': {'bytes': 22063, 'sha256': 'daea6e751be031affba17daeac788a212f9618877d9a7072fe96d3ebbeddb019'}, '9to1 Workspace/shared/src/Haven.Application/Safety/CheckpointService.cs': {'bytes': 14626, 'sha256': '26320ef788650a53fe4e4da3cbc89e831ed6f40cb75b1fcf9def3baf05a92ab8'}, '9to1 Workspace/shared/tests/Haven.Core.Tests/CheckpointAndInstructionsTests.cs': {'bytes': 14722, 'sha256': '82f12eb5d5b1c233fcbcd517cdfc325be1785c98211644a6298f5b5f4e1b8f01'}, '9to1 Workspace/shared/docs/architecture/state-and-persistence.md': {'bytes': 15009, 'sha256': '992ec6ff1d2e282b1966768dc2bb2c28f21cc577cf6cd647465d23704f80e7ce'}, '9to1 Workspace/Dev/DeveloperTaskWorkspaceService.cs': {'bytes': 52546, 'sha256': '1bc9d3cbd7627175ebdce680e158babe8259731b4b17ce7612537f7d4ad73b6e'}, '9to1 Workspace/Dev/Tests/DeveloperTaskWorkspaceServiceTests.cs': {'bytes': 66174, 'sha256': '8c5a4ab3855fa2053522044ffd0b2f14b461516ec490a8089fd721034b26863c'}, '9to1 Workspace/shared/src/Haven.Desktop/Services/OriginalNativeDevelopmentServiceCollectionExtensions.cs': {'bytes': 5356, 'sha256': '48a26fe047f358429316725bff21410006d5cef327f7aebf9a0560f622279dd3'}}
assert inherited_c46['parentHead']==inherited_c45['afterHead'] and inherited_c46['parentTicket']==pin(inherited_c45_ticket) and inherited_c46['afterHead']=='45fc9b3d1de7539ecd00b14bf50a4cf9eb811c9e'
assert len(inherited_c46['newRows'])==7 and {r['target']:{k:r['afterPin'][k] for k in ('bytes','sha256')} for r in inherited_c46['newRows']}==expected_c46_after_pins
assert len(inherited_c46['afterRows'])==459 and len(inherited_c46['newlyEnrolledRows'])==2
# Root supplies the exact current integrated ticket/SHA/head at launch. Every
# intervening whole ticket and Git parent/source body is read and bound here;
# this never authorizes an unintegrated proposal or a new invented project.
current_reviewed_source_chain=[];walk_body=supplement;walk_path=ticket;seen_tickets=set()
while walk_body['afterHead']!=inherited_c45['afterHead']:
 assert str(walk_path) not in seen_tickets;seen_tickets.add(str(walk_path));enroll(walk_path)
 assert walk_body['parentHead']==subprocess.check_output(['git','rev-parse',walk_body['afterHead']+'^'],cwd=repo,text=True).strip()
 parent_pin=walk_body['parentTicket'];parent_path=pathlib.Path(parent_pin['path']);assert pin(parent_path)==parent_pin;enroll(parent_path)
 parent_body=json.loads(parent_path.read_bytes());assert parent_body['afterHead']==walk_body['parentHead']
 assert walk_body['retiredRows']==parent_body['retiredRows']
 assert {r['target'] for r in walk_body['afterRows']}=={r['target'] for r in parent_body['afterRows']}|{r['target'] for r in walk_body['newlyEnrolledRows']}
 parent_rows={r['target']:{k:r['afterPin'][k] for k in ('bytes','sha256')} for r in parent_body['afterRows']};after_rows={r['target']:{k:r['afterPin'][k] for k in ('bytes','sha256')} for r in walk_body['afterRows']}
 changes={r['target']:r for r in walk_body['newRows']};assert len(changes)==len(walk_body['newRows'])
 for target,row in after_rows.items():
  if target not in changes:assert target in parent_rows and row['bytes']==parent_rows[target]['bytes'] and row['sha256']==parent_rows[target]['sha256']
 for reviewed in walk_body['newRows']:
  target=reviewed['target'];before=reviewed['beforePin'];after=reviewed['afterPin']
  if reviewed['beforeExists']:
   before_git=subprocess.check_output(['git','show',walk_body['parentHead']+':'+target],cwd=repo)
   assert len(before_git)==before['bytes'] and hashlib.sha256(before_git).hexdigest()==before['sha256']
  else:
   assert (before is None or {k:before[k] for k in ('bytes','sha256')}=={'bytes':0,'sha256':hashlib.sha256(b'').hexdigest()}) and subprocess.run(['git','show',walk_body['parentHead']+':'+target],cwd=repo,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode!=0
  after_git=subprocess.check_output(['git','show',walk_body['afterHead']+':'+target],cwd=repo)
  assert len(after_git)==after['bytes'] and hashlib.sha256(after_git).hexdigest()==after['sha256']
  assert after_rows[target]['bytes']==after['bytes'] and after_rows[target]['sha256']==after['sha256']
  for descriptor in ([before] if reviewed['beforeExists'] else [])+[after]:
   if descriptor is not None and 'path' in descriptor:assert pin(descriptor['path'])==descriptor;enroll(descriptor['path'])
  if 'afterSource' in reviewed:
   actual_after=enroll(reviewed['afterSource']);assert actual_after['bytes']==after['bytes'] and actual_after['sha256']==after['sha256']
  carriers=reviewed.get('packets') or ([reviewed['packet']['path']] if isinstance(reviewed.get('packet'),dict) else [reviewed['packet']] if reviewed.get('packet') else [])
  if not carriers and pin(walk_path)=={'path': '/workspace/astra-root-checkpoint49-files-assertions64/OWNING-SOURCE-TICKET.json', 'bytes': 394799, 'sha256': '59e6ba1c1ae556289463be7603b529db07e4b5b06ff0cc112f9a8c42de275dc0'}:
   assert walk_body['parentHead']=='7d41e42a7fb89a7f0097fe2d37923989dccbbd25' and walk_body['afterHead']=='cff9281d0bc6640c3305f9a54daf1c1141877497'
   assert len(walk_body['newRows'])==1 and walk_body['newlyEnrolledRows']==[] and len(walk_body['afterRows'])==501 and target=='9to1 Workspace/shared/tests/Haven.Desktop.Tests/FilesNativeBrowserMutationTests.cs'
   assert before=={'bytes': 17621, 'sha256': 'ff6f0f6285aa7eb1861fdbd75b5eb7f15bd230f57d61f548381719b4a26f4712'} and after=={'bytes': 17608, 'sha256': '6a7c77fcff0024e840505955a497d06afbf43917c07b75804a9e81ba255fe895'} and reviewed['afterSource']=='/workspace/astra-source/friday-files-downstream-sol61u62/files64-assert-single04/proposed/9to1 Workspace/shared/tests/Haven.Desktop.Tests/FilesNativeBrowserMutationTests.cs'
   assert actual_after['bytes']==after['bytes'] and actual_after['sha256']==after['sha256'];witnessed_body=before_git
   for fragment_before,fragment_after in [(b'        var change = Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items\n            .Where(row => row.ItemId == ack.Value.ItemId));', b'        var change = Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items,\n            row => row.ItemId == ack.Value.ItemId);'), (b'        Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items.Where(row => row.ItemId == ack.Value.ItemId));', b'        Assert.Single((await h.Workspace.Provider.GetChangesAsync(null, 100, token)).Items, row => row.ItemId == ack.Value.ItemId);')]:
    assert witnessed_body.count(fragment_before)==1;witnessed_body=fragment_after.join(witnessed_body.split(fragment_before,1))
   assert witnessed_body==after_git;carriers=[str(walk_path)]
  assert carriers,('Root reviewed source carrier missing',target)
  for carrier in carriers:enroll(carrier)
 current_reviewed_source_chain.append((walk_body,walk_path));walk_body=parent_body;walk_path=parent_path
assert pin(walk_path)==pin(inherited_c45_ticket)
current_reviewed_source_chain.reverse()
assert current_reviewed_source_chain[0][0]==inherited_c46 and pin(current_reviewed_source_chain[0][1])==pin(inherited_c46_ticket)
assert current_reviewed_source_chain[-1][0]['afterHead']==head==expected_head and pin(current_reviewed_source_chain[-1][1])==pin(ticket)
assert not subprocess.check_output(['git','status','--porcelain=v1','--untracked-files=no'],cwd=repo,text=True)



for controlling_body,controlling_path in [(inherited_current,inherited_current_ticket),(inherited_successor,inherited_successor_ticket),(inherited_latest,inherited_latest_ticket),(inherited_final,inherited_final_ticket),(inherited_infra,inherited_infra_ticket),(inherited_product,inherited_product_ticket),(inherited_tests,inherited_tests_ticket),(inherited_planner,inherited_planner_ticket),(inherited_console,inherited_console_ticket),(inherited_writer,inherited_writer_ticket),(inherited_c42,inherited_c42_ticket),(inherited_c43,inherited_c43_ticket),(inherited_c44,inherited_c44_ticket),(inherited_c45,inherited_c45_ticket),*current_reviewed_source_chain]:
 for changed_row in controlling_body['newRows']:
  target=changed_row['target']
  if target not in complete_preferred_union:continue
  prior=complete_preferred_union[target];before={k:changed_row['beforePin'][k] for k in ('bytes','sha256')};after={k:changed_row['afterPin'][k] for k in ('bytes','sha256')}
  assert changed_row['beforeExists'] and before=={'bytes':prior['bytes'],'sha256':prior['sha256']},('Current whole source override must conserve exact inherited prior',target,prior,before)
  actual_parent=subprocess.check_output(['git','show',controlling_body['parentHead']+':'+target],cwd=repo)
  actual_after=subprocess.check_output(['git','show',controlling_body['afterHead']+':'+target],cwd=repo)
  assert len(actual_parent)==before['bytes'] and hashlib.sha256(actual_parent).hexdigest()==before['sha256']
  assert len(actual_after)==after['bytes'] and hashlib.sha256(actual_after).hexdigest()==after['sha256']
  override={'target':target,'historicalWholeSourcePin':prior,'currentBefore':before,'currentAfter':after,'controllingSourceTicket':pin(controlling_path),'reviewedWholeSourceCarrier':changed_row.get('packet',changed_row.get('packets')),'currentWholeSourceOverride':True}
  complete_preferred_union[target]={'bytes':after['bytes'],'sha256':after['sha256'],'carrier':changed_row.get('packet',changed_row.get('packets')),'exactHistoricalPrecursor':prior,'currentControllingSourceTicket':pin(controlling_path),'currentExplicitWholeSourceCorrection':True}
  current_ticket_overrides.append(override)
for target,expected in complete_preferred_union.items():
 assert target in selected and selected[target]['bytes']==expected['bytes'] and selected[target]['sha256']==expected['sha256'],('Omitted/different inherited complete preferred source',target)
 assert (repo/target).read_bytes() and pin(repo/target)['sha256']==expected['sha256']
xml_paths=subprocess.check_output(['rg','--files','--hidden','--no-ignore','-g','*.csproj','-g','*.proj','-g','*.props','-g','*.targets','-g','!**/obj/**','-g','!**/bin/**','-g','!**/.git/**'],cwd=repo,text=True).splitlines()
xml_set=set(xml_paths);required=set()
for rel in [*xml_paths,*selected]:
 p=pathlib.PurePosixPath(rel).parent
 while str(p)!='.':required.add(str(p));p=p.parent
def materialize(relative):
 orig=repo/relative;dest=shadow/relative
 for child in orig.iterdir():
  rel=str((pathlib.PurePosixPath(relative)/child.name).as_posix());target=dest/child.name
  if rel in xml_set or rel in selected:
   target.write_bytes(child.read_bytes());enroll(child);enroll(target)
  elif child.is_dir() and rel in required:target.mkdir();materialize(rel)
  else:target.symlink_to(child,target_is_directory=child.is_dir())
materialize('')
for rel in xml_paths:assert (shadow/rel).is_file() and not (shadow/rel).is_symlink(),rel
# Prefer actual restored private XML over canonical historical observations when both exist.
oldmeta=json.loads(gzip.decompress(metadata_receipt.read_bytes()))
canonical_metadata_xml={x['path'][len(str(repo)) + 1:]:x for x in oldmeta['inputs']
 if x['path'].startswith(str(repo)+'/') and x['path'].endswith(('.csproj','.props','.targets'))}
private_metadata_xml={x['path'].split('/source-root/',1)[1]:x for x in oldmeta['inputs']
 if '/source-root/' in x['path'] and x['path'].endswith(('.csproj','.props','.targets'))}
metadata_xml_authorities=dict(canonical_metadata_xml);metadata_xml_authorities.update(private_metadata_xml)
assert len(metadata_xml_authorities)==359
home_tests_relative='9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj'
files_graph_carrier=pathlib.Path('/tmp/astra-home-files-scoped-evidence01/FILES-ORIGINAL-PARENT-SCOPED-EVIDENCE-SOURCE01.json.gz')
assert pin(files_graph_carrier)=={'path':str(files_graph_carrier),'bytes':13643,'sha256':'5a3c02244226bbde1264e54c20c9c29295deb2c465f95103745d489ce0d57c82'};enroll(files_graph_carrier)
xml_exceptions={}
for packet,relative,graph_changed in [(files_graph_carrier,home_tests_relative,True)]:
 payload=json.loads(gzip.decompress(packet.read_bytes()));record=next(x for x in payload['rows'] if x['target']==relative)
 before=record['before'].encode();after=record['after'].encode();authority=metadata_xml_authorities[relative]
 assert len(before)==authority['bytes'] and hashlib.sha256(before).hexdigest()==authority['sha256']
 assert len(after)==record['afterPin']['bytes'] and hashlib.sha256(after).hexdigest()==record['afterPin']['sha256']
 assert (shadow/relative).read_bytes()==after,('Final ticket must select exact reviewed owning XML',relative)
 assert graph_changed
 insertion=b'    <ProjectReference Include="..\\..\\Files\\NativeHost\\HavenOS.Files.NativeHost.csproj" />\n'
 assert after.count(insertion)==1 and after.replace(insertion,b'',1)==before
 xml_exceptions[relative]={'relative':relative,'before':authority,'current':pin(shadow/relative),'sourceCarrier':pin(packet),
  'wholeForwardInverseSourceVerified':True,'projectPackageReferenceGraphChanged':graph_changed,
  'normalOwningRestoreRequired':graph_changed,'freshWholeSourceEvaluationRequired':True}
desktop_tests_relative='9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj'
prepared_consent_graph_carrier=pathlib.Path('/tmp/astra-home-dev-prepared-consent01/DEV-SAME-PROJECT-ROOT-PREPARED-CONSENT-CALLER01.json.gz')
assert pin(prepared_consent_graph_carrier)=={'path':str(prepared_consent_graph_carrier),'bytes':43139,'sha256':'eb8603b013a8bd55bf8812eee8e458f5c76022a707fb38e1f54e567251a2cc67'};enroll(prepared_consent_graph_carrier)
prepared_graph=json.loads(gzip.decompress(prepared_consent_graph_carrier.read_bytes()))
prepared_xml=next(x for x in prepared_graph['rows'] if x['target']==desktop_tests_relative)
prepared_before=prepared_xml['before'].encode();prepared_after=prepared_xml['after'].encode();prepared_authority=metadata_xml_authorities[desktop_tests_relative]
assert len(prepared_before)==prepared_authority['bytes'] and hashlib.sha256(prepared_before).hexdigest()==prepared_authority['sha256']
assert len(prepared_after)==prepared_xml['afterPin']['bytes'] and hashlib.sha256(prepared_after).hexdigest()==prepared_xml['afterPin']['sha256']
prepared_link=b'    <Compile Include="../../../Dev/Tests/DeveloperOriginalPreparedConsentTests.cs" Link="Dev/DeveloperOriginalPreparedConsentTests.cs" />\n'
assert prepared_after.count(prepared_link)==1 and prepared_after.replace(prepared_link,b'',1)==prepared_before
assert (shadow/desktop_tests_relative).read_bytes()==prepared_after,('Final ticket must select exact full reviewed Desktop.Tests XML',desktop_tests_relative)
xml_exceptions[desktop_tests_relative]={'relative':desktop_tests_relative,'before':prepared_authority,'current':pin(shadow/desktop_tests_relative),'sourceCarrier':pin(prepared_consent_graph_carrier),'wholeForwardInverseSourceVerified':True,'singleExplicitCompileLinkOnly':True,'projectPackageReferenceGraphChanged':False,'normalOwningRestoreRequired':True,'freshWholeSourceEvaluationRequired':True,'requiredActualCompileItem':str(shadow/'9to1 Workspace/Dev/Tests/DeveloperOriginalPreparedConsentTests.cs')}
# Exact Root-reviewed content/resource-only metadata delta outside the22 owners.
# PackageReference, ProjectReference and property graph bytes are unchanged.
external_home_relative='apps/Home/src/AvaloniaHome/AvaloniaHome.csproj'
external_home_before={'bytes': 2315, 'sha256': 'ab1a0ab1fa185d4b66ef6f75d66546be457481947cc655cb164a9842a6b5344c'}
external_home_after={'bytes': 2495, 'sha256': '77247bab222d32ccca85aa5410147fdd15105ef79826d6f0551be8145eaf19fd'}
assert external_home_relative in metadata_xml_authorities and {k:metadata_xml_authorities[external_home_relative][k] for k in ('bytes','sha256')}==external_home_before
external_home_body=(shadow/external_home_relative).read_bytes();assert len(external_home_body)==external_home_after['bytes'] and hashlib.sha256(external_home_body).hexdigest()==external_home_after['sha256']
external_home_content_before=b'    <Content Include="..\\..\\..\\..\\9to1 Workspace\\Home\\UI\\Home.cui" Link="UI\\Home.cui" CopyToOutputDirectory="PreserveNewest" />\n';external_home_content_after=b'    <Content Include="..\\..\\..\\..\\9to1 Workspace\\Home\\UI\\Home.cui" Link="UI\\Home.cui" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />\n';external_home_icon=b'    <EmbeddedResource Include="..\\..\\..\\..\\9to1 Workspace\\shared\\src\\Haven.Desktop\\Assets\\haven.ico" LogicalName="AvaloniaHome.Home.ico" />\n'
assert external_home_body.count(external_home_content_after)==external_home_body.count(external_home_icon)==1
external_home_inverse=external_home_body.replace(external_home_content_after,external_home_content_before,1).replace(external_home_icon,b'',1)
assert len(external_home_inverse)==external_home_before['bytes'] and hashlib.sha256(external_home_inverse).hexdigest()==external_home_before['sha256']
external_home_reviewed=next((reviewed,body,carrier) for body,carrier in current_reviewed_source_chain for reviewed in body['newRows'] if reviewed['target']==external_home_relative and {k:reviewed['afterPin'][k] for k in ('bytes','sha256')}==external_home_after)
external_home_row,external_home_ticket,external_home_carrier=external_home_reviewed
assert external_home_row['beforeExists'] and {k:external_home_row['beforePin'][k] for k in ('bytes','sha256')}==external_home_before
assert subprocess.check_output(['git','show',external_home_ticket['parentHead']+':'+external_home_relative],cwd=repo)==external_home_inverse
assert subprocess.check_output(['git','show',external_home_ticket['afterHead']+':'+external_home_relative],cwd=repo)==external_home_body
external_home_packets=[{'path': '/workspace/astra-source/friday-home-files-sol61u62/HOME62-APP-OWNED-NATIVE-HOST-SOURCE01.json.gz', 'bytes': 21596, 'sha256': '6469ef62e063555502781a9fa88ae3595e39cfd75f02c0cb2c7100baf46d35a5'}, {'path': '/workspace/astra-source/friday-home-files-sol61u62/native02/HOME62-NATIVE-SHELL-REOPEN-SOURCE02.json.gz', 'bytes': 12227, 'sha256': '61864be49327df6ae6e58732c926d45608c59726c8c4a1105e3b9668f21b5672'}]
for descriptor in external_home_packets:assert pin(descriptor['path'])==descriptor;enroll(descriptor['path'])
xml_exceptions[external_home_relative]={'relative':external_home_relative,'before':metadata_xml_authorities[external_home_relative],'current':pin(shadow/external_home_relative),'sourceCarrier':pin(external_home_carrier),'originalReviewedSourcePackets':external_home_packets,'wholeForwardInverseSourceVerified':True,'exactContentCopyToPublishAndExistingIconOnly':True,'projectPackageReferenceGraphChanged':False,'normalOwningRestoreRequired':False,'existing22ProjectAssetsAndPropertiesUnchanged':True,'externalOwningHomeBuildAndPublishNotClaimed':True,'current22OwningSourceCompilerStillRequired':True}
del external_home_body,external_home_inverse
xml_comparison=[]
for rel,before in metadata_xml_authorities.items():
 p=shadow/rel;assert p.is_file(),('Restored actual XML authority missing',rel)
 current=pin(p)
 if rel in xml_exceptions:
  xml_comparison.append(xml_exceptions[rel]);continue
 if rel=='9to1 Models/Dulche Alpha/Dulche.Runtime.csproj':
  data=p.read_bytes();resolver=b'    <Compile Include="ModelRouteResolver.CatalogueEligibility.cs" />\n';engines=b'    <Compile Include="InferenceEngines/**/*.cs" />\n'
  assert data.count(resolver)==1 and data.count(engines)==1
  stripped=data.replace(resolver,b'',1).replace(engines,b'',1)
  assert len(stripped)==before['bytes'] and hashlib.sha256(stripped).hexdigest()==before['sha256']
  xml_comparison.append({'relative':rel,'restoredMetadataInput':before,'current':current,'exactBytes':False,
   'twoExplicitCompileItemsOnly':True,'projectPackageReferenceGraphChanged':False,'freshWholeSourceEvaluationRequired':True});continue
 assert current['sha256']==before['sha256'] and current['bytes']==before['bytes'],('Unreviewed original metadata graph XML change',rel,before,current)
 xml_comparison.append({'relative':rel,'restoredMetadataInput':before,'current':current,'exactBytes':True})
console_tests_relative='9to1 Workspace/shared/tests/Haven.Console.Tests/Haven.Console.Tests.csproj'
assert console_tests_relative in selected and console_tests_relative not in metadata_xml_authorities
console_xml_current=pin(shadow/console_tests_relative)
assert console_xml_current['bytes']==selected[console_tests_relative]['bytes'] and console_xml_current['sha256']==selected[console_tests_relative]['sha256']
xml_exceptions[console_tests_relative]={'relative':console_tests_relative,'current':console_xml_current,'sourceCarrier':pin(ticket),'newGenuinePlatformOwningProject':True,'projectPackageReferenceGraphChanged':True,'normalOwningRestoreRequired':True,'freshWholeSourceEvaluationRequired':True}
metadata_graph_restore_roots=[('HavenOS.Home.Tests',home_tests_relative),('Haven.Console.Tests',console_tests_relative)]
# Restore the last actual metadata bytes to the SAME logical project identity.
last={r['original']['path']:r for r in json.loads(gzip.decompress(metadata_custody.read_bytes()))['archives']}
for logical,row in last.items():
 p=pathlib.Path(logical);assert p.is_relative_to(scope/'metadata');a=pathlib.Path(row['archive']['path']);assert pin(a)==row['archive'];enroll(a)
 raw=gzip.decompress(a.read_bytes());assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
 p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw);os.chmod(p,row['mode']&0o7777);os.utime(p,ns=(row['mtime_ns'],row['mtime_ns']))
 restored.append({'original':pin(p),'archive':row['archive'],'historicalPhysicalIdentityReused':False});archive_by_sha[row['original']['sha256']]=row['archive']
# Three missing genuine test-root metadata restorations already completed normally.
additional_metadata=pathlib.Path('/tmp/astra-framework-owning-additional-metadata-20261006-04b/ADDITIONAL-OWNING-METADATA-RECEIPT04.json.gz')
assert pin(additional_metadata)['sha256']=='48f9e8ff1c6be233a3908429ef51f291b1d3260c2f0ef77556b59850a7039ad8'
enroll(additional_metadata);additional=json.loads(gzip.decompress(additional_metadata.read_bytes()));assert additional['exitCode']==0 and not additional['changedXmlInputs']
for row in additional['archives']:
 p=pathlib.Path(row['original']['path']);assert p.is_relative_to(scope/'metadata');a=pathlib.Path(row['archive']['path']);assert pin(a)==row['archive'];enroll(a)
 raw=gzip.decompress(a.read_bytes());assert len(raw)==row['original']['bytes'] and hashlib.sha256(raw).hexdigest()==row['original']['sha256']
 p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw);os.chmod(p,row['mode']&0o7777);os.utime(p,ns=(row['mtime_ns'],row['mtime_ns']))
 restored.append({'original':pin(p),'archive':row['archive'],'actualAdditionalRestoreReceipt':pin(additional_metadata),'historicalPhysicalIdentityReused':False});archive_by_sha[row['original']['sha256']]=row['archive']
# Reuse only the exact actual C28 generated metadata after its successful changed-graph
# Restore. All 359 current project/import XML authorities and retained package bodies
# remain exact except the single fully reviewed Desktop.Tests Compile link. Old metadata is a seed until genuine owning Restore; no changed-source enrollment comes from that seed.
historical_c28_metadata_receipt=pathlib.Path('/tmp/astra-a4-coherent-owning-compiler28-20261006-08/CHECKPOINT28-COHERENT-OWNING-COMPILER-RECEIPT01.json.gz')
assert pin(historical_c28_metadata_receipt)['sha256']=='0724e088e836ef67095cb7f115d664229e37aba819b8b7a71052174b02e2186e';enroll(historical_c28_metadata_receipt)
historical_c28_metadata=json.loads(gzip.decompress(historical_c28_metadata_receipt.read_bytes()))
latest_metadata_receipt=pathlib.Path('/tmp/astra-a4-coherent-owning-compiler31-20261006-13/CHECKPOINT31-COHERENT-OWNING-COMPILER-RECEIPT01.json.gz')
assert pin(latest_metadata_receipt)['sha256']=='20b6306411015463c46206d0b19d271ea12ce54150a3b3ea6a4ddcb3b7c6e58c';enroll(latest_metadata_receipt)
latest_metadata=json.loads(gzip.decompress(latest_metadata_receipt.read_bytes()))
assert latest_metadata['headAtCapture']==latest_metadata['headAtCompletion']=='36f1f6d20a89bbaed8307c75efe77da7b855abab' and latest_metadata['inputsUnchangedAtCompletion']
assert latest_metadata['exitCode']==1 and next(x for x in latest_metadata['stages'] if x['stage']=='Dulche.Runtime-compiler01')['exitCode']==1
# C31 is an actual failed Dulche interval; its separate normal owning metadata Restore succeeded.
assert any(x['stage']=='Haven.Desktop.Tests-reviewed-graph-cache-only-restore01' and x['exitCode']==0 for x in latest_metadata['stages'])
latest_xml={x['path'].split('/source-root/',1)[1]:x for x in latest_metadata['inputs'] if '/source-root/' in x['path'] and x['path'].endswith(('.csproj','.props','.targets'))}
assert len(latest_xml)==359
latest_xml_exact_count=0;latest_xml_reviewed_non_graph_count=0
for relative,before in latest_xml.items():
 current=pin(shadow/relative)
 if relative==external_home_relative:
  assert {k:before[k] for k in ('bytes','sha256')}==external_home_before and {k:current[k] for k in ('bytes','sha256')}==external_home_after
  current_body=(shadow/relative).read_bytes();assert current_body.count(external_home_content_after)==current_body.count(external_home_icon)==1
  inverse_body=current_body.replace(external_home_content_after,external_home_content_before,1).replace(external_home_icon,b'',1)
  assert len(inverse_body)==before['bytes'] and hashlib.sha256(inverse_body).hexdigest()==before['sha256']
  assert xml_exceptions[relative]['exactContentCopyToPublishAndExistingIconOnly'] is True and xml_exceptions[relative]['projectPackageReferenceGraphChanged'] is False and xml_exceptions[relative]['normalOwningRestoreRequired'] is False
  latest_xml_reviewed_non_graph_count+=1;del current_body,inverse_body
 else:
  assert current['bytes']==before['bytes'] and current['sha256']==before['sha256'],('Actual C31 restored graph XML changed; new owning restore required',relative)
  latest_xml_exact_count+=1
assert latest_xml_exact_count==358 and latest_xml_reviewed_non_graph_count==1 and latest_xml_exact_count+latest_xml_reviewed_non_graph_count==359
xml_exceptions[external_home_relative]['actualC31MetadataGraphAdmission']={'receipt':pin(latest_metadata_receipt),'wholeExactOtherXmlBodies':latest_xml_exact_count,'exactReviewedNonGraphXmlBodies':latest_xml_reviewed_non_graph_count,'wholeOriginalHomeXmlInverseVerifiedAgain':True,'projectPackagePropertyGraphUnchanged':True,'existing22MetadataProductsUnchanged':True,'noOutside22OwningBuildAcceptance':True}

for package in latest_metadata['packagePins']:
 for kind in ('nupkg','checksum','metadata'):
  observed=package.get(kind)
  if observed:assert pin(observed['path'])==observed;enroll(observed['path'])
latest_metadata_rows=[row for row in latest_metadata['outputArchives'] if pathlib.Path(row['original']['path']).is_relative_to(scope/'metadata')]
assert len(latest_metadata_rows)==305
for row in latest_metadata_rows:
 p=pathlib.Path(row['original']['path']);raw=read_archive(row)
 p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw);os.chmod(p,row['mode']);os.utime(p,ns=(row['mtimeNs'],row['mtimeNs']))
 assert pin(p)==row['original']
 restored.append({'original':pin(p),'archive':row['archive'],'actualAdditionalRestoreReceipt':pin(latest_metadata_receipt),'historicalPhysicalIdentityReused':False,'sameLogicalGeneratedMetadataReconstructed':True})
 archive_by_sha[row['original']['sha256']]=row['archive']
prior_home=latest_metadata['reviewedMetadataXmlExceptions'][home_tests_relative]['actualPriorOwningRestore']
assert prior_home['stage']['exitCode']==0 and prior_home['actualProject']['bytes']==pin(shadow/home_tests_relative)['bytes'] and prior_home['actualProject']['sha256']==pin(shadow/home_tests_relative)['sha256']
asset=scope/'metadata'/'HavenOS.Home.Tests'/'project.assets.json';assert pin(asset)==prior_home['actualAssets']
actual_project_refs=json.loads(asset.read_text())['project']['restore']['frameworks']['net10.0']['projectReferences']
assert actual_project_refs==prior_home['actualProjectReferences'] and str(shadow/'9to1 Workspace/Files/NativeHost/HavenOS.Files.NativeHost.csproj') in actual_project_refs
assert any(x['stage']=='Dulche.Runtime.Tests-cache-only-restore01' and x['exitCode']==0 for x in historical_c28_metadata['stages'])
assert (scope/'metadata'/'Dulche.Runtime.Tests'/'project.assets.json').is_file()
xml_exceptions[home_tests_relative]['normalOwningRestoreRequired']=False
xml_exceptions[home_tests_relative]['actualPriorOwningRestore']=prior_home
xml_exceptions[home_tests_relative]['exactGeneratedMetadataReuse']={'receipt':pin(latest_metadata_receipt),'historicalWholeGraphXmlAuthorityCount':359,'currentExactC31XmlBodies':358,'exactReviewedNonGraphExternalHomeXmlBodies':1,'originalProjectPackagePropertyGraphEqualityCount':359,'reviewedChangedOwningXmlCount':0,'actualAssets':pin(asset),'actualProjectReferences':actual_project_refs,'freshOwningSourceCompilerStillRequired':True}
prior_desktop=latest_metadata['reviewedMetadataXmlExceptions'][desktop_tests_relative]['actualOwningRestore']
assert prior_desktop['stage']['exitCode']==0 and prior_desktop['actualProject']==pin(shadow/desktop_tests_relative)
desktop_asset=scope/'metadata'/'Haven.Desktop.Tests'/'project.assets.json';assert pin(desktop_asset)==prior_desktop['actualAssets']
desktop_project=json.loads(desktop_asset.read_text())['project']['restore'];assert desktop_project['originalTargetFrameworks']==['net10.0-windows10.0.19041.0']
desktop_project_refs=desktop_project['frameworks']['net10.0-windows10.0.19041.0']['projectReferences']
expected_desktop_refs={str(shadow/'9to1 Workspace/Dev/HavenOS.Dev.csproj'),str(shadow/'9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj')}
assert desktop_project_refs==prior_desktop['actualProjectReferences'] and set(desktop_project_refs)==expected_desktop_refs
xml_exceptions[desktop_tests_relative]['normalOwningRestoreRequired']=False
xml_exceptions[desktop_tests_relative]['actualPriorOwningRestore']=prior_desktop
xml_exceptions[desktop_tests_relative]['exactGeneratedMetadataReuse']={'receipt':pin(latest_metadata_receipt),'currentWholeGraphXmlEqualityCount':358,'exactReviewedNonGraphExternalHomeXmlBodies':1,'originalProjectPackagePropertyGraphEqualityCount':359,'actualAssets':pin(desktop_asset),'actualProjectReferences':desktop_project_refs,'freshOwningSourceCompilerStillRequired':True}
metadata_graph_restore_roots=[]
products=[
 ('Haven.Core','9to1 Workspace/shared/src/Haven.Core/Haven.Core.csproj','net10.0'),
 ('Haven.Application','9to1 Workspace/shared/src/Haven.Application/Haven.Application.csproj','net10.0'),
 ('Dulche.Runtime','9to1 Models/Dulche Alpha/Dulche.Runtime.csproj','net10.0'),
 ('HavenOS.Home','9to1 Workspace/Home/HavenOS.Home.csproj','net10.0'),
 ('Haven.Infrastructure','9to1 Workspace/shared/src/Haven.Infrastructure/Haven.Infrastructure.csproj','net10.0'),
 ('HavenOS.Dev','9to1 Workspace/Dev/HavenOS.Dev.csproj','net10.0'),
 ('Haven.Browser','9to1 Workspace/shared/src/Haven.Browser/Haven.Browser.csproj','net10.0'),
 ('HavenOS.Files.CUI','9to1 Workspace/Files/CUI/HavenOS.Files.CUI.csproj','net10.0'),
 ('HavenOS.Files.NativeHost','9to1 Workspace/Files/NativeHost/HavenOS.Files.NativeHost.csproj','net10.0'),
 ('HavenOS.Files.NativeUI','9to1 Workspace/Files/NativeUI/HavenOS.Files.NativeUI.csproj','net10.0'),
 ('HavenOS.Sites','9to1 Workspace/Sites/HavenOS.Sites.csproj','net10.0'),
 ('HavenOS.Spaces','9to1 Workspace/Spaces/HavenOS.Spaces.csproj','net10.0'),
 ('Haven.PluginFixture','9to1 Workspace/shared/tests/Haven.PluginFixture/Haven.PluginFixture.csproj','net10.0'),
 ('Haven.Desktop','9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj','net10.0')]
tests=[('Dulche.Runtime.Tests','9to1 Models/Dulche Alpha/Tests/Dulche.Runtime.Tests.csproj','net10.0'),
       ('Haven.Core.Tests','9to1 Workspace/shared/tests/Haven.Core.Tests/Haven.Core.Tests.csproj','net10.0'),
       ('Haven.Infrastructure.Tests','9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj','net10.0'),
       ('Haven.Desktop.Tests','9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj','net10.0-windows10.0.19041.0'),
       ('HavenOS.Dev.Tests','9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj','net10.0'),
       ('HavenOS.Home.Tests','9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj','net10.0'),
       ('HavenOS.Spaces.Tests','9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj','net10.0'),
       ('Haven.Console.Tests','9to1 Workspace/shared/tests/Haven.Console.Tests/Haven.Console.Tests.csproj','net10.0')]
projects=products+tests;fresh={n for n,_,_ in projects};test_module_names={n for n,_,_ in tests};independent_test_failures=[];desktop_compiler_failure=[]
assert reuse_modules=={'Haven.Core','Haven.PluginFixture'}
assert not any(row['target'].startswith('9to1 Workspace/shared/src/Haven.Core/') and old_inputs.get(str(repo/row['target']),{}).get('sha256')!=row['sha256'] for row in selection['finalRows']), 'Core production changed; reuse refused.'
props=scope/'Checkpoint37CoherentOwningLayout.props';xml=ET.Element('Project');pg=ET.SubElement(xml,'PropertyGroup');ET.SubElement(pg,'_OriginalProps').text="$([MSBuild]::GetDirectoryNameOfFileAbove('$(MSBuildProjectDirectory)', 'Directory.Build.props'))"
ET.SubElement(xml,'Import',Project='$(_OriginalProps)/Directory.Build.props',Condition="'$(_OriginalProps)' != ''")
pg=ET.SubElement(xml,'PropertyGroup')
for k,v in {'ArtifactsPath':'/tmp/astra-native-host-receiving-narrow10-1d8-20261006-01/artifacts','ArtifactsProjectName':'$(MSBuildProjectName)','MSBuildProjectExtensionsPath':str(scope/'metadata')+'/$(MSBuildProjectName)/','ProjectAssetsFile':str(scope/'metadata')+'/$(MSBuildProjectName)/project.assets.json'}.items():ET.SubElement(pg,k).text=v
pg=ET.SubElement(xml,'PropertyGroup',Condition=' Or '.join("'$(MSBuildProjectName)' == '"+n+"'" for n in sorted(fresh)))
ET.SubElement(pg,'ArtifactsPath').text=str(scope/'artifacts');ET.SubElement(pg,'ArtifactsProjectName').text='$(MSBuildProjectName)-checkpoint37-coherent19'
for reuse_name in sorted(reuse_modules):
 artifact_part=pathlib.Path(reuse_originals[reuse_name][3]['output']['path']).parts
 artifact_name=artifact_part[artifact_part.index('obj')+1]
 pg=ET.SubElement(xml,'PropertyGroup',Condition="'$(MSBuildProjectName)' == '"+reuse_name+"'");ET.SubElement(pg,'ArtifactsProjectName').text=artifact_name
pg=ET.SubElement(xml,'PropertyGroup',Condition="'$(RootNormalOutputModule)' != '' And '$(RootNormalOutputModule)' == '$(MSBuildProjectName)'")
ET.SubElement(pg,'OutDir').text='$(RootNormalOutputDirectory)'
ET.SubElement(pg,'OutputPath').text='$(RootNormalOutputDirectory)'
ET.indent(xml);ET.ElementTree(xml).write(props,encoding='unicode')
config=scope/'NuGet.Config';config.write_text('<configuration><packageSources><clear /></packageSources><packageSourceMapping><clear /></packageSourceMapping><auditSources><clear /></auditSources></configuration>')
for p in [props,config]:enroll(p)
env=os.environ.copy();env.update(DOTNET_ROOT=str(sdk.parent),DOTNET_CLI_HOME=str(scope/'cli-home'),NUGET_PACKAGES=str(cache),DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true',MSBUILDDISABLENODEREUSE='1',TMPDIR=str(scope/'task-temp'),DOTNET_GCHeapHardLimit='0x50000000')
common=['-p:Configuration=Debug','-p:UseArtifactsOutput=true','-p:DirectoryBuildPropsPath='+str(props),'-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false','-p:RestoreDisableParallel=true','-p:RestoreConfigFile='+str(config),'-p:NuGetAudit=false','-p:AvaloniaBuildTasksLocation=/workspace/astra-native-vertical/framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/bin/Release/netstandard2.0/Avalonia.Build.Tasks.dll','-nologo','-nr:false','-m:1']
runtime_package_rows={};runtime_closure=None;runtime_output_failures=[];test_runtime_closures=[];test_runtime_output_failures=[]
runtime_restorer=pathlib.Path('/tmp/astra-a4-coherent-owning-compiler-preparation11-20261006/restore-exact-desktop-runtime-closure12.py')
assert pin(runtime_restorer)['sha256']=='5c711acd6ac1a0f11b0dacccf5258a44d057a4b9d7fef7f1a6711426242af29c';enroll(runtime_restorer)
test_runtime_restorer=pathlib.Path('/tmp/astra-a4-coherent-owning-compiler-preparation14-20261006/restore-exact-owning-test-runtime-closure14.py')
assert pin(test_runtime_restorer)['sha256']=='33f4fbe3cd15fd5fc6c77e0b547e87095007793d2474fbdf7588c3fb91906fb3';enroll(test_runtime_restorer)
initial=save('CHECKPOINT37-COHERENT-COMPILER-INITIAL-INPUTS01.json.gz',{'head':head,'ticket':pin(ticket),'sourceRows':selection['finalRows'],'inputs':list(known.values()),'immutableGitBlobInputs':list(immutable_git_blob_inputs.values()),'realPrivateXmlNodes':len(xml_paths),'restoredMetadata':restored,'metadataXmlComparison':xml_comparison,'metadataXmlAuthorityCount':len(metadata_xml_authorities),'reviewedMetadataXmlExceptions':xml_exceptions,'requiredActualGraphRestoreRoots':metadata_graph_restore_roots,'exactC28GeneratedMetadataReceipt':pin(historical_c28_metadata_receipt),'exactCurrentGeneratedMetadataReceipt':pin(latest_metadata_receipt),'completeInheritedPreferredSourceUnion':complete_preferred_union,'historicalCompletePreferredSourceUnion':historical_preferred_union,'exactCurrentControllingSourceOverrides':current_ticket_overrides,'explicitReviewedSourceRetirements':retirement_rows,'exactReviewedSourceConsolidation':pin(consolidation_packet),'exactReviewedCurrentCustodyCompilerCorrection':pin(current_custody_correction),'verifiedHistoricalRetirementHead':historical_retirement_head,'common':common,'projects':projects,'compilerOnly':True,'namespace':str(scope),'physicalNamespaceAdmission':pin(namespace_receipt),'currentSourceItemsRequireFreshEvaluation':True})
enroll(initial['path'])
raw_metadata_logs=scope/'raw-stage-logs';raw_metadata_logs.mkdir()
def stage_log(name):
 return raw_metadata_logs/(name+'.log')
def guarded_write_retained_blocks(path,chunks,descriptor):
 # Whole retained failure streams are written block by block. No additional
 # full-body join may exceed the conservative64MiB resident audit reserve.
 p=pathlib.Path(path);rounded=((descriptor['bytes']+4095)//4096)*4096
 before=capacity_sample('before-retained-blocks:'+str(p));capacity_measurements.append(before)
 assert p.is_absolute() and p.resolve(strict=False)==p and not p.exists() and not p.is_symlink()
 assert free(repo)-rounded>=disk_envelope_components['finalDiskSpare'],('Conserved actual workspace spare for whole failure blocks',p,rounded)
 assert before['combinedAvailableBytes']-rounded>=floor and free(p.parent)>=rounded
 with p.open('xb') as stream:
  for chunk in chunks:stream.write(chunk)
  stream.flush();os.fsync(stream.fileno())
 actual=pin(p);assert {key:actual[key] for key in ('bytes','sha256')}==descriptor
 after=capacity_sample('after-retained-blocks:'+str(p));capacity_measurements.append(after)
 assert after['combinedAvailableBytes']>=floor and free(repo)>=disk_envelope_components['finalDiskSpare']
 return actual

def audited_fixture_stage(name,cmd):
 # No ordinary log writer is opened before the observer verifies both absent
 # paths and prepares the pipes. Only this SAME Fixture stage is traced.
 assert name=='Haven.PluginFixture-normal-fixture-runtime-output01'
 assert '-p:SkipCompilerExecution=true' in cmd and '-p:UseSharedCompilation=false' in cmd and '-v:diagnostic' in cmd
 log=stage_log(name);audit_log=raw_metadata_logs/(name+'.exec-audit.log');beg=time.time()
 context=fixture_audit.launch(cmd,shadow,env,log,audit_log);child=context['child']
 live={'stage':name,'pid':child.pid,'command':cmd,'actualPopenCommand':context['actualPopenCommand'],'actualPidKind':'OWNED_STRACE_WRAPPER','log':str(log),'execAuditLog':str(audit_log),'inputReceipt':initial,'started':beg,'namespace':str(scope)}
 publication_error=None;capture_error=None;capture_exception=None
 try:guarded_write(out/'compiler.live.json',(json.dumps(live)+'\n').encode(),True)
 except Exception as cause:publication_error=repr(cause)
 print('CURRENT_CHECKPOINT_TARGETED65_STAGE_STARTED',name,child.pid,flush=True)
 try:capture=fixture_audit.capture(context)
 except fixture_audit.AuditFailure as cause:
  capture_exception=cause;capture_error=repr(cause);capture=cause.report
  # Preserve every original retained block into a whole task-owned fallback;
  # a partial primary remains in the explicit custody set below. This is a
  # failure path and can never gain natural/no-second-compiler qualification.
  for stream,chunks in cause.retained_raw_chunks.items():
   assert stream in ('diagnostic','audit')
   retained=out/(name+'.'+stream+'.unpublished-whole-fallback')
   descriptor=capture['unpublishedWholeStreams'][stream]
   actual=guarded_write_retained_blocks(retained,chunks,descriptor)
   capture['wholeDiagnosticPin' if stream=='diagnostic' else 'wholeAuditPin']=actual
   capture.setdefault('callerWholePublicationFallbackPins',{})[stream]=actual
 status=capture['actualWrapperExitCode'];assert status==child.returncode and child.poll()==status
 # Actual natural/forced process status is kept distinct from an observer,
 # publication, parser or outer pipeline fault. No status0 is manufactured.
 original_log=capture['wholeDiagnosticPin'];original_audit=capture['wholeAuditPin']
 assert original_log is not None and original_audit is not None
 assert pin(original_log['path'])==original_log and pin(original_audit['path'])==original_audit
 log_custody=custody_group([pathlib.Path(original_log['path'])],name+'-stdout01')
 audit_custody=custody_group([pathlib.Path(original_audit['path'])],name+'-whole-exec-audit01')
 retained_files=[];seen=set()
 for candidates in capture['rawPublicationAttemptPaths'].values():
  for candidate in candidates:
   path=pathlib.Path(candidate)
   if path.is_file() and str(path) not in seen:
    seen.add(str(path));retained_files.append(custody_group([path],name+'-capture-original-and-emergency01'))
 for suffix in ('.capture-failure.json','.qualify-failure.json'):
  path=pathlib.Path(capture['failureReportBase']+suffix)
  if path.is_file():retained_files.append(custody_group([path],name+'-capture-failure-report01'))
 capture_receipt=save(name+'-WHOLE-EXEC-AUDIT-CAPTURE01.json.gz',capture);enroll(capture_receipt['path'])
 row={'stage':name,'pid':child.pid,'actualPidKind':'OWNED_STRACE_WRAPPER','exitCode':status,'command':cmd,'actualPopenCommand':context['actualPopenCommand'],'log':original_log,'actualChildOutputPin':original_log,'exactWholeStdoutCustody':log_custody,'durableRawLog':original_log if pathlib.Path(original_log['path']).is_relative_to(out) else None,'rawPhysicalLifetime':'Whole original diagnostic and audit streams are retained without truncation before terminal publication. Partial primary and emergency writer artifacts remain explicitly custodied on failure.','capacityAfterStdoutCustody':capacity_sample(name+'-stdout-terminal'),'livePublicationError':publication_error,'elapsedSeconds':time.time()-beg,'fixtureExecAuditCapture':capture,'fixtureExecAuditCaptureReceipt':capture_receipt,'wholeFixtureExecAuditCustody':audit_custody,'fixtureExecAuditFailureReportCustody':retained_files,'capturePipelineFailure':capture_error,'actualNaturalSDKExitNotInferredFromRenderedCommand':True}
 stages.append(row)
 guarded_write(out/'compiler.progress.json',(json.dumps(stages,separators=(',',':'))+'\n').encode(),True)
 print('CURRENT_CHECKPOINT_TARGETED65_STAGE_TERMINAL',name,status,flush=True)
 if capture_exception is not None:raise capture_exception
 if publication_error is not None:raise RuntimeError(('Actual live receipt publication failed; same owned child joined and both streams preserved',publication_error,row))
 return status

def stage(name,cmd,fixture_exec_audit=False):
 if fixture_exec_audit:return audited_fixture_stage(name,cmd)
 log=stage_log(name);beg=time.time()
 with log.open('wb') as f:
  child=subprocess.Popen(cmd,cwd=shadow,env=env,stdout=f,stderr=subprocess.STDOUT)
  live={'stage':name,'pid':child.pid,'command':cmd,'log':str(log),'inputReceipt':initial,'started':beg,'namespace':str(scope)}
  publication_error=None
  try:guarded_write(out/'compiler.live.json',(json.dumps(live)+'\n').encode(),True)
  except Exception as e:publication_error=repr(e)
  print('CURRENT_CHECKPOINT_TARGETED65_STAGE_STARTED',name,child.pid,flush=True)
  status=child.wait();f.flush();os.fsync(f.fileno())
 original_log=pin(log)
 log_custody=custody_group([log],name+'-stdout01')
 durable_raw=None
 if not name.endswith(('-evaluation01','-references01')):
  raw=log.read_bytes();rounded=((len(raw)+4095)//4096)*4096
  if combined_free()-rounded>=floor and free(out)>=rounded:durable_raw=guarded_write(out/(name+'.log'),raw)
 row={'stage':name,'pid':child.pid,'exitCode':status,'command':cmd,'log':original_log,'actualChildOutputPin':original_log,'exactWholeStdoutCustody':log_custody,'durableRawLog':durable_raw,'rawPhysicalLifetime':'Original child stdout is owned SHM and survives until this namespace closes. Whole contents retained in exact verified custody before terminal publication; durableRawLog is physically present only when explicitly pinned here.','capacityAfterStdoutCustody':capacity_sample(name+'-stdout-terminal'),'livePublicationError':publication_error,'elapsedSeconds':time.time()-beg};stages.append(row)
 guarded_write(out/'compiler.progress.json',(json.dumps(stages,separators=(',',':'))+'\n').encode(),True);print('CURRENT_CHECKPOINT_TARGETED65_STAGE_TERMINAL',name,status,flush=True)
 if status:print(log.read_text(errors='replace')[-22000:],flush=True)
 if publication_error is not None:raise RuntimeError(('Actual live receipt publication failed; same child naturally joined and stdout preserved',publication_error,row))
 return status
archive_root=pathlib.Path('/workspace/astra-source/a4-current-targeted-owning-output-custody65/exact-gzip');archive_root.mkdir(parents=True,exist_ok=True);archive_fallback=pathlib.Path('/workspace/astra-source/a4-current-targeted-owning-output-fallback65/exact-gzip');archive_fallback.mkdir(parents=True,exist_ok=False)
def custody_group(paths,label):
 first=paths[0];raw=first.read_bytes();sha=hashlib.sha256(raw).hexdigest();st=first.stat()
 if sha not in archive_by_sha:
  data=gzip.compress(raw,9,mtime=0)
  manifest,literal_data,encoded=custody_library.make_layer(raw,resource_anchor,closed_layers=closed_layers) if len(raw)>1000000 and first.suffix=='.dll' else (None,None,None)
  destination=max([archive_root,archive_fallback],key=free)
  if manifest is not None and len(data)-len(literal_data)>1048576:
   literal_path=destination/(sha+'-literal.gz');manifest['literalArchive']=guarded_write(literal_path,literal_data)
   descriptor=destination/(sha+'-resource-subrange.json');descriptor_bytes=json.dumps(manifest,separators=(',',':')).encode();guarded_write(descriptor,descriptor_bytes)
   stored=custody_library.read_descriptor(descriptor)
   assert custody_library.decode_layer(stored,closed_layers)==raw and custody_library.encoded_layer(stored,closed_layers)==data==encoded
   archive_by_sha[sha]=pin(descriptor);archive_encodings[sha]=custody_library.SCHEMA
  else:
   a=destination/(sha+'.gz');guarded_write(a,data)
   assert gzip.decompress(a.read_bytes())==raw;archive_by_sha[sha]=pin(a)
 row={'stage':label,'original':pin(first),'originalPaths':[str(p) for p in paths],'archive':archive_by_sha[sha],'mode':st.st_mode&0o7777,'mtimeNs':st.st_mtime_ns,'device':st.st_dev,'inode':st.st_ino,'nlink':st.st_nlink,'decodedVerified':True,'inverse':'Decode exact gzip, verify whole length/SHA, restore first path mode/mtime and same-device hardlink aliases in a NEW authorized namespace. Historical physical inode/lifetime is observation only.'}
 if sha in archive_encodings:row['encoding']=archive_encodings[sha];row['archiveAvailability']='Actual descriptor with exact physical literal gzip plus resource anchor; FULL newly generated raw and encoded gzip reconstruction independently verified before acceptance.'
 elif row['archive']['path'] in archive_transports:row['archiveTransport']=archive_transports[row['archive']['path']]
 archives.append(row);return row
def current_parent_compiler_origin(module,compiled):
 origin_body=component_basis;origin_descriptor=pin(component_basis_path);chain=[];seen=set()
 while True:
  assert origin_descriptor['path'] not in seen;seen.add(origin_descriptor['path']);assert pin(origin_descriptor['path'])==origin_descriptor;enroll(origin_descriptor['path'])
  original=next(o for o in origin_body['outputs'] if o['module']==module)
  assert all(original.get(k)==compiled.get(k) for k in ('output','target','pdb','reference')) and original['compiledSourceHead']==compiled['compiledSourceHead']
  failed_qualified=origin_descriptor==pin(failed55_basis_path)
  if origin_body['exitCode']!=0:
   assert failed_qualified and origin_body['exitCode']==1 and failed55_closed['currentCompiledOutputPins']==origin_body['outputs']
  chain.append({'receipt':origin_descriptor,'originalReceiptNaturalExitCode':origin_body['exitCode'],'compiledSourceHead':original['compiledSourceHead'],'compiledInOriginalInterval':original['compiledInCurrentInterval'],'failed55IndividualComponentCustody':pin(failed55_closed_path) if failed_qualified else None,'failedIntervalNotClaimedFullSuccessful':failed_qualified})
  if original['compiledInCurrentInterval']:break
  origin_descriptor=original['reusedFromActualCompilerReceipt'];assert pin(origin_descriptor['path'])==origin_descriptor
  origin_body=json.loads(gzip.decompress(pathlib.Path(origin_descriptor['path']).read_bytes()));assert origin_body['inputsUnchangedAtCompletion']
 stage_original=next(s for s in origin_body['stages'] if s['stage']==module+'-compiler01')
 assert stage_original['exitCode']==0 and origin_body['headAtCapture']==origin_body['headAtCompletion']==compiled['compiledSourceHead']
 return {'currentCompilerInvoked':False,'actualOriginalNaturalExitCode':0,'actualOriginalCompilerStage':stage_original,'actualOriginalCompilerReceipt':origin_descriptor,'exactQualifiedCompilerReceiptChain':chain,'actualCurrentParentSuccessfulCompilerReceipt':pin(component_basis_path),'actualCurrentParentFullClosedReadback':pin(component_closed_path),'actualCurrentParentNaturalExitCode':0,'originalC43SuccessfulInterval':pin(prior_path),'preservedFailed55IndividualComponentCustody':pin(failed55_closed_path),'compiledSourceHead':compiled['compiledSourceHead'],'completeCurrentSourceResourceCscRefAnalyzerEquivalence':True}

def preserve_actual_desktop_runtime_output(project,flags,plan):
 global runtime_closure
 import zipfile
 compiled=next(o for o in outputs if o['module']=='Haven.Desktop')
 assert compiled['compiledInCurrentInterval'] and compiled['compiledSourceHead']==head and plan['compilerExitCode']==0
 plan['actualRuntimeCompilerSuccessProvenance']={'currentCompilerInvoked':True,'actualCurrentNaturalExitCode':0,'actualCurrentCompilerStage':next(s for s in stages if s['stage']=='Haven.Desktop-compiler01'),'compiledSourceHead':head,'currentParentSuccessfulCompilerReceipt':pin(component_basis_path),'currentParentFullClosedReadback':pin(component_closed_path)}
 originals=[o[k] for o in outputs for k in ('output','target','pdb','reference') if o.get(k)]
 for before in originals:assert pin(before['path'])==before
 targets='GenerateBuildDependencyFile;GenerateBuildRuntimeConfigurationFiles;CopyFilesToOutputDirectory'
 flags,target_dir,normal_layout=prepare_durable_normal_layout(plan['module'],project,flags,plan)
 status=stage('Haven.Desktop-normal-runtime-output01',[str(sdk),'msbuild',str(shadow/project),*flags,'-t:'+targets,'-v:normal'])
 record_durable_normal_stage('Haven.Desktop')
 if status:
  runtime_output_failures.append({'stage':stages[-1],'naturalExitCode':status,'qualification':'Fresh Csc output may remain valid but runnable runtime closure failed; no executable-ready claim.'});return
 for before in originals:assert pin(before['path'])==before,('Runtime output stage changed genuine compiled product',before)
 runtime_log=stage_log('Haven.Desktop-normal-runtime-output01').read_text(errors='replace')
 assert 'Compilation request Haven.Desktop' not in runtime_log and not re.search(r'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\"]|$)',runtime_log), 'Runtime output target must not recompile Desktop.'
 assert target_dir==normal_root/plan['module'] and target_dir.resolve()==target_dir
 for name in ('Haven.dll','Haven.deps.json','Haven.runtimeconfig.json'):assert (target_dir/name).is_file(),('Missing genuine normal runtime output',name)
 package_index={};containers={}
 for package in package_pins:
  container=package.get('nupkg')
  if not container or container['path'] in containers:continue
  assert pin(container['path'])==container;enroll(container['path']);containers[container['path']]=container
  with zipfile.ZipFile(container['path']) as z:
   for item in z.infolist():
    if not item.is_dir():package_index.setdefault((pathlib.PurePosixPath(item.filename).name,item.file_size),[]).append((container,item.filename,item.CRC))
 files=[]
 for path in sorted(target_dir.rglob('*')):
  if not path.is_file():continue
  assert not path.is_symlink() and path.resolve()==path,('Normal runtime output must be direct owned physical file',path)
  original=pin(path);stat=path.stat();restoration=None
  for container,member,crc in package_index.get((path.name,original['bytes']),[]):
   if not exact_package_member_matches(container,member,crc,original,path):continue
   restoration={'kind':'EXACT_PACKAGE_NUPKG_MEMBER','container':container,'member':member,'memberBytes':original['bytes'],'memberCRC32':crc,'memberSha256':original['sha256'],'wholeBytesComparedWithActualCopiedFile':True,'streamingWholeComparison':True};break
  if restoration is None:
   row=custody_group([path],'Haven.Desktop-normal-runtime-output01')
   assert read_archive(row)==path.read_bytes();restoration={'kind':'EXACT_COMPILER_CUSTODY_ARCHIVE','row':row}
  item={'relative':str(path.relative_to(target_dir)),'original':original,'mode':stat.st_mode,'mtimeNs':stat.st_mtime_ns,'device':stat.st_dev,'inode':stat.st_ino,'nlink':stat.st_nlink,'restoration':restoration}
  files.append(item)
  if restoration['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':runtime_package_rows[str(path)]=item
  # Runtime copied bytes are immutable final evidence, independently terminal-rehashed.
  enroll(path)
 assert any(x['relative']=='Haven.dll' and x['original']['sha256']==next(o['target']['sha256'] for o in outputs if o['module']=='Haven.Desktop') for x in files)
 preserve_genuine_normal_prerequisite_aliases('Haven.Desktop',target_dir,plan)
 value={'status':'ACTUAL_NORMAL_DESKTOP_RUNTIME_OUTPUT_CLOSURE_PASS','head':head,'ticket':pin(ticket),'normalOwningProject':plan['canonicalProject'],'normalTargetFramework':plan['targetFramework'],'actualRuntimeStage':stages[-1],'actualCompilerSuccessProvenance':plan.get('actualRuntimeCompilerSuccessProvenance',{'currentCompilerInvoked':next(o for o in outputs if o['module']=='Haven.Desktop')['compiledInCurrentInterval'],'compiledSourceHead':next(o for o in outputs if o['module']=='Haven.Desktop')['compiledSourceHead']}),'actualOriginalOutputDirectory':str(target_dir),'actualDurableNormalLayout':normal_layout,'files':files,'fileCount':len(files),'wholeCopiedBytes':sum(x['original']['bytes'] for x in files),'packageMemberFileCount':len(runtime_package_rows),'allActualCompiledProductIdentitiesUnchanged':True,'noSecondCompiler':True,'dotnet':pin(sdk),'exactResourceCustodyHelper':pin(custody_helper),'closedLayerArchiveRegistry':pin(closed_layer_registry),'restorer':pin(runtime_restorer),'launchAfterExactRestore':[str(sdk),'<restored-absolute-root>/Haven.dll','--local-task-console','--data-directory','<private-absolute-data-root>'],'qualification':'Actual original normal deps/runtimeconfig/CopyLocal target outputs, complete whole-byte restoration source for every copied file. Package members reference entire retained pinned NUPKG containers and are full compared, avoiding duplicate package/native body storage. No model/server/execution/native authority or Task acceptance.'}
 runtime_closure=save('Haven.Desktop-NORMAL-RUNTIME-OUTPUT-CLOSURE01.json.gz',value)
 print('CURRENT_CHECKPOINT_TARGETED65_RUNTIME_OUTPUT_CLOSURE',json.dumps({'manifest':runtime_closure,'files':len(files),'copiedBytes':value['wholeCopiedBytes'],'packageFiles':len(runtime_package_rows)}),flush=True)

def preserve_actual_owning_test_runtime_output(module,project,flags,plan):
 import zipfile
 assert module in test_module_names
 compiled=next(o for o in outputs if o['module']==module)
 if compiled['compiledInCurrentInterval']:
  assert plan['compilerExitCode']==0 and compiled['compiledSourceHead']==head
  compiler_success={'currentCompilerInvoked':True,'actualCurrentNaturalExitCode':plan['compilerExitCode'],'compiledSourceHead':head}
 else:
  assert plan['reusedActualSuccessfulCompiler']==pin(component_basis_path) and compiled['reusedFromActualCompilerReceipt']==pin(component_basis_path) and compiled['reusedFromCurrentParentFullClosedReadback']==pin(component_closed_path)
  assert compiled['completeSourceItemResourceOrderEqual'] and compiled['actualCompilerReferenceAndAnalyzerBytesEqual']
  assert not plan['targetedCompilerDecision']['freshCscRequired'] and plan['targetedCompilerDecision']['actualCompilerRefsAndAnalyzersWholeEqual']
  compiler_success=current_parent_compiler_origin(module,compiled)
 plan['actualRuntimeCompilerSuccessProvenance']=compiler_success
 originals=[o[k] for o in outputs for k in ('output','target','pdb','reference') if o.get(k)]
 for before in originals:assert pin(before['path'])==before
 label=module+'-normal-test-runtime-output01'
 targets='GenerateBuildDependencyFile;GenerateBuildRuntimeConfigurationFiles;CopyFilesToOutputDirectory'
 flags,target_dir,normal_layout=prepare_durable_normal_layout(plan['module'],project,flags,plan)
 status=stage(label,[str(sdk),'msbuild',str(shadow/project),*flags,'-t:'+targets,'-v:normal'])
 record_durable_normal_stage(module)
 if status:
  test_runtime_output_failures.append({'module':module,'stage':stages[-1],'naturalExitCode':status,'qualification':'Genuine owning test compiler success is verified; normal runnable test output closure failed and remains unavailable.'});return
 for before in originals:assert pin(before['path'])==before,('Test runtime output changed an actual compiled product',module,before)
 runtime_log=stage_log(label).read_text(errors='replace')
 assert 'Compilation request ' not in runtime_log and not re.search(r'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\"]|$)',runtime_log),('Normal test runtime targets must not recompile',module)
 assert target_dir==normal_root/plan['module'] and target_dir.resolve()==target_dir
 assembly_name=plan['properties']['AssemblyName'];assert pathlib.PurePosixPath(assembly_name).name==assembly_name
 required=[assembly_name+'.dll',assembly_name+'.deps.json',assembly_name+'.runtimeconfig.json','testhost.dll']
 for name in required:assert (target_dir/name).is_file(),('Missing genuine normal owning test runtime output',module,name)
 assert pin(target_dir/(assembly_name+'.dll'))['sha256']==compiled['output']['sha256']
 package_index={};containers={}
 for package in package_pins:
  container=package.get('nupkg')
  if not container or container['path'] in containers:continue
  assert pin(container['path'])==container;enroll(container['path']);containers[container['path']]=container
  with zipfile.ZipFile(container['path']) as z:
   for item in z.infolist():
    if not item.is_dir():package_index.setdefault((pathlib.PurePosixPath(item.filename).name,item.file_size),[]).append((container,item.filename,item.CRC))
 files=[]
 for path in sorted(target_dir.rglob('*')):
  if not path.is_file():continue
  assert not path.is_symlink() and path.resolve()==path,('Normal test runtime output must be direct owned physical file',module,path)
  original=pin(path);stat=path.stat();restoration=None
  for container,member,crc in package_index.get((path.name,original['bytes']),[]):
   if not exact_package_member_matches(container,member,crc,original,path):continue
   restoration={'kind':'EXACT_PACKAGE_NUPKG_MEMBER','container':container,'member':member,'memberBytes':original['bytes'],'memberCRC32':crc,'memberSha256':original['sha256'],'wholeBytesComparedWithActualCopiedFile':True,'streamingWholeComparison':True};break
  if restoration is None:
   row=custody_group([path],label);assert read_archive(row)==path.read_bytes();restoration={'kind':'EXACT_COMPILER_CUSTODY_ARCHIVE','row':row}
  item={'relative':str(path.relative_to(target_dir)),'original':original,'mode':stat.st_mode,'mtimeNs':stat.st_mtime_ns,'device':stat.st_dev,'inode':stat.st_ino,'nlink':stat.st_nlink,'restoration':restoration}
  files.append(item)
  if restoration['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':runtime_package_rows[str(path)]=item
  enroll(path)
 assembly=next(x for x in files if x['relative']==assembly_name+'.dll');assert assembly['original']['sha256']==compiled['output']['sha256']
 package_count=sum(x['restoration']['kind']=='EXACT_PACKAGE_NUPKG_MEMBER' for x in files)
 value={'status':'ACTUAL_NORMAL_OWNING_TEST_RUNTIME_OUTPUT_CLOSURE_PASS','head':head,'ticket':pin(ticket),'module':module,'normalOwningProject':plan['canonicalProject'],'normalTargetFramework':plan['targetFramework'],'actualRuntimeStage':stages[-1],'actualCompilerSuccessProvenance':compiler_success,'actualOriginalOutputDirectory':str(target_dir),'actualDurableNormalLayout':normal_layout,'actualAssemblyFile':assembly_name+'.dll','actualCompiledAssembly':compiled['output'],'requiredActualRuntimeFiles':required,'files':files,'fileCount':len(files),'wholeCopiedBytes':sum(x['original']['bytes'] for x in files),'packageMemberFileCount':package_count,'allActualCompiledProductIdentitiesUnchanged':True,'noSecondCompiler':True,'dotnet':pin(sdk),'exactResourceCustodyHelper':pin(custody_helper),'closedLayerArchiveRegistry':pin(closed_layer_registry),'restorer':pin(test_runtime_restorer),'supportedRunnerAfterExactRestore':[str(sdk),'vstest','<restored-absolute-root>/'+assembly_name+'.dll','/Tests:<explicit-qualified-test-names>'],'testsExecuted':False,'testDiscoveryExecuted':False,'modelExecuted':False,'qualification':'Actual original owning test deps/runtimeconfig/testhost/CopyLocal target outputs are fully byte-pinned and restorable. Package sources are entire retained pinned NUPKGs with exact member equality; every other copied file has complete compiler custody. No discovery/test execution, platform compatibility, model authority or accepted Task is established.'}
 manifest=save(module+'-NORMAL-OWNING-TEST-RUNTIME-OUTPUT-CLOSURE01.json.gz',value)
 test_runtime_closures.append({'module':module,'manifest':manifest,'compiledAssembly':compiled['output'],'normalTargetFramework':plan['targetFramework'],'fileCount':len(files),'wholeCopiedBytes':value['wholeCopiedBytes'],'packageMemberFileCount':package_count})
 print('CURRENT_CHECKPOINT_TARGETED65_OWNING_TEST_RUNTIME_OUTPUT_CLOSURE',json.dumps(test_runtime_closures[-1]),flush=True)

def archive_stage(label):
 groups={}
 for root in [scope/'metadata',scope/'artifacts',raw_metadata_logs]:
  if not root.exists():continue
  for p in root.rglob('*'):
   if p.is_file() and not p.is_symlink():
    if str(p) in runtime_package_rows:
     assert pin(p)==runtime_package_rows[str(p)]['original'];continue
    s=p.stat();groups.setdefault((s.st_dev,s.st_ino),[]).append(p)
 rows=[custody_group(paths,label) for paths in groups.values()]
 return save(label+'-custody01.json.gz',{'stage':label,'archives':rows,'initial':initial,'exactRetainedPackageRuntimeFiles':list(runtime_package_rows.values()),'runtimeClosureManifest':runtime_closure,'owningTestRuntimeClosureManifests':test_runtime_closures})
def materialize_original_resources(obj,plan):
 inventory={};detached=[]
 for kind in ('AvaloniaResource','AvaloniaXaml'):
  for item in obj['Items'].get(kind,[]):
   dest=pathlib.Path(item.get('FullPath',item['Identity']));assert dest.is_relative_to(shadow),('Resource outside owned source namespace',dest)
   if str(dest) in inventory:
    inventory[str(dest)]['kinds'].append(kind);continue
   source=dest.resolve(strict=True);raw=source.read_bytes();before=pin(source);assert len(raw)==before['bytes']
   # Detach only task-private alias ancestors; never write through a source-directory symlink.
   relative=dest.parent.relative_to(shadow);cursor=shadow
   for part in relative.parts:
    cursor=cursor/part
    if cursor.is_symlink():
     original=cursor.resolve(strict=True);children=list(original.iterdir());cursor.unlink();cursor.mkdir()
     for child in children:(cursor/child.name).symlink_to(child,target_is_directory=child.is_dir())
     detached.append({'privateDirectory':str(cursor),'originalSourceDirectory':str(original)})
   assert all(not p.is_symlink() for p in [dest.parent,*dest.parent.parents] if p.is_relative_to(shadow))
   if dest.is_symlink():dest.unlink();dest.write_bytes(raw)
   else:assert dest.read_bytes()==raw,('Physical resource must remain whole exact original',dest)
   assert dest.resolve()==dest and dest.is_file() and not dest.is_symlink()
   actual=pin(dest);assert actual['bytes']==before['bytes'] and actual['sha256']==before['sha256']
   assert dest.lstat().st_size==len(dest.read_bytes())==len(raw)
   inventory[str(dest)]={'kinds':[kind],'source':before,'physical':actual,'fileLength':dest.lstat().st_size,'openedLength':len(raw),'exactBytesVerified':True}
   enroll(source);enroll(dest)
 plan['physicalOriginalResourceInventory']=list(inventory.values());plan['resourceAliasDirectoriesDetached']=detached
 plan['resourceMaterializationQualification']='All evaluated AvaloniaResource AND AvaloniaXaml named files have original canonical bytes at the SAME logical source paths, physical regular files and physical directory ancestors before maintained PrepareResources. File length/open length/whole SHA verified. No original source/project/task alteration or resource blob substitution.'
 print('CURRENT_CHECKPOINT_TARGETED65_PHYSICAL_RESOURCES',plan['module'],len(inventory),sum(x['openedLength'] for x in inventory.values()),flush=True)
def validate_generated_resource_index(obj,plan):
 import struct
 resource=pathlib.Path(plan['properties']['IntermediateOutputPath'])/'Avalonia'/'resources'
 raw=resource.read_bytes();index_end=4+struct.unpack_from('<i',raw,0)[0]
 version,count=struct.unpack_from('<ii',raw,4);assert version==2 and 0<count<10000
 cursor=12;entries={}
 for _ in range(count):
  size=0;shift=0
  while True:
   byte=raw[cursor];cursor+=1;size|=(byte&127)<<shift
   if not byte&128:break
   shift+=7;assert shift<35
  name=raw[cursor:cursor+size].decode('utf-8');cursor+=size
  offset,length=struct.unpack_from('<ii',raw,cursor);cursor+=8
  assert name not in entries and offset>=0 and length>=0 and index_end+offset+length<=len(raw)
  entries[name]=(offset,length)
 assert cursor==index_end
 expected={}
 for kind in ('AvaloniaResource','AvaloniaXaml'):
  for item in obj['Items'].get(kind,[]):
   name='/'+(item.get('Link') or item['Identity']).replace('\\','/')
   p=pathlib.Path(item.get('FullPath',item['Identity']));source=pin(p)
   if name in expected:assert expected[name]==source
   expected[name]=source
 assert set(entries)==set(expected)|{'/!AvaloniaResourceXamlInfo'},('Unexpected generated resource names',set(entries)^set(expected))
 checked=[]
 for name,source in expected.items():
  offset,length=entries[name];body=raw[index_end+offset:index_end+offset+length]
  assert length==source['bytes'] and hashlib.sha256(body).hexdigest()==source['sha256'],('Fresh resource slice must equal SAME original source',name,length,source)
  if name.endswith(('.axaml','.xaml','.paml')):ET.fromstring(body)
  checked.append({'name':name,'offset':offset,'bytes':length,'sha256':source['sha256'],'sourcePath':source['path'],'exactOriginalSlice':True})
 plan['actualFreshResourceIndex']={'resource':pin(resource),'version':version,'entryCount':count,'indexEnd':index_end,'namedSources':checked,'wholeNamedSlicesEqualSourceBytes':True}
 enroll(resource,False)
 print('CURRENT_CHECKPOINT_TARGETED65_FRESH_INDEX_EXACT',plan['module'],len(checked),count,flush=True)
def evaluated(module,project,tfm):
 flags=[*common,'-p:BuildProjectReferences=false','-p:TargetFramework='+tfm]
 command=[str(sdk),'msbuild',str(shadow/project),*flags,'-getProperty:TargetPath,IntermediateOutputPath,ProjectAssetsFile,TargetRefPath,AssemblyName,TargetFramework,TargetFrameworks,OutputType,MSBuildAllProjects'+(',RootNamespace,MSBuildProjectName' if module=='Haven.Console.Tests' else ''),'-getItem:Compile,EmbeddedResource,AvaloniaResource,AvaloniaXaml,AdditionalFiles,ProjectReference,PackageReference,Analyzer']
 status=stage(module+'-evaluation01',command);assert status==0,('Evaluation failed',module,status)
 obj=json.loads(stage_log(module+'-evaluation01').read_text());ep=save(module+'-evaluation01.json.gz',obj);enroll(ep['path']);physical=[];absent=[]
 for kind,items in obj['Items'].items():
  for item in items:
   p=pathlib.Path(item.get('FullPath',item['Identity']))
   if p.is_file():
    logical=enroll(p,not p.is_relative_to(scope/'artifacts'));physical.append({'kind':kind,'logical':logical,'resolved':pin(p.resolve())})
    if kind=='Compile':
     resolved=p.resolve()
     if resolved.is_relative_to(repo):source_membership.setdefault(str(resolved.relative_to(repo)),set()).add(module)
     elif p.is_relative_to(shadow):source_membership.setdefault(str(p.relative_to(shadow)),set()).add(module)
   elif kind not in ['PackageReference']:absent.append({'kind':kind,'path':str(p)})
 for imp in obj['Properties'].get('MSBuildAllProjects','').split(';'):
  if imp and pathlib.Path(imp).is_file():enroll(imp,not pathlib.Path(imp).is_relative_to(scope/'artifacts') and not pathlib.Path(imp).is_relative_to(scope/'metadata'))
 asset=pathlib.Path(obj['Properties']['ProjectAssetsFile']);enroll(asset,False)
 plan={'module':module,'project':pin(shadow/project),'canonicalProject':pin(repo/project),'evaluation':ep,'properties':obj['Properties'],'sourceCount':len(obj['Items']['Compile']),'physicalItems':physical,'missingItems':absent,'targetFramework':tfm};plans.append(plan)
 assert obj['Properties']['TargetFramework']==tfm,plan['properties'];return flags,plan
# Targeted current-owner continuation: original21 project boundaries remain.
# All required compilers precede CopyLocal. No test/console/model execution.
import zipfile,shutil
prior_path=pathlib.Path('/workspace/astra-source/a4-c43-targeted-owning-compiler46/output/C43-TARGETED-OWNING-COMPILER46-RECEIPT.json.gz')
prior_closed_path=pathlib.Path('/workspace/astra-source/a4-c43-targeted-owning-compiler-preparation46/C43-TARGETED46-FULL-CLOSED-CUSTODY-READBACK01.json')
symbol_path=pathlib.Path('/workspace/astra-source/a4-c37-exact-runtime-symbol-restoration01/C37-EXACT-SEVEN-RUNTIME-SYMBOLS-RESTORATION-RECEIPT01.json')
assert pin(prior_path)=={'path':str(prior_path),'bytes':3286504,'sha256':'826d3fe10ddc5b32933c9240c819a5966db5d20aa585f402e9de6d2adee00a6f'}
assert pin(prior_closed_path)=={'path':str(prior_closed_path),'bytes':3244130,'sha256':'988d3f50e99eabd9710ddc1d7161e29a5f2005ddb1f3898a959026d76a79dff9'}
assert pin(symbol_path)=={'path':str(symbol_path),'bytes':5878,'sha256':'0aead1b5fc8888024dfce911f37bd37bd69075d989f792f97f96a79713f72730'}
prior=json.loads(gzip.decompress(prior_path.read_bytes()));prior_closed=json.loads(prior_closed_path.read_bytes());symbols=json.loads(symbol_path.read_bytes())
compiled_parent_head='f7aa74440b43858ed0d5a488576c2d5de566ae46'
assert prior['headAtCapture']==prior['headAtCompletion']==prior_closed['head']==compiled_parent_head
assert prior['exitCode']==0 and prior['inputsUnchangedAtCompletion'] and prior_closed['inputsUnchanged'] and prior_closed['activeSDKNone'] and prior_closed['activeSDK'] is None
assert len(prior['outputs'])==21 and not prior['independentTestCompilerFailures'] and not prior['desktopCompilerFailures'] and not prior['desktopRuntimeOutputFailures']
assert not prior['owningTestRuntimeOutputFailures']
assert symbols['head']=='cacd6375bb6dfc99340a8d9bac878a2994a5ea19' and symbols['onlySevenSymbolsRestored'] and len(symbols['symbols'])==7
for s in symbols['symbols']:assert pin(s['path'])=={k:s[k] for k in ('path','bytes','sha256')};enroll(s['path'])
for path in (prior_path,prior_closed_path,symbol_path):enroll(path)
# C43 remains the genuine successful interval. The failed55 receipt is only
# an individually qualified component basis, never an exit0/fullclosed parent.
component_basis_path=pathlib.Path('/workspace/astra-source/a4-c44-targeted-owning-compiler55/output/C44-TARGETED-OWNING-COMPILER55-RECEIPT.json.gz')
component_closed_path=pathlib.Path('/workspace/astra-source/a4-c44-actual55-failure-repair57/C44-ACTUAL55-FAILED-INTERVAL-FULL-CUSTODY-READBACK57.json')
component_peer_path=pathlib.Path('/workspace/astra-source/lifecycle-peer-sol61u51/C44-FAILED-INTERVAL-CUSTODY57C-INDEPENDENT-QUALIFIED-SOURCE-PEER06.json')
assert pin(component_basis_path)=={'path':str(component_basis_path),'bytes':3748669,'sha256':'5fd3ca18e745fa678b7abcf9bf2363117ceb56a00fe1284cd611d49fcff6a451'}
assert pin(component_closed_path)=={'path':str(component_closed_path),'bytes':2640235,'sha256':'97a3f25fa0ab0cacd7dd23ee443494d956767d6b850a4e48d6f68965766cfce3'}
assert pin(component_peer_path)=={'path':str(component_peer_path),'bytes':9942,'sha256':'baa420fc725570d11c9633dafd5dc79b75cc8e427acebb04c5af862d1d8fe6d0'}
component_basis=json.loads(gzip.decompress(component_basis_path.read_bytes()));component_closed=json.loads(component_closed_path.read_bytes());component_peer=json.loads(component_peer_path.read_bytes())
assert component_basis['headAtCapture']==component_basis['headAtCompletion']==component_closed['head']==inherited_c44['afterHead']
assert component_basis['exitCode']==component_closed['originalNaturalExitCode']==1 and component_basis['status']=='ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED'
assert component_closed['status']=='ACTUAL_C44_TARGETED55_FAILURE_FULL_CUSTODY_READBACK_PASS_NO_COMPILER_RUNTIME_ACCEPTANCE' and component_closed['originalReceipt']==pin(component_basis_path)
assert component_closed['activeSDKNone'] and component_closed['inputsUnchanged'] and component_basis['inputsUnchangedAtCompletion']
assert component_closed['failureQualification']['fullCompilerIntervalAccepted'] is False and component_closed['failureQualification']['runtimeReady'] is False
assert component_basis['actualC43ParentCompilerReceipt']==pin(prior_path) and component_basis['actualC43FullClosedReadback']==pin(prior_closed_path)
assert component_basis['ticket']==pin(inherited_c44_ticket) and inherited_c45['actual55FailedIntervalCustody']==pin(component_closed_path)
assert component_closed['currentCompiledOutputPins']==component_basis['outputs'] and len(component_basis['outputs'])==21 and not component_closed['actualNormalRuntimeClosures']
assert component_peer['status']=='QUALIFIED_INDEPENDENT_SOURCE_PASS_C44_FAILED_INTERVAL_CUSTODY57C_RUNTIME_READBACK_UNEXECUTED'
for path in (component_basis_path,component_closed_path,component_peer_path):enroll(path)
component_final_pin=component_basis['finalCustody'];assert pin(component_final_pin['path'])==component_final_pin;enroll(component_final_pin['path'])
component_final=json.loads(gzip.decompress(pathlib.Path(component_final_pin['path']).read_bytes()))
assert component_final['stage']=='C44-targeted55-final' and all(row in component_basis['outputArchives'] for row in component_final['archives'])
old_plans={p['module']:p for p in component_basis['modulePlans'] if p['module']!='Haven.Console.Tests'}
old_outputs={o['module']:o for o in component_basis['outputs']}
assert len(old_plans)==len(old_outputs)==21 and {o['module'] for o in component_closed['individuallySuccessfulComponentCompilerReadbacks']}==set(component_basis['actualFreshCscOwners'])
failed55_basis_path=component_basis_path;failed55_closed_path=component_closed_path;failed55_peer_path=component_peer_path
failed55_basis=component_basis;failed55_closed=component_closed
component_basis_path=pathlib.Path('/workspace/astra-source/a4-c45-targeted-owning-compiler59/output/C45-TARGETED-OWNING-COMPILER59-RECEIPT.json.gz')
component_closed_path=pathlib.Path('/workspace/astra-source/c45-closed-verifier-sol61u59/C45-SELECTIVE59-FULL-CLOSED-CUSTODY-READBACK01.json')
component_peer_path=pathlib.Path('/workspace/astra-source/lifecycle-peer-sol61u51/C45-SELECTIVE-COMPONENT59-INDEPENDENT-QUALIFIED-STRICT-RECEIVING-SOURCE-PEER14.json')
assert pin(component_basis_path)=={'path':str(component_basis_path),'bytes':3152001,'sha256':'59d56e8b0b5a922b07d8380c6097c741bb55f401d09b73ff3647bef087f46b70'}
assert pin(component_closed_path)=={'path':str(component_closed_path),'bytes':11618324,'sha256':'7f218c5318a17c79aa0b013c5b8adc3129ab207abf9a5d0dbe96b3c2a9d2d802'}
assert pin(component_peer_path)=={'path':str(component_peer_path),'bytes':31016,'sha256':'52527138bd69500b45bc09ce73be6c0105287e5e7b79031a70b9a3e5c333e582'}
component_basis=json.loads(gzip.decompress(component_basis_path.read_bytes()));component_closed=json.loads(component_closed_path.read_bytes());component_peer=json.loads(component_peer_path.read_bytes())
assert component_basis['headAtCapture']==component_basis['headAtCompletion']==component_closed['head']==inherited_c45['afterHead']
assert component_basis['exitCode']==component_closed['originalNaturalExitCode']==0 and component_basis['status']=='ACTUAL_TARGETED_OWNING_COMPILER_PASS'
assert component_closed['status']=='ACTUAL_C45_TARGETED59_FULL_CLOSED_CUSTODY_READBACK_PASS' and component_closed['currentCompilerReceipt']==component_closed['originalReceipt']==pin(component_basis_path)
assert component_closed['activeSDKNone'] and component_closed['activeSDK'] is None and component_closed['modelOrConsoleNone'] and component_closed['inputsUnchanged'] and component_basis['inputsUnchangedAtCompletion']
assert component_basis['ticket']==component_closed['ticket']==pin(inherited_c45_ticket) and component_closed['currentCompiledOutputPins']==component_basis['outputs']
assert len(component_basis['outputs'])==len(component_basis['modulePlans'])==22 and not any(component_basis[k] for k in ('independentTestCompilerFailures','desktopCompilerFailures','desktopRuntimeOutputFailures','owningTestRuntimeOutputFailures'))
assert component_basis['actualFailedC44ComponentBasis']==pin(failed55_basis_path) and component_basis['actualFailedC44ComponentCustody']==pin(failed55_closed_path)
assert component_basis['actualC43ParentCompilerReceipt']==pin(prior_path) and component_basis['actualC43FullClosedReadback']==pin(prior_closed_path)
assert component_peer['status']=='QUALIFIED_INDEPENDENT_SOURCE_PASS_C45_SELECTIVE_COMPONENT59_RUNTIME_UNEXECUTED'
for path in (component_basis_path,component_closed_path,component_peer_path):enroll(path)
for key in ('successClosedVerifier','successClosedSourceProof','exactRootInputBinding'):
 descriptor=component_closed[key];assert pin(descriptor['path'])==descriptor;enroll(descriptor['path'])
component_final_pin=component_basis['finalCustody'];assert pin(component_final_pin['path'])==component_final_pin;enroll(component_final_pin['path'])
component_final=json.loads(gzip.decompress(pathlib.Path(component_final_pin['path']).read_bytes()))
assert component_final['stage']=='C45-targeted59-final' and all(row in component_basis['outputArchives'] for row in component_final['archives'])
old_plans={p['module']:p for p in component_basis['modulePlans']};old_outputs={o['module']:o for o in component_basis['outputs']}
assert set(old_plans)==set(old_outputs)=={n for n,_,_ in projects} and len(old_outputs)==22
mandatory_fresh={'Haven.Application','HavenOS.Dev','Haven.Desktop','Haven.Core.Tests','HavenOS.Dev.Tests','Haven.Desktop.Tests'}
conditional_fresh=set(old_outputs)-mandatory_fresh
required_current_normal_modules={'Haven.Desktop'}|test_module_names
new_test_module='Haven.Console.Tests' # Existing genuine portable owner; no new enrollment or project substitution.
assert len(old_outputs)==22 and mandatory_fresh<=set(old_outputs) and conditional_fresh.isdisjoint(mandatory_fresh)
compiled_now=[];reused_now=[];reuse_decisions=[];runtime_queue=[];restored_current_parent=[];historical_package_rows={};historical_inputs=[];released_runtime_rows=[];targeted_failure=None
required_later_normal_inputs=set()
plugin_runtime_closure=None
fixture_stage_proofs=[]
reused_c43_test_closures=[]
validated_parent_test_closures=[]
# These are superseded SOURCE bodies, not omitted current inputs. Every after
# is bound by the controlling whole ticket and actual Git/physical readback.
parent_to_current_changes={}
for controlling,controlling_path in ((inherited_c44,inherited_c44_ticket),(inherited_c45,inherited_c45_ticket),*current_reviewed_source_chain):
 for row in controlling['newRows']:
  target=row['target'];after=row['afterPin'];assert pin(repo/target)['bytes']==selected[target]['bytes'] and pin(repo/target)['sha256']==selected[target]['sha256']
  parent_to_current_changes[target]={'target':target,'before':row['beforePin'],'after':after,'ticket':pin(controlling_path)}

def exact_package_member_matches(container,member,crc,expected,actual_path=None):
 # Decode and compare the entire actual member in64KiB chunks; ZipExtFile
 # validates CRC on EOF. No large PDB/member or copied-file body buffer.
 import contextlib
 with zipfile.ZipFile(container['path']) as z:
  info=z.getinfo(member);assert info.file_size==expected['bytes'] and info.CRC==crc
  digest=hashlib.sha256();count=0;equal=True
  with z.open(info) as source, contextlib.ExitStack() as stack:
   actual=stack.enter_context(pathlib.Path(actual_path).open('rb')) if actual_path is not None else None
   for chunk in iter(lambda:source.read(65536),b''):
    digest.update(chunk);count+=len(chunk)
    if actual is not None and actual.read(len(chunk))!=chunk:equal=False
   if actual is not None and actual.read(1)!=b'':equal=False
  assert count==expected['bytes']
  return digest.hexdigest()==expected['sha256'] and equal

def decode_runtime_item(item,actual_path=None):
 source=item['restoration']
 if source['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':
  container=source['container'];assert pin(container['path'])==container;enroll(container['path'])
  assert source['memberBytes']==item['original']['bytes'] and source['memberSha256']==item['original']['sha256']
  assert exact_package_member_matches(container,source['member'],source['memberCRC32'],item['original'],actual_path)
 else:
  assert source['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE';raw=read_archive(source['row'])
  assert len(raw)==item['original']['bytes'] and hashlib.sha256(raw).hexdigest()==item['original']['sha256']
  if actual_path is not None:assert pathlib.Path(actual_path).read_bytes()==raw
 return True

def release_closed_runtime_copies(manifest_pin,module):
 # Root ratified only these NEW, naturally closed, fully reconstructable
 # CopyLocal outputs. Current source/cache and all compiler outputs survive.
 assert pin(manifest_pin['path'])==manifest_pin
 m=json.loads(gzip.decompress(pathlib.Path(manifest_pin['path']).read_bytes()))
 assert m['head']==head and m['ticket']==pin(ticket) and m['actualRuntimeStage']['exitCode']==0
 protected={o[k]['path'] for o in outputs for k in ('output','target','pdb','reference') if o.get(k)}
 protected.update(required_later_normal_inputs)
 for o in outputs:
  if o.get('pdb'):
   alias=pathlib.Path(o['target']['path']).with_suffix('.pdb');actual_alias=pin(alias)
   assert actual_alias=={'path':str(alias),'bytes':o['pdb']['bytes'],'sha256':o['pdb']['sha256']}
   protected.add(str(alias))
 current_managed={pathlib.Path(o['target']['path']).name:o['target']['sha256'] for o in outputs}
 for item in m['files']:
  if '/' not in item['relative'] and item['relative'] in current_managed:
   assert item['original']['sha256']==current_managed[item['relative']],('Normal CopyLocal contains different current product',module,item['relative'])
 preserved=[];released=[]
 for item in m['files']:
  path=pathlib.Path(item['original']['path'])
  assert m['actualDurableNormalLayout']['module']==module and m['actualDurableNormalLayout']['targetDirectory']==str(normal_root/module)
  assert path.is_relative_to(normal_root/module) and path==normal_root/module/item['relative'] and path.resolve()==path and not path.is_symlink()
  st=path.stat();assert (st.st_dev,st.st_ino,st.st_nlink)==(item['device'],item['inode'],item['nlink'])
  assert pin(path)==item['original'] and decode_runtime_item(item,path)
  if str(path) in protected:preserved.append(item['original']);continue
  # There is no later compiler. Normal subsequent project CopyLocal obtains
  # its own dependencies from preserved product outputs/cache, not this bin.
  assert compiled_now and all(q['compilerDecisionComplete'] for q in runtime_queue)
  record={'module':module,'original':item['original'],'mode':item['mode'],'mtimeNs':item['mtimeNs'],'device':item['device'],'inode':item['inode'],'nlink':item['nlink'],'restoration':item['restoration'],'actualClosedRuntimeManifest':manifest_pin,'wholeSourceRoundtripBeforeRelease':True,'onlyNewTaskOwnedCopyLocalOutput':True,'physicalAtFinal':False}
  path.unlink();assert not path.exists()
  known.pop(str(path),None);generated.pop(str(path),None);runtime_package_rows.pop(str(path),None)
  released_runtime_rows.append(record);released.append(record)
 after=validate_disk_sample(disk_capacity_sample(module+'-after-normal-retirement'));assert after['workspaceAvailableBytes']>=disk_envelope_components['finalDiskSpare'] and not after['activeFullNormalModules']
 print('CURRENT_TARGETED65_CLOSED_RUNTIME_COPIES_CUSTODIED',module,len(released),sum(r['original']['bytes'] for r in released),flush=True)
 return {'manifest':manifest_pin,'retiredNewTaskOwnedCopies':len(released),'retiredRawBytes':sum(r['original']['bytes'] for r in released),'protectedCompilerOutputs':preserved}

def preserve_original_plugin_fixture_runtime(plan):
 global plugin_runtime_closure
 module='Haven.PluginFixture';compiled=next(o for o in outputs if o['module']==module)
 assert not compiled['compiledInCurrentInterval'] and not plan['targetedCompilerDecision']['freshCscRequired']
 assert plan['targetedCompilerDecision']['actualCompilerRefsAndAnalyzersWholeEqual'] and plan['targetedCompilerDecision']['allResolvedRefsWholeEqual']
 source_pins=[x['logical'] for x in plan['physicalItems']]
 ref_pins=[x['input'] for x in plan['actualReferencePins']]
 original_outputs=[o[k] for o in outputs for k in ('output','target','pdb','reference') if o.get(k)]
 before=[*source_pins,*ref_pins,*original_outputs]
 for p in before:assert pin(p['path'])==p
 project=next(project for name,project,_ in projects if name==module)
 flags=[*common,'-p:BuildProjectReferences=false','-p:TargetFramework='+plan['targetFramework'],'-p:SkipCompilerExecution=true']
 label=module+'-normal-fixture-runtime-output01'
 target_names='ResolveReferences;_CreateAppHost;GenerateBuildDependencyFile;GenerateBuildRuntimeConfigurationFiles;CopyFilesToOutputDirectory'
 flags,target_dir,normal_layout=prepare_durable_normal_layout(module,project,flags,plan)
 status=stage(label,[str(sdk),'msbuild',str(shadow/project),*flags,'-t:'+target_names,'-v:diagnostic'],fixture_exec_audit=True)
 record_durable_normal_stage(module)
 after=[pin(p['path']) for p in before]
 assert after==before,('Fixture normal output changed protected source/ref/compiled bytes',before,after)
 proof={'module':module,'actualStage':stages[-1],'skipCompilerExecutionOnlyForOriginalFixture':True,'actualCurrentCompilerInvoked':None,'noSecondCompilerQualified':False,'actualFixtureExecAuditObserver':fixture_exec_audit_observer,'originalCompilerOutput':compiled,'wholeProtectedSourceReferenceAndCompilerPins':before,'wholeProtectedSourceReferenceAndCompilerPinsAfter':after,'wholePinsUnchangedBeforeAndAfter':True,'genuineSdkCreateAppHostRequested':True,'noSourceOrReferenceExcluded':True}
 fixture_stage_proofs.append(proof)
 if status:
  test_runtime_output_failures.append({'module':module,'stage':stages[-1],'naturalExitCode':status,'qualification':'Actual owning executable fixture normal SDK target failed; no replacement or apphost/runtime body was fabricated.'});return
 assert target_dir==normal_root/module and target_dir.resolve()==target_dir
 # A rendered SDK Csc command is distinct from OS execution. Qualification
 # requires the whole owned-child exec trace, exact actual task parameters
 # when a Csc task is observed, and all independently protected pins above.
 actual_stage=stages[-1];capture=actual_stage['fixtureExecAuditCapture']
 diagnostic=pathlib.Path(capture['wholeDiagnosticPin']['path']).read_bytes();audit=pathlib.Path(capture['wholeAuditPin']['path']).read_bytes()
 try:qualification=fixture_audit.qualify(capture,diagnostic,audit,actual_stage['command'])
 except fixture_audit.AuditFailure as cause:
  proof['actualExecAuditQualificationFailure']=cause.report
  if cause.report_pin is not None:
   assert pin(cause.report_pin['path'])==cause.report_pin
   actual_stage['fixtureExecAuditFailureReportCustody'].append(custody_group([pathlib.Path(cause.report_pin['path'])],label+'-qualification-failure-report01'))
  actual_stage['fixtureExecAuditQualificationFailureReceipt']=save(label+'-WHOLE-EXEC-AUDIT-QUALIFICATION-FAILURE01.json.gz',cause.report)
  raise
 del diagnostic,audit
 assert qualification['noSecondCompilerQualified'] and qualification['actualExecProof']['otherExecAttemptCount']==qualification['actualExecProof']['compilerExecAttemptCount']==0
 assert qualification['actualExecProof']['wholeTraceParsedWithoutOmissionOrTruncation'] and all(value==0 for value in qualification['actualExecProof']['allObservedProcessExitCodes'].values())
 for task in qualification['actualCscTaskSkipProof']['actualCscTaskSkipProofs']:assert task['actualTaskParameterSkipCompilerExecution'] and task['actualTaskParameterUseSharedCompilation'] is False and task['actualDiagnosticTaskClosedSuccessfully']
 assert qualification['actualTaskSkipWitnessObserved']==bool(qualification['actualCscTaskSkipProof']['actualCscTaskSkipProofs'])
 observer=qualification['actualObserverDiagnosticProof']
 assert observer['unknownObserverDiagnosticCount']==0 and observer['observerWarningsErrorsDetachOmissionOrUnknownPrefixesAccepted'] is False and observer['allAttachmentPidsUniqueAndObservedNaturalZero']
 assert observer['noObserverNoticeDoesNotIndependentlyProveTraceCompleteness'] is True
 qualification_receipt=save(label+'-WHOLE-EXEC-AUDIT-QUALIFICATION01.json.gz',qualification);enroll(qualification_receipt['path'])
 proof.update(actualCurrentCompilerInvoked=False,noSecondCompilerQualified=True,actualFixtureExecAuditQualification=qualification,actualFixtureExecAuditQualificationReceipt=qualification_receipt)
 actual_stage['fixtureExecAuditQualificationReceipt']=qualification_receipt
 assembly_name=plan['properties']['AssemblyName']
 required=[target_dir/(assembly_name+'.dll'),target_dir/(assembly_name+'.deps.json'),target_dir/(assembly_name+'.runtimeconfig.json'),pathlib.Path(plan['properties']['IntermediateOutputPath'])/'apphost']
 for path in required:assert path.is_file() and path.resolve()==path and not path.is_symlink();enroll(path,False)
 files=[]
 for path in sorted(target_dir.rglob('*')):
  if not path.is_file():continue
  assert path.resolve()==path and not path.is_symlink();st=path.stat();row=custody_group([path],label);assert read_archive(row)==path.read_bytes();enroll(path)
  files.append({'relative':str(path.relative_to(target_dir)),'original':pin(path),'mode':st.st_mode,'mtimeNs':st.st_mtime_ns,'device':st.st_dev,'inode':st.st_ino,'nlink':st.st_nlink,'restoration':{'kind':'EXACT_COMPILER_CUSTODY_ARCHIVE','row':row}})
 preserve_genuine_normal_prerequisite_aliases(module,target_dir,plan)
 value={'status':'ACTUAL_NORMAL_EXECUTABLE_TEST_FIXTURE_RUNTIME_OUTPUT_CLOSURE_PASS','head':head,'ticket':pin(ticket),'module':module,'normalOwningProject':plan['canonicalProject'],'normalTargetFramework':plan['targetFramework'],'actualRuntimeStage':stages[-1],'actualCompilerSuccessProvenance':{**current_parent_compiler_origin(module,compiled),'currentWholeSourceRefAndCompiledBytesUnchanged':True},'actualOriginalOutputDirectory':str(target_dir),'actualDurableNormalLayout':normal_layout,'files':files,'fileCount':len(files),'wholeCopiedBytes':sum(x['original']['bytes'] for x in files),'allActualCompiledProductIdentitiesUnchanged':True,'noSecondCompiler':True,'actualFixtureExecAuditObserver':fixture_exec_audit_observer,'actualFixtureExecAuditQualification':qualification,'actualFixtureExecAuditQualificationReceipt':qualification_receipt,'wholeProtectedSourceReferenceAndCompilerPins':before,'wholeProtectedSourceReferenceAndCompilerPinsAfter':after,'dotnet':pin(sdk),'exactResourceCustodyHelper':pin(custody_helper),'closedLayerArchiveRegistry':pin(closed_layer_registry),'qualification':'Actual maintained executable fixture apphost/deps/runtimeconfig/CopyLocal outputs. Official SkipCompilerExecution applies only to this apphost prerequisite, with all source/ref/compiler bytes protected before and after. No test execution or fabricated output is claimed.'}
 plugin_runtime_closure=save(module+'-NORMAL-EXECUTABLE-FIXTURE-RUNTIME-OUTPUT-CLOSURE01.json.gz',value)
 print('CURRENT_TARGETED65_ACTUAL_PLUGIN_FIXTURE_NORMAL_RUNTIME_CLOSURE',json.dumps(plugin_runtime_closure),flush=True)

normal_filesystem=admit_durable_normal_allocation()
generated_layout_successor=None
def verify_generated_owning_layout_successor(original,current):
 global generated_layout_successor
 assert original=={'path':str(props),'bytes':2429,'sha256':'bcc3f9798b5cad579928eac2d428ea2afc31e903756588e047d28f712d36c4db'} and current==pin(props)
 root=ET.fromstring(props.read_bytes());condition="'$(RootNormalOutputModule)' != '' And '$(RootNormalOutputModule)' == '$(MSBuildProjectName)'"
 groups=[g for g in root.findall('PropertyGroup') if g.get('Condition')==condition]
 assert len(groups)==1 and [(x.tag,x.text) for x in groups[0]]==[('OutDir','$(RootNormalOutputDirectory)'),('OutputPath','$(RootNormalOutputDirectory)')]
 root.remove(groups[0]);ET.indent(root);source50_body=ET.tostring(root,encoding='unicode').encode()
 assert len(source50_body)==2481 and hashlib.sha256(source50_body).hexdigest()=='a9a5754608a141cc7adc5a2a5adb0e298067ad3c530f24afeaa5e467948adc0f'
 owners=[g for g in root.findall('PropertyGroup') if "'$(MSBuildProjectName)' == 'Haven.Console.Tests'" in g.get('Condition','')]
 token="'$(MSBuildProjectName)' == 'Haven.Console.Tests' Or "
 assert len(owners)==1 and owners[0].get('Condition').count(token)==1
 assert [(x.tag,x.text) for x in owners[0]]==[('ArtifactsPath',str(scope/'artifacts')),('ArtifactsProjectName','$(MSBuildProjectName)-checkpoint37-coherent19')]
 owners[0].set('Condition',owners[0].get('Condition').replace(token,''));ET.indent(root);original_body=ET.tostring(root,encoding='unicode').encode()
 assert len(original_body)==original['bytes'] and hashlib.sha256(original_body).hexdigest()==original['sha256']
 custody=custody_group([props],'exact-generated-owning-layout-successor55');assert read_archive(custody)==props.read_bytes()
 generated_layout_successor={'actualProps':current,'actualPropsCustody':custody,'originalC43Props':original,'exactSource50Props':{'path':str(props),'bytes':len(source50_body),'sha256':hashlib.sha256(source50_body).hexdigest()},'wholeInverseSource50Props':True,'wholeInverseOriginalC43Props':True,'onlyNormalOwnerPropertyGroupAndNewConsoleCondition':True}
 return generated_layout_successor

def custody_unqualified_durable_normal_files():
 # No success claim. Every actual partial output absent from a successful
 # manifest still has whole exact package/archive reconstruction provenance.
 covered=set()
 manifests=[runtime_closure,plugin_runtime_closure]+[x['manifest'] for x in test_runtime_closures]
 for manifest in manifests:
  if not manifest:continue
  assert pin(manifest['path'])==manifest
  m=json.loads(gzip.decompress(pathlib.Path(manifest['path']).read_bytes()))
  covered.update(x['original']['path'] for x in m['files'])
 package_index={};containers={}
 for package in package_pins:
  container=package.get('nupkg')
  if not container or container['path'] in containers:continue
  assert pin(container['path'])==container;enroll(container['path']);containers[container['path']]=container
  with zipfile.ZipFile(container['path']) as z:
   for info in z.infolist():
    if not info.is_dir():package_index.setdefault((pathlib.PurePosixPath(info.filename).name,info.file_size),[]).append((container,info.filename,info.CRC))
 for path in sorted(normal_root.rglob('*')):
  if not path.is_file() or str(path) in covered:continue
  assert path.resolve()==path and not path.is_symlink()
  relative=path.relative_to(normal_root);module=relative.parts[0];layout=next(x for x in durable_normal_layouts if x['module']==module)
  assert path.is_relative_to(normal_root/module);original=pin(path);st=path.stat();restoration=None
  for container,member,crc in package_index.get((path.name,original['bytes']),[]):
   if exact_package_member_matches(container,member,crc,original,path):
    restoration={'kind':'EXACT_PACKAGE_NUPKG_MEMBER','container':container,'member':member,'memberBytes':original['bytes'],'memberCRC32':crc,'memberSha256':original['sha256'],'wholeBytesComparedWithActualCopiedFile':True,'streamingWholeComparison':True};break
  if restoration is None:
   row=custody_group([path],module+'-unqualified-partial-normal-output55');assert read_archive(row)==path.read_bytes();restoration={'kind':'EXACT_COMPILER_CUSTODY_ARCHIVE','row':row}
  item={'status':'UNQUALIFIED_PARTIAL_NORMAL_OUTPUT_WHOLE_CUSTODY_NO_SUCCESS','module':module,'relative':str(path.relative_to(normal_root/module)),'original':original,'mode':st.st_mode,'mtimeNs':st.st_mtime_ns,'device':st.st_dev,'inode':st.st_ino,'nlink':st.st_nlink,'restoration':restoration,'sourceNormalLayout':layout}
  unqualified_durable_normal_files.append(item);enroll(path)
 return unqualified_durable_normal_files

try:
 admission=capacity_sample('actual-before-any-sdk');capacity_measurements.append(admission)
 assert admission['combinedAvailableBytes']>=minimum_admission
 # Restore exact current-parent compiler inputs and object products only.
 # Failed partial CopyLocal trees are historical outputs, not compiler inputs.
 component_private_pins={x['path']:x for x in [*component_basis['inputs'],*component_basis['generatedInputPins']] if x['path'].startswith(str(scope/'artifacts')+'/') or x['path'].startswith(str(scope/'metadata')+'/')}
 assert len(component_private_pins)==621 and sum(x['bytes'] for x in component_private_pins.values())==154948201
 needed_private=set(component_private_pins)
 for o in component_basis['outputs']:
  needed_private.update(o[k]['path'] for k in ('output','target','pdb','reference') if o.get(k))
  if o.get('pdb'):needed_private.add(str(pathlib.Path(o['target']['path']).with_suffix('.pdb')))
 original_restoration_index={}
 for row in [*prior['outputArchives'],*component_basis['outputArchives']]:
  archive_by_sha[row['original']['sha256']]=row['archive']
  if row.get('encoding'):archive_encodings[row['original']['sha256']]=row['encoding']
  for original_path in row['originalPaths']:
   if original_path in needed_private:
    expected=component_private_pins.get(original_path)
    if expected is None or (row['original']['bytes'],row['original']['sha256'])==(expected['bytes'],expected['sha256']):original_restoration_index[original_path]=row
 for row in component_final['archives']:
  for original_path in row['originalPaths']:
   if original_path.startswith(str(scope/'artifacts/obj')+'/'):
    needed_private.add(original_path);original_restoration_index[original_path]=row
 # Restore exactly the TWO genuine original C43 Desktop prerequisite JSON
 # bodies which disappeared during failed55's fresh Desktop bin retirement.
 # They are legacy compiler/CopyLocal dependencies, NOT new current normals.
 historical_desktop_prerequisite_rows={}
 historical_desktop_manifest_pin=prior['actualDesktopRuntimeClosureManifest']
 assert pin(historical_desktop_manifest_pin['path'])==historical_desktop_manifest_pin;enroll(historical_desktop_manifest_pin['path'])
 historical_desktop_manifest=json.loads(gzip.decompress(pathlib.Path(historical_desktop_manifest_pin['path']).read_bytes()))
 assert historical_desktop_manifest['head']==prior['headAtCompletion'] and historical_desktop_manifest['actualRuntimeStage']['exitCode']==0
 for filename,expected_bytes,expected_sha in [('Haven.deps.json',91434,'1629f2aa07ce3741fd18f9cf65e736b1db4c5c134ba356aa0ee41e751d000b7e'),('Haven.runtimeconfig.json',376,'bfb88e22e1e264761fd17d9266793dbeff7f3d3134547440265297435465a393')]:
  original_path=str(scope/'artifacts/bin/Haven.Desktop-checkpoint37-coherent19/debug_net10.0'/filename)
  expected={'path':original_path,'bytes':expected_bytes,'sha256':expected_sha}
  assert expected in prior['inputs']
  normal_file=next(item for item in historical_desktop_manifest['files'] if item['relative']==filename)
  assert normal_file['original']==expected and normal_file['restoration']['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE'
  candidates=[row for row in prior['outputArchives'] if row['stage']=='C43-targeted46-final' and original_path in row['originalPaths'] and (row['original']['bytes'],row['original']['sha256'])==(expected_bytes,expected_sha)]
  assert len(candidates)==1;donor=candidates[0]
  assert donor['archive']==normal_file['restoration']['row']['archive']
  raw=read_archive(donor);assert len(raw)==expected_bytes and hashlib.sha256(raw).hexdigest()==expected_sha;del raw
  if original_path not in needed_private:original_restoration_index[original_path]=donor;needed_private.add(original_path)
  historical_desktop_prerequisite_rows[original_path]={'qualification':'EXACT_HISTORICAL_C43_NORMAL_PREREQUISITE_RESTORATION_NOT_CURRENT_NORMAL_SUCCESS','originalRequiredC43Input':expected,'originalC43NormalManifest':historical_desktop_manifest_pin,'originalC43WholeArchiveDonor':donor,'wholeOriginalDependencyConserved':True,'currentComponentArtifactNotOverwritten':True,'currentNormalClosureNotClaimed':True,'current59PrerequisiteRetainedIfPresent':original_path in component_private_pins,'actualRestorationSource':original_restoration_index[original_path]}
 assert len(historical_desktop_prerequisite_rows)==2 and sum(row['originalRequiredC43Input']['bytes'] for row in historical_desktop_prerequisite_rows.values())==91810
 assert sum(original_restoration_index[path]['original']['bytes'] for path in needed_private)<=182374123, 'actual selective component reconstruction must fit conserved original raw reconstruction allowance'
 assert needed_private<=set(original_restoration_index),('Current compiler input lacks retained whole body',needed_private-set(original_restoration_index))
 for original_path in sorted(needed_private):
  row=original_restoration_index[original_path];raw=read_archive(row);dest=pathlib.Path(original_path)
  assert dest.is_relative_to(scope) and dest.resolve(strict=False)==dest and not dest.is_symlink()
  dest.parent.mkdir(parents=True,exist_ok=True)
  if dest.exists():assert dest.is_file();dest.unlink()
  with dest.open('xb') as f:f.write(raw);f.flush();os.fsync(f.fileno())
  os.chmod(dest,row['mode']);os.utime(dest,ns=(row['mtimeNs'],row['mtimeNs']))
  assert pin(dest)=={'path':str(dest),'bytes':len(raw),'sha256':row['original']['sha256']}
  restoration={'actualRestored':pin(dest),'originalComponentCustody':row['archive'],'successfulCurrent59Parent':pin(component_basis_path),'current59FullClosedReadback':pin(component_closed_path),'historicalFailed55Custody':pin(failed55_closed_path)}
  if original_path in historical_desktop_prerequisite_rows:
   witness=historical_desktop_prerequisite_rows[original_path];expected_actual=component_private_pins.get(original_path,witness['originalRequiredC43Input']);assert restoration['actualRestored']==expected_actual
   restoration['historicalC43PrerequisiteProvenance']=witness
  restored_current_parent.append(restoration);enroll(dest,False)
 for original in [*prior['actualOwningTestRuntimeClosureManifests'],*prior['genuineUnchangedC42TestRuntimeClosureManifests']]:
  manifest=original['manifest'];assert pin(manifest['path'])==manifest;enroll(manifest['path'])
  m=json.loads(gzip.decompress(pathlib.Path(manifest['path']).read_bytes()))
  assert m['module']==original['module'] and m['actualRuntimeStage']['exitCode']==0
  if original in prior['actualOwningTestRuntimeClosureManifests']:
   assert m['head']==compiled_parent_head and m['ticket']==prior['ticket']
   assert any(x['module']==original['module'] and x['manifest']==manifest for x in prior_closed['actualNormalRuntimeClosures'])
  else:
   witness=next(x for x in prior_closed['qualifiedHistoricalC42NormalTestRuntimeClosures'] if x['module']==original['module'] and x['manifest']==manifest)
   assert witness['actualOriginalNormalOutputHead']==m['head'] and witness['currentWholeSourceResourceRefEquivalence']
  assert m['actualCompilerSuccessProvenance']['compiledSourceHead']==next(o['compiledSourceHead'] for o in prior['outputs'] if o['module']==original['module'])
  assert m['fileCount']==original['fileCount'] and len(m['files'])==m['fileCount']
  for item in m['files']:
   decode_runtime_item(item);path=pathlib.Path(item['original']['path'])
   if item['restoration']['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':historical_package_rows[str(path)]=item
  validated_parent_test_closures.append(original)
 assert len(validated_parent_test_closures)==7 and len({x['module'] for x in validated_parent_test_closures})==7
 for item in prior['releasedNewTaskOwnedRuntimeCopies']:
  path=item['original']['path']
  if item['restoration']['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':historical_package_rows[path]=item
 # Historical runtime copies remain exact in complete manifests and archives.
 # They are not restored as compiler inputs or relabelled current physical data.
 # Historical complete source/input census remains preserved. Changed source
 # receives exact reviewed-current after bytes; retired runtime copies are
 # archive-proven historical outputs, never claimed physically fresh inputs.
 for original in prior['inputs']:
  path=pathlib.Path(original['path']);record={'originalC43Input':original}
  if str(path) in historical_package_rows:
   assert historical_package_rows[str(path)]['original']==original
   record.update(status='HISTORICAL_NORMAL_RUNTIME_COPY_FULL_RESTORATION_CUSTODY',physicalRestorationRequiredForCompiler=False,restoration=historical_package_rows[str(path)]['restoration'])
  else:
   assert path.is_file(),('Unexplained actual current-parent input absence',original)
   current=pin(path)
   if current!=original:
    if path.is_relative_to(scope/'artifacts') and str(path) in original_restoration_index:
     row=original_restoration_index[str(path)]
     assert current['bytes']==row['original']['bytes'] and current['sha256']==row['original']['sha256']
     owner=next((name for name,old_plan in old_plans.items() if path.is_relative_to(pathlib.Path(old_plan['properties']['IntermediateOutputPath'])) or path.is_relative_to(pathlib.Path(old_plan['properties']['TargetPath']).parent)),None)
     assert owner in old_outputs and (row in component_final['archives'] or row in component_basis['outputArchives'])
     record.update(status='EXACT_GENUINE_COMPONENT_ARTIFACT_SUPERSESSION_FROM_SUCCESSFUL_CURRENT59',current=current,componentOwner=owner,actualSuccessfulCurrent59CompilerReceipt=pin(component_basis_path),actualCurrent59FullClosedReadback=pin(component_closed_path),wholeCurrentArtifactCustody=row)
    elif path==props:
     witness=verify_generated_owning_layout_successor(original,current)
     record.update(status='EXACT_REVIEWED_GENERATED_OWNING_LAYOUT_SUCCESSOR',current=current,generatedOwningLayoutSuccessor=witness)
    else:
     relative=str(path.relative_to(repo)) if path.is_relative_to(repo) else str(path.relative_to(shadow)) if path.is_relative_to(shadow) else None
     assert relative in parent_to_current_changes and current['bytes']==selected[relative]['bytes'] and current['sha256']==selected[relative]['sha256'],('Unreviewed prior input change',original,current)
     record.update(status='EXACT_REVIEWED_CURRENT_SOURCE_SUPERSESSION',current=current,sourceChange=parent_to_current_changes[relative])
   else:record.update(status='WHOLE_ORIGINAL_C43_INPUT_EXACT',current=current)
   enroll(path,not path.is_relative_to(scope/'artifacts') and not path.is_relative_to(scope/'metadata'))
  historical_inputs.append(record)
 for package in prior['packagePins']:
  for key in ('nupkg','checksum','metadata'):assert pin(package[key]['path'])==package[key];enroll(package[key]['path'])
 package_pins.extend(prior['packagePins'])
 current59_input_dispositions=[]
 current59_runtime_items={item['original']['path']:item for item in component_basis['releasedNewTaskOwnedRuntimeCopies']}
 current59_source_changes={row['target']:row for body,_ in current_reviewed_source_chain for row in body['newRows']}
 current59_donor_index={}
 for row in component_basis['outputArchives']:
  for path in row['originalPaths']:current59_donor_index.setdefault(path,[]).append(row)
 for original in component_basis['inputs']:
  path=pathlib.Path(original['path']);record={'originalCurrent59Input':original}
  if str(path) in current59_runtime_items:
   item=current59_runtime_items[str(path)];assert item['original']==original;decode_runtime_item(item)
   record.update(status='CURRENT59_RETIRED_NORMAL_COPY_WHOLE_RESTORATION_CUSTODY',restoration=item['restoration'],physicalCurrentCompilerInput=False)
  elif path.is_relative_to(scope) and not path.is_file():
   candidates=[r for r in current59_donor_index.get(str(path),[]) if (r['original']['bytes'],r['original']['sha256'])==(original['bytes'],original['sha256'])]
   assert candidates,('Unexplained current59 private input absence',original)
   donor=candidates[-1];raw=read_archive(donor);assert len(raw)==original['bytes'] and hashlib.sha256(raw).hexdigest()==original['sha256'];del raw
   record.update(status='CURRENT59_PRIVATE_NONCOMPILER_OBSERVATION_WHOLE_ARCHIVE',wholeOriginalCustody=donor,physicalCurrentCompilerInput=False)
  else:
   assert path.is_file(),('Unexplained current59 durable input absence',original)
   current=pin(path)
   if current!=original:
    relative=str(path.relative_to(repo)) if path.is_relative_to(repo) else str(path.relative_to(shadow)) if path.is_relative_to(shadow) else None
    assert relative in current59_source_changes and current['bytes']==selected[relative]['bytes'] and current['sha256']==selected[relative]['sha256'],('Unreviewed successful current59 input change',original,current)
    record.update(status='REVIEWED_EXACT_C46_SOURCE_SUPERSESSION',current=current,reviewedSource=current59_source_changes[relative])
   else:record.update(status='WHOLE_ORIGINAL_CURRENT59_INPUT_EXACT',current=current)
   enroll(path,not path.is_relative_to(scope/'artifacts') and not path.is_relative_to(scope/'metadata'))
  current59_input_dispositions.append(record)
 for original in [component_basis['actualDesktopRuntimeClosureManifest'],component_basis['actualPluginFixtureRuntimeClosureManifest']]+[row['manifest'] for row in component_basis['actualOwningTestRuntimeClosureManifests']]+[row['manifest'] for row in component_basis['genuineUnchangedC43TestRuntimeClosureManifests']]:
  assert pin(original['path'])==original;enroll(original['path']);normal=json.loads(gzip.decompress(pathlib.Path(original['path']).read_bytes()))
  assert normal['actualRuntimeStage']['exitCode']==0 and normal['fileCount']==len(normal['files'])
  for item in normal['files']:decode_runtime_item(item)
 for package in component_basis['packagePins']:
  for key in ('nupkg','checksum','metadata'):assert pin(package[key]['path'])==package[key];enroll(package[key]['path'])
 # All22 owning metadata products are the same exact genuine current59 graph.
 # No project/package/target XML changed in C46; all source owners are freshly
 # evaluated below. No synthetic metadata, new owner or blind source reuse.
 for module,project,tfm in projects:
  asset=scope/'metadata'/module/'project.assets.json';assert asset.is_file() and asset.resolve()==asset
  assert pin(asset)==component_private_pins[str(asset)]
  asset_body=json.loads(asset.read_bytes());assert asset_body['project']['restore']['projectPath']==str(shadow/project)
  assert tfm in asset_body['project']['frameworks'];enroll(asset,False)
 # All original21 owners retain whole current input/ref/resource guards.
 for module,project,tfm in projects:
  assert (scope/'metadata'/module/'project.assets.json').is_file()
  flags,plan=evaluated(module,project,tfm);p=plan['properties'];old_plan=old_plans.get(module)
  assert old_plan is not None and plan['canonicalProject']==old_plan['canonicalProject'] and plan['project']==old_plan['project'] and p==old_plan['properties']
  assert not plan['missingItems'],('Current owning source/resource item unresolved',module,plan['missingItems'])
  if module==new_test_module:
   assert tfm==p['TargetFramework']=='net10.0' and p['AssemblyName']==p['MSBuildProjectName']==new_test_module and p['RootNamespace']=='Haven.Desktop.Tests'
   assert plan['canonicalProject']==pin(repo/console_tests_relative) and plan['project']==pin(shadow/console_tests_relative)
  old_items=[(x['kind'],x['logical']['path'],x['logical']['bytes'],x['logical']['sha256']) for x in old_plan.get('physicalItems',[])]
  new_items=[(x['kind'],x['logical']['path'],x['logical']['bytes'],x['logical']['sha256']) for x in plan['physicalItems']]
  if module not in mandatory_fresh|conditional_fresh:assert old_items==new_items,('Unchanged owner source/item/resource/order differs',module)
  if module!=new_test_module:assert plan['canonicalProject']==old_plan['canonicalProject'] and plan['project']==old_plan['project'] and p==old_plan['properties']
  if module=='Haven.Desktop.Tests':
   required_item=xml_exceptions[desktop_tests_relative]['requiredActualCompileItem']
   assert any(x['kind']=='Compile' and x['logical']['path']==required_item and x['logical']['sha256']==pin(repo/'9to1 Workspace/Dev/Tests/DeveloperOriginalPreparedConsentTests.cs')['sha256'] for x in plan['physicalItems'])
  rc=[str(sdk),'msbuild',str(shadow/project),*flags,'-t:ResolveReferences;FindReferenceAssembliesForReferences','-getItem:ReferencePath,ReferencePathWithRefAssemblies,Analyzer','-v:quiet']
  status=stage(module+'-references01',rc);assert status==0,('Actual normal references failed',module,status)
  refs=json.loads(stage_log(module+'-references01').read_text());rp=save(module+'-references01.json.gz',refs);enroll(rp['path']);actual=[]
  for kind,items in refs['Items'].items():
   for item in items:
    path=pathlib.Path(item.get('FullPath',item['Identity']));assert path.is_file()
    actual.append({'kind':kind,'input':enroll(path,not path.is_relative_to(scope/'artifacts')),'metadata':item})
  plan['actualReferencePins']=actual;plan['references']=rp
  current_upstreams=[]
  for parent_name,_,_ in projects:
   if parent_name==module:continue
   baseline=old_outputs.get(parent_name);current=next((o for o in outputs if o['module']==parent_name),None)
   if not baseline:continue
   names={pathlib.Path(baseline[k]['path']).name for k in ('output','target','reference') if baseline.get(k)}
   found=[r for r in actual if pathlib.Path(r['input']['path']).name in names]
   if found:
    assert current is not None,('Missing genuinely validated upstream output',module,parent_name)
    allowed={current[k]['sha256'] for k in ('output','target','reference') if current.get(k)}
    assert all(r['input']['sha256'] in allowed for r in found),('Current resolver did not select actual current product',module,parent_name)
    current_upstreams.append({'owner':parent_name,'inputs':[r['input'] for r in found],'successfulUpstream':current['output']})
  plan['actualUpstreamReferenceClosure']=current_upstreams
  old_refs=[(r['kind'],r['input']) for r in old_plan['actualReferencePins']]
  new_refs=[(r['kind'],r['input']) for r in actual]
  old_csc_refs=[r for r in old_refs if r[0] in ('ReferencePathWithRefAssemblies','Analyzer')]
  new_csc_refs=[r for r in new_refs if r[0] in ('ReferencePathWithRefAssemblies','Analyzer')]
  need_fresh=module in mandatory_fresh
  if module in conditional_fresh:need_fresh=old_items!=new_items or old_csc_refs!=new_csc_refs
  if module not in mandatory_fresh|conditional_fresh:assert old_refs==new_refs,('Other owner resolved refs/analyzers changed',module)
  if module in conditional_fresh and not need_fresh:
   # Only genuine changed-owner implementation paths may differ outside the actual
   # compiler reference set. Public/API claims never replace whole ref equality.
   assert len(old_refs)==len(new_refs)
   for old_ref,new_ref in zip(old_refs,new_refs):
    if old_ref==new_ref:continue
    assert old_ref[0]==new_ref[0]=='ReferencePath' and old_ref[1]['path']==new_ref[1]['path']
    changed_owner=next((name for name,o in old_outputs.items() if o.get('target')==old_ref[1]),None)
    assert changed_owner in mandatory_fresh|conditional_fresh
    assert new_ref[1]==next(o['target'] for o in outputs if o['module']==changed_owner)
   assert old_csc_refs==new_csc_refs
  decision={'module':module,'mandatoryFresh':module in mandatory_fresh,'conditionalFresh':module in conditional_fresh,'actualCompilerRefsAndAnalyzersWholeEqual':old_csc_refs==new_csc_refs,'completeSourceItemResourceOrderEqual':old_items==new_items,'allResolvedRefsWholeEqual':old_refs==new_refs,'freshCscRequired':need_fresh,'unreviewedAbiWaiver':False};reuse_decisions.append(decision);plan['targetedCompilerDecision']=decision
  if not need_fresh:
   assert module in old_outputs and old_items==new_items
   output=old_outputs[module]
   for key in ('output','target','pdb','reference'):
    if output.get(key):assert pin(output[key]['path'])==output[key]
   borrowed={k:output[k] for k in ('module','output','target','pdb','reference')}
   borrowed.update(compiledInCurrentInterval=False,compiledSourceHead=output['compiledSourceHead'],originalComponentCompiledInSuccessful59Interval=output['compiledInCurrentInterval'],componentReuseQualification='GENUINE_FULL_SUCCESSFUL_CURRENT59_COMPONENT_AFTER_EXACT_CURRENT_SOURCE_RESOURCE_REF_ANALYZER_EQUALITY',reusedFromActualCompilerReceipt=pin(component_basis_path),reusedFromCurrentParentFullClosedReadback=pin(component_closed_path),reusedReceiptOriginalNaturalExitCode=0,completeSourceItemResourceOrderEqual=True,actualCompilerReferenceAndAnalyzerBytesEqual=True,evaluation=plan['evaluation'],references=rp)
   outputs.append(borrowed);reused_now.append(module);plan['reusedActualSuccessfulCompiler']=pin(component_basis_path);plan['currentParentFullClosedReadback']=pin(component_closed_path)
   print('CURRENT_TARGETED65_REUSED_GENUINE_C43_QUALIFIED_COMPILER_CHAIN',module,flush=True)
  else:
   # Exclusive task-generated products only. All original bytes/source/custody
   # remain separately preserved; no source/cache or other module deletion.
   objdir=pathlib.Path(p['IntermediateOutputPath']);bindir=pathlib.Path(p['TargetPath']).parent
   for d in (objdir,bindir):
    assert d.is_relative_to(scope/'artifacts') and d.resolve()==d
    for path in list(known):
     if pathlib.Path(path).is_relative_to(d):known.pop(path)
    for path in list(generated):
     if pathlib.Path(path).is_relative_to(d):generated.pop(path)
    if d.exists():shutil.rmtree(d)
   assert module in mandatory_fresh|conditional_fresh and module not in {o['module'] for o in outputs}
   assert all((decision['freshCscRequired'] or (decision['completeSourceItemResourceOrderEqual'] and decision['actualCompilerRefsAndAnalyzersWholeEqual'])) for decision in reuse_decisions)
   materialize_original_resources(json.loads(gzip.decompress(pathlib.Path(plan['evaluation']['path']).read_bytes())),plan)
   actual_output=objdir/(p['AssemblyName']+'.dll');assert not actual_output.exists()
   targets='ResolveReferences;PrepareResources;Compile'+(';CompileAvaloniaXaml' if module=='Haven.Desktop' else '')
   status=stage(module+'-compiler01',[str(sdk),'msbuild',str(shadow/project),*flags,'-t:'+targets,'-v:normal']);plan['compilerExitCode']=status
   if module=='Haven.Desktop':validate_generated_resource_index(json.loads(gzip.decompress(pathlib.Path(plan['evaluation']['path']).read_bytes())),plan)
   if status:
    failure={'module':module,'naturalCompilerExitCode':status,'rawLog':pin(stage_log(module+'-compiler01'))};archive_stage(module+'-compiler-failed')
    if module in test_module_names:independent_test_failures.append(failure);continue
    if module=='Haven.Desktop':desktop_compiler_failure.append(failure);continue
    raise RuntimeError(('Required production Csc failed',failure))
   assert actual_output.is_file();target=pathlib.Path(p['TargetPath']);target.parent.mkdir(parents=True,exist_ok=True);os.link(actual_output,target)
   pdb=actual_output.with_suffix('.pdb')
   if pdb.exists():os.link(pdb,target.with_suffix('.pdb'))
   ri=actual_output.parent/'refint'/actual_output.name;reference=None
   if ri.exists():rf=pathlib.Path(p['TargetRefPath']);rf.parent.mkdir(parents=True,exist_ok=True);os.link(ri,rf);reference=pin(rf)
   log=stage_log(module+'-compiler01').read_text(errors='replace');csc_refs=[]
   for match in re.finditer(r'/reference:(?:"([^"]+)"|([^\s]+))',log):
    for value in (match.group(1) or match.group(2)).split(','):
     ref=pathlib.Path(value)
     if ref.is_file():csc_refs.append(enroll(ref,not ref.is_relative_to(scope/'artifacts')))
   assert re.search(r'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\"]|$)',log),('No actual Csc command',module)
   outputs.append({'module':module,'output':pin(actual_output),'target':pin(target),'pdb':pin(pdb) if pdb.exists() else None,'reference':reference,'compiledInCurrentInterval':True,'compiledSourceHead':head,'evaluation':plan['evaluation'],'references':rp,'actualCscReferencePins':csc_refs,'cscCommandObserved':True,'xamlCompilationRequired':module=='Haven.Desktop'})
   compiled_now.append(module);plan['custody']=archive_stage(module+'-compiler01')
   # Actual generator outputs, not hand-edited source/aliases, must reproduce
   # their entire genuine failed55 bodies with only RootNamespace corrected.
   if module==new_test_module:
    generated_namespace_readbacks=[]
    captured_root=pathlib.Path('/workspace/astra-source/a4-c44-actual55-failure-repair57/genuine-captured-source-and-logs')
    for filename,original_bytes,original_sha in [('SelfRegisteredExtensions.cs',850,'e98bbe62981b236747aafb352a7c161688afefbf4ea9b5072f5342981726dff3'),('XunitAutoGeneratedEntryPoint.cs',705,'e4d03a62d168204cad0351c53cad7ec3db9214b9340afe950b1ae56afd673791')]:
     captured=captured_root/filename;assert pin(captured)=={'path':str(captured),'bytes':original_bytes,'sha256':original_sha};enroll(captured)
     original=captured.read_bytes();old_namespace=b'namespace Haven.Console.Tests';new_namespace=b'namespace Haven.Desktop.Tests'
     assert original.count(old_namespace)==1
     actual=objdir/filename;assert actual.is_file() and actual.resolve()==actual and not actual.is_symlink()
     current=actual.read_bytes();assert current==original.replace(old_namespace,new_namespace,1) and current.count(new_namespace)==1 and old_namespace not in current
     generated_namespace_readbacks.append({'filename':filename,'originalGenuineFailed55GeneratedSource':pin(captured),'actualGenuineCorrectedGeneratedSource':enroll(actual,False),'actualCurrentRootNamespace':p['RootNamespace'],'wholeInverseChangesOnlySingleGeneratedNamespace':True,'generatedByActualSDKAndPackageTargets':True})
    plan['actualGeneratedNamespaceCorrectionReadbacks']=generated_namespace_readbacks
   print('CURRENT_TARGETED65_FRESH_CSC_PASS',module,flush=True)
  if module in required_current_normal_modules:runtime_queue.append({'module':module,'project':project,'flags':flags,'plan':plan,'compilerDecisionComplete':True})
 # Every compiler is naturally settled before full native RID copying. Each
 # normal runtime is complete, checked and losslessly custodied before copies
 # retire; assembly/ref/PDB aliases remain for later normal project reads.
 assert set(compiled_now).issubset(mandatory_fresh|conditional_fresh)
 assert set(compiled_now)<=set(old_outputs) and len(compiled_now)<=22
 assert mandatory_fresh<=set(compiled_now),('Required fresh current-source compiler incomplete',sorted(mandatory_fresh-set(compiled_now)))
 for original in validated_parent_test_closures:
  module=original['module'];decision=next(x for x in reuse_decisions if x['module']==module)
  if module in required_current_normal_modules:continue
  assert not decision['freshCscRequired'] and decision['allResolvedRefsWholeEqual']
  reused_c43_test_closures.append(original)
 assert not reused_c43_test_closures, 'Every current owning test receives new normal closure; historical C43 closures remain qualified history only.'
 # Required executable project outputs must survive until downstream normal
 # CopyLocal reads naturally settle. These five paths are explicitly protected.
 for owner in ('Haven.Desktop','Haven.PluginFixture'):
  owner_plan=next(p for p in plans if p['module']==owner);d=pathlib.Path(owner_plan['properties']['TargetPath']).parent;a=owner_plan['properties']['AssemblyName']
  required_later_normal_inputs.update(str(d/(a+suffix)) for suffix in ('.deps.json','.runtimeconfig.json'))
 fixture_plan=next(p for p in plans if p['module']=='Haven.PluginFixture')
 required_later_normal_inputs.add(str(pathlib.Path(fixture_plan['properties']['IntermediateOutputPath'])/'apphost'))
 preserve_original_plugin_fixture_runtime(fixture_plan)
 for queued in runtime_queue:
  module=queued['module']
  if module=='Haven.Desktop':
   preserve_actual_desktop_runtime_output(queued['project'],queued['flags'],queued['plan'])
   if runtime_closure:release_closed_runtime_copies(runtime_closure,module)
  else:
   prior_count=len(test_runtime_closures)
   preserve_actual_owning_test_runtime_output(module,queued['project'],queued['flags'],queued['plan'])
   if len(test_runtime_closures)>prior_count:release_closed_runtime_copies(test_runtime_closures[-1]['manifest'],module)
except BaseException as cause:
 code=1;targeted_failure={'type':type(cause).__name__,'message':str(cause)}
 print('CURRENT_TARGETED65_ACTUAL_FAILURE',json.dumps(targeted_failure),flush=True)
finally:
 if independent_test_failures or desktop_compiler_failure or runtime_output_failures or test_runtime_output_failures:code=1
 custody_unqualified_durable_normal_files()
 released_checked=[]
 for row in released_runtime_rows:
  assert not pathlib.Path(row['original']['path']).exists()
  item={'original':row['original'],'restoration':row['restoration']};decode_runtime_item(item)
  released_checked.append({'original':row['original'],'exactRestorationRevalidated':True,'physicalAtFinal':False})
 final_custody=archive_stage('current-targeted65-final')
 changed=[];unavailable=[]
 for path,before in known.items():
  try:after=pin(path)
  except OSError:unavailable.append(path);continue
  if after!=before:changed.append({'before':before,'after':after})
 for original in immutable_git_blob_inputs.values():
  try:custody_library.original_git_blob(original['repository'],{'gitBlobObjectId':original['objectId'],'bytes':original['bytes'],'sha256':original['sha256']})
  except BaseException as cause:unavailable.append({'immutableGitBlob':original,'failure':repr(cause)})
 actual_head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()
 if changed or unavailable or actual_head!=head:code=1
 successful={o['module'] for o in outputs};coverage=[]
 for row in selection['finalRows']:
  owners=sorted(source_membership.get(row['target'],set()));completed=[n for n in owners if n in successful]
  status='ACTUAL_OWNING_CSHARP_COMPILED' if any(n in compiled_now for n in completed) else 'REUSED_GENUINE_PRIOR_COMPILER_EXACT_CURRENT_INPUT_EQUIVALENT' if completed else 'EVALUATED_OWNER_FAILED_OR_SOURCE_ONLY'
  coverage.append({'target':row['target'],'expectedAfterBytes':row['bytes'],'expectedAfterSha256':row['sha256'],'status':status,'evaluatedCompileOwners':owners,'completedCompilerOwners':completed})
 disk_final=validate_disk_sample(disk_capacity_sample('actual-durable-normal-before-final-receipt'));assert disk_final['workspaceAvailableBytes']>=disk_envelope_components['finalDiskSpare']
 durable_allocation={'normalRoot':str(normal_root),'workspaceFilesystem':normal_filesystem,'declaredDiskEnvelope':disk_envelope,'rawResidentRequirementBytes':raw_resident_requirement,'originalRawRequirementBytes':original_raw_requirement,'onlyMovedNormalTreeBytes':only_moved_normal_tree,'noCacheCredit':True,'capacityMeasurements':disk_capacity_measurements,'normalAliasBudgetBytes':normal_alias_budget,'normalAliasActualBytes':sum(x['alias']['bytes'] for x in normal_alias_rows),'totalBlockAllocationAtCompletion':disk_final['totalNormalAllocatedBytes']}
 receipt=save('CURRENT-TARGETED-OWNING-COMPILER65-RECEIPT.json.gz',{'status':'ACTUAL_TARGETED_OWNING_COMPILER_PASS' if code==0 else 'ACTUAL_TARGETED_OWNING_COMPILER_FAILURE_PRESERVED','exitCode':code,'headAtCapture':head,'headAtCompletion':actual_head,'ticket':pin(ticket),'compiler':{'ticket':pin(ticket)},'actualC43ParentCompilerReceipt':pin(prior_path),'actualC43FullClosedReadback':pin(prior_closed_path),'actualFailedC44ComponentBasis':pin(failed55_basis_path),'actualFailedC44ComponentCustody':pin(failed55_closed_path),'actualFailedC44ComponentSourcePeer':pin(failed55_peer_path),'actualCurrent59SuccessfulCompilerReceipt':pin(component_basis_path),'actualCurrent59FullClosedReadback':pin(component_closed_path),'actualCurrent59QualifiedSourcePeer':pin(component_peer_path),'exactSelectiveCompilerSource':exact_selective_compiler_source,'exactFixtureExecAuditHelper':pin(fixture_audit_path),'componentBasisOriginalNaturalExitCode':0,'componentBasisIsGenuineFullSuccessfulCurrent59Interval':True,'preservedFailed55NeverClaimedFullSuccessfulInterval':True,'actualSevenSymbolRestoration':pin(symbol_path),'mandatoryFreshOwners':sorted(mandatory_fresh),'conditionalFreshOwners':sorted(conditional_fresh),'actualFreshCscOwners':compiled_now,'genuinePriorCompilerReusedCscOwners':reused_now,'actualReuseDecisions':reuse_decisions,'transparentReusedCompiledSourceHeads':{o['module']:o['compiledSourceHead'] for o in outputs if not o['compiledInCurrentInterval']},'reusedCompilerOutputNotRelabeledCurrent':True,'sourceRows':selection['finalRows'],'endpointCoverage':coverage,'inputs':list(known.values()),'generatedInputPins':list(generated.values()),'inputsUnchangedAtCompletion':not changed and not unavailable and actual_head==head,'changedInputs':changed,'unavailableInputsAtCompletion':unavailable,'modulePlans':plans,'stages':stages,'outputs':outputs,'outputArchives':archives,'finalCustody':final_custody,'independentTestCompilerFailures':independent_test_failures,'desktopCompilerFailures':desktop_compiler_failure,'desktopRuntimeOutputFailures':runtime_output_failures,'owningTestRuntimeOutputFailures':test_runtime_output_failures,'actualDesktopRuntimeClosureManifest':runtime_closure,'actualOwningTestRuntimeClosureManifests':test_runtime_closures,'actualPluginFixtureRuntimeClosureManifest':plugin_runtime_closure,'actualPluginFixtureStageProofs':fixture_stage_proofs,'actualFixtureExecAuditObserver':fixture_exec_audit_observer,'requiredLaterNormalInputsExplicitlyRetained':sorted(required_later_normal_inputs),'genuineUnchangedC43TestRuntimeClosureManifests':reused_c43_test_closures,'exactDesktopRuntimeClosureRestorer':pin(runtime_restorer),'exactOwningTestRuntimeClosureRestorer':pin(test_runtime_restorer),'restoredActualQualifiedComponentGeneratedProducts':restored_current_parent,'wholeHistoricalC43InputCurrentDisposition':historical_inputs,'wholeSuccessfulCurrent59InputCurrentDisposition':current59_input_dispositions,'historicalNormalRuntimePackageCopyRows':list(historical_package_rows.values()),'releasedNewTaskOwnedRuntimeCopies':released_runtime_rows,'wholeReleasedCopyRestorationRevalidation':released_checked,'runtimePhysicalAbsenceExplicitlyQualified':True,'exactCurrentControllingSourceOverrides':current_ticket_overrides,'completeInheritedPreferredSourceUnion':complete_preferred_union,'historicalCompletePreferredSourceUnion':historical_preferred_union,'explicitReviewedSourceRetirements':retirement_rows,'packagePins':package_pins,'missingPackages':missing,'metadataXmlAuthorityCount':len(metadata_xml_authorities),'reviewedMetadataXmlExceptions':xml_exceptions,'generatedOwningLayoutSuccessor':generated_layout_successor,'actualUnqualifiedDurableNormalFileCustody':unqualified_durable_normal_files,'actualDurableNormalLayouts':durable_normal_layouts,'actualGeneratedNormalPrerequisiteAliases':normal_alias_rows,'durableNormalAllocation':durable_allocation,'actualCapacityMeasurements':capacity_measurements,'actualCapacityBeforeFinalReceipt':capacity_sample('before-targeted-final-receipt'),'remainingCombinedFloorBytes':floor,'physicalNamespaceAdmission':pin(namespace_receipt),'namespace':str(scope),'exactResourceCustodyHelper':pin(custody_helper),'closedLayerArchiveRegistry':pin(closed_layer_registry),'immutableGitBlobInputs':list(immutable_git_blob_inputs.values()),'immutableGitBlobInputsVerifiedAtCompletion':not unavailable,'actualParentArchiveTransports':archive_transports,'actualPrimaryException':targeted_failure,'compilerOnly':True,'testsExecuted':False,'testDiscoveryExecuted':False,'modelExecuted':False,'consoleExecuted':False,'networkFetchAllowed':False,'qualification':'Actual Root-ticket integrated source from C46 onward is compiled from genuine successful current59 fullclosed custody. Application, Dev, Desktop, Core.Tests, Dev.Tests and Desktop.Tests require fresh Csc; other existing22 owners require fresh Csc whenever exact current whole source/resource/ref/analyzer comparison differs. Every reused product keeps its original compiled source head and successful compiler-stage chain; historical failed55/58 and original C43 successful baseline remain distinct. Current Desktop plus all eight owning test normal closures and genuine Fixture are generated from actual SDK targets with whole disk/allocation/package/archive/alias/retirement custody. New external app projects require their separate normal owner build and are not enrolled into these22. Original Console14, Core27 and Dev13 controls remain unexecuted until the separate Root actual runtime interval. All original source/ref/compiler/lifetime/native/licence gates are retained; source preparation issues no runtime, model, CLI or authority acceptance.'})
 final_capacity=capacity_sample('after-final-receipt');assert final_capacity['combinedAvailableBytes']>=floor
 disk_after_receipt=disk_capacity_sample('actual-durable-normal-after-final-receipt')
 final_observation=json.dumps({'receipt':receipt,'actualMeasurement':final_capacity,'actualSeparateDiskMeasurement':disk_after_receipt,'declaredDiskEnvelope':disk_envelope,'diskSpareActuallyObserved':True,'floorActuallyObserved':True},separators=(',',':')).encode()
 final_observation_blocks=((len(final_observation)+4095)//4096)*4096
 validate_disk_sample(disk_after_receipt,final_observation_blocks)
 guarded_write(out/'FINAL-ACTUAL-CAPACITY-OBSERVATION62.json',final_observation)
 print('CURRENT_TARGETED65_ORIGINAL_TERMINAL',code,'INPUTS_UNCHANGED',not changed and not unavailable,'RECEIPT',json.dumps(receipt),flush=True)
sys.exit(code)
