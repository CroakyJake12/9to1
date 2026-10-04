#!/usr/bin/env python3
"""Run intact actual owning projects from frozen1c78; preserve fullMail required-TLS failures."""
import gzip,hashlib,importlib.util,json,os,shutil,subprocess,sys,time
from pathlib import Path
import xml.etree.ElementTree as ET
sys.dont_write_bytecode=True
ROOT=Path('/workspace/team-c-c4-public-mail-nine-provisional');SOURCE='1c78a6107264baf5f7da92a5b6abbde7eac10e40'
OUTPUT=Path('/workspace/team-c-resume-evidence/c4-public-mail-pr8-review/ordinary-mail72-infra444-1c78')
MATERIAL=Path('/workspace/team-c-resume-evidence/c4-public-mail-pr8-review/capacity/actual-complete-five-seven-owning-source-materialization.json')
OLD_TRX=Path('/tmp/team-c-c5-full-infra-5ec-output-20261004/test-results/whole-normal-434.trx')
SDK='/workspace/.tools/dotnet/dotnet';NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
sha=lambda b:hashlib.sha256(b).hexdigest()
assert not OUTPUT.exists();assert shutil.disk_usage('/workspace').free>900000000
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip()==SOURCE
assert not subprocess.check_output(['git','status','--porcelain'],cwd=ROOT)
material=json.loads(MATERIAL.read_bytes());assert material['source']==SOURCE and material['trackedFiles']==793
helper=ROOT/'apps/Web/Tests/ci/run-ordinary-native.py';assert sha(helper.read_bytes())=='a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38'
spec=importlib.util.spec_from_file_location('maintained_mail_infra_commands',helper);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
OUTPUT.mkdir();common=OUTPUT/'source-custody';common.mkdir()
def write(p,value):p.write_text(json.dumps(value,indent=2)+'\n')
def snapshot():
 result=[]
 for row in material['files']:
  p=ROOT/row['path'];assert p.is_file() and not p.is_symlink();b=p.read_bytes()
  assert sha(b)==row['sha256'] and len(b)==row['bytes'],str(p)
  result.append(dict(path=row['path'],mode=row['mode'],blob=row['blob'],bytes=len(b),sha256=sha(b)))
 return result
before=snapshot();write(common/'source-before.json',before)
root_now=subprocess.check_output(['git','-C','/workspace/team-c-integration','rev-parse','HEAD']).decode().strip()
root_rows={}
for p in before:
 b=subprocess.check_output(['git','-C','/workspace/team-c-integration','show',root_now+':'+p['path']]);assert sha(b)==p['sha256'] and len(b)==p['bytes'];root_rows[p['path']]=p['sha256']
write(common/'receiving-root-body-compatibility.json',dict(actualRootAtInvocation=root_now,testedFrozenSource=SOURCE,whole793CurrentBodyHashesEqual=True,qualification='Frozen1c78 invocation remains attributed to1c78, not later Home/auth/root commit. Complete physical graph bodies equal receiving root at this recorded observation.'))
old_doc=ET.parse(OLD_TRX).getroot();old_rows=old_doc.findall('.//t:UnitTestResult',NS);old_names={r.attrib['testName'] for r in old_rows};assert len(old_rows)==len(old_names)==434 and all(r.attrib['outcome']=='Passed' for r in old_rows)
assert sha(OLD_TRX.read_bytes())=='52df1fab64bb5682037f5a0278076c6e2c256542fdf288ee8a57ac9d8266e794'
write(common/'historical434-identities.json',dict(path=str(OLD_TRX),sha256=sha(OLD_TRX.read_bytes()),names=sorted(old_names),qualification='Verified prior5ec434 retained as historical; new shared Graph provider requires current full444 execution.'))
T=('HAVEN_MAIL_FIXTURE_HOST','HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT','HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT','HAVEN_MAIL_FIXTURE_ADDRESS','HAVEN_MAIL_FIXTURE_PASSWORD')
assert not any(os.environ.get(name) for name in T),'Unexpected Mail fixture configuration needs existing authorized loopback custody before full execution'
write(common/'mail-required-tls-prerequisite-presence.json',dict(presenceOnly={n:False for n in T},expectedUnchangedCases=3,qualification='No values, fixture provisioning, host, credentials or certificate change. Canonical realTLS3 fail before network without requiredENV; fullMail72 remains failed.'))
results=[]
BUDGET=512*1024*1024;FLOOR=256*1024*1024
def capacity(label):
 total=sum(p.stat().st_blocks*512 for p in OUTPUT.rglob('*') if p.is_file());free=shutil.disk_usage('/workspace').free
 write(common/'capacity-'+label+'.json',dict(label=label,outputAllocatedBytes=total,outputLimitBytes=BUDGET,workspaceAvailable=free,requiredFreeFloor=FLOOR))
 assert total<BUDGET and free>FLOOR,(label,total,free)
def decode_sdk(text):
 off=text.find('{');assert off>=0;r,end=json.JSONDecoder().raw_decode(text[off:]);assert not text[off:][end:].strip();return r
for suite,project,expected,project_count in [
 ('mail72','9to1 Workspace/Mail/Tests/HavenOS.Mail.Tests.csproj',72,5),
 ('infrastructure444','9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj',444,7)]:
 out=OUTPUT/suite;out.mkdir();diag=out/'diagnostics';diag.mkdir()
 env=os.environ.copy()
 for var,name in {'DOTNET_CLI_HOME':'cli','NUGET_PACKAGES':'nuget','NUGET_HTTP_CACHE_PATH':'http','NUGET_PLUGINS_CACHE_PATH':'plugins','XDG_CACHE_HOME':'cache','XDG_CONFIG_HOME':'config','TMPDIR':'tmp','TMP':'tmp','TEMP':'tmp','HAVEN_DATA_DIR':'fixture-data'}.items():
  p=out/name;p.mkdir();env[var]=str(p)
 env.update(PATH='/workspace/.tools/dotnet:'+env.get('PATH',''),DOTNET_ROOT='/workspace/.tools/dotnet',NUGET_FALLBACK_PACKAGES='/workspace/.tools/nuget/packages',CI='true',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1')
 commands=module.Commands(diag,env,ROOT);artifacts=out/'artifacts'
 flags=['--artifacts-path',str(artifacts),'-r','linux-x64','--disable-build-servers','-m:1','-nodeReuse:false','-p:SelfContained=false','-p:UseSharedCompilation=false','-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false','-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false','-p:CreateHardLinksForCopyLocalIfPossible=false','-p:CreateHardLinksForPublishFilesIfPossible=false']
 result={'state':'NOT_RUN','sourceCommit':SOURCE,'actualReceivingRootBodyObservation':root_now,'project':project,'expected':expected,'ordinaryProjects':project_count,'configuration':'Release','runtimeIdentifier':'linux-x64','testFilter':None,'originalSourceAssertionsPoliciesProjectsUnchanged':True,'scope':'Actual unchanged normal source-built owning project; provisional Linux component scope, no graphical package/Windows/Home/provider/fullrelease acceptance.'}
 projects=set()
 def graph(p):
  p=p.resolve();assert p.is_file() and p.is_relative_to(ROOT)
  if p in projects:return
  projects.add(p)
  for row in ET.parse(p).iter('ProjectReference'):graph(p.parent/row.attrib['Include'].replace('\\','/'))
 graph(ROOT/project);assert len(projects)==project_count
 write(diag/'actual-ordinary-project-graph.json',[p.relative_to(ROOT).as_posix() for p in sorted(projects)])
 target=None;runtime_before=None;deferred=None
 try:
  capacity(suite+'-before-restore');assert commands.run('git-head',['git','rev-parse','HEAD'],15).strip()==SOURCE
  commands.run('git-clean-before',['git','diff','--exit-code','HEAD','--'],15)
  assert commands.run('dotnet-version',[SDK,'--version'],30).strip()=='10.0.401'
  commands.run('restore',[SDK,'restore',project,'--configfile',str(ROOT/'NuGet.Config'),'--disable-parallel','-p:Configuration=Release']+flags,600);capacity(suite+'-after-restore')
  folders={}
  for p in artifacts.rglob('project.assets.json'):
   rows=json.loads(p.read_bytes())['packageFolders'];assert '/workspace/.tools/nuget/packages/' in rows or '/workspace/.tools/nuget/packages' in rows
   folders[str(p.relative_to(out))]=list(rows)
  write(diag/'actual-package-folders.json',folders)
  commands.run('build',[SDK,'build',project,'--no-restore','-c','Release','-f','net10.0']+flags,600);capacity(suite+'-after-build')
  compile_pins=[]
  for index,p in enumerate(sorted(projects)):
   eval_data=decode_sdk(commands.run('evaluated-project-'+str(index),[SDK,'msbuild',str(p),'-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(artifacts),'-p:SelfContained=false','-p:UseSharedCompilation=false','-nodeReuse:false','-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,IsTestProject','-getItem:Compile,EmbeddedResource,Content,AdditionalFiles'],30))
   write(diag/('evaluated-project-'+str(index)+'.json'),eval_data)
   for kind,rows in eval_data.get('Items',{}).items():
    for row in rows:
     actual=Path(row['FullPath']);assert actual.is_file() and not actual.is_symlink();b=actual.read_bytes()
     assert actual.resolve().is_relative_to(ROOT) or actual.resolve().is_relative_to(artifacts),str(actual)
     compile_pins.append(dict(project=p.relative_to(ROOT).as_posix(),kind=kind,path=str(actual),bytes=len(b),sha256=sha(b),generatedOwned=actual.resolve().is_relative_to(artifacts)))
   if p==ROOT/project: evaluated=eval_data['Properties']
  write(diag/'actual-evaluated-compile-content-inputs.json',compile_pins)
  target=Path(evaluated['TargetPath']);assert target.is_file() and target.resolve().is_relative_to(artifacts) and evaluated['TargetFramework']=='net10.0'
  runtime_before=[dict(path=str(p.relative_to(target.parent)),bytes=p.stat().st_size,sha256=sha(p.read_bytes())) for p in sorted(target.parent.rglob('*')) if p.is_file()];write(diag/'runtime-before.json',runtime_before)
  discovery=commands.run('whole-discovery',[SDK,'test',project,'--no-build','--no-restore','-c','Release','-f','net10.0','--list-tests']+flags,120)
  prefix='HavenOS.Mail.Tests.' if suite=='mail72' else 'Haven.Infrastructure.Tests.'
  names=[l.strip() for l in discovery.splitlines() if l.strip().startswith(prefix)];assert len(names)==len(set(names))==expected,(suite,len(names))
  write(diag/'whole-discovery.json',dict(discovered=len(names),expected=expected,names=names,testFilter=None))
  trxdir=out/'test-results';trxdir.mkdir();trx=trxdir/(suite+'.trx');start=time.time_ns()
  try:commands.run('whole-unfiltered-owning-tests',[SDK,'test',project,'--no-build','--no-restore','-c','Release','-f','net10.0','--logger','trx;LogFileName='+suite+'.trx','--results-directory',str(trxdir)]+flags,600)
  except Exception as error:deferred=repr(error)
  assert trx.is_file() and trx.stat().st_mtime_ns>=start
  doc=ET.parse(trx).getroot();rows=doc.findall('.//t:UnitTestResult',NS);counts={k:int(v) for k,v in doc.find('.//t:Counters',NS).attrib.items()};actual_names={r.attrib['testName'] for r in rows}
  assert len(rows)==len(actual_names)==counts['total']==counts['executed']==expected and counts['notExecuted']==0
  failures=[]
  for row in rows:
   if row.attrib['outcome']!='Passed':
    m=row.find('.//t:Message',NS);s=row.find('.//t:StackTrace',NS);failures.append(dict(name=row.attrib['testName'],outcome=row.attrib['outcome'],message='' if m is None else m.text or '',stack='' if s is None else s.text or ''))
  write(diag/'whole-trx-readback.json',dict(counters=counts,names=sorted(actual_names),failures=failures,fresh=True,sha256=sha(trx.read_bytes()),testFilter=None))
  result.update(actualCounters=counts,failures=failures,testCommandException=deferred,trxSha256=sha(trx.read_bytes()),runtimeFiles=len(runtime_before),evaluatedInputRows=len(compile_pins),discovered=len(names))
  if suite=='mail72':
   group_counts={cls:sum('.'+cls+'.' in name for name in actual_names) for cls in ['MailFoundationTests','MailKitInboxReconciliationTests','MailKitTransportTests','MailDomainRegressionTests','MailStoreRegressionTests']}
   assert group_counts=={'MailFoundationTests':12,'MailKitInboxReconciliationTests':2,'MailKitTransportTests':2,'MailDomainRegressionTests':23,'MailStoreRegressionTests':33},group_counts
   result['exactOriginal16PlusNew56']=group_counts
   assert counts['passed']==69 and counts['failed']==3 and len(failures)==3
   assert all(f['outcome']=='Failed' and 'HAVEN_MAIL_FIXTURE_HOST' in f['message'] and ('.MailKitInboxReconciliationTests.' in f['name'] or '.MailKitTransportTests.' in f['name']) for f in failures)
   assert commands.records[-1]['exit']==commands.records[-1]['exitAfterDrain']==1
   result.update(state='FULL_MAIL72_EXECUTED_69PASS_3_REAL_TLS_PREREQUISITE_FAIL_FULL_SUITE_FAILED',fullSuitePassed=False,expectedMissingTLSFixtureFailuresRetained=True)
  else:
   added=actual_names-old_names;assert old_names<=actual_names and len(added)==10 and all('MicrosoftMailContinuationBoundaryTests.' in name for name in added)
   assert counts['passed']==444 and counts['failed']==0 and deferred is None
   result.update(state='PASS_FULL_ORDINARY_INFRASTRUCTURE444_SCOPED',original434Conserved=True,Graph10ActualCaseNames=sorted(added),Graph10Passed=True,fullSuitePassed=True)
 except Exception as error:result.update(state='FAIL_OR_INCOMPLETE',error=repr(error))
 finally:
  try:
   assert snapshot()==before
   if target is not None and runtime_before is not None:
    after_runtime=[dict(path=str(p.relative_to(target.parent)),bytes=p.stat().st_size,sha256=sha(p.read_bytes())) for p in sorted(target.parent.rglob('*')) if p.is_file()];write(diag/'runtime-after.json',after_runtime);assert after_runtime==runtime_before
   commands.run('git-clean-after',['git','diff','--exit-code','HEAD','--'],15)
   assert commands.run('git-head-after',['git','rev-parse','HEAD'],15).strip()==SOURCE
   capacity(suite+'-after-tests')
  except Exception as error:result.update(state='FAIL_OR_INCOMPLETE',finalCustodyError=repr(error))
  result['commandFamilies']=len(commands.records)
  result['allCommandFamiliesNormalClosedNoSignals']=all(c['normalEOF'] and c['familyClosed'] and c['finalECHILD'] and not c['signals'] and c['error'] is None and all(b['gone'] for b in c['births']) for c in commands.records)
  result['actualNonzeroExitCommands']=[dict(name=c['name'],exit=c['exit'],exitAfterDrain=c['exitAfterDrain']) for c in commands.records if c['exit']!=0]
  if not result['allCommandFamiliesNormalClosedNoSignals']:result['state']='FAIL_OR_INCOMPLETE'
  write(diag/'result.json',result);results.append(result)
 print(json.dumps(result,indent=2),flush=True)
 if result['state']=='FAIL_OR_INCOMPLETE' and (shutil.disk_usage('/workspace').free<=FLOOR):break
write(common/'source-after.json',snapshot());assert snapshot()==before
final={'sourceCommit':SOURCE,'actualRootBodyObservation':root_now,'actualSource793BeforeAfterIdentical':True,'suites':results,'noSourceProjectOracleAssertionsOrProviderChanges':True,'fullMailSuitePassed':False,'scope':'Actual own frozen1c78 normal Linux component graphs. Historical5ec434 remains separate; rootHome/auth/newWindows changes outside these physical793 source inputs are not tested.'}
final['completedRequiredScopedExecutions']=len(results)==2 and results[0]['state']=='FULL_MAIL72_EXECUTED_69PASS_3_REAL_TLS_PREREQUISITE_FAIL_FULL_SUITE_FAILED' and results[1]['state']=='PASS_FULL_ORDINARY_INFRASTRUCTURE444_SCOPED'
write(OUTPUT/'result.json',final);print(json.dumps(final,indent=2));sys.exit(0 if final['completedRequiredScopedExecutions'] else 1)
