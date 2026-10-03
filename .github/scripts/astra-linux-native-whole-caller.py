import argparse,hashlib,json,os,pathlib,subprocess,sys
p=argparse.ArgumentParser();p.add_argument('--expected-commit',required=True);p.add_argument('--manifest',required=True);p.add_argument('--manifest-sha',required=True);a=p.parse_args()
root=pathlib.Path.cwd();cohort=os.environ.get('COHORT')
if cohort not in ('owning','regression','resource'):raise SystemExit('exact full cohort required')
if a.manifest!='.github/validation/astra-desktop-visible-cut.json' or hashlib.sha256((root/a.manifest).read_bytes()).hexdigest()!=a.manifest_sha or subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()!=a.expected_commit or os.environ['GITHUB_SHA']!=a.expected_commit:raise SystemExit('exact current cut/commit required')
out=root/('artifacts/desktop-visible-'+cohort);out.mkdir(parents=True,exist_ok=True)
runner=root/'.github/scripts'/('astra-linux-supervised-home-owning.py' if cohort=='owning' else 'astra-desktop-visible-'+cohort+'.py')
args=[sys.executable,str(runner),'--expected-commit',a.expected_commit,'--manifest',a.manifest,'--manifest-sha',a.manifest_sha]
receipt={'cohort':cohort,'commit':a.expected_commit,'manifestSha256':a.manifest_sha,'managedExit':None,'protectedStateCleanup':'not-created','status':'PENDING','qualification':'Actual known original test/setup sessions only; no guarantee of unobserved escaped descendants'}
primary=None;cleanup=None
try:
 with (out/'whole-caller.log').open('wb') as log:
  result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,check=False)
 receipt['managedExit']=result.returncode
 if result.returncode:raise RuntimeError('Actual whole cohort failed with exit '+str(result.returncode))
except BaseException as error:primary=error
finally:
 if cohort=='owning':
  setup=pathlib.Path(os.environ['RUNNER_TEMP'])/('astra-synthetic-home-setup-'+os.environ['GITHUB_RUN_ID']+'-'+os.environ['GITHUB_RUN_ATTEMPT'])
  env={k:os.environ[k] for k in ('GITHUB_WORKSPACE','RUNNER_TEMP','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_ACTIONS','RUNNER_ENVIRONMENT')};env['ASTRA_ISOLATED_SYNTHETIC_ROOT_FIXTURE']='1'
  # Root-owned directory is deliberately inaccessible to the ordinary CI caller.
  exists=subprocess.run(['sudo','-n','test','-f',str(setup/'protected-state-ownership.json')],check=False).returncode==0
  if exists:
   command=['sudo','-n','env',*[k+'='+v for k,v in sorted(env.items())],sys.executable,str(root/'.github/scripts/astra-linux-supervised-home-synthetic-setup.py'),'--output',str(setup),'--cleanup']
   with (out/'protected-cleanup.log').open('wb') as log:result=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,check=False)
   receipt['protectedStateCleanup']='removed-after-known-original-drain' if result.returncode==0 else 'FAILED-RETAINED-STATE'
   if result.returncode:cleanup=RuntimeError('Protected-state cleanup witness failed; retain actual failure and state')
 receipt['status']='FAILED' if primary or cleanup else 'ACTUAL_WHOLE_COHORT_PASSED'
 (out/'whole-caller-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
if primary and cleanup:raise BaseExceptionGroup('Whole cohort and cleanup both failed',[primary,cleanup])
if primary:raise primary
if cleanup:raise cleanup
