export function latestAttempt(tasks,id){return tasks.filter(t=>t.id===id||t.continuation_of===id).sort((a,b)=>(b.resume_attempt??0)-(a.resume_attempt??0))[0];}
export function predecessorFor(tasks,task){return task.requires_predecessor?latestAttempt(tasks,task.requires_predecessor):null;}
export function queueReady(tasks,task){const before=predecessorFor(tasks,task);return !task.requires_predecessor||!!before&&['COMPLETED','CHECKPOINT'].includes(before.status)&&before.evidence?.length>0&&before.evidence.every(e=>e.status!=='FAIL');}
export function inactivityDecision({task,identity,expected,now=Date.now(),lastOutput,startedAt,warningMs=600000}){
 if(task.status!=='WORKING')return task.status==='QUEUED'?'DISPATCH':'TERMINAL';
 if(!expected)return 'STARTUP_OR_REVIEW';
 if(!identity)return 'RESUME_AFTER_CHECKPOINT';
 if(identity.pid!==expected.pid||identity.parent!==expected.parent||identity.created!==expected.created)return 'IDENTITY_MISMATCH';
 return now-(lastOutput??startedAt??now)>warningMs?'LIVE_SILENCE_REVIEW':'WORKING';
}
