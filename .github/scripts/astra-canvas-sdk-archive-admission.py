"""Hosted-only admission of a fresh complete official SDK install and bundled VSTest lineage."""
import hashlib,json,pathlib,tarfile,urllib.request,stat
SDK_VERSION='10.0.301'
SDK_URL='https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.301/dotnet-sdk-10.0.301-linux-x64.tar.gz'
SDK_SHA512='cfbeec3a3a1d3ad3e168e37a77c4cc26c23125acd84a86d014047da3ecffce4c368a9acac4d7c950a047fa3d98989ce8aea69f8e5842cb6d330e8911e1c335a7'
VMR_COMMIT='96856fd726ffd058fb3dfef0851dbafc7ef3b011'
VSTEST_COMMIT='778909789acae5b87b753b4daea984e4b23a1e4c'
SOURCE_SHA256='f8c1fd1e5a09563257011cab551f688a7fe45e8f91b5ddb6fd468dd58da30231'

def verify_install(installed_root,archive_path):
    root=pathlib.Path(installed_root).resolve(strict=True);archive=pathlib.Path(archive_path).resolve(strict=True)
    def digest(p):
        with pathlib.Path(p).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
    with archive.open('rb') as f:
        if hashlib.file_digest(f,'sha512').hexdigest()!=SDK_SHA512:raise ValueError('Complete official SDK archive SHA512 mismatch')
    expected={};links={};directories={}
    with tarfile.open(archive,'r:gz') as bundle:
        for member in bundle:
            rel=pathlib.PurePosixPath(member.name)
            if rel.is_absolute() or '..' in rel.parts:raise ValueError('Unsafe official archive member')
            key=rel.as_posix().removeprefix('./')
            if key=='':continue
            path=root/key
            permission=member.mode & 0o7777
            if member.isdir():
                if key in directories:raise ValueError('Duplicate SDK directory member')
                if not path.is_dir() or path.is_symlink() or stat.S_IMODE(path.stat().st_mode)!=permission:raise ValueError('SDK directory type/permission differs from archive')
                directories[key]={'mode':permission,'archiveUid':member.uid,'archiveGid':member.gid}
                continue
            if key in expected or key in links:raise ValueError('Duplicate SDK archive payload')
            if member.isfile():
                source=bundle.extractfile(member)
                if source is None:raise ValueError('SDK regular archive member unavailable')
                h=hashlib.file_digest(source,'sha256').hexdigest();expected[key]={'bytes':member.size,'sha256':h,'mode':permission,'archiveUid':member.uid,'archiveGid':member.gid}
                current=root
                for part in pathlib.PurePosixPath(key).parts:
                    current=current/part
                    if current.is_symlink():raise ValueError('SDK regular member physical ancestry contains symlink')
                if not path.is_file() or path.stat().st_size!=member.size or stat.S_IMODE(path.stat().st_mode)!=permission or digest(path)!=h:raise ValueError('Fresh SDK member differs from official archive: '+key)
            elif member.issym():
                if not path.is_symlink() or path.readlink().as_posix()!=member.linkname or not path.resolve(strict=True).is_relative_to(root):raise ValueError('SDK symbolic member differs or escapes')
                if stat.S_IMODE(path.lstat().st_mode)!=permission:raise ValueError('SDK symbolic member permissions differ')
                links[key]={'target':member.linkname,'mode':permission,'archiveUid':member.uid,'archiveGid':member.gid}
            else:raise ValueError('Unadmitted SDK archive member kind')
    actual=set();actualDirectories=set()
    for p in root.rglob('*'):
        mode=p.lstat().st_mode;key=p.relative_to(root).as_posix()
        if stat.S_ISREG(mode) or stat.S_ISLNK(mode):actual.add(key)
        elif stat.S_ISDIR(mode):actualDirectories.add(key)
        else:raise ValueError('Unexpected special node in SDK complete inventory: '+key)
    if actual!=set(expected)|set(links):raise ValueError('Fresh SDK installed payload differs from complete official inventory')
    if '.' in directories:actualDirectories.add('.')
    if actualDirectories!=set(directories):raise ValueError('Fresh SDK directory inventory differs from complete official archive')
    return {'root':str(root),'archivePath':str(archive),'archiveSha512':SDK_SHA512,'sdkFiles':expected,'sdkLinks':links,'sdkDirectories':directories}

def admit(installed_root,archive_path,metadata_record,output):
    verified=verify_install(installed_root,archive_path)
    root=pathlib.Path(verified['root']);archive=pathlib.Path(verified['archivePath']);out=pathlib.Path(output);out.mkdir(parents=True,exist_ok=True)
    expected=verified['sdkFiles'];links=verified['sdkLinks'];directories=verified['sdkDirectories']
    def digest(p):
        with pathlib.Path(p).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
    provider=root/'sdk'/SDK_VERSION/'Extensions/Microsoft.TestPlatform.TestHostRuntimeProvider.dll'
    if pathlib.Path(metadata_record['providerPath']).resolve(strict=True)!=provider or metadata_record['sha256']!=expected[provider.relative_to(root).as_posix()]['sha256']:raise ValueError('Actual PE metadata is not official SDK provider')
    if metadata_record.get('productVersion')!='18.6.0-release-26270-133':raise ValueError('Actual SDK provider ProductVersion differs from source-bound VMR build')
    urls={'vmr':f'https://raw.githubusercontent.com/dotnet/dotnet/{VMR_COMMIT}/src/source-manifest.json','source':f'https://raw.githubusercontent.com/microsoft/vstest/{VSTEST_COMMIT}/src/Microsoft.TestPlatform.TestHostProvider/Hosting/DotnetTestHostManager.cs'}
    payloads={}
    for label,url in urls.items():
        with urllib.request.urlopen(url,timeout=30) as response:
            if response.status!=200 or response.geturl()!=url:raise ValueError('Immutable official source response mismatch')
            b=response.read(524289)
        if not b or len(b)>524288:raise ValueError('Official source size outside bounds')
        payloads[label]=b;(out/(label+'.json' if label=='vmr' else 'DotnetTestHostManager.cs')).write_bytes(b)
    repositories=json.loads(payloads['vmr'])['repositories'];rows=[r for r in repositories if r['path']=='vstest']
    if len(rows)!=1 or rows[0]['remoteUri']!='https://github.com/microsoft/vstest' or rows[0]['commitSha']!=VSTEST_COMMIT:raise ValueError('SDK VMR source manifest VSTest lineage mismatch')
    if hashlib.sha256(payloads['source']).hexdigest()!=SOURCE_SHA256:raise ValueError('Actual SDK source contract differs from reviewed immutable bytes')
    deps=root/'sdk'/SDK_VERSION/'vstest.console.deps.json';deps_data=json.loads(deps.read_bytes())
    libraries={k:v for k,v in deps_data['libraries'].items() if k.startswith(('Microsoft.TestPlatform.','Microsoft.VisualStudio.TestPlatform.'))}
    if not libraries:raise ValueError('SDK VSTest actual dependency version records absent')
    executable=root/'dotnet';record={'path':str(executable),'sha256':expected['dotnet']['sha256'],'sdkVersion':SDK_VERSION,'officialArchiveSha512':SDK_SHA512}
    if digest(executable)!=record['sha256'] or digest(provider)!=metadata_record['sha256']:raise ValueError('SDK producer changed during admission')
    receipt={'sdkExecutable':record,'archivePath':str(archive),'archiveUrl':SDK_URL,'archiveSha512':SDK_SHA512,'sdkFiles':expected,'sdkLinks':links,'sdkDirectories':directories,'provider':metadata_record,'vmrCommit':VMR_COMMIT,'vstestCommit':VSTEST_COMMIT,'sourceSha256':SOURCE_SHA256,'sourceLineageAccepted':True,'depsSha256':digest(deps),'testPlatformLibraries':libraries,'qualification':'Complete official archive byte/type/permission/directory inventory and immutable source lineage; archive uid/gid retained as provenance, installed ownership not asserted equal. Sampled pre/post hashes are not an atomic file lease; caller must recheck before/after native execution.'}
    (out/'official-sdk-admission.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return record,receipt
