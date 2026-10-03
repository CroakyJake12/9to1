"""Actual hosted SDK preflight caller; no local SDK execution in authoring."""
import importlib.util,json,pathlib,subprocess,hashlib,os,shutil

def capture(root,out,cut_paths,command,digest):
    sdk_root=pathlib.Path(os.environ['ASTRA_CANVAS_OFFICIAL_SDK_ROOT']).resolve(strict=True)
    archive=pathlib.Path(os.environ['ASTRA_CANVAS_OFFICIAL_SDK_ARCHIVE']).resolve(strict=True)
    selected=pathlib.Path(shutil.which('dotnet')).resolve(strict=True)
    if selected!=sdk_root/'dotnet':raise ValueError('Actual caller dotnet is not isolated official SDK root')
    helper=root/'.github/scripts/astra-canvas-sdk-archive-admission.py'
    if helper.is_symlink() or digest(helper)!=cut_paths[helper.relative_to(root).as_posix()]:raise ValueError('SDK archive admission helper differs from exact cut')
    spec=importlib.util.spec_from_file_location('actual_sdk_admission',helper);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    initial=module.verify_install(sdk_root,archive)
    if digest(selected)!=initial['sdkFiles']['dotnet']['sha256']:raise ValueError('Selected SDK executable differs from verified official archive before execution')
    (out/'sdk-install-before-execution.json').write_text(json.dumps(initial,indent=2)+'\n')
    directory=root/'.github/validation/canvas-sdk-reader'
    project=directory/'ProviderMetadata.csproj';source=directory/'Program.cs'
    for path in (project,source):
        if path.is_symlink() or digest(path)!=cut_paths[path.relative_to(root).as_posix()]:raise ValueError('Actual metadata reader source/project is not exact cut source')
    artifacts=root/'artifacts/canvas-sdk-reader-build'
    if artifacts.exists():raise ValueError('Metadata reader requires fresh isolated build output')
    props=['-p:Configuration=Release','-p:TargetFramework=net10.0','-p:RuntimeIdentifier=linux-x64','-p:RuntimeIdentifiers=linux-x64','-p:SelfContained=false','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-p:IncludeProjectNameInArtifactsPaths=true','-p:UseSharedCompilation=false']
    if command([str(selected),'restore',str(project),*props,'--disable-build-servers'],'sdk-reader-restore'):raise ValueError('Actual reader restore failed')
    def reader_restore():
        query=subprocess.run([str(selected),'msbuild',str(project),'-nologo','-m:1','-nr:false',*props,'-getProperty:MSBuildProjectFullPath,TargetFramework,ProjectAssetsFile,MSBuildProjectExtensionsPath'],capture_output=True,text=True)
        query.check_returncode();values=json.loads(query.stdout)['Properties']
        if pathlib.Path(values['MSBuildProjectFullPath']).resolve()!=project or values['TargetFramework']!='net10.0':raise ValueError('Reader restored project identity/framework mismatch')
        extensions=pathlib.Path(values['MSBuildProjectExtensionsPath']).resolve(strict=True);assets=pathlib.Path(values['ProjectAssetsFile']).resolve(strict=True)
        if not extensions.is_relative_to(artifacts) or not assets.is_relative_to(extensions):raise ValueError('Reader restored metadata not fresh isolated output')
        data=json.loads(assets.read_bytes())
        if pathlib.Path(data['project']['restore']['projectPath']).resolve()!=project or data['libraries']:raise ValueError('Reader unknown restored project/package dependency')
        dgspec=extensions/(project.name+'.nuget.dgspec.json')
        graph=json.loads(dgspec.read_bytes())
        if set(graph['projects'])!={str(project)}:raise ValueError('Reader unknown restored source project')
        rows=[]
        for path in sorted(extensions.iterdir()):
            if path.name.endswith(('.nuget.dgspec.json','.nuget.g.props','.nuget.g.targets')) or path.name in ('project.assets.json','project.nuget.cache'):
                if path.is_symlink() or not path.is_file():raise ValueError('Reader restored metadata not regular')
                rows.append({'path':str(path),'sha256':digest(path),'bytes':path.stat().st_size})
        if not rows:raise ValueError('Reader restore metadata empty')
        return {'properties':values,'metadata':rows,'projectSha256':digest(project),'sourceSha256':digest(source)}
    restore_before=reader_restore();(out/'sdk-reader-restore-before.json').write_text(json.dumps(restore_before,indent=2)+'\n')
    if command([str(selected),'build',str(project),*props,'--no-restore','--disable-build-servers','-m:1','-nr:false'],'sdk-reader-build'):raise ValueError('Actual reader build failed')
    query=subprocess.run([str(selected),'msbuild',str(project),'-nologo','-m:1','-nr:false',*props,'-getProperty:TargetPath,TargetFramework,OutputPath,IntermediateOutputPath','-getItem:Compile,Analyzer'],capture_output=True,text=True)
    (out/'sdk-reader-target.stdout').write_text(query.stdout);(out/'sdk-reader-target.stderr').write_text(query.stderr)
    query.check_returncode();data=json.loads(query.stdout);properties=data['Properties'];target=pathlib.Path(properties['TargetPath']).resolve(strict=True)
    if properties['TargetFramework']!='net10.0' or not target.is_relative_to(artifacts) or not target.with_suffix('.pdb').is_file():raise ValueError('Actual SDK metadata reader output/TFM/PDB mismatch')
    compile_rows=data['Items']['Compile']
    if sum(pathlib.Path(row.get('FullPath',row['Identity'])).resolve()==source for row in compile_rows)!=1:raise ValueError('Reader Program not uniquely compiled')
    intermediate=pathlib.Path(properties['IntermediateOutputPath']);intermediate=intermediate if intermediate.is_absolute() else directory/intermediate;intermediate=intermediate.resolve(strict=True)
    compiled=[]
    for row in compile_rows:
        path=pathlib.Path(row.get('FullPath',row['Identity']));path=path if path.is_absolute() else directory/path;path=path.resolve(strict=True)
        if path!=source and not path.is_relative_to(intermediate):raise ValueError('Unknown reader compile source outside exact source/fresh actual generated output')
        compiled.append({'path':str(path),'sha256':digest(path)})
    analyzers=[]
    for row in data['Items'].get('Analyzer',[]):
        path=pathlib.Path(row.get('FullPath',row['Identity'])).resolve(strict=True)
        if not path.is_relative_to(sdk_root):raise ValueError('Unknown non-SDK reader analyzer')
        analyzers.append({'path':str(path),'sha256':digest(path)})
    closure=[{'path':str(path),'sha256':digest(path),'bytes':path.stat().st_size} for path in sorted(target.parent.rglob('*')) if path.is_file()]
    if any(path.is_symlink() for path in target.parent.rglob('*')):raise ValueError('Reader compiled output symlink')
    provider=sdk_root/'sdk/10.0.301/Extensions/Microsoft.TestPlatform.TestHostRuntimeProvider.dll'
    if command([str(selected),str(target),str(provider.parent.parent),str(provider)],'sdk-reader-actual-provider'):raise ValueError('Actual FileVersionInfo provider metadata read failed')
    metadata=json.loads((out/'sdk-reader-actual-provider.log').read_text())
    record,admission=module.admit(sdk_root,archive,metadata,out/'sdk-provider-lineage')
    if any(admission[key]!=initial[key] for key in ('sdkFiles','sdkLinks','sdkDirectories','archiveSha512')):raise ValueError('Official installed SDK inventory changed during reader execution')
    for row in compiled+analyzers+closure:
        if digest(pathlib.Path(row['path']))!=row['sha256']:raise ValueError('Reader source/analyzer/compiled closure changed during actual metadata read')
    for row in analyzers:
        key=pathlib.Path(row['path']).relative_to(sdk_root).as_posix()
        if admission['sdkFiles'].get(key,{}).get('sha256')!=row['sha256']:raise ValueError('Compiled reader analyzer differs from official SDK archive member')
    (out/'sdk-reader-compiled.json').write_text(json.dumps({'target':str(target),'targetSha256':digest(target),'sourceInputs':compiled,'analyzers':analyzers,'closure':closure,'providerMetadata':metadata,'sdkExecutable':record},indent=2)+'\n')
    restore_after=reader_restore();(out/'sdk-reader-restore-after.json').write_text(json.dumps(restore_after,indent=2)+'\n')
    if restore_before!=restore_after:raise ValueError('Actual reader restored/source metadata changed')
    retained=out/'compiled/sdk-metadata-reader';retained.mkdir(parents=True,exist_ok=False)
    for path in target.parent.iterdir():
        if path.is_file():shutil.copyfile(path,retained/path.name)
    return record,admission
