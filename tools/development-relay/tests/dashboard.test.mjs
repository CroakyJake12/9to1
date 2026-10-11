import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile} from 'node:fs/promises';
import {createServer} from 'node:http';
import {generateKeyPairSync} from 'node:crypto';
import path from 'node:path';
import os from 'node:os';
import {LocalRunner} from '../runner.mjs';
import {git,sha} from '../git.mjs';

test('A stopped managed runner recovers an expired fence, but never replaces an unresolved writer',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-expired-')),key=path.join(root,'fixture.pem'),pair=generateKeyPairSync('rsa',{modulusLength:2048});await writeFile(key,pair.privateKey.export({type:'pkcs8',format:'pem'}));
 const schedule={timezone:'Europe/London',weekdays:Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}]))};let acquisitions=0,heartbeats=0,current={lease:{worker:'local',fence:12,expires_at:Date.now()-1000},tasks:[],handovers:[],presence:{home:true,paused:false}};
 const server=createServer((req,res)=>{req.resume();res.setHeader('content-type','application/json');if(req.url==='/acquire'){acquisitions++;current.lease={worker:'local',fence:13,expires_at:Date.now()+90000};}if(req.url==='/heartbeat'){heartbeats++;if(current.lease.expires_at<=Date.now()){res.statusCode=409;res.end(JSON.stringify({error:'Lease absent, expired or fenced'}));return;}}res.end(JSON.stringify({state:current}));});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 const runner=new LocalRunner({state_db:path.join(root,'state.db'),schedule,supervisor_managed:true,actor:'local',signing_key:key,coordinator_url:'http://127.0.0.1:'+server.address().port});runner.presence={home:true,paused:false};runner.draining=true;runner.health=async()=>({ready:true});
 try{await runner.tick();assert.equal(acquisitions,1);assert.equal(runner.remote.lease.fence,13);assert.equal(runner.error,null);assert.equal(runner.draining,false);current.lease.expires_at=Date.now()-1000;current.tasks=[{owner:'local',status:'RECOVERY_REQUIRED'}];await runner.tick();assert.equal(acquisitions,1);assert.match(runner.error,/expired/);current.tasks=[];current.handovers=[{from:'local',status:'READY'}];await runner.tick();assert.equal(acquisitions,1);assert.ok(heartbeats>=1);}finally{runner.db.close();await new Promise(r=>server.close(r));}
});

test('A private continuation keeps source and checkpoint locally without publishing or committing it',async()=>{
  const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-private-'));
  await git(root,['init']);await git(root,['config','user.name','Relay Temporary Test']);await git(root,['config','user.email','relay-test@example.invalid']);
  await writeFile(path.join(root,'owned.txt'),'baseline\n');await writeFile(path.join(root,'.gitignore'),'fixture.pem\nstate.db*\nlogs/\ncheckpoints/\n');await git(root,['add','owned.txt','.gitignore']);await git(root,['commit','-m','Private baseline fixture']);const base=await sha(root);
  const pair=generateKeyPairSync('rsa',{modulusLength:2048}),key=path.join(root,'fixture.pem');await writeFile(key,pair.privateKey.export({type:'pkcs8',format:'pem'}));
  let result;const server=createServer((req,res)=>{let body='';req.on('data',b=>body+=b);req.on('end',()=>{if(req.url==='/result')result=JSON.parse(body);res.setHeader('content-type','application/json');res.end(JSON.stringify({state:{}}));});});await new Promise(r=>server.listen(0,'127.0.0.1',r));
  const runner=new LocalRunner({state_db:path.join(root,'state.db'),logs:path.join(root,'logs'),checkpoints:path.join(root,'checkpoints'),actor:'local',signing_key:key,coordinator_url:'http://127.0.0.1:'+server.address().port,allowed_test_executables:['node'],remote:'no-remote-should-be-used'});
  runner.active={root,base,branch:'private-fixture',fence:3};runner.command=async()=>({code:0,log:'fixture-observed-command'});
  try{await writeFile(path.join(root,'owned.txt'),'private implementation\n');await runner.executeTask({id:'private',local_only:true,objective:'Preserve application work',source:{sha:base},paths:['owned.txt'],tests:[{executable:'node',args:[]}]},3);assert.equal(result.status,'CHECKPOINT');assert.equal(result.result.private_source,true);assert.equal(await sha(root),base);assert.ok(result.result.checkpoint);assert.equal(result.result.source,undefined);assert.equal(runner.recovery,null);
    assert.equal(result.result.checkpoint,path.basename(result.result.checkpoint));assert.equal(result.result.checkpoint_ref_kind,'LOCAL_PRIVATE_CHECKPOINT_ID');
    await writeFile(path.join(root,'owned.txt'),'baseline\n');runner.active={root,base,branch:'private-fixture',fence:3};await runner.executeTask({id:'empty',local_only:true,objective:'Actual implementation required',source:{sha:base},paths:['owned.txt'],tests:[{executable:'node',args:[]}]},3);assert.equal(result.status,'BLOCKED');assert.equal(result.acceptance,'NOT_RUN');assert.match(result.result.blockers[0],/No implementation source changes/);assert.equal(result.result.checkpoint,path.basename(result.result.checkpoint));
  }finally{runner.db.close();await new Promise(r=>server.close(r));}
});

test('An exact drained cloud ACK during renewal clears handover drain, while unexplained lease loss stays blocked',async()=>{
  const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-transfer-'));
  const pair=generateKeyPairSync('rsa',{modulusLength:2048}),key=path.join(root,'fixture.pem');await writeFile(key,pair.privateKey.export({type:'pkcs8',format:'pem'}));
  const schedule={timezone:'Europe/London',weekdays:Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}]))};
  const source={sha:'a'.repeat(40)},handover={id:'accepted',from:'local',to:'cloud',fence:7,revision:4,status:'ACKNOWLEDGED',source,ack:{worker:'cloud',sha:source.sha,revision:4}};let current={lease:null,handovers:[handover]},heartbeats=0;
  const server=createServer((req,res)=>{req.resume();res.setHeader('content-type','application/json');if(req.url==='/heartbeat'){heartbeats++;res.statusCode=409;res.end(JSON.stringify({error:'Lease absent, expired or fenced'}));}else res.end(JSON.stringify({state:current}));});await new Promise(r=>server.listen(0,'127.0.0.1',r));
  const runner=new LocalRunner({state_db:path.join(root,'state.db'),logs:path.join(root,'logs'),schedule,actor:'local',signing_key:key,coordinator_url:'http://127.0.0.1:'+server.address().port});runner.presence={home:true,paused:false};
  try{runner.remote={lease:{worker:'local',fence:7}};runner.draining=true;await runner.renew();assert.equal(runner.draining,false);assert.equal(runner.remote.lease,null);assert.equal(heartbeats,0);current={lease:null,handovers:[]};runner.remote={lease:{worker:'local',fence:8}};await runner.renew();assert.equal(runner.draining,true);assert.match(runner.error,/ownership changed/);current={lease:null,handovers:[handover]};runner.active={task_id:'still-writing'};runner.remote={lease:{worker:'local',fence:7}};await runner.renew();assert.equal(runner.draining,true);assert.equal(runner.active.task_id,'still-writing');}finally{runner.db.close();await new Promise(r=>server.close(r));}
});

test('A noninteractive child reading redirected stdin receives EOF and its positional prompt',async()=>{
  const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-eof-'));
  const runner=new LocalRunner({state_db:path.join(root,'state.db'),logs:path.join(root,'logs')});
  try{const result=await runner.command(process.execPath,['-e',"process.stdin.resume();process.stdin.on('end',()=>process.stdout.write(process.argv[1]))",'owned prompt'],root,3000,'stdin-eof');assert.equal(result.code,0);assert.equal(result.stdout,'owned prompt');}finally{runner.db.close();}
});
test('Actual dashboard HTTP controls persist, fail closed, prevent CSRF and reject duplicate listener',async()=>{
  const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-ui-'));
  const config={state_db:path.join(root,'state.db'),dashboard_port:18891,checkpoints:path.join(root,'checkpoints'),logs:path.join(root,'logs'),schedule:{timezone:'Europe/London',weekdays:Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}]))}};
  let runner=await new LocalRunner(config).start();const base='http://127.0.0.1:18891';
  try{
    const html=await(await fetch(base)).text();const csrf=html.match(/name="relay-csrf" content="([^"]+)"/)[1];
    const control=async a=>fetch(base+'/control/'+a,{method:'POST',headers:{origin:base,'x-relay-csrf':csrf}});
    assert.equal((await fetch(base+'/control/home',{method:'POST'})).status,403);
    for(const [action,property,expected] of [['home','home',true],['away','home',false],['pause','paused',true],['resume','paused',false]]){assert.equal((await control(action)).status,200);assert.equal((await(await fetch(base+'/status')).json()).presence[property],expected);}
    assert.equal((await control('checkpoint')).status,200);assert.equal((await control('handover')).status,409);assert.equal(runner.active,null);
    const duplicate=new LocalRunner({...config,state_db:path.join(root,'other.db')});await assert.rejects(()=>duplicate.start(),{code:'EADDRINUSE'});duplicate.db.close();
    await control('home');await runner.stop();runner=await new LocalRunner(config).start();assert.equal(runner.presence.home,true);assert.equal(runner.remote,null);
  }finally{await runner.stop();}
});
