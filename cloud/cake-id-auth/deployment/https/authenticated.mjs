import nodeAssert from 'node:assert/strict';
import {randomBytes,createHash} from 'node:crypto';
import {open} from 'node:fs/promises';
import {constants} from 'node:fs';
import {pathToFileURL} from 'node:url';
import {dirname,join} from 'node:path';
import {createAuthClient} from 'better-auth/client';
import {oauthProviderClient} from '@better-auth/oauth-provider/client';
import {createLocalJWKSet,jwtVerify} from 'jose';
import {origin,defaultDeploymentVersion,deploymentPin} from './validate.mjs';

const issuer=`${origin}/api/auth`;
const callback='https://client.example.test:5096/callback';
const scopes='openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke';
export async function privateFixture(path) {
  const file=await open(path,constants.O_RDONLY|constants.O_NOFOLLOW);
  let primary,result;
  try {
    const stat=await file.stat();
    nodeAssert.ok(stat.isFile()&&(stat.mode&0o077)===0&&stat.size<=65536,'Fixture must be a private bounded regular file');
    if(process.getuid)nodeAssert.equal(stat.uid,process.getuid(),'Fixture owner must be current operator');
    const fixture=JSON.parse(await file.readFile('utf8'));
    nodeAssert.equal(fixture.origin,origin);nodeAssert.equal(fixture.callback,callback);
    nodeAssert.ok(/^[a-zA-Z0-9_-]{20,80}$/.test(fixture.clientId));
    nodeAssert.equal(fixture.accounts.length,2);
    nodeAssert.equal(new Set(fixture.accounts.map(a=>a.id)).size,2);
    for(const account of fixture.accounts) {
      nodeAssert.match(account.id,/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
      nodeAssert.match(account.email,/^cake-https-[a-f0-9]+-[12]@example\.test$/);
      nodeAssert.ok(typeof account.password==='string'&&account.password.length>=32&&account.password.length<=128);
    }
    result=fixture;
  } catch(error){primary=error;}
  let closing;try{await file.close();}catch(error){closing=error;}
  if(primary&&closing)throw new AggregateError([primary,closing],'Private fixture read and close failed');if(primary)throw primary;if(closing)throw closing;return result;
}
export async function authenticated(fixture,{request=fetch,deploymentVersion=defaultDeploymentVersion}={}) {
  const operatorPinnedDeploymentVersion=deploymentPin(deploymentVersion);
  let count=0;const assert=new Proxy(nodeAssert,{get(target,key){const fn=target[key];return typeof fn==='function'? (...args)=>{count++;return fn(...args)}:fn}});
  const cookies=new Map();let activeURL=new URL(origin);
  const savedFetch=globalThis.fetch,savedWindow=globalThis.window;
  const observations=[];
  let requestCount=0;
  async function wire(input,init={},withCookies=true) {
    const incoming=input instanceof Request?input:new Request(input,init);
    const url=new URL(incoming.url);assert.equal(url.origin,origin,'Only exact isolated issuer receives requests');
    const headers=new Headers(incoming.headers);headers.set('origin',origin);
    if(withCookies&&cookies.size)headers.set('cookie',[...cookies].map(([k,v])=>`${k}=${v}`).join('; '));
    assert.ok(++requestCount<=60,'Bounded phase request budget exceeded');
    const response=await request(new Request(incoming,{...init,headers,redirect:'manual',credentials:'omit',signal:AbortSignal.timeout(15000)}));
    observations.push({path:url.pathname,method:incoming.method,status:response.status});
    if(withCookies)for(const cookie of response.headers.getSetCookie?.()??[]) {
      const pair=cookie.split(';',1)[0],index=pair.indexOf('=');if(index<1)continue;
      const name=pair.slice(0,index);if(/max-age=0/i.test(cookie))cookies.delete(name);else cookies.set(name,pair.slice(index+1));
    }
    return response;
  }
  let client;
  const response=(path,init={})=>wire(new URL(path,origin),init);
  const json=(path,method,body)=>response(path,{method,headers:{'content-type':'application/json'},body:JSON.stringify(body)});
  const api=(path,token,init={})=>wire(new URL(path,origin),{...init,headers:{...init.headers,authorization:`Bearer ${token}`}},false);
  const token=(body)=>wire(`${issuer}/oauth2/token`,{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:new URLSearchParams(body)},false);
  async function navigation(reply) {
    const location=reply.headers.get('location');if(location)return new URL(location,origin);
    assert.equal(reply.status,200);const body=await reply.json();assert.equal(body.redirect,true);assert.equal(typeof body.url,'string');return new URL(body.url,origin);
  }
  async function flow(account,login=false,requested=scopes) {
    const verifier=randomBytes(48).toString('base64url'),state=randomBytes(20).toString('base64url'),nonce=randomBytes(20).toString('base64url');
    const query=new URLSearchParams({response_type:'code',client_id:fixture.clientId,redirect_uri:callback,scope:requested,state,nonce,code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256',resource:origin});
    if(login)cookies.clear();
    activeURL=await navigation(await response(`/api/auth/oauth2/authorize?${query}`));
    if(login) {
      assert.equal(activeURL.origin,origin);assert.equal(activeURL.pathname,'/sign-in');
      const signed=await client.signIn.email({email:account.email,password:account.password});assert.equal(signed.error,null);assert.ok(cookies.size>0);
      if(signed.data?.url)activeURL=new URL(signed.data.url,origin);
    }
    if(activeURL.origin!==new URL(callback).origin&&activeURL.pathname!=='/consent')activeURL=await navigation(await wire(activeURL));
    if(activeURL.origin===origin&&activeURL.pathname==='/consent') {
      const page=await wire(activeURL);assert.equal(page.status,200);assert.match(await page.text(),/Authorize application/);
      // Maintained client extracts actual server-signed oauth_query from current URL.
      const consent=await client.oauth2.consent({accept:true});assert.equal(consent.error,null);assert.equal(typeof consent.data?.url,'string');activeURL=new URL(consent.data.url,origin);
    }
    assert.equal(`${activeURL.origin}${activeURL.pathname}`,callback);assert.equal(activeURL.searchParams.get('state'),state);assert.ok(activeURL.searchParams.get('code'));
    return {code:activeURL.searchParams.get('code'),verifier,nonce};
  }
  const exchange=f=>token({grant_type:'authorization_code',client_id:fixture.clientId,code:f.code,code_verifier:f.verifier,redirect_uri:callback,resource:origin});
  try {
    globalThis.fetch=(input,init)=>wire(input,init);
    globalThis.window={location:{get href(){return activeURL.href},set href(value){activeURL=new URL(value,origin)},get search(){return activeURL.search},get pathname(){return activeURL.pathname},assign(value){activeURL=new URL(value,origin)}}};
    client=createAuthClient({baseURL:issuer,plugins:[oauthProviderClient()]});
    const metadata=await (await response('/api/auth/.well-known/openid-configuration')).json();assert.equal(metadata.issuer,issuer);assert.equal(metadata.token_endpoint,`${issuer}/oauth2/token`);assert.equal(metadata.jwks_uri,`${issuer}/jwks`);
    const keys=await (await response('/api/auth/jwks')).json(),jwks=createLocalJWKSet(keys);
    const bad=await flow(fixture.accounts[0],true);
    const wrong=await exchange({...bad,verifier:randomBytes(48).toString('base64url')});assert.equal(wrong.status,401);
    const consumed=await exchange(bad);assert.equal(consumed.status,400);assert.equal((await consumed.json()).error,'invalid_grant');
    const fresh=await flow(fixture.accounts[0]);const exchanged=await exchange(fresh);assert.equal(exchanged.status,200);const tokens=await exchanged.json();
    const {payload:id}=await jwtVerify(tokens.id_token,jwks,{issuer,audience:fixture.clientId});const {payload:access}=await jwtVerify(tokens.access_token,jwks,{issuer,audience:origin});
    assert.equal(id.sub,fixture.accounts[0].id);assert.equal(access.sub,id.sub);assert.equal(id.nonce,fresh.nonce);assert.ok(access.sid&&access.sid!==access.sub);assert.match(access.scope,/cake:profile:write/);assert.doesNotMatch(access.scope,/business|entitlement|role/i);
    const current=await api('/api/account/current',tokens.access_token);assert.equal(current.status,200);assert.equal((await current.json()).accountId,id.sub);
    assert.equal((await api('/api/account/current',tokens.id_token)).status,401,'Genuine ID token cannot replace API access token');
    const profile=(await (await api('/api/account/profile',tokens.access_token)).json()).profile;assert.equal(profile.accountId,id.sub);
    const updated=await api('/api/account/profile',tokens.access_token,{method:'PATCH',headers:{'content-type':'application/json'},body:JSON.stringify({expectedRevision:profile.revision,fields:{name:'Synthetic HTTPS updated'}})});assert.equal(updated.status,200);assert.equal((await updated.json()).profile.revision,profile.revision+1);
    assert.equal((await api('/api/account/profile',tokens.access_token,{method:'PATCH',headers:{'content-type':'application/json'},body:JSON.stringify({expectedRevision:profile.revision,fields:{name:'Stale isolated update'}})})).status,409);
    const refresh=await token({grant_type:'refresh_token',client_id:fixture.clientId,refresh_token:tokens.refresh_token,resource:origin});assert.equal(refresh.status,200);const rotated=await refresh.json();assert.ok(rotated.refresh_token);assert.notEqual(rotated.refresh_token,tokens.refresh_token);
    const {payload:rotatedClaims}=await jwtVerify(rotated.access_token,jwks,{issuer,audience:origin});assert.equal(rotatedClaims.sub,id.sub);assert.equal(rotatedClaims.sid,access.sid);
    const readOnly=await flow(fixture.accounts[0],false,'openid cake:account:read');const readOnlyResponse=await exchange(readOnly);assert.equal(readOnlyResponse.status,200);const readOnlyTokens=await readOnlyResponse.json();assert.equal((await api('/api/account/profile',readOnlyTokens.access_token)).status,401,'Actual reduced-scope token cannot read profile');
    const firstCookies=new Map(cookies);const other=await flow(fixture.accounts[1],true);const otherExchange=await exchange(other);assert.equal(otherExchange.status,200);const otherTokens=await otherExchange.json();
    const {payload:otherClaims}=await jwtVerify(otherTokens.access_token,jwks,{issuer,audience:origin});assert.equal(otherClaims.sub,fixture.accounts[1].id);
    const otherCurrent=await response('/api/account/current');assert.equal(otherCurrent.status,200);assert.equal((await otherCurrent.json()).accountId,fixture.accounts[1].id);
    assert.equal((await api(`/api/account/sessions/${otherClaims.sid}`,tokens.access_token,{method:'DELETE'})).status,404,'First account cannot revoke another account session');
    const oldOtherCookies=new Map(cookies);activeURL=new URL(origin);const newLogin=await client.signIn.email({email:fixture.accounts[1].email,password:fixture.accounts[1].password});assert.equal(newLogin.error,null);
    const newest=(await (await response('/api/auth/get-session')).json()).session;const newestCookies=new Map(cookies);
    const before=(await (await response('/api/account/sessions')).json()).sessions;assert.ok(before.some(s=>s.sessionId===otherClaims.sid));assert.ok(before.some(s=>s.sessionId===newest.id));
    assert.equal((await json('/api/account/revoke-other-sessions','POST',{})).status,204);
    const after=(await (await response('/api/account/sessions')).json()).sessions;assert.equal(after.length,1);assert.equal(after[0].sessionId,newest.id);
    cookies.clear();for(const [k,v]of oldOtherCookies)cookies.set(k,v);assert.equal((await response('/api/account/current')).status,401);
    cookies.clear();for(const [k,v]of newestCookies)cookies.set(k,v);assert.equal((await response('/api/account/current')).status,200);
    assert.equal((await json('/api/account/signout','POST',{})).status,204);assert.equal((await response('/api/account/current')).status,401);
    cookies.clear();for(const [k,v]of firstCookies)cookies.set(k,v);
    const sessions=(await (await api('/api/account/sessions',tokens.access_token)).json()).sessions;assert.ok(sessions.some(s=>s.sessionId===access.sid&&s.accountId===id.sub));
    assert.equal((await api(`/api/account/sessions/${access.sid}`,tokens.access_token,{method:'DELETE'})).status,204);
    assert.equal((await api('/api/account/current',tokens.access_token)).status,401);
    const revokedRefresh=await token({grant_type:'refresh_token',client_id:fixture.clientId,refresh_token:rotated.refresh_token,resource:origin});assert.equal(revokedRefresh.status,400);assert.equal((await revokedRefresh.json()).error,'invalid_grant');
    return {result:'passed',assertions:count,observations,operatorPinnedDeploymentVersion,qualification:'Actual Node HTTPS transport with maintained client hooks and real library tokens; seeded fictional identities/client are preconditions. No browser UI/native/registration/mail/admin provisioning acceptance.'};
  } finally {globalThis.fetch=savedFetch;globalThis.window=savedWindow;cookies.clear();}
}
export async function runAuthenticated(fixturePath,resultPath,{journey=authenticated,deploymentVersion=defaultDeploymentVersion}={}) {
  const operatorPinnedDeploymentVersion=deploymentPin(deploymentVersion);
  // Acquire the new evidence file before any authenticated request/mutation.
  const file=await open(resultPath,'wx',0o600);let primary,result;
  try {const fixture=await privateFixture(fixturePath);result=await journey(fixture,{deploymentVersion:operatorPinnedDeploymentVersion});await file.writeFile(JSON.stringify(result,null,2)+'\n');await file.sync();}catch(error){primary=error;}
  let closing;try{await file.close();}catch(error){closing=error;}
  if(primary&&closing)throw new AggregateError([primary,closing],'Journey/evidence and close failed');if(primary)throw primary;if(closing)throw closing;return result;
}
if(process.argv[1]&&pathToFileURL(process.argv[1]).href===import.meta.url) {
  if(![4,5].includes(process.argv.length))throw new Error('Usage: node --use-env-proxy --use-system-ca deployment/https/authenticated.mjs PRIVATE_FIXTURE.json NEW_PUBLIC_RESULT.json [PROVIDER_VERSION_UUID]');
  try {const result=await runAuthenticated(process.argv[2],process.argv[3],{deploymentVersion:process.argv[4]});console.log(JSON.stringify({result:result.result,assertions:result.assertions}));}
  catch(error){
    let evidenceError;const filename=`private-run-error-${randomBytes(8).toString('hex')}.json`;
    const describe=e=>({name:e.name,message:e.message,stack:e.stack,...(e instanceof AggregateError?{errors:e.errors.map(describe)}:{})});
    try {const f=await open(join(dirname(process.argv[2]),filename),'wx',0o600);let primary;try{await f.writeFile(JSON.stringify(describe(error),null,2)+'\n');await f.sync();}catch(failure){primary=failure;}let closing;try{await f.close();}catch(failure){closing=failure;}if(primary&&closing)throw new AggregateError([primary,closing],'Private diagnostics write and close failed');if(primary)throw primary;if(closing)throw closing;}catch(failure){evidenceError=failure;}
    console.error(JSON.stringify({result:'failed',name:error.name,privateDiagnosticFile:evidenceError?null:filename,evidenceFailure:evidenceError?.name??null,qualification:'Private original/independent-error diagnostics must never be published'}));process.exitCode=1;
  }
}
