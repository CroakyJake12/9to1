import test from 'node:test';
import assert from 'node:assert/strict';
import {seal,unseal,randomToken,challenge,allowedAccountRoute,boundedJson,setCookie,SESSION_COOKIE} from '../src/security.mjs';
import {authRoute} from '../src/auth.mjs';
import {fetchRequest} from '../src/worker.mjs';
import {SessionEngine} from '../src/session-engine.mjs';
const secret='local-test-only-not-a-production-secret-123456';

test('configured private testing offers one shared account entry without invitation signup',async()=>{
  for(const host of ['cakemods.com','9to1.uk']){
    const response=await fetchRequest(new Request('https://'+host+'/account'),{ACCOUNTS_AVAILABLE:'private-testing',CAKE_CLIENT_ID:'cake-client',NINE_CLIENT_ID:'nine-client'});
    const html=await response.text();
    assert.ok(html.includes('https://9to1.uk/auth/sign-in'));
    assert.ok(html.includes('New accounts and invitation redemption are closed.'));
    assert.ok(!html.includes('Use an invitation'));
    assert.ok(!html.includes('CakeMods account'));
  }
});
test('OAuth state is encrypted, authenticated and origin-bound',async()=>{const data={state:randomToken(),verifier:randomToken(),created:Date.now()};const sealed=await seal(data,secret,'https://cakemods.com');assert.deepEqual(await unseal(sealed,secret,'https://cakemods.com'),data);assert.equal(await unseal(sealed,secret,'https://9to1.uk'),null);assert.equal(await unseal(sealed+'x',secret,'https://cakemods.com'),null);assert.ok(!sealed.includes(data.verifier));});
test('PKCE matches RFC7636 known vector',async()=>{assert.equal(await challenge('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk'),'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM');});
test('opaque session cookie has secure host-only attributes',()=>{const c=setCookie(SESSION_COOKIE,randomToken());for(const flag of ['__Host-','Secure','HttpOnly','SameSite=Lax','Path=/'])assert.ok(c.includes(flag));assert.ok(!c.includes('Domain='));});
test('API routing cannot reach admin or arbitrary issuer endpoints',()=>{assert.equal(allowedAccountRoute('/api/auth/oauth2/create-client','POST'),false);assert.equal(allowedAccountRoute('/api/account/../auth/oauth2/create-client','POST'),false);assert.equal(allowedAccountRoute('/api/account/profile','PATCH'),true);assert.equal(allowedAccountRoute('/api/account/profile','DELETE'),false);});
test('bounded JSON rejects oversized streams',async()=>{assert.equal(await boundedJson(new Request('https://x.test',{method:'POST',body:'x'.repeat(20000)})),null);assert.deepEqual(await boundedJson(new Response('{"ok":true}')),{ok:true});});
test('cross-origin profile mutations are denied before session lookup',async()=>{const r=await authRoute(new Request('https://cakemods.com/api/account/profile',{method:'PATCH',headers:{origin:'https://evil.test','content-type':'application/json'},body:'{}'}),{},'https://cakemods.com','cake');assert.equal(r.status,403);});
test('login generates exact callback and does not accept an external next URL',async()=>{const r=await authRoute(new Request('https://cakemods.com/auth/sign-in?next=https://evil.test'),{CAKE_CLIENT_ID:'public-client',SESSION_KEY:secret,ACCOUNTS_AVAILABLE:'invite-only'},'https://cakemods.com','cake');const target=new URL(r.headers.get('location'));assert.equal(target.searchParams.get('redirect_uri'),'https://cakemods.com/oauth/callback');assert.equal(target.searchParams.get('code_challenge_method'),'S256');assert.ok(target.searchParams.get('scope').includes('offline_access'));assert.ok(r.headers.get('set-cookie').includes('Max-Age=600'));});
test('callback with wrong state cannot exchange a code',async()=>{let calls=0;const flow=await seal({state:'real',verifier:'v',created:Date.now()},secret,'https://cakemods.com');const r=await authRoute(new Request('https://cakemods.com/oauth/callback?state=wrong&code=foo',{headers:{cookie:'__Host-cake_oauth='+flow}}),{SESSION_KEY:secret,IDENTITY:{fetch(){calls++;}}},'https://cakemods.com','cake');assert.equal(calls,0);assert.equal(r.headers.get('location'),'/account?notice=invalid-login');assert.ok(r.headers.get('set-cookie').includes('Max-Age=0'));});
test('public pages, account and unknown route have correct cache and security',async()=>{for(const host of ['cakemods.com','9to1.uk']){const home=await fetchRequest(new Request('https://'+host+'/'),{});assert.equal(home.status,200);assert.ok((await home.text()).includes('9to1 Account'));assert.ok(home.headers.get('content-security-policy').includes("frame-ancestors 'none'"));const account=await fetchRequest(new Request('https://'+host+'/account'),{});assert.equal(account.headers.get('cache-control'),'no-store');assert.equal(account.headers.get('x-robots-tag'),'noindex');assert.equal((await fetchRequest(new Request('https://'+host+'/not-found/'),{})).status,404);}});
test('canonical redirects remain on intended site even for double-slash paths',async()=>{const r=await fetchRequest(new Request('https://cakemods.com//evil.test'),{});assert.equal(new URL(r.headers.get('location')).origin,'https://cakemods.com');const www=await fetchRequest(new Request('https://www.9to1.uk/apps/?x=1'),{});assert.equal(www.headers.get('location'),'https://9to1.uk/apps/?x=1');assert.equal((await fetchRequest(new Request('https://evil.test/'),{})).status,421);});
test('unauthenticated account API fails closed without leaking markup',async()=>{const r=await fetchRequest(new Request('https://9to1.uk/api/account/current'),{});assert.equal(r.status,401);assert.deepEqual(await r.json(),{error:'sign_in_required'});});
test('exports preserve private attachment headers and do not turn failures into downloads',async()=>{
 let response=Response.json({account:{name:'Test account'}},{headers:{'content-disposition':'attachment; filename="upstream.json"'}});
 const storage={async get(){return {access:'synthetic-token',refresh:'synthetic-refresh',clientId:'test',origin:'https://cakemods.com',expires:Date.now()+300000,deadline:Date.now()+600000};},async deleteAll(){}};
 const engine=new SessionEngine(storage,{IDENTITY:{fetch:async(url,options)=>{assert.equal(url,'https://cake-id-release-validation.jcbailey008.workers.dev/api/account/export');assert.equal(options.headers.authorization,'Bearer synthetic-token');return response;}}});
 const download=await engine.request('/api/account/export','GET',null,'https://cakemods.com');assert.equal(download.status,200);assert.equal(download.headers.get('content-disposition'),'attachment; filename="9to1-account-export.json"');assert.equal(download.headers.get('cache-control'),'private, no-store');assert.equal(download.headers.get('x-content-type-options'),'nosniff');
 response=Response.json({error:'export_requires_support'},{status:409});const failed=await engine.request('/api/account/export','GET',null,'https://cakemods.com');assert.equal(failed.status,409);assert.equal(failed.headers.get('content-disposition'),null);
});


test('brochure launch does not expose unready login, invitation or recovery journeys',async()=>{for(const host of ['cakemods.com','9to1.uk'])for(const path of ['/auth/sign-in','/auth/sign-up','/auth/recover']){const r=await fetchRequest(new Request('https://'+host+path),{CAKE_CLIENT_ID:'configured',NINE_CLIENT_ID:'configured',SESSION_KEY:secret});assert.equal(r.status,303);assert.equal(r.headers.get('location'),'/account?notice=not-ready');assert.equal(r.headers.get('set-cookie'),null);}const page=await fetchRequest(new Request('https://cakemods.com/account'),{});const html=await page.text();assert.ok(html.includes('being prepared for invited testing'));assert.ok(!html.includes('href="/auth/sign-in"'));});
