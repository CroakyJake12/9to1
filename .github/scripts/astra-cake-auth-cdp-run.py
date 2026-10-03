"""Owned-CDP combined direct700/browser job; unchanged full6 OIDC and traffic09 jobs remain required."""
import pathlib,subprocess,json,hashlib,os,time,importlib.util,tempfile,sys,re,stat
root=pathlib.Path.cwd();out=root/'artifacts/cake-auth-independent';out.mkdir(parents=True,exist_ok=False)
expected=os.environ['EXPECTED_COMMIT'];manifest=root/'.github/validation/astra-cake-id-local-runtime-cut.json'
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==expected==os.environ['GITHUB_SHA']
assert hashlib.sha256(manifest.read_bytes()).hexdigest()==os.environ['EXPECTED_MANIFEST_SHA']
cut=json.loads(manifest.read_text());cut_paths={row['path']:row for row in cut['files']}
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
allow_generated_ui=False
generated_ui=None
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
  if path=='cloud/cake-id-auth/public/auth-ui.txt' and allow_generated_ui:
   assert generated_ui is not None and generated_ui=={'path':path,'type':'file','bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
  else:assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256']
  # Generated auth UI has the sole byte allowlist; original Git object still bound to cut.
  expected_map[path]=(row['mode'],row['object'])
 assert actual==expected_map and links=={row['path']:row['commit'] for row in cut['gitlinks']}
 assert len(links)==22
 assert digest(manifest)==os.environ['EXPECTED_MANIFEST_SHA']
verify()
def exact_module(name,relative):
 p=root/relative;assert digest(p)==cut_paths[relative]['sha256'];spec=importlib.util.spec_from_file_location(name,p);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
sessions=exact_module('independent_sessions','.github/scripts/astra_original_native_session_drain.py')
state_wrapper=exact_module('independent_cdp_state','.github/scripts/astra-cake-auth-cdp-state.py')
browser_module=exact_module('original_headless_cdp','.github/scripts/astra-cake-auth-original-headless-cdp.py')
package=root/'cloud/cake-id-auth';harness=root/'.github/validation/cake-auth-browser';fixture=harness/'login-traffic-browser-cdp-phase.mjs'
assert not (package/'.dev.vars').exists() and not (package/'.local-run').exists()
assert subprocess.check_output(['node','--version'],text=True).strip().startswith('v24.')
env=dict(os.environ,CI='true',WRANGLER_SEND_METRICS='false',NO_COLOR='1');cache=pathlib.Path(os.environ['RUNNER_TEMP'])/'astra-auth-chromium'
assert not cache.exists();env['PLAYWRIGHT_BROWSERS_PATH']=str(cache);os.environ['PLAYWRIGHT_BROWSERS_PATH']=str(cache)
for key in env:
 if key=='MINIFLARE_CACHE_DIR' or key.startswith(('CLOUDFLARE_','CF_API_')):raise ValueError('No cloud credentials or ambient Miniflare cache in private local validation')
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
 global allow_generated_ui,generated_ui
 records=out/('cleanup-'+label);records.mkdir();(records/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
 with tempfile.TemporaryFile() as log:
  session=None;code=None;errors=[]
  try:
   process=subprocess.Popen(argv,cwd=cwd,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
   session=sessions.OriginalSession(process,records);deadline=time.monotonic()+900
   while process.poll() is None:
    session.observe()
    if time.monotonic()>deadline:raise TimeoutError('Independent bootstrap command deadline')
    time.sleep(.1)
   code=process.wait()
   if code:raise RuntimeError('Actual independent bootstrap failed: '+label)
  except BaseException as error:errors.append(error)
  finally:
   try:
    if session is None:raise RuntimeError('Original bootstrap session unavailable; preserve false seal')
    session.drain()
   except BaseException as error:errors.append(error)
   try:
    log.seek(0);head=b'';tail=b'';length=0;fullhash=hashlib.sha256()
    while block:=log.read(65536):
     length+=len(block);fullhash.update(block)
     if len(head)<262144:head+=block[:262144-len(head)]
     tail=(tail+block)[-262144:]
    raw=head if length<=262144 else head+tail[len(head)+len(tail)-length:] if length<=524288 else (head.rsplit(b'\n',1)[0] if b'\n' in head else b'')+b'\n[BOUNDED LOG CUT]\n'+(tail.split(b'\n',1)[1] if b'\n' in tail else b'')
    (out/(label+'-sanitized.log')).write_text(sanitized(raw));commands.append({'command':argv,'exitCode':code,'rawLogSha256':fullhash.hexdigest(),'rawLogBytes':length});(out/'commands.json').write_text(json.dumps(commands,indent=2)+'\n')
   except BaseException as error:errors.append(error)
  if errors:raise errors[0] if len(errors)==1 else BaseExceptionGroup('Bootstrap execution/original drain/evidence failed',errors)
 if label=='build-ui':
  ui=root/'cloud/cake-id-auth/public/auth-ui.txt';info=ui.lstat()
  assert stat.S_ISREG(info.st_mode) and not ui.is_symlink()
  data=ui.read_bytes();generated_ui={'path':'cloud/cake-id-auth/public/auth-ui.txt','type':'file','bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
  # Retain actual generated source bytes before any Worker/browser/test credentials exist.
  (out/'generated-auth-ui-original.txt').write_bytes(data)
  (out/'generated-auth-ui-producer.json').write_text(json.dumps({'status':'ACTUAL_GENERATED_UI_BOUND_BEFORE_FIRST_BROWSER_WORKER_EXECUTION','generated':generated_ui,'buildCommand':argv,'builderSourceSha256':cut_paths['cloud/cake-id-auth/scripts/build-ui.mjs']['sha256'],'cutManifestSha256':os.environ['EXPECTED_MANIFEST_SHA'],'qualification':'Sole intentional build overwrite remains source-bound; exact ordinary generated bytes/SHA are now required at every subsequent verification and retained before synthetic credentials exist.'},indent=2)+'\n')
  allow_generated_ui=True
 verify()
for args,label in [(['npm','ci','--no-audit','--no-fund'],'npm-ci'),(['npm','run','typecheck'],'typecheck'),(['npm','run','schema:validate'],'schema'),(['npm','run','build:ui'],'build-ui')]:command(args,label,package)
command(['npm','ci','--no-audit','--no-fund'],'playwright-pinned-ci',harness)
archive_root=root/'artifacts/cake-auth-official-headless-original'
archive_receipt=out/'official-headless-archive-admission.json'
installer=root/'.github/scripts/astra-cake-auth-headless-archive.py'
assert digest(installer)==cut_paths['.github/scripts/astra-cake-auth-headless-archive.py']['sha256']
command([sys.executable,str(installer),'--harness',str(harness),'--cache',str(cache),'--archive-root',str(archive_root),'--receipt',str(archive_receipt)],'official-headless-archive',harness)
def inventory(directory):
 import stat
 rows=[]
 for p in sorted(directory.rglob('*')):
  info=p.lstat();row={'path':p.relative_to(directory).as_posix(),'mode':stat.S_IMODE(info.st_mode)}
  if p.is_symlink():
   if not p.resolve(strict=True).is_relative_to(directory.resolve()):raise ValueError('Dependency/browser symlink escapes private tree')
   row.update(type='symlink',symlink=str(p.readlink()))
  elif p.is_file():row.update(type='file',bytes=info.st_size,sha256=digest(p))
  elif p.is_dir():row['type']='directory'
  else:raise ValueError('Unknown dependency/browser node refused')
  rows.append(row)
 return rows
node_before=inventory(harness/'node_modules');auth_node_before=inventory(package/'node_modules');browser_before=inventory(cache)
assert node_before and auth_node_before and browser_before
admission=json.loads(archive_receipt.read_text())
assert admission['status']=='OFFICIAL_HEADLESS_ARCHIVE_COMPLETE_BEFORE_FIRST_EXECUTION'
assert admission['descriptorSha256']==digest(root/'.github/validation/cake-auth-browser/official-browsers.json')
assert digest(harness/'node_modules/playwright-core/browsers.json')==admission['descriptorSha256']
assert digest(harness/'node_modules/playwright-core/lib/coreBundle.js')==admission['maintainedRegistrySourceSha256']
assert digest(pathlib.Path(admission['archivePath']))==admission['archive']['sha256']
assert pathlib.Path(admission['archivePath']).stat().st_size==admission['archive']['bytes']
wrapper=pathlib.Path(admission['installedRoot']).relative_to(cache).as_posix()
expected_browser=[{'path':wrapper,'mode':admission['installedRootIdentity']['mode'],'type':'directory'}]
expected_browser.extend(dict(row,path=wrapper+'/'+row['path']) for row in admission['completeInstalledMap'])
assert browser_before==sorted(expected_browser,key=lambda row:row['path'])
executable=browser_module.headless_executable(cache,harness,admission['descriptorSha256'],admission['executableSha256'])
assert str(executable)==admission['executable'] and browser_module.identity(executable)==admission['executableIdentity']
(out/'browser-dependencies-before.json').write_text(json.dumps({'nodeModules':node_before,'authNodeModules':auth_node_before,'browser':browser_before,'playwrightVersion':'1.63.0','chromiumRevision':'1243','qualification':'Direct pinned official HTTPS headless archive and complete ZIP/member/type/mode inventory before first execution; no independently published expected upstream browser archive hash'},indent=2)+'\n')
execution_error=None
try:
 result=state_wrapper.run_fixture(package,fixture,harness,out,sessions,browser_module,executable,admission['executableSha256'],pathlib.Path(os.environ['RUNNER_TEMP']),sanitized,known_secrets)
 actual=result['publicSuccessMarker']
 for key,value in {'phase':'combined-cdp','browserExecuted':True,'directRequests':700,'durableWindowAttempts':8,'durableThrottleResponses':692,'durableLockSeconds':1800,'sameStoreRestart':True,'browserSubmits':2,'browserNetworkRequests':1,'browserConnection':'maintained connectOverCDP'}.items():assert type(actual[key]) is type(value) and actual[key]==value
 assert type(actual['browserOriginalPid']) is int and actual['browserOriginalPid']>0
 assert actual['browserOriginalPid']==json.loads((out/'original-browser-cdp-receipt.json').read_text())['launcherPid']
 for key in ['credentialErrorResponses','builtinThrottleResponses','throttleResponses']:assert type(actual[key]) is int
 assert 1<=actual['credentialErrorResponses']<=8 and 0<=actual['builtinThrottleResponses']<=7
 assert actual['credentialErrorResponses']+actual['builtinThrottleResponses']==8 and actual['throttleResponses']==actual['builtinThrottleResponses']+692
 (out/'actual-independent-success.json').write_text(json.dumps(actual,indent=2)+'\n')
except BaseException as error:execution_error=error
finally:
 guard_error=None
 try:
  verify()
  node_after=inventory(harness/'node_modules');auth_node_after=inventory(package/'node_modules');browser_after=inventory(cache)
  (out/'browser-dependencies-after.json').write_text(json.dumps({'nodeModules':node_after,'authNodeModules':auth_node_after,'browser':browser_after},indent=2)+'\n')
  assert node_before==node_after and auth_node_before==auth_node_after and browser_before==browser_after
  assert not (package/'.dev.vars').exists() and not (package/'.local-run').exists()
  assert digest(pathlib.Path(admission['archivePath']))==admission['archive']['sha256']
  assert json.loads((out/'original-browser-cdp-receipt.json').read_text()).get('profileRemovedAfterBothDrains') is True
  assert all(json.loads(p.read_text()).get('drained') is True for p in (out/'original-browser-session').glob('*.json'))
  assert all(json.loads(p.read_text()).get('drained') is True for p in (out/'independent-auth-original-session').glob('*.json'))
  assert all(json.loads(p.read_text()).get('drained') is True for p in out.glob('cleanup-*/expected-managed-launch.json'))
 except BaseException as error:guard_error=error
 if execution_error is not None and guard_error is not None:raise BaseExceptionGroup('Combined fixture and retained final guards failed',[execution_error,guard_error])
 if guard_error is not None:raise guard_error
 if execution_error is not None:raise execution_error
(out/'result.json').write_text(json.dumps({'independentAuthTrafficBrowserPassed':True,'browserPhase':'combined-cdp','browserOriginalProcessAndNodeOriginalSessionDrained':True,'originalSixCommandOidcContractPassed':False,'deployment':False,'existingUserTransfer':False})+'\n')
