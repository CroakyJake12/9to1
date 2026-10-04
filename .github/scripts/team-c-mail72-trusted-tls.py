"""Owned synthetic maintained SMTP/IMAP witness; ordinary TLS trust, no Send grant.
Team C executes this only inside its reviewed disposable GitHub-hosted Ubuntu caller.
"""
import argparse,base64,hashlib,http.client,re,importlib.util,json,os,pathlib,secrets,shutil,socket,ssl,subprocess,sys,tempfile,time,urllib.request
P=pathlib.Path
import atexit
from team_c_mail72_source_guard import require_disposable_runner, owned_output, verify_current_cut, collect_public
require_disposable_runner()
sys.path.insert(0,str((P.cwd()/'.github/scripts').resolve()))
from astra_mail_original_caller_session import MailCallerSession
p=argparse.ArgumentParser();p.add_argument('caller',nargs=argparse.REMAINDER);a=p.parse_args();assert a.caller and a.caller[0]=='--';argv=a.caller[1:];assert argv
root=P.cwd();output=owned_output(root);assert not output.exists(), 'Fresh owned runner output required';out=output/'diagnostics';out.mkdir(parents=True,exist_ok=False)
# Authenticate the complete exact source cut BEFORE download, trust mutation or runtime spawn.
for flag in ('--expected-commit','--manifest','--manifest-sha'):assert argv.count(flag)==1
expectedCommit=argv[argv.index('--expected-commit')+1];manifestPath=argv[argv.index('--manifest')+1];manifestSHA=argv[argv.index('--manifest-sha')+1]
assert manifestPath=='.github/validation/team-c-mail72-trusted-greenmail.json'
def verify_original_cut():
 return verify_current_cut(root,manifestPath,manifestSHA,expectedCommit)
verify_original_cut()

spec=importlib.util.spec_from_file_location('original_session',root/'.github/scripts/astra_original_native_session_drain.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
url='https://repo.maven.apache.org/maven2/com/icegreen/greenmail-standalone/2.1.14/greenmail-standalone-2.1.14.jar'
expected='0381392f3a44e4d8ae78051778440f58820d01acaf5757c3f43649deda8c1d23'
work=P(tempfile.mkdtemp(prefix='astra-mail-private-'));os.chmod(work,0o700)
trust=P('/usr/local/share/ca-certificates')/('astra-mail-'+secrets.token_hex(12)+'.crt')
process=session=serverLog=caller=callerSession=None;trusted=False;primary=None;drained=False;callerDrained=False;javaAttempted=False;callerAttempted=False
crlProcess=crlSession=None;crlAttempted=False;crlDrained=False
javaRecords=out/'mail-original-java-session';callerRecords=out/'mail-original-caller-session'
crlRecords=out/'mail-original-crl-session'
for records in (javaRecords,callerRecords,crlRecords):
 records.mkdir();(records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False,'status':'before original spawn attempt'})+'\n')
password=secrets.token_urlsafe(32);storePassword=secrets.token_urlsafe(32)
address='fixture@localhost';host='localhost'
def retain_declared_public_diagnostics():
 try:
  resultPath=out/'normal-mail72-result.json'
  result=json.loads(resultPath.read_text()) if resultPath.is_file() else {'status':'NOT_RUN'}
  summary={'sourceCommit':expectedCommit,'runId':os.environ.get('GITHUB_RUN_ID'),'runAttempt':os.environ.get('GITHUB_RUN_ATTEMPT'),'normalMailStatus':result['status'],'javaAttempted':javaAttempted,'callerAttempted':callerAttempted,'crlAttempted':crlAttempted,'javaFamilyDrained':drained,'callerFamilyDrained':callerDrained,'crlFamilyDrained':crlDrained,'introducedRunnerTrustRemoved':not trusted,'primaryFailureType':None if primary is None else type(primary).__name__,'cleanupFailureTypes':[type(x).__name__ for x in globals().get('cleanup',[])],'qualification':'Sampled original Linux family custody; synthetic loopback real TLS and normal managed72 only, no native GUI/Home/provider/Windows/full release acceptance.'}
  summary['fullScopedPass']=result['status']=='PASS_FULL_NORMAL_MAIL72_WITH_REAL_LOOPBACK_TRUSTED_TLS_UNACCEPTED' and drained and callerDrained and crlDrained and not trusted and primary is None and not globals().get('cleanup',[])
  collect_public(output,expectedCommit,(password,storePassword),summary)
  destination=os.environ.get('GITHUB_OUTPUT')
  if not destination:raise RuntimeError('Actual workflow output channel absent')
  with open(destination,'a') as stream:stream.write('public_ready=true\n')
 except BaseException as failure:
  # No secret/server exception text is printed. A publication refusal also
  # fails an otherwise successful invocation; original private diagnostics stay.
  print('Declared public evidence refusal: '+type(failure).__name__,file=sys.stderr,flush=True)
  os._exit(1)
atexit.register(retain_declared_public_diagnostics)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def run(args):
 r=subprocess.run(args,stdout=subprocess.PIPE,stderr=subprocess.PIPE,check=False)
 if r.returncode:raise RuntimeError('Owned TLS setup command failed: '+args[0])
 return r.stdout
try:
 java=P(shutil.which('java') or '').resolve();openssl=P(shutil.which('openssl') or '').resolve();assert java.is_file() and openssl.is_file()
 # Independently retain exact maintained release tag/object/commit and defining source bytes.
 def api(route):
  with urllib.request.urlopen('https://api.github.com/repos/greenmail-mail-test/greenmail/'+route,timeout=30) as f:return json.load(f)
 tag=api('git/ref/tags/release-2.1.14');assert tag['object']['sha']=='fddd2b1e45c01008cf737268a3f44a426642e442' and tag['object']['type']=='tag'
 annotated=api('git/tags/'+tag['object']['sha']);assert annotated['object']['sha']=='cdde8bb9488f11763df0737d69a892cf131b9445' and annotated['object']['type']=='commit'
 (out/'mail-maintained-release-tag.json').write_text(json.dumps({'ref':tag,'annotated':annotated},indent=2)+'\n')
 sourcePins=[('greenmail-standalone/src/main/java/com/icegreen/greenmail/standalone/GreenMailStandaloneRunner.java', '667aa9df09e8e371dffe2cf5d36dbfe81608db26a6fd7f6738d10b72bab61411'), ('greenmail-core/src/main/java/com/icegreen/greenmail/util/PropertiesBasedServerSetupBuilder.java', 'c4f570360592208e1a7de782301701167a99bc0a981764980caf737c9cab0726'), ('greenmail-core/src/main/java/com/icegreen/greenmail/configuration/PropertiesBasedGreenMailConfigurationBuilder.java', '482aa631a7a732dc2c3cc65a874f4533eb4c517d9793021259fb25e17f9a9cca')]
 sourceOut=out/'mail-maintained-configuration-source';sourceOut.mkdir()
 for rel,pin in sourcePins:
  entry=api('contents/'+rel+'?ref=cdde8bb9488f11763df0737d69a892cf131b9445');data=base64.b64decode(entry['content']);assert hashlib.sha256(data).hexdigest()==pin
  (sourceOut/P(rel).name).write_bytes(data)
 jar=work/'greenmail-standalone-2.1.14.jar'
 with urllib.request.urlopen(url,timeout=90) as response,jar.open('wb') as f:
  total=0
  while b:=response.read(1024*1024):
   total+=len(b);assert total<=64*1024*1024;f.write(b)
 assert sha(jar)==expected
 retained=out/'mail-maintained-runtime';retained.mkdir();shutil.copyfile(jar,retained/jar.name);assert sha(retained/jar.name)==expected
 # Retain checksum and immutable release source identity; no fixture credentials/private keys.
 (out/'mail-maintained-server-producer.json').write_text(json.dumps({'artifactURL':url,'jarSHA256':sha(jar),'jarBytes':jar.stat().st_size,'sourceRepository':'https://github.com/greenmail-mail-test/greenmail','releaseTag':'release-2.1.14','annotatedTagObject':'fddd2b1e45c01008cf737268a3f44a426642e442','peeledCommit':'cdde8bb9488f11763df0737d69a892cf131b9445','javaPath':str(java),'javaSHA256':sha(java),'opensslPath':str(openssl),'opensslSHA256':sha(openssl),'qualification':'Real isolated maintained test server; no cloud account/Home Send/native production deployment claim'},indent=2)+'\n')
 (out/'mail-java-version.txt').write_bytes(subprocess.run([str(java),'-version'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,check=True).stdout)
 # Each public certificate has a genuine loopback CRL distribution point; ordinary
 # MailKit/.NET online revocation remains enabled. No production callback changes.
 reserved=[]
 for _ in range(3):
  s=socket.socket();s.bind(('127.0.0.1',0));reserved.append(s)
 smtp,imap,crlPort=[s.getsockname()[1] for s in reserved]
 for s in reserved:s.close()
 crlRequestPath='/astra-crl-'+secrets.token_hex(16)+'.der';crlURL='http://127.0.0.1:'+str(crlPort)+crlRequestPath
 ca=work/'ca.pem';caKey=work/'ca.key';cert=work/'server.pem';key=work/'server.key';csr=work/'server.csr';p12=work/'server.p12'
 crlPem=work/'original-crl.pem';crlDer=work/'original-crl.der';caConfig=work/'ca.cnf';caIndex=work/'ca-index';caSerial=work/'ca-serial';caCrlNumber=work/'ca-crlnumber';issued=work/'issued'
 caIndex.write_text('');caSerial.write_text(secrets.token_hex(16)+'\n');caCrlNumber.write_text('1000\n');issued.mkdir()
 run([str(openssl),'req','-x509','-newkey','rsa:3072','-sha256','-nodes','-days','2','-subj','/CN=Astra owned synthetic Mail CA '+secrets.token_hex(12),'-addext','basicConstraints=critical,CA:TRUE','-addext','keyUsage=critical,keyCertSign,cRLSign','-addext','subjectKeyIdentifier=hash','-addext','crlDistributionPoints=URI:'+crlURL,'-keyout',str(caKey),'-out',str(ca)])
 run([str(openssl),'req','-new','-newkey','rsa:3072','-nodes','-subj','/CN=localhost','-keyout',str(key),'-out',str(csr)])
 caConfig.write_text('[ca]\ndefault_ca=owned_ca\n[owned_ca]\ndatabase='+str(caIndex)+'\nnew_certs_dir='+str(issued)+'\ncertificate='+str(ca)+'\nprivate_key='+str(caKey)+'\nserial='+str(caSerial)+'\ncrlnumber='+str(caCrlNumber)+'\ndefault_md=sha256\ndefault_days=2\ndefault_crl_days=2\npolicy=synthetic_subject\nx509_extensions=owned_server\ncrl_extensions=owned_crl\nunique_subject=no\n[synthetic_subject]\ncommonName=supplied\n[owned_server]\nsubjectAltName=DNS:localhost,IP:127.0.0.1\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid:always\ncrlDistributionPoints=URI:'+crlURL+'\n[owned_crl]\nauthorityKeyIdentifier=keyid:always\n')
 run([str(openssl),'ca','-batch','-notext','-config',str(caConfig),'-in',str(csr),'-out',str(cert)])
 run([str(openssl),'ca','-gencrl','-config',str(caConfig),'-out',str(crlPem)])
 run([str(openssl),'crl','-in',str(crlPem),'-outform','DER','-out',str(crlDer)])
 run([str(openssl),'verify','-CAfile',str(ca),'-CRLfile',str(crlPem),'-crl_check_all',str(cert)])
 run([str(openssl),'pkcs12','-export','-inkey',str(key),'-in',str(cert),'-certfile',str(ca),'-name','greenmail','-out',str(p12),'-passout','pass:'+storePassword])
 run(['sudo','cp',str(ca),str(trust)]);trusted=True;run(['sudo','update-ca-certificates'])
 shutil.copyfile(ca,out/'mail-owned-public-ca.pem');shutil.copyfile(cert,out/'mail-owned-public-server.pem')
 # The CRL producer owns an original session and exact loopback listener, just
 # like the original maintained Java server. Its private receipt contains no key.
 crlHelper=root/'.github/scripts/astra-mail-local-crl-server.py';assert crlHelper.is_file() and not crlHelper.is_symlink()
 crlHelperSHA=sha(crlHelper);python=P(sys.executable).resolve();pythonSHA=sha(python);crlReceipt=work/'crl-receipt.json';crlSHA=sha(crlDer)
 shutil.copyfile(crlPem,out/'mail-owned-public-crl.pem');shutil.copyfile(crlDer,out/'mail-owned-public-crl.der')
 crlAttempted=True;crlProcess=subprocess.Popen([str(python),str(crlHelper),'--port',str(crlPort),'--crl',str(crlDer),'--sha256',crlSHA,'--request-path',crlRequestPath,'--receipt',str(crlReceipt)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
 crlSession=module.OriginalSession(crlProcess,crlRecords)
 javaArgs=[str(java),'-Dgreenmail.smtps.hostname=127.0.0.1','-Dgreenmail.imaps.hostname=127.0.0.1','-Dgreenmail.smtps.port='+str(smtp),'-Dgreenmail.imaps.port='+str(imap),'-Dgreenmail.users=fixture:'+password+'@localhost','-Dgreenmail.users.login=email','-Dgreenmail.tls.keystore.file='+str(p12),'-Dgreenmail.tls.keystore.password='+storePassword,'-Dgreenmail.tls.key.password='+storePassword,'-jar',str(jar)]
 # Raw server output stays private because maintained components may print account configuration.
 serverLog=(work/'server.log').open('wb');javaAttempted=True;process=subprocess.Popen(javaArgs,stdout=serverLog,stderr=subprocess.STDOUT,start_new_session=True)
 session=module.OriginalSession(process,javaRecords)
 expectedCertificateSHA=hashlib.sha256(run([str(openssl),'x509','-in',str(cert),'-outform','DER'])).hexdigest()
 def require_original_listener(ownedProcess,ownedSession,port):
  before=module.OriginalSession.stat(ownedProcess.pid);assert before is not None and before[3]==ownedSession.original[3] and ownedProcess.poll() is None
  descriptors=set()
  for fd in (P('/proc')/str(ownedProcess.pid)/'fd').iterdir():
   try:value=os.readlink(fd)
   except FileNotFoundError:continue
   match=re.fullmatch(r'socket:\[(\d+)\]',value)
   if match:descriptors.add(match.group(1))
  listeners=[]
  for table in ('tcp','tcp6'):
   for line in (P('/proc/net')/table).read_text().splitlines()[1:]:
    row=line.split()
    if len(row)>9 and row[3]=='0A' and int(row[1].rsplit(':',1)[1],16)==port and row[9] in descriptors:listeners.append({'table':table,'local':row[1],'inode':row[9]})
  after=module.OriginalSession.stat(ownedProcess.pid);assert listeners and after is not None and after[1:4]==before[1:4], 'Expected port must belong to original owned process'
  return listeners
 def require_java_listener(port):return require_original_listener(process,session,port)
 crlReady=False;deadline=time.monotonic()+30
 while time.monotonic()<deadline and not crlReady:
  crlSession.observe();assert crlProcess.poll() is None,'Original CRL producer stopped before readiness'
  try:
   crlListeners=require_original_listener(crlProcess,crlSession,crlPort)
   connection=http.client.HTTPConnection('127.0.0.1',crlPort,timeout=2)
   try:
    connection.request('GET',crlRequestPath);response=connection.getresponse();served=response.read(1024*1024+1)
    assert response.status==200 and response.getheader('Content-Type')=='application/pkix-crl' and served==crlDer.read_bytes() and hashlib.sha256(served).hexdigest()==crlSHA
   finally:connection.close()
   readyCrl=json.loads(crlReceipt.read_text());assert readyCrl['originalCrlSha256']==crlSHA and readyCrl['pid']==crlProcess.pid and readyCrl['successfulGetRequests']>=1
   crlReady=True
  except (OSError,AssertionError,http.client.HTTPException):time.sleep(.1)
 assert crlReady,'Original signed CRL loopback distribution readiness failed'
 # Python readiness checks the genuine signed CRL across the full certificate
 # chain as well as normal OS trust, hostname, original leaf and protocol banner.
 ready={};context=ssl.create_default_context();context.load_verify_locations(cafile=str(crlPem));context.verify_flags|=ssl.VERIFY_CRL_CHECK_CHAIN;assert context.cert_store_stats()['crl']>=1;deadline=time.monotonic()+60
 while time.monotonic()<deadline and len(ready)<2:
  session.observe();crlSession.observe();assert process.poll() is None and crlProcess.poll() is None
  for protocol,port,prefix in [('smtps',smtp,b'220'),('imaps',imap,b'* OK')]:
   if protocol in ready:continue
   try:
    with socket.create_connection(('127.0.0.1',port),timeout=2) as raw,context.wrap_socket(raw,server_hostname=host) as connection:
     peerSHA=hashlib.sha256(connection.getpeercert(binary_form=True)).hexdigest();assert peerSHA==expectedCertificateSHA
     assert connection.recv(1024).startswith(prefix);listeners=require_java_listener(port);ready[protocol]={'port':port,'tlsVersion':connection.version(),'peerCertificateSHA256':peerSHA,'expectedOriginalCertificateMatched':True,'originalJavaListeners':listeners,'javaPID':process.pid,'javaStartTicks':session.original[3],'normalOSCertificateValidation':True,'originalSignedChainCrlValidation':True,'publicCrlSha256':crlSHA}
   except (OSError,AssertionError):pass
  time.sleep(.1)
 assert len(ready)==2,'Actual strict TLS protocol readiness failed'
 (out/'mail-actual-strict-tls-readiness.json').write_text(json.dumps(ready,indent=2)+'\n')
 publicCrl={'crlURL':crlURL,'originalDerSha256':crlSHA,'originalPemSha256':sha(crlPem),'originalHelperSha256':crlHelperSHA,'pythonPath':str(python),'pythonSha256':pythonSHA,'originalCrlPID':crlProcess.pid,'originalCrlStartTicks':crlSession.original[3],'originalCrlListeners':crlListeners,'fullChainOpenSslCrlCheckPassed':True,'qualification':'Synthetic local signed CRL. Production MailKit and .NET online revocation remain enabled; readiness GETs are not attributed to the owning .NET caller.'}
 baselineCrl=json.loads(crlReceipt.read_text());assert baselineCrl['originalCrlSha256']==crlSHA and baselineCrl['pid']==crlProcess.pid and baselineCrl['successfulGetRequests']>=1
 publicCrl['readinessGetRequests']=baselineCrl['successfulGetRequests'];(out/'mail-original-public-crl-distribution.json').write_text(json.dumps(publicCrl,indent=2)+'\n')
 verify_original_cut();assert sha(jar)==expected and sha(java)==json.loads((out/'mail-maintained-server-producer.json').read_text())['javaSHA256']
 env=dict(os.environ,HAVEN_MAIL_FIXTURE_HOST=host,HAVEN_MAIL_FIXTURE_ADDRESS=address,HAVEN_MAIL_FIXTURE_PASSWORD=password,HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT=str(imap),HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT=str(smtp))
 callerAttempted=True;caller=subprocess.Popen(argv,env=env,start_new_session=True)
 callerSession=MailCallerSession(caller,callerRecords,out/'profile-correction'/'original-native-session-drain');deadline=time.monotonic()+2400
 try:
  while caller.poll() is None:
   session.observe();callerSession.observe();crlSession.observe();assert process.poll() is None and crlProcess.poll() is None,'Original real mail or CRL server stopped during owning tests'
   if time.monotonic()>deadline:raise TimeoutError('Whole Mail caller exceeded supervised deadline')
   time.sleep(.1)
  assert caller.returncode==0,'Actual full owning caller failed'
  verify_original_cut();assert sha(jar)==expected and sha(java)==json.loads((out/'mail-maintained-server-producer.json').read_text())['javaSHA256']
 finally:
  # The original caller owns only its session; authenticated inner native owners
  # must have their own recorded drain before the caller true seal can be written.
  originalFailure=sys.exc_info()[1]
  callerFinalFailures=[]
  try:
   finalCrl=json.loads(crlReceipt.read_text());assert finalCrl['pid']==crlProcess.pid and finalCrl['originalCrlSha256']==crlSHA and finalCrl['successfulGetRequests']>=baselineCrl['successfulGetRequests']
   assert sha(crlHelper)==crlHelperSHA and sha(python)==pythonSHA and sha(crlDer)==crlSHA
   publicCrl['getRequestsAtCallerExit']=finalCrl['successfulGetRequests'];publicCrl['additionalGetRequestsDuringCaller']=finalCrl['successfulGetRequests']-baselineCrl['successfulGetRequests'];(out/'mail-original-public-crl-distribution.json').write_text(json.dumps(publicCrl,indent=2)+'\n')
  except BaseException as crlEvidenceError:
   callerFinalFailures.append(crlEvidenceError)
  try:callerSession.drain();caller.wait(timeout=30);callerDrained=True
  except BaseException as callerDrainError:callerFinalFailures.append(callerDrainError)
  if callerFinalFailures:raise BaseExceptionGroup('Owning and original caller/evidence failures',([originalFailure] if originalFailure is not None else [])+callerFinalFailures)
except BaseException as error:
 primary=error;raise
finally:
 cleanup=[]
 try:
  if callerSession is not None and not callerDrained:
   callerSession.drain();caller.wait(timeout=30);callerDrained=True
  elif not callerAttempted:
   callerDrained=True;(callerRecords/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':False,'drained':True,'status':'No caller spawn was attempted'})+'\n')
 except BaseException as error:cleanup.append(error)
 try:
  if session is not None:session.drain();drained=True
  elif not javaAttempted:
   drained=True;(javaRecords/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':False,'drained':True,'status':'No Java spawn was attempted'})+'\n')
  if process is not None:process.wait(timeout=10)
  if serverLog is not None:serverLog.close()
 except BaseException as error:cleanup.append(error)
 try:
  if crlSession is not None:crlSession.drain();crlDrained=True
  elif not crlAttempted:
   crlDrained=True;(crlRecords/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':False,'drained':True,'status':'No CRL spawn was attempted'})+'\n')
  if crlProcess is not None:crlProcess.wait(timeout=10)
 except BaseException as error:cleanup.append(error)
 if trusted and drained and callerDrained and crlDrained:
  try:run(['sudo','rm','--',str(trust)]);run(['sudo','update-ca-certificates']);trusted=False
  except BaseException as error:cleanup.append(error)
 if drained and callerDrained and crlDrained and not cleanup:
  try:shutil.rmtree(work)
  except BaseException as error:cleanup.append(error)
 if not (drained and callerDrained and crlDrained) or cleanup:
  try:(out/'mail-cleanup-refused.json').write_text(json.dumps({'originalJavaDrainProven':drained,'originalCallerAndDelegatedNativeDrainsProven':callerDrained,'originalCrlDrainProven':crlDrained,'ownedTrustRetained':trusted,'privateStateRetained':work.exists(),'actualCleanupFailureTypes':[type(error).__name__ for error in cleanup],'qualification':'Failed cleanup retains private state; every actual cleanup failure remains in the raised exception group; no raw server logs/credentials archived'})+'\n')
  except BaseException as error:cleanup.append(error)
 if cleanup:raise BaseExceptionGroup('Owning and all original cleanup failures',([primary] if primary is not None else [])+cleanup)
