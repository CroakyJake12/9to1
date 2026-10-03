"""Full actual Linux supervised compiler-source admission, derived from accepted Picture20."""
import pathlib,subprocess,hashlib,json

def capture(root,out,files,graphs,required_materialized,command,digest,task_target,*,postbuild=False):
 root=root.resolve()
 admitted={str((root/x['path']).resolve()):x['sha256'] for x in files}
 for graph in graphs:
  for package in graph['packages'].values():
   for file in package['files']:admitted[str((pathlib.Path(package['root'])/file['path']).resolve())]=file['sha256']
 sdk=pathlib.Path(subprocess.check_output(['which','dotnet'],text=True).strip()).resolve().parent
 sdkFiles={}
 for prefix in ('sdk','packs'):
  for file in sorted((sdk/prefix).rglob('*')):
   if file.is_symlink():raise ValueError('SDK symlink requires explicit admission')
   if file.is_file():sdkFiles[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
 for file,evidence in sdkFiles.items():admitted[file]=evidence['sha256']
 (out/'actual-sdk-input-pins.json').write_text(json.dumps(sdkFiles,indent=2)+'\n')
 for donorPath in required_materialized:
  for record in subprocess.check_output(['git','-C',str(root/donorPath),'ls-files','-z']).split(b'\0'):
   if record:
    file=root/donorPath/record.decode()
    if not file.is_file() or file.is_symlink():raise ValueError('materialized compiler input missing or symlink')
    admitted[str(file.resolve())]=digest(file)
 generated={}
 if postbuild:
  for directory in ('root14-managed-build','root14-host-build-tasks','root14-supervised-publish-build'):
   for file in sorted((root/'artifacts'/directory).rglob('*')):
    if file.is_symlink():raise ValueError('actual fresh generated output symlink')
    if file.is_file():generated[str(file.resolve())]={'bytes':file.stat().st_size,'sha256':digest(file)}
 projects={}
 for graph in graphs:
  for item in graph['projects']:
   key=(item['path'],item['effectiveFramework'],item['hostContext'],item['publishContext'])
   projects[key]=item
 for key,item in sorted(projects.items()):
  sourceProject=item['path'];effectiveFramework=item['effectiveFramework']
  directory='root14-host-build-tasks' if item['hostContext'] else 'root14-supervised-publish-build' if item['publishContext'] else 'root14-managed-build'
  props=['-p:Configuration=Release','-p:TargetFramework='+effectiveFramework,'-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained='+('true' if item['publishContext'] else 'false'),'-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(root/'artifacts'/directory),'-p:IncludeProjectNameInArtifactsPaths=true','-p:UseSharedCompilation=false','-p:AvsSkipBuildingLegacyTargetFrameworks=True','-p:EnableWindowsTargeting=true','-p:AvaloniaBuildTasksLocation='+str(task_target)]
  if item['hostContext']:props=[p for p in props if not p.startswith(('-p:RuntimeIdentifier=','-p:RuntimeIdentifiers=','-p:SelfContained='))]
  label=('postbuild-' if postbuild else 'prebuild-')+'evaluated-'+hashlib.sha256(repr(key).encode()).hexdigest()[:20]
  args=['dotnet','msbuild',str((root/sourceProject).resolve()),'-nologo','-m:1','-nr:false',*props,'-getItem:Compile,AdditionalFiles,Analyzer,EmbeddedResource,MicroComIdl','-getProperty:TargetPath,MSBuildProjectExtensionsPath,MicroComGeneratorMSBuildDll,UseLocalMicroComBuild']
  if command(args,label):raise ValueError('actual recursive compiler input query failed')
  data=json.loads((out/(label+'.log')).read_text())
  if postbuild:
   # Complete exact maintained donor MicroCom map; evaluated items must equal these source-declared pairs.
   microComProjects={
    'framework/CUI/vendor/Avalonia/src/Avalonia.Native/Avalonia.Native.csproj':[('avn.idl','Interop.Generated.cs')],
    'framework/CUI/vendor/Avalonia/src/Windows/Avalonia.Win32/Avalonia.Win32.csproj':[('WinRT/winrt.idl','WinRT/WinRT.Generated.cs'),('Win32Com/win32.idl','Win32Com/Win32.Generated.cs'),('DirectX/directx.idl','DirectX/directx.Generated.cs'),('DComposition/dcomp.idl','DComposition/DComp.Generated.cs')]
   }
   projectKey=(root/sourceProject).resolve().relative_to(root).as_posix()
   if projectKey in microComProjects:
    nativeProject=root/projectKey
    assert admitted[str(nativeProject.resolve())]==digest(nativeProject)
    props=data['Properties'];assert props.get('UseLocalMicroComBuild','').lower()!='true'
    idls=data['Items'].get('MicroComIdl',[]);expectedPairs=microComProjects[projectKey]
    actualPairs=[(pathlib.Path(i['FullPath']).resolve().relative_to(nativeProject.parent).as_posix(),i['CSharpInteropPath'].replace(chr(92),'/'))for i in idls]
    assert len(actualPairs)==len(set(actualPairs)) and sorted(actualPairs)==sorted(expectedPairs)
    for entry in idls:
     idl=pathlib.Path(entry['FullPath']).resolve();output=(nativeProject.parent/entry['CSharpInteropPath'].replace(chr(92),'/')).resolve()
     assert (idl.relative_to(nativeProject.parent).as_posix(),output.relative_to(nativeProject.parent).as_posix()) in expectedPairs
     assert not idl.is_symlink() and not output.is_symlink() and idl.is_file() and output.is_file()
     assert admitted[str(idl)]==digest(idl)
     generator=pathlib.Path(props['MicroComGeneratorMSBuildDll']).resolve()
     assert generator.as_posix().lower().endswith('/microcom.codegenerator.msbuild/0.11.4/tools/netstandard2.0/microcom.codegenerator.msbuild.dll')
     assert admitted[str(generator)]==digest(generator)
     packageRoot=generator.parents[2];targets=packageRoot/'build/MicroCom.CodeGenerator.MSBuild.targets'
     assert admitted[str(targets.resolve())]==digest(targets)
     imported=root/'framework/CUI/vendor/Avalonia/build/MicroCOM.props';assert admitted[str(imported.resolve())]==digest(imported)
     compilePaths=[pathlib.Path(x['FullPath']).resolve()for x in data['Items']['Compile']];assert compilePaths.count(output)==1
     generated[str(output)]={'bytes':output.stat().st_size,'sha256':digest(output),'producer':'MicroCom.CodeGenerator.MSBuild/0.11.4 GenerateMicroComItems → UpdateMicroComCompileItems','projectSha256':digest(nativeProject),'idlSha256':digest(idl),'generatorSha256':digest(generator),'targetsSha256':digest(targets),'importSha256':digest(imported)}
     (out/'actual-generated-output-pins.json').write_text(json.dumps(generated,indent=2)+'\n')
  for items in data.get('Items',{}).values():
   for item in items:
    value=item.get('FullPath')
    if not value:raise ValueError('evaluated input has no physical FullPath')
    file=pathlib.Path(value).resolve();expected=admitted.get(str(file),generated.get(str(file),{}).get('sha256'))
    if not file.is_file() or expected!=digest(file):raise ValueError('unknown or changed actual compiler/generator input '+str(file))
 (out/('postbuild-input-admission.json' if postbuild else 'prebuild-input-admission.json')).write_text(json.dumps({'projects':len(projects),'admittedPaths':len(admitted),'generated':generated,'qualification':'Actual physical source/package/SDK/generated producer hashes; not publisher trust or runtime proof'},indent=2)+'\n')
 return {'sdk':sdkFiles,'generated':generated}
