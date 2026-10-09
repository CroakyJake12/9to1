#!/usr/bin/env python3
"""Single final ordinary full maintained Infrastructure suite at frozen source."""
import hashlib,importlib.util,json,os,shutil,subprocess,sys,time
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE='4544d1157afaeb3a50f2042ee6dcc0be78b2d945'
REPO=Path('/tmp/team-c-c5-plugin-temp-provisional-20261004')
OUTPUT=Path('/tmp/team-c-c5-plugin-temp-provisional-output-20261004')
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
result={'state':'NOT_RUN','sourceCommit':SOURCE,'expectedPerConfiguration':4,'ordinaryProjects':7,
    'scope':'Actual original whole ExtensionPluginEndToEndTests class with private TMPDIR; portable temp process-boundary correction only, not Windows/full release/provider acceptance',
    'testFilter':'FullyQualifiedName~Haven.Infrastructure.Tests.ExtensionPluginEndToEndTests','originalTestsUnchanged':True,'configurations':[]}
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
    for configuration in ['Debug','Release']:
        commands.run(configuration+'-restore',['dotnet','restore',PROJECT,'--configfile',str(REPO/'NuGet.Config'),
            '--disable-parallel','-p:Configuration='+configuration]+flags,600)
        commands.run(configuration+'-build',['dotnet','build',PROJECT,'--no-restore','-c',configuration]+flags,1200)
        discovery=commands.run(configuration+'-discovery',['dotnet','test',PROJECT,'--no-build','--no-restore','-c',configuration,
            '--list-tests','--filter',result['testFilter']]+flags,120)
        discovered=[line.strip() for line in discovery.splitlines() if line.strip().startswith('Haven.Infrastructure.Tests.ExtensionPluginEndToEndTests.')]
        assert len(discovered)==4;write(configuration+'-discovery.json',{'discovered':len(discovered),'names':discovered})
        evaluated=module.parse_sdk(commands.run(configuration+'-target-properties',['dotnet','msbuild',PROJECT,'-p:Configuration='+configuration,
            '-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-nodeReuse:false','-getProperty:TargetPath,TargetDir,TargetFramework'],30))
        write(configuration+'-target-properties.json',evaluated);target=Path(evaluated['TargetPath'])
        assert target.is_file() and target.resolve().is_relative_to(artifacts) and evaluated['TargetFramework']=='net10.0'
        runtime_before=[{'path':str(p.relative_to(target.parent)),'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
            for p in sorted(target.parent.rglob('*')) if p.is_file()]
        write(configuration+'-runtime-before.json',runtime_before)
        tests=OUTPUT/'test-results'/configuration;tests.mkdir(parents=True);trx_path=tests/'plugin-original-four.trx';started_ns=time.time_ns()
        deferred=None
        try:commands.run(configuration+'-original-plugin-four-test',['dotnet','test',PROJECT,'--no-build','--no-restore','-c',configuration,
            '--filter',result['testFilter'],'--logger','trx;LogFileName=plugin-original-four.trx','--results-directory',str(tests)]+flags,600)
        except Exception as error:deferred=error
        assert trx_path.is_file() and trx_path.stat().st_mtime_ns>=started_ns
        doc=ET.parse(trx_path).getroot();rows=doc.findall('.//t:UnitTestResult',ns)
        counters={k:int(v) for k,v in doc.find('.//t:Counters',ns).attrib.items()}
        names={r.attrib['testName'] for r in rows}
        failures=[{'name':r.attrib['testName'],'outcome':r.attrib['outcome'],
            'message':(r.find('.//t:Message',ns).text or '') if r.find('.//t:Message',ns) is not None else None}
            for r in rows if r.attrib['outcome']!='Passed']
        write(configuration+'-trx-readback.json',{'counters':counters,'names':sorted(names),'failures':failures,
            'bytes':trx_path.stat().st_size,'sha256':sha(trx_path.read_bytes()),'fresh':True})
        after_runtime=[{'path':str(p.relative_to(target.parent)),'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
            for p in sorted(target.parent.rglob('*')) if p.is_file()]
        write(configuration+'-runtime-after.json',after_runtime);assert after_runtime==runtime_before
        result['configurations'].append({'configuration':configuration,'counters':counters,'runtimeFiles':len(runtime_before),'failures':failures})
        if deferred is not None:raise deferred
        assert counters['total']==counters['executed']==counters['passed']==4 and counters['failed']==counters['notExecuted']==0
        assert len(rows)==len(names)==4 and names==set(discovered)
    result['state']='PASS_ORIGINAL_PLUGIN_FOUR_DEBUG_RELEASE_PRIVATE_TMPDIR'

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
    result['acceptedScopedFullSuite']=result['state']=='PASS_ORIGINAL_PLUGIN_FOUR_DEBUG_RELEASE_PRIVATE_TMPDIR' and result['allCommandFamiliesNormalClosed']
    write('result.json',result)
print(json.dumps(result,indent=2));sys.exit(0 if result['acceptedScopedFullSuite'] else 1)
