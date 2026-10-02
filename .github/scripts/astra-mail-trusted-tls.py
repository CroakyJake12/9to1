"""Owned synthetic maintained SMTP/IMAP witness; ordinary TLS trust, no Send grant.
Root executes this only inside the reviewed disposable Linux CI caller.
"""
import argparse,base64,hashlib,re,importlib.util,json,os,pathlib,secrets,shutil,socket,ssl,subprocess,sys,tempfile,time,urllib.request
P=pathlib.Path
sys.path.insert(0,str((P.cwd()/'.github/scripts').resolve()))
from astra_mail_original_caller_session import MailCallerSession
p=argparse.ArgumentParser();p.add_argument('caller',nargs=argparse.REMAINDER);a=p.parse_args();assert a.caller and a.caller[0]=='--';argv=a.caller[1:];assert argv
root=P.cwd();out=root/'artifacts/desktop-visible-owning';out.mkdir(parents=True,exist_ok=True)
# Authenticate the complete exact source cut BEFORE download, trust mutation or runtime spawn.
for flag in ('--expected-commit','--manifest','--manifest-sha'):assert argv.count(flag)==1
expectedCommit=argv[argv.index('--expected-commit')+1];manifestPath=argv[argv.index('--manifest')+1];manifestSHA=argv[argv.index('--manifest-sha')+1]
assert manifestPath=='.github/validation/astra-desktop-visible-cut.json'
def verify_original_cut():
 assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==expectedCommit and os.environ.get('GITHUB_SHA')==expectedCommit
 manifest=root/manifestPath;assert not manifest.is_symlink() and hashlib.sha256(manifest.read_bytes()).hexdigest()==manifestSHA
 cut=json.loads(manifest.read_text());actual={};links={}
 for item in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
  if not item:continue
  header,path=item.split(b'\t',1);mode,kind,oid=header.decode().split();path=path.decode()
  if mode=='160000':links[path]=oid
  else:assert kind=='blob' and mode in ('100644','100755');actual[path]=(mode,oid)
 assert manifestPath in actual;del actual[manifestPath];expected={}
 for row in cut['files']:
  relative=P(row['path']);assert not relative.is_absolute() and '..' not in relative.parts and row['path'] not in expected
  path=root/relative;assert path.is_file() and not path.is_symlink();data=path.read_bytes()
  assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256']
  oid=hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest();assert oid==row['object'];expected[row['path']]=(row['mode'],oid)
 assert actual==expected and links=={x['path']:x['commit'] for x in cut['gitlinks']} and len(links)==22
verify_original_cut()

spec=importlib.util.spec_from_file_location('original_session',root/'.github/scripts/astra_original_native_session_drain.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
url='https://repo.maven.apache.org/maven2/com/icegreen/greenmail-standalone/2.1.14/greenmail-standalone-2.1.14.jar'
expected='0381392f3a44e4d8ae78051778440f58820d01acaf5757c3f43649deda8c1d23'
work=P(tempfile.mkdtemp(prefix='astra-mail-private-'));os.chmod(work,0o700)
trust=P('/usr/local/share/ca-certificates')/('astra-mail-'+secrets.token_hex(12)+'.crt')
process=session=serverLog=caller=callerSession=None;trusted=False;primary=None;drained=False;callerDrained=False;javaAttempted=False;callerAttempted=False
javaRecords=out/'mail-original-java-session';callerRecords=out/'mail-original-caller-session'
for records in (javaRecords,callerRecords):
 records.mkdir();(records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False,'status':'before original spawn attempt'})+'\n')
password=secrets.token_urlsafe(32);storePassword=secrets.token_urlsafe(32)
address='fixture@localhost';host='localhost'
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
 ca=work/'ca.pem';caKey=work/'ca.key';cert=work/'server.pem';key=work/'server.key';csr=work/'server.csr';ext=work/'server.ext';p12=work/'server.p12'
 run([str(openssl),'req','-x509','-newkey','rsa:3072','-sha256','-nodes','-days','2','-subj','/CN=Astra owned synthetic Mail CA','-addext','basicConstraints=critical,CA:TRUE','-addext','keyUsage=critical,keyCertSign,cRLSign','-keyout',str(caKey),'-out',str(ca)])
 run([str(openssl),'req','-new','-newkey','rsa:3072','-nodes','-subj','/CN=localhost','-keyout',str(key),'-out',str(csr)])
 ext.write_text('subjectAltName=DNS:localhost,IP:127.0.0.1\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n')
 run([str(openssl),'x509','-req','-in',str(csr),'-CA',str(ca),'-CAkey',str(caKey),'-CAcreateserial','-days','2','-sha256','-extfile',str(ext),'-out',str(cert)])
 run([str(openssl),'pkcs12','-export','-inkey',str(key),'-in',str(cert),'-certfile',str(ca),'-name','greenmail','-out',str(p12),'-passout','pass:'+storePassword])
 run(['sudo','cp',str(ca),str(trust)]);trusted=True;run(['sudo','update-ca-certificates'])
 shutil.copyfile(ca,out/'mail-owned-public-ca.pem');shutil.copyfile(cert,out/'mail-owned-public-server.pem')
 # Chosen loopback ports are observed at actual TLS readiness; collision refuses.
 reserved=[]
 for _ in range(2):
  s=socket.socket();s.bind(('127.0.0.1',0));reserved.append(s)
 smtp,imap=[s.getsockname()[1] for s in reserved]
 for s in reserved:s.close()
 javaArgs=[str(java),'-Dgreenmail.smtps.hostname=127.0.0.1','-Dgreenmail.imaps.hostname=127.0.0.1','-Dgreenmail.smtps.port='+str(smtp),'-Dgreenmail.imaps.port='+str(imap),'-Dgreenmail.users=fixture:'+password+'@localhost','-Dgreenmail.users.login=email','-Dgreenmail.tls.keystore.file='+str(p12),'-Dgreenmail.tls.keystore.password='+storePassword,'-Dgreenmail.tls.key.password='+storePassword,'-jar',str(jar)]
 # Raw server output stays private because maintained components may print account configuration.
 serverLog=(work/'server.log').open('wb');javaAttempted=True;process=subprocess.Popen(javaArgs,stdout=serverLog,stderr=subprocess.STDOUT,start_new_session=True)
 session=module.OriginalSession(process,javaRecords)
 expectedCertificateSHA=hashlib.sha256(run([str(openssl),'x509','-in',str(cert),'-outform','DER'])).hexdigest()
 def require_java_listener(port):
  before=module.OriginalSession.stat(process.pid);assert before is not None and before[3]==session.original[3] and process.poll() is None
  descriptors=set()
  for fd in (P('/proc')/str(process.pid)/'fd').iterdir():
   try:value=os.readlink(fd)
   except FileNotFoundError:continue
   match=re.fullmatch(r'socket:\[(\d+)\]',value)
   if match:descriptors.add(match.group(1))
  listeners=[]
  for table in ('tcp','tcp6'):
   for line in (P('/proc/net')/table).read_text().splitlines()[1:]:
    row=line.split()
    if len(row)>9 and row[3]=='0A' and int(row[1].rsplit(':',1)[1],16)==port and row[9] in descriptors:listeners.append({'table':table,'local':row[1],'inode':row[9]})
  after=module.OriginalSession.stat(process.pid);assert listeners and after is not None and after[1:4]==before[1:4], 'Expected port must belong to original Java process'
  return listeners
 ready={};context=ssl.create_default_context();deadline=time.monotonic()+60
 while time.monotonic()<deadline and len(ready)<2:
  session.observe();assert process.poll() is None
  for protocol,port,prefix in [('smtps',smtp,b'220'),('imaps',imap,b'* OK')]:
   if protocol in ready:continue
   try:
    with socket.create_connection(('127.0.0.1',port),timeout=2) as raw,context.wrap_socket(raw,server_hostname=host) as connection:
     peerSHA=hashlib.sha256(connection.getpeercert(binary_form=True)).hexdigest();assert peerSHA==expectedCertificateSHA
     assert connection.recv(1024).startswith(prefix);listeners=require_java_listener(port);ready[protocol]={'port':port,'tlsVersion':connection.version(),'peerCertificateSHA256':peerSHA,'expectedOriginalCertificateMatched':True,'originalJavaListeners':listeners,'javaPID':process.pid,'javaStartTicks':session.original[3],'normalOSCertificateValidation':True}
   except (OSError,AssertionError):pass
  time.sleep(.1)
 assert len(ready)==2,'Actual strict TLS protocol readiness failed'
 (out/'mail-actual-strict-tls-readiness.json').write_text(json.dumps(ready,indent=2)+'\n')
 verify_original_cut();assert sha(jar)==expected and sha(java)==json.loads((out/'mail-maintained-server-producer.json').read_text())['javaSHA256']
 env=dict(os.environ,HAVEN_MAIL_FIXTURE_HOST=host,HAVEN_MAIL_FIXTURE_ADDRESS=address,HAVEN_MAIL_FIXTURE_PASSWORD=password,HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT=str(imap),HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT=str(smtp))
 callerAttempted=True;caller=subprocess.Popen(argv,env=env,start_new_session=True)
 callerSession=MailCallerSession(caller,callerRecords,out/'original-native-session-drain');deadline=time.monotonic()+5400
 try:
  while caller.poll() is None:
   session.observe();callerSession.observe();assert process.poll() is None,'Original real mail server stopped during owning tests'
   if time.monotonic()>deadline:raise TimeoutError('Whole Mail caller exceeded supervised deadline')
   time.sleep(.1)
  assert caller.returncode==0,'Actual full owning caller failed'
  verify_original_cut();assert sha(jar)==expected and sha(java)==json.loads((out/'mail-maintained-server-producer.json').read_text())['javaSHA256']
 finally:
  # The original caller owns only its session; authenticated inner native owners
  # must have their own recorded drain before the caller true seal can be written.
  originalFailure=sys.exc_info()[1]
  try:callerSession.drain();caller.wait(timeout=30);callerDrained=True
  except BaseException:
   if originalFailure is None:raise
except BaseException as error:
 primary=error;raise
finally:
 cleanup=None
 try:
  if callerSession is not None and not callerDrained:
   callerSession.drain();caller.wait(timeout=30);callerDrained=True
  elif not callerAttempted:
   callerDrained=True;(callerRecords/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':False,'drained':True,'status':'No caller spawn was attempted'})+'\n')
 except BaseException as error:cleanup=error
 try:
  if session is not None:session.drain();drained=True
  elif not javaAttempted:
   drained=True;(javaRecords/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':False,'drained':True,'status':'No Java spawn was attempted'})+'\n')
  if process is not None:process.wait(timeout=10)
  if serverLog is not None:serverLog.close()
 except BaseException as error:cleanup=cleanup or error
 if trusted and drained and callerDrained:
  try:run(['sudo','rm','--',str(trust)]);run(['sudo','update-ca-certificates'])
  except BaseException as error:cleanup=cleanup or error
 if drained and callerDrained and cleanup is None:shutil.rmtree(work)
 else:(out/'mail-cleanup-refused.json').write_text(json.dumps({'originalJavaDrainProven':drained,'originalCallerAndDelegatedNativeDrainsProven':callerDrained,'ownedTrustRetained':trusted,'privateStateRetained':True,'qualification':'Failed cleanup retains private state; no raw server logs/credentials archived'})+'\n')
 if cleanup is not None and primary is None:raise cleanup
