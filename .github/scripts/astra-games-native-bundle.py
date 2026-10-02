import pathlib,zipfile,hashlib,json
src=pathlib.Path('artifacts/games-normals-native');dst=pathlib.Path('artifacts/games-normals-native-bounded');dst.mkdir(parents=True,exist_ok=True)
archive=dst/'original-native-evidence.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED)as z:
 for p in sorted(src.rglob('*')):
  if p.is_file():z.write(p,str(p.relative_to(src)))
def sha(p):
 with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
entries=[]
with zipfile.ZipFile(archive)as z:
 for i in z.infolist():
  with z.open(i)as f:d=hashlib.file_digest(f,'sha256').hexdigest()
  entries.append({'path':i.filename,'bytes':i.file_size,'crc32':i.CRC,'sha256':d})
parts=[]
with archive.open('rb')as f:
 while b:=f.read(24*1024*1024):
  assert len(parts)<32
  p=dst/('part-%03d.bin'%len(parts));p.write_bytes(b);parts.append({'path':p.name,'bytes':len(b),'sha256':sha(p)})
(dst/'receipt.json').write_text(json.dumps({'originalSha256':sha(archive),'originalBytes':archive.stat().st_size,'entries':entries,'parts':parts},indent=2)+'\n')
small=pathlib.Path('artifacts/games-normals-native-small');small.mkdir(parents=True,exist_ok=True)
used=0
for p in sorted(src.glob('*.log')):
 if p.stat().st_size<=4*1024*1024 and used+p.stat().st_size<=20*1024*1024:
  (small/p.name).write_bytes(p.read_bytes());used+=p.stat().st_size
(small/'receipt.json').write_bytes((dst/'receipt.json').read_bytes())
archive.unlink()
