import {readFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import {request} from './client.mjs';
import {selectShift} from './policy.mjs';
import {receiveHandover} from './receiver.mjs';
const [command,configPath,arg,arg2]=process.argv.slice(2);const c=JSON.parse(await readFile(configPath,'utf8'));
if(command==='state')console.log(JSON.stringify(await request(c,'state'),null,2));
else if(command==='policy')console.log(JSON.stringify({configured:!!(await request(c,'policy',{policy:c.schedule})).policy}));
else if(command==='enqueue'){const task=JSON.parse(await readFile(arg,'utf8'));const state=await request(c,'enqueue',{task});console.log(JSON.stringify({queued:state.tasks.find(t=>t.id===task.id).id}));}
else if(command==='check-local'||command==='check-task'){
  const s=await request(c,'state');const at=Date.now();
  const shift=selectShift(c.schedule,{...s.presence,healthy:s.presence.healthy&&at-s.presence.seen_at<90000});
  if(shift.shift!=='local'||s.presence.paused||s.checkpoint_requested||s.lease?.worker!=='local'||s.lease.fence!==Number(arg)||s.lease.expires_at<=at)throw new Error('Local start denied by schedule, availability or exclusive lease');
  if(command==='check-task'&&!s.tasks.some(t=>t.id===arg2&&t.owner==='local'&&t.fence===Number(arg)&&t.status==='WORKING'))throw new Error('Task ownership not established');
  console.log('Local ownership and schedule validated');
}else if(command==='receive'){
  const received=await receiveHandover(c,arg);const r=received.receipt;
  console.log(JSON.stringify({acknowledged:r.handover_id,sha:r.source.sha,root:r.root}));
}else if(command==='heartbeat'){
  const fence=Number(arg);if(!Number.isSafeInteger(fence))throw new Error('Exact lease fence required');
  await request(c,'heartbeat',{fence});const timer=setInterval(async()=>{try{await request(c,'heartbeat',{fence});}catch(e){console.error(e.message);clearInterval(timer);process.exitCode=2;}},30000);
}else if(command==='summary'){
  const db=new DatabaseSync(c.state_db,{readOnly:true});const day=arg??new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/London'}).format(new Date());
  const dateFormat=new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/London'});
  const events=db.prepare('SELECT at,event,value FROM events ORDER BY id').all().filter(e=>dateFormat.format(new Date(e.at))===day).map(e=>({...e,value:JSON.parse(e.value)}));
  const summary={day,timezone:'Europe/London',changes:events.filter(x=>x.event==='REVIEWED_SOURCE_PUBLISHED'||x.event==='WORKER_FINISHED'&&x.value.status==='COMPLETED'),operations:events,unattended_switching:'NOT_VERIFIED'};db.close();console.log(JSON.stringify(summary,null,2));
}else throw new Error('Supported commands: state, policy, enqueue, check-local, check-task, receive, heartbeat, summary');
