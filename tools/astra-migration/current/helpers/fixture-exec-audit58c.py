"""Bounded Root-owned child tracing and strict whole evidence parser; no main."""
from pathlib import Path
import ast, codecs, fcntl, hashlib, json, os, re, select, selectors, signal, subprocess, time

DIAGNOSTIC_LIMIT=8*1024*1024
AUDIT_LIMIT=1024*1024
ADDITIONAL_RESIDENT_ALLOWANCE=64*1024*1024
ADDITIONAL_DURABLE_EVIDENCE_ALLOWANCE=64*1024*1024
STRACE={'path':'/usr/bin/strace','bytes':2189792,'sha256':'69f88291a4e47f258b8e32b089c919a247509f063ef3d6afe5389939051102b3'}
CORE_TARGETS={'path':'/workspace/astra-tools/dotnet-10.0.401/sdk/10.0.401/Roslyn/Microsoft.CSharp.Core.targets','bytes':12655,'sha256':'33a63870665f8eb019f43da5effa450f6c8000089a879bc5373061e870bd3780'}
CSC_TASK={'path':'/workspace/astra-tools/dotnet-10.0.401/sdk/10.0.401/Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll','bytes':363304,'sha256':'43ef13365d717a93706ffea19cb5ab5766557a1e9088ee00a2b70e19d9e2e1e5'}

def pin(path):
 path=Path(path);assert path.is_absolute() and path.resolve(strict=True)==path and not path.is_symlink()
 digest=hashlib.sha256();count=0
 with path.open('rb') as stream:
  for chunk in iter(lambda:stream.read(65536),b''):digest.update(chunk);count+=len(chunk)
 return {'path':str(path),'bytes':count,'sha256':digest.hexdigest()}

def verify_tools():
 for descriptor in (STRACE,CORE_TARGETS,CSC_TASK):assert pin(descriptor['path'])==descriptor
 targets=Path(CORE_TARGETS['path']).read_text()
 assert 'SkipCompilerExecution="$(SkipCompilerExecution)"' in targets and 'ProvideCommandLineArgs="$(ProvideCommandLineArgs)"' in targets
 assert 'execution pass executing CoreCompile passing SkipCompilerExecution=true and ProvideCommandLineArgs=true' in targets
 return {'strace':STRACE,'coreTargets':CORE_TARGETS,'cscTask':CSC_TASK,'observer':pin(__file__)}

READ_CHUNK=65536
MAX_PIPE_BYTES=1024*1024
MAX_COMMAND_BYTES=128*1024
MAX_COMMAND_ARGS=4096
MAX_OBSERVED_PIDS=4096
MAX_AUDIT_ROWS=32768
MAX_CSC_TASKS=128
MAX_RENDERED_COMMANDS=256
MAX_FAILURE_REPORT_BYTES=3*1024*1024
WHOLE_CAPTURE_MAX=DIAGNOSTIC_LIMIT+AUDIT_LIMIT+2*MAX_PIPE_BYTES+2*READ_CHUNK

class AuditFailure(RuntimeError):
 """Whole reports and any not-yet-persisted raw chunks survive propagation."""
 def __init__(self,report,report_pin=None,retained_raw_chunks=None):
  self.report=report;self.report_pin=report_pin;self.retained_raw_chunks=retained_raw_chunks or {}
  super().__init__('Fixture exec audit failed; full report and raw custody retained: '+str(report_pin or report.get('failureReportPath')))

def _describe_bytes(body):
 return {'bytes':len(body),'sha256':hashlib.sha256(body).hexdigest()}

def _fault(cause,phase):
 return {'phase':phase,'type':type(cause).__name__,'message':str(cause),'atUnixSeconds':time.time()}

def _record_fault(faults,cause,phase):
 current=_fault(cause,phase)
 for previous in faults:
  if all(previous.get(key)==current[key] for key in ('phase','type','message')):
   previous['occurrences']=previous.get('occurrences',1)+1;previous['lastAtUnixSeconds']=current['atUnixSeconds'];return
 faults.append(current)

def _validate_utf8(body):
 # Validate the whole stream incrementally. A single wide Unicode character
 # cannot promote an entire8MiB diagnostic string to a32MiB allocation.
 decoder=codecs.getincrementaldecoder('utf-8')('strict')
 for offset in range(0,len(body),READ_CHUNK):decoder.decode(body[offset:offset+READ_CHUNK],False)
 decoder.decode(b'',True)

def _bounded_matches(pattern,body,limit):
 matches=[]
 for match in re.finditer(pattern,body,re.M):
  assert len(matches)<limit,('Structured evidence count exceeds explicit bound',limit)
  matches.append(match)
 return matches

def _parse_strace_argv(serialized):
 # Parse one quoted C string at a time. A whole argv AST could amplify a
 # malformed1MiB trace into hundreds of thousands of Python syntax nodes.
 assert serialized.startswith('[') and serialized.endswith(']') and len(serialized)<=4*MAX_COMMAND_BYTES+4*MAX_COMMAND_ARGS
 values=[];cursor=1;total=0
 while cursor<len(serialized)-1:
  match=re.match(r'"(?:[^"\\]|\\.)*"',serialized[cursor:]);assert match,('unknown serialized argv string',cursor)
  literal=match[0];assert len(literal)<4*65535
  value=ast.literal_eval(literal);assert isinstance(value,str) and '\x00' not in value and len(value.encode('utf-8'))<65535
  assert len(values)<MAX_COMMAND_ARGS;values.append(value);total+=len(value.encode('utf-8'))+1;assert total<=MAX_COMMAND_BYTES
  cursor+=len(literal)
  if cursor==len(serialized)-1:break
  assert serialized[cursor:cursor+2]==', ';cursor+=2;assert cursor<len(serialized)-1
 assert cursor==len(serialized)-1
 return values

def _publish_failure(report,path):
 path=Path(path);report['failureReportPath']=str(path)
 try:
  assert path.is_absolute() and path.resolve(strict=False)==path and not path.exists() and not path.is_symlink() and path.parent.is_dir()
  body=(json.dumps(report,separators=(',',':'),ensure_ascii=True)+'\n').encode()
  assert len(body)<=MAX_FAILURE_REPORT_BYTES,('Full failure report exceeds reserved metadata envelope',len(body))
  with path.open('xb') as stream:stream.write(body);stream.flush();os.fsync(stream.fileno())
  descriptor=pin(path);assert descriptor['bytes']==len(body) and descriptor['sha256']==hashlib.sha256(body).hexdigest()
  return descriptor
 except BaseException as cause:
  # A filesystem publication fault is itself explicit. The complete structured
  # report remains on AuditFailure.report; no physical report pin is invented.
  report['failureReportPublicationFault']=_fault(cause,'failure-report-publication')
  return None

def _persist_whole(chunks,path,descriptor,faults):
 # Both names are absent task-owned outputs. A partially written primary is
 # retained; the exclusive emergency sibling holds the entire original body.
 for candidate in (Path(path),Path(str(path)+'.capture-emergency')):
  try:
   assert candidate.is_absolute() and candidate.resolve(strict=False)==candidate and not candidate.exists() and not candidate.is_symlink()
   with candidate.open('xb') as stream:
    for chunk in chunks:stream.write(chunk)
    stream.flush();os.fsync(stream.fileno())
   actual=pin(candidate);assert actual['bytes']==descriptor['bytes'] and actual['sha256']==descriptor['sha256']
   return actual
  except BaseException as cause:faults.append(_fault(cause,'whole-stream-publication:'+str(candidate)))
 return None

def launch(command,cwd,env,diagnostic_path,audit_path):
 """Prepare both bounded pipes and primary/fallback selectors BEFORE Popen.
 The caller still publishes its live receipt before capture(). No child or
 foreign process is started or attached until Root invokes this function.
 """
 tools=verify_tools();command=list(command)
 assert command and len(command)<=MAX_COMMAND_ARGS and all(isinstance(arg,str) and '\x00' not in arg for arg in command)
 assert all(len(arg.encode('utf-8'))<65535 for arg in command)
 assert sum(len(arg.encode('utf-8'))+1 for arg in command)<=MAX_COMMAND_BYTES
 diagnostic_path=Path(diagnostic_path);audit_path=Path(audit_path)
 for path in (diagnostic_path,audit_path):
  assert path.is_absolute() and path.resolve(strict=False)==path and not path.exists() and not path.is_symlink() and path.parent.is_dir() and path.parent.resolve()==path.parent
  assert not Path(str(path)+'.capture-emergency').exists()
 assert diagnostic_path!=audit_path
 report_base=str(diagnostic_path)+'.fixture-exec-audit58'
 for suffix in ('.capture-failure.json','.qualify-failure.json'):assert not Path(report_base+suffix).exists()
 handles=[];selector=None;diagnostic_stream=None
 try:
  diagnostic_read,diagnostic_write=os.pipe();handles.extend((diagnostic_read,diagnostic_write))
  audit_read,audit_write=os.pipe();handles.extend((audit_read,audit_write))
  primary={'diagnostic':diagnostic_read,'audit':audit_read}
  pipe_bytes={name:fcntl.fcntl(fd,fcntl.F_GETPIPE_SZ) for name,fd in primary.items()}
  assert all(0<size<=MAX_PIPE_BYTES for size in pipe_bytes.values())
  backup={}
  for name,fd in primary.items():backup[name]=os.dup(fd);handles.append(backup[name])
  for fd in [*primary.values(),*backup.values()]:os.set_blocking(fd,False)
  selector=selectors.DefaultSelector()
  for name,fd in primary.items():selector.register(fd,selectors.EVENT_READ,name)
  fallback=select.poll()
  for fd in backup.values():fallback.register(fd,select.POLLIN|select.POLLHUP|select.POLLERR)
  diagnostic_stream=os.fdopen(diagnostic_read,'rb',buffering=0)
  wrapper=[STRACE['path'],'-f','-ttt','-s','65535','-e','trace=execve,execveat','-o','/proc/self/fd/'+str(audit_write),'--',*command]
  child=subprocess.Popen(wrapper,cwd=cwd,env=env,stdout=diagnostic_write,stderr=subprocess.STDOUT,pass_fds=(audit_write,),start_new_session=True)
 except BaseException:
  if selector is not None:selector.close()
  if diagnostic_stream is not None:diagnostic_stream.close()
  for fd in handles:
   try:os.close(fd)
   except OSError:pass
  raise
 # Everything that can reject the measured capture topology precedes Popen.
 # Parent read descriptors remain live until both streams and the child close.
 child.stdout=diagnostic_stream;post_launch_faults=[]
 for fd in (diagnostic_write,audit_write):
  try:os.close(fd)
  except BaseException as cause:post_launch_faults.append(_fault(cause,'post-launch-parent-writer-close'))
 return {'child':child,'auditReadFd':audit_read,'primaryReadFds':primary,'backupReadFds':backup,'selector':selector,'fallbackPoll':fallback,'command':command,'actualPopenCommand':wrapper,'diagnosticPath':diagnostic_path,'auditPath':audit_path,'auditPipeBytes':pipe_bytes['audit'],'diagnosticPipeBytes':pipe_bytes['diagnostic'],'tools':tools,'startedUnixSeconds':time.time(),'wrapperProcessGroup':child.pid,'failureReportBase':report_base,'postLaunchFaults':post_launch_faults}

def capture(context):
 """Retain bounded raw chunks while draining both streams. Capture faults
 switch to the prepared duplicate-descriptor poller; they can never qualify.
 Only a declared stream-cap failure may signal this owned process group.
 Whole streams and full failure reports are published after natural joining.
 """
 child=context['child'];selector=context['selector'];poller=context['fallbackPoll']
 primary=context['primaryReadFds'];backup=context['backupReadFds']
 chunks={'diagnostic':[],'audit':[]};pending={name:bytearray() for name in chunks};counters={'diagnostic':0,'audit':0};hashes={name:hashlib.sha256() for name in chunks}
 limits={'diagnostic':DIAGNOSTIC_LIMIT,'audit':AUDIT_LIMIT};closed=set();faults=list(context['postLaunchFaults']);forced=None;mode='selector';stream_complete=True
 reverse_backup={fd:name for name,fd in backup.items()}
 while closed!={'diagnostic','audit'}:
  try:
   if mode=='selector':events=[(key.data,key.fd) for key,_ in selector.select(timeout=0.25)]
   elif mode=='poll':events=[(reverse_backup[fd],fd) for fd,_ in poller.poll(250)]
   else:
    active=[backup[name] for name in backup if name not in closed]
    ready,_,_=select.select(active,[],[],0.25);events=[(reverse_backup[fd],fd) for fd in ready]
  except InterruptedError:continue
  except BaseException as cause:
   _record_fault(faults,cause,'capture-'+mode);mode='poll' if mode=='selector' else 'select'
   if mode=='select':
    # Duplicate descriptors survive an accidental primary-descriptor fault.
    # Fundamental loss of both read handles is an explicit incomplete failure,
    # never a fabricated whole-stream or natural-close qualification.
    for name in backup:
     if name in closed:continue
     try:os.fstat(backup[name])
     except OSError:
      try:backup[name]=os.dup(primary[name]);reverse_backup[backup[name]]=name
      except OSError as missing:
       _record_fault(faults,missing,'unrecoverable-read-handle:'+name);closed.add(name);stream_complete=False
   continue
  for name,fd in events:
   if name in closed:continue
   try:chunk=os.read(fd,READ_CHUNK)
   except (BlockingIOError,InterruptedError):continue
   except BaseException as cause:_record_fault(faults,cause,'capture-read:'+name);mode='poll' if fd==primary[name] else 'select';continue
   if not chunk:
    if pending[name]:chunks[name].append(pending[name]);pending[name]=bytearray()
    closed.add(name)
    try:selector.unregister(primary[name])
    except BaseException as cause:_record_fault(faults,cause,'selector-unregister:'+name)
    try:poller.unregister(backup[name])
    except KeyError:pass
    except BaseException as cause:_record_fault(faults,cause,'poll-unregister:'+name)
    continue
   # Coalesce even one-byte reads into blocks of64KiB to128KiB. Retaining
   # millions of tiny bytes objects would violate the resident envelope.
   pending[name].extend(chunk)
   if len(pending[name])>=READ_CHUNK:chunks[name].append(pending[name]);pending[name]=bytearray()
   hashes[name].update(chunk);counters[name]+=len(chunk)
   if counters[name]>limits[name] and forced is None:
    forced={'reason':'DECLARED_WHOLE_STREAM_CAP_EXCEEDED','stream':name,'observedBytesAtEnforcement':counters[name],'capBytes':limits[name],'ownedProcessGroup':context['wrapperProcessGroup'],'atUnixSeconds':time.time()}
    try:os.killpg(context['wrapperProcessGroup'],signal.SIGKILL)
    except ProcessLookupError:forced['ownedGroupAlreadyNaturallyAbsent']=True
    except BaseException as cause:_record_fault(faults,cause,'owned-cap-failure-termination')
 while True:
  try:status=child.wait();break
  except InterruptedError:continue
  except BaseException as cause:_record_fault(faults,cause,'natural-child-join')
 try:selector.close()
 except BaseException as cause:_record_fault(faults,cause,'selector-close')
 if child.stdout and not child.stdout.closed:
  try:child.stdout.close()
  except BaseException as cause:_record_fault(faults,cause,'diagnostic-read-handle-close')
 for fd in {primary['audit'],*backup.values()}:
  try:os.close(fd)
  except OSError:pass
 for name in chunks:
  if pending[name]:chunks[name].append(pending[name]);pending[name]=bytearray()
 descriptors={name:{'bytes':counters[name],'sha256':hashes[name].hexdigest()} for name in chunks}
 if sum(counters.values())>WHOLE_CAPTURE_MAX:faults.append({'phase':'capture-bound','type':'ObservedCaptureEnvelopeExceeded','observedBytes':sum(counters.values()),'declaredMaximumBytes':WHOLE_CAPTURE_MAX})
 paths={'diagnostic':context['diagnosticPath'],'audit':context['auditPath']}
 pins={name:_persist_whole(chunks[name],paths[name],descriptors[name],faults) for name in chunks}
 unpublished={name:chunks[name] for name in chunks if pins[name] is None}
 report={'requestedChildCommand':context['command'],'actualPopenCommand':context['actualPopenCommand'],'actualWrapperPid':child.pid,'actualWrapperProcessGroup':context['wrapperProcessGroup'],'actualWrapperExitCode':status,'forcedTermination':forced,'captureFaults':faults,'wholeDiagnosticPin':pins['diagnostic'],'wholeAuditPin':pins['audit'],'rawPublicationAttemptPaths':{name:[str(paths[name]),str(paths[name])+'.capture-emergency'] for name in paths},'retainedRawBlockCounts':{name:len(chunks[name]) for name in chunks},'streamCapsBytes':limits,'streamBytes':counters,'measuredKernelPipeBytes':{'diagnostic':context['diagnosticPipeBytes'],'audit':context['auditPipeBytes']},'bothWholeStreamsEOF':closed=={'diagnostic','audit'} and stream_complete,'bothWholeStreamsNaturallyEOF':closed=={'diagnostic','audit'} and stream_complete and forced is None,'allObservedBytesRetainedWithoutTruncation':True,'allWholeStreamsPersisted':not unpublished,'unpublishedWholeStreams':{name:descriptors[name] for name in unpublished},'tools':context['tools'],'startedUnixSeconds':context['startedUnixSeconds'],'closedUnixSeconds':time.time(),'failureReportBase':context['failureReportBase'],'noSecondCompilerQualified':False}
 if faults or forced is not None or status!=0 or unpublished or not report['bothWholeStreamsEOF']:
  failure={**report,'status':'ACTUAL_FIXTURE_CAPTURE_FAILURE_PRESERVED_NO_QUALIFICATION','capture':report,'noSecondCompilerQualified':False}
  failure_pin=_publish_failure(failure,context['failureReportBase']+'.capture-failure.json')
  report['captureFailureReportPin']=failure_pin
  if faults or unpublished or not stream_complete:raise AuditFailure(failure,failure_pin,unpublished)
 return report

def parse_exec_audit(raw,expected_command):
 """Parse every row. Ordering is per PID, never across independent threads.
 Unknown/resumed/unfinished/truncated attempts or signals fail closed.
 """
 assert isinstance(raw,bytes) and 0<len(raw)<=AUDIT_LIMIT
 assert raw.endswith(b'\n') and b'...' not in raw and b'\x00' not in raw
 expected_command=list(expected_command);assert expected_command and len(expected_command)<=MAX_COMMAND_ARGS
 assert all(isinstance(arg,str) and '\x00' not in arg for arg in expected_command)
 assert sum(len(arg.encode('utf-8'))+1 for arg in expected_command)<=MAX_COMMAND_BYTES
 seen=set();terminals={};exec_rows=[];signals=[];per_pid_time={};row_count=0;first=None
 for line in raw.splitlines():
  row_count+=1;assert row_count<=MAX_AUDIT_ROWS
  match=re.fullmatch(rb'(\d+)\s+(\d+)\.(\d{1,9})\s+(.*)',line);assert match,('unknown or incomplete whole exec audit row',row_count,_describe_bytes(line))
  pid=int(match[1]);stamp=int(match[2])*1000000000+int(match[3].ljust(9,b'0'));event=match[4].decode('ascii','strict')
  assert pid>0 and stamp>=per_pid_time.get(pid,-1),('nonmonotonic same-PID timestamp',pid,row_count)
  assert pid not in terminals,('event after observed process termination',pid,row_count)
  per_pid_time[pid]=stamp;seen.add(pid);assert len(seen)<=MAX_OBSERVED_PIDS
  if first is None:first=(pid,event)
  if event.startswith('execve('):
   em=re.fullmatch(r'execve\(("(?:[^"\\]|\\.)*"), (\[.*\]), (?:0x[0-9a-f]+|NULL)(?: /\* \d+ vars \*/)?\) = 0',event)
   assert em,('non-successful, truncated or unknown exec attempt',row_count,_describe_bytes(line))
   assert len(em[1])<4*65535
   path=ast.literal_eval(em[1]);argv=_parse_strace_argv(em[2])
   assert isinstance(path,str) and isinstance(argv,list) and len(argv)<=MAX_COMMAND_ARGS and all(isinstance(arg,str) and '\x00' not in arg for arg in argv)
   assert len(exec_rows)==0,('additional exec attempt',row_count)
   exec_rows.append({'pid':pid,'executable':path,'argv':argv,'unixNanoseconds':stamp})
  elif event.startswith('+++ exited with '):
   tm=re.fullmatch(r'\+\+\+ exited with (\d+) \+\+\+',event);assert tm and int(tm[1])==0,('nonzero or invalid process close',pid,row_count)
   terminals[pid]=0
  elif event.startswith('--- SIGCHLD '):
   sm=re.fullmatch(r'--- SIGCHLD \{si_signo=SIGCHLD, si_code=CLD_EXITED, si_pid=(\d+), si_uid=\d+, si_status=0, si_utime=\d+, si_stime=\d+\} ---',event)
   assert sm and len(signals)<MAX_OBSERVED_PIDS,('unqualified child signal',row_count,_describe_bytes(line))
   signals.append({'pid':pid,'childPid':int(sm[1]),'event':event})
  else:raise AssertionError(('unknown signal, trace failure or incomplete audit event',row_count,_describe_bytes(line)))
 assert len(exec_rows)==1 and exec_rows[0]['executable']==expected_command[0] and exec_rows[0]['argv']==expected_command
 assert first[1].startswith('execve(') and first[0]==exec_rows[0]['pid'] and seen==set(terminals)
 assert all(row['childPid'] in terminals and terminals[row['childPid']]==0 for row in signals),('SIGCHLD lacks observed traced natural child close',signals)
 return {'initialActualExec':exec_rows[0],'observedPids':sorted(seen),'allObservedProcessExitCodes':terminals,'wholeAuditRows':row_count,'otherExecAttemptCount':0,'compilerExecAttemptCount':0,'knownNaturalChildSignals':signals,'timestampOrdering':'EXACT_INTEGER_NANOSECONDS_PER_PID_ONLY','wholeTraceParsedWithoutOmissionOrTruncation':True}

def parse_actual_csc_skip(diagnostic):
 assert isinstance(diagnostic,bytes) and len(diagnostic)<=DIAGNOSTIC_LIMIT
 _validate_utf8(diagnostic);assert b'\x00' not in diagnostic and b'Compilation request ' not in diagnostic
 starts=_bounded_matches(rb'^[ \t]*Task "Csc" \(TaskId:(\d+)\)[ \t]*\r?$',diagnostic,MAX_CSC_TASKS)
 all_headers=_bounded_matches(rb'^[ \t]*Task "Csc"[^\r\n]*',diagnostic,MAX_CSC_TASKS)
 assert len(all_headers)==len(starts),('unknown Csc task header or TaskId format',len(all_headers),len(starts))
 all_closes=_bounded_matches(rb'^[ \t]*Done executing task "Csc"\. \(TaskId:(\d+)\)[ \t]*\r?$',diagnostic,MAX_CSC_TASKS)
 close_headers=_bounded_matches(rb'^[ \t]*Done executing task "Csc"[^\r\n]*',diagnostic,MAX_CSC_TASKS)
 assert len(close_headers)==len(all_closes),('unknown Csc task close or TaskId format',len(close_headers),len(all_closes))
 assert len(all_closes)==len(starts)
 proofs=[];seen_ids=set()
 for index,start in enumerate(starts):
  task_id=start[1];numeric_id=int(task_id);assert 0<numeric_id<2147483648 and numeric_id not in seen_ids;seen_ids.add(numeric_id)
  matching=[end for end in all_closes if end[1]==task_id and end.start()>start.end()];assert len(matching)==1,('Csc task lacks one exact successful diagnostic close',numeric_id)
  end=matching[0];assert index+1==len(starts) or starts[index+1].start()>end.end(),('unknown interleaved Csc task blocks',numeric_id)
  block=diagnostic[start.end():end.start()];parameter_proofs={}
  for name,value in ((b'SkipCompilerExecution',b'True'),(b'UseSharedCompilation',b'False')):
   rows=_bounded_matches(rb'^[ \t]*Task Parameter:'+name+rb'=([^\r\n]*)\r?$',block,2)
   assert len(rows)==1,('expected one actual Csc task parameter',numeric_id,name.decode(),len(rows))
   parameter=re.fullmatch(value+rb'(?:[ \t]+\(TaskId:(\d+)\))?[ \t]*',rows[0][1]);assert parameter,('wrong actual Csc task parameter value/format',numeric_id,name.decode(),_describe_bytes(rows[0][1]))
   assert parameter[1] is None or parameter[1]==task_id,('parameter belongs to another Csc TaskId',numeric_id,name.decode())
   parameter_proofs[name.decode()]={'value':value.decode(),'parameterTaskId':int(parameter[1]) if parameter[1] is not None else None,'enclosingActualTaskId':numeric_id}
  proofs.append({'taskId':numeric_id,'actualTaskParameterSkipCompilerExecution':True,'actualTaskParameterUseSharedCompilation':False,'actualDiagnosticTaskClosedSuccessfully':True,'actualParameterTaskAssociation':parameter_proofs})
 rendered=_bounded_matches(rb'/Roslyn/bincore/csc(?:\.dll)?(?:[\s\"]|$)',diagnostic,MAX_RENDERED_COMMANDS)
 assert bool(rendered)==bool(starts),('rendered compiler string without captured actual Csc task',len(rendered),len(starts))
 return {'actualCscTaskSkipProofs':proofs,'actualCscTasksObserved':len(starts),'actualTaskSkipWitnessObserved':bool(starts),'renderedCompilerCommandStringsObserved':len(rendered),'compilerCommandStringDoesNotEstablishExecution':True,'compilerServerRequestCount':0}

def parse_observer_diagnostics(diagnostic,exec_proof):
 """The whole merged stderr/stdout body is a second observer fault surface.
 Only the exact normal attachment notice may appear, and its PID must have
 a fully captured natural-zero trace terminal. Unknown text fails closed.
 """
 assert isinstance(diagnostic,bytes) and len(diagnostic)<=DIAGNOSTIC_LIMIT
 _validate_utf8(diagnostic);assert b'\x00' not in diagnostic
 assert exec_proof['wholeTraceParsedWithoutOmissionOrTruncation'] is True
 observed=exec_proof['observedPids'];terminals=exec_proof['allObservedProcessExitCodes']
 assert isinstance(observed,list) and len(observed)<=MAX_OBSERVED_PIDS and len(observed)==len(set(observed))
 assert observed and all(isinstance(pid,int) and pid>0 for pid in observed) and set(observed)==set(terminals) and all(terminals[pid]==0 for pid in observed)
 tokens=_bounded_matches(rb'(?i:strace)',diagnostic,MAX_OBSERVED_PIDS)
 notices=[];seen=set()
 for token in tokens:
  start=diagnostic.rfind(b'\n',0,token.start())+1;end=diagnostic.find(b'\n',token.end())
  assert end>=0 and end-start<=256 and token.start()==start,('unknown, incomplete, embedded or prefixed observer diagnostic',token.start())
  line=diagnostic[start:end]
  match=re.fullmatch(rb'strace: Process ([1-9]\d*) attached\r?',line)
  assert match,('unknown observer warning/error/detach/omission or attachment format',_describe_bytes(line))
  pid=int(match[1]);assert pid<2147483648 and pid in terminals and terminals[pid]==0 and pid not in seen,('observer attachment lacks one unique captured natural-zero PID terminal',pid)
  seen.add(pid);notices.append({'pid':pid,'exactNormalAttachmentNotice':line.rstrip(b'\r').decode('ascii'),'wholeObservedTraceNaturalExitCode':terminals[pid]})
 return {'status':'WHOLE_MERGED_DIAGNOSTIC_OBSERVER_TEXT_FAIL_CLOSED_BOUND_TO_TRACE_NATURAL_TERMINALS','wholeMergedDiagnosticBytes':_describe_bytes(diagnostic),'wholeObserverTokensExamined':len(tokens),'exactNormalAttachmentNotices':notices,'allAttachmentPidsUniqueAndObservedNaturalZero':True,'unknownObserverDiagnosticCount':0,'observerWarningsErrorsDetachOmissionOrUnknownPrefixesAccepted':False,'noObserverNoticeDoesNotIndependentlyProveTraceCompleteness':True}

def qualify(capture_report,diagnostic_body,audit_body,expected_command):
 try:
  assert capture_report['forcedTermination'] is None and capture_report['actualWrapperExitCode']==0 and not capture_report['captureFaults']
  assert capture_report['requestedChildCommand']==list(expected_command) and capture_report['bothWholeStreamsNaturallyEOF'] and capture_report['bothWholeStreamsEOF'] and capture_report['allObservedBytesRetainedWithoutTruncation'] and capture_report['allWholeStreamsPersisted']
  assert len(diagnostic_body)==capture_report['wholeDiagnosticPin']['bytes'] and hashlib.sha256(diagnostic_body).hexdigest()==capture_report['wholeDiagnosticPin']['sha256']
  assert len(audit_body)==capture_report['wholeAuditPin']['bytes'] and hashlib.sha256(audit_body).hexdigest()==capture_report['wholeAuditPin']['sha256']
  exec_proof=parse_exec_audit(audit_body,expected_command);task_proof=parse_actual_csc_skip(diagnostic_body);observer_proof=parse_observer_diagnostics(diagnostic_body,exec_proof)
  assert '-p:SkipCompilerExecution=true' in expected_command and '-p:UseSharedCompilation=false' in expected_command and '-v:diagnostic' in expected_command
  assert capture_report['tools']==verify_tools()
  status='ACTUAL_FIXTURE_ZERO_COMPILER_EXEC_WITH_EXPLICIT_TASK_SKIP_AND_NATURAL_TRACE_CLOSE' if task_proof['actualTaskSkipWitnessObserved'] else 'ACTUAL_FIXTURE_ZERO_COMPILER_EXEC_NO_CSC_TASK_RENDERED'
  return {'status':status,'capture':capture_report,'actualExecProof':exec_proof,'actualCscTaskSkipProof':task_proof,'actualObserverDiagnosticProof':observer_proof,'actualTaskSkipWitnessObserved':task_proof['actualTaskSkipWitnessObserved'],'noSecondCompilerQualified':True,'allProtectedSourceRefCompilerOutputPinsMustSeparatelyRemainEqual':True,'diagnosticAndExecEvidenceAreDistinct':True,'zeroTaskBranchDoesNotClaimObservedSkipTrue':not task_proof['actualTaskSkipWitnessObserved']}
 except BaseException as cause:
  failure={**capture_report,'status':'ACTUAL_FIXTURE_QUALIFICATION_FAILURE_WHOLE_EVIDENCE_PRESERVED','capture':capture_report,'failure':_fault(cause,'whole-evidence-qualification'),'diagnosticBody':_describe_bytes(diagnostic_body),'auditBody':_describe_bytes(audit_body),'noSecondCompilerQualified':False}
  failure_pin=_publish_failure(failure,capture_report['failureReportBase']+'.qualify-failure.json')
  raise AuditFailure(failure,failure_pin) from cause
