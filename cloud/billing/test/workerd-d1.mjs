import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {createHmac,randomBytes} from 'node:crypto';
const root=process.env.BILLING_TOOLCHAIN_ROOT;
if (!root) throw new Error('Explicit pinned toolchain root required');
const require=createRequire(resolve(root,'package.json'));
assert.equal(require('miniflare/package.json').version,'5.20261001.0-alpha');
const {Miniflare,convertV4MiniflareOptions}=require('miniflare');
const esbuild=require('esbuild');
assert.equal(esbuild.version,'0.28.1');
const controlBundle=await esbuild.build({entryPoints:[resolve('test/workerd-control.mjs')],bundle:true,format:'esm',platform:'browser',write:false});
const state=process.argv[2];
if (!state || !state.startsWith('/')) throw new Error('Fresh absolute fixture state required');
const secret=`whsec_${randomBytes(32).toString('hex')}`;
const binding={BILLING_DB:'billing-local'};
const config={modules:true,compatibilityDate:'2026-10-01',d1Databases:binding};
let mf,primary;
let checks=0;
const check=(actual,expected)=>{assert.deepEqual(actual,expected);checks++;};
const start=()=>new Miniflare(convertV4MiniflareOptions({telemetry:{enabled:false},resourcePersistencePath:state,workers:[
 {...config,name:'billing',scriptPath:resolve('src/stripe-webhook.mjs'),bindings:{STRIPE_MODE:'test',STRIPE_WEBHOOK_SECRET:secret,SIGNATURE_TOLERANCE_SECONDS:300,MAX_WEBHOOK_BYTES:65536}},
 {...config,name:'control',script:controlBundle.outputFiles[0].text}
]}));
const event=(id,created=10,livemode=false)=>({object:'event',id,type:'invoice.created',created,livemode,data:{object:{id:'in_fixture'}}});
const send=async(value,alterSignature=false)=>{
 const body=typeof value==='string'?value:JSON.stringify(value), t=Math.floor(Date.now()/1000);
 const signature=createHmac('sha256',secret).update(`${t}.${body}`).digest('hex');
 return mf.dispatchFetch('https://fixture.invalid/webhooks/stripe',{method:'POST',body,headers:{'stripe-signature':`t=${t},v1=${alterSignature?'0'.repeat(64):signature}`}});
};
const control=async(operation,...args)=>{
 const worker=await mf.getWorker('control');
 const response=await worker.fetch('https://control.invalid/',{method:'POST',body:JSON.stringify({operation,args})});
 check(response.status,200);return response.json();
};
try {
 mf=start();
 let db=await mf.getD1Database('BILLING_DB','billing');
 check((await send(event('evt_beforeSchema'))).status,503);
 if(process.argv[3]==='--negative-control')throw new Error('Intentional actual Worker fixture refusal; must never report PASS');
 const schema=readFileSync('schema/0001-stripe-inbox.sql','utf8');
 await db.prepare(schema).run();
 const concurrent=await Promise.all(Array.from({length:20},()=>send(event('evt_duplicate'))));
 check(concurrent.map(r=>r.status),Array(20).fill(202));
 check((await db.prepare('SELECT count(*) AS n FROM stripe_event_inbox').first()).n,1);
 check((await send({...event('evt_duplicate'),created:11})).status,409);
 check((await send(event('evt_badSignature'),true)).status,400);
 check((await send(event('evt_live',10,true))).status,400);
 check((await send('x'.repeat(65537))).status,413);
 check((await db.prepare('SELECT count(*) AS n FROM stripe_event_inbox').first()).n,1);
 await db.prepare(schema).run();
 check((await db.prepare('SELECT count(*) AS n FROM stripe_event_inbox').first()).n,1);
 await mf.dispose();mf=null;
 mf=start();db=await mf.getD1Database('BILLING_DB','billing');
 check((await send(event('evt_duplicate'))).status,202);
 check((await db.prepare('SELECT count(*) AS n FROM stripe_event_inbox').first()).n,1);
 check((await send(event('evt_newer',100))).status,202);
 check((await send(event('evt_older',1))).status,202);
 check((await db.prepare("SELECT count(*) AS n FROM stripe_event_inbox WHERE state='pending'").first()).n,3);
 const claims=await Promise.all(Array.from({length:10},()=>control('claim','test','evt_duplicate',100,10)));
 check(claims.filter(Boolean).length,1);
 const first=claims.find(Boolean);check(first.reconciliationRequired,true);
 check(await control('complete','test','evt_duplicate',first.claim_token,110),false);
 const retry=await control('claim','test','evt_duplicate',110,10);
 check(retry.attempts,2);
 check(await control('complete','test','evt_duplicate',first.claim_token,111),false);
 check(await control('fail','test','evt_duplicate',first.claim_token),false);
 check(await control('fail','test','evt_duplicate',retry.claim_token),true);
 const final=await control('claim','test','evt_duplicate',112,10);
 check(final.attempts,3);
 check(await control('complete','test','evt_duplicate',final.claim_token,113),true);
 check(await control('claim','test','evt_duplicate',114,10),null);
 check((await db.prepare("SELECT state,attempts FROM stripe_event_inbox WHERE event_id='evt_duplicate'").first()),{state:'processed',attempts:3});
} catch(error) {primary=error;}
let disposal;
try {if(mf) await mf.dispose();} catch(error){disposal=error;}
esbuild.stop();
if(primary || disposal) {
 console.error(primary);console.error(disposal);
 console.log(`BILLING_WORKER_RESULT=${JSON.stringify({passed:false,checks,pid:process.pid})}`);
 process.exitCode=1;
} else console.log(`BILLING_WORKER_RESULT=${JSON.stringify({passed:true,checks,pid:process.pid})}`);
