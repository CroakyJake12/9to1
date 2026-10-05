import hashlib,json,subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
B=Path('/workspace/team-c-resume-evidence/c4-public-mail-pr8-review/ordinary-mail72-infra444-1c78-attempt03');R=Path('/workspace/team-c-c4-public-mail-nine-provisional');SOURCE='1c78a6107264baf5f7da92a5b6abbde7eac10e40';NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
sha=lambda b:hashlib.sha256(b).hexdigest()
def load(p):return json.loads(p.read_bytes())
def pin(p):return dict(path=str(p),bytes=p.stat().st_size,sha256=sha(p.read_bytes()))
source_before=load(B/'source-custody/source-before.json');source_after=load(B/'source-custody/source-after.json');assert source_before==source_after and len(source_before)==793
assert subprocess.check_output(['git','-C',str(R),'rev-parse','HEAD']).decode().strip()==SOURCE
assert not subprocess.check_output(['git','-C',str(R),'status','--porcelain'])
for row in source_before:assert sha((R/row['path']).read_bytes())==row['sha256']
observations=[]
for suite,total in [('mail72',72),('infrastructure444',444)]:
 p=B/suite;diag=p/'diagnostics';trx=p/'test-results'/(suite+'.trx');doc=ET.parse(trx).getroot();rows=doc.findall('.//t:UnitTestResult',NS);names={r.attrib['testName'] for r in rows};counts={k:int(v) for k,v in doc.find('.//t:Counters',NS).attrib.items()}
 assert len(rows)==len(names)==counts['total']==counts['executed']==total and counts['notExecuted']==0
 commands=load(diag/'commands.json');assert all(c['normalEOF'] and c['familyClosed'] and c['finalECHILD'] and not c['signals'] and c['error'] is None and all(b['gone'] for b in c['births']) for c in commands)
 for command in commands:
  if command['name']=='whole-unfiltered-owning-tests':assert '--filter' not in command['argv'] and command['exit']==command['exitAfterDrain']==(1 if suite=='mail72' else 0)
  else:assert command['exit']==command['exitAfterDrain']==0
 before=load(diag/'runtime-before.json');after=load(diag/'runtime-after.json');assert before==after
 evaluated=load(diag/'actual-evaluated-compile-content-inputs.json')
 for row in evaluated:
  file=Path(row['path']);assert file.is_file() and sha(file.read_bytes())==row['sha256'] and file.stat().st_size==row['bytes']
 for row in before:
  target_dirs=[]
  for ep in sorted(diag.glob('evaluated-project-*.json')):
   props=load(ep)['Properties']
   if props.get('IsTestProject')=='true':target_dirs.append(Path(props['TargetDir']))
  assert len(target_dirs)==1
  file=target_dirs[0]/row['path'];assert file.is_file() and sha(file.read_bytes())==row['sha256'] and file.stat().st_size==row['bytes']
 build=(diag/'build.log').read_text();assert '0 Warning(s)' in build and '0 Error(s)' in build
 discovery=load(diag/'whole-discovery.json');assert discovery['discovered']==total and discovery['testFilter'] is None
 item=dict(suite=suite,counters=counts,discovered=total,projectCount=5 if suite=='mail72' else 7,configuration='Release',runtime='linux-x64',unfiltered=True,trx=pin(trx),commandFamilies=len(commands),allNormalEOF_ECHILD_OriginalBirthsGone_NoSignals=True,runtimeFiles=len(before),allRuntimeCurrentAndBeforeAfterSHAEqual=True,evaluatedInputRows=len(evaluated),allEvaluatedPhysicalCompileContentBodiesCurrentSHAEqual=True,buildWarningsErrors=0)
 if suite=='mail72':
  group_counts={c:sum('.'+c+'.' in n for n in names) for c in ['MailFoundationTests','MailKitInboxReconciliationTests','MailKitSubmissionValidationTests','MailKitRealTransportTests','MailDomainRegressionTests','MailStoreRegressionTests']}
  assert group_counts=={'MailFoundationTests':12,'MailKitInboxReconciliationTests':2,'MailKitSubmissionValidationTests':1,'MailKitRealTransportTests':1,'MailDomainRegressionTests':23,'MailStoreRegressionTests':33}
  failed=[r for r in rows if r.attrib['outcome']!='Passed'];assert counts['passed']==69 and counts['failed']==len(failed)==3
  failures=[]
  for row in failed:
   message=row.find('.//t:Message',NS);text=message.text or '';assert row.attrib['outcome']=='Failed' and 'HAVEN_MAIL_FIXTURE_HOST' in text and 'Actual_TLS_' in row.attrib['testName']
   assert '.MailKitInboxReconciliationTests.' in row.attrib['testName'] or '.MailKitRealTransportTests.' in row.attrib['testName']
   failures.append(dict(name=row.attrib['testName'],message=text))
  raw=load(diag/'result.json');assert raw['state']=='FAIL_OR_INCOMPLETE' and raw['error'].startswith('AssertionError(') and 'MailKitTransportTests' in raw['error'] and 'finalCustodyError' not in raw
  item.update(status='FULL_MAIL72_FAILED_69PASS_3_MISSING_REAL_TLS_PREREQUISITES_NO_SKIP',fullSuitePassed=False,original16AndNew56CaseClassDenominators=group_counts,exactThreeOriginalFailures=failures,rawOrchestrationReadback=pin(diag/'result.json'),readbackCorrection='Original preserved full suite result remains69PASS3FAIL. The C4 metadata reader used nonexistent class MailKitTransportTests inferred from filename; canonical file declares SubmissionValidation andRealTransport one case each. This separate immutable post-readback corrects only that classification, with no test/build rerun or source/oracle/expectation edits.')
 else:
  old=load(B/'source-custody/historical434-identities.json');old_names=set(old['names']);added=names-old_names;assert len(old_names)==434 and old_names<=names and len(added)==10 and all('MicrosoftMailContinuationBoundaryTests.' in n for n in added)
  assert counts['passed']==444 and counts['failed']==0 and all(r.attrib['outcome']=='Passed' for r in rows)
  item.update(status='PASS_FULL_ORDINARY_INFRASTRUCTURE444_SCOPED',fullSuitePassed=True,allOriginal434ActualCaseIdentitiesConserved=True,Graph10Names=sorted(added),allGraph10Passed=True)
 observations.append(item)
r={'status':'OBSERVED_FULL_NORMAL_MAIL72_69PASS3TLS_FAIL_AND_INFRA444PASS_C6_RUNTIME_PEER_PENDING','sourceCommit':SOURCE,'actualReceivingRootBodyObservation':load(B/'source-custody/receiving-root-body-compatibility.json'),'sourceTrackedPhysicalBodies793BeforeAfterAndCurrentExact':True,'sdk':'10.0.401','sourceGuardCustodianSHA256':'a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38','observations':observations,'originalOrchestrationExit':1,'originalOrchestrationResult':pin(B/'result.json'),'fullMailPassed':False,'noPayloadRerun':True,'noProviderFixtureCredentialTLSCertificateTestProjectAssertionPolicySourceGuardEdits':True,'qualification':'Exact frozen1c78 Linux source-built normal owning graphs only. RootHome/auth changes outside these793 inputs are not tested. WholeMail remainsfailed on original3 realTLS prerequisites. Shared Graph behavior has fresh full444 acceptance, prior5ec434 preserved as historical. No GUI/package/provider/Windows/installedHome/cleanPC/signing/donor/fullrelease acceptance.'}
p=B/'actual-independent-readback.json';b=(json.dumps(r,indent=2)+'\n').encode();p.write_bytes(b);print(json.dumps(dict(path=str(p),bytes=len(b),sha256=sha(b),source=SOURCE,mail='72executed69pass3fail0skip',infrastructure='444executed444pass0fail0skip',commandFamilies=sum(x['commandFamilies'] for x in observations)),indent=2))
