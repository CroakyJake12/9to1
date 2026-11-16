import test from 'node:test';
import assert from 'node:assert/strict';
import {authRoute} from '../src/auth.mjs';
import {unseal} from '../src/security.mjs';
import {fetchRequest} from '../src/worker.mjs';
const key='local-files-entry-test-secret-not-production';
test('Files return address is an exact origin-bound allowlist entry',async()=>{
 for(const [host,target,expected] of [['9to1.uk','/files/','/files/'],['9to1.uk','//evil.test','/account'],['9to1.uk','https://evil.test/','/account'],['9to1.uk','/files/../admin','/account'],['cakemods.com','/files/','/account']]){
  const response=await authRoute(new Request(`https://${host}/auth/sign-in?return=${encodeURIComponent(target)}`),{NINE_CLIENT_ID:'nine',CAKE_CLIENT_ID:'cake',SESSION_KEY:key,ACCOUNTS_AVAILABLE:'private-testing'},`https://${host}`,host==='9to1.uk'?'nine':'cake');
  const cookie=response.headers.get('set-cookie').split(';')[0].split('=')[1];
  assert.equal((await unseal(cookie,key,`https://${host}`)).returnTo,expected);
 }
});
test('Files service receives only canonical 9to1 app/API routes and keeps its WASM policy',async()=>{
 const seen=[];const env={FILES:{fetch:async req=>{seen.push([new URL(req.url).pathname,req.method]);return new Response('files',{headers:{'content-security-policy':"script-src 'self' 'wasm-unsafe-eval'"}});}}};
 for(const path of ['/files/','/files/_framework/dotnet.js','/api/files/v1/operations']){
  const response=await fetchRequest(new Request('https://9to1.uk'+path,{method:path.includes('/api/')?'POST':'GET'}),env);
  assert.equal(await response.text(),'files');assert.ok(response.headers.get('content-security-policy').includes('wasm-unsafe-eval'));
 }
 assert.equal(seen.length,3);
 for(const path of ['/files-other/','/api/files/v2/items'])await fetchRequest(new Request('https://9to1.uk'+path),env);
 await fetchRequest(new Request('https://cakemods.com/files/'),env);
 assert.equal(seen.length,3);
});
test('Drive disclosure does not add live billing or public account admission',async()=>{
 const response=await fetchRequest(new Request('https://9to1.uk/files-privacy/'),{});const html=await response.text();
 assert.equal(response.status,200);assert.ok(html.includes('Google API Services User Data Policy'));
 assert.ok(html.includes('Usage counters remain'));assert.ok(html.includes('restricted to the operator'));
});
