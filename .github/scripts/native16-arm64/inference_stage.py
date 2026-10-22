#!/usr/bin/env python3
"""Pinned CI acquisition/data-only stage and bounded package merge. Never executes inference."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,shutil,stat,sys,tarfile,urllib.request,urllib.parse,zipfile
REPO='CroakyJake12/9to1';RUN=39562111673;ARTIFACT=12328502225
ARTIFACT_NAME='9to1-llamacpp-b10809-arm64-bookworm'
PROVIDER_ZIP_SHA='b596f767158d3a53de226bed35cf498d07f485e00da0cd4649331db988affa18'
RUN_HEAD='e3841dd89fc9be9d69882020d5ec8eb198606823'
INNER='llamacpp-b10809-linux-arm64-bookworm.tar.gz'
INNER_SHA='f34911ff90f7805dfbed221e0a8cabc7a8d71ea0ed4ab69b534657b204434256'
SOURCE_TAR='llama.cpp-5266f24da75dc449bd56cbed7addb9c8e4a6a73e-source.tar.gz'
SOURCE_SHA='8d9637a726a8de93e9037b8ea938f463ae6e0bd825177173f3a2be4c52cc6f00'
MODEL_SHA='1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b';MODEL_SIZE=495107776
MODEL_URL='https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf'
NOTICE_URL='https://huggingface.co/Qwen/Qwen3-0.6B/resolve/c1899de289a04d12100db370d81485cdf75e47ca/LICENSE'
NOTICE_SHA='832dd9e00a68dd83b3c3fb9f5588dad7dcf337a0db50f7d9483f310cd292e92e';NOTICE_SIZE=11343
DULCHE=Path('9to1 Workspace/Home/Source/Dulche')
def digest(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as f:
  for chunk in iter(lambda:f.read(1048576),b''):h.update(chunk)
 return h.hexdigest()
def verify_file(path,sha,size=None):
 p=Path(path)
 if p.is_symlink() or not p.is_file() or digest(p)!=sha or size is not None and p.stat().st_size!=size:raise ValueError('Pinned regular-file identity mismatch: '+str(p))
 return p
def no_symlink_ancestors(path):
 p=Path(path).absolute()
 for q in [p,*p.parents]:
  if q.is_symlink():raise ValueError('Symlink in input/output path: '+str(q))
def source_context(root):
 for row in json.loads((root/'.github/scripts/native16-arm64/inference-source-manifest.json').read_text())['files']:
  verify_file(root/row['path'],row['sha256'],row['size'])
 sys.path.insert(0,str(root/DULCHE))
def validate_artifact_metadata(meta):
 if (meta.get('id'),meta.get('name'),meta.get('digest'),meta.get('expired'))!=(ARTIFACT,ARTIFACT_NAME,'sha256:'+PROVIDER_ZIP_SHA,False):raise ValueError('Wrong/expired Bookworm provider artifact identity')
 run=meta.get('workflow_run',{})
 if run.get('id')!=RUN or run.get('head_sha')!=RUN_HEAD:raise ValueError('Wrong producing native run/head')
 return meta
class ArtifactRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,req,fp,code,msg,headers,newurl):
  if urllib.parse.urlsplit(newurl).scheme!='https':raise ValueError('Artifact redirect must stay HTTPS')
  redirected=super().redirect_request(req,fp,code,msg,headers,newurl)
  if urllib.parse.urlsplit(req.full_url).netloc!=urllib.parse.urlsplit(newurl).netloc:
   redirected.remove_header('Authorization')
  return redirected
def fetch_metadata(destination,provider_zip,cache_dir=None):
 # An exact cache hit reuses previously authenticated immutable input bytes;
 # it does not claim that the original short-lived Actions artifact still exists.
 if cache_dir is not None:
  no_symlink_ancestors(cache_dir);cache_dir.mkdir(parents=True,exist_ok=True)
  cached_zip=cache_dir/'provider.zip';cached_meta=cache_dir/'origin-metadata.json'
  if cached_zip.exists() or cached_meta.exists():
   no_symlink_ancestors(cached_meta)
   origin=validate_artifact_metadata(json.loads(cached_meta.read_text()))
   verify_file(cached_zip,PROVIDER_ZIP_SHA,47849044)
   no_symlink_ancestors(provider_zip)
   if provider_zip.exists():raise ValueError('Fresh provider ZIP output required')
   shutil.copyfile(cached_zip,provider_zip)
   meta={**origin,'independent_provider_zip_sha256':digest(provider_zip),
         'acquisition_source':'EXACT_BYTE_PINNED_CACHE',
         'original_artifact_current_availability':'NOT CHECKED; historical origin metadata only'}
   Path(destination).write_text(json.dumps(meta,indent=2)+'\n');return meta
 token=os.environ.get('GH_TOKEN','')
 if not token:raise ValueError('Ephemeral workflow token required for standard same-repository metadata read')
 req=urllib.request.Request('https://api.github.com/repos/'+REPO+'/actions/artifacts/'+str(ARTIFACT),headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'9to1-pinned-inference-stage'})
 with urllib.request.urlopen(req,timeout=60) as response:meta=json.load(response)
 validate_artifact_metadata(meta)
 # Independently pin the provider ZIP bytes as well as the metadata. The standard
 # Actions downloader below remains the actual artifact extraction transport.
 no_symlink_ancestors(provider_zip)
 archive_request=urllib.request.Request('https://api.github.com/repos/'+REPO+'/actions/artifacts/'+str(ARTIFACT)+'/zip',headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'9to1-pinned-inference-stage'})
 with urllib.request.build_opener(ArtifactRedirect()).open(archive_request,timeout=60) as response,provider_zip.open('xb') as dst:shutil.copyfileobj(response,dst)
 verify_file(provider_zip,PROVIDER_ZIP_SHA,47849044)
 meta['independent_provider_zip_sha256']=digest(provider_zip)
 meta['origin_metadata_observed_at']=datetime.datetime.now(datetime.timezone.utc).isoformat()
 meta['acquisition_source']='AUTHENTICATED_ACTIONS_ARTIFACT'
 if cache_dir is not None:
  shutil.copyfile(provider_zip,cache_dir/'provider.zip')
  (cache_dir/'origin-metadata.json').write_text(json.dumps(meta,indent=2)+'\n')
 Path(destination).write_text(json.dumps(meta,indent=2)+'\n');return meta
def extract_provider_zip(provider_zip,artifact_root):
 verify_file(provider_zip,PROVIDER_ZIP_SHA,47849044);no_symlink_ancestors(artifact_root)
 if artifact_root.exists():raise ValueError('Fresh provider extraction directory required')
 with zipfile.ZipFile(provider_zip) as archive:
  names=set();members=archive.infolist()
  if sum(m.file_size for m in members)>256*1024*1024:raise ValueError('Oversized provider archive')
  for member in members:
   rel=Path(member.filename)
   kind=stat.S_IFMT(member.external_attr>>16)
   if rel.is_absolute() or '..' in rel.parts or '\\' in member.filename or member.filename in names or kind not in (0,stat.S_IFREG,stat.S_IFDIR):raise ValueError('Unsafe provider archive member')
   names.add(member.filename)
  artifact_root.mkdir(parents=True)
  for member in members:
   target=artifact_root/member.filename
   if member.is_dir():target.mkdir(parents=True,exist_ok=True);continue
   target.parent.mkdir(parents=True,exist_ok=True)
   with archive.open(member) as source,target.open('xb') as destination:shutil.copyfileobj(source,destination)
def unique(root,name):
 hits=[p for p in root.rglob(name) if p.is_file() and not p.is_symlink()]
 if len(hits)!=1:raise ValueError('Expected one '+name+' in standard downloaded artifact')
 return hits[0]
def extract_payload(archive,destination):
 destination.mkdir()
 with tarfile.open(archive,'r:gz') as tar:
  for member in tar:
   rel=Path(member.name)
   if rel.is_absolute() or '..' in rel.parts or not(member.isdir() or member.isfile()):raise ValueError('Unsafe engine archive member '+member.name)
   target=destination/rel
   if member.isdir():target.mkdir(parents=True,exist_ok=True);continue
   target.parent.mkdir(parents=True,exist_ok=True)
   with tar.extractfile(member) as src,target.open('xb') as dst:shutil.copyfileobj(src,dst)
   target.chmod(0o755 if member.mode&0o111 else 0o644)
def download(url,destination,sha,size):
 if destination.exists():return verify_file(destination,sha,size)
 partial=destination.with_name(destination.name+'.partial')
 with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'9to1-pinned-inference-stage'}),timeout=60) as response,partial.open('xb') as dst:shutil.copyfileobj(response,dst)
 verify_file(partial,sha,size);partial.rename(destination);return destination
def cached_input(cache_dir,name,url,sha,size):
 no_symlink_ancestors(cache_dir);cache_dir.mkdir(parents=True,exist_ok=True)
 return download(url,cache_dir/name,sha,size)
def stage_expected_files(root):
 spec=json.loads((root/DULCHE/'bundle/arm64-runtime-bundle-spec.json').read_text())
 return spec,{'usr/lib/haven/'+a['installPath'] for a in spec['assets']}|{'usr/lib/haven/inference/'+n for n in ['bundle/bundle-manifest.json','bundle/bundle-receipt.json','modelctl.py','gguf.py','model_lease.py']}|{'usr/share/haven/default-models/'+n for n in [MODEL_SHA+'.gguf','qwen3-06b-q6k.json','notices/Qwen3-Apache-2.0-LICENSE.txt']}
def validate_stage(stage,root):
 no_symlink_ancestors(stage);source_context(root)
 from bundle.verifier import verify_installed
 from bundle.receipt import build_receipt
 spec,expected=stage_expected_files(root);allowed_dirs={str(p) for n in expected for p in Path(n).parents if str(p)!='.'}
 for p in stage.rglob('*'):
  rel=p.relative_to(stage).as_posix()
  if p.is_symlink() or not(p.is_file() or p.is_dir()) or p.is_file() and rel not in expected or p.is_dir() and rel not in allowed_dirs:raise ValueError('Unreviewed stage entry '+rel)
 if {p.relative_to(stage).as_posix() for p in stage.rglob('*') if p.is_file()}!=expected:raise ValueError('Incomplete inference data stage')
 install=stage/'usr/lib/haven';manifest=json.loads((install/'inference/bundle/bundle-manifest.json').read_text());receipt=json.loads((install/'inference/bundle/bundle-receipt.json').read_text())
 if receipt!=build_receipt(manifest) or manifest['bundleId']!=spec['bundleId'] or manifest['bundleVersion']!=spec['bundleVersion'] or manifest['host']!=spec['host']:raise ValueError('Untrusted/mismatched canonical manifest receipt')
 bylogical={a['logical']:a for a in manifest['assets']}
 if set(bylogical)!={a['logical'] for a in spec['assets']}:raise ValueError('Unexpected runtime manifest assets')
 for a in spec['assets']:
  m=bylogical[a['logical']]
  if any(m[k]!=a[k] for k in ['kind','engine','platform','runtimeFormat','source','license','installPath','redistribution']) or m['sha256']!=a['expectedSha256'] or m['size']!=a['expectedSize']:raise ValueError('Changed runtime asset pin '+a['logical'])
 if verify_installed(install,manifest).state!='ready':raise ValueError('Runtime closure verification failed')
 engine=install/'llama.cpp/llama-server'
 if stat.S_IMODE(engine.stat().st_mode)!=0o755:raise ValueError('Engine must retain0755')
 for n in ['modelctl.py','gguf.py','model_lease.py']:
  if digest(install/'inference'/n)!=digest(root/DULCHE/'llamacpp'/n):raise ValueError('Unreviewed Python integration '+n)
 models=stage/'usr/share/haven/default-models';seed=json.loads((models/'qwen3-06b-q6k.json').read_text());canonical=json.loads((root/'.github/scripts/native16-arm64/qwen3-06b-q6k.json').read_text())
 if seed!=canonical:raise ValueError('Changed canonical seed metadata')
 verify_file(models/(MODEL_SHA+'.gguf'),MODEL_SHA,MODEL_SIZE);verify_file(models/'notices/Qwen3-Apache-2.0-LICENSE.txt',NOTICE_SHA,NOTICE_SIZE)
 return {'files':sorted(expected),'engine_sha256':digest(engine),'engine_mode':'0755','model_sha256':MODEL_SHA,'model_bytes':MODEL_SIZE,'manifest_sha256':digest(install/'inference/bundle/bundle-manifest.json'),'receipt_sha256':digest(install/'inference/bundle/bundle-receipt.json'),'acceptance':'DATA_IDENTITY_ONLY_APP_NOT_RUN'}
def merge_reviewed_stage(source,destination,root):
 summary=validate_stage(source,root);no_symlink_ancestors(destination)
 # Preflight EVERY target before writing any reviewed file into app package stage.
 for name in summary['files']:
  target=destination/name;no_symlink_ancestors(target)
  if target.exists():raise ValueError('Refuse existing package target '+name)
 for name in summary['files']:
  src=source/name;dst=destination/name;dst.parent.mkdir(parents=True,exist_ok=True)
  with src.open('rb') as r,dst.open('xb') as w:shutil.copyfileobj(r,w)
  dst.chmod(0o755 if name=='usr/lib/haven/llama.cpp/llama-server' else 0o644)
 return summary
def prepare(root,artifact_root,output_root,metadata,model_file=None,notice_file=None):
 artifact_meta=validate_artifact_metadata(json.loads(metadata.read_text()));source_context(root);no_symlink_ancestors(output_root)
 if output_root.exists():raise ValueError('Fresh inference output required')
 output_root.mkdir(parents=True);inner=verify_file(unique(artifact_root,INNER),INNER_SHA);payload=output_root/'payload';extract_payload(inner,payload)
 upstream=verify_file(unique(artifact_root,SOURCE_TAR),SOURCE_SHA);source_copy=output_root/SOURCE_TAR;shutil.copyfile(upstream,source_copy)
 spec=json.loads((root/DULCHE/'bundle/arm64-runtime-bundle-spec.json').read_text());supplied=output_root/'supplied'
 for a in spec['assets']:
  src=unique(artifact_root,'engine-closure.json') if a['path']=='evidence/engine-closure.json' else payload/a['path'];verify_file(src,a['expectedSha256'],a['expectedSize']);dst=supplied/a['path'];dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst)
 model=output_root/'Qwen3-0.6B-Q6_K.gguf';notice=output_root/'Qwen3-Apache-2.0-LICENSE.txt'
 cache=os.environ.get('PREVIEW_INPUT_CACHE')
 if cache:
  if model_file is None:model_file=cached_input(Path(cache),'Qwen3-0.6B-Q6_K.gguf',MODEL_URL,MODEL_SHA,MODEL_SIZE)
  if notice_file is None:notice_file=cached_input(Path(cache),'Qwen3-Apache-2.0-LICENSE.txt',NOTICE_URL,NOTICE_SHA,NOTICE_SIZE)
 if model_file:verify_file(model_file,MODEL_SHA,MODEL_SIZE);os.link(model_file,model)
 else:download(MODEL_URL,model,MODEL_SHA,MODEL_SIZE)
 if notice_file:verify_file(notice_file,NOTICE_SHA,NOTICE_SIZE);shutil.copyfile(notice_file,notice)
 else:download(NOTICE_URL,notice,NOTICE_SHA,NOTICE_SIZE)
 from bundle.data_stage import stage_package
 stage=output_root/'debian';stager=stage_package(spec_path=root/DULCHE/'bundle/arm64-runtime-bundle-spec.json',payload_root=supplied,seed_model=model,seed_json_path=root/'.github/scripts/native16-arm64/qwen3-06b-q6k.json',seed_notice=notice,python_source_root=root/DULCHE/'llamacpp',stage_root=stage);verified=validate_stage(stage,root)
 receipt={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'producer_run':RUN,'producer_head':RUN_HEAD,'artifact_id':ARTIFACT,'artifact_name':ARTIFACT_NAME,'provider_zip_digest_metadata':PROVIDER_ZIP_SHA,'provider_zip_bytes_sha256':artifact_meta.get('independent_provider_zip_sha256'),'transport':describe_transport(artifact_meta),'inner_archive_sha256':INNER_SHA,'upstream_source_archive_sha256':SOURCE_SHA,'source_archive':str(source_copy),'stage':str(stage),'validated':verified,'stager':stager,'engine_model_execution':'NOT RUN','package':'NOT RUN'};out=output_root/'inference-stage-receipt.json';out.write_text(json.dumps(receipt,indent=2)+'\n')
 if os.environ.get('GITHUB_ENV'):
  with open(os.environ['GITHUB_ENV'],'a') as f:f.write('PREVIEW_INFERENCE_STAGE='+str(stage)+'\nPREVIEW_INFERENCE_RECEIPT='+str(out)+'\nPREVIEW_INFERENCE_SOURCE='+str(source_copy)+'\n')
 return receipt
def describe_transport(metadata):
 source=metadata.get('acquisition_source')
 if source=='EXACT_BYTE_PINNED_CACHE':return 'Previously authenticated byte-pinned cache; fresh ZIP extraction after full SHA256/size validation; original artifact current availability NOT CHECKED'
 if source=='AUTHENTICATED_ACTIONS_ARTIFACT':return 'Authenticated GitHub Actions artifact HTTPS acquisition via urllib; fresh ZIP extraction after full SHA256/size validation'
 return 'Provider ZIP independently byte-pinned; acquisition transport UNKNOWN in supplied metadata'
def main():
 p=argparse.ArgumentParser();p.add_argument('--validate-artifact-only',type=Path);p.add_argument('--provider-zip',type=Path);p.add_argument('--provider-cache',type=Path);p.add_argument('--extract-provider-zip',type=Path);p.add_argument('--artifact-root',type=Path);p.add_argument('--artifact-metadata',type=Path);p.add_argument('--output-root',type=Path);p.add_argument('--model-file',type=Path);p.add_argument('--notice-file',type=Path);a=p.parse_args();root=Path(__file__).resolve().parents[3]
 if a.validate_artifact_only:
  if not a.provider_zip:p.error('--validate-artifact-only requires --provider-zip under RUNNER_TEMP')
  fetch_metadata(a.validate_artifact_only,a.provider_zip,a.provider_cache);print('Exact pinned provider origin metadata and ZIP bytes verified');return
 if a.extract_provider_zip:
  if not a.artifact_root:p.error('--extract-provider-zip requires --artifact-root')
  extract_provider_zip(a.extract_provider_zip,a.artifact_root);return
 if not all([a.artifact_root,a.artifact_metadata,a.output_root]):p.error('prepare needs --artifact-root --artifact-metadata --output-root')
 print(json.dumps(prepare(root,a.artifact_root,a.output_root,a.artifact_metadata,a.model_file,a.notice_file),indent=2))
if __name__=='__main__':main()
