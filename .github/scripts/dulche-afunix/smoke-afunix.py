#!/usr/bin/env python3
"""Exact artifact AF_UNIX engine probe; never app, laptop or provider acceptance."""
from pathlib import Path
import argparse,datetime,hashlib,http.client,json,os,platform,signal,socket,stat,struct,subprocess,tarfile,tempfile,time,urllib.request

P=Path(__file__).resolve().parent
LOCK=json.loads((P/'pins.json').read_text())
now=lambda:datetime.datetime.now(datetime.timezone.utc).isoformat()
def sha(p):
 h=hashlib.sha256()
 with Path(p).open('rb') as f:
  for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
 return h.hexdigest()
def save(p,x):Path(p).write_text(json.dumps(x,indent=2)+'\n')
ap=argparse.ArgumentParser();ap.add_argument('--artifact',type=Path);ap.add_argument('--output',type=Path);ap.add_argument('--check-only',action='store_true');a=ap.parse_args()
assert LOCK['engine']['source_commit']=='5266f24da75dc449bd56cbed7addb9c8e4a6a73e'
assert LOCK['model']['sha256']=='1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b'
if a.check_only:
 if a.artifact:assert sha(a.artifact/LOCK['engine']['archive'])==LOCK['engine']['archive_sha256']
 print(json.dumps({'status':'PASS_READ_ONLY_PACKET_GUARD','engine_execution':'NOT RUN','network':'NOT RUN'}));raise SystemExit(0)
if not a.artifact or not a.output or a.output.exists():raise SystemExit('Require --artifact existing-directory --output NEW-directory')
assert platform.machine()=='aarch64' and os.name=='posix','Native ARM Linux required'
assert subprocess.check_output(['dpkg','--print-architecture'],text=True).strip()=='arm64'
out=a.output.resolve();out.mkdir(parents=True);artifact=a.artifact.resolve();archive=artifact/LOCK['engine']['archive'];assert sha(archive)==LOCK['engine']['archive_sha256']
engine=out/'engine';engine.mkdir()
with tarfile.open(archive) as t:
 for member in t.getmembers():
  target=(engine/member.name).resolve()
  assert target.is_relative_to(engine) and (member.isfile() or member.isdir()),'Unsafe archive member'
 t.extractall(engine,filter='data')
server=engine/'bin/llama-server';assert sha(server)==LOCK['engine']['server_sha256'] and server.stat().st_size==14234480
assert (engine/'SOURCE-COMMIT').read_text().strip()==LOCK['engine']['source_commit']
model=out/'Qwen3-0.6B-Q6_K.gguf';source=LOCK['model']
with urllib.request.urlopen(source['url'],timeout=120) as response,model.open('xb') as f:
 while True:
  chunk=response.read(1024*1024)
  if not chunk:break
  f.write(chunk)
assert model.stat().st_size==source['bytes'] and sha(model)==source['sha256']
license_bytes=urllib.request.urlopen(source['base_license_url'],timeout=30).read();assert hashlib.sha256(license_bytes).hexdigest()==source['base_license_sha256'];(out/'Qwen3-Apache-2.0.txt').write_bytes(license_bytes)
save(out/'model-source.json',source)
runtime=Path(tempfile.mkdtemp(prefix='d15-afunix-'));runtime.chmod(0o700);sock=runtime/'inference.sock';assert len(os.fsencode(sock))<=100 and str(sock).endswith('.sock')
alias='qwen3-06b-q6k'
command=[str(server),'--model',str(model),'--alias',alias,'--host',str(sock),'--no-ui','--no-slots','--log-disable','--offline','--parallel','1','--ctx-size','32768','--cache-ram','0','--jinja','--reasoning','off']
receipt={'started_at':now(),'status':'RUNNING','transport':'AF_UNIX_ONLY','configuration_scope':'Diagnostic bundled Qwen profile; app runtime update pending. Same32-token probe and all assertions retained','command':command,'artifact':LOCK['artifact'],'engine':LOCK['engine'],'model':source,'platform':platform.uname()._asdict(),'runtime_directory':str(runtime),'socket_path':str(sock),'stages':[],'application_acceptance':'NOT RUN','laptop_acceptance':'NOT RUN','model_in_artifact':False}
proc=None;failure=None;log=(out/'engine.stdout.log').open('w');peer_records=[]
class UnixHTTP(http.client.HTTPConnection):
 def __init__(self,timeout):super().__init__('llamacpp.local',timeout=timeout)
 def connect(self):
  self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM);self.sock.settimeout(self.timeout)
  try:
   self.sock.connect(str(sock));pid,uid,gid=struct.unpack('3i',self.sock.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))
   assert proc is not None and pid==proc.pid and uid==os.getuid(),'Unexpected kernel peer'
   assert sha(Path('/proc')/str(pid)/'exe')==LOCK['engine']['server_sha256'],'Unexpected peer executable'
   record={'pid':pid,'uid':uid,'gid':gid,'at':now()}
   if not peer_records:peer_records.append(record)
  except BaseException:self.sock.close();raise
def request(path,payload=None,timeout=120):
 c=UnixHTTP(timeout);body=None if payload is None else json.dumps(payload).encode()
 try:
  c.request('GET' if payload is None else 'POST',path,body,{'Content-Type':'application/json','Connection':'close'})
  r=c.getresponse()
  if r.status!=200:
   detail=r.read(65536).decode(errors='replace');raise RuntimeError(f'HTTP {r.status} on Unix socket {path}: {detail}')
  return c,r
 except BaseException:c.close();raise
def get(path,timeout=5):
 c,r=request(path,timeout=timeout)
 try:return json.loads(r.read())
 finally:r.close();c.close()
def complete(payload,name):
 c,r=request('/v1/chat/completions',payload)
 try:data=json.loads(r.read());save(out/(name+'.json'),data);return data
 finally:r.close();c.close()
def stage(name,body):
 s={'name':name,'started_at':now()};receipt['stages'].append(s)
 try:result=body();s.update(status='PASS',ended_at=now());return result
 except BaseException as e:s.update(status='FAIL',ended_at=now(),error=repr(e));raise
def ready():
 started=time.monotonic();deadline=started+60;attempts=[]
 while True:
  if proc.poll() is not None:raise RuntimeError('Actual child exited before readiness: '+str(proc.returncode))
  try:
   assert stat.S_ISSOCK(sock.stat().st_mode)
   health=get('/health',min(3,max(.1,deadline-time.monotonic())));assert health.get('status')=='ok'
   catalogue=get('/v1/models');models=catalogue['data'];assert any(m.get('id')==alias and m.get('meta',{}).get('n_params',0)>0 and m.get('meta',{}).get('n_vocab',0)>0 for m in models)
   props=get('/props');assert props['model_alias']==alias and Path(props['model_path']).resolve()==model
   assert props['default_generation_settings']['n_ctx']>0 and sha(model)==source['sha256']
   save(out/'readiness.json',{'health':health,'models':catalogue,'props':props,'seconds':time.monotonic()-started,'failed_attempts':attempts,'kernel_peers':peer_records});return
  except (OSError,TimeoutError,RuntimeError,http.client.HTTPException,AssertionError) as e:
   attempts.append({'seconds':time.monotonic()-started,'error':repr(e)})
   if time.monotonic()>=deadline:raise TimeoutError('No actual same-model AF_UNIX readiness within60s') from e
   time.sleep(min(.15,max(0,deadline-time.monotonic())))
def text_probe():
 data=complete({'model':alias,'messages':[{'role':'user','content':'Reply with a short greeting.'}],'stream':False,'max_tokens':32,'temperature':0},'text-response')
 assert data['choices'][0]['message'].get('content','').strip(),'Product-equivalent text probe has no actual text'
def stream_probe():
 payload={'model':alias,'messages':[{'role':'user','content':'Reply with a short greeting.'}],'stream':True,'max_tokens':32,'temperature':0}
 c,r=request('/v1/chat/completions',payload);events=[];texts=[];done=False;deadline=time.monotonic()+120
 try:
  while True:
   assert time.monotonic()<deadline,'Streaming wall time exceeded120s'
   line=r.readline()
   if not line:break
   line=line.decode().strip()
   if not line.startswith('data:'):continue
   data=line[5:].strip()
   if data=='[DONE]':done=True;break
   item=json.loads(data);events.append(item)
   for choice in item.get('choices',[]):
    value=choice.get('delta',{}).get('content')
    if isinstance(value,str):texts.append(value)
 finally:r.close();c.close();save(out/'stream-response.json',{'events':events,'done':done,'text':''.join(texts)})
 assert done and ''.join(texts).strip(),'Product-equivalent stream probe has no complete actual text'
def tool_probe():
 data=complete({'model':alias,'messages':[{'role':'user','content':'Call echo_probe with marker equal to local_probe. Do not invent a result.'}],'tools':[{'type':'function','function':{'name':'echo_probe','description':'Return a marker; this is a read-only protocol probe.','parameters':{'type':'object','properties':{'marker':{'type':'string'}},'required':['marker']}}}],'tool_choice':'auto','stream':False,'max_tokens':128,'temperature':0},'tool-response')
 calls=data['choices'][0]['message'].get('tool_calls',[]);assert len(calls)==1
 function=calls[0]['function'];assert function['name']=='echo_probe' and json.loads(function['arguments'])=={'marker':'local_probe'},'Product-equivalent automatic tool capability probe failed'
try:
 proc=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT)
 receipt['child_pid']=proc.pid
 stage('same-kernel-peer-socket-health-model-ready',ready)
 stage('actual-text-product-probe',text_probe)
 stage('actual-SSE-product-probe',stream_probe)
 stage('actual-auto-tool-product-probe',tool_probe)
 receipt['status']='PASS_NATIVE_ARM_AF_UNIX_ENGINE_PRODUCT_PROBES_ONLY'
except BaseException as e:failure=e;receipt.update(status='FAIL',error=repr(e))
finally:
 if proc is not None:
  if proc.poll() is None:
   proc.send_signal(signal.SIGINT)
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:
    proc.kill();proc.wait(timeout=10);receipt.update(status='FAIL',shutdown_failure='Actual child required SIGKILL after30s SIGINT')
  receipt['child_exit_code']=proc.returncode;receipt['actual_child_joined']=proc.poll() is not None
  if proc.returncode not in (0,-signal.SIGINT):receipt.update(status='FAIL',shutdown_exit_failure=proc.returncode)
 log.close();receipt['ended_at']=now();receipt['kernel_peers']=peer_records
 receipt['socket_remains_after_exit']=sock.exists()
 if sock.exists():sock.unlink()
 runtime.rmdir()
 save(out/'afunix-receipt.json',receipt)
 manifest={str(p.relative_to(out)):sha(p) for p in sorted(out.rglob('*')) if p.is_file() and p!=model and 'engine' not in p.relative_to(out).parts}
 save(out/'evidence-sha256.json',manifest)
print(json.dumps(receipt,indent=2))
if failure:raise failure
if receipt['status']!='PASS_NATIVE_ARM_AF_UNIX_ENGINE_PRODUCT_PROBES_ONLY':raise SystemExit(1)
