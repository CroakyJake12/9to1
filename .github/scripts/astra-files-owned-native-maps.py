"""Exact produced Desktop apphost/managed assembly and original owned native maps."""
import pathlib,subprocess,os,json,time,shutil,signal

import sys
sys.path.insert(0,str(pathlib.Path(".github/scripts").resolve()))
from astra_original_native_session_drain import OriginalSession

def run(argv,name,root,out,digest,target_record,natives):
 target,closure=target_record
 pinned={str((root/r['path']).resolve()):r['sha256'] for r in closure}
 apphost=target.with_suffix('').resolve();managed=target.resolve();host=(target.parent/'testhost.dll').resolve();dotnet=pathlib.Path(shutil.which('dotnet')).resolve()
 assert str(apphost) in pinned and str(managed) in pinned and str(host) in pinned
 assert apphost.read_bytes().startswith(b'\x7fELF') and apphost.read_bytes().count(managed.name.encode()+b'\0')==1
 assert digest(apphost)==pinned[str(apphost)] and digest(managed)==pinned[str(managed)]
 libraries=[(p.resolve(),h) for p,h in natives if p.suffix=='.so'];assert len(libraries)==2
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
   while process.poll() is None:
    session.observe()
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
      if executable!=apphost or not items or pathlib.Path(items[0]).resolve()!=apphost:continue
      parentPid=int(tail[1])
      if parentPid not in owned or parentPid not in observed:continue
      parent=directory.parent/str(parentPid);parentStat=(parent/'stat').read_text();parentTail=parentStat[parentStat.rfind(')')+2:].split()
      if (int(parentTail[1]),parentTail[19])!=observed[parentPid]:continue
      parentExe=pathlib.Path(os.readlink(parent/'exe')).resolve();parentCwd=pathlib.Path(os.readlink(parent/'cwd')).resolve();parentArgs=[x.decode(errors='replace') for x in (parent/'cmdline').read_bytes().split(b'\0') if x]
      if parentExe!=dotnet or not any((pathlib.Path(x) if pathlib.Path(x).is_absolute() else parentCwd/x).resolve()==host for x in parentArgs):continue
      parentAfter=(parent/'stat').read_text();parentAfterTail=parentAfter[parentAfter.rfind(')')+2:].split()
      if (int(parentAfterTail[1]),parentAfterTail[19])!=observed[parentPid]:continue
      chain=original_chain(pid,observed)
      if chain is None:continue
      row['parentStartTicks']=parentAfterTail[19];row['launcherStartTicks']=launcherStartTicks;row['verifiedOriginalChain']=chain
      if digest(apphost)!=pinned[str(apphost)] or digest(managed)!=pinned[str(managed)] or digest(host)!=pinned[str(host)]:raise RuntimeError('Produced owning host bytes changed')
      for p,h in libraries:
       if str(p) in mapped:
        if digest(p)!=h:raise RuntimeError('Fresh native bytes changed')
        witnesses[str(p)]={**row,'apphostSHA':pinned[str(apphost)],'managedDLL':str(managed),'managedSHA':pinned[str(managed)],'testhostDLL':str(host),'testhostSHA':pinned[str(host)],'librarySHA':h}
     except (OSError,ValueError,IndexError):continue
    time.sleep(.05)
   code=process.wait()
  finally:
   session.drain()

 (out/(name+'-owned-native-map-receipt.json')).write_text(json.dumps({'processExit':code,'owningPid':process.pid,'launcherStartTicks':launcherStartTicks,'witnesses':witnesses,'observedTopology':observedTopology,'qualification':'Exact fresh native libraries in privately descended actual produced Desktop xUnit apphost with corresponding mapped managed assembly; sampled topology is not exhaustive scheduling evidence.'},indent=2)+'\n')
 if set(witnesses)!={str(p) for p,h in libraries}:raise RuntimeError('Both fresh renderer libraries were not observed in the exact owned managed test apphost')
 return code
