"""Pinned existing artifact/source reuse only. No .NET build, credentials, or app-source changes."""
import hashlib,json,os,pathlib,re,shutil,stat,subprocess,urllib.request,urllib.error,urllib.parse,zipfile
root=pathlib.Path.cwd();out=root/'artifacts/reuse-input';out.mkdir(parents=True,exist_ok=True)
BUILD_HEAD='d3e265272f410e0e1323b87b30971ad647d0c371';BUILD_RUN='36903571809';BUILD_MANIFEST='653c1bc219797b96e2190ad45561a79ac8bdd887dbdb97e2bee4a029348b94ee'
ARTIFACT_ID=11183780711;ARCHIVE_BYTES=118239638;ARCHIVE_SHA='37145cb6f1339dd3b7c347dbbf1eebe801bf68c233fadb3636fcf2b0b8b90237'
APK_SHA='faf45b3d1521fdc5d199be9722d41f9604aaf8170aef6e01ab605426c97e122c';APK_BYTES=118833539;CERT='2e2193e3754cd3e2d7deb586b87560a681b13eb10029dbeb9029ce286fdbb0c6'
def sha(path):
 with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def git(*args,cwd=root):return subprocess.check_output(['git',*args],cwd=cwd,text=True).strip()
metadataHead=os.environ['EXPECTED_COMMIT'];manifest=root/'.github/validation/astra-android08-smoke-reuse.json'
assert re.fullmatch('[0-9a-f]{40}',metadataHead) and metadataHead==os.environ['GITHUB_SHA']==git('rev-parse','HEAD')
assert os.environ['GITHUB_REF'].startswith('refs/heads/validation/astra-android29-frozen91-')
assert sha(manifest)==os.environ['EXPECTED_MANIFEST_SHA256'];m=json.loads(manifest.read_text())
allowed={'.github/workflows/build-haven-android.yml','.github/scripts/astra-android32-two-launch.py','.github/scripts/astra-android-activity-observation.py','.github/scripts/astra-android08-reuse.py','.github/validation/astra-android08-smoke-reuse.json'}
assert set(git('diff','--name-only',BUILD_HEAD,'HEAD').splitlines())==allowed,'APK program source must remain identical'
assert not git('diff','--name-only','HEAD') and not git('diff','--cached','--name-only')
assert len(m['metadataFiles'])==4 and {entry['path'] for entry in m['metadataFiles']}==allowed-{str(manifest.relative_to(root))}
for entry in m['metadataFiles']:
 path=root/entry['path'];assert not path.is_symlink() and path.is_file() and entry['mode']=='100644'
 assert git('ls-files','-s','--',entry['path']).startswith('100644 ') and sha(path)==entry['sha256']
source=pathlib.Path(os.environ['RUNNER_TEMP']).resolve()/'build08-source';assert not source.exists()
subprocess.run(['git','clone','--shared','--no-checkout',str(root),str(source)],check=True)
subprocess.run(['git','checkout','--detach',BUILD_HEAD],cwd=source,check=True)
assert git('rev-parse','HEAD',cwd=source)==BUILD_HEAD and not git('diff','--name-only','HEAD',cwd=source)
assert sha(source/'.github/validation/astra-android29-frozen91.json')==BUILD_MANIFEST
# Original source verifier is exact build08 Git bytes, never silently replaced by metadata branch.
originalVerifier=source/'.github/scripts/astra-android29-verify.py';assert originalVerifier.read_bytes()==subprocess.check_output(['git','show',BUILD_HEAD+':.github/scripts/astra-android29-verify.py'],cwd=root)
env=dict(os.environ,GITHUB_SHA=BUILD_HEAD,EXPECTED_COMMIT=BUILD_HEAD,EXPECTED_MANIFEST_SHA256=BUILD_MANIFEST,GITHUB_RUN_ID=BUILD_RUN)
with (out/'original-source-verifier.log').open('wb') as log:subprocess.run(['python3',str(originalVerifier),'source'],cwd=source,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
assert os.environ['GITHUB_REPOSITORY']=='CroakyJake12/9to1'
api='https://api.github.com/repos/CroakyJake12/9to1/actions/artifacts/'+str(ARTIFACT_ID)
headers={'Authorization':'Bearer '+os.environ['GH_ARTIFACT_TOKEN'],'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'}
with urllib.request.urlopen(urllib.request.Request(api,headers=headers),timeout=60) as response:
 raw=response.read(1048577);assert len(raw)<=1048576;info=json.loads(raw)
assert info['id']==ARTIFACT_ID and not info['expired'] and info['name']=='Astra-Android29-APK-'+BUILD_HEAD and info['size_in_bytes']==ARCHIVE_BYTES and info['digest']=='sha256:'+ARCHIVE_SHA
assert str(info['workflow_run']['id'])==BUILD_RUN and info['workflow_run']['head_sha']==BUILD_HEAD and info['workflow_run']['repository_id']==info['workflow_run']['head_repository_id']==1378313608
(out/'artifact-metadata.json').write_text(json.dumps(info,indent=2)+'\n')
class NoRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,*args,**kwargs):return None
try:urllib.request.build_opener(NoRedirect()).open(urllib.request.Request(api+'/zip',headers=headers),timeout=60)
except urllib.error.HTTPError as error:
 assert error.code==302;location=error.headers['Location'];error.close()
else:raise RuntimeError('missing exact artifact redirect')
parsed=urllib.parse.urlparse(location);assert parsed.scheme=='https' and not parsed.username and not parsed.password and parsed.hostname and (parsed.hostname.endswith('.blob.core.windows.net') or parsed.hostname.endswith('.actions.githubusercontent.com'))
# No GitHub token sent to the archive host; signed redirect is never logged or persisted.
archive=out/'original-apk-artifact.zip';h=hashlib.sha256();count=0
with urllib.request.urlopen(location,timeout=120) as response,archive.open('xb') as stream:
 while data:=response.read(1048576):
  count+=len(data);assert count<=ARCHIVE_BYTES;h.update(data);stream.write(data)
assert count==ARCHIVE_BYTES and h.hexdigest()==ARCHIVE_SHA
files={'android29-frozen91-Signed.apk','build-receipt.json','certificate.txt','badging.txt'};destination=source/'artifacts/apk';destination.mkdir(parents=True)
with zipfile.ZipFile(archive) as z:
 assert len(z.namelist())==len(files) and set(z.namelist())==files and z.testzip() is None
 for entry in z.infolist():
  assert not entry.flag_bits&1 and not stat.S_ISLNK(entry.external_attr>>16)
  assert entry.file_size==(APK_BYTES if entry.filename.endswith('.apk') else entry.file_size) and entry.file_size<=(APK_BYTES if entry.filename.endswith('.apk') else 1048576)
  with z.open(entry) as incoming,(destination/entry.filename).open('xb') as outgoing:shutil.copyfileobj(incoming,outgoing)
receipt=json.loads((destination/'build-receipt.json').read_text());expected={'commit':BUILD_HEAD,'manifestSha256':BUILD_MANIFEST,'runId':BUILD_RUN,'apkSha256':APK_SHA,'apkBytes':APK_BYTES,'certificateSha256':CERT,'package':'com.cakemods.haven','abis':['arm64-v8a','x86_64'],'signing':'ephemeral-debug-only','versionCode':20001,'versionName':'0.2.1-mobile-preview'}
for key,value in expected.items():assert receipt[key]==value,key
assert sha(destination/'android29-frozen91-Signed.apk')==APK_SHA
(root/'artifacts/apk').mkdir(parents=True)
for filename in files:os.link(destination/filename,root/'artifacts/apk'/filename)
(out/'reuse-receipt.json').write_text(json.dumps({'buildReceipt':receipt,'archiveSha256':ARCHIVE_SHA,'archiveBytes':count,'artifactId':ARTIFACT_ID,'originalVerifierSha256':sha(originalVerifier),'smokeMetadataCommit':metadataHead,'smokeRun':os.environ['GITHUB_RUN_ID'],'sourceRoot':str(source),'qualification':'Existing build08 source/artifact reused; no rebuild or new APK provenance'},indent=2)+'\n')
