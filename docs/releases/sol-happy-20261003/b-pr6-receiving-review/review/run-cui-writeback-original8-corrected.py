import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

root = Path('/workspace/team-c-c5-approved-writeback-picture-owner-repair')
output = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/cui-writeback-current/target-corrected')
output.mkdir()
diagnostics = output / 'diagnostics'
diagnostics.mkdir()
source = 'a0de5ad5c2975c98140be79dfcac884dea0ef76a'
custodian = root / 'apps/Web/Tests/ci/run-ordinary-native.py'
assert hashlib.sha256(custodian.read_bytes()).hexdigest() == 'a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('actual_reviewed_b_commands', custodian)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
env = os.environ.copy()
for variable, directory in {'DOTNET_CLI_HOME':'cli', 'XDG_CONFIG_HOME':'xdg-config', 'XDG_CACHE_HOME':'font-cache', 'TMPDIR':'tmp', 'TMP':'tmp', 'TEMP':'tmp', 'HAVEN_DATA_DIR':'fixture-data'}.items():
    path = output / directory
    path.mkdir(exist_ok=True)
    env[variable] = str(path)
env.update(DOTNET_ROOT='/workspace/.tools/dotnet', NUGET_PACKAGES='/workspace/.tools/nuget/packages',
           PATH='/workspace/.tools/dotnet:' + env['PATH'], DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
           DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
           DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1', AVALONIA_TELEMETRY_OPTOUT='1',
           PYTHONDONTWRITEBYTECODE='1')
commands = module.Commands(diagnostics, env, root)
artifacts = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/picture-eec-closure-release/artifacts')
task = artifacts / 'bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll'
props = ['-p:SelfContained=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
         '-p:AvaloniaBuildTasksLocation=' + str(task),
         '-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false',
         '-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false',
         '-p:CreateHardLinksForCopyLocalIfPossible=false',
         '-p:CreateHardLinksForPublishFilesIfPossible=false']
common = ['--artifacts-path', str(artifacts), '-r', 'linux-x64', '--disable-build-servers', '-m:1', '-nodeReuse:false'] + props
source='a0de5ad5c2975c98140be79dfcac884dea0ef76a'
assert commands.run('head-before',['git','rev-parse','HEAD'],15).strip()==source
commands.run('clean-before',['git','diff','--exit-code','HEAD','--'],15)
project='/workspace/team-c-resume-evidence/c5-b-pr6-receiving/cui-writeback-current/CuiSameTurn.Current.csproj'
tp=module.parse_sdk(commands.run('original8-target',['dotnet','msbuild',project,'-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false','-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost']+props,30))
target=Path(tp['TargetPath']).resolve()
assert target.is_relative_to(artifacts) and target.is_file() and target.with_suffix('').is_file()
assert tp['TargetFramework']=='net10.0' and tp['RuntimeIdentifier']=='linux-x64' and tp['UseAppHost'].lower()=='true'
log=commands.run('original8-native',[str(target.with_suffix(''))],120)
assert '8/8 actual native same-turn writeback checks PASS' in log
result={'sourceCommit':source,'scope':'Exact original8 unchanged and already compiled from actualfullcurrentRuntime/Headless project. Corrected SDK one-property JSON orchestration only; original failedquery beforetest NOT_RUN retained. No source/oracle edit or test retry.','targetProperties':tp,'original8passed':8,'failed':0,'nativeExit':0,'entrySHA256':hashlib.sha256(target.read_bytes()).hexdigest(),'sourceAfter':commands.run('head-after',['git','rev-parse','HEAD'],15).strip()}
commands.run('clean-after',['git','diff','--exit-code','HEAD','--'],15)
assert result['sourceAfter']==source
(diagnostics/'result.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2),flush=True)
