import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

root = Path('/workspace/team-c-c5-approved-writeback-picture-owner-repair')
output = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/picture-owner-repair-release')
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
projects = [('picture15', 'apps/Web/Tests/PictureAdmission/PictureAdmission.Tests.csproj', 'NineToOne.Picture.Admission.Tests')]

results = {'sourceCommit':source, 'receivingBase':'43c5b0ebf7d26f46ee9a29ad009e9dd0e68b835a',
           'scope':'Existing maintained actual projects and source-linked owning project graph; no test/source edits. Chat controlled interface, lifecycle missing owner authority, Picture controlled media are scoped supporting controls.',
           'qualification':'Root sole integrator. No browser/Windows/provider/full release acceptance. Sequential reuse only task-owned source-built outputs, with complete current actual project graph; no alternate/copied vendor DLL.',
           'recipeDifferencesFromHostedCI':{'NuGetAudit':False, 'taskPrivateWritableXDGConfig':True,
               'cachedActualNuGetPackages':'/workspace/.tools/nuget/packages',
               'sharedOwnArtifactsAcrossSerialNormalProjects':True, 'sourceCompilerTaskSubstitute':False},
           'projects':[]}
def dump():
    (diagnostics/'result.json').write_text(json.dumps(results,indent=2)+'\n')
def source_snapshot():
    records=[]
    for line in subprocess.check_output(['git','ls-tree','-rz','HEAD'],cwd=root).split(b'\0'):
        if not line: continue
        fields,name=line.split(b'\t',1)
        mode,kind,blob=fields.split()
        if kind != b'blob': continue
        p=root/os.fsdecode(name)
        records.append({'path':os.fsdecode(name),'gitBlob':blob.decode(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'bytes':p.stat().st_size})
    return records
before = source_snapshot()
(diagnostics/'source-before.json').write_text(json.dumps(before,indent=2)+'\n')
assert commands.run('head-before',['git','rev-parse','HEAD'],15).strip() == source
commands.run('clean-before',['git','diff','--exit-code','HEAD','--'],15)
assert commands.run('sdk-version',['dotnet','--version'],30).strip() == '10.0.401'
for name, project, assembly in projects:
    result={'name':name,'project':project,'status':'NOT_RUN'}
    results['projects'].append(result)
    dump()
    try:
        commands.run(name+'-restore',['dotnet','restore',project,'--configfile',str(root/'NuGet.Config'),'-p:Configuration=Release','--disable-parallel']+common,300)
        commands.run(name+'-build',['dotnet','build',project,'--no-restore','-c','Release']+common,300)
        target_properties = module.parse_sdk(commands.run(name+'-target',['dotnet','msbuild',project,
            '-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64','-p:UseArtifactsOutput=true',
            '-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false',
            '-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost']+props,30))
        target = Path(target_properties['TargetPath']).resolve()
        apphost = target.with_suffix('')
        assert target.is_relative_to(artifacts) and target.is_file() and apphost.is_file()
        assert target_properties['TargetFramework']=='net10.0' and target_properties['RuntimeIdentifier']=='linux-x64' and target_properties['UseAppHost'].lower()=='true'
        result.update(targetProperties=target_properties, entrySHA256=hashlib.sha256(target.read_bytes()).hexdigest(), apphostSHA256=hashlib.sha256(apphost.read_bytes()).hexdigest(), buildSucceeded=True)
        native_args = [str(apphost)]
        if name != 'chat10': native_args.append(str(output/'fixture-data'/name))
        if name == 'sites18': env['HAVEN_DATA_DIR']=str(output/'fixture-data'/name/'profile')
        try:
            log = commands.run(name+'-native',native_args,120)
        except RuntimeError as error:
            result.update(status='FAILED', error=repr(error),actualNative=commands.records[-1])
            if name=='picture15' and (output/'fixture-data'/name/'results.json').is_file():
                native=json.loads((output/'fixture-data'/name/'results.json').read_text())
                result['actualNativeCounts']={k:native.get(k) for k in ('discovered','executed','passed','failed','notRun','timedOut','prerequisite','assertions','exitCode')}
                result['failures']=[r for r in native['results'] if r['state']!='PASS']
                (diagnostics/'picture15-native-results.json').write_bytes((output/'fixture-data'/name/'results.json').read_bytes())
            dump()
            continue
        native=json.loads((output/'fixture-data'/name/'results.json').read_text())
        result['actualNativeCounts']={k:native.get(k) for k in ('discovered','executed','passed','failed','notRun','timedOut','prerequisite','assertions','exitCode')}
        (diagnostics/'picture15-native-results.json').write_bytes((output/'fixture-data'/name/'results.json').read_bytes())
        result.update(status='EXECUTED_EXIT_ZERO',actualNative=commands.records[-1],passLabels=re.findall(r'^PASS(?:[: ]|$).*$',log,re.M),summaryLines=[l for l in log.splitlines() if l.startswith(('RESULT ','SUPPORTING_SCRIPTED_INTERFACE_UNIT '))])
        dump()
    except Exception as error:
        result.update(status='FAILED_PREREQUISITE',error=repr(error),firstFailedCommand=commands.records[-1])
        dump()
after=source_snapshot()
(diagnostics/'source-after.json').write_text(json.dumps(after,indent=2)+'\n')
results['sourceUnchanged']=before==after
assert before==after
assert commands.run('head-after',['git','rev-parse','HEAD'],15).strip()==source
commands.run('clean-after',['git','diff','--exit-code','HEAD','--'],15)
dump()
print(json.dumps({'sourceCommit':source,'sourceUnchanged':results['sourceUnchanged'],'projects':[{k:v for k,v in r.items() if k in ('name','status','error','actualNativeCounts','summaryLines')} for r in results['projects']]},indent=2),flush=True)
