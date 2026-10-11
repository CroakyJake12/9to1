export const partNames=['Foundations & first apps','Full apps & wider previews','The complete app ecosystem','9to1OS'];
export function taskTitle(task){return task?.display_title??task?.objective?.split('. ')[0]??'Next development requirement';}
export function groupApplications(part){
 const groups=new Map();
 for(const [index,item] of (part?.scope??[]).entries()){
  const name=item.live_work?item.app.split(' · ')[0]:item.app;
  if(!groups.has(name))groups.set(name,{name,entries:[],completed:0,total:0,assessed:false,live:false});
  const group=groups.get(name);group.entries.push({...item,index});group.assessed||=item.assessed!==false;group.live||=!!item.live_work;
  if(item.assessed!==false){group.total+=item.milestones?.length??0;group.completed+=item.milestones?.filter(m=>m.done===true).length??0;}
 }
 return [...groups.values()].sort((a,b)=>Number(b.live)-Number(a.live));
}
export function operationalState(s){
 const tasks=s.tasks??[],current=tasks.filter(t=>!s.current_plan_task_ids||s.current_plan_task_ids.includes(t.continuation_of??t.id)),working=tasks.filter(t=>t.status==='WORKING'),queued=current.filter(t=>t.status==='QUEUED'&&t.lane==='local');
 const recovery=s.recovery?tasks.find(t=>t.id===s.recovery.task_id):null;
 const continued=recovery&&working.some(t=>t.executor===recovery.executor&&t.resume_worktree_id&&s.recovery.root.replace(/\\/g,'/').endsWith('/'+t.resume_worktree_id));
 const blocked=continued?null:recovery??[...current].reverse().find(t=>t.status==='BLOCKED'&&t.owner&&!t.resolution&&!working.some(w=>w.executor===t.executor));
 const workers=['strata','opencode'].map(executor=>{
  const active=working.find(t=>t.executor===executor),next=queued.find(t=>t.executor===executor),last=[...tasks].reverse().find(t=>t.executor===executor);
  const held=blocked?.executor===executor?blocked:null,hold=s.worker_supervision?.workers?.find(w=>w.worker===executor&&w.status==='ON_HOLD')?.hold;
  return {executor,name:executor==='strata'?'Strata':'OpenCode',role:executor==='strata'?'Local coordinator':'Implementation worker',active,next,last,held,
   hold,state:hold?'On hold':active?'Working':held?'Blocked':next?'Queued':'Idle',task:active??held??next??last};
 });
 return {working,queued,recovery,blocked,workers,title:working.length?'Development in motion':blocked?'Development held at a checkpoint':s.presence?.paused?'Development paused':'Ready for the next task'};
}
export function failureReason(task){
 if(task?.evidence?.some(e=>e.exit_code===-1))return 'Previous worker reached its configured time limit';
 if(task?.result?.blockers?.some(b=>/verification failed/i.test(b)))return 'Build or verification failed';
 if(task?.result?.blockers?.some(b=>/No implementation source changes/.test(b)))return 'No implementation changes recorded';
 return task?.result?.blockers?.[0]??'Checkpoint review required';
}
export function executionPlan(s){
 const op=operationalState(s),steps=[];
 if(op.blocked)steps.push({title:'Review checkpoint: '+taskTitle(op.blocked),owner:op.blocked.executor==='strata'?'Strata / lead review':'OpenCode / lead review',state:'Blocked',dependency:'Requires source review and focused checks before continuation.',description:failureReason(op.blocked),action:'diagnostics',task:op.blocked.id,actionLabel:'Inspect checkpoint'});
 for(const task of op.working)steps.push({title:taskTitle(task),owner:task.executor==='strata'?'Strata':'OpenCode',state:'Working',dependency:'Publish the checkpoint to main after focused checks, then advance the queue. Local work continues until 22:00.',description:task.objective,action:'diagnostics',task:task.id,actionLabel:'View owned work'});
 for(const task of op.queued)steps.push({title:taskTitle(task),owner:task.executor==='strata'?'Strata':'OpenCode',state:task.requires_predecessor?'Waiting':'Queued',dependency:task.requires_predecessor?'Starts after its predecessor passes focused checks.':'Starts when the local lease and model are available.',description:task.objective,action:'diagnostics',task:task.id,actionLabel:'View queued task'});
 for(const task of s.planned_queue??[])steps.push({title:task.title,owner:task.executor==='strata'?'Strata':'OpenCode',state:'Waiting',dependency:'After '+task.after_title+' passes focused checks.',description:task.objective,action:null});
 if(!steps.length)steps.push({title:'Select the next development requirement',owner:'Relay coordinator',state:'Waiting',dependency:'Requires an explicitly scoped, unblocked task.',description:'No local task is queued. Model readiness alone does not start development.',action:null});
 // Shift handover is time-driven and rendered separately from task dependencies.
 return steps;
}
export function milestoneEvidence(part,item,milestone,s){
 const live=item.live_work===true,checkpoint=s.checkpoint?.manifest,checkpointFiles=checkpoint?.files??[];
 const explicit=milestone.evidence??item.evidence??null;
 return {title:milestone.name,recorded:milestone.done===true,assessed:item.assessed!==false,target:item.stage,app:item.app,platform:item.platform,
  source:explicit??(live?{kind:'Private source checkpoint',base_sha:checkpoint?.head,files:checkpointFiles.map(f=>({path:f.path,sha256:f.sha256})),task_id:s.recovery?.task_id}:null),
  note:item.note??part.note,limitation:explicit||live?'Source and targeted verification evidence do not grant independent acceptance.':'This milestone is a recorded ledger observation. A direct per-milestone source or test receipt is not linked; independent acceptance remains unverified.'};
}
