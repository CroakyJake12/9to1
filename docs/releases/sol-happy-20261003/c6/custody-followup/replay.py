import hashlib,io,json,pathlib,sys,zipfile
custody=pathlib.Path(sys.argv[1]);report=json.loads(pathlib.Path(sys.argv[2]).read_text());errors=[];sha=lambda b:hashlib.sha256(b).hexdigest()
for a in report['artifacts']:
 original=custody/(a['kind']+'-original.zip')
 if original.stat().st_size!=a['outerBytes'] or sha(original.read_bytes())!=a['outerSha256']:errors.append(a['kind']+':outer')
 with zipfile.ZipFile(original) as outer:
  if 'innerSha256' in a:
   nested=outer.read(outer.namelist()[0])
   if len(nested)!=a['innerBytes'] or sha(nested)!=a['innerSha256']:errors.append(a['kind']+':inner')
   archive=zipfile.ZipFile(io.BytesIO(nested))
  else:archive=outer
  by={x['name']:x for x in a['members']}
  if set(archive.namelist())!=set(by):errors.append(a['kind']+':member-set')
  for name in archive.namelist():
   data=archive.read(name);x=by[name]
   if len(data)!=x['bytes'] or sha(data)!=x['sha256']:errors.append(a['kind']+':'+name)
  print(a['kind'],len(by),'CRC/SHA verified members')
print(json.dumps({'errors':errors,'scope':'original custody only; no payload execution or native acceptance'}));sys.exit(bool(errors))
