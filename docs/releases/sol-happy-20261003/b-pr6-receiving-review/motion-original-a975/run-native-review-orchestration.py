#!/usr/bin/env python3
"""Ordinary whole native Motion source build and unchanged Program self-test."""
import hashlib, importlib.util, json, os, shutil, subprocess, sys
from pathlib import Path

SOURCE='a975e539fa5e8bf045104e6931f5e5e15d3c8695'
REPO=Path('/workspace/c1-motion-original-owner-closure')
OUTPUT=Path('/tmp/team-c-c5-motion-native-a975-20261004')
RECEIPTS=Path('/workspace/team-c-resume-evidence/c5-motion-original-a975')
PROJECT='9to1 Workspace/Motion/HavenOS.Motion.csproj'
sha=lambda data:hashlib.sha256(data).hexdigest()
proposal=json.loads((RECEIPTS/'c1-source-proposal.json').read_text())
rows=proposal['nativeProjectReadOnlyClosure']['wholeSource']
assert len(rows)==653 and len(proposal['nativeProjectReadOnlyClosure']['projects'])==5
assert not OUTPUT.exists() and shutil.disk_usage('/tmp').free>750000000
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=REPO).decode().strip()==SOURCE
assert not subprocess.check_output(['git','status','--porcelain'],cwd=REPO)
sys.dont_write_bytecode=True
helper=REPO/'apps/Web/Tests/ci/run-ordinary-native.py'
assert sha(helper.read_bytes())=='a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'
spec=importlib.util.spec_from_file_location('maintained_motion_native_commands',helper)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
OUTPUT.mkdir();diag=OUTPUT/'diagnostics';diag.mkdir()
env=os.environ.copy()
for var,name in {'DOTNET_CLI_HOME':'cli','NUGET_PACKAGES':'nuget','NUGET_HTTP_CACHE_PATH':'http',
    'NUGET_PLUGINS_CACHE_PATH':'plugins','XDG_CACHE_HOME':'cache','XDG_CONFIG_HOME':'config',
    'TMPDIR':'tmp','TMP':'tmp','TEMP':'tmp'}.items():
    p=OUTPUT/name;p.mkdir(exist_ok=True);env[var]=str(p)
env.update(CI='true',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_TELEMETRY_OPTOUT='1',
    DOTNET_NOLOGO='1',MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1',
    NUGET_FALLBACK_PACKAGES='/workspace/.tools/nuget/packages')
commands=module.Commands(diag,env,REPO)
def write(name,value):(diag/name).write_text(json.dumps(value,indent=2)+'\n')
def snapshot():
    found=[]
    for row in rows:
        p=REPO/row['path'];body=p.read_bytes();s=row['source']
        assert p.is_file() and not p.is_symlink() and len(body)==s['bytes'] and sha(body)==s['sha256']
        found.append({'path':row['path'],'gitBlob':s['blob'],'bytes':len(body),'sha256':sha(body)})
    return sorted(found,key=lambda r:r['path'])
result={'state':'NOT_RUN','sourceCommit':SOURCE,'actualProjects':5,'sourceInputs':653,
    'scope':'Whole current native Motion managed graph and unchanged Program self-test only; no UI, real media decoding/donor/provider/Files registration/Windows/full Motion acceptance',
    'assertions':None,'assertionQualification':'Native Program and original Run return codes are not measured assertion totals.'}
before=None;runtime_before=None;target=None
try:
    assert commands.run('git-head',['git','rev-parse','HEAD'],15).strip()==SOURCE
    commands.run('git-clean-before',['git','diff','--exit-code','HEAD','--'],15)
    assert commands.run('dotnet-version',['dotnet','--version'],30).strip()=='10.0.401'
    before=snapshot();write('source-before.json',before)
    artifacts=OUTPUT/'artifacts'
    flags=['--artifacts-path',str(artifacts),'-r','linux-x64','-p:SelfContained=false','-p:UseAppHost=true',
        '-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nodeReuse:false']
    commands.run('restore',['dotnet','restore',PROJECT,'--configfile',str(REPO/'NuGet.Config'),
        '--disable-parallel','-p:Configuration=Release']+flags,600)
    assets=artifacts/'obj/HavenOS.Motion/project.assets.json'
    write('actual-package-folders.json',json.loads(assets.read_text())['packageFolders'])
    commands.run('build',['dotnet','build',PROJECT,'--no-restore','-c','Release']+flags,900)
    evaluated=module.parse_sdk(commands.run('target-properties',['dotnet','msbuild',PROJECT,
        '-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64','-p:UseAppHost=true',
        '-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false',
        '-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost'],30))
    write('target-properties.json',evaluated)
    target=Path(evaluated['TargetPath']);apphost=target.with_suffix('')
    assert target.is_file() and target.resolve().is_relative_to(artifacts) and target.read_bytes()[:2]==b'MZ'
    assert evaluated['TargetFramework']=='net10.0' and evaluated['RuntimeIdentifier']=='linux-x64' and evaluated['UseAppHost'].lower()=='true'
    assert apphost.is_file() and apphost.read_bytes()[:5]==b'\x7fELF\x02' and os.access(apphost,os.X_OK)
    runtime_before=[{'path':str(p.relative_to(target.parent)),'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
        for p in sorted(target.parent.rglob('*')) if p.is_file()]
    write('runtime-before.json',runtime_before)
    commands.run('native-current-program-self-test',[str(apphost),'--self-test'],120)
    status=json.loads(commands.run('native-current-program-status',[str(apphost),'status'],30))
    assert status['Route']=='motion' and status['TimelineAvailable'] and status['PersistenceAvailable']
    assert not status['EngineAvailable'] and not status['RenderAvailable'] and not status['ExportAvailable']
    write('current-program-status.json',status)
    result.update(state='PASS_WHOLE_NATIVE_MOTION_CURRENT_PROGRAM_SCOPED',selfTestReturnCode=0,
        entryDLLSHA256=sha(target.read_bytes()),apphostSHA256=sha(apphost.read_bytes()))
except Exception as error:result.update(state='FAIL_OR_INCOMPLETE',error=repr(error))
finally:
    try:
        after=snapshot();write('source-after.json',after);assert before is None or before==after
        if runtime_before is not None:
            runtime_after=[{'path':str(p.relative_to(target.parent)),'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
                for p in sorted(target.parent.rglob('*')) if p.is_file()]
            write('runtime-after.json',runtime_after);assert runtime_before==runtime_after
        commands.run('git-clean-after',['git','diff','--exit-code','HEAD','--'],15)
        assert commands.run('git-head-after',['git','rev-parse','HEAD'],15).strip()==SOURCE
    except Exception as error:result.update(state='FAIL_OR_INCOMPLETE',finalCustodyError=repr(error))
    result['commandFamilies']=len(commands.records)
    result['allCommandFamiliesNormalClosed']=all(c['exit']==c['exitAfterDrain']==0 and c['normalEOF'] and c['familyClosed']
        and c['finalECHILD'] and not c['signals'] and c['error'] is None and all(b['gone'] for b in c['births']) for c in commands.records)
    result['runtimeFiles']=None if runtime_before is None else len(runtime_before)
    result['acceptedScoped']=result['state']=='PASS_WHOLE_NATIVE_MOTION_CURRENT_PROGRAM_SCOPED' and result['allCommandFamiliesNormalClosed']
    write('result.json',result)
print(json.dumps(result,indent=2));sys.exit(0 if result['acceptedScoped'] else 1)
