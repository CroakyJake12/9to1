#!/usr/bin/env python3
"""Compare request-scoped helper affinity, then bounded dual-GPU throughput."""
import concurrent.futures,datetime,json,pathlib,time,threading
ROOT=pathlib.Path('/home/jacob/9to1-swarm-harness-20261124')
source=(ROOT/'speed_benchmark.py').read_text()
ns={};exec(compile(source[:source.index("state={'created'")],str(ROOT/'speed_benchmark.py'),'exec'),ns)
api=ns['api'];atom=ns['atom'];messages=ns['messages']
FLASH=ns['MODELS'][0];V100=ns['MODELS'][1]
path=ROOT/'speed-tuning.json'
result={'created':datetime.datetime.now(datetime.timezone.utc).isoformat(),'trials':[]}
lock=threading.Lock()
def save():
 with lock:atom(path,result)
def run(model,label,limit=1024,tune=None):
 started=time.time();rid=None
 try:
  resources=api('GET','/v1/resources')
  if resources.get('host_available_mib',0)<8192:raise RuntimeError('host memory below 8GiB floor')
  if time.time()-resources.get('sample_at',0)>15:raise RuntimeError('stale resource telemetry')
  payload={'model':model,'messages':messages,'max_tokens':limit,'temperature':0,'reasoning_effort':'none','stream':True,'stream_options':{'include_usage':True}}
  if tune is not None:payload['strata_tune']={'aux_cpus':tune}
  job=api('POST','/v1/agents',{'model':model,'objective':'9to1 speed verification '+label,'steps':[{'type':'generate','payload':payload}]})
  rid=job['id']
  print(json.dumps({'label':label,'model':model,'run':rid,'state':'started'}),flush=True)
  with lock:result.setdefault('dispatches',[]).append({'label':label,'model':model,'run':rid,'started':started});atom(path,result)
  while time.time()-started<180:
   job=api('GET','/v1/agents/'+rid)
   if job['state'] in ('completed','failed','stopped','review','checkpointed'):break
   time.sleep(.1)
  if job['state']!='completed':raise RuntimeError('run '+job['state']+' '+str(job.get('reason')))
  observed=time.time();out=job['completed_steps'][-1];tm=out.get('timings') or {};usage=out.get('usage') or {}
  tokens=usage.get('completion_tokens');token_source='usage.completion_tokens'
  if not isinstance(tokens,int):tokens=tm.get('predicted_n');token_source='timings.predicted_n'
  row={'label':label,'model':model,'run':rid,'state':'completed','max_tokens':limit,'aux_cpus':tune,'started':started,'observed_end':observed,'tokens':tokens,'token_source':token_source,'engine_tps':tm.get('predicted_per_second'),'decode_seconds':tm.get('predicted_ms',0)/1000,'wall_seconds':observed-started,'output_per_wall_second':tokens/(observed-started) if isinstance(tokens,int) else None,'request_elapsed_s':out.get('elapsed_s'),'time_to_first_token_s':out.get('ttft_s'),'timings':tm,'usage':usage,'finish_reason':out['choices'][0].get('finish_reason')}
  atom(ROOT/('speed-probe-'+rid+'.json'),{'request':payload,'result':out})
 except Exception as exc:
  row={'label':label,'model':model,'run':rid,'state':'error','error':str(exc)}
 finally:
  if rid:
   try:
    j=api('GET','/v1/agents/'+rid)
    if j['state'] not in ('completed','failed','stopped','review','checkpointed'):api('POST','/v1/agents/'+rid+'/stop',{})
   except Exception:pass
 with lock:result['trials'].append(row);atom(path,result)
 print(json.dumps(row),flush=True);return row
save()
a=run(FLASH,'affinity-on-1',tune=1)
b=run(FLASH,'affinity-on-2',tune=1)
c=run(FLASH,'affinity-control',tune=0)
baseline=json.loads((ROOT/'speed-benchmark.json').read_text())
controls=[x['engine_tps'] for x in baseline['trials'] if x.get('model')==FLASH and x.get('state')=='completed']
if c.get('engine_tps'):controls.append(c['engine_tps'])
tuned=[x['engine_tps'] for x in (a,b) if x.get('state')=='completed' and x.get('engine_tps')]
control=sum(controls)/len(controls) if controls else None
candidate=sum(tuned)/len(tuned) if tuned else None
selected=1 if candidate and control and min(tuned)>control*1.03 else 0
result['affinity']={'control_mean_engine_tps':control,'candidate_mean_engine_tps':candidate,'selected_aux_cpus':selected,'criterion':'Both tuned trials must exceed control mean by 3%; otherwise keep default.'};save()
begin=time.time()
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
 fs=[pool.submit(run,FLASH,'concurrent-flash',2048,selected),pool.submit(run,V100,'concurrent-v100-a',1024),pool.submit(run,V100,'concurrent-v100-b',1024)]
 rows=[f.result() for f in fs]
end=time.time();good=all(r.get('state')=='completed' and isinstance(r.get('tokens'),int) for r in rows)
result['concurrent']={'started':begin,'ended':end,'wall_seconds':end-begin,'complete':good,'output_tokens':sum(r.get('tokens',0) for r in rows),'aggregate_wall_tps':sum(r.get('tokens',0) for r in rows)/(end-begin) if good else None,'models':{'flash':1,'v100':2}}
result['finished']=datetime.datetime.now(datetime.timezone.utc).isoformat();save()
