#!/usr/bin/env bash
set -euo pipefail
nine_to_one_launcher_code=$(cat <<'PY'
import hashlib,json,os,re,stat,sys
from pathlib import Path,PurePosixPath
base=Path('/home/jacob/Documents/Codex/2026-10-24/prepare-the-local-9to1-teams-from')
configuration=base/'outputs/9to1-local-launch.json'
def refuse(message):
    print('9to1: '+message,file=sys.stderr);raise SystemExit(78)
def digest(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        while chunk:=stream.read(1024*1024):value.update(chunk)
    return value.hexdigest()
options=sys.argv[1:]
if len(options)!=len(set(options)) or any(option not in ('--check','--console') for option in options):
    refuse('Use no arguments for the desktop, --console for the native Task console, and optionally --check to verify only.')
console='--console' in options
if not configuration.is_file():refuse('The verified Linux development build has not been installed yet. No app or profile was started.')
try:
    settings=json.loads(configuration.read_text())
    if settings.get('schema')!='9to1.local-desktop-launch/v1':refuse('The local build registration is invalid.')
    source=settings.get('source_revision','')
    if not re.fullmatch('[0-9a-f]{40}',source):refuse('The registered source identity is invalid.')
    artifact=Path(settings['artifact_directory'])
    allowed=(base/'work/desktop-artifacts').resolve()
    if not artifact.is_absolute() or artifact.is_symlink() or not artifact.is_dir() or not artifact.resolve().is_relative_to(allowed):refuse('The development artifact is outside its verified installation directory.')
    manifest_path=artifact/'Haven-linux-x64.manifest.json'
    if manifest_path.is_symlink() or digest(manifest_path)!=settings['manifest_sha256']:refuse('The package manifest changed; rebuild or restore the exact artifact.')
    manifest=json.loads(manifest_path.read_text())
    if (manifest.get('schema')!='9to1.linux-desktop-package/v1' or manifest.get('source_revision')!=source or
        manifest.get('rid')!='linux-x64' or manifest.get('target_framework')!='net10.0' or
        manifest.get('self_contained') is not False or manifest.get('entrypoint')!='app/Haven.dll' or
        manifest.get('publish_result')!='PROCESS_SUCCEEDED'):refuse('The installed package does not match its built Linux candidate.')
    expected=set()
    for item in manifest['files']:
        relative=PurePosixPath(item['path'])
        if relative.is_absolute() or '..' in relative.parts or not relative.parts or relative.parts[0]!='app' or '\\' in item['path']:refuse('An invalid package path was recorded.')
        if item['path'] in expected:refuse('The package contains duplicate file identities.')
        expected.add(item['path']);path=artifact/item['path']
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(artifact.resolve()):refuse('A package file is missing or replaced by a link.')
        if path.stat().st_size!=item['bytes'] or digest(path)!=item['sha256']:refuse('A built application file changed: '+item['path'])
    actual={path.relative_to(artifact).as_posix() for path in (artifact/'app').rglob('*') if path.is_file()}
    if actual!=expected:refuse('The published application file set changed.')
    runtime=base/'work/dotnet-sdk-10.0.301/dotnet'
    if runtime.is_symlink() or not runtime.is_file() or digest(runtime)!=settings['dotnet_sha256']:refuse('The verified local .NET runtime is unavailable or changed.')
    profile=base/'work/9to1-development-profile'
    if profile.exists() and (profile.is_symlink() or not profile.is_dir() or stat.S_IMODE(profile.stat().st_mode)&0o077):refuse('The existing development profile is not a private directory; it was preserved.')
    if '--check' in options:
        print('Verified development artifact '+source+'. Native workflow smoke is a separate check.');raise SystemExit(0)
    os.umask(0o077);profile.mkdir(mode=0o700,parents=True,exist_ok=True)
    env=dict(os.environ);env['HAVEN_DATA_DIR']=str(profile);env['DOTNET_ROOT']=str(runtime.parent);env['DOTNET_MULTILEVEL_LOOKUP']='0'
    if console:env['HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY']='1' # Requests normal capture; grants no permission/readiness.
    os.chdir(artifact/'app')
    print('Starting 9to1 development candidate '+source[:12]+' with its separate persistent profile.',flush=True)
    app_arguments=['--local-task-console','--data-directory',str(profile)] if console else []
    os.execve(runtime,[str(runtime),str(artifact/'app/Haven.dll'),*app_arguments],env)
except (OSError,KeyError,ValueError,TypeError) as error:
    refuse('The verified development build could not be opened ('+type(error).__name__+'). Existing data was preserved; no replacement build was adopted.')
PY
)
exec python3 -c "$nine_to_one_launcher_code" "$@"
