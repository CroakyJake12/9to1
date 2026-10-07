import gzip,hashlib,importlib.util,json,os,pathlib,sys,zipfile
assert len(sys.argv)==4,'Require exact test runtime manifest gzip, whole manifest SHA256 and absent task-private output directory.'
manifest=pathlib.Path(sys.argv[1]);expected_manifest_sha=sys.argv[2];destination=pathlib.Path(sys.argv[3])
assert destination.is_absolute() and not destination.exists() and not destination.is_symlink() and destination.resolve()==destination
encoded=manifest.read_bytes();assert hashlib.sha256(encoded).hexdigest()==expected_manifest_sha
value=json.loads(gzip.decompress(encoded))
assert value['status']=='ACTUAL_NORMAL_OWNING_TEST_RUNTIME_OUTPUT_CLOSURE_PASS' and value['noSecondCompiler']
assert value['testsExecuted'] is False and value['testDiscoveryExecuted'] is False
assert value['exactResourceCustodyHelper']['sha256']=='0d650d3f4be643adbaa60e1ee748389be7c869deb67feb959700f3c80fd732b2'
assert value['closedLayerArchiveRegistry']['sha256']=='d7b117d1d919bcdd9dbc95cefd9e241f02ab4194a3b2bbf7c4793c32ccc2bfa4'
def verify(raw,pin):
 assert len(raw)==pin['bytes'] and hashlib.sha256(raw).hexdigest()==pin['sha256'],pin
def verify_file(path,pin):
 h=hashlib.sha256();n=0
 with pathlib.Path(path).open('rb') as f:
  for part in iter(lambda:f.read(1048576),b''):n+=len(part);h.update(part)
 assert n==pin['bytes'] and h.hexdigest()==pin['sha256'],pin
helper=pathlib.Path(value['exactResourceCustodyHelper']['path']);verify_file(helper,value['exactResourceCustodyHelper'])
spec=importlib.util.spec_from_file_location('exact_test_runtime_custody',helper);lib=importlib.util.module_from_spec(spec);spec.loader.exec_module(lib)
registry_path=pathlib.Path(value['closedLayerArchiveRegistry']['path']);verify_file(registry_path,value['closedLayerArchiveRegistry']);registry=json.loads(registry_path.read_bytes())
checked_containers={}
def decode(row):
 source=row['restoration']
 if source['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':
  container=pathlib.Path(source['container']['path'])
  if str(container) not in checked_containers:verify_file(container,source['container']);checked_containers[str(container)]=source['container']
  else:assert checked_containers[str(container)]==source['container']
  with zipfile.ZipFile(container) as archive:
   info=archive.getinfo(source['member']);assert info.file_size==source['memberBytes'] and info.CRC==source['memberCRC32'];body=archive.read(info)
 elif source['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE':
  item=source['row']
  if item.get('encoding')==lib.SCHEMA:body=lib.decode_archive(item,registry)
  else:
   original_encoded,_=lib.read_original_gzip(item['archive']['path'],item['archive'],registry);body=gzip.decompress(original_encoded)
 else:raise AssertionError(source)
 verify(body,row['original']);return body
seen=set()
for row in value['files']:
 relative=pathlib.PurePosixPath(row['relative'])
 assert not relative.is_absolute() and '..' not in relative.parts and str(relative) not in seen;seen.add(str(relative))
 decode(row)
assert len(seen)==value['fileCount']
assert set(value['requiredActualRuntimeFiles']).issubset(seen)
assembly=value['actualAssemblyFile'];assert pathlib.PurePosixPath(assembly).name==assembly and assembly.endswith('.dll')
assembly_row=next(row for row in value['files'] if row['relative']==assembly)
assert assembly_row['original']['sha256']==value['actualCompiledAssembly']['sha256']
verify_file(value['dotnet']['path'],value['dotnet'])
destination.mkdir(mode=0o700)
for row in value['files']:
 body=decode(row);p=destination/row['relative'];p.parent.mkdir(parents=True,exist_ok=True)
 with p.open('xb') as f:f.write(body);f.flush();os.fsync(f.fileno())
 os.chmod(p,row['mode']&0o7777);os.utime(p,ns=(row['mtimeNs'],row['mtimeNs']));verify_file(p,row['original'])
print(json.dumps({'status':'ENTIRE_NORMAL_OWNING_TEST_RUNTIME_CLOSURE_RESTORED_AND_WHOLE_VERIFIED','module':value['module'],'targetFramework':value['normalTargetFramework'],'files':len(value['files']),'bytes':sum(row['original']['bytes'] for row in value['files']),'destination':str(destination),'supportedRunnerCommand':[value['dotnet']['path'],'vstest',str(destination/assembly),'/Tests:<explicit-qualified-test-names>'],'qualification':'Exact byte restoration only. No discovery/test/model execution, platform compatibility, actor/grant or accepted Task is established by restoration.'}),flush=True)
