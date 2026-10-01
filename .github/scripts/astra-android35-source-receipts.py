"""Exact full tracked-source receipt and bounded three-submodule cleanliness checks."""
import hashlib,json,os,pathlib,subprocess,sys
variant,phase=sys.argv[1:]
assert variant in ('normal','probe') and phase in ('before-preparation','before-build','after-build')
root=pathlib.Path.cwd().resolve();assert root==pathlib.Path(os.environ['RUNNER_TEMP']).resolve()/(variant+'35-source')
def git(*args):return subprocess.check_output(['git',*args])
assert git('rev-parse','HEAD').decode().strip()==os.environ['GITHUB_SHA']==os.environ['EXPECTED_COMMIT']
assert not git('diff','--name-only','HEAD'), 'tracked source changed'
assert not git('diff','--cached','--name-only'), 'index changed'
pins={'framework/CUI/vendor/Avalonia/external/XamlX':'009d4815470cf4bf71d1adbb633a5d81dcb2bb52','framework/CUI/vendor/Avalonia/external/Avalonia.DBus':'864a05282841bf04006890f04d11d60d1a046aa9','9to1 Workspace/Terminal/Source/libvterm':'934bc2fbf21800ac3458a499df8820ca5fb45fd3'}
files=[];links=[]
for record in git('ls-tree','-rz','HEAD').split(b'\0'):
 if not record:continue
 meta,name=record.split(b'\t',1);mode,kind,oid=meta.decode().split();path=name.decode();p=root/path
 if mode=='160000':links.append({'path':path,'oid':oid});continue
 data=p.read_bytes();assert hashlib.sha1(('blob '+str(len(data))+'\0').encode()+data).hexdigest()==oid,path
 files.append({'path':path,'mode':mode,'oid':oid,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)})
assert len(links)==22,'complete tracked gitlink count differs'
materialized=[]
for path,expected in pins.items():
 assert next(x['oid'] for x in links if x['path']==path)==expected
 if phase!='before-preparation':
  actual=subprocess.check_output(['git','-C',str(root/path),'rev-parse','HEAD']).decode().strip();assert actual==expected
  assert not subprocess.check_output(['git','-C',str(root/path),'status','--porcelain','--untracked-files=no']),path
  materialized.append({'path':path,'head':actual,'trackedClean':True})
for link in links:
 if link['path'] not in pins:assert not (root/link['path']/'.git').exists(),'unexpected donor materialized'
outputs=[]
for directory in root.rglob('*'):
 if directory.is_dir() and directory.name in ('bin','obj') and not any(x in ('bin','obj','.git') for x in directory.relative_to(root).parts[:-1]):
  regular=[p for p in directory.rglob('*') if p.is_file() and not p.is_symlink()];outputs.append({'path':str(directory.relative_to(root)),'regularBytes':sum(p.stat().st_size for p in regular),'files':len(regular)})
out=root/'artifacts/logs';out.mkdir(parents=True,exist_ok=True)
(out/('source-'+phase+'.json')).write_text(json.dumps({'variant':variant,'phase':phase,'root':str(root),'commit':os.environ['GITHUB_SHA'],'runId':os.environ['GITHUB_RUN_ID'],'files':files,'gitlinks':links,'materialized':materialized,'trackedClean':True,'generatedOutputs':outputs,'generatedRegularBytes':sum(x['regularBytes'] for x in outputs),'qualification':'Physical checkout/source/output provenance, not JNI runtime acceptance'},indent=2)+'\n')
