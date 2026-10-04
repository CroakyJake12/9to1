import hashlib,io,json,pathlib,sys,zipfile
custody=pathlib.Path(sys.argv[1]); report=json.loads(pathlib.Path(sys.argv[2]).read_text()); index=json.loads((custody/'manifest.json').read_text()); errors=[]; pieces={}
for part in report['parts']:
 original=custody/f"part{part['part']}-original.zip"
 if original.stat().st_size!=part['outerBytes'] or hashlib.sha256(original.read_bytes()).hexdigest()!=part['outerSha256']: errors.append('outer:'+str(part['part']))
 with zipfile.ZipFile(original) as outer:
  nested=outer.read(outer.namelist()[0])
  if len(nested)!=part['innerBytes'] or hashlib.sha256(nested).hexdigest()!=part['innerSha256']:errors.append('inner:'+str(part['part']))
  with zipfile.ZipFile(io.BytesIO(nested)) as archive:
   by={x['piece']:x for f in index['files'] for x in f['pieces'] if x['artifactPart']==part['part']}
   if set(archive.namelist())!=set(by):errors.append('piece-set')
   for name in archive.namelist():
    data=archive.read(name); x=by[name]; pieces[(part['part'],name)]=data
    if len(data)!=x['bytes'] or hashlib.sha256(data).hexdigest()!=x['sha256']:errors.append(name)
for f in index['files']:
 data=b''.join(pieces[(x['artifactPart'],x['piece'])] for x in sorted(f['pieces'],key=lambda x:x['offset']))
 if len(data)!=f['bytes'] or hashlib.sha256(data).hexdigest()!=f['sha256']:errors.append(f['path'])
print(json.dumps({'pieces':len(pieces),'descriptors':len(index['files']),'errors':errors,'scope':'custody only; no payload execution or native acceptance'}));sys.exit(bool(errors))
