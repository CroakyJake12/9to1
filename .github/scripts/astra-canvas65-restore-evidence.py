# Imported trusted branch runner helper; read-only restored graph/package receipts.
import json,pathlib,hashlib,base64,subprocess

def snapshot_restore(root,entry):
 root=root.resolve();entry=(root/entry).resolve();pending=[entry];seen=set();projects=[];packages={}
 def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest()
 while pending:
  project=pending.pop()
  if project in seen:continue
  if not project.is_relative_to(root) or not project.is_file():raise ValueError('restored source project not bound to root')
  seen.add(project)
  # Standalone entry and Accounts project use standard project-relative obj; actual graph dgspec validates every path.
  props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts/canvas65-managed-build'),'-p:IncludeProjectNameInArtifactsPaths=true','-p:SelfContained=false','-p:EnableWindowsTargeting=true','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:UseSharedCompilation=false']
  query=subprocess.run(['dotnet','msbuild',str(project),'-nologo','-m:1','-nr:false',*props,'-getProperty:MSBuildProjectExtensionsPath,ProjectAssetsFile'],capture_output=True,text=True,check=True)
  evaluated=json.loads(query.stdout)['Properties'];obj=pathlib.Path(evaluated['MSBuildProjectExtensionsPath']);obj=obj if obj.is_absolute() else project.parent/obj;obj=obj.resolve()
  if not obj.is_relative_to(root):raise ValueError('project extensions path escapes immutable source root')
  assets=pathlib.Path(evaluated['ProjectAssetsFile']);assets=assets if assets.is_absolute() else project.parent/assets;assets=assets.resolve();dg=obj/(project.name+'.nuget.dgspec.json')
  if not assets.is_relative_to(root):raise ValueError('actual assets path escapes immutable graph')
  if not assets.is_file() or not dg.is_file():raise ValueError('missing actual restored graph metadata')
  graph=json.loads(dg.read_text());data=json.loads(assets.read_text())
  if pathlib.Path(data['project']['restore']['projectPath']).resolve()!=project:raise ValueError('asset project binding mismatch')
  metadata=[]
  for f in sorted(obj.iterdir()):
   if f.is_file() and (f.name in ('project.assets.json','project.nuget.cache') or f.name.endswith(('.nuget.dgspec.json','.nuget.g.props','.nuget.g.targets'))):metadata.append({'path':str(f.relative_to(root)),'bytes':f.stat().st_size,'sha256':sha(f)})
  projects.append({'path':str(project.relative_to(root)),'sourceSha256':sha(project),'metadata':metadata})
  for path,spec in graph['projects'].items():
   other=pathlib.Path(path).resolve()
   if not other.is_relative_to(root) or not other.is_file():raise ValueError('dgspec foreign project')
   if other!=project:pending.append(other)
  folders=[pathlib.Path(x) for x in data['packageFolders']]
  libraries=dict(data['libraries'])
  for framework in data['project']['restore']['frameworks'].values():
   for dependency in framework.get('downloadDependencies',[]):
    version=dependency['version'];match=__import__('re').fullmatch(r'\[([^,\]]+)(?:,\s*\1)?\]',version)
    if not match:raise ValueError('download dependency does not have one exact version')
    version=match.group(1);identity=dependency['name']+'/'+version
    libraries.setdefault(identity,{'type':'package','path':dependency['name'].lower()+'/'+version,'downloadDependency':True})
  for identity,lib in libraries.items():
   if lib['type']!='package':continue
   name,version=identity.rsplit('/',1)
   if name.lower().startswith('microsoft.identitymodel.') and version!='8.14.0':raise ValueError('IdentityModel version mismatch')
   found=[folder/lib['path'] for folder in folders if (folder/lib['path']).is_dir()]
   if len(found)!=1:raise ValueError('ambiguous/missing actual package root')
   directory=found[0];payload=[]
   for f in sorted(directory.rglob('*')):
    if f.is_symlink():raise ValueError('package symlink')
    if f.is_file():payload.append({'path':str(f.relative_to(directory)),'bytes':f.stat().st_size,'sha256':sha(f)})
   hashes=list(directory.glob('*.nupkg.sha512'))
   if len(hashes)!=1:raise ValueError('missing nupkgsha512')
   recorded=hashes[0].read_text().strip()
   metadata=directory/'.nupkg.metadata'
   if not metadata.is_file():raise ValueError('missing NuGet content metadata')
   contentHash=json.loads(metadata.read_text()).get('contentHash')
   if not contentHash or (not lib.get('downloadDependency') and contentHash!=lib.get('sha512')):raise ValueError('assets vs metadata content hash mismatch')
   archives=list(directory.glob('*.nupkg'))
   if len(archives)!=1:raise ValueError('missing unique package archive')
   with archives[0].open('rb') as stream:physical=base64.b64encode(hashlib.file_digest(stream,'sha512').digest()).decode()
   if physical!=recorded:raise ValueError('physical package archive vs nupkg.sha512 mismatch')
   for value in (contentHash,recorded):
    if len(base64.b64decode(value,validate=True))!=64 or base64.b64encode(base64.b64decode(value,validate=True)).decode()!=value:raise ValueError('noncanonical digest base64')
   evidence={'root':str(directory),'sha512':recorded,'assetsSha512':lib.get('sha512'),'metadataContentHash':contentHash,'physicalArchiveSha512':physical,'files':payload}
   if identity in packages and packages[identity]!=evidence:raise ValueError('same package identity has differing root/payload/hash')
   packages[identity]=evidence
 return {'projects':sorted(projects,key=lambda x:x['path']),'packages':packages}
