import os,sys,pathlib,subprocess,importlib.util,signal,json,time
root=pathlib.Path(sys.argv[2]) if len(sys.argv)>2 else pathlib.Path('/workspace/team-c/c3-admin-key-proposal'); evidence=pathlib.Path('/workspace/team-c/evidence/c3/admin-sdk03'); phase=sys.argv[1]
spec=importlib.util.spec_from_file_location('original_drain',root/'.github/scripts/astra-original-native-session-drain.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
out=evidence/('accounts-'+phase);out.mkdir();records=[]
env={**os.environ,'DOTNET_ROOT':'/workspace/.tools/dotnet','DOTNET_CLI_HOME':str(evidence/'dotnet-home'),'NUGET_PACKAGES':str(evidence/'nuget'),'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER':'1','MSBUILDDISABLENODEREUSE':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1'}
dotnet='/workspace/.tools/dotnet/dotnet';proj='9to1 Workspace/Accounts/Tests/Accounts.Specs.csproj';art=root/'artifacts/accounts-partial';props=['-m:1','-nr:false','-p:UseSharedCompilation=false','-p:UseArtifactsOutput=true','-p:ArtifactsPath='+str(art),'-p:IncludeProjectNameInArtifactsPaths=true','-p:Configuration=Debug']
commands=[('build',[dotnet,'build',proj,'--disable-build-servers',*props]),('run',[dotnet,str(art/'bin/Accounts.Specs/debug/Accounts.Specs.dll')])]
for name,argv in commands:
 rec=out/name;rec.mkdir();(rec/'expected-managed-launch.json').write_text(json.dumps({'expectedManagedLaunch':True,'drained':False}));readfd,writefd=os.pipe();log=out/(name+'.log');err=None;cleanup=[]
 with log.open('xb') as output:
  process=subprocess.Popen([sys.executable,'-I','-c',"import os,sys;fd=int(sys.argv[1]);token=os.read(fd,1);os.close(fd);assert token==b'G';os.execv(sys.argv[2],sys.argv[2:])",str(readfd),*argv],cwd=root,env=env,start_new_session=True,pass_fds=(readfd,),stdout=output,stderr=subprocess.STDOUT)
  creator=os.pidfd_open(process.pid);session=module.OriginalSession.__new__(module.OriginalSession)
  try:
   module.OriginalSession.__init__(session,process,rec);os.close(readfd);readfd=None;os.write(writefd,b'G');os.close(writefd);writefd=None;start=time.monotonic()
   while process.poll() is None:
    session.observe()
    if time.monotonic()-start>600 or log.stat().st_size>16*1024*1024:raise RuntimeError('bounded partial Accounts command refused')
    time.sleep(.04)
  except BaseException as e:err=repr(e)
  finally:
   for fd in (readfd,writefd):
    if fd is not None:os.close(fd)
   try:session.drain()
   except BaseException as e:cleanup.append(repr(e))
   try:signal.pidfd_send_signal(creator,signal.SIGKILL)
   except ProcessLookupError:pass
   process.wait(timeout=10);os.close(creator)
  record={'argv':argv,'exitCode':process.returncode,'primary':err,'cleanup':cleanup,'seal':json.loads((rec/'expected-managed-launch.json').read_text())};records.append(record);(out/'commands.json').write_text(json.dumps(records,indent=2)+'\n');print(name,process.returncode,record['seal'])
  if err or cleanup or process.returncode:sys.exit(1)
