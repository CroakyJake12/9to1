#!/usr/bin/python3
"""Bounded qualification through Dulche; run only after controlled native adapter load."""
import ast, datetime, http.client, json, os, pathlib, signal, socket, time, uuid
ROOT=pathlib.Path("/home/jacob/9to1-swarm-harness-20261124")
MODEL="inkling-2-mini-strata-ud-q4-k-xl"; V100="qwen3.8-27b-v100"
SOCK="/run/user/1000/9to1-dulche/api.sock"
TERMINAL={"completed","failed","stopped","checkpointed","review"}
STOP=False; OWNED=set()
DEADLINE=time.time()+240
STATE={"model":MODEL,"started":time.time(),"context_required":32768,"trials":[],"qualified":False}
def atom(path,data):
 p=path.with_name(path.name+"."+uuid.uuid4().hex+".tmp");p.write_text(json.dumps(data,indent=2)+"\n");p.replace(path)
def checkpoint():atom(ROOT/"inkling-qualification-progress.json",STATE)
def stopped(*_):
 global STOP
 STOP=True
for sig in (signal.SIGTERM,signal.SIGINT):signal.signal(sig,stopped)
class LocalHTTP(http.client.HTTPConnection):
 def connect(self):
  self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM);self.sock.settimeout(self.timeout);self.sock.connect(SOCK)
def api(method,path,body=None):
 c=LocalHTTP("localhost",timeout=10)
 try:
  headers={"Content-Type":"application/json","Connection":"close"}
  if method=="POST":headers["Idempotency-Key"]="inkling-qual-"+uuid.uuid4().hex
  c.request(method,path,None if body is None else json.dumps(body).encode(),headers)
  r=c.getresponse();raw=r.read(8*1024*1024+1)
  if len(raw)>8*1024*1024:raise RuntimeError("response too large")
  d=json.loads(raw)
  if not 200<=r.status<300:raise RuntimeError("Dulche "+str(r.status)+": "+str(d.get("error")))
  return d
 finally:c.close()
def available():
 if STOP or time.time()>=DEADLINE:raise RuntimeError("qualification stop/deadline")
def guard_check():
 p=pathlib.Path("/home/jacob/strata-run/thermal_guard_status.json")
 d=json.loads(p.read_text())
 if not 0<=time.time()-d.get("ts",0)<6 or d.get("halted") is not False:raise RuntimeError("thermal guard halted/stale")
 for name in ("rtx5080","v100"):
  x=d["devices"][name]
  if x.get("telemetry_ok") is not True or x.get("stopped") is not False:raise RuntimeError(name+" thermal telemetry unsafe")
  if name=="rtx5080" and not 0<=x["reading_full"]["temp"]<80:raise RuntimeError("RTX80C ceiling")
 return d
def health():
 available()
 guard=guard_check()
 rows={m:api("GET","/v1/models/"+m) for m in (MODEL,V100)}
 for m,r in rows.items():
  if r.get("state")!="loaded":raise RuntimeError(m+" is not loaded")
 context=rows[MODEL].get("context")
 if not isinstance(context,(int,float)) or context<32768:raise RuntimeError("native context is below32768")
 r=api("GET","/v1/resources")
 if time.time()-r.get("sample_at",0)>15:raise RuntimeError("stale resource telemetry")
 if r.get("host_available_mib",0)<8192:raise RuntimeError("available RAM below8GiB")
 if r.get("disk_free_bytes",0)<60*2**30:raise RuntimeError("disk below60GiB")
 return {"models":rows,"resources":r,"thermal_guard":guard,"at":time.time()}
def cancel(runid):
 j=api("GET","/v1/agents/"+runid)
 if j["state"] not in TERMINAL:
  api("POST","/v1/agents/"+runid+"/stop",{})
  until=time.monotonic()+10
  while time.monotonic()<until:
   j=api("GET","/v1/agents/"+runid)
   if j["state"] in TERMINAL:break
   time.sleep(.2)
 if j["state"] not in TERMINAL:raise RuntimeError("owned cancellation not terminal: "+runid)
 OWNED.discard(runid)
def generate(name,messages,limit):
 available()
 counted=api("POST","/v1/models/"+MODEL+"/count-tokens",{"messages":messages})
 n=counted.get("input_tokens")
 if not isinstance(n,int) or n<=0 or n+limit>30000:raise RuntimeError("invalid token count/context budget")
 payload={"model":MODEL,"messages":messages,"max_tokens":limit,"temperature":0,"reasoning_effort":"none","stream":True,"stream_options":{"include_usage":True}}
 start=time.time()
 j=api("POST","/v1/agents",{"model":MODEL,"objective":"9to1 native Inkling qualification "+name,"steps":[{"type":"generate","payload":payload}]})
 rid=j["id"];OWNED.add(rid);STATE["active"]={"run":rid,"name":name};checkpoint()
 try:
  until=min(DEADLINE,start+150)
  while time.time()<until and not STOP:
   guard_check()
   j=api("GET","/v1/agents/"+rid)
   if j["state"] in TERMINAL:break
   time.sleep(.15)
  if j.get("state")!="completed":raise RuntimeError(name+" did not complete: "+str(j.get("state")))
  if j.get("model")!=MODEL:raise RuntimeError("wrong model on completed job")
  out=j["completed_steps"][-1];message=out["choices"][0]["message"];content=message.get("content")
  usage=out.get("usage") or {};tokens=usage.get("completion_tokens")
  if not isinstance(content,str) or not content.strip() or not isinstance(tokens,int) or tokens<=0:raise RuntimeError("missing actual text/output-token usage")
  t=out.get("timings") or {};dt=time.time()-start
  row={"name":name,"run":rid,"model":j["model"],"input_tokens":n,"tokenization":counted,"tokens":tokens,"token_source":"usage.completion_tokens","wall_seconds":dt,"wall_tps":tokens/dt,"elapsed_s":out.get("elapsed_s"),"ttft_s":out.get("ttft_s"),"engine_tps":t.get("predicted_per_second"),"timings":t,"usage":usage,"finish_reason":out["choices"][0].get("finish_reason"),"content":content}
  drafted=t.get("draft_n");accepted=t.get("draft_n_accepted")
  row["spec_acceptance_pct"]=100*accepted/drafted if isinstance(drafted,(int,float)) and drafted>0 and isinstance(accepted,(int,float)) and 0<=accepted<=drafted else None
  STATE["trials"].append(row);checkpoint();return row
 finally:
  cancel(rid);STATE.pop("active",None);checkpoint()
def validate_smoke(row):
 text=row["content"].strip()
 if text.startswith("```"):
  ls=text.splitlines()
  if ls[-1].strip()=="```":text="\n".join(ls[1:-1])
 data=json.loads(text)
 if data.get("language")!="python" or not isinstance(data.get("code"),str):raise RuntimeError("smoke JSON contract")
 tree=ast.parse(data["code"])
 allowed=(ast.Module,ast.FunctionDef,ast.arguments,ast.arg,ast.Return,ast.Call,ast.Name,ast.Load,ast.Constant,ast.Expr)
 if any(not isinstance(x,allowed) for x in ast.walk(tree)):raise RuntimeError("smoke code outside pure bounded expression")
 funcs=[x for x in tree.body if isinstance(x,ast.FunctionDef)]
 if len(tree.body)!=1 or len(funcs)!=1 or funcs[0].name!="clamp" or len(funcs[0].args.args)!=3 or funcs[0].decorator_list:raise RuntimeError("smoke function contract")
 for x in ast.walk(tree):
  if isinstance(x,ast.Call) and (not isinstance(x.func,ast.Name) or x.func.id not in ("min","max")):raise RuntimeError("smoke call outside min/max")
 ns={"__builtins__":{},"min":min,"max":max,"int":int}
 exec(compile(tree,"<inkling-smoke>","exec"),ns)
 cases=[((-5,0,10),0),((4,0,10),4),((15,0,10),10),((0,0,0),0),((-4,-9,-2),-4)]
 for args,want in cases:
  if ns["clamp"](*args)!=want:raise RuntimeError("generated clamp failed case "+repr(args))
 row["correctness"]={"passed":True,"cases":len(cases),"execution":"AST-whitelisted pure function; calls limited to min/max"}
SMOKE=[{"role":"system","content":"Return only the requested JSON object, with no markdown."},{"role":"user","content":"Return one JSON object with language set to python and code containing exactly this public function contract: clamp(value, low, high). Assume low <= high. Clamp value into the inclusive range. Use a single return expression calling min and max, no imports, loops, recursion or helpers. The code must be valid executable Python with real newline characters represented correctly in JSON."}]
SUSTAINED=[{"role":"system","content":"You are an experienced C# developer. Output code directly. Do not include hidden reasoning."},{"role":"user","content":"Write a complete C# implementation of a bounded asynchronous job scheduler for a desktop development tool, followed by xUnit regression tests. Requirements: stable job identity; configurable concurrency; cancellation of queued and running jobs; no user callback while holding a lock; explicit completed/failed/cancelled states; disposal that awaits running work; test cancellation, exact-once completion, maximum concurrency and ordering. Output actual complete code and tests. Include detailed XML comments. Use enough detail for at least 200 lines. Do not stop early for brevity."}]
def main():
 checkpoint()
 try:
  STATE["health_before"]=health()
  smoke=generate("json-code-smoke",SMOKE,512);validate_smoke(smoke);checkpoint()
  sustained=generate("sustained-code",SUSTAINED,1024)
  if sustained["tokens"]<512:raise RuntimeError("sustained run shorter than512 output tokens")
  if not any(word in sustained["content"] for word in ("class ","namespace ","using System")):raise RuntimeError("sustained output is not recognizable C# code")
  STATE["health_after"]=health()
  STATE.update(qualified=True,passed=True,context_size=32768,generation_run=sustained["run"],coexistence_v100=True,finished=time.time());checkpoint()
  atom(ROOT/"inkling-qualification.json",STATE)
  print(json.dumps({"qualified":True,"trials":[{k:r.get(k) for k in ("name","run","tokens","wall_tps","engine_tps","ttft_s","spec_acceptance_pct")} for r in STATE["trials"]]}),flush=True)
 except Exception as exc:
  STATE.update(qualified=False,error=str(exc),finished=time.time());checkpoint();atom(ROOT/"inkling-qualification-failed.json",STATE);raise
 finally:
  for rid in list(OWNED):cancel(rid)
if __name__=="__main__":main()
