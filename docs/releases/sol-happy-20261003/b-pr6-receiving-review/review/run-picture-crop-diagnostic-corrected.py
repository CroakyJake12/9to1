import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

root = Path('/workspace/team-c-c5-b-picture-eec-closure-provisional')
output = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/picture-crop-diagnostic/corrected')
output.mkdir()
diagnostics = output / 'diagnostics'
diagnostics.mkdir()
source = '680f1301c3ae255a645822adb55be36ec11bb146'
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
artifacts = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/normal-release/artifacts')
task = artifacts / 'bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll'
props = ['-p:SelfContained=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
         '-p:AvaloniaBuildTasksLocation=' + str(task),
         '-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false',
         '-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false',
         '-p:CreateHardLinksForCopyLocalIfPossible=false',
         '-p:CreateHardLinksForPublishFilesIfPossible=false']
common = ['--artifacts-path', str(artifacts), '-r', 'linux-x64', '--disable-build-servers', '-m:1', '-nodeReuse:false'] + props
result={'sourceCommit':source,'scope':'DIAGNOSTIC_ONLY: exactoldproduction source, original15program/assertions unchanged except one synchronouspreassertJSONobservation. No acceptance or testweakening.'}
assert commands.run('head-before',['git','rev-parse','HEAD'],15).strip()==source
commands.run('clean-before',['git','diff','--exit-code','HEAD','--'],15)
try:
 project='/workspace/team-c-resume-evidence/c5-b-pr6-receiving/picture-crop-diagnostic/PictureAdmission.Diagnostics.csproj'
 commands.run('restore',['dotnet','restore',project,'--configfile',str(root/'NuGet.Config'),'-p:Configuration=Release','--disable-parallel']+common,300)
 commands.run('build',['dotnet','build',project,'--no-restore','-c','Release']+common,300)
 tp=module.parse_sdk(commands.run('target',['dotnet','msbuild',project,'-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false','-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost']+props,30))
 target=Path(tp['TargetPath']).resolve();assert target.is_relative_to(artifacts) and target.is_file() and target.with_suffix('').is_file()
 result['targetProperties']=tp;result['entrySHA256']=hashlib.sha256(target.read_bytes()).hexdigest()
 try:
  log=commands.run('native',[str(target.with_suffix('')),str(output/'fixture-data'),str(root/'apps/Web/Tests/PictureAdmission/Fixtures/Picture8x6.png')],120)
 except RuntimeError as error:
  result['retainedNativeFailure']=repr(error);log=(diagnostics/'native.log').read_text()
 observations=[json.loads(line.split('C5_DIAGNOSTIC_CROP ',1)[1]) for line in log.splitlines() if line.startswith('C5_DIAGNOSTIC_CROP ')]
 assert len(observations)==1;result['actualObservation']=observations[0]
 result['actualOriginalControl']=json.loads((output/'fixture-data/results.json').read_text())
except Exception as error:
 result['firstError']=repr(error)
result['sourceAfter']=commands.run('head-after',['git','rev-parse','HEAD'],15).strip()
commands.run('clean-after',['git','diff','--exit-code','HEAD','--'],15);assert result['sourceAfter']==source
(diagnostics/'result.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:v for k,v in result.items() if k!='actualOriginalControl'},indent=2),flush=True)
