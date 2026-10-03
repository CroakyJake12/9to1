import hashlib,io,json,pathlib,sys,zipfile
custody=pathlib.Path(sys.argv[1]); report=json.loads(pathlib.Path(sys.argv[2]).read_text()); errors=[]
for kind,part in [('home',2),('canvas',1)]:
 expected=report[kind]; original=custody/(kind+'-original.zip'); index=json.loads((custody/(kind+'-manifest.json')).read_text())
 if original.stat().st_size!=expected['outerBytes'] or hashlib.sha256(original.read_bytes()).hexdigest()!=expected['outerSha256']: errors.append(kind+':outer')
 with zipfile.ZipFile(original) as outer:
  nested=outer.read(outer.namelist()[0])
  with zipfile.ZipFile(io.BytesIO(nested)) as archive:
   by={x['piece']:x for f in index['files'] for x in f['pieces'] if x['artifactPart']==part}
   if set(archive.namelist())!=set(by):errors.append(kind+':piece-set')
   for name in archive.namelist():
    data=archive.read(name); x=by[name]
    if len(data)!=x['bytes'] or hashlib.sha256(data).hexdigest()!=x['sha256']:errors.append(kind+':'+name)
 print(kind,'verified pieces',len(by))
print(json.dumps({'errors':errors,'scope':'custody bytes only; no runtime or native acceptance'}));sys.exit(bool(errors))
