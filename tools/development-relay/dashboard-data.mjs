import {readFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import path from 'node:path';
import {workerView} from './worker-view.mjs';
import {recordSourceMilestones} from './source-activity.mjs';

export async function augmentDashboardStatus(data,privateRoot){
 try{data.worker_supervision=JSON.parse(await readFile(path.join(privateRoot,'worker-status.private.json'),'utf8'));}catch{data.worker_supervision={available:false};}
 try{const plan=JSON.parse(await readFile(path.join(privateRoot,'autonomous-worker-plan.private.json'),'utf8'));data.planned_queue=plan.tasks.filter(t=>t.deferred_until&&!data.tasks.some(s=>s.id===t.id)).map(t=>({id:t.id,title:t.display_title,executor:t.executor,after:t.deferred_until}));}catch{data.planned_queue=[];}
 const views=await Promise.all(['strata','opencode'].map(async worker=>{try{return await workerView(privateRoot,worker);}catch(e){return {worker,error:e.message};}}));
 data.source_activity=recordSourceMilestones(privateRoot,views);data.live_source=views.map(v=>({worker:v.worker,task_id:v.task_id,last_source_edit_at:v.work_output?.last_source_edit_at,files:v.work_output?.files??[],snapshot_sha:v.work_output?.snapshot_sha,error:v.error}));data.task_milestones=[];
 for(const task of data.tasks){if(!/^[a-z0-9][a-z0-9-]{0,79}$/.test(task.id))continue;let db;try{db=new DatabaseSync(path.join(privateRoot,task.id+'.db'),{readOnly:true});for(const e of db.prepare("SELECT at,event,value FROM events WHERE event IN ('WORKER_STARTED','WORKER_FINISHED') ORDER BY id").all()){const value=JSON.parse(e.value);if(value.task===task.id)data.task_milestones.push({at:e.at,event:e.event,task:task.id,status:value.status});}}catch{}finally{db?.close();}}
 return data;
}
