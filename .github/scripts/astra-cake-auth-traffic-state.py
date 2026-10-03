"""Explicit traffic-only synthetic auth fixture primitive; original OIDC contract remains separately required."""
import pathlib,os,json,hashlib,subprocess,time,tempfile,secrets,stat,shutil

def extract_public_success_marker(raw):
 # Exact synthetic fixture09 public traffic record only. No arbitrary log string or secret is exempted from redaction.
 fields=['result','phase','browserExecuted','directRequests','durableWindowAttempts','credentialErrorResponses','builtinThrottleResponses','durableThrottleResponses','throttleResponses','durableLockSeconds','sameStoreRestart']
 scope='isolated local Wrangler/D1 direct API only; genuine browser still mandatory in combined phase; source-backed pre-handler ordering, no credential-call counter/elapsed CPU/ingress billing/universal quota/cloud/OIDC acceptance'
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
  if not isinstance(value,dict) or set(value)!=set(fields+['scope']) or value['scope']!=scope:raise ValueError('Unexpected public success record fields/scope')
  for key,expected in {'result':'passed','phase':'traffic-only','browserExecuted':False,'directRequests':700,'durableWindowAttempts':8,'durableThrottleResponses':692,'durableLockSeconds':1800,'sameStoreRestart':True}.items():
   if type(value[key]) is not type(expected) or value[key]!=expected:raise ValueError('Public success record exact value/type mismatch')
  for key in ['credentialErrorResponses','builtinThrottleResponses','throttleResponses']:
   if type(value[key]) is not int:raise ValueError('Public response counter requires an actual integer')
  if not 1<=value['credentialErrorResponses']<=8 or not 0<=value['builtinThrottleResponses']<=7 or value['credentialErrorResponses']+value['builtinThrottleResponses']!=8 or value['throttleResponses']!=value['builtinThrottleResponses']+692:raise ValueError('Public success record response bounds mismatch')
  markers.append({key:value[key] for key in fields})
 if len(markers)!=1:raise ValueError('Exactly one actual public traffic success record required')
 return markers[0]

def run_fixture(package,fixture,harness,out,session_module,sanitized,known_secrets):
 package=pathlib.Path(package).resolve(strict=True);fixture=pathlib.Path(fixture).resolve(strict=True);harness=pathlib.Path(harness).resolve(strict=True);out=pathlib.Path(out)
 state=package/'.local-run';vars_path=package/'.dev.vars'
 if 'MINIFLARE_CACHE_DIR' in os.environ:raise ValueError('Ambient Miniflare cache override refuses isolated fixture')
 if state.exists() or state.is_symlink() or vars_path.exists() or vars_path.is_symlink():raise ValueError('Existing private auth state refuses isolated fixture')
 state.mkdir(mode=0o700);state_identity=state.stat();nonce=secrets.token_hex(16)
 persist=state/('traffic-'+nonce);receipt_path=state/('receipt-'+nonce+'.json')
 cache=state/('miniflare-'+nonce);cache.mkdir(mode=0o700);cache_identity=cache.lstat()
 if cache.is_symlink() or cache.resolve(strict=True)!=cache or not stat.S_ISDIR(cache_identity.st_mode) or stat.S_IMODE(cache_identity.st_mode)!=0o700:raise ValueError('Issued Miniflare cache not exact mode0700 directory')
 cache_record={'status':'PRIVATE_MINIFLARE_CACHE_ISSUED_BEFORE_LAUNCH','path':str(cache),'device':cache_identity.st_dev,'inode':cache_identity.st_ino,'mode':cache_identity.st_mode,'originalSessionDrained':False,'qualification':'Maintained MINIFLARE_CACHE_DIR override only; raw CF cache not archived, complete node_modules equality remains mandatory'}
 cache_record_path=out/'private-miniflare-cache-custody.json'
 if cache_record_path.exists() or cache_record_path.is_symlink():raise ValueError('Existing Miniflare cache custody receipt refuses isolated fixture')
 cache_record_path.write_text(json.dumps(cache_record,indent=2)+'\n')
 records=out/'independent-auth-original-session';records.mkdir(parents=True,exist_ok=False)
 seal=records/'expected-managed-launch.json';seal.write_text(json.dumps({'expectedManagedLaunch':True,'drained':False})+'\n')
 session=None;primary=None;drained=False;process=None;exit_code=None;public_success=None
 env=dict(os.environ,CI='true',WRANGLER_SEND_METRICS='false',NO_COLOR='1')
 env['MINIFLARE_CACHE_DIR']=str(cache)
 for name in env:
  if name.startswith(('CLOUDFLARE_','CF_API_')):raise ValueError('Cloud credentials forbidden in independent local fixture')
 with tempfile.TemporaryFile() as log:
  try:
   process=subprocess.Popen(['node',str(fixture),str(package),str(harness),str(persist),str(receipt_path),'traffic-only'],cwd=package,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
   session=session_module.OriginalSession(process,records);deadline=time.monotonic()+900
   while process.poll() is None:
    session.observe()
    if vars_path.is_file() and not vars_path.is_symlink():
     for line in vars_path.read_text().splitlines():
      if '=' in line:known_secrets.add(line.split('=',1)[1])
    if time.monotonic()>deadline:raise TimeoutError('Independent auth fixture deadline exceeded')
    time.sleep(.1)
   exit_code=process.wait()
   if exit_code:raise RuntimeError('Actual independent auth fixture failed')
  except BaseException as error:
   primary=error
  finally:
   cleanup_error=None
   try:
    if session is None:raise RuntimeError('Private original session construction absent; preserve state')
    session.drain();drained=json.loads(seal.read_text()).get('drained') is True
    if not drained:raise RuntimeError('Original session disappearance not proven')
   except BaseException as error:cleanup_error=error
   try:
    if vars_path.is_file() and not vars_path.is_symlink():
     for line in vars_path.read_text().splitlines():
      if '=' in line:known_secrets.add(line.split('=',1)[1])
    log.seek(0);length=0;raw_digest=hashlib.sha256();head=b'';tail=b''
    while block:=log.read(65536):
     length+=len(block);raw_digest.update(block)
     if len(head)<262144:head+=block[:262144-len(head)]
     tail=(tail+block)[-262144:]
    if length<=262144:retained=head
    elif length<=524288:retained=head+tail[len(head)+len(tail)-length:]
    else:retained=(head.rsplit(b'\n',1)[0] if b'\n' in head else b'')+b'\n[BOUNDED LOG CUT]\n'+(tail.split(b'\n',1)[1] if b'\n' in tail else b'')
    (out/'independent-auth-sanitized.log').write_text(sanitized(retained))
    (out/'independent-auth-command.json').write_text(json.dumps({'exitCode':exit_code,'rawLogSha256':raw_digest.hexdigest(),'rawLogBytes':length,'rawLogRetained':False,'originalSessionDrained':drained})+'\n')
    if exit_code==0 and drained:public_success=extract_public_success_marker(retained)
   except BaseException as error:
    cleanup_error=cleanup_error or error
   if drained and cleanup_error is None:
    try:
     if state.is_symlink() or (state.stat().st_dev,state.stat().st_ino)!=(state_identity.st_dev,state_identity.st_ino):raise ValueError('Introduced state parent identity changed')
     if receipt_path.is_symlink() or not receipt_path.is_file() or stat.S_IMODE(receipt_path.stat().st_mode)!=0o600:raise ValueError('Private introduced-state receipt missing or invalid')
     receipt=json.loads(receipt_path.read_text())
     if receipt.get('version')!=1 or receipt.get('root')!=str(package):raise ValueError('Private receipt scope mismatch')
     for key,path,kind in [('persist',persist,'directory'),('vars',vars_path,'file')]:
      row=receipt.get(key)
      if not isinstance(row,dict) or row.get('path')!=str(path) or path.is_symlink():raise ValueError('Introduced private state path/receipt absent')
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
     cache_record.update({'status':'PRIVATE_MINIFLARE_CACHE_VALIDATED_AFTER_ORIGINAL_DRAIN','originalSessionDrained':True,'children':cache_children})
     cache_record_path.write_text(json.dumps(cache_record,indent=2)+'\n')
     if not shutil.rmtree.avoids_symlink_attacks:raise ValueError('Private state removal lacks symlink-safe directory cleanup')
     vars_path.unlink();shutil.rmtree(persist);receipt_path.unlink();shutil.rmtree(cache);state.rmdir()
     cache_record['removedAfterOriginalDrain']=True;cache_record_path.write_text(json.dumps(cache_record,indent=2)+'\n')
    except BaseException as error:cleanup_error=error
   if cleanup_error is not None:
    (out/'independent-auth-cleanup-refused.json').write_text(json.dumps({'privateStateRetained':True,'originalSessionDrained':drained,'qualification':'No private receipt/database/vars archived; sampled owned drain only'})+'\n')
    if primary is None:primary=cleanup_error
    else:primary=BaseExceptionGroup('Actual auth fixture and cleanup both failed',[primary,cleanup_error])
  if primary is not None:raise primary
 if public_success is None:raise ValueError('Actual typed public success record absent')
 return {'independentFixturePassed':True,'originalOidcContractPassed':False,'deployment':False,'publicSuccessMarker':public_success}
