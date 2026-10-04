import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {configuration} from '../deployment/prepare.mjs';
for (const receipt of [{},{database_name:'cake'},{database_name:'cake-id-release-validation',database_id:'00000000-0000-0000-0000-000000000000'},{database_name:'cake-id-release-validation',database_id:'not-a-uuid'}]) assert.throws(()=>configuration(receipt));
// Synthetic fixture exercises validation only; this ID is never written or deployed.
const config=configuration({account_id:'a'.repeat(32),database_name:'cake-id-release-validation',database_id:'12345678-1234-1234-1234-123456789abc'});
assert.equal(config.main,'../src/index.ts');assert.equal(config.vars.EMAIL_CAPTURE,'false');assert.equal(config.vars.AUTH_BASE_URL,config.vars.API_RESOURCE);assert.equal(config.vars.ALLOWED_WEB_ORIGINS,config.vars.AUTH_BASE_URL);assert.equal(config.send_email,undefined);assert.deepEqual(config.secrets.required,['AUTH_SECRET','LOGIN_LIMITER_KEY']);
assert.equal(config.vars.LOGIN_MAX_ATTEMPTS,'8');assert.equal(config.vars.LOGIN_WINDOW_SECONDS,'900');assert.equal(config.vars.LOGIN_LOCK_SECONDS,'1800');assert.equal(config.workers_dev,false);assert.deepEqual(config.observability,{enabled:false});
const build=JSON.parse(await readFile(new URL('../deployment/wrangler.build.json',import.meta.url),'utf8'));
for(const candidate of [config,build]) {
  assert.deepEqual(candidate.compatibility_flags,['nodejs_compat','global_fetch_strictly_public']);
  assert.deepEqual(candidate.limits,{cpu_ms:1000});assert.equal(candidate.preview_urls,false);
  assert.equal(candidate.workers_dev,false);assert.deepEqual(candidate.observability,{enabled:false});
  assert.equal(candidate.main,'../src/index.ts');
  assert.equal(candidate.vars.APP_MODE,'production');assert.equal(candidate.vars.EMAIL_CAPTURE,'false');assert.equal(candidate.send_email,undefined);
}
assert.deepEqual(config.d1_databases,[{binding:'DB',database_name:'cake-id-release-validation',database_id:'12345678-1234-1234-1234-123456789abc',migrations_dir:'../migrations'}]);
console.log('PASS: isolated config guards plus public self-fetch, unchanged CPU/bindings/secrets/exposure controls; synthetic receipt not deployed');
