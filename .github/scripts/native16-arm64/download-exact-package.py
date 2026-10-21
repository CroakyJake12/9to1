"""One supported Actions artifact download, pinned metadata, strict provider ZIP + inner .deb verification. No rebuild."""
from pathlib import Path
import os,subprocess,json,hashlib,zipfile,shutil,re,datetime
REPO='CroakyJake12/9to1';RUN=39593676167;ART=12342954132;HEAD='ab7bc6b8b2d72a0c97d3b56423d93e0f4f5a466a';ZIP_SHA='a041b08868b7bb3300b2b0b2506aac6c0f215da801697e60cbb856fe9c389e4e';NAME='9to1-native16-arm64-internal-package'
out=Path(os.environ['GITHUB_WORKSPACE'])/'evidence';out.mkdir(exist_ok=True);work=Path(os.environ['RUNNER_TEMP'])/'9to1-exact-chat-package';work.mkdir();archive=work/'provider.zip'
def api(route):return json.loads(subprocess.check_output(['gh','api','repos/'+REPO+'/'+route]))
def save(n,d):(out/n).write_text(json.dumps(d,indent=2)+'\n')
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
run=api('actions/runs/'+str(RUN));artifact=api('actions/artifacts/'+str(ART));save('source-run.json',run);save('source-artifact.json',artifact)
assert run['id']==RUN and run['head_sha']==HEAD and run['status']=='completed';assert artifact['id']==ART and artifact['name']==NAME and not artifact['expired'];assert artifact['workflow_run']['id']==RUN and artifact['workflow_run']['head_sha']==HEAD;assert artifact['digest']=='sha256:'+ZIP_SHA
with archive.open('wb') as f:subprocess.run(['gh','api','repos/'+REPO+'/actions/artifacts/'+str(ART)+'/zip'],stdout=f,check=True)
assert archive.stat().st_size==560388221
assert sha(archive)==ZIP_SHA,'Actual provider ZIP differs from exact pinned artifact digest'
with zipfile.ZipFile(archive) as z:
 members=z.infolist();assert all(not Path(i.filename).is_absolute() and '..' not in Path(i.filename).parts and not ((i.external_attr>>16)&0o170000)==0o120000 for i in members)
 selected={}
 for key,condition in [('deb',lambda n:n.endswith('.deb')),('receipt',lambda n:Path(n).name=='package-receipt.json'),('sums',lambda n:Path(n).name=='SHA256SUMS')]:
  hits=[i for i in members if not i.is_dir() and condition(i.filename)];assert len(hits)==1,(key,len(hits));i=hits[0];p=work/Path(i.filename).name
  with z.open(i) as source,p.open('wb') as target:shutil.copyfileobj(source,target,1024*1024)
  selected[key]=p
receipt=json.loads(selected['receipt'].read_text());deb=selected['deb'];digest=sha(deb);assert digest=='5c3e76afc08028041efb11c984cdb541217132f7ed3269f9916c4bfbc1d50ec5';assert deb.stat().st_size==560510220;assert digest==receipt['sha256'] and deb.stat().st_size==receipt['bytes'];assert receipt['architecture']=='arm64' and receipt['source_head']==HEAD;assert receipt['source_patch_sha256']=='b95d25560d8ecd5459c4e2a3bab715c287bc34ef6f9c7cf9105c4ce22f1b3f24';assert digest in selected['sums'].read_text()
fields={k:subprocess.check_output(['dpkg-deb','--field',str(deb),k],text=True).strip() for k in ['Package','Architecture','Version']};assert fields['Package']=='9to1-preview' and fields['Architecture']=='arm64';assert fields['Version'].startswith('0.1.0~lesson.preview.arm64.20261021.')
shutil.copy2(selected['receipt'],out/'downloaded-package-receipt.json');shutil.copy2(selected['sums'],out/'downloaded-SHA256SUMS');archive.unlink() # no redundant retained 560MB ZIP
save('exact-package-download.json',{'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_run':RUN,'source_run_conclusion':run['conclusion'],'source_head':HEAD,'artifact_id':ART,'artifact_name':NAME,'provider_zip_sha256':ZIP_SHA,'provider_zip_verified_then_removed':True,'package_sha256':digest,'package_bytes':deb.stat().st_size,'deb_fields':fields,'SDK_rebuild_or_model_download':False})
with open(os.environ['GITHUB_ENV'],'a') as f:f.write('EXACT_PREVIEW_DEB='+str(deb)+'\n')
print(json.dumps({'artifact_id':ART,'package_sha256':digest,'architecture':fields['Architecture'],'source_run_conclusion':run['conclusion']}))
