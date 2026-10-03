"""Owned original browser plus independent Node/Worker session; private local fixtures only."""
import pathlib, os, json, hashlib, subprocess, time, tempfile, secrets, stat, shutil

def add_error(errors,error):
 errors.append(error)

def extract_public_success_marker(raw,browser_pid):
 fields=['result','phase','browserExecuted','directRequests','durableWindowAttempts','credentialErrorResponses','builtinThrottleResponses','durableThrottleResponses','throttleResponses','durableLockSeconds','sameStoreRestart','browserSubmits','browserNetworkRequests','browserConnection','browserOriginalPid']
 scope='isolated local Wrangler/D1 and genuine generated UI via runner-issued original browser CDP; source-backed pre-handler ordering, no credential-call counter/elapsed CPU/ingress billing/universal quota/cloud/OIDC acceptance'
 def unique_object(pairs):
  result={}
  for key,value in pairs:
   if key in result:raise ValueError('Duplicate public success record field')
   result[key]=value
  return result
 markers=[]
 for line in raw.splitlines():
  if not line.startswith(b'{"result":"passed",'):continue
  if len(line)>4096:raise ValueError('Public success record exceeds exact bounded scope')
  value=json.loads(line.decode('utf-8','strict'),object_pairs_hook=unique_object)
  if not isinstance(value,dict) or set(value)!=set(fields+['scope']) or value['scope']!=scope:raise ValueError('Unexpected public CDP success fields/scope')
  for key,expected in {'result':'passed','phase':'combined-cdp','browserExecuted':True,'directRequests':700,'durableWindowAttempts':8,'durableThrottleResponses':692,'durableLockSeconds':1800,'sameStoreRestart':True,'browserSubmits':2,'browserNetworkRequests':1,'browserConnection':'maintained connectOverCDP','browserOriginalPid':browser_pid}.items():
   if type(value[key]) is not type(expected) or value[key]!=expected:raise ValueError('Public CDP success exact value/type mismatch')
  for key in ['credentialErrorResponses','builtinThrottleResponses','throttleResponses']:
   if type(value[key]) is not int:raise ValueError('Public response counter requires actual integer')
  if not 1<=value['credentialErrorResponses']<=8 or not 0<=value['builtinThrottleResponses']<=7 or value['credentialErrorResponses']+value['builtinThrottleResponses']!=8 or value['throttleResponses']!=value['builtinThrottleResponses']+692:raise ValueError('Public response counter bounds mismatch')
  markers.append({key:value[key] for key in fields})
 if len(markers)!=1:raise ValueError('Exactly one actual public combined-CDP success record required')
 return markers[0]

def run_fixture(package,fixture,harness,out,session_module,browser_module,executable,exe_sha,private_parent,sanitized,known_secrets):
 package=pathlib.Path(package).resolve(strict=True);fixture=pathlib.Path(fixture).resolve(strict=True);harness=pathlib.Path(harness).resolve(strict=True);out=pathlib.Path(out)
 state=package/'.local-run';vars_path=package/'.dev.vars';private_parent=pathlib.Path(private_parent)
 if not private_parent.is_absolute() or private_parent.resolve(strict=True)!=private_parent:raise ValueError('Exact absolute private RUNNER_TEMP parent required')
 if 'MINIFLARE_CACHE_DIR' in os.environ:raise ValueError('Ambient Miniflare cache override refuses isolated fixture')
 if state.exists() or state.is_symlink() or vars_path.exists() or vars_path.is_symlink():raise ValueError('Existing private auth state refuses isolated fixture')
 state.mkdir(mode=0o700);state_identity=state.lstat();nonce=secrets.token_hex(16)
 persist=state/('traffic-'+nonce);receipt_path=state/('receipt-'+nonce+'.json')
 cache=state/('miniflare-'+nonce);cache.mkdir(mode=0o700);cache_identity=cache.lstat()
 if cache.is_symlink() or cache.resolve(strict=True)!=cache or not stat.S_ISDIR(cache_identity.st_mode) or stat.S_IMODE(cache_identity.st_mode)!=0o700:raise ValueError('Issued Miniflare cache not exact mode0700 directory')
 cache_record={'status':'PRIVATE_MINIFLARE_CACHE_ISSUED_BEFORE_LAUNCH','path':str(cache),'device':cache_identity.st_dev,'inode':cache_identity.st_ino,'mode':cache_identity.st_mode,'originalSessionDrained':False,'browserOriginalSessionDrained':False,'qualification':'Maintained MINIFLARE_CACHE_DIR override only; raw CF cache not archived, complete node_modules equality mandatory'}
 cache_record_path=out/'private-miniflare-cache-custody.json'
 if cache_record_path.exists() or cache_record_path.is_symlink():raise ValueError('Existing cache custody receipt refuses isolated fixture')
 cache_record_path.write_text(json.dumps(cache_record,indent=2)+'\n')
 records=out/'independent-auth-original-session';records.mkdir(mode=0o700,parents=True,exist_ok=False)
 seal=records/'expected-managed-launch.json';seal.write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
 browser_records=out/'original-browser-session';browser_receipt=out/'original-browser-cdp-receipt.json';profile=private_parent/('astra-auth-original-browser-profile-'+nonce)
 session=None;browser=None;primary=None;node_drained=False;browser_drained=False;process=None;exit_code=None;public_success=None
 env=dict(os.environ,CI='true',WRANGLER_SEND_METRICS='false',NO_COLOR='1');env['MINIFLARE_CACHE_DIR']=str(cache)
 for name in env:
  if name.startswith(('CLOUDFLARE_','CF_API_')):raise ValueError('Cloud credentials forbidden in independent local fixture')
 def learn_vars():
  if vars_path.is_file() and not vars_path.is_symlink():
   for line in vars_path.read_text().splitlines():
    if '=' in line:known_secrets.add(line.split('=',1)[1])
 with tempfile.TemporaryFile() as log:
  try:
   browser=browser_module.OriginalHeadlessCdpBrowser(executable=executable,expected_exe_sha=exe_sha,private_parent=private_parent,profile_path=profile,records=browser_records,receipt_path=browser_receipt,sessions=session_module,env=env)
   browser.observe()
   argv=['node',str(fixture),str(package),str(harness),str(persist),str(receipt_path),'combined-cdp',browser.endpoint,str(browser_receipt)]
   process=subprocess.Popen(argv,cwd=package,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
   session=session_module.OriginalSession(process,records)
   node_executable=pathlib.Path('/proc')/str(process.pid)/'exe'
   node_executable=node_executable.resolve(strict=True);node_identity=browser_module.identity(node_executable);node_sha=browser_module.digest(node_executable)
   node_cmdline=(pathlib.Path('/proc')/str(process.pid)/'cmdline').read_bytes().rstrip(b'\0').split(b'\0');browser_cmdline=(pathlib.Path('/proc')/str(browser.process.pid)/'cmdline').read_bytes().rstrip(b'\0').split(b'\0')
   node_cmdline=[value.decode('utf-8','strict') for value in node_cmdline];browser_cmdline=[value.decode('utf-8','strict') for value in browser_cmdline]
   if node_cmdline!=argv or browser_cmdline!=browser.process.args:raise ValueError('Original process argv does not match both direct creator invocations')
   if session.original[0]!=os.getpid() or browser.session.original[0]!=os.getpid():raise ValueError('Both original process parent identities must match actual creator')
   launch={'argv':argv,'actualNodeCmdline':node_cmdline,'actualBrowserCmdline':browser_cmdline,'browserArgv':browser.process.args,'creatorPid':os.getpid(),'launcherPid':process.pid,'launcherTuple':session.original,'nodeExecutable':str(node_executable),'nodeExecutableIdentity':node_identity,'nodeExecutableSha256':node_sha,'originalBrowserPid':browser.process.pid,'issuedEndpoint':browser.endpoint,'issuedBrowserReceipt':str(browser_receipt),'qualification':'Caller directly created both original processes; original Node/Worker and browser sessions remain independently observed and mandatory to drain'}
   (out/'original-dual-launch.json').write_text(json.dumps(launch,indent=2)+'\n')
   deadline=time.monotonic()+900
   while process.poll() is None:
    session.observe();browser.observe();learn_vars()
    try:
     if node_executable!=pathlib.Path('/proc').joinpath(str(process.pid),'exe').resolve(strict=True) or browser_module.identity(node_executable)!=node_identity:raise ValueError('Original Node executable identity changed')
    except FileNotFoundError:
     if process.poll() is None:raise
    if time.monotonic()>deadline:raise TimeoutError('Independent combined-CDP fixture deadline exceeded')
    time.sleep(.05)
   exit_code=process.wait()
   if exit_code:raise RuntimeError('Actual independent combined-CDP fixture failed')
  except BaseException as error:primary=error
  finally:
   errors=[]
   # Both drains are always independently attempted; neither failure suppresses the other.
   try:
    if session is None:raise RuntimeError('Original Node session construction absent; preserve all introduced state')
    session.drain();node_drained=json.loads(seal.read_text()).get('drained') is True
    if not node_drained:raise RuntimeError('Original Node disappearance not proven')
    if browser_module.digest(node_executable)!=node_sha or browser_module.identity(node_executable)!=node_identity:raise RuntimeError('Original Node executable changed')
   except BaseException as error:add_error(errors,error)
   try:
    if browser is None:raise RuntimeError('Original browser construction unavailable; preserve profile/state and its constructor receipt')
    browser.drain();browser_drained=browser.receipt.get('drained') is True
    if not browser_drained:raise RuntimeError('Original browser disappearance not proven')
   except BaseException as error:add_error(errors,error)
   try:
    learn_vars();log.seek(0);length=0;raw_digest=hashlib.sha256();head=b'';tail=b''
    while block:=log.read(65536):
     length+=len(block);raw_digest.update(block)
     if len(head)<262144:head+=block[:262144-len(head)]
     tail=(tail+block)[-262144:]
    if length<=262144:retained=head
    elif length<=524288:retained=head+tail[len(head)+len(tail)-length:]
    else:retained=(head.rsplit(b'\n',1)[0] if b'\n' in head else b'')+b'\n[BOUNDED LOG CUT]\n'+(tail.split(b'\n',1)[1] if b'\n' in tail else b'')
    (out/'independent-auth-sanitized.log').write_text(sanitized(retained))
    (out/'independent-auth-command.json').write_text(json.dumps({'exitCode':exit_code,'rawLogSha256':raw_digest.hexdigest(),'rawLogBytes':length,'rawLogRetained':False,'originalSessionDrained':node_drained,'originalBrowserSessionDrained':browser_drained})+'\n')
    if exit_code==0 and node_drained and browser_drained:public_success=extract_public_success_marker(retained,browser.process.pid)
   except BaseException as error:add_error(errors,error)
   if node_drained and browser_drained and not errors:
    try:
     if state.is_symlink() or (state.lstat().st_dev,state.lstat().st_ino,state.lstat().st_mode)!=(state_identity.st_dev,state_identity.st_ino,state_identity.st_mode):raise ValueError('Introduced state parent identity changed')
     if receipt_path.is_symlink() or not receipt_path.is_file() or stat.S_IMODE(receipt_path.stat().st_mode)!=0o600:raise ValueError('Private introduced-state receipt missing/invalid')
     receipt=json.loads(receipt_path.read_text())
     if receipt.get('version')!=1 or receipt.get('root')!=str(package):raise ValueError('Private receipt scope mismatch')
     for key,path,kind in [('persist',persist,'directory'),('vars',vars_path,'file')]:
      row=receipt.get(key)
      if not isinstance(row,dict) or row.get('path')!=str(path) or path.is_symlink():raise ValueError('Introduced private path/receipt absent')
      actual=path.lstat()
      if (actual.st_dev,actual.st_ino,actual.st_mode)!=(row['device'],row['inode'],row['mode']):raise ValueError('Introduced private state identity changed')
      if kind=='directory' and not stat.S_ISDIR(actual.st_mode):raise ValueError('Introduced persist not directory')
      if kind=='file' and (not stat.S_ISREG(actual.st_mode) or hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']):raise ValueError('Introduced vars changed')
     if cache.is_symlink() or cache.resolve(strict=True)!=cache:raise ValueError('Introduced Miniflare cache path changed')
     actual_cache=cache.lstat()
     if (actual_cache.st_dev,actual_cache.st_ino,actual_cache.st_mode)!=(cache_identity.st_dev,cache_identity.st_ino,cache_identity.st_mode):raise ValueError('Introduced Miniflare cache identity changed')
     if set(state.iterdir())!={persist,receipt_path,cache}:raise ValueError('Unknown introduced private state child; preserve')
     children=list(cache.iterdir())
     if any(p.name!='cf.json' or p.is_symlink() or not stat.S_ISREG(p.lstat().st_mode) for p in children):raise ValueError('Unknown introduced Miniflare cache child; preserve')
     cache_children=[]
     for child in children:
      with child.open('rb') as source:cache_children.append({'name':child.name,'bytes':child.stat().st_size,'sha256':hashlib.file_digest(source,'sha256').hexdigest()})
     cache_record.update({'status':'PRIVATE_MINIFLARE_CACHE_VALIDATED_AFTER_BOTH_ORIGINAL_DRAINS','originalSessionDrained':True,'browserOriginalSessionDrained':True,'children':cache_children});cache_record_path.write_text(json.dumps(cache_record,indent=2)+'\n')
     if not shutil.rmtree.avoids_symlink_attacks:raise ValueError('Private state removal lacks symlink-safe cleanup')
     # Producer validates every expected launch/pending seal and both original identities before first deletion.
     browser.remove_profile(records)
     vars_path.unlink();shutil.rmtree(persist);receipt_path.unlink();shutil.rmtree(cache);state.rmdir()
     cache_record['removedAfterBothOriginalDrains']=True;cache_record_path.write_text(json.dumps(cache_record,indent=2)+'\n')
    except BaseException as error:add_error(errors,error)
   if errors:
    (out/'independent-auth-cleanup-refused.json').write_text(json.dumps({'privateStateCleanupComplete':False,'statePresent':state.exists() or state.is_symlink(),'varsPresent':vars_path.exists() or vars_path.is_symlink(),'profilePresent':profile.exists() or profile.is_symlink(),'originalSessionDrained':node_drained,'originalBrowserSessionDrained':browser_drained,'qualification':'No private vars/database/browser profile archived; sampled owned drain only. Cleanup may be partially complete if a later filesystem deletion failed.'})+'\n')
    if primary is not None:errors.insert(0,primary)
    primary=errors[0] if len(errors)==1 else BaseExceptionGroup('Combined-CDP execution/independent drains/evidence/cleanup failed',errors)
  if primary is not None:raise primary
 if public_success is None:raise ValueError('Actual typed combined-CDP success record absent')
 return {'independentFixturePassed':True,'originalOidcContractPassed':False,'deployment':False,'publicSuccessMarker':public_success}
