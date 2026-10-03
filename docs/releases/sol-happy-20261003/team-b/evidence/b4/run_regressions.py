"""Bounded sequential existing regressions; full command/exit/count evidence."""
import datetime
import hashlib
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

BASE=Path(__file__).resolve().parent
ROOT=Path('/workspace/team-b-worktree')
SDK='/workspace/.tools/dotnet/dotnet'
for d in ['logs','dotnet-home','nuget','tmp','artifacts-files','artifacts-sites','results-sites']:(BASE/d).mkdir(exist_ok=True)
env=os.environ.copy()
env.update(DOTNET_CLI_HOME=str(BASE/'dotnet-home'),NUGET_PACKAGES=str(BASE/'nuget'),TMPDIR=str(BASE/'tmp'),TMP=str(BASE/'tmp'),TEMP=str(BASE/'tmp'),DOTNET_CLI_TELEMETRY_OPTOUT='1',AVALONIA_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
tasks={
 'files':([SDK,'run','--project','9to1 Workspace/Files/CUI.Tests/HavenOS.Files.CUI.Tests.csproj','--configuration','Release','--artifacts-path',str(BASE/'artifacts-files')], 'custom executable; 11 declared checks, no VSTest discovery'),
 'sites':([SDK,'test','9to1 Workspace/Sites/Tests/HavenOS.Sites.Tests.csproj','--configuration','Release','--artifacts-path',str(BASE/'artifacts-sites'),'--results-directory',str(BASE/'results-sites'),'--logger','trx;LogFileName=sites.trx','--logger','console;verbosity=normal'], 'xUnit/VSTest; expected 15 theory/fact cases, must verify actual discovery/execution')}
for name in sys.argv[1:]:
 command,count_note=tasks[name]
 timestamp=datetime.datetime.now(datetime.timezone.utc).isoformat()
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
 logfile=BASE/'logs'/f'{name}.log'
 with logfile.open('w') as out:
  out.write(json.dumps({'command':command,'cwd':str(ROOT),'start':timestamp,'commit':head,'environment':{k:env[k] for k in ['DOTNET_CLI_HOME','NUGET_PACKAGES','TMPDIR','AVALONIA_TELEMETRY_OPTOUT']}},indent=2)+'\n')
  out.flush()
  try:completed=subprocess.run(command,cwd=ROOT,env=env,stdout=out,stderr=subprocess.STDOUT,timeout=300);exit_code=completed.returncode
  except subprocess.TimeoutExpired:exit_code=124;out.write('\nBOUNDED_TIMEOUT_300_SECONDS\n')
  out.write(f'\nEXIT_STATUS={exit_code}\n')
 text=logfile.read_text()
 result={'command':command,'cwd':str(ROOT),'start':timestamp,'end':datetime.datetime.now(datetime.timezone.utc).isoformat(),'tested_commit':head,'platform':'Debian 13 Linux x64','sdk':'10.0.401','exit_status':exit_code,'expected_count':11 if name=='files' else 15,'count_note':count_note,'discovered':None,'executed':None,'passed':0,'failed':None,'outcome':'PASS_LOCAL_REGRESSION_ONLY' if exit_code==0 else 'FAILED_BUILD_OR_EXECUTION','runtime_browser_acceptance':'NOT_RUN','log':str(logfile),'log_sha256':hashlib.sha256(logfile.read_bytes()).hexdigest()}
 if name=='files':
  result['declared_check_count']=11
  result['discovered']='NOT_APPLICABLE_CUSTOM_MAIN'
  # All checks execute serially; success marker only emitted after RunAll returns.
  if exit_code==0 and 'Files CUI domain contract checks passed.' in text:result.update(executed=11,passed=11,failed=0)
  else:result['executed']=None;result['passed']=0;result['count_note']+='; no per-check logging; cannot claim exact completed count on failure'
 else:
  trx=BASE/'results-sites/sites.trx'
  if trx.exists():
   tree=ET.parse(trx); ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
   counters=tree.find('.//t:Counters',ns)
   if counters is not None:result.update(discovered=int(counters.attrib['total']),executed=int(counters.attrib['executed']),passed=int(counters.attrib['passed']),failed=int(counters.attrib['failed']))
  elif 'error CS' in text:
   result.update(discovery_reached=False,executed=0)
   result['count_note']+='; compilation failed before discovery, zero executed'
 (BASE/f'{name}-result.json').write_text(json.dumps(result,indent=2)+'\n')
 print(json.dumps(result,indent=2),flush=True)
 print(text[-6500:],flush=True)
