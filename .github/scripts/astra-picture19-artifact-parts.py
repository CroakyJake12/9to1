"""Source-only hosted artifact splitter. Each upload directory stays below26MiB; no deletion."""
import argparse,pathlib,hashlib,json,os,zipfile
p=argparse.ArgumentParser();p.add_argument('--input',required=True);p.add_argument('--output',required=True);a=p.parse_args();source=pathlib.Path(a.input).resolve();out=pathlib.Path(a.output).resolve();limit=26*1024*1024
if not source.is_dir() or out.exists() or out.is_relative_to(source):raise SystemExit('new disjoint output required')
out.mkdir(parents=True);files=[];pieces=[];index=0;used=0;pieceNumber=0;identities={}
for file in sorted(source.rglob('*')):
 if file.is_symlink():raise SystemExit('artifact symlink rejected')
 if not file.is_file():continue
 relative=str(file.relative_to(source));before=file.stat();identities[relative]=(before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns);h=hashlib.sha256();filePieces=[]
 with file.open('rb') as stream:
  while data:=stream.read(limit-used):
   if index>=12:raise SystemExit('bounded maximum12 independent artifacts exceeded')
   directory=out/('part-%02d'%index);directory.mkdir(exist_ok=True);piece=directory/('piece-%06d.part'%pieceNumber);piece.write_bytes(data);digest=hashlib.sha256(data).hexdigest();h.update(data)
   record={'artifactPart':index,'piece':piece.name,'path':relative,'offset':sum(x['bytes'] for x in filePieces),'bytes':len(data),'sha256':digest};filePieces.append(record);pieces.append(record);pieceNumber+=1;used+=len(data)
   if used==limit:index+=1;used=0
 after=file.stat()
 if (before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns)!=(after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns):raise SystemExit('input changed during chunking')
 if sum(x['bytes'] for x in filePieces)!=before.st_size:raise SystemExit('piece total mismatch')
 with file.open('rb') as stream:
  if hashlib.file_digest(stream,'sha256').hexdigest()!=h.hexdigest():raise SystemExit('input bytes changed after chunking')
 files.append({'path':relative,'bytes':before.st_size,'sha256':h.hexdigest(),'pieces':filePieces})
manifest={'commit':os.environ['GITHUB_SHA'],'runId':os.environ['GITHUB_RUN_ID'],'status':'INDEPENDENT PARTS NO CROSSARTIFACT ATOMICITY','files':files,'partCount':index+(1 if used else 0),'maxPayloadBytes':limit,'qualification':'Every independent downloaded artifact must verify ZIP CRC/path set and its piece SHA; reassemble in offsets then require exact fullfile bytes/SHA. Missing/foreign parts unavailable, never accepted partial closure.'}
bundles=[]
for i in range(manifest['partCount']):
 directory=out/('part-%02d'%i);archive=out/('bundle-%02d.zip'%i)
 with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_STORED,allowZip64=True) as z:
  for file in sorted(directory.iterdir()):z.write(file,file.name)
 if archive.stat().st_size>29*1024*1024:raise SystemExit('independent artifact ZIP exceeds29MiB (outerartifact30MiB reserve)')
 with zipfile.ZipFile(archive) as z:
  expected={x['piece']:x for x in pieces if x['artifactPart']==i}
  if len(z.namelist())!=len(expected) or set(z.namelist())!=set(expected) or z.testzip() is not None:raise SystemExit('bundle CRC/set mismatch')
  for name in z.namelist():
   data=z.read(name)
   if len(data)!=expected[name]['bytes'] or hashlib.sha256(data).hexdigest()!=expected[name]['sha256']:raise SystemExit('bundle piece mismatch')
 with archive.open('rb') as stream:bundles.append({'artifactPart':i,'file':archive.name,'bytes':archive.stat().st_size,'sha256':hashlib.file_digest(stream,'sha256').hexdigest()})
manifest['bundles']=bundles
if any(p.is_symlink() for p in source.rglob('*')):raise SystemExit('input symlink introduced during bundling')
actualSet={str(p.relative_to(source)) for p in source.rglob('*') if p.is_file()}
if actualSet!={x['path'] for x in files}:raise SystemExit('source evidence set changed')
for record in files:
 file=source/record['path'];current=file.stat()
 if file.is_symlink() or (current.st_dev,current.st_ino,current.st_size,current.st_mtime_ns)!=identities[record['path']]:raise SystemExit('input identity changed after bundling')
 with file.open('rb') as stream:
  if file.stat().st_size!=record['bytes'] or hashlib.file_digest(stream,'sha256').hexdigest()!=record['sha256']:raise SystemExit('source evidence changed after bundling')
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if os.environ.get('GITHUB_OUTPUT'):
 with open(os.environ['GITHUB_OUTPUT'],'a') as output:output.write('count='+str(manifest['partCount'])+'\n')
print(json.dumps({'count':manifest['partCount'],'manifestSha256':hashlib.sha256((out/'manifest.json').read_bytes()).hexdigest()}))
