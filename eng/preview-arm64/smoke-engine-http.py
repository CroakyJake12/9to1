#!/usr/bin/env python3
"""Genuine localhost engine/model smoke; never application, laptop or performance acceptance."""
from pathlib import Path
import datetime,hashlib,http.client,json,os,signal,socket,subprocess,sys,time,urllib.error,urllib.request
server=Path(sys.argv[1]).resolve();models=Path(sys.argv[2]);out=Path(sys.argv[3]);models.mkdir(parents=True,exist_ok=True);out.mkdir(parents=True,exist_ok=True)
model_url='https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf';expected='1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b';model=models/'Qwen3-0.6B-Q6_K.gguf'
if model.exists():raise SystemExit('Refuse a pre-existing model target')
h=hashlib.sha256();size=0
with urllib.request.urlopen(model_url,timeout=120) as response,model.open('xb') as f:
 while True:
  b=response.read(1024*1024)
  if not b:break
  f.write(b);h.update(b);size+=len(b)
assert size==495107776 and h.hexdigest()==expected,'Pinned model size/SHA mismatch'
card_url='https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/raw/50968a4468ef4233ed78cd7c3de230dd1d61a56b/README.md';card=urllib.request.urlopen(card_url,timeout=30).read();assert b'license: apache-2.0' in card;(out/'model-card.md').write_bytes(card)
license_url='https://huggingface.co/Qwen/Qwen3-0.6B/raw/c1899de289a04d12100db370d81485cdf75e47ca/LICENSE';license_data=urllib.request.urlopen(license_url,timeout=30).read();assert hashlib.sha256(license_data).hexdigest()=='832dd9e00a68dd83b3c3fb9f5588dad7dcf337a0db50f7d9483f310cd292e92e';(out/'Qwen3-Apache-2.0.txt').write_bytes(license_data)
(out/'model-source.json').write_text(json.dumps({'url':model_url,'repository':'unsloth/Qwen3-0.6B-GGUF','revision':'50968a4468ef4233ed78cd7c3de230dd1d61a56b','bytes':size,'sha256':h.hexdigest(),'license':'Apache-2.0 per pinned quantization model card/base model license','base_license_url':license_url,'base_license_sha256':hashlib.sha256(license_data).hexdigest(),'model_in_artifact':False},indent=2)+'\n')
assert os.uname().machine=='aarch64','Requires actual native ARM runner'
base='http://127.0.0.1:18181'
def get(path,timeout=10):return json.load(urllib.request.urlopen(base+path,timeout=timeout))
def request(payload):
 return urllib.request.urlopen(urllib.request.Request(base+'/v1/chat/completions',data=json.dumps(payload).encode(),headers={'Content-Type':'application/json'}),timeout=120)
def payload(prompt,stream=False,tokens=40):
 return {'model':'Qwen3-0.6B-Q6_K','messages':[{'role':'user','content':prompt+' /no_think'}],'max_tokens':tokens,'stream':stream,'temperature':0,'seed':42,'chat_template_kwargs':{'enable_thinking':False}}
def slots():
 x=get('/slots');a=x if isinstance(x,list) else x.get('slots')
 assert isinstance(a,list) and len(a)==1,('Unexpected real slots payload',x)
 assert all(isinstance(v.get('is_processing'),bool) for v in a),('Slots missing genuine bool state',a)
 return a
def first_text(response,events):
 for line in response:
  text=line.decode().strip()
  if not text.startswith('data:'):continue
  text=text[5:].strip()
  if text=='[DONE]':raise AssertionError('Generation ended before a real text delta')
  e=json.loads(text);events.append(e)
  for c in e.get('choices',[]):
   s=c.get('delta',{}).get('content')
   if isinstance(s,str) and s:return s
 raise AssertionError('No actual streaming text')
command=[str(server),'-m',str(model),'--host','127.0.0.1','--port','18181','--ctx-size','2048','--threads','2','--parallel','1','--n-gpu-layers','0','--slots','--no-webui']
env=dict(os.environ);env['LD_LIBRARY_PATH']=str(server.parent)+(':'+env['LD_LIBRARY_PATH'] if env.get('LD_LIBRARY_PATH') else '')
log=(out/'engine.stdout.log').open('w');proc=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT,env=env);receipt={'started_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'native_architecture':os.uname().machine,'command':command,'model_sha256':expected,'status':'RUNNING','application_acceptance':'NOT RUN','laptop_acceptance':'NOT RUN','performance':'NOT BENCHMARKED'};failure=None
try:
 deadline=time.monotonic()+180
 while True:
  if proc.poll() is not None:raise RuntimeError('Engine exited while loading: '+str(proc.returncode))
  try:
   health=get('/health')
   if health.get('status')=='ok':break
  except (urllib.error.URLError,TimeoutError,ConnectionError):pass
  if time.monotonic()>deadline:raise TimeoutError('Real model did not load within180s')
  time.sleep(.25)
 receipt['health']=health
 with request(payload('Explain a variable in computer science in one sentence.')) as response:answer=json.load(response)
 content=answer['choices'][0]['message'].get('content');assert isinstance(content,str) and content.strip(),'No actual nonstream answer';(out/'prompt-response.json').write_text(json.dumps(answer,indent=2)+'\n');receipt['prompt']='PASS_REAL_HTTP_TEXT'
 chunks=[];stream_text=[];done=False
 with request(payload('Explain an algorithm in one short sentence.',True,48)) as response:
  for line in response:
   text=line.decode().strip()
   if not text.startswith('data:'):continue
   text=text[5:].strip()
   if text=='[DONE]':done=True;break
   chunk=json.loads(text);chunks.append(chunk)
   for c in chunk.get('choices',[]):
    text=c.get('delta',{}).get('content')
    if isinstance(text,str):stream_text.append(text)
 assert done and ''.join(stream_text).strip(),'No complete real SSE text';(out/'stream-response.json').write_text(json.dumps({'events':chunks,'done':done,'text':''.join(stream_text)},indent=2)+'\n');receipt['stream']='PASS_REAL_SSE_TEXT_AND_DONE'
 # Start a longer real stream, observe an actual active slot, close its socket,
 # require the SAME engine to release the slot and accept a new real request.
 cancel_events=[];response=request(payload('Keep counting integers in order, with no explanation, until the output limit.',True,1536));first=first_text(response,cancel_events);before=slots();assert any(s['is_processing'] for s in before),'Cancellation target already inactive';closed_at=time.monotonic();response.close()
 deadline=closed_at+20
 while True:
  after=slots()
  if all(not s['is_processing'] for s in after):break
  if time.monotonic()>deadline:raise TimeoutError('Closed real stream did not release its processing slot')
  time.sleep(.1)
 with request(payload('Reply with the single word READY.',False,12)) as response:followup=json.load(response)
 assert followup['choices'][0]['message'].get('content','').strip(),'Engine did not recover after cancellation'
 cancel={'first_real_text':first,'events_before_close':cancel_events,'slots_before_socket_close':before,'slots_after_socket_close':after,'release_seconds':time.monotonic()-closed_at,'followup':followup,'status':'PASS_REAL_SOCKET_CANCELLATION_SLOT_RELEASE_AND_FOLLOWUP'};(out/'cancel-response.json').write_text(json.dumps(cancel,indent=2)+'\n');receipt['cancellation']=cancel['status'];receipt['status']='PASS_NATIVE_ENGINE_MODEL_HTTP_PROMPT_STREAM_CANCEL_ONLY'
except BaseException as e:failure=e;receipt['status']='FAIL';receipt['error']=repr(e)
finally:
 if proc.poll() is None:
  proc.send_signal(signal.SIGINT)
  try:proc.wait(timeout=30)
  except subprocess.TimeoutExpired:proc.kill();proc.wait();receipt['shutdown_failure']='Did not stop onSIGINT';receipt['status']='FAIL'
 log.close();receipt['engine_exit_code']=proc.returncode;receipt['ended_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();(out/'http-smoke-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps(receipt,indent=2))
if failure:raise failure
if receipt['status']!='PASS_NATIVE_ENGINE_MODEL_HTTP_PROMPT_STREAM_CANCEL_ONLY':raise SystemExit(1)
