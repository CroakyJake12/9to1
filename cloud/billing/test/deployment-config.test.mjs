import test from 'node:test';
import assert from 'node:assert/strict';
import {deploymentConfig} from '../scripts/deployment-config.mjs';
const explicit = {accountId:'a'.repeat(32),workerName:'billing-test',databaseId:'11111111-2222-3333-4444-555555555555',databaseName:'billing-test',stripeMode:'test',signatureToleranceSeconds:300,maxWebhookBytes:65536,cpuMs:100,workersDev:false};
test('explicit verified identities produce numeric Worker bindings without secret values',()=>{
 const config=deploymentConfig(explicit);
 assert.equal(config.vars.MAX_WEBHOOK_BYTES,65536);
 assert.equal(config.d1_databases[0].binding,'BILLING_DB');
 assert.equal(config.workers_dev,false);
 assert.equal(config.preview_urls,false,'Version preview URLs must remain closed independently of workers.dev');
 assert.equal(deploymentConfig({...explicit,workersDev:true}).preview_urls,false);
 assert.deepEqual(config.secrets,{required:['STRIPE_WEBHOOK_SECRET']});
 assert.equal(config.observability.enabled,false);
});
test('missing, extra, placeholder, invalid mode and unbounded inputs refuse preparation',()=>{
 for (const key of Object.keys(explicit)) { const missing={...explicit};delete missing[key];assert.throws(()=>deploymentConfig(missing)); }
 for (const delta of [{secret:'whsec_never_accept'},{databaseId:'00000000-0000-0000-0000-000000000000'},{stripeMode:'production'},{cpuMs:300001},{maxWebhookBytes:0},{workersDev:'false'},{signatureToleranceSeconds:NaN},{accountId:'unverified'}]) assert.throws(()=>deploymentConfig({...explicit,...delta}));
});
