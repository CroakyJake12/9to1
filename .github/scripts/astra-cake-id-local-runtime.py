import pathlib,subprocess,json,hashlib,os,time,importlib.util,tempfile,sys,re,urllib.parse
root=pathlib.Path.cwd();out=root/'artifacts/cake-id-local-runtime';out.mkdir(parents=True,exist_ok=False)
sha=lambda b:hashlib.sha256(b).hexdigest()
helper=root/'.github/scripts/astra_original_native_session_drain.py';spec=importlib.util.spec_from_file_location('drain',helper);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
assert sys.platform=='linux' and subprocess.check_output(['uname','-m'],text=True).strip()=='x86_64'
assert subprocess.check_output(['node','--version'],text=True).strip().startswith('v24.')
package=root/'cloud/cake-id-auth';assert not (package/'.dev.vars').exists() and not (package/'.local-run').exists()
for name in os.environ:
 if name.startswith(('CLOUDFLARE_','CF_API_')):raise RuntimeError('Cloud credentials forbidden in isolated local test')
def tracked():
 result={}
 for line in subprocess.check_output(['git','ls-tree','-r','HEAD'],text=True).splitlines():
  meta,path=line.split('\t',1);mode,kind,oid=meta.split()
  if kind!='blob':continue
  p=root/path;data=p.readlink().as_posix().encode() if mode=='120000' else p.read_bytes()
  assert subprocess.check_output(['git','hash-object','--stdin'],input=data).decode().strip()==oid
  result[path]={'mode':mode,'oid':oid,'sha256':sha(data),'bytes':len(data)}
 return result
before=tracked();(out/'source-before.json').write_text(json.dumps(before,sort_keys=True)+'\n')
commands=[['npm','ci','--no-audit','--no-fund'],['npm','run','typecheck'],['npm','run','schema:validate'],['npm','run','build:ui'],['npm','test'],['npm','run','test:password-runtime']]
env=os.environ.copy();env.update(CI='true',WRANGLER_SEND_METRICS='false',NO_COLOR='1');results=[];known_secrets=set()
def sanitized(raw):
 text=raw.decode("utf-8","replace")
 for secret in sorted(known_secrets,key=len,reverse=True):
  if len(secret)>=4:text=text.replace(secret,"[REDACTED_TEST_SECRET]")
 text=re.sub(r"(?i)(?:Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+","[REDACTED_AUTHORIZATION]",text)
 text=re.sub(r"[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}","[REDACTED_JWT]",text)
 text=re.sub(r"(?i)((?:\"|\x27)?(?:password|access_token|refresh_token|id_token|client_secret|secret|cookie|set-cookie|authorization|LOCAL_TEST_KEY|AUTH_SECRET|LOGIN_LIMITER_KEY)(?:\"|\x27)?\s*[:=]\s*)(?:\"[^\"]*\"|\x27[^\x27]*\x27|[^\s,}]+)",r"\1[REDACTED_FIELD]",text)
 text=re.sub(r"[A-Za-z0-9_+.-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}","[REDACTED_TEST_EMAIL]",text)
 text=re.sub(r"[A-Za-z0-9_~+/=-]{24,}","[REDACTED_OPAQUE_VALUE]",text)
 return text

try:
 for index,cmd in enumerate(commands):
  records=out/('cleanup-'+str(index));records.mkdir();(records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
  with tempfile.TemporaryFile() as log:
   process=subprocess.Popen(['bash','-c','sleep 0.2; exec "$@"','cake-runtime',*cmd],cwd=package,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
   session=module.OriginalSession(process,records);deadline=time.monotonic()+900
   try:
    while process.poll() is None:
     session.observe()
     vars_file=package/'.dev.vars'
     if vars_file.exists():
      for line in vars_file.read_text().splitlines():
       if '=' in line:known_secrets.add(line.split('=',1)[1])
     if time.monotonic()>deadline:raise TimeoutError('Bounded local package command exceeded deadline')
     time.sleep(.1)
    code=process.wait()
   finally:session.drain()
   log.seek(0);digest=hashlib.sha256();length=0;head=b'';tail=b''
   while block:=log.read(65536):
    digest.update(block);length+=len(block)
    if len(head)<262144:head+=block[:262144-len(head)]
    tail=(tail+block)[-262144:]
   complete_head=head.rsplit(b'\n',1)[0] if b'\n' in head else b''
   complete_tail=tail.split(b'\n',1)[1] if b'\n' in tail else b''
   retained=head if length<=262144 else head+tail[len(head)+len(tail)-length:] if length<=524288 else complete_head+b'\n[BOUNDED LOG: MIDDLE AND CUT EDGE LINES OMITTED]\n'+complete_tail
   (out/('command-'+str(index)+'-sanitized.log')).write_text(sanitized(retained))
   # Actual maintained final public JSON markers, independently of redacted log text.
   if cmd in (['npm','test'],['npm','run','test:password-runtime']) and code==0:
    decoder=json.JSONDecoder();candidates=[];decoded=retained.decode('utf-8','replace')
    for match in re.finditer(r'\{',decoded):
     try:value,_=decoder.raw_decode(decoded[match.start():])
     except json.JSONDecodeError:continue
     if isinstance(value,dict) and value.get('result')=='passed':candidates.append(value)
    assert len(candidates)==1,'Require exactly one actual final owner success JSON marker'
    actual=candidates[0]
    if cmd==['npm','test']:
     assert actual['environment']=='local Wrangler Workerd + local D1 only' and actual['cloudflareDeployment']=='none'
     assert actual['publicClientRegistration']=='synthetic and local only; client id intentionally not printed'
     assert actual['redirectUri']=='http://127.0.0.1:5096/callback (synthetic test only)'
     assert actual['externalEmailDelivery']=='not tested; messages captured in local D1 outbox'
     fields=['result','environment','issuer','authorizationEndpoint','tokenEndpoint','jwksUri','publicClientRegistration','redirectUri','passwordResetAndLoginMs','passwordLoginAndTokenExchangeMs','externalEmailDelivery','cloudflareDeployment']
     for field in ['issuer','authorizationEndpoint','tokenEndpoint','jwksUri']:
      assert isinstance(actual[field],str);parsed=urllib.parse.urlsplit(actual[field])
      assert parsed.scheme=='http' and parsed.hostname=='127.0.0.1' and parsed.port==8798 and parsed.username is None and parsed.password is None and not parsed.fragment
     for field in ['passwordResetAndLoginMs','passwordLoginAndTokenExchangeMs']:assert type(actual[field]) in [int,float] and 0<=actual[field]<900000
    else:
     assert actual['runtime']=='local Cloudflare Workerd' and actual['algorithm']=='Better Auth default scrypt' and actual['database']=='none' and actual['externalService']=='none'
     assert actual['saltBytes']==16 and actual['derivedKeyBytes']==64
     fields=['result','runtime','algorithm','saltBytes','derivedKeyBytes','hashDurationMs','twoPasswordVerificationsDurationMs','database','externalService']
     for field in ['hashDurationMs','twoPasswordVerificationsDurationMs']:assert type(actual[field]) in [int,float] and 0<=actual[field]<900000
    (out/('command-'+str(index)+'-actual-public-success.json')).write_text(json.dumps({k:actual[k] for k in fields},indent=2)+'\n')

   results.append({'command':cmd,'exitCode':code,'privateLogSha256':digest.hexdigest(),'privateLogBytes':length,'rawLogRetained':False})
   (out/'commands.json').write_text(json.dumps(results,indent=2)+'\n')
   if code!=0:raise RuntimeError('Actual local package command failed: '+str(cmd)+' exit '+str(code))
finally:
 assert not (package/'.dev.vars').exists(),'Owner test retained private local vars'
 assert not (package/'.local-run').exists(),'Owner test retained private local database state'
 # Verify every source except the single maintained build:ui output; no generated private state is archived.
 after={}
 for path,entry in before.items():
  p=root/path;data=p.readlink().as_posix().encode() if entry['mode']=='120000' else p.read_bytes()
  after[path]={'mode':entry['mode'],'sha256':sha(data),'bytes':len(data)}
  if path!='cloud/cake-id-auth/public/auth-ui.txt':assert after[path]['sha256']==entry['sha256'],path
 (out/'source-after.json').write_text(json.dumps(after,sort_keys=True)+'\n')
 assert all(json.loads(p.read_text()).get('drained') is True for p in out.glob('cleanup-*/expected-managed-launch.json'))
(out/'result.json').write_text(json.dumps({'actualLocalCommandsPassed':True,'deployment':False,'productionCredentials':False,'generatedSourceAllowlist':['cloud/cake-id-auth/public/auth-ui.txt'],'rawPrivateLogsArchived':False})+'\n')
