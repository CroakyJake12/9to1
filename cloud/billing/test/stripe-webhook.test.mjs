import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { createHmac } from 'node:crypto';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { receiveWebhook } from '../src/stripe-webhook.mjs';
const secret = 'whsec_isolated_test_only';
const now = 1900000000;
const payload = overrides => JSON.stringify({ id:'evt_123', object:'event', type:'invoice.paid', created:now, livemode:false, data:{object:{id:'in_123'}}, ...overrides });
const signature = (body, timestamp = now) => `t=${timestamp},v1=${createHmac('sha256',secret).update(`${timestamp}.`).update(body).digest('hex')}`;
function adapter(db) {
  return { prepare(sql) { return { bind(...args) { return { async run() { return db.prepare(sql).run(...args); }, async first() { return db.prepare(sql).get(...args) ?? null; } }; } }; } };
}
function fixture(t) {
  const root = mkdtempSync(join(tmpdir(),'c3-billing-'));
  const file = join(root,'billing.sqlite');
  let db = new DatabaseSync(file);
  db.exec(readFileSync(new URL('../schema/0001-stripe-inbox.sql',import.meta.url),'utf8'));
  const env = { STRIPE_MODE:'test', STRIPE_WEBHOOK_SECRET:secret, SIGNATURE_TOLERANCE_SECONDS:300, MAX_WEBHOOK_BYTES:65536, BILLING_DB:adapter(db) };
  t.after(() => { db.close(); rmSync(root,{recursive:true}); });
  return { env, db:() => db, restart() { db.close(); db = new DatabaseSync(file); env.BILLING_DB = adapter(db); } };
}
function request(body, sig = signature(body)) { return new Request('https://billing.example/webhooks/stripe',{method:'POST',headers:{'stripe-signature':sig},body}); }
const count = f => f.db().prepare('SELECT count(*) n FROM stripe_event_inbox').get().n;
test('C3-WH-01: verified receipt survives restart; replay does not duplicate receipt (no allocation consumer exists)',async t=>{
 const f=fixture(t), body=payload();
 assert.equal((await receiveWebhook(request(body),f.env,now)).status,202);
 f.restart();
 assert.equal(count(f),1);
 const row=f.db().prepare('SELECT * FROM stripe_event_inbox').get();
 assert.equal(row.event_id,'evt_123'); assert.equal(row.state,'pending');
 assert.equal(row.provider_created,now); assert.equal(row.mode,'test');
 const results=await Promise.all(Array.from({length:20},()=>receiveWebhook(request(body),f.env,now)));
 assert.ok(results.every(r=>r.status===202)); assert.equal(count(f),1);
 assert.equal(f.db().prepare('SELECT state FROM stripe_event_inbox').get().state,'pending');
});
test('C3-WH-02: byte tampering, stale/future signatures and duplicate timestamps cannot persist',async t=>{
 const f=fixture(t),body=payload();
 for(const [b,s] of [[body+' ',signature(body)],[body,signature(body,now-301)],[body,signature(body,now+301)],[body,signature(body)+`,t=${now}`],[body,'t=bad,v1=00']]) {
  assert.equal((await receiveWebhook(request(b,s),f.env,now)).status,400); assert.equal(count(f),0);
 }
});
test('C3-WH-03: rotation accepts a matching v1 without JSON normalisation',async t=>{
 const f=fixture(t),body=payload()+'\n';
 const sig=signature(body).replace('v1=',`v1=${'00'.repeat(32)},v1=`);
 assert.equal((await receiveWebhook(request(body,sig),f.env,now)).status,202);assert.equal(count(f),1);
});
test('C3-WH-04: test/live separation fails closed, configuration required',async t=>{
 const f=fixture(t);
 assert.equal((await receiveWebhook(request(payload({livemode:true})),f.env,now)).status,400);assert.equal(count(f),0);
 for(const key of ['STRIPE_MODE','STRIPE_WEBHOOK_SECRET','BILLING_DB','SIGNATURE_TOLERANCE_SECONDS','MAX_WEBHOOK_BYTES']) {
  assert.equal((await receiveWebhook(request(payload()),{...f.env,[key]:undefined},now)).status,503);assert.equal(count(f),0);
 }
});
test('C3-WH-05: conflicting event identity cannot replace durable accepted facts',async t=>{
 const f=fixture(t);
 assert.equal((await receiveWebhook(request(payload()),f.env,now)).status,202);
 assert.equal((await receiveWebhook(request(payload({type:'invoice.voided'})),f.env,now)).status,409);
 assert.equal(count(f),1);assert.equal(f.db().prepare('SELECT event_type FROM stripe_event_inbox').get().event_type,'invoice.paid');
});
test('C3-WH-06: database failure does not acknowledge delivery; retry recovers',async t=>{
 const f=fixture(t),body=payload();
 const bad={...f.env,BILLING_DB:{prepare(){throw new Error('controlled database fault');}}};
 const r=await receiveWebhook(request(body),bad,now);assert.equal(r.status,503);assert.equal((await r.json()).retryable,true);assert.equal(count(f),0);
 assert.equal((await receiveWebhook(request(body),f.env,now)).status,202);assert.equal(count(f),1);
});
test('C3-WH-07: old events are receipts only and cannot overwrite newer subscription facts',async t=>{
 const f=fixture(t);
 for(const e of [{id:'evt_new',created:now},{id:'evt_old',created:now-10000}]) assert.equal((await receiveWebhook(request(payload(e)),f.env,now)).status,202);
 assert.equal(count(f),2);assert.deepEqual(f.db().prepare('SELECT DISTINCT state FROM stripe_event_inbox').all().map(x=>x.state),['pending']);
});
test('C3-WH-08: bounded body and malformed verified events leave no receipt',async t=>{
 const f=fixture(t);
 assert.equal((await receiveWebhook(request('x'.repeat(65537)),f.env,now)).status,413);
 for(const body of ['{','null',payload({created:-1}),payload({id:'../../etc/passwd'}),payload({livemode:'false'})]) assert.equal((await receiveWebhook(request(body),f.env,now)).status,400);
 assert.equal(count(f),0);
});

import { claimEvent, completeEvent, failEvent } from '../src/inbox-processing.mjs';
test('C3-WH-09: concurrent claims have one owner; stale claim cannot complete after restart/recovery',async t=>{
 const f=fixture(t);await receiveWebhook(request(payload()),f.env,now);
 const claims=await Promise.all(Array.from({length:20},()=>claimEvent(f.env.BILLING_DB,'test','evt_123',now,30)));
 const owners=claims.filter(Boolean);assert.equal(owners.length,1);assert.equal(owners[0].reconciliationRequired,true);
 f.restart();assert.equal(await claimEvent(f.env.BILLING_DB,'test','evt_123',now+29,30),null);
 const recovered=await claimEvent(f.env.BILLING_DB,'test','evt_123',now+30,30);
 assert.equal(recovered.attempts,2);assert.notEqual(recovered.claim_token,owners[0].claim_token);
 assert.equal(await completeEvent(f.env.BILLING_DB,'test','evt_123',owners[0].claim_token,now+31),false);
 assert.equal(await completeEvent(f.env.BILLING_DB,'test','evt_123',recovered.claim_token,now+31),true);
 assert.equal(await claimEvent(f.env.BILLING_DB,'test','evt_123',now+100,30),null);
 assert.equal(await completeEvent(f.env.BILLING_DB,'test','evt_123',recovered.claim_token,now+31),false);
});
test('C3-WH-10: failed consumer becomes reconciliation-required, mode/token cannot steal ownership',async t=>{
 const f=fixture(t);await receiveWebhook(request(payload()),f.env,now);
 const claim=await claimEvent(f.env.BILLING_DB,'test','evt_123',now,30);
 assert.equal(await failEvent(f.env.BILLING_DB,'live','evt_123',claim.claim_token),false);
 assert.equal(await failEvent(f.env.BILLING_DB,'test','evt_123','other'),false);
 assert.equal(await failEvent(f.env.BILLING_DB,'test','evt_123',claim.claim_token),true);
 f.restart();assert.equal(f.db().prepare('SELECT state FROM stripe_event_inbox').get().state,'reconciliation_required');
 const retry=await claimEvent(f.env.BILLING_DB,'test','evt_123',now+1,30);
 assert.equal(retry.attempts,2);assert.equal(retry.reconciliationRequired,true);
 assert.equal(await completeEvent(f.env.BILLING_DB,'test','evt_123',retry.claim_token,now+31),false);
});

import worker from '../src/stripe-webhook.mjs';
test('C3-WH-11: actual Worker fetch ignores execution context and durably accepts valid receipt',async t=>{
 const f=fixture(t),body=payload({id:'evt_fetch'}),time=Math.floor(Date.now()/1000);
 const response=await worker.fetch(request(body,signature(body,time)),f.env,{waitUntil(){throw new Error('unexpected background execution');}});
 assert.equal(response.status,202);assert.equal((await response.json()).code,'AcceptedPendingReconciliation');
 assert.equal(f.db().prepare('SELECT event_id FROM stripe_event_inbox').get().event_id,'evt_fetch');
});
test('C3-WH-12: lost acknowledgement after commit is safely retried against existing receipt',async t=>{
 const f=fixture(t),body=payload();let failed=false;
 const binding={prepare(sql){const prepared=f.env.BILLING_DB.prepare(sql);if(sql.startsWith('SELECT'))return {bind(){return {async first(){failed=true;throw new Error('controlled read failure after durable insert');}}}};return prepared;}};
 assert.equal((await receiveWebhook(request(body),{...f.env,BILLING_DB:binding},now)).status,503);
 assert.equal(failed,true);assert.equal(count(f),1);f.restart();
 assert.equal((await receiveWebhook(request(body),f.env,now)).status,202);assert.equal(count(f),1);
});
