"""ROOT EXECUTED ONLY after independently retaining all uploaded artifacts. No overwrite/delete."""
import argparse,pathlib,json,hashlib,zipfile,os,shutil
p=argparse.ArgumentParser();p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);p.add_argument('--commit',required=True);p.add_argument('--run-id',required=True);p.add_argument('--bundles',required=True);p.add_argument('--output',required=True);a=p.parse_args()
manifest=pathlib.Path(a.manifest);assert hashlib.sha256(manifest.read_bytes()).hexdigest()==a.manifest_sha
m=json.loads(manifest.read_text());assert m['commit']==a.commit and m['runId']==a.run_id
out=pathlib.Path(a.output).resolve();bundles=pathlib.Path(a.bundles).resolve();assert not out.exists();total=sum(x['bytes'] for x in m['files'])
def capacity(growth=0):
 workspaceGrowth=growth if out.is_relative_to(pathlib.Path('/workspace')) else 0
 tmpGrowth=growth if out.is_relative_to(pathlib.Path('/tmp')) else 0
 if not (out.is_relative_to(pathlib.Path('/workspace')) or out.is_relative_to(pathlib.Path('/tmp'))):raise SystemExit('output must use declared capacity volumes')
 if shutil.disk_usage('/workspace').free<943718400+workspaceGrowth+1048576 or shutil.disk_usage('/tmp').free<1073741824+tmpGrowth+1048576:raise SystemExit('unchanged capacity floor/reservation not met')
capacity(total);pieces={}
for bundle in m['bundles']:
 path=bundles/bundle['file'];assert path.is_file() and not path.is_symlink() and path.stat().st_size==bundle['bytes'] and path.stat().st_size<=30*1024*1024
 with path.open('rb') as stream:assert hashlib.file_digest(stream,'sha256').hexdigest()==bundle['sha256']
 expected={x['piece']:x for f in m['files'] for x in f['pieces'] if x['artifactPart']==bundle['artifactPart']}
 with zipfile.ZipFile(path) as z:
  assert len(z.namelist())==len(expected) and set(z.namelist())==set(expected) and z.testzip() is None
  for name,x in expected.items():
   data=z.read(name);assert len(data)==x['bytes'] and hashlib.sha256(data).hexdigest()==x['sha256'];pieces[(bundle['artifactPart'],name)]=(path,x)
capacity();out.mkdir();written=0
for record in m['files']:
 rel=pathlib.PurePosixPath(record['path']);assert not rel.is_absolute() and '..' not in rel.parts;target=out/rel;assert not target.exists();capacity();target.parent.mkdir(parents=True,exist_ok=True);h=hashlib.sha256();position=0
 with target.open('xb') as destination:
  for piece in record['pieces']:
   assert piece['offset']==position;archive,x=pieces[(piece['artifactPart'],piece['piece'])]
   with zipfile.ZipFile(archive) as z, z.open(piece['piece']) as stream:
    while data:=stream.read(1024*1024):capacity(len(data));destination.write(data);h.update(data);position+=len(data);written+=len(data)
 assert position==record['bytes'] and h.hexdigest()==record['sha256']
capacity();print(json.dumps({'status':'FULL REASSEMBLY VERIFIED EACH INDEPENDENT ARTIFACT','manifestSha256':a.manifest_sha,'commit':a.commit,'runId':a.run_id,'files':len(m['files']),'bytes':written,'qualification':'No crossartifact atomicity or native/managed execution inferred.'}))
