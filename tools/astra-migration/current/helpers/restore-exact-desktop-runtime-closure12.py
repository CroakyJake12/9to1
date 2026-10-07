import gzip,hashlib,importlib.util,json,os,pathlib,sys,zipfile
assert len(sys.argv)==3,'Require exact runtime manifest gzip and absent task-private output directory.'
manifest=pathlib.Path(sys.argv[1]);destination=pathlib.Path(sys.argv[2])
assert destination.is_absolute() and not destination.exists() and not destination.is_symlink()
def verify(raw,pin):
 assert len(raw)==pin['bytes'] and hashlib.sha256(raw).hexdigest()==pin['sha256'],pin
raw=gzip.decompress(manifest.read_bytes());value=json.loads(raw)
assert value['status']=='ACTUAL_NORMAL_DESKTOP_RUNTIME_OUTPUT_CLOSURE_PASS'
assert value['exactResourceCustodyHelper']['sha256']=='0d650d3f4be643adbaa60e1ee748389be7c869deb67feb959700f3c80fd732b2'
assert value['closedLayerArchiveRegistry']['sha256']=='d7b117d1d919bcdd9dbc95cefd9e241f02ab4194a3b2bbf7c4793c32ccc2bfa4'
helper=pathlib.Path(value['exactResourceCustodyHelper']['path']);verify(helper.read_bytes(),value['exactResourceCustodyHelper'])
spec=importlib.util.spec_from_file_location('exact_runtime_custody',helper);lib=importlib.util.module_from_spec(spec);spec.loader.exec_module(lib)
registry_path=pathlib.Path(value['closedLayerArchiveRegistry']['path']);verify(registry_path.read_bytes(),value['closedLayerArchiveRegistry']);registry=json.loads(registry_path.read_bytes())
seen=set();decoded=[]
for row in value['files']:
 relative=pathlib.PurePosixPath(row['relative'])
 assert not relative.is_absolute() and '..' not in relative.parts and str(relative) not in seen;seen.add(str(relative))
 source=row['restoration']
 if source['kind']=='EXACT_PACKAGE_NUPKG_MEMBER':
  container=pathlib.Path(source['container']['path']);verify(container.read_bytes(),source['container'])
  with zipfile.ZipFile(container) as archive:
   info=archive.getinfo(source['member']);assert info.file_size==source['memberBytes'] and info.CRC==source['memberCRC32'];body=archive.read(info)
 elif source['kind']=='EXACT_COMPILER_CUSTODY_ARCHIVE':
  item=source['row']
  if item.get('encoding')==lib.SCHEMA:body=lib.decode_archive(item,registry)
  else:
   encoded,_=lib.read_original_gzip(item['archive']['path'],item['archive'],registry);body=gzip.decompress(encoded)
 else:raise AssertionError(source)
 verify(body,row['original']);decoded.append((row,body))
destination.mkdir(mode=0o700)
for row,body in decoded:
 p=destination/row['relative'];p.parent.mkdir(parents=True,exist_ok=True)
 with p.open('xb') as f:f.write(body);f.flush();os.fsync(f.fileno())
 os.chmod(p,row['mode']&0o7777);os.utime(p,ns=(row['mtimeNs'],row['mtimeNs']));verify(p.read_bytes(),row['original'])
assert (destination/'Haven.dll').is_file() and (destination/'Haven.deps.json').is_file() and (destination/'Haven.runtimeconfig.json').is_file()
print(json.dumps({'status':'ENTIRE_NORMAL_DESKTOP_RUNTIME_CLOSURE_RESTORED_AND_WHOLE_VERIFIED','files':len(decoded),'bytes':sum(len(body) for _,body in decoded),'destination':str(destination),'launch':[value['dotnet']['path'],str(destination/'Haven.dll'),'--local-task-console','--data-directory','<private-absolute-data-root>'],'qualification':'Byte restoration only; no model, server, Home actor/consent or accepted Task is issued by this script.'}),flush=True)
