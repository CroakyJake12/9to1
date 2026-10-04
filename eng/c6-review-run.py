import subprocess,json,hashlib,os,datetime,sys
from pathlib import Path
name,workspace,expected,*cmd=sys.argv[1:]
out=Path(__file__).resolve().parent.parent/'docs/releases/sol-happy-20261003/c6'
commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=workspace,text=True).strip()
status=subprocess.check_output(['git','status','--porcelain'],cwd=workspace,text=True).strip()
env=os.environ.copy();env.update(DOTNET_CLI_HOME='/tmp/c6-dotnet',NUGET_PACKAGES='/workspace/.tools/nuget',DOTNET_CLI_TELEMETRY_OPTOUT='1')
meta={'id':name,'tested_commit':commit,'worktree_status':status,'command':cmd,'cwd':workspace,'expected':expected,'started':datetime.datetime.now(datetime.timezone.utc).isoformat()}
with (out/'raw'/f'{name}.log').open('w') as f:
 f.write(json.dumps(meta)+'\n');f.flush();r=subprocess.run(cmd,cwd=workspace,stdout=f,stderr=subprocess.STDOUT,env=env)
meta.update(exit_code=r.returncode,observed='See durable raw log; counts audit pending',outcome='PASS-PARTIAL' if r.returncode==0 else 'FAIL',log=f'raw/{name}.log',ended=datetime.datetime.now(datetime.timezone.utc).isoformat())
with (out/'test-results.jsonl').open('a') as f:f.write(json.dumps(meta)+'\n')
print(json.dumps(meta))
