"""Synthetic direct700/browser job; original six-command OIDC job remains required."""
import pathlib,subprocess,json,hashlib,os,time,importlib.util,tempfile,sys,re
root=pathlib.Path.cwd();out=root/'artifacts/cake-auth-independent';out.mkdir(parents=True,exist_ok=False)
expected=os.environ['EXPECTED_COMMIT'];manifest=root/'.github/validation/astra-cake-id-local-runtime-cut.json'
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==expected==os.environ['GITHUB_SHA']
assert hashlib.sha256(manifest.read_bytes()).hexdigest()==os.environ['EXPECTED_MANIFEST_SHA']
cut=json.loads(manifest.read_text());cut_paths={row['path']:row for row in cut['files']}
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
allow_generated_ui=False
def verify():
 actual={};links={}
 for item in subprocess.check_output(['git','ls-tree','-rz','HEAD']).split(b'\0'):
  if not item:continue
  header,path=item.split(b'\t',1);mode,kind,oid=header.decode().split();path=path.decode()
  if mode=='160000':links[path]=oid;continue
  if mode not in ('100644','100755') or kind!='blob':raise ValueError('Unknown actual source type')
  actual[path]=(mode,oid)
 del actual['.github/validation/astra-cake-id-local-runtime-cut.json']
 expected_map={}
 for path,row in cut_paths.items():
  p=root/path;assert p.is_file() and not p.is_symlink();data=p.read_bytes()
  if path!='cloud/cake-id-auth/public/auth-ui.txt' or not allow_generated_ui:assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256']
  # Generated auth UI has the sole byte allowlist; original Git object still bound to cut.
  expected_map[path]=(row['mode'],row['object'])
 assert actual==expected_map and links=={row['path']:row['commit'] for row in cut['gitlinks']}
 assert len(links)==22
 assert digest(manifest)==os.environ['EXPECTED_MANIFEST_SHA']
verify()
def exact_module(name,relative):
 p=root/relative;assert digest(p)==cut_paths[relative]['sha256'];spec=importlib.util.spec_from_file_location(name,p);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
sessions=exact_module('independent_sessions','.github/scripts/astra_original_native_session_drain.py')
state_wrapper=exact_module('independent_state','.github/scripts/astra-cake-auth-independent-state.py')
package=root/'cloud/cake-id-auth';harness=root/'.github/validation/cake-auth-browser';fixture=package/'tests/login-traffic-browser.mjs'
assert not (package/'.dev.vars').exists() and not (package/'.local-run').exists()
assert subprocess.check_output(['node','--version'],text=True).strip().startswith('v24.')
env=dict(os.environ,CI='true',WRANGLER_SEND_METRICS='false',NO_COLOR='1');cache=pathlib.Path(os.environ['RUNNER_TEMP'])/'astra-auth-chromium'
assert not cache.exists();env['PLAYWRIGHT_BROWSERS_PATH']=str(cache);os.environ['PLAYWRIGHT_BROWSERS_PATH']=str(cache)
for key in env:
 if key.startswith(('CLOUDFLARE_','CF_API_')):raise ValueError('No cloud credentials in private local validation')
known_secrets=set()
def sanitized(raw):
 text=raw.decode('utf-8','replace')
 for secret in sorted(known_secrets,key=len,reverse=True):
  if len(secret)>=4:text=text.replace(secret,'[REDACTED_TEST_SECRET]')
 text=re.sub(r'(?i)(?:Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+','[REDACTED_AUTHORIZATION]',text)
 text=re.sub(r'[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}','[REDACTED_JWT]',text)
 text=re.sub(r'(?i)((?:"|\x27)?(?:password|access_token|refresh_token|id_token|client_secret|secret|cookie|set-cookie|authorization|LOCAL_TEST_KEY|AUTH_SECRET|LOGIN_LIMITER_KEY)(?:"|\x27)?\s*[:=]\s*)(?:"[^"]*"|\x27[^\x27]*\x27|[^\s,}]+)',r'\1[REDACTED_FIELD]',text)
 text=re.sub(r'[A-Za-z0-9_+.-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}','[REDACTED_TEST_EMAIL]',text)
 return re.sub(r'[A-Za-z0-9_~+/=-]{24,}','[REDACTED_OPAQUE_VALUE]',text)
commands=[]
def command(argv,label,cwd):
 global allow_generated_ui
 records=out/('cleanup-'+label);records.mkdir();(records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
 with tempfile.TemporaryFile() as log:
  process=subprocess.Popen(argv,cwd=cwd,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);session=None
  try:
   session=sessions.OriginalSession(process,records);deadline=time.monotonic()+900
   while process.poll() is None:
    session.observe()
    if time.monotonic()>deadline:raise TimeoutError('Independent bootstrap command deadline')
    time.sleep(.1)
   code=process.wait()
  finally:
   if session is not None:session.drain()
  log.seek(0);head=b'';tail=b'';length=0;fullhash=hashlib.sha256()
  while block:=log.read(65536):
   length+=len(block);fullhash.update(block)
   if len(head)<262144:head+=block[:262144-len(head)]
   tail=(tail+block)[-262144:]
  raw=head if length<=262144 else head+tail[len(head)+len(tail)-length:] if length<=524288 else (head.rsplit(b'\n',1)[0] if b'\n' in head else b'')+b'\n[BOUNDED LOG CUT]\n'+(tail.split(b'\n',1)[1] if b'\n' in tail else b'')
  (out/(label+'-sanitized.log')).write_text(sanitized(raw));commands.append({'command':argv,'exitCode':code,'rawLogSha256':fullhash.hexdigest(),'rawLogBytes':length});(out/'commands.json').write_text(json.dumps(commands,indent=2)+'\n')
  if code:raise RuntimeError('Actual independent bootstrap failed: '+label)
 if label=='build-ui':allow_generated_ui=True
 verify()
for args,label in [(['npm','ci','--no-audit','--no-fund'],'npm-ci'),(['npm','run','typecheck'],'typecheck'),(['npm','run','schema:validate'],'schema'),(['npm','run','build:ui'],'build-ui')]:command(args,label,package)
command(['npm','ci','--no-audit','--no-fund'],'playwright-pinned-ci',harness)
command([str(harness/'node_modules/.bin/playwright'),'install','--with-deps','chromium'],'official-chromium-install',harness)
def inventory(directory):
 rows=[]
 for p in sorted(directory.rglob('*')):
  if p.is_symlink():
   if not p.resolve(strict=True).is_relative_to(directory.resolve()):raise ValueError('Dependency/browser symlink escapes private tree')
   rows.append({'path':str(p.relative_to(directory)),'symlink':str(p.readlink())})
  elif p.is_file():rows.append({'path':str(p.relative_to(directory)),'bytes':p.stat().st_size,'sha256':digest(p)})
 return rows
node_before=inventory(harness/'node_modules');auth_node_before=inventory(package/'node_modules');browser_before=inventory(cache)
assert node_before and auth_node_before and browser_before
(out/'browser-dependencies-before.json').write_text(json.dumps({'nodeModules':node_before,'authNodeModules':auth_node_before,'browser':browser_before,'playwrightVersion':'1.63.0','chromiumRevision':'1243','qualification':'Official pinned Playwright install and original observed full byte inventory, not an independent upstream browser archive hash'},indent=2)+'\n')
try:
 result=state_wrapper.run_fixture(package,fixture,harness,out,sessions,sanitized,known_secrets)
 log=(out/'independent-auth-sanitized.log').read_text();decoder=json.JSONDecoder();markers=[]
 for match in re.finditer(r'\{',log):
  try:value,_=decoder.raw_decode(log[match.start():])
  except json.JSONDecodeError:continue
  if isinstance(value,dict) and value.get('result')=='passed':markers.append(value)
 assert len(markers)==1;actual=markers[0]
 for key,value in {'directRequests':700,'wrongAdmitted':8,'blocked':692,'durableLockSeconds':1800,'sameStoreRestart':True,'browserSubmits':2,'browserNetworkRequests':1}.items():assert type(actual[key]) is type(value) and actual[key]==value
 (out/'actual-independent-success.json').write_text(json.dumps({key:actual[key] for key in ['result','directRequests','wrongAdmitted','blocked','durableLockSeconds','sameStoreRestart','browserSubmits','browserNetworkRequests']},indent=2)+'\n')
finally:
 verify()
 node_after=inventory(harness/'node_modules');auth_node_after=inventory(package/'node_modules');browser_after=inventory(cache)
 (out/'browser-dependencies-after.json').write_text(json.dumps({'nodeModules':node_after,'authNodeModules':auth_node_after,'browser':browser_after},indent=2)+'\n')
 assert node_before==node_after and auth_node_before==auth_node_after and browser_before==browser_after
 assert not (package/'.dev.vars').exists() and not (package/'.local-run').exists()
 assert all(json.loads(p.read_text()).get('drained') is True for p in out.glob('cleanup-*/expected-managed-launch.json'))
(out/'result.json').write_text(json.dumps({'independentAuthTrafficBrowserPassed':True,'originalSixCommandOidcContractPassed':False,'deployment':False,'existingUserTransfer':False})+'\n')
