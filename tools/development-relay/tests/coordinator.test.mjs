import test from 'node:test';
import assert from 'node:assert/strict';
import {Miniflare,convertV4MiniflareOptions} from 'miniflare';
import {generateKeyPairSync,createHash,randomUUID,sign} from 'node:crypto';
import {mkdtemp,readFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const pair=generateKeyPairSync('rsa',{modulusLength:2048});const publicKey=pair.publicKey.export({type:'spki',format:'der'}).toString('base64');
function headers(method,route,body,nonce=randomUUID(),stamp=String(Date.now()),actor='local'){return {'content-type':'application/json','x-relay-key':actor,'x-relay-time':stamp,'x-relay-nonce':nonce,'x-relay-signature':sign('RSA-SHA256',Buffer.from(`${stamp}\n${nonce}\n${method}\n${route}\n${createHash('sha256').update(body).digest('hex')}`),pair.privateKey).toString('base64')};}

test('Signed HTTP expired return rejects incomplete proofs and preserves exact custody after main advances',async()=>{
  const source={repository:'CroakyJake12/9to1',branch:'main',sha:'a'.repeat(40)},digest='b'.repeat(64),head='c'.repeat(40);
  const state={version:1,fence:15,lease:{worker:'local',fence:15,expires_at:1},presence:{home:true,healthy:true,paused:false,seen_at:1},events:[],tasks:[{id:'returned',status:'COMPLETED',owner:'cloud',fence:14,source},{id:'stale',status:'WORKING',owner:'local',fence:15,source}],handovers:[{id:'return',status:'READY',from:'cloud',to:'local',fence:14,revision:6,source,tasks:['returned'],artifact_hashes:{'evidence/report.json':digest}}]};
  const fixture="import {RelayCoordinator} from '../coordinator.mjs';export {default} from '../coordinator.mjs';export class RecoveryFixture extends RelayCoordinator{constructor(ctx,env){super(ctx,env);if(!ctx.storage.sql.exec('SELECT value FROM relay_state WHERE id=1').toArray().length)ctx.storage.sql.exec('INSERT INTO relay_state VALUES(1,?)',env.TEST_STATE);}}";
  const options=convertV4MiniflareOptions({modules:[{type:'ESModule',path:path.join(here,'recovery-http-fixture.mjs'),contents:fixture},{type:'ESModule',path:path.join(here,'../coordinator.mjs')},{type:'ESModule',path:path.join(here,'../policy.mjs')},{type:'ESModule',path:path.join(here,'../git-remote.mjs')}],modulesRoot:path.join(here,'..'),compatibilityDate:'2026-10-11',durableObjects:{COORDINATOR:{className:'RecoveryFixture',useSQLite:true,unsafeUniqueKey:'relay-expired-http-return'}},bindings:{PUBLIC_KEYS:JSON.stringify({local:publicKey,cloud:publicKey}),TEST_STATE:JSON.stringify(state)},outboundService:async request=>{const url=new URL(request.url);if(url.pathname.endsWith('/git/ref/heads/main'))return Response.json({object:{sha:head}});assert.equal(url.pathname,`/repos/CroakyJake12/9to1/compare/${source.sha}...${head}`);return Response.json({status:'ahead',merge_base_commit:{sha:source.sha}});}});
  const mf=new Miniflare(options),proof={handover_id:'return',revision:6,reconstructed_sha:source.sha,verified_artifact_hashes:{'evidence/report.json':digest},stopped_tasks:[{task_id:'stale',fence:15,source_sha:source.sha,owned_processes_ended:true,checkpoint_id:'preserved',checkpoint_manifest_sha256:'d'.repeat(64)}]};
  const call=async(route,p,actor='local')=>{const body=JSON.stringify(p);return mf.dispatchFetch('http://relay.test'+route,{method:'POST',body,headers:headers('POST',route,body,randomUUID(),String(Date.now()),actor)});};
  try{
    assert.equal((await mf.dispatchFetch('http://relay.test/ack-recovery',{method:'POST',body:JSON.stringify(proof)})).status,401);
    assert.equal((await call('/ack-recovery',proof,'cloud')).status,409);
    assert.equal((await call('/ack-recovery',{...proof,stopped_tasks:[]})).status,409);
    const unchanged=(await(await call('/state',{})).json()).state;assert.equal(unchanged.lease.fence,15);assert.equal(unchanged.handovers[0].status,'READY');assert.equal(unchanged.tasks[1].status,'WORKING');
    const accepted=await call('/ack-recovery',proof),result=await accepted.json();assert.equal(accepted.status,200,JSON.stringify(result));assert.equal(result.state.lease,null);assert.equal(result.state.handovers[0].source.sha,source.sha);assert.equal(result.state.handovers[0].recovery.previous_lease.fence,15);assert.equal(result.state.tasks[1].status,'BLOCKED');
    assert.equal((await call('/ack-recovery',proof)).status,409);
  }finally{await mf.dispose();}
});

test('Actual HTTP handover verifies the remote ref and exact cloud acknowledgement transfers custody',async()=>{
  const persist=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-http-'));
  const source={repository:'CroakyJake12/9to1',branch:'relay/source',sha:'a'.repeat(40)};
  const options=convertV4MiniflareOptions({modules:[{type:'ESModule',path:path.join(here,'../coordinator.mjs')},{type:'ESModule',path:path.join(here,'../policy.mjs')},{type:'ESModule',path:path.join(here,'../git-remote.mjs')}],modulesRoot:path.join(here,'..'),compatibilityDate:'2026-10-06',resourcePersistencePath:persist,durableObjects:{COORDINATOR:{className:'RelayCoordinator',useSQLite:true,unsafeUniqueKey:'relay-http-handover-test'}},bindings:{PUBLIC_KEYS:JSON.stringify({local:publicKey,cloud:publicKey})},outboundService:async request=>{assert.equal(new URL(request.url).pathname,'/repos/CroakyJake12/9to1/git/ref/heads/relay/source');return Response.json({object:{sha:source.sha}});}});
  const mf=new Miniflare(options);
  const call=async(route,p,actor='local')=>{const body=JSON.stringify(p);const r=await mf.dispatchFetch('http://relay.test'+route,{method:'POST',body,headers:headers('POST',route,body,randomUUID(),String(Date.now()),actor)});const data=await r.json();assert.equal(r.status,200,JSON.stringify(data));return data.state;};
  try{
    await call('/presence',{home:true,healthy:true,local_permitted:true,local_until:Date.now()+30000});
    await call('/enqueue',{task:{id:'http-one',objective:'Exact transfer',paths:['tools/development-relay'],source,tests:[{executable:'node',args:['--test']}]}});
    const s=await call('/acquire',{});await call('/claim',{fence:s.lease.fence,task_id:'http-one'});
    await call('/result',{fence:s.lease.fence,task_id:'http-one',status:'CHECKPOINT',evidence:[],acceptance:'NOT_RUN'});
    const id=randomUUID();const ready=await call('/handover',{fence:s.lease.fence,handover:{id,from:'local',to:'cloud',source,tasks:['http-one'],tests:[],blockers:[]}});
    const ack=await call('/ack',{handover_id:id,revision:ready.handovers[0].revision,reconstructed_sha:source.sha},'cloud');assert.equal(ack.lease,null);assert.equal(ack.handovers[0].status,'ACKNOWLEDGED');assert.equal(ack.tasks[0].owner,null);
  }finally{await mf.dispose();}
});
test('Actual Durable Object runtime: authenticated exclusive arbitration, replay rejection and persistent recovery',async()=>{
  const persist=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-do-'));
  const options=convertV4MiniflareOptions({modules:[{type:'ESModule',path:path.join(here,'../coordinator.mjs')},{type:'ESModule',path:path.join(here,'../policy.mjs')},{type:'ESModule',path:path.join(here,'../git-remote.mjs')}],modulesRoot:path.join(here,'..'),compatibilityDate:'2026-10-06',resourcePersistencePath:persist,durableObjects:{COORDINATOR:{className:'RelayCoordinator',useSQLite:true,unsafeUniqueKey:'relay-runtime-persistence-test'}},bindings:{PUBLIC_KEYS:JSON.stringify({local:publicKey})}});
  let mf=new Miniflare(options);
  try{
    const call=async(route,p,custom)=>{const method=p===undefined?'GET':'POST',body=p===undefined?'':JSON.stringify(p);return mf.dispatchFetch('http://relay.test'+route,{method,headers:custom??headers(method,route,body),...(p===undefined?{}:{body})});};
    assert.equal((await call('/state',undefined,{})).status,401);
    const policy={timezone:'Europe/London',weekdays:Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}]))};
    assert.equal((await call('/policy',{policy})).status,200);assert.equal((await call('/presence',{home:true,healthy:true})).status,200);
    const claims=await Promise.all(Array.from({length:20},()=>call('/acquire',{})));assert.equal(claims.filter(r=>r.status===200).length,1);assert.equal(claims.filter(r=>r.status===409).length,19);
    const body=JSON.stringify({fence:1}),signed=headers('POST','/heartbeat',body);assert.equal((await call('/heartbeat',{fence:1},signed)).status,200);assert.equal((await call('/heartbeat',{fence:1},signed)).status,409);
    assert.equal((await call('/heartbeat',{fence:999})).status,409);
    assert.equal((await call('/heartbeat',{fence:1},{...headers('POST','/heartbeat',body),'x-relay-time':'NaN'})).status,401);
    await mf.dispose();mf=new Miniflare(options);const recovered=await(await call('/state')).json();assert.equal(recovered.state.lease.fence,1);assert.equal(recovered.state.lease.worker,'local');
  }finally{await mf.dispose();}
});
