#!/usr/bin/env python3
"""Single final ordinary full maintained Infrastructure suite at frozen source."""
import hashlib,importlib.util,json,os,shutil,subprocess,sys,time
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE='27dd12f72cbf42d992a18765a933584b346d05d7'
REPO=Path('/tmp/team-c-c5-full-infra-27dd-20261004')
OUTPUT=Path('/tmp/team-c-c5-full-infra-27dd-output-20261004')
PROJECT='9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj'
OLD_TRX=Path('/workspace/team-c-resume-evidence/c5-mesh-filename-provisional/normal-Release/results/normal-infrastructure.trx')
sha=lambda body:hashlib.sha256(body).hexdigest()
assert not OUTPUT.exists() and shutil.disk_usage('/tmp').free>1000000000
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=REPO).decode().strip()==SOURCE
assert not subprocess.check_output(['git','status','--porcelain'],cwd=REPO)
projects=set()
def graph(project):
    project=project.resolve();assert project.is_file() and project.is_relative_to(REPO)
    if project in projects:return
    projects.add(project)
    for row in ET.parse(project).iter('ProjectReference'):
        graph(project.parent/row.attrib['Include'].replace('\\','/'))
graph(REPO/PROJECT);assert len(projects)==7
def snapshot():
    paths=set(projects)
    for project in projects:
        for p in project.parent.rglob('*'):
            if p.is_file() and p.suffix in ('.cs','.csproj','.cui') and not any(part in ('bin','obj') or part.startswith(('bin-','obj-')) for part in p.relative_to(project.parent).parts):
                paths.add(p)
        for parent in [project.parent,*project.parent.parents]:
            if not parent.is_relative_to(REPO):break
            for name in ('Directory.Build.props','Directory.Build.targets','Directory.Packages.props'):
                p=parent/name
                if p.is_file():paths.add(p)
    paths.update([REPO/'global.json',REPO/'NuGet.Config',REPO/'apps/Web/Tests/ci/run-ordinary-native.py'])
    rows=[]
    for path in sorted(paths):
        assert not path.is_symlink();body=path.read_bytes()
        rows.append({'path':path.relative_to(REPO).as_posix(),'bytes':len(body),'sha256':sha(body)})
    return rows
sys.dont_write_bytecode=True
helper=REPO/'apps/Web/Tests/ci/run-ordinary-native.py'
assert sha(helper.read_bytes())=='a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'
spec=importlib.util.spec_from_file_location('maintained_final_infra_commands',helper)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
OUTPUT.mkdir();diag=OUTPUT/'diagnostics';diag.mkdir()
env=os.environ.copy()
for var,name in {'DOTNET_CLI_HOME':'cli','NUGET_PACKAGES':'nuget','NUGET_HTTP_CACHE_PATH':'http',
    'NUGET_PLUGINS_CACHE_PATH':'plugins','XDG_CACHE_HOME':'cache','XDG_CONFIG_HOME':'config',
    'TMPDIR':'tmp','TMP':'tmp','TEMP':'tmp','HAVEN_DATA_DIR':'fixture-data'}.items():
    path=OUTPUT/name;path.mkdir(exist_ok=True);env[var]=str(path)
env.update(CI='true',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_TELEMETRY_OPTOUT='1',
    DOTNET_NOLOGO='1',MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1',
    NUGET_FALLBACK_PACKAGES='/workspace/.tools/nuget/packages')
commands=module.Commands(diag,env,REPO)
def write(name,value):(diag/name).write_text(json.dumps(value,indent=2)+'\n')
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
old_doc=ET.parse(OLD_TRX).getroot();old_tests=old_doc.findall('.//t:UnitTestResult',ns)
assert len(old_tests)==428
old_names={r.attrib['testName'] for r in old_tests};assert len(old_names)==428
write('historical428-identity.json',{'path':str(OLD_TRX),'bytes':OLD_TRX.stat().st_size,'sha256':sha(OLD_TRX.read_bytes()),
    'count':428,'names':sorted(old_names),'scope':'Historical whole428 receipt preserved; original3 failures remain historical evidence.'})
result={'state':'NOT_RUN','sourceCommit':SOURCE,'expected':434,'ordinaryProjects':7,'configuration':'Release',
    'scope':'Full maintained Haven.Infrastructure.Tests on actual frozen provisional source; Linux only, not whole release/Windows/provider/full app acceptance',
    'testFilter':None,'assertionOrPolicyOrAnalyzerEdits':False,'sourceGraphExclusions':False,
    'historical428ReceiptPreserved':True}
before=None;runtime_before=None;target=None;trx_path=None;started_ns=None
try:
    assert commands.run('git-head',['git','rev-parse','HEAD'],15).strip()==SOURCE
    commands.run('git-clean-before',['git','diff','--exit-code','HEAD','--'],15)
    commands.run('source-tree',['git','ls-tree','-r','HEAD'],15)
    assert commands.run('dotnet-version',['dotnet','--version'],30).strip()=='10.0.401'
    commands.run('dotnet-info',['dotnet','--info'],30)
    before=snapshot();write('source-before.json',before)
    write('ordinary-project-graph.json',[p.relative_to(REPO).as_posix() for p in sorted(projects)])
    artifacts=OUTPUT/'artifacts'
    flags=['--artifacts-path',str(artifacts),'-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nodeReuse:false']
    commands.run('restore',['dotnet','restore',PROJECT,'--configfile',str(REPO/'NuGet.Config'),
        '--disable-parallel','-p:Configuration=Release']+flags,600)
    commands.run('build',['dotnet','build',PROJECT,'--no-restore','-c','Release']+flags,1200)
    discovery=commands.run('discovery',['dotnet','test',PROJECT,'--no-build','--no-restore','-c','Release','--list-tests']+flags,120)
    discovered=[line.strip() for line in discovery.splitlines() if line.strip().startswith('Haven.Infrastructure.Tests.')]
    write('discovery.json',{'discovered':len(discovered),'expected':434,'names':discovered,
        'original428Missing':sorted(old_names-set(discovered)),'added':sorted(set(discovered)-old_names)})
    result['actualDiscovered']=len(discovered)
    evaluated=module.parse_sdk(commands.run('target-properties',['dotnet','msbuild',PROJECT,'-p:Configuration=Release',
        '-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false','-getProperty:TargetPath,TargetDir,TargetFramework'],30))
    write('target-properties.json',evaluated);target=Path(evaluated['TargetPath'])
    assert target.is_file() and target.resolve().is_relative_to(artifacts) and evaluated['TargetFramework']=='net10.0'
    runtime_before=[{'path':str(p.relative_to(target.parent)),'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
        for p in sorted(target.parent.rglob('*')) if p.is_file()]
    write('runtime-before.json',runtime_before)
    tests=OUTPUT/'test-results';tests.mkdir();trx_path=tests/'whole-normal-434.trx';started_ns=time.time_ns()
    deferred=None
    try:commands.run('whole-ordinary-infrastructure-test',['dotnet','test',PROJECT,'--no-build','--no-restore','-c','Release',
        '--logger','trx;LogFileName=whole-normal-434.trx','--results-directory',str(tests)]+flags,1200)
    except Exception as error:deferred=error
    assert trx_path.is_file() and trx_path.stat().st_mtime_ns>=started_ns
    doc=ET.parse(trx_path).getroot();rows=doc.findall('.//t:UnitTestResult',ns)
    counters={k:int(v) for k,v in doc.find('.//t:Counters',ns).attrib.items()}
    names={r.attrib['testName'] for r in rows};failures=[]
    for row in rows:
        if row.attrib['outcome']!='Passed':
            message=row.find('.//t:Message',ns);stack=row.find('.//t:StackTrace',ns)
            failures.append({'name':row.attrib['testName'],'outcome':row.attrib['outcome'],
                'message':None if message is None else (message.text or '')[:32768],
                'stack':None if stack is None else (stack.text or '')[:32768]})
    write('trx-readback.json',{'counters':counters,'names':sorted(names),'original428Missing':sorted(old_names-names),
        'newNames':sorted(names-old_names),'failures':failures,'bytes':trx_path.stat().st_size,'sha256':sha(trx_path.read_bytes()),'fresh':True})
    result.update(actualCounters=counters,failures=failures,original428Conserved=old_names<=names,
        newTests=len(names-old_names),runtimeFiles=len(runtime_before))
    if deferred is not None:raise deferred
    assert counters['total']==counters['executed']==counters['passed']==434 and counters['failed']==counters['notExecuted']==0
    assert len(rows)==len(names)==434 and old_names<=names and len(names-old_names)==6
    assert all('MapsJourneyAuthoringCaptureTests.' in name for name in names-old_names)
    result['state']='PASS_FULL_ORDINARY_INFRASTRUCTURE_434_SCOPED'
except Exception as error:result.update(state='FAIL_OR_INCOMPLETE',error=repr(error))
finally:
    try:
        after=snapshot();write('source-after.json',after);assert before is None or before==after
        if runtime_before is not None:
            after_runtime=[{'path':str(p.relative_to(target.parent)),'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
                for p in sorted(target.parent.rglob('*')) if p.is_file()]
            write('runtime-after.json',after_runtime);assert after_runtime==runtime_before
        commands.run('git-clean-after',['git','diff','--exit-code','HEAD','--'],15)
        assert commands.run('git-head-after',['git','rev-parse','HEAD'],15).strip()==SOURCE
    except Exception as error:result.update(state='FAIL_OR_INCOMPLETE',finalCustodyError=repr(error))
    result['sourceInputs']=None if before is None else len(before)
    result['commandFamilies']=len(commands.records)
    result['allCommandFamiliesNormalClosed']=all(c['exit']==c['exitAfterDrain']==0 and c['normalEOF'] and c['familyClosed']
        and c['finalECHILD'] and not c['signals'] and c['error'] is None and all(b['gone'] for b in c['births']) for c in commands.records)
    result['acceptedScopedFullSuite']=result['state']=='PASS_FULL_ORDINARY_INFRASTRUCTURE_434_SCOPED' and result['allCommandFamiliesNormalClosed']
    write('result.json',result)
print(json.dumps(result,indent=2));sys.exit(0 if result['acceptedScopedFullSuite'] else 1)
