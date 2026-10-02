"""Full immutable normal977 or validation source map, one batched tree read."""
import os,json,pathlib,hashlib,subprocess,sys
root=pathlib.Path.cwd();external=pathlib.Path(os.environ['ASTRA_ANDROID_METADATA_ROOT']);manifest=external/'.github/validation/astra-root14-android-cut.json'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(manifest)==os.environ['EXPECTED_MANIFEST_SHA256']
cut=json.loads(manifest.read_text());variant=os.environ.get('ASTRA_ANDROID_VARIANT','probe');normal=variant=='normal'
assert variant in ('normal','probe')
expected=cut['normalSourceCommit'] if normal else os.environ['EXPECTED_COMMIT']
assert cut['currentNormalCommit']=='977b8eed3495f41c8b9ffcdc3705f1e115b131cc'
assert len(cut['normalSourceOverlayProvenance'])==5
assert cut['coherentBaseProductTree']=='c1b9318844df56135e396f5452df082b42f97c89'
assert len(cut['coherentProductOverlayProvenance'])==100
assert len(cut['coherentOwnerBindings'])==114
assert expected==cut['normalSourceCommit'] if normal else True
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==expected
files=cut['normalFiles'] if normal else cut['files'];links=cut['normalGitlinks'] if normal else cut['gitlinks']
actual={};actualLinks={}
for record in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
 if not record:continue
 header,name=record.split(b'\t',1);mode,kind,oid=header.decode().split();name=name.decode()
 if mode=='160000':actualLinks[name]=oid
 else:assert kind=='blob' and mode in ('100644','100755');actual[name]=(mode,oid)
if not normal:del actual['.github/validation/astra-root14-android-cut.json']
expectedFiles={}
for item in files:
 name=item['path'];path=root/name;assert not path.is_symlink() and path.is_file()
 data=path.read_bytes();oid=hashlib.sha1(('blob '+str(len(data))+'\0').encode()+data).hexdigest()
 assert len(data)==item['bytes'] and sha(path)==item['sha256'];assert item.get('object',oid)==oid
 assert name not in expectedFiles;expectedFiles[name]=(item.get('mode',item.get('gitMode')),oid)
assert actual==expectedFiles
assert len(links)==22 and actualLinks=={x['path']:x['commit'] for x in links}
assert not subprocess.check_output(['git','diff','--name-only','HEAD'])
assert not subprocess.check_output(['git','diff','--cached','--name-only'])
sourceLookup={row['path']:row['sha256'] for row in files}
for row in cut['coherentOwnerBindings']:
 assert sourceLookup.get(row['path'])==row['sha256']
for row in cut['normalSourceOverlayProvenance']:
 assert sourceLookup.get(row['target'])==row['sha256'] or (not normal and row['target']=='9-1 OS (Android)/src/Haven.Android/Haven.Android.csproj')
pins={'framework/CUI/vendor/Avalonia/external/XamlX':'009d4815470cf4bf71d1adbb633a5d81dcb2bb52','framework/CUI/vendor/Avalonia/external/Avalonia.DBus':'864a05282841bf04006890f04d11d60d1a046aa9','9to1 Workspace/Terminal/Source/libvterm':'934bc2fbf21800ac3458a499df8820ca5fb45fd3'}
assert {x['path']:x['commit'] for x in cut['materializedGitlinks']}==pins
if os.environ.get('ASTRA_ANDROID_PREPARED')=='1':
 for name,pin in pins.items():
  assert subprocess.check_output(['git','-C',name,'rev-parse','HEAD'],text=True).strip()==pin
  assert not subprocess.check_output(['git','-C',name,'status','--porcelain','--untracked-files=no'])
out=root/'artifacts/logs';out.mkdir(parents=True,exist_ok=True)
phase=os.environ.get('ASTRA_ANDROID_SOURCE_PHASE','verify')
(out/(variant+'-'+phase+'-source.json')).write_text(json.dumps({'commit':expected,'manifestSha256':sha(manifest),'variant':variant,'files':len(files),'gitlinks':22,'prepared':os.environ.get('ASTRA_ANDROID_PREPARED')=='1'},indent=2)+'\n')
