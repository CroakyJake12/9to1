// Supporting UNIT negatives only. Scripted discovery and popup events are not issuer/browser/backend acceptance.
import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { BrowserPublicClient, createConfiguredAccounts, handleOAuthPopupCallback } from './configured-accounts.bundle.js';
const origin = 'https://client.example.test:5096';
const config = { issuer: 'http://127.0.0.1:8798/api/auth', apiResource: 'http://127.0.0.1:8798', clientId: 'unit-public-config',
  redirectUri: origin + '/callback', scopes: 'openid cake:account:read', allowLoopbackForIsolatedTests: true };
const metadata = { issuer: config.issuer, authorization_endpoint: config.issuer + '/oauth2/authorize', token_endpoint: config.issuer + '/oauth2/token', jwks_uri: config.issuer + '/jwks' };
function WindowFixture() {
  const listeners = new Set(); let navigate;
  const navigation = new Promise(resolve => { navigate = resolve; });
  const popup = { closed: false, location: { replace(url) { popup.url = url; navigate(url); } }, close() { popup.closed = true; } };
  return { location: { origin }, popup, navigation, open() { return popup; }, addEventListener(type, fn) { if(type === 'message') listeners.add(fn); }, removeEventListener(type, fn) { listeners.delete(fn); }, emit(data, source = popup, messageOrigin = origin) { for(const listener of [...listeners]) listener({ source, origin: messageOrigin, data }); } };
}
function Client(win, fetch, extra = {}) {
  return new BrowserPublicClient({ configuration: config, window: win, crypto: webcrypto, fetch,
    onBeforeSignIn: async()=>{}, verifyCurrentAccount: async()=>assert.fail('scripted negatives cannot verify account'), onVerifiedIdentity: async()=>assert.fail('scripted negatives cannot register identity'),
    onSignInFailed: async()=>{}, onTokenExpired: async()=>{}, onFailure: ()=>{}, ...extra });
}
test('null host configuration retains unavailable transport and no sign-in authority', async()=> {
  let callbacks=0;const accounts=createConfiguredAccounts({ configuration:null, window:{location:{origin}}, onPrivateContextInvalidated:()=>{callbacks++;},onVerifiedIdentity:()=>assert.fail(),onFailure:()=>{} });
  await accounts.prepare();assert.equal(accounts.signInAvailable(),false);const result=JSON.parse(await accounts.invoke('id','GetCurrent','null'));assert.equal(result.error.code,'ServiceUnavailable');assert.equal(callbacks,0);accounts.dispose();
});
test('invalid remote HTTP/cross-origin callback/client secret config fails before network',()=> {
  let requests=0;for(const change of [{issuer:'http://other.example.test/api/auth'},{redirectUri:'https://other.example.test/callback'},{clientSecret:'must-not-be-configured'}]) assert.throws(()=>Client(WindowFixture(),()=>{requests++;},{configuration:{...config,...change}}), {code:'InvalidConfiguration'});assert.equal(requests,0);
});
test('configured but tokenless actions require proven private cleanup and never grant identity', async()=> {
  let cleanups=0, network=0;const accounts=createConfiguredAccounts({configuration:config,window:WindowFixture(),crypto:webcrypto,fetch:()=>{network++;throw Error();},onPrivateContextInvalidated:async()=>{cleanups++;},onVerifiedIdentity:()=>assert.fail(),onFailure:()=>{} });
  assert.equal(JSON.parse(await accounts.invoke('before','GetCurrent','null')).error.code,'PrivateContextNotPrepared');await accounts.prepare();assert.equal(accounts.signInAvailable(),true);
  assert.equal(JSON.parse(await accounts.invoke('after','GetCurrent','null')).error.code,'AuthenticationRequired');assert.equal(cleanups,1);assert.equal(network,0);accounts.dispose();
});
test('unconfigured private lifetime invalidation awaits owner cleanup and remains unavailable',async()=> {
  let release,entered=false;const gate=new Promise(resolve=>release=resolve);
  const accounts=createConfiguredAccounts({configuration:null,window:WindowFixture(),onPrivateContextInvalidated:async reason=>{assert.equal(reason,'bfcache_restored');entered=true;await gate;},onVerifiedIdentity:()=>assert.fail(),onFailure:()=>{}});
  let finished=false;const invalidation=accounts.invalidatePrivateContext('bfcache_restored').then(()=>finished=true);await Promise.resolve();assert.equal(entered,true);assert.equal(finished,false);release();await invalidation;
  assert.equal(accounts.signInAvailable(),false);assert.equal(JSON.parse(await accounts.invoke('read','GetCurrent','null')).error.code,'ServiceUnavailable');accounts.dispose();
});
test('configured private lifetime invalidation aborts pending sign-in and preserves prepared ability',async()=> {
  const win=WindowFixture();let reasons=[];let started;const requested=new Promise(resolve=>started=resolve);
  const accounts=createConfiguredAccounts({configuration:config,window:win,crypto:webcrypto,fetch:async(_url,options)=>{
    started();await new Promise((_resolve,reject)=>options.signal.addEventListener('abort',()=>reject(new DOMException('Aborted','AbortError')),{once:true}));
  },onPrivateContextInvalidated:async reason=>{reasons.push(reason);},onVerifiedIdentity:()=>assert.fail('no unverified account registration'),onFailure:()=>{}});
  try {
    await accounts.prepare();const signin=accounts.requestSignIn('pending');await requested;await accounts.invalidatePrivateContext('bfcache_restored');const result=JSON.parse(await signin);
    assert.equal(result.ok,false);assert.equal(result.error.code,'Cancelled');assert.equal(win.popup.closed,true);assert.equal(accounts.signInAvailable(),true);assert.ok(reasons.includes('bfcache_restored'));
    assert.equal(JSON.parse(await accounts.invoke('read','GetCurrent','null')).error.code,'AuthenticationRequired');
  } finally { accounts.dispose(); }
});
test('callback forwards only to same-origin parent and strips one-use code without running app',()=> {
  let sent, replaced;const win={location:{origin,pathname:'/callback',href:origin+'/callback?state=unit&code=unit-negative'},history:{replaceState(_a,_b,value){replaced=value;}},opener:{closed:false,location:{origin},postMessage(value,target){sent={value,target};}}};
  assert.equal(handleOAuthPopupCallback(config,win),true);assert.equal(sent.target,origin);assert.equal(sent.value.type,'nineToOne.oauth.callback');assert.equal(replaced,'/callback');
  win.opener.location.origin='https://foreign.example.test';assert.throws(()=>handleOAuthPopupCallback(config,win),{code:'CallbackParentUnavailable'});
});
test('discovery issuer mismatch rejects and closes owned popup with cleanup', async()=> {
  const win=WindowFixture();let cleanups=0;const client=Client(win,async()=>new Response(JSON.stringify({...metadata,issuer:'https://wrong.example.test'})),{onSignInFailed:async()=>{cleanups++;}});
  await assert.rejects(client.signIn(),{code:'IssuerMismatch'});assert.equal(win.popup.closed,true);assert.equal(client.getAccessToken(),null);assert.equal(cleanups,1);client.dispose();
});
test('fresh S256/state/nonce and wrong state reject before code exchange', async()=> {
  const win=WindowFixture();let requests=0;const client=Client(win,async()=>{requests++;return new Response(JSON.stringify(metadata));});const flow=client.signIn();const url=new URL(await win.navigation);
  assert.equal(url.searchParams.get('code_challenge_method'),'S256');assert.ok(url.searchParams.get('code_challenge').length>=43);assert.notEqual(url.searchParams.get('state'),url.searchParams.get('nonce'));
  win.emit({type:'nineToOne.oauth.callback',callbackUri:origin+'/callback?state=wrong&code=unit-negative'});await assert.rejects(flow,{code:'StateMismatch'});assert.equal(requests,1);assert.equal(client.getAccessToken(),null);client.dispose();
});
test('foreign source and origin cannot consume actual pending callback',async()=> {
  const win=WindowFixture();const client=Client(win,async()=>new Response(JSON.stringify(metadata)));const flow=client.signIn();await win.navigation;
  win.emit({type:'nineToOne.oauth.callback',callbackUri:origin+'/callback?state=wrong&code=unit'}, {}, origin);win.emit({type:'nineToOne.oauth.callback',callbackUri:origin+'/callback?state=wrong&code=unit'},win.popup,'https://foreign.example.test');
  assert.equal(win.popup.closed,false);win.emit({type:'nineToOne.oauth.callback',callbackUri:origin+'/callback?state=wrong&code=unit'});await assert.rejects(flow,{code:'StateMismatch'});client.dispose();
});
test('caller cancellation closes popup and leaves no access token',async()=> {
  const win=WindowFixture();const client=Client(win,async()=>new Response(JSON.stringify(metadata)));const controller=new AbortController();const flow=client.signIn({signal:controller.signal});await win.navigation;controller.abort();await assert.rejects(flow,{code:'Cancelled'});assert.equal(win.popup.closed,true);assert.equal(client.getAccessToken(),null);client.dispose();
});
test('duplicate callback cannot exchange code twice and unsigned token never grants account',async()=> {
  const win=WindowFixture();let exchanges=0;const client=Client(win,async(url)=>{if(url===metadata.token_endpoint){exchanges++;return new Response(JSON.stringify({token_type:'Bearer',access_token:'invalid-negative',id_token:'invalid-negative'}));}return new Response(JSON.stringify(metadata));});
  const flow=client.signIn();const url=new URL(await win.navigation);const event={type:'nineToOne.oauth.callback',callbackUri:origin+'/callback?state='+url.searchParams.get('state')+'&code=unit-negative'};win.emit(event);win.emit(event);await assert.rejects(flow,{code:'TokenVerificationFailed'});assert.equal(exchanges,1);assert.equal(client.getAccessToken(),null);client.dispose();
});
