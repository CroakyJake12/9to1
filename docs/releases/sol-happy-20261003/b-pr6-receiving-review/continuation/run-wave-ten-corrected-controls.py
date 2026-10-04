import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

root = Path('/workspace/team-c-c5-wave-canonical5-provisional')
output = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/wave-ten-native-corrected-controls')
output.mkdir()
diagnostics = output / 'diagnostics'
diagnostics.mkdir()
source = '9cd3e846fdebf6d6f42262407ceeee5cd8973cb1'
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
artifacts = Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/wave-ten-native-release/artifacts')
task = artifacts / 'bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll'
props = ['-p:SelfContained=false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
         '-p:AvaloniaBuildTasksLocation=' + str(task),
         '-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false',
         '-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false',
         '-p:CreateHardLinksForCopyLocalIfPossible=false',
         '-p:CreateHardLinksForPublishFilesIfPossible=false']
common = ['--artifacts-path', str(artifacts), '-r', 'linux-x64', '--disable-build-servers', '-m:1', '-nodeReuse:false'] + props
result={'sourceCommit':source,'scope':'OriginalFilesWorkflowfirstassertionswithrequiredphysicalrealPCMfixtureconfigured; priorinitialmissingfixtureNOT_RUN retained. Exactalreadycompiledcurrentnativeproject/CLI andactualengine source graph; no assertions/production/expectedvalues edits.' ,'stages':[]}
assert commands.run('head-before',['git','rev-parse','HEAD'],15).strip()==source
commands.run('clean-before',['git','diff','--exit-code','HEAD','--'],15)
app='/workspace/team-c-resume-evidence/c5-b-pr6-receiving/wave-ten-native-release/artifacts/bin/HavenOS.Wave/release_linux-x64/HavenOS.Wave'
assert hashlib.sha256(Path(app+'.dll').read_bytes()).hexdigest()=='37dd3f68f0115f54a8ce8b934d8cdfda38b3de6020814251380700b85d77592a'
env['ASTRA_GSTREAMER_FIXTURE']='/workspace/team-c-resume-evidence/c5-b-pr6-receiving/wave-ten-native-release/fixture-data/files-pcm16-controlled.wav'
log=commands.run('files-workflow-configured',[app,'--files-workflow-test'],120);result['stages'].append({'name':'originalFilesWorkflowwithRequiredFixture','exit':0,'scope':'controllednativelease/revision/movedsource/exactphysicalPCMexport; no productionadapter'})
projectPath=output/'fixture-data/console-roundtrip.waveproject.json'
commands.run('cli-create',[app,'--project-create',str(projectPath),'C5 original native track'],30)
before=json.loads(commands.run('cli-open',[app,'project','open',str(projectPath)],30))['result'];assert before['Revision']==0 and len(before['Tracks'])==1
changed=json.loads(commands.run('cli-add-track',[app,'project','add-track',str(projectPath),'0','C5 additional native track'],30))['result'];assert changed['Revision']==1 and changed['ProjectId']==before['ProjectId'] and len(changed['Tracks'])==2
filehash=hashlib.sha256(projectPath.read_bytes()).hexdigest()
try:
 commands.run('cli-stale-revision',[app,'project','add-track',str(projectPath),'0','C5 stale must refuse'],30)
 raise AssertionError('staleCLI unexpectedly succeeded')
except RuntimeError:
 assert commands.records[-1]['exit']==2
 payload=json.loads((diagnostics/'cli-stale-revision.log').read_text());assert payload['ok']==False and payload['error']['code']=='RevisionConflict'
assert hashlib.sha256(projectPath.read_bytes()).hexdigest()==filehash
reopened=json.loads(commands.run('cli-reopen',[app,'project','open',str(projectPath)],30))['result'];assert reopened['Revision']==1 and reopened['ProjectId']==before['ProjectId'] and len(reopened['Tracks'])==2
result['stages'].append({'name':'actualconsole-cli-roundtrip-and-stale-save-refusal','createOpenMutationReopen':True,'staleRevisionExit':2,'latestBytesPreserved':True})
engine='apps/Web/Wave/Engine/NineToOne.Wave.BrowserEngine.csproj'
commands.run('browser-engine-restore',['dotnet','restore',engine,'--configfile',str(root/'NuGet.Config'),'-p:Configuration=Release','--disable-parallel']+common,300)
commands.run('browser-engine-build',['dotnet','build',engine,'--no-restore','-c','Release']+common,300)
manifests=list(artifacts.glob('obj/NineToOne.Wave.BrowserEngine/**/owner-decoder-manifest.json'));assert len(manifests)==1
m=json.loads(manifests[0].read_text());assert m['sourceSha256']=='4cabd81a662c87df0cc8f70326099b5b358c182c184a6f0c0b60344bdaf77b6f' and m['prefixSha256']=='5426f61c5568b4728d3409d3dd3722dd4a70b23867b86168f0967da4b6a7125a'
(diagnostics/'actual-owner-decoder-manifest.json').write_bytes(manifests[0].read_bytes());result['stages'].append({'name':'actualsource-linked-browser-engine-build','exit':0,'exactOriginalGeneratorAndSourceGuards':True})
publisher=root/'apps/Web/Tests/ci/run-ordinary-web-publish.py'
spec=importlib.util.spec_from_file_location('actual_maintained_publisher_source_check',publisher);pmod=importlib.util.module_from_spec(spec);spec.loader.exec_module(pmod)
tree=subprocess.check_output(['git','ls-tree','-rz','HEAD'],cwd=root).decode();inputs=pmod.source_snapshot(root,tree)
(diagnostics/'current-publisher-source-presence.json').write_text(json.dumps({'sourceCommit':source,'currentMaintainedSourceGuardPass':True,'inputs':len(inputs),'linkedCuts':pmod.LINKED_CUTS,'scope':'sameoriginalsource-custodyfunctiononly; NOTactualWebbuild/publish/runtime'},indent=2)+'\n')
result['status']='SCOPED_REMAINING_NATIVE_AND_ENGINE_PASS'
result['sourceAfter']=commands.run('head-after',['git','rev-parse','HEAD'],15).strip();commands.run('clean-after',['git','diff','--exit-code','HEAD','--'],15);assert result['sourceAfter']==source
(diagnostics/'result.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2),flush=True)
