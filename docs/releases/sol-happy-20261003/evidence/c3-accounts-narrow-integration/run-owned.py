import os,sys,time,json,pathlib,importlib.util,hashlib,subprocess
root=pathlib.Path(sys.argv[1]);out=pathlib.Path(sys.argv[2]);argv=sys.argv[3:]
out.mkdir(mode=0o700,parents=True,exist_ok=False)
helper=pathlib.Path('/workspace/team-c/c3-billing-hosted/cloud/cake-id-auth/tests/linux-fixture-custodian.py')
expected=subprocess.check_output(['git','show','ef6bf2c5:cloud/cake-id-auth/tests/linux-fixture-custodian.py'],cwd=root)
assert helper.read_bytes()==expected
spec=importlib.util.spec_from_file_location('custodian',helper);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
os.chdir(root)
head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
os.environ.update({'DOTNET_CLI_HOME':str(out/'cli-home'),'NUGET_PACKAGES':'/workspace/team-c/evidence/c3/accounts-integration/nuget','DOTNET_CLI_USE_MSBUILD_SERVER':'0','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1','MSBUILDDISABLENODEREUSE':'1'})
custody=m.Custody();pid=None;primary=None;cleanup=[];receipt=None
try:
 pid=m.launch(argv,custody);start=time.monotonic()
 while True:
  custody.observe();row=m.stat(pid)
  if row is None or row['state'] in ('Z','X'):break
  if time.monotonic()-start>600:raise TimeoutError('Owned original command deadline')
  time.sleep(.04)
except BaseException as error:primary=error
finally:
 try:receipt=custody.drain()
 except BaseException as error:cleanup.append(error)
 actual=next((r for r in (receipt or {}).get('reaped',[]) if r['pid']==pid),None)
 row={'head':head,'cwd':str(root),'argv':argv,'helperSha256':hashlib.sha256(expected).hexdigest(),'originalPid':pid,'originalOutcome':actual,'strictReceipt':receipt,'primary':None if primary is None else {'type':type(primary).__name__,'message':str(primary)},'cleanup':[{'type':type(e).__name__,'message':str(e)} for e in cleanup]}
 try:(out/'receipt.json').write_text(json.dumps(row,indent=2)+'\n')
 except BaseException as error:cleanup.append(error)
if primary or cleanup:raise BaseExceptionGroup('Original command and custody errors retained',([primary] if primary else [])+cleanup)
if actual is None:raise RuntimeError('Original command outcome missing')
if actual['code']!=1 or actual['status']!=0:sys.exit(1)
