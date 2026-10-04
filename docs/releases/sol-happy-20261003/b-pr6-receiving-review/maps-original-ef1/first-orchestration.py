#!/usr/bin/env python3
"""Ordinary current original Maps project invocation via unchanged Commands.

This is task orchestration, not the fixed 889-source Canvas/Maps acceptance driver.
It compiles and runs the complete unchanged original Maps test project and records
normal discovery/TRX/source/runtime custody without replacing any assertion.
"""
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE = 'ef1d3a7546c130ef5948fab0711ef5d9feca9381'
REPO = Path('/workspace/c1-maps-original-owner-controls')
OUTPUT = Path('/tmp/team-c-c5-maps-original-ef1-20261004')
RECEIPT = Path('/workspace/team-c-resume-evidence/c1-pr6-current-review/maps-two-wholefile-source-proposal.json')
PROJECT = 'apps/Web/Productivity/Maps/Tests/Maps.OriginalAuthoring.Tests.csproj'
COMMANDS = 'apps/Web/Tests/ci/run-ordinary-native.py'
COMMANDS_SHA = 'a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'


def sha(body):
    return hashlib.sha256(body).hexdigest()


def write(name, value):
    (DIAG / name).write_text(json.dumps(value, indent=2) + '\n')


def snapshot():
    rows = []
    for row in proposal['actualCurrentManagedClosure']['files']:
        path = REPO / row['path']
        assert path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(REPO)
        body = path.read_bytes()
        actual = row['actualReceiving']
        assert len(body) == actual['bytes'] and sha(body) == actual['sha256']
        rows.append({'path':row['path'], 'gitBlob':actual['blob'], 'bytes':len(body), 'sha256':sha(body)})
    for path in [REPO / COMMANDS, REPO / 'global.json', REPO / 'NuGet.Config']:
        if path.relative_to(REPO).as_posix() not in {row['path'] for row in rows}:
            body = path.read_bytes()
            rows.append({'path':path.relative_to(REPO).as_posix(), 'bytes':len(body), 'sha256':sha(body)})
    return sorted(rows, key=lambda row:row['path'])


assert not OUTPUT.exists() and OUTPUT.is_absolute() and not OUTPUT.is_relative_to(REPO)
assert shutil.disk_usage('/tmp').free > 750000000
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=REPO).decode().strip() == SOURCE
assert not subprocess.check_output(['git','status','--porcelain'],cwd=REPO)
proposal = json.loads(RECEIPT.read_text())
assert proposal['proposalCommit'] == SOURCE and proposal['originalExpectedTotal'] == 6
assert sha((REPO / COMMANDS).read_bytes()) == COMMANDS_SHA
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('maintained_commands', REPO / COMMANDS)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
assert sha((REPO / COMMANDS).read_bytes()) == COMMANDS_SHA
OUTPUT.mkdir()
DIAG = OUTPUT / 'diagnostics'
DIAG.mkdir()
env = os.environ.copy()
for variable, folder in {'DOTNET_CLI_HOME':'cli','NUGET_PACKAGES':'nuget','NUGET_HTTP_CACHE_PATH':'http',
                        'NUGET_PLUGINS_CACHE_PATH':'plugins','XDG_CACHE_HOME':'cache','TMPDIR':'tmp','TMP':'tmp','TEMP':'tmp'}.items():
    path = OUTPUT / folder
    path.mkdir()
    env[variable] = str(path)
env.update(CI='true', DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1', PYTHONDONTWRITEBYTECODE='1')
commands = module.Commands(DIAG, env, REPO)
result = {'state':'NOT_RUN','sourceCommit':SOURCE,
    'scope':'Six original managed authoring/capture/filesystem/CAS cases only; no Maps UI/CoMaps/native Home/provider/actor/Windows/full parity acceptance',
    'fixed889CombinedDriver':'NOT_IMPORTED_OR_REBOUND', 'expectedTests':proposal['originalExpectedNativeNames']}
before = None
runtime_before = None
runtime_dir = None
try:
    assert commands.run('git-head',['git','rev-parse','HEAD'],15).strip() == SOURCE
    commands.run('git-clean-before',['git','diff','--exit-code','HEAD','--'],15)
    commands.run('source-tree',['git','ls-tree','-r','HEAD'],15)
    before = snapshot()
    write('source-before.json',before)
    assert commands.run('dotnet-version',['dotnet','--version'],30).strip() == '10.0.401'
    commands.run('dotnet-info',['dotnet','--info'],30)
    artifacts = OUTPUT / 'artifacts'
    common = ['--artifacts-path',str(artifacts),'-r','linux-x64','-p:SelfContained=false',
              '-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nodeReuse:false']
    commands.run('restore',['dotnet','restore',PROJECT,'--configfile',str(REPO / 'NuGet.Config'),
        '--disable-parallel','-p:Configuration=Release'] + common,600)
    commands.run('build',['dotnet','build',PROJECT,'--no-restore','-c','Release'] + common,900)
    discovery = commands.run('discovery',['dotnet','test',PROJECT,'--no-build','--no-restore','-c','Release','--list-tests'] + common,60)
    actual_names = [line.strip() for line in discovery.splitlines() if line.strip().startswith('Haven.Infrastructure.Tests.MapsJourneyAuthoringCaptureTests.')]
    assert len(actual_names) == 6 and set(actual_names) == set(proposal['originalExpectedNativeNames'])
    write('discovery-identity.json',{'expected':proposal['originalExpectedNativeNames'],'actual':actual_names,'discovered':6,'equal':True})
    evaluated = module.parse_sdk(commands.run('target-properties',['dotnet','msbuild',PROJECT,'-p:Configuration=Release',
        '-p:RuntimeIdentifier=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),
        '-nodeReuse:false','-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier'],30))
    write('target-properties.json',evaluated)
    target = Path(evaluated['TargetPath'])
    assert target.is_file() and target.resolve().is_relative_to(artifacts)
    assert evaluated['TargetFramework'] == 'net10.0' and evaluated['RuntimeIdentifier'] == 'linux-x64'
    runtime_dir = target.parent
    runtime_before = [{'path':str(p.relative_to(runtime_dir)), 'bytes':p.stat().st_size, 'sha256':sha(p.read_bytes())}
        for p in sorted(runtime_dir.rglob('*')) if p.is_file()]
    write('runtime-before.json',runtime_before)
    tests = OUTPUT / 'test-results'
    tests.mkdir()
    started_ns = time.time_ns()
    commands.run('original-maps-six',['dotnet','test',PROJECT,'--no-build','--no-restore','-c','Release',
        '--logger','trx;LogFileName=maps-original-six.trx','--results-directory',str(tests)] + common,120)
    trx_path = tests / 'maps-original-six.trx'
    assert trx_path.stat().st_mtime_ns >= started_ns
    trx = ET.parse(trx_path).getroot()
    ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    rows = trx.findall('.//t:UnitTestResult',ns)
    counters = trx.find('.//t:Counters',ns).attrib
    assert len(rows) == 6 and set(r.attrib['testName'] for r in rows) == set(proposal['originalExpectedNativeNames'])
    assert all(r.attrib['outcome'] == 'Passed' for r in rows)
    assert all(int(counters[k]) == v for k,v in {'total':6,'executed':6,'passed':6,'failed':0,'notExecuted':0}.items())
    write('trx-identity.json',{'counters':counters, 'names':[r.attrib['testName'] for r in rows],
        'fresh':True,'allOriginalIdentities':True,'trxBytes':trx_path.stat().st_size,'trxSha256':sha(trx_path.read_bytes())})
    result.update(state='PASS_SIX_ORIGINAL_MANAGED_OWNER_CASES',discovered=6,executed=6,passed=6,failed=0,skipped=0)
except Exception as error:
    result.update(state='FAIL_OR_INCOMPLETE',error=repr(error))
finally:
    try:
        after = snapshot()
        write('source-after.json',after)
        assert before is None or after == before
        commands.run('git-clean-after',['git','diff','--exit-code','HEAD','--'],15)
        assert commands.run('git-head-after',['git','rev-parse','HEAD'],15).strip() == SOURCE
        if runtime_before is not None:
            runtime_after = [{'path':str(p.relative_to(runtime_dir)), 'bytes':p.stat().st_size, 'sha256':sha(p.read_bytes())}
                for p in sorted(runtime_dir.rglob('*')) if p.is_file()]
            write('runtime-after.json',runtime_after)
            assert runtime_before == runtime_after
    except Exception as error:
        result.update(state='FAIL_OR_INCOMPLETE',finalCustodyError=repr(error))
    closed = all(r['exit'] == r['exitAfterDrain'] == 0 and r['normalEOF'] and r['familyClosed'] and r['finalECHILD']
        and r['error'] is None and not r['signals'] and all(b['gone'] for b in r['births']) for r in commands.records)
    result['commandFamilies'] = len(commands.records)
    result['allCommandFamiliesNormalClosed'] = closed
    result['acceptedScopedOriginalTests'] = result['state'] == 'PASS_SIX_ORIGINAL_MANAGED_OWNER_CASES' and closed
    result['sourceInputs'] = None if before is None else len(before)
    result['nativeRuntimeMetadataFiles'] = None if runtime_before is None else len(runtime_before)
    write('result.json',result)
print(json.dumps(result,indent=2))
sys.exit(0 if result['acceptedScopedOriginalTests'] else 1)
