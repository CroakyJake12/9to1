import assert from 'node:assert/strict';
import {configuration} from '../deployment/prepare.mjs';
for (const receipt of [{},{database_name:'cake'},{database_name:'cake-id-release-validation',database_id:'00000000-0000-0000-0000-000000000000'},{database_name:'cake-id-release-validation',database_id:'not-a-uuid'}]) assert.throws(()=>configuration(receipt));
// Synthetic fixture exercises validation only; this ID is never written or deployed.
const config=configuration({account_id:'a'.repeat(32),database_name:'cake-id-release-validation',database_id:'12345678-1234-1234-1234-123456789abc'});
assert.equal(config.main,'../src/index.ts');assert.equal(config.vars.EMAIL_CAPTURE,'false');assert.equal(config.vars.AUTH_BASE_URL,config.vars.API_RESOURCE);assert.equal(config.vars.ALLOWED_WEB_ORIGINS,config.vars.AUTH_BASE_URL);assert.equal(config.send_email,undefined);assert.deepEqual(config.secrets.required,['AUTH_SECRET','LOGIN_LIMITER_KEY']);
assert.equal(config.vars.LOGIN_MAX_ATTEMPTS,'8');assert.equal(config.vars.LOGIN_WINDOW_SECONDS,'900');assert.equal(config.vars.LOGIN_LOCK_SECONDS,'1800');assert.equal(config.workers_dev,false);assert.deepEqual(config.observability,{enabled:false});
console.log('PASS: 15 isolated deployment configuration assertions; synthetic receipt not deployed');
