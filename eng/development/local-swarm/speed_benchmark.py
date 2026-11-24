#!/usr/bin/env python3
"""Owned, bounded sustained generation benchmark through existing Dulche."""
import datetime,http.client,json,pathlib,socket,time,uuid
ROOT=pathlib.Path('/home/jacob/9to1-swarm-harness-20261124')
MODELS=['qwen3.8-flash-next-unsloth-ud-iq4_xs','qwen3.8-27b-v100']
class LocalHTTP(http.client.HTTPConnection):
 def connect(self):
  self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
  self.sock.settimeout(self.timeout)
  self.sock.connect('/run/user/1000/9to1-dulche/api.sock')
def api(method,path,body=None):
 c=LocalHTTP('localhost',timeout=20)
 try:
  c.request(method,path,body=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json','Idempotency-Key':'swarm-bench-'+uuid.uuid4().hex})
  r=c.getresponse(); d=json.loads(r.read())
  if not 200<=r.status<300: raise RuntimeError(str(r.status)+' '+str(d.get('error')))
  return d
 finally: c.close()
def atom(p,data):
 t=p.with_suffix('.tmp');t.write_text(json.dumps(data,indent=2));t.replace(p)
PROMPT='''Write a complete C# implementation of a bounded asynchronous job scheduler for a desktop development tool, followed by xUnit regression tests. Requirements: stable job identity; configurable concurrency; cancellation of queued and running jobs; no user callback while holding a lock; explicit completed/failed/cancelled states; disposal that awaits running work; test cancellation, exact-once completion, maximum concurrency and ordering. Output actual complete code and tests. Include detailed XML comments. Use enough detail for at least 200 lines. Do not stop early for brevity.'''
messages=[{'role':'system','content':'You are an experienced C# developer. Output code directly. Do not include hidden reasoning.'},{'role':'user','content':PROMPT}]
state={'created':datetime.datetime.now(datetime.timezone.utc).isoformat(),'purpose':'Sustained local completion speed, same exact prompt and max_tokens per model; benchmark output is not a feature change.','max_tokens':1024,'temperature':0,'trials':[]}
path=ROOT/'speed-benchmark.json'
atom(path,state)
for model in MODELS:
 for trial in range(1,3):
  runid=None
  try:
   status=api('GET','/v1/models/'+model)
   if status.get('state')!='loaded': raise RuntimeError('model not loaded: '+str(status.get('state')))
   payload={'model':model,'messages':messages,'max_tokens':1024,'temperature':0,'reasoning_effort':'none','stream':True,'stream_options':{'include_usage':True}}
   started=time.time()
   j=api('POST','/v1/agents',{'model':model,'objective':'9to1 sustained speed benchmark '+str(trial),'steps':[{'type':'generate','payload':payload}]})
   runid=j['id']; state['active']={'model':model,'trial':trial,'run':runid,'started':started};atom(path,state)
   deadline=started+180
   while time.time()<deadline:
    j=api('GET','/v1/agents/'+runid)
    if j['state'] in ('completed','failed','stopped','checkpointed','review'): break
    time.sleep(.15)
   if j['state']!='completed': raise RuntimeError('benchmark '+str(j['state'])+': '+str(j.get('reason')))
   observed=time.time()
   out=j['completed_steps'][-1];tim=out.get('timings') or {};usage=out.get('usage') or {}
   tokens=usage.get('completion_tokens')
   token_source='usage.completion_tokens'
   if not isinstance(tokens,int):
    tokens=tim.get('predicted_n');token_source='timings.predicted_n'
   sec=tim.get('predicted_ms',0)/1000
   row={'model':model,'trial':trial,'run':runid,'started':started,'observed_end':observed,'state':'completed','tokens':tokens,'token_source':token_source,'engine_tps':tim.get('predicted_per_second'),'decode_seconds':sec,'token_count_over_decode_seconds':tokens/sec if isinstance(tokens,int) and sec else None,'request_elapsed_s':out.get('elapsed_s'),'time_to_first_token_s':out.get('ttft_s'),'wall_seconds':observed-started,'output_per_wall_second':tokens/(observed-started) if isinstance(tokens,int) else None,'timings':tim,'usage':usage,'finish_reason':out['choices'][0].get('finish_reason')}
   atom(ROOT/('speed-benchmark-'+runid+'.json'),{'request':payload,'result':out})
   state['trials'].append(row)
   print(json.dumps(row),flush=True)
  except Exception as exc:
   state['trials'].append({'model':model,'trial':trial,'run':runid,'state':'error','error':str(exc)})
   print(model,trial,str(exc),flush=True)
  finally:
   if runid:
    try:
     job=api('GET','/v1/agents/'+runid)
     if job['state'] not in ('completed','failed','stopped','checkpointed','review'):api('POST','/v1/agents/'+runid+'/stop',{})
    except Exception:pass
   state.pop('active',None);atom(path,state)
state['finished']=datetime.datetime.now(datetime.timezone.utc).isoformat();atom(path,state)
