import {readActive,logTail,redactOutput,openCodeSession} from './worker-log.mjs';
import {readFile,writeFile,mkdir,rename,readdir,open} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {LocalRunner} from './runner.mjs';
import {request} from './client.mjs';
import {selectShift} from './policy.mjs';
import {preserve,git,dirty,reviewPaths,sha,blobHash,verifyCheckpointFiles} from './git.mjs';
import {processIdentity,sameProcess} from './worker-guard.mjs';
import {latestAttempt,queueReady,inactivityDecision} from './queue.mjs';
import {deliverPendingFeedback} from './reviews.mjs';
import {ensureModelReady} from './model-readiness.mjs';
const here=path.dirname(fileURLToPath(import.meta.url)),execute=promisify(execFile);
export const taskLabels={
 'cui-runtime-continuous-goal-20261011':'CUI: reusable definitions and Prefab instances',
 'cui-resource-definition-syntax-20261011':'CUI: definition syntax in resource builds',
 'cui-resource-root-boundary-20261011':'CUI: correct resource path boundaries',
 'cui-resource-name-collision-20261011':'CUI: prevent duplicate resource identities'
};
export async function supervise(configPath,planPath){
 const config=JSON.parse(await readFile(configPath,'utf8'));config.config_path=path.resolve(configPath);const plan=JSON.parse(await readFile(planPath,'utf8')),privateRoot=path.dirname(configPath),db=new DatabaseSync(path.join(privateRoot,'supervisor.db'));db.exec('CREATE TABLE IF NOT EXISTS events(at TEXT,event TEXT,value TEXT);CREATE TABLE IF NOT EXISTS steering(id TEXT PRIMARY KEY,worker TEXT,task_id TEXT,message TEXT,interrupt INTEGER,status TEXT,at TEXT);CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY,value TEXT)');
 const storedOwner=db.prepare("SELECT value FROM state WHERE key='supervisor-owner'").get();if(storedOwner){const expected=JSON.parse(storedOwner.value);if(sameProcess(expected,await processIdentity(expected.pid,config.powershell))){db.close();return {alreadyRunning:true,pid:expected.pid};}}
 const supervisorIdentity=await processIdentity(process.pid,config.powershell);db.prepare("INSERT INTO state VALUES ('supervisor-owner',?) ON CONFLICT(key) DO UPDATE SET value=excluded.value").run(JSON.stringify(supervisorIdentity));
 const event=(name,value)=>db.prepare('INSERT INTO events VALUES (?,?,?)').run(new Date().toISOString(),name,JSON.stringify(value));
 const children=new Map(),retryCounts=new Map();let busy=false;
 function pending(worker){return db.prepare("SELECT * FROM steering WHERE worker=? AND status='PENDING' ORDER BY at").all(worker);}
 async function retry(original,task,state,active){
  const messages=pending(task.executor),attempt=(task.resume_attempt??0)+1;if(attempt>2&&!messages.length){event('GOAL_BLOCKED',{task:task.id,reason:'Two autonomous resumption attempts exhausted; failure evidence retained'});return null;}
  const root=active?.root??(task.resume_worktree_id?path.join(config.worktrees,task.resume_worktree_id):path.join(config.worktrees,`${task.id}-${task.fence}`));const checkpoint=await preserve(root,config.checkpoints,'resume-'+task.id+'-'+attempt);
  const log=(task.evidence??[]).findLast(e=>e.log)?.log;let tail='';if(log){const file=path.isAbsolute(log)?log:path.join(config.logs,log);if(path.resolve(file).startsWith(path.resolve(config.logs)+path.sep))tail=redactOutput(await logTail(file,12000));}
  const session=task.codex_session_id??tail.match(/session id:\s*([0-9a-f-]{36})/i)?.[1],ocSession=task.opencode_session_id??(task.executor==='opencode'?openCodeSession(privateRoot,root)?.id:null)??tail.match(/"sessionID"\s*:\s*"(ses_[a-zA-Z0-9]+)"/)?.[1];
  if(task.executor==='strata'&&!session||task.executor==='opencode'&&!ocSession){event('RESUME_REQUIRES_REVIEW',{task:task.id,reason:'Known session identity is unavailable; source preserved'});return null;}
  const next={...original,id:original.id+'-resume-'+attempt,continuation_of:original.id,resume_attempt:attempt,source:task.source,requires_predecessor:null,resume_worktree_id:path.basename(root),resume_branch:config.branch_strategy==='main'?'main':(await git(root,['branch','--show-current'])).trim(),continuation_checkpoint_id:path.basename(checkpoint.directory),codex_session_id:session,opencode_session_id:ocSession,requirements:[...original.requirements,'Resume the exact preserved session/source; the preceding writer has ended. Do not repeat a broad audit. Address the actual verification failure or queued steering, then continue the persistent goal. Latest failure evidence:\n'+tail,...messages.map(m=>'Human steering: '+m.message)]};
  for(const field of ['status','owner','fence','result','evidence','acceptance','started_at','ended_at'])delete next[field];await request(config,'enqueue',{task:next});for(const m of messages)db.prepare("UPDATE steering SET status='DELIVERED' WHERE id=?").run(m.id);event('AUTONOMOUS_RESUME_QUEUED',{task:next.id,previous:task.id,checkpoint:checkpoint.directory,reason:messages.length?'Human steering':'Worker inactive or verification failed'});return next;
 }
 async function dispatch(task,state){
  const c={...config,state_db:path.join(privateRoot,task.id+'.db')},runner=new LocalRunner(c);runner.presence={home:state.presence.home,paused:state.presence.paused};runner.remote=state;
  if(runner.recovery){runner.db.close();event('RECOVERY_REVIEW_REQUIRED',{task:task.id});return;}
  const model=await ensureModelReady(runner,{fence:state.lease.fence,permitted:true,retryAt:Number(db.prepare("SELECT value FROM state WHERE key='model-retry-at'").get()?.value??0)});db.prepare("INSERT INTO state VALUES ('model-retry-at',?) ON CONFLICT(key) DO UPDATE SET value=excluded.value").run(String(model.retryAt));if(!model.ready){runner.db.close();event('MODEL_READINESS_BLOCKED',{task:task.id,reason:model.reason,log:model.log});return false;}
  children.set(task.executor,runner);event('AUTONOMOUS_DISPATCH',{task:task.id,worker:task.executor});
  try{await runner.startTask(task,state.lease.fence);return true;}catch(e){event('DISPATCH_FAILED',{task:task.id,error:e.message});try{await request(config,'result',{fence:state.lease.fence,task_id:task.id,status:'BLOCKED',evidence:[],acceptance:'NOT_RUN',result:{blockers:[e.message]}});}catch{}children.delete(task.executor);runner.db.close();return false;}
 }
 async function scan(){if(busy)return;busy=true;try{
  const freshPlan=JSON.parse(await readFile(planPath,'utf8'));plan.tasks=freshPlan.tasks;plan.goal=freshPlan.goal;
  const state=await request(config,'state'),permitted=!freshPlan.maintenance_pause&&state.lease?.worker==='local'&&state.lease.expires_at>Date.now()&&state.presence.home&&!state.presence.paused&&!state.checkpoint_requested&&selectShift(config.schedule,{...state.presence,healthy:true}).shift==='local';
  await deliverPendingFeedback(privateRoot);
  // Completion advances the source-backed queue. It never requests a handover.
  for(const template of plan.tasks.filter(t=>t.deferred_until&& !state.tasks.some(s=>s.id===t.id))){
   const previous=latestAttempt(state.tasks,template.deferred_until);if(!permitted||!previous||!['CHECKPOINT','COMPLETED'].includes(previous.status)||!previous.evidence?.length||previous.evidence.some(e=>e.status==='FAIL'))continue;
   const original=plan.tasks.find(t=>t.id===(previous.continuation_of??previous.id)),root=previous.resume_worktree_id?path.join(config.worktrees,previous.resume_worktree_id):path.join(config.worktrees,previous.id+'-'+previous.fence);
   const manifest=JSON.parse(await readFile(path.join(config.checkpoints,previous.result.checkpoint,'manifest.json'),'utf8'));let source;
   if(config.branch_strategy==='main'){
    if(!previous.result.source?.remote_verified||previous.result.source?.branch!=='main')throw new Error('Published main predecessor required');
    const commit=await sha(config.repository);source={repository:previous.source.repository,branch:'main',sha:commit,artifact_hashes:{},deleted_paths:[]};for(const file of manifest.files){if(file.deleted)source.deleted_paths.push(file.path);else source.artifact_hashes[file.path]=await blobHash(config.repository,commit,file.path);}
   }else{
    await verifyCheckpointFiles(root,manifest);const changed=await dirty(root);reviewPaths(changed.paths,original.paths);if(changed.paths.length){await git(root,['add','--',...changed.paths]);await git(root,['diff','--cached','--check']);await git(root,['commit','-m','Private verified relay checkpoint: '+original.id]);}
    source={...previous.source,sha:await sha(root),branch:(await git(root,['branch','--show-current'])).trim(),artifact_hashes:{},deleted_paths:[]};for(const file of manifest.files){if(file.deleted)source.deleted_paths.push(file.path);else source.artifact_hashes[file.path]=await blobHash(root,source.sha,file.path);}
   }
   const next={...template,source,requires_predecessor:previous.id,predecessor_source_embedded:true};delete next.deferred_until;
   await request(config,'enqueue',{task:next});event('NEXT_DEVELOPMENT_REQUIREMENT_QUEUED',{task:next.id,previous:previous.id,private_source:source.sha});
  }
  for(const [worker,r]of children){r.remote=state;r.presence={home:state.presence.home,paused:state.presence.paused};if(r.active){if(permitted)await r.renew();else{r.draining=true;if(r.child)await r.stopOwnedChild(r.child);}}else{r.db.close();children.delete(worker);}}
  const views=[];
  for(const worker of ['strata','opencode']){
   const originals=plan.tasks.filter(t=>t.executor===worker),attempts=originals.map(t=>latestAttempt(state.tasks,t.id)).filter(Boolean),working=attempts.find(t=>t.status==='WORKING');
   if(working){const active=readActive(path.join(privateRoot,working.id+'.db')),identity=active?.identity?await processIdentity(active.pid,config.powershell):null;let visible=null;try{const {workerView}=await import('./worker-view.mjs');const view=await workerView(privateRoot,worker);if(view.task_id===working.id)visible=view;}catch{}const decision=inactivityDecision({task:working,identity,expected:active?.identity,lastOutput:visible?.last_activity_at?Date.parse(visible.last_activity_at):active?.last_output_at,startedAt:active?.started_at});
    views.push({worker,task_id:working.id,title:taskLabels[working.continuation_of??working.id]??working.objective,status:identity?'WORKING':decision,phase:active?.phase??'Development',pid:identity?.pid??null,started_at:active?.started_at,last_output_at:active?.last_output_at??null,last_activity_at:visible?.last_activity_at??null,session_id:visible?.session_id??working.codex_session_id??null});
    const messages=pending(worker);if(messages.some(m=>m.interrupt)&&identity&&sameProcess(active.identity,identity)&&permitted){event('HUMAN_STEERING_INTERRUPT',{task:working.id});if(process.platform==='win32')await execute('taskkill.exe',['/PID',String(identity.pid),'/T','/F'],{windowsHide:true});else process.kill(identity.pid,'SIGTERM');}
    else if(decision==='RESUME_AFTER_CHECKPOINT'&&permitted){const checkpoint=await preserve(active.root,config.checkpoints,'inactive-'+working.id);await request(config,'result',{fence:state.lease.fence,task_id:working.id,status:'BLOCKED',evidence:[],acceptance:'NOT_RUN',result:{blockers:['Owned worker process ended; preserved for autonomous resume'],checkpoint:path.basename(checkpoint.directory)}});await retry(originals.find(t=>t.id===(working.continuation_of??working.id)),{...working,result:{checkpoint:path.basename(checkpoint.directory)}},state,active);}
    else if(decision==='LIVE_SILENCE_REVIEW'&&!retryCounts.has(working.id)){retryCounts.set(working.id,true);event('LIVE_WORKER_QUIET',{task:working.id,reason:'No recent output; live writer retained. Inference/tool activity may still be running.'});}
    continue;
   }
   const queued=attempts.filter(t=>t.status==='QUEUED'&&queueReady(state.tasks,t)).sort((a,b)=>b.priority-a.priority)[0];
   if(queued&&permitted){const started=await dispatch(queued,state);views.push({worker,task_id:queued.id,title:taskLabels[queued.continuation_of??queued.id]??queued.objective,status:started?'STARTING':'WAITING_MODEL'});continue;}
   const steerAtCheckpoint=pending(worker).length?attempts.find(t=>['CHECKPOINT','COMPLETED','BLOCKED'].includes(t.status)):null;
   const failed=steerAtCheckpoint??attempts.find(t=>t.status==='BLOCKED'&&!retryCounts.has('retry:'+t.id));if(failed&&permitted){retryCounts.set('retry:'+failed.id,true);await retry(originals.find(t=>t.id===(failed.continuation_of??failed.id)),failed,state,null);}
   const held=failed??attempts.find(t=>t.status==='BLOCKED');
   const selected=held??queued;
   views.push({worker,status:held?'BLOCKED':queued?'QUEUED':'IDLE',task_id:selected?.id,worktree_id:selected?(selected.resume_worktree_id??selected.id+'-'+selected.fence):null,title:taskLabels[selected?.continuation_of??selected?.id]??'Waiting for the next owned requirement'});
  }
  const report={at:new Date().toISOString(),supervisor_pid:process.pid,supervisor_identity:supervisorIdentity,permitted,goal:plan.goal,workers:views,events:db.prepare('SELECT * FROM events ORDER BY rowid DESC LIMIT 20').all().map(e=>({...e,value:JSON.parse(e.value)})),steering:db.prepare('SELECT id,worker,task_id,status,at FROM steering ORDER BY at DESC LIMIT 20').all()};const target=path.join(privateRoot,'worker-status.private.json');await writeFile(target+'.tmp',JSON.stringify(report,null,2));await rename(target+'.tmp',target);if(config.supervisor_output_file)await writeFile(config.supervisor_output_file,JSON.stringify(report,null,2));
 }catch(e){event('SUPERVISOR_ERROR',{error:e.message});}finally{busy=false;}}
 await scan();const timer=setInterval(()=>void scan(),15000);event('SUPERVISOR_STARTED',{pid:process.pid});return {scan,timer,db,stop(){if([...children.values()].some(r=>r.active))throw new Error('Supervisor still owns active writers; shutdown refused');clearInterval(timer);db.prepare("DELETE FROM state WHERE key='supervisor-owner'").run();db.close();}};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))await supervise(path.resolve(process.argv[2]),path.resolve(process.argv[3]));

