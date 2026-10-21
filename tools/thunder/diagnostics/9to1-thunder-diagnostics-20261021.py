#!/usr/bin/env python3
"""Read-only owner-PC Thunder diagnostics. No network, keys, model load or service changes."""
import argparse, datetime, hashlib, json, os, pathlib, platform, re, subprocess, tempfile
ROOT=pathlib.Path("/home/jacob/AI/strata-optimisation-linux")
START=datetime.datetime(2026,10,21,15,tzinfo=datetime.timezone.utc)
END=datetime.datetime(2026,10,21,20,tzinfo=datetime.timezone.utc)
report={"scope":"Read-only owner-PC preflight/crash evidence; not workload peak telemetry or performance comparison","started_at":datetime.datetime.now(datetime.timezone.utc).isoformat(),"limitations":["Installed model path/hash and service-to-GPU workload binding NOT VERIFIED","GPU/RAM observations are snapshots, not benchmark peaks","No credentials, argv, environment, configuration contents, raw journal text or personal files are emitted"]}
def active_window():
 now=datetime.datetime.now(datetime.timezone.utc)
 if not START<=now<END: raise RuntimeError("outside_authorized_21Oct15to20UTC_window")
def run(argv,seconds=5):
 active_window()
 try:
  r=subprocess.run(argv,capture_output=True,text=True,timeout=min(seconds,max(.1,(END-datetime.datetime.now(datetime.timezone.utc)).total_seconds())),env={"PATH":os.environ.get("PATH","/usr/bin:/bin"),"HOME":os.environ.get("HOME",""),"LANG":"C","LC_ALL":"C","GIT_OPTIONAL_LOCKS":"0","XDG_RUNTIME_DIR":os.environ.get("XDG_RUNTIME_DIR",""),"DBUS_SESSION_BUS_ADDRESS":os.environ.get("DBUS_SESSION_BUS_ADDRESS","")})
  return r.returncode,r.stdout[:262144],None
 except Exception as e:return None,"",type(e).__name__
def classify_signals(text):
 # Only fixed categories/counts/codes leave the machine. Never return raw journal text,
 # assertion contents, prompts, paths, tokens, process argv or configuration fields.
 patterns={
  "abort":r"(?i)\babort(?:ed|ing)?\b|\bSIGABRT\b|exit.code.?-6\b",
  "assertion":r"(?i)\bassert(?:ion)?(?:.failed)?\b",
  "out_of_memory":r"(?i)out.of.memory|\bOOM\b",
  "cuda_error":r"(?i)\bCUDA\b.{0,30}\berror\b|\bCUDA_ERROR_[A-Z_]+\b",
  "nonfinite":r"(?i)\bnon.?finite\b|\bNaN\b",
  "context_limit":r"(?i)\bcontext\b.{0,40}(?:exceed|too.large|limit)",
  "segmentation_fault":r"(?i)segmentation.fault|\bSIGSEGV\b",
  "core_dump":r"(?i)core.dump",
 }
 counts={k:0 for k in patterns};codes=set()
 allowlisted_cuda={"CUDA_ERROR_OUT_OF_MEMORY","CUDA_ERROR_ILLEGAL_ADDRESS","CUDA_ERROR_INVALID_VALUE","CUDA_ERROR_LAUNCH_FAILED","CUDA_ERROR_UNKNOWN"}
 for line in text.splitlines():
  for category,pattern in patterns.items():
   if re.search(pattern,line):counts[category]+=1
  if re.search(r"(?i)exit.code.?-6\b|\bSIGABRT\b",line):codes.add("SIGABRT/-6")
  if re.search(r"(?i)\bSIGSEGV\b",line):codes.add("SIGSEGV")
  for code in allowlisted_cuda:
   if re.search(r"\b"+code+r"\b",line):codes.add(code)
 return {"marker_counts":counts,"allowlisted_codes":sorted(codes),"interpretation":"Marker matches only; no root cause attribution or raw log text"}
def main(argv=None):
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--print-json",action="store_true",help="Also print safe structured diagnostics for copying; private file is still written")
 args=parser.parse_args(argv)
 active_window();os.umask(0o077)
 report["machine"]={"os":platform.system(),"architecture":platform.machine(),"kernel":platform.release()}
 try:
  rows=pathlib.Path('/proc/cpuinfo').read_text().splitlines();report["cpu"]={"model":next((x.split(':',1)[1].strip() for x in rows if x.startswith('model name')),None),"logical_processors":os.cpu_count()}
 except Exception as e:report["cpu"]={"status":type(e).__name__}
 try:
  values={x.split(':',1)[0]:x.split(':',1)[1].strip() for x in pathlib.Path('/proc/meminfo').read_text().splitlines()};report["ram"]={k:values.get(k) for k in ['MemTotal','MemAvailable','SwapTotal']}
 except Exception as e:report["ram"]={"status":type(e).__name__}
 code,out,error=run(['nvidia-smi','--query-gpu=name,memory.total,memory.used,utilization.gpu,driver_version','--format=csv,noheader,nounits']);report['gpu_snapshot']={"exit_code":code,"error_type":error,"rows":[x[:240] for x in out.splitlines()[:8]],"kind":"preflight snapshot only"}
 report['source']={"known_root_exists":ROOT.is_dir(),"git_head":None,"git_tag":None,"dirty":None}
 if ROOT.is_dir():
  for name,args in [('git_head',['rev-parse','HEAD']),('git_tag',['describe','--tags','--exact-match'])]:
   c,o,e=run(['git','-C',str(ROOT)]+args);value=o.strip();report['source'][name]=value if c==0 and re.fullmatch(r'[A-Za-z0-9._/-]{1,128}',value) else None
  c,o,e=run(['git','-C',str(ROOT),'status','--porcelain','--untracked-files=no']);report['source']['dirty']=(bool(o.strip()) if c==0 else None)
  report['source']['git_status_error_type']=e
 # Discover actual available homepc units; never assume inferred supervisor/gateway/tunnel names.
 c,o,e=run(['systemctl','--user','list-unit-files','homepc*','--no-legend','--no-pager']);units=[]
 for line in o.splitlines():
  candidate=line.split()[0] if line.split() else ''
  if re.fullmatch(r'homepc[A-Za-z0-9@_.-]*\.service',candidate):units.append(candidate)
 units=sorted(set(units))[:12];report['homepc_unit_discovery']={"exit_code":c,"error_type":e,"units":units};report['services']=[]
 for unit in units:
  c,o,e=run(['systemctl','--user','show',unit,'--property=Id,ActiveState,SubState,MainPID,ExecMainCode,ExecMainStatus','--no-pager']);state={}
  for line in o.splitlines():
   if '=' in line:
    k,v=line.split('=',1)
    if k in ['Id','ActiveState','SubState','MainPID','ExecMainCode','ExecMainStatus'] and len(v)<160:state[k]=v
  c,lines,e=run(['journalctl','--user','-u',unit,'--utc','--since','2026-10-21 15:38:00','--until','2026-10-21 15:42:00','--no-pager','--output=cat','-n','300'],seconds=6)
  signal_summary=classify_signals(lines)
  report['services'].append({"unit":unit,"state":state,"journal_exit_code":c,"journal_error_type":e,"crash_signal_summary":signal_summary})
 report['ended_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();active_window()
 directory=pathlib.Path(tempfile.mkdtemp(prefix='9to1-thunder-diagnostics-',dir=str(pathlib.Path.home())));output=directory/'diagnostics.json'
 fd=os.open(output,os.O_WRONLY|os.O_CREAT|os.O_EXCL|getattr(os,'O_NOFOLLOW',0),0o600)
 with os.fdopen(fd,'w') as f:json.dump(report,f,indent=2);f.write('\n')
 if args.print_json:print(json.dumps(report,indent=2))
 print('Saved private diagnostics to '+str(output))
 print('No model load, network, service change, credentials or unrelated process changes were performed.')
if __name__=='__main__':main()
