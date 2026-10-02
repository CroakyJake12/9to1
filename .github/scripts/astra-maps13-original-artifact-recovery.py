"""Read only exact existing original artifact; no SDK, native rerun or acceptance."""
import argparse,os,pathlib,json,hashlib,urllib.request,zipfile
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();out=root/'artifacts/maps-artifact-recovery';out.mkdir(parents=True,exist_ok=True)
sha=lambda data:hashlib.sha256(data).hexdigest()
import subprocess
assert subprocess.check_output(['git','rev-parse','HEAD']).decode().strip()==a.expected_commit
manifest=root/a.manifest;assert sha(manifest.read_bytes())==a.manifest_sha
cut=json.loads(manifest.read_text())
for row in cut['files']:
 f=root/row['path'];assert f.is_file()and sha(f.read_bytes())==row['sha256'],row['path']
artifactId=11238533263;expectedSha='997b962db9d8e16e15178144e722c0c57e0546c04fd7251e316c9505acf1f9aa';expectedBytes=55825366
api='https://api.github.com/repos/'+os.environ['GITHUB_REPOSITORY']+'/actions/artifacts/'+str(artifactId)
headers={'Authorization':'Bearer '+os.environ['GITHUB_TOKEN'],'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'}
with urllib.request.urlopen(urllib.request.Request(api,headers=headers),timeout=30)as response:metadata=json.load(response)
assert os.environ['GITHUB_REPOSITORY']=='CroakyJake12/9to1'
assert metadata['id']==artifactId and metadata['size_in_bytes']==expectedBytes and not metadata['expired'] and metadata['digest']=='sha256:'+expectedSha
assert metadata['workflow_run']['id']==37032719199 and metadata['workflow_run']['head_sha']=='4ab1cccc8cde2034f47f6cb81ace8711ff7938e4'
(out/'actual-original-api-metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
# Handle authenticated GitHub redirect explicitly; never forward bearer token to storage URL.
class NoRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,*args):return None
opener=urllib.request.build_opener(NoRedirect)
try:
 response=opener.open(urllib.request.Request(api+'/zip',headers=headers),timeout=30)
except urllib.error.HTTPError as error:
 if error.code not in (301,302,303,307,308):raise
 location=error.headers['Location'];assert location.startswith('https://');response=urllib.request.urlopen(location,timeout=60)
archive=out/'original-artifact.zip';digest=hashlib.sha256();count=0
with response,archive.open('wb')as stream:
 while data:=response.read(1024*1024):
  count+=len(data);assert count<=expectedBytes;digest.update(data);stream.write(data)
assert count==expectedBytes and digest.hexdigest()==expectedSha
entries=[];seen=set();logs=[]
with zipfile.ZipFile(archive)as z:
 for entry in z.infolist():
  rel=pathlib.PurePosixPath(entry.filename);assert not rel.is_absolute()and '..'not in rel.parts and str(rel)==entry.filename and entry.filename not in seen;seen.add(entry.filename)
  if entry.is_dir():continue
  digest=hashlib.sha256();size=0
  with z.open(entry)as source:
   while data:=source.read(1024*1024):size+=len(data);digest.update(data)
  assert size==entry.file_size
  entries.append({'path':entry.filename,'bytes':size,'sha256':digest.hexdigest(),'crc32':entry.CRC,'zipOffset':entry.header_offset})
  if rel.suffix in ('.log','.trx') and entry.file_size<=4*1024*1024 and sum(x['bytes'] for x in logs)+entry.file_size<=12*1024*1024:
   name='diagnostic-%04d%s'%(len(logs),rel.suffix)
   (out/name).write_bytes(z.read(entry));logs.append({'originalPath':entry.filename,'exposedPath':name,'bytes':entry.file_size,'sha256':digest.hexdigest()})
assert logs, 'Original archive has no bounded diagnostic logs/TRX'
parts=[];offset=0
with archive.open('rb')as stream:
 while data:=stream.read(24*1024*1024):
  name='original-artifact.part-%03d.bin'%len(parts);(out/name).write_bytes(data);parts.append({'path':name,'offset':offset,'bytes':len(data),'sha256':sha(data)});offset+=len(data)
assert offset==expectedBytes
(out/'original-recovery-receipt.json').write_text(json.dumps({'status':'EXACT_ORIGINAL_ZIP_SHA_ALL_ENTRY_SHA_AND_CRC_VERIFIED_EVIDENCE_RECOVERY_ONLY','sourceArtifactId':artifactId,'originalSha256':expectedSha,'originalBytes':expectedBytes,'originalEntries':entries,'parts':parts,'exposedLogs':logs,'limits':'Existing Maps failed artifact only; no tests rerun or acceptance. Original exact bytes remotely retained in boundedparts; restore error from genuine original log only.'},indent=2)+'\n')
archive.unlink() # Duplicate original is losslessly retained by every exact contiguous 24MiB part.
