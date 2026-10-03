"""Exact maintained dotnet/testhost Canvas xunit2 managed assembly and original owned native maps."""
import pathlib,subprocess,os,json,time,shutil,signal

import sys
sys.path.insert(0,str(pathlib.Path(".github/scripts").resolve()))
from astra_original_native_session_drain import OriginalSession

def run(argv,name,root,out,digest,target_record,natives,*,sdk_record):
 target,closure=target_record
 pinned={str((root/r['path']).resolve()):r['sha256'] for r in closure}
 managed=target.resolve();host=(target.parent/'testhost.dll').resolve();dotnet=pathlib.Path(sdk_record['path']).resolve();dotnetSha=sdk_record['sha256']
 assert pathlib.Path(sdk_record['path']).is_absolute() and digest(dotnet)==dotnetSha
 assert argv[0]=='dotnet';argv=[str(dotnet),*argv[1:]]
 assert str(managed) in pinned and str(host) in pinned
 assert dotnet.read_bytes().startswith(b'\x7fELF')
 assert digest(managed)==pinned[str(managed)] and digest(host)==pinned[str(host)]
 libraries=[(p.resolve(),h) for p,h in natives if p.suffix=='.so'];assert len(libraries)==1
 witnesses={};observedTopology={}
 with (out/(name+'.log')).open('wb') as log:
  process=subprocess.Popen(argv,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  session=OriginalSession(process,os.environ['ASTRA_NATIVE_SESSION_DRAIN_RECORDS'])
  try:
   launchStat=(pathlib.Path('/proc')/str(process.pid)/'stat').read_text();launchTail=launchStat[launchStat.rfind(')')+2:].split();launcherStartTicks=launchTail[19]
   def original_chain(pid,observed):
    chain=[];visited=set()
    for _ in range(128):
     if pid in visited or pid not in observed:return None
     visited.add(pid);directory=pathlib.Path('/proc')/str(pid);stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split()
     if (int(tail[1]),tail[19])!=observed[pid]:return None
     chain.append({'pid':pid,'parent':int(tail[1]),'startTicks':tail[19]})
     if pid==process.pid:return chain if tail[19]==launcherStartTicks else None
     pid=int(tail[1])
    return None
   deadline=time.monotonic()+1200
   while process.poll() is None:
    session.observe()
    if time.monotonic()>deadline:raise TimeoutError("Actual owned Canvas native process exceeded1200s")
    observed={}
    for directory in pathlib.Path('/proc').iterdir():
     if not directory.name.isdigit():continue
     try:
      stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split();observed[int(directory.name)]=(int(tail[1]),tail[19])
     except (OSError,ValueError,IndexError):continue
    owned={process.pid}
    for _ in range(128):
     new={pid for pid,(parent,_) in observed.items() if parent in owned}
     if new.issubset(owned):break
     owned.update(new)
    for pid in owned:
     if pid not in observed:continue
     directory=pathlib.Path('/proc')/str(pid)
     try:
      raw=(directory/'maps').read_bytes();assert len(raw)<=4*1024*1024
      maps=raw.decode();stat=(directory/'stat').read_text();tail=stat[stat.rfind(')')+2:].split()
      if (int(tail[1]),tail[19])!=observed[pid]:continue
      executable=pathlib.Path(os.readlink(directory/'exe')).resolve();cwd=pathlib.Path(os.readlink(directory/'cwd')).resolve();items=[x.decode(errors='replace') for x in (directory/'cmdline').read_bytes().split(b'\0') if x]
      key=str(pid)+'-'+tail[19];mapped=[str(p) for p,h in libraries if str(p) in maps]
      if key not in observedTopology and len(observedTopology)>=2048:raise RuntimeError('Owned topology bound exceeded')
      row={'pid':pid,'parent':observed[pid][0],'startTicks':tail[19],'ownedAncestorPid':process.pid,'exe':str(executable),'cwd':str(cwd),'argv':items,'freshMappedLibraries':mapped}
      if mapped:row['maps']=maps
      if key not in observedTopology or mapped:observedTopology[key]=row
      if not mapped or str(managed) not in maps:continue
      if executable!=dotnet or len(items)<2 or pathlib.Path(items[0]).resolve()!=dotnet:continue
      if items[1]!='exec':continue
      position=2;optionRows=[]
      for flag,suffix in (('--runtimeconfig','.runtimeconfig.json'),('--depsfile','.deps.json')):
       expected=(target.parent/(target.stem+suffix)).resolve()
       if str(expected) not in pinned:
        if position<len(items) and items[position]==flag:break
        continue
       if position+1>=len(items) or items[position]!=flag:break
       supplied=(pathlib.Path(items[position+1]) if pathlib.Path(items[position+1]).is_absolute() else cwd/items[position+1]).resolve()
       if supplied!=expected or digest(expected)!=pinned[str(expected)]:break
       optionRows.append({'option':flag,'path':str(expected),'sha256':pinned[str(expected)]});position+=2
      else:
       if position>=len(items):continue
       entry=(pathlib.Path(items[position]) if pathlib.Path(items[position]).is_absolute() else cwd/items[position]).resolve()
       if entry!=host or str(host) not in maps:continue
       row['sourceBoundHostOptions']=optionRows
       row['entrypointIndex']=position
       position=-1
      if position!=-1:continue
      parentPid=int(tail[1])
      if parentPid not in owned or parentPid not in observed:continue
      parent=directory.parent/str(parentPid);parentStat=(parent/'stat').read_text();parentTail=parentStat[parentStat.rfind(')')+2:].split()
      if (int(parentTail[1]),parentTail[19])!=observed[parentPid]:continue
      parentExe=pathlib.Path(os.readlink(parent/'exe')).resolve();parentCwd=pathlib.Path(os.readlink(parent/'cwd')).resolve();parentArgs=[x.decode(errors='replace') for x in (parent/'cmdline').read_bytes().split(b'\0') if x]
      if parentExe!=dotnet or not parentArgs or pathlib.Path(parentArgs[0]).resolve()!=dotnet:continue
      parentAfter=(parent/'stat').read_text();parentAfterTail=parentAfter[parentAfter.rfind(')')+2:].split()
      if (int(parentAfterTail[1]),parentAfterTail[19])!=observed[parentPid]:continue
      chain=original_chain(pid,observed)
      if chain is None:continue
      row['parentStartTicks']=parentAfterTail[19];row['launcherStartTicks']=launcherStartTicks;row['verifiedOriginalChain']=chain
      if digest(dotnet)!=dotnetSha or digest(managed)!=pinned[str(managed)] or digest(host)!=pinned[str(host)]:raise RuntimeError('Exact SDK dotnet or produced owning host bytes changed')
      for p,h in libraries:
       if str(p) in mapped:
        if digest(p)!=h:raise RuntimeError('Fresh native bytes changed')
        witnesses[str(p)]={**row,'dotnetExecutable':str(dotnet),'dotnetExecutableSHA':dotnetSha,'managedDLL':str(managed),'managedSHA':pinned[str(managed)],'testhostDLL':str(host),'testhostSHA':pinned[str(host)],'librarySHA':h}
     except (OSError,ValueError,IndexError):continue
    time.sleep(.05)
   code=process.wait()
  finally:
   session.drain()

 (out/(name+'-owned-native-map-receipt.json')).write_text(json.dumps({'processExit':code,'owningPid':process.pid,'launcherStartTicks':launcherStartTicks,'witnesses':witnesses,'observedTopology':observedTopology,'qualification':'Exact fresh native libraries in privately descended actual maintained Canvas xunit2 dotnet/testhost with exact testhost entrypoint with corresponding mapped managed assembly; sampled topology is not exhaustive scheduling evidence.'},indent=2)+'\n')
 if set(witnesses)!={str(p) for p,h in libraries}:raise RuntimeError('The exact fresh Canvas renderer library was not observed in the exact owned managed dotnet/testhost')
 return code
