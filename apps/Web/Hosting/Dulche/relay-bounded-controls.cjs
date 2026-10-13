const { Miniflare, convertV4MiniflareOptions } = require('miniflare');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const accountId='11111111-1111-4111-8111-111111111111'; let revoked=false; let accountRequests=0;
const lease='22222222-2222-4222-8222-222222222222';
const mf=new Miniflare(convertV4MiniflareOptions({modules:true,scriptPath:require('node:path').join(__dirname,'relay-test-entry.bundle.mjs'),compatibilityDate:'2026-10-08',compatibilityFlags:['enable_request_signal'],
 durableObjects:{RUNTIME_RELAY:{className:'RuntimeRelay',useSQLite:true}},bindings:{ACCOUNT_ORIGIN:'https://account.test',PREVIEW_ORIGIN:'https://preview.test',MODEL_ID:'qwen3:1.7b'},
 serviceBindings:{ACCOUNT_AUTH:async req=>(accountRequests++,req.headers.get('Authorization'))==='Bearer controlled-fixture'&&!revoked?Response.json({accountId,displayName:'Isolated control'}):Response.json({error:'unauthenticated'},{status:401})}}));
const checks=[]; checks.push = value => { console.log('CONTROL', value); return Array.prototype.push.call(checks,value); };

let baseURL; const request=(path,options={})=>fetch(new URL(path,baseURL),options);
const connector=(path,body,jobId,sequence)=>request('/connector/'+path,{method:'POST',headers:{'X-Connector-Lease':lease,...(jobId?{'X-Job-Id':jobId}:{}),...(sequence!==undefined?{'X-Chunk-Sequence':String(sequence)}:{})},body:typeof body==='string'?body:body===undefined?undefined:JSON.stringify(body)});
async function sourceFinalizationControls(){
const path=require('node:path').join(__dirname,'relay-worker.mjs');
const source=fs.readFileSync(path,'utf8');
const exact=source.slice(source.indexOf('export async function dispatchAuthorized'),source.indexOf('// One coordination atom'));
const helper=source.slice(source.indexOf('const json ='),source.indexOf('const uuid ='));
const {dispatchAuthorized}=await import('data:text/javascript;base64,'+Buffer.from(helper+exact).toString('base64'));
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return{promise,resolve};};
const run=stub=>dispatchAuthorized(new Request('https://private.example/api/tags'),{RUNTIME_RELAY:{getByName:()=>stub}},{waitUntil:p=>p.catch(()=>{})});
const results=[];
{
  const entered=deferred(),held=deferred();let retired=0,releases=0,cancelled=false;
  const response=await run({dispatch:async request=>new Response(new ReadableStream({start(c){c.close();}}),{headers:{'X-Original-Relay-Admitted':request.headers.get('X-Original-Relay-Job')}}),
    retireOriginal:async()=>{retired++;assert.equal(releases,0);},
    releaseOriginal:async()=>{releases++;entered.resolve();await held.promise;}});
  await entered.promise;
  const cancelling=response.body.cancel('Stop at EOF').then(()=>cancelled=true);
  await new Promise(r=>setTimeout(r,0));assert.equal(cancelled,false,'Stop must join held original release');
  held.resolve();await cancelling;assert.equal(retired,1);assert.equal(releases,1);
  results.push('EOF and concurrent Stop share one retained retire/release; Stop awaits held release');
}
{
  let calls=0;
  const response=await run({dispatch:async()=>new Response('unauthorized',{status:401}),retireOriginal:()=>{calls++;throw new Error('No original');},releaseOriginal:()=>{calls++;throw new Error('No original');}});
  assert.equal(response.status,401);assert.equal(calls,0);
  results.push('Pre-admission401 issues no invented runtime retirement or release');
}
{
  const entered=deferred(),held=deferred();let released=0,returned=false;
  const pending=run({dispatch:async request=>new Response('runtime refused',{status:502,headers:{'X-Original-Relay-Admitted':request.headers.get('X-Original-Relay-Job')}}),
    retireOriginal:async()=>{entered.resolve();await held.promise;},releaseOriginal:async()=>{released++;}}).then(response=>{returned=true;return response;});
  await entered.promise;await new Promise(r=>setTimeout(r,0));assert.equal(returned,false);assert.equal(released,0);
  held.resolve();const response=await pending;assert.equal(response.status,502);assert.equal(released,1);
  results.push('Exact admitted runtime502 awaits original terminal and releases its slot once');
}
return results;
}
(async()=>{
 const sourceFinalizationChecks=await sourceFinalizationControls();console.log(JSON.stringify({scope:"Exact original RPC boundary source controls; no network/authentication acceptance",sourceFinalizationChecks}));
 baseURL = await mf.ready;
 assert.equal((await request('/api/tags')).status,503); checks.push('No connector refuses inference');
 const poll=connector('poll');
 await new Promise(r=>setTimeout(r,20));
 assert.equal((await request('/api/tags')).status,401); checks.push('No bearer refuses authenticated queue');
 const admissionStart=accountRequests, held=[];
 const slow=Array.from({length:4},()=>request('/api/chat',{method:'POST',headers:{Authorization:'Bearer controlled-fixture'},duplex:'half',body:new ReadableStream({start(controller){controller.enqueue(new TextEncoder().encode('{'));held.push(controller);}})}));
 for(let i=0;i<100&&accountRequests<admissionStart+4;i++) await new Promise(resolve=>setTimeout(resolve,10));
 assert.equal(accountRequests,admissionStart+4); assert.equal((await request('/api/tags',{headers:{Authorization:'Bearer controlled-fixture'}})).status,429);
 for(const controller of held){controller.enqueue(new TextEncoder().encode('}'));controller.close();}
 for(const response of await Promise.all(slow))assert.equal(response.status,400);
 checks.push('Four held request bodies reserve admission before awaiting; fifth refused');
 const opening=request('/api/tags',{headers:{Authorization:'Bearer controlled-fixture'}}); opening.then(async r => {if(r.status!==200) console.log('opening-status',r.status,await r.clone().text());});
 console.log('WAIT job'); const {job}=await(await poll).json(); console.log('HAVE job',!!job); assert.equal(job.path,'/api/tags'); assert.equal(job.accountId,accountId);
 assert.equal((await connector('headers',{status:200,contentType:'application/json'},job.id)).status,200);
 const chunk=connector('chunk','{"models":[',job.id,0); chunk.then(async r=>{ if(r.status!==200) console.log('CHUNK ERROR',r.status,await r.clone().text()); });
 console.log('WAIT response'); const response=await opening,reader=response.body.getReader(); console.log('HAVE response');
 console.log('WAIT chunk'); const first=await reader.read(); console.log('HAVE chunk');assert.equal(new TextDecoder().decode(first.value),'{"models":[');assert.equal((await chunk).status,200);
 assert.equal((await connector('chunk','x',job.id,0)).status,409); checks.push('Original bytes and sequence preserved');
 const quarantineStart=accountRequests; let quarantineBody;
 const quarantineAdmission=request('/api/chat',{method:'POST',headers:{Authorization:'Bearer controlled-fixture'},duplex:'half',body:new ReadableStream({start(controller){quarantineBody=controller;controller.enqueue(new TextEncoder().encode('{'));}})});
 for(let i=0;i<100&&accountRequests<quarantineStart+1;i++)await new Promise(resolve=>setTimeout(resolve,10));
 assert.equal(accountRequests,quarantineStart+1);
 revoked=true;
 const refused=connector('chunk',']}',job.id,1);
 assert.equal((await refused).status,401); const stopping = reader.read().then(value=>({value}),error=>({error}));
 const notices=await(await connector('events')).json();assert.equal(notices.events[0].jobId,job.id);assert.equal(notices.events[0].cancel,true);
 assert.equal((await connector('terminal',{completed:false,cleanupSettled:false},job.id)).status,502);
 quarantineBody.enqueue(new TextEncoder().encode(JSON.stringify({model:'qwen3:1.7b',messages:[{role:'user',content:'isolated quarantine control'}],stream:true}).slice(1)));quarantineBody.close();
 assert.equal((await quarantineAdmission).status,503);checks.push('Cleanup uncertainty fences already-admitted held bodies before dispatch');
 assert.equal((await request('/api/tags',{headers:{Authorization:'Bearer controlled-fixture'}})).status,503);
 assert.equal((await connector('terminal',{completed:false,cleanupSettled:true},job.id)).status,200);
 const stopped=await stopping; assert.ok(stopped.error||stopped.value.done);
 checks.push('Known original cleanup failure blocks new work until actual later acknowledgement');
 checks.push('Authority revocation drops late bytes and signals exact original job; EOF requires canonical completion guard');
 revoked=false;
 revoked=false;
 for(let index=0;index<6;index++){
   const failurePoll=connector('poll'), failureOpening=request('/api/tags',{headers:{Authorization:'Bearer controlled-fixture'}});
   const {job:failureJob}=await(await failurePoll).json();assert.ok(failureJob?.id);
   assert.equal((await connector('terminal',{completed:false,cleanupSettled:true},failureJob.id)).status,200);
   assert.equal((await failureOpening).status,502);
 }
 checks.push('Six claimed pre-header provider failures join and release without exhausting admission');
 const poll3=connector('poll'), opening3=request('/test/direct-chat',{method:'POST',headers:{Authorization:'Bearer controlled-fixture'},body:JSON.stringify({model:'qwen3:1.7b',messages:[{role:'user',content:'isolated exact terminal control'}],stream:true})});
 const job3=(await(await poll3).json()).job; await connector('headers',{status:200,contentType:'application/x-ndjson'},job3.id);
 const doneBytes=connector('chunk','{"done":true}\n',job3.id,0);const response3=await opening3;const reader3=response3.body.getReader();assert.equal(new TextDecoder().decode((await reader3.read()).value),'{"done":true}\n');await doneBytes;
 assert.equal((await connector('terminal',{completed:true,cleanupSettled:true},job3.id)).status,200);
 assert.equal((await request('/test/retire?id='+job3.id)).status,200);
 assert.equal((await request('/test/retire?id=99999999-9999-4999-8999-999999999999')).status,409);
 await reader3.cancel();checks.push('Exact original completed receipt joins after done:true and terminal; unknown ID refused');

 fs.writeFileSync(process.env.RELAY_EVIDENCE_PATH ?? require('node:path').join(__dirname,'relay-controls-result.json'),JSON.stringify({time:new Date().toISOString(),scope:'Actual Workerd/DO with isolated account protocol fixture; no hosted auth acceptance',checks,sourceFinalizationChecks,limitations:['HTTP client abort propagation is not qualified by this local bridge','No signed-in production inference acceptance'],passed:true},null,2));console.log(JSON.stringify({passed:true,checks,sourceFinalizationChecks}));
})().catch(error=>{console.error(error);process.exitCode=1;}).finally(()=>mf.dispose());
