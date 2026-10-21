"""Acceptance-only reproduction using the exact installed engine/model and baseline flags.
Derived from genuine prior .github/scripts/dulche-afunix/smoke-afunix.py at5e74f014.
No App, SDK, model download, source modification or socket unlink before/after either run.
"""
from pathlib import Path
import argparse,datetime,hashlib,http.client,json,os,platform,signal,socket,stat,struct,subprocess,tempfile,time,traceback
HEAD='ab7bc6b8b2d72a0c97d3b56423d93e0f4f5a466a'
ENGINE=Path('/usr/lib/haven/llama.cpp/llama-server');ENGINE_SHA='edcd91e1a50804fbf2f82245534a5bc60161c7d9a5356c498b07f6157d5ecdf5'
MODEL_SHA='1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b';MODEL=Path('/usr/share/haven/default-models')/(MODEL_SHA+'.gguf');ALIAS='qwen3-06b-q6k'
parser=argparse.ArgumentParser();parser.add_argument('--diagnostic-logging',action='store_true');args=parser.parse_args()
OUT=Path(os.environ['GITHUB_WORKSPACE'])/'evidence';OUT.mkdir(exist_ok=True)
now=lambda:datetime.datetime.now(datetime.timezone.utc).isoformat()
def sha(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as f:
  for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
 return h.hexdigest()
assert os.geteuid()!=0 and platform.machine()=='aarch64' and os.name=='posix'
assert sha(ENGINE)==ENGINE_SHA and sha(MODEL)==MODEL_SHA and MODEL.stat().st_size==495107776
provenance=json.loads(Path('/usr/share/doc/9to1-preview/source-and-build.json').read_text());assert provenance['source_head']==HEAD
runtime=Path(tempfile.mkdtemp(prefix='h9r-',dir='/tmp'));runtime.chmod(0o700);SOCK=runtime/'inference.sock';assert len(os.fsencode(SOCK))<=100 and not SOCK.exists()
COMMAND=[str(ENGINE),'--model',str(MODEL),'--alias',ALIAS,'--host',str(SOCK),'--no-ui','--no-slots','--log-disable','--offline','--parallel','1','--ctx-size','32768','--cache-ram','0','--jinja','--reasoning','off']
receipt={'started_at':now(),'package_source_head':HEAD,'tools_head':os.environ['GITHUB_SHA'],'engine_sha256':ENGINE_SHA,'model_sha256':MODEL_SHA,'model_bytes':MODEL.stat().st_size,'command':COMMAND,'runtime':str(runtime),'socket_path':str(SOCK),'runs':[],'status':'RUNNING','application_acceptance':'NOT RUN','owner_laptop':'NOT RUN','socket_unlinked_by_probe':False,'product_flags_changed':False,'path_comparison':'Both launch commands identical; matches observed App flags except same-hash installed seed model path and fresh short socket path'};proc=None;root_ticks=None

def save():(OUT/'engine-double-start-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
def socket_info():
 try:
  s=SOCK.lstat();return {'exists':True,'device':s.st_dev,'inode':s.st_ino,'mode':s.st_mode,'is_socket':stat.S_ISSOCK(s.st_mode),'uid':s.st_uid,'gid':s.st_gid}
 except FileNotFoundError:return {'exists':False}
def process_ticks(pid):return int((Path('/proc')/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[19])
def peer_get(path):
 class UnixHTTP(http.client.HTTPConnection):
  def connect(self):
   self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM);self.sock.settimeout(2)
   try:
    self.sock.connect(str(SOCK));pid,uid,gid=struct.unpack('3i',self.sock.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))
    assert proc.poll() is None and pid==proc.pid and uid==os.geteuid() and gid==os.getegid(),'Unexpected physical peer'
    assert process_ticks(pid)==root_ticks and sha(Path('/proc')/str(pid)/'exe')==ENGINE_SHA,'Peer identity changed'
    self.peer={'pid':pid,'uid':uid,'gid':gid,'start_ticks':root_ticks,'observed_at':now()}
   except BaseException:self.sock.close();raise
 c=UnixHTTP('llamacpp.local',timeout=2)
 try:
  c.request('GET',path,headers={'Connection':'close'});r=c.getresponse();body=r.read(65536).decode(errors='replace');return {'status':r.status,'body':body,'peer':c.peer}
 finally:c.close()
def observe_live(record,bound=60):
 deadline=time.monotonic()+bound
 while time.monotonic()<deadline:
  if proc.poll() is not None:
   record.update(terminal_observed_at=now(),raw_exit_code=proc.returncode,socket_at_raw_exit_before_any_cleanup=socket_info());save();return 'EXITED'
  try:
   h=peer_get('/health');record['health_attempts'].append({'at':now(),**h})
   if h['status']==200 and json.loads(h['body']).get('status')=='ok':
    models=peer_get('/v1/models');props=peer_get('/props');assert models['status']==200 and props['status']==200
    catalogue=json.loads(models['body']);settings=json.loads(props['body'])
    assert any(m.get('id')==ALIAS and m.get('meta',{}).get('n_params',0)>0 and m.get('meta',{}).get('n_vocab',0)>0 for m in catalogue['data'])
    assert settings['model_alias']==ALIAS and Path(settings['model_path']).resolve()==MODEL and settings['default_generation_settings']['n_ctx']==32768
    record.update(ready_at=now(),same_peer_live_models=models,same_peer_live_props=props,socket_ready=socket_info());save();return 'READY'
  except (OSError,ValueError,AssertionError,http.client.HTTPException) as e:record['health_attempts'].append({'at':now(),'error':repr(e)})
  save();time.sleep(.15)
 record.update(readiness_bound_expired_at=now(),raw_exit_code=proc.poll(),socket_before_any_cleanup=socket_info());save();return 'BOUND_EXPIRED'
def normal_stop(record):
 assert proc.poll() is None
 assert process_ticks(proc.pid)==root_ticks
 record['normal_sigint_requested_at']=now();proc.send_signal(signal.SIGINT);record['normal_exit_code']=proc.wait(timeout=30);record['joined_at']=now();record['socket_after_normal_exit']=socket_info();save();assert record['normal_exit_code']==0,'Owned engine did not exit normally'
def start(label,command):
 global proc,root_ticks
 log=(OUT/(label+'.stdout.log')).open('w');env=dict(os.environ);env.pop('GH_TOKEN',None);env.pop('GITHUB_TOKEN',None)
 before_socket=socket_info();proc=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT,env=env,start_new_session=True);root_ticks=process_ticks(proc.pid)
 record={'label':label,'started_at':now(),'pid':proc.pid,'start_ticks':root_ticks,'command':command,'log':log.name,'socket_before':before_socket,'health_attempts':[]};receipt['runs'].append(record);save();return record,log
try:
 first,log=start('first-exact-launch',COMMAND)
 try:assert observe_live(first)=='READY';normal_stop(first)
 finally:log.close()
 retained=socket_info();receipt['retained_socket_between_identical_launches']=retained;assert retained.get('is_socket') and retained['uid']==os.geteuid(),'No owned retained socket to reproduce'
 second,log=start('second-identical-launch-no-unlink',COMMAND)
 try:
  result=observe_live(second)
  if result=='EXITED':
   assert second['raw_exit_code']!=0,'Second exited0 without readiness';receipt['status']='REPRODUCED_SECOND_START_EXIT_FAILURE_WITH_RETAINED_SOCKET'
  elif result=='READY':
   normal_stop(second);receipt['status']='SECOND_IDENTICAL_START_READY_CAUSE_NOT_REPRODUCED'
  else:raise TimeoutError('Second exact launch neither became ready nor exited within60s')
 finally:log.close()
 if args.diagnostic_logging and receipt['status']=='REPRODUCED_SECOND_START_EXIT_FAILURE_WITH_RETAINED_SOCKET':
  diagnostic=[x for x in COMMAND if x!='--log-disable'];record,log=start('third-explicit-logging-only-diagnostic',diagnostic)
  record['comparison_scope']='One flag removed (--log-disable); diagnostic evidence only, never product acceptance'
  try:
   outcome=observe_live(record,30);record['diagnostic_outcome']=outcome
   if outcome=='READY':normal_stop(record)
   elif outcome=='BOUND_EXPIRED':raise TimeoutError('Logging-only comparison exceeded30s')
  finally:log.close()
except BaseException as error:receipt.update(status='FAIL_DIAGNOSTIC_EXECUTION',error=repr(error),traceback=traceback.format_exc())
finally:
 if proc is not None and proc.poll() is None:
  receipt['failure_cleanup_only']=True
  try:
   assert process_ticks(proc.pid)==root_ticks;proc.send_signal(signal.SIGINT);proc.wait(timeout=30)
  except subprocess.TimeoutExpired:
   assert process_ticks(proc.pid)==root_ticks;proc.kill();proc.wait(timeout=10);receipt['forced_kill_required']=True
  except BaseException as error:receipt['cleanup_error']=repr(error)
 receipt.update(ended_at=now(),final_socket=socket_info());save()
print(json.dumps({'status':receipt['status'],'raw_exit_codes':[r.get('raw_exit_code',r.get('normal_exit_code')) for r in receipt['runs']],'receipt':'evidence/engine-double-start-receipt.json'}))
if receipt['status']=='FAIL_DIAGNOSTIC_EXECUTION' or receipt.get('failure_cleanup_only') or receipt.get('cleanup_error'):raise SystemExit(1)
