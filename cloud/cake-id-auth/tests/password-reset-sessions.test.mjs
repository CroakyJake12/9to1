import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {createServer} from 'node:http';
import {readFileSync} from 'node:fs';
import {randomBytes,createHash} from 'node:crypto';
import {build} from 'esbuild';
import {decodeJwt} from 'jose';

// The opt-in baseline modes exist only to record the before-fix behavior.
// Normal test runs always require the complete recovery boundary.
const mode=process.env.NINE_PASSWORD_RESET_CHECK??'fixed';
assert.ok(['fixed','baseline','flag-only'].includes(mode));
const built=await build({stdin:{contents:`export {createAuthOptions} from './src/auth.ts';export {handleResourceApi} from './src/resource-api.ts';export {betterAuth} from 'better-auth';`,resolveDir:process.cwd()},bundle:true,write:false,platform:'node',format:'esm',banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"}});
const {createAuthOptions,handleResourceApi,betterAuth}=await import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'));
const scopes='openid profile email offline_access cake:account:read cake:profile:read';
let fixtureNumber=0;

async function fixture(){
 const fixtureAddress=`198.51.100.${++fixtureNumber}`;
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 for(const name of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql','0003_turnstile_redemptions.sql','0004_account_invitations.sql','0005_public_account_policy.sql','0006_password_reset_fence.sql'])db.exec(readFileSync('migrations/'+name,'utf8'));
 let auth,env,origin;
 const server=createServer(async(req,res)=>{
  try{
   const chunks=[];for await(const chunk of req)chunks.push(chunk);
   const body=Buffer.concat(chunks);const request=new Request(origin+req.url,{method:req.method,headers:req.headers,...(body.length?{body}:{} )});
   const response=new URL(request.url).pathname.startsWith('/api/account/')?await handleResourceApi(request,env,auth):await auth.handler(request);
   res.statusCode=response.status;for(const[key,value]of response.headers)if(key!=='set-cookie')res.setHeader(key,value);
   const cookies=response.headers.getSetCookie();if(cookies.length)res.setHeader('set-cookie',cookies);
   res.end(Buffer.from(await response.arrayBuffer()));
  }catch{res.statusCode=500;res.end('isolated fixture failure');}
 });
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
 origin=`http://127.0.0.1:${server.address().port}`;
 const batchCalls=[];
 const DB={prepare(sql){return {bind(...values){return {sql,values,first:async()=>db.prepare(sql).get(...values)??null,all:async()=>({success:true,results:db.prepare(sql).all(...values)}),run:async()=>({success:true,meta:db.prepare(sql).run(...values)})};}};},async batch(statements){
  batchCalls.push(statements.map(({sql,values})=>({sql,values})));db.exec('BEGIN');try{const result=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return result;}catch(error){db.exec('ROLLBACK');throw error;}
 }};
 env={DB,APP_MODE:'local-test-only',AUTH_BASE_URL:origin,API_RESOURCE:origin,AUTH_SECRET:randomBytes(48).toString('base64url'),EMAIL_CAPTURE:'true'};
 const options=createAuthOptions(env);
 if(mode!=='fixed'){
  assert.equal(options.emailAndPassword.onPasswordReset,undefined,'baseline probe must run before adding the reset cleanup');
  assert.notEqual(options.emailAndPassword.revokeSessionsOnPasswordReset,true,'baseline probe must run before adding the revocation flag');
  if(mode==='flag-only')options.emailAndPassword.revokeSessionsOnPasswordReset=true;
 }
 auth=betterAuth({...options,database:db});
 const context=await auth.$context;
 const accounts={};
 for(const[label,role]of [['owner','admin'],['other','user']]){
  const id=crypto.randomUUID(),password=randomBytes(32).toString('base64url'),email=label+'@example.test';
  const hash=await context.password.hash(password);
  db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,role) VALUES(?,?,?,1,?,?,?)').run(id,label,email,'2026-01-01','2026-01-01',role);
  db.prepare('INSERT INTO account(id,accountId,providerId,userId,password,createdAt,updatedAt) VALUES(?,?,?,?,?,?,?)').run(crypto.randomUUID(),id,'credential',id,hash,'2026-01-01','2026-01-01');
  db.prepare('UPDATE user SET username=? WHERE id=?').run(label,id);
  accounts[label]={id,password,email,username:label};
 }
 async function request(path,{method='GET',body,headers={},cookie=''}={}){
  const content=body===undefined?undefined:headers['content-type']==='application/x-www-form-urlencoded'?new URLSearchParams(body).toString():JSON.stringify(body);
  return fetch(origin+path,{method,redirect:'manual',headers:{'cf-connecting-ip':fixtureAddress,...(method==='POST'?{origin,'content-type':'application/json'}:{}),...(cookie?{cookie}:{}),...headers},...(content===undefined?{}:{body:content})});
 }
 function browser(){
  const cookies=new Map();return {get cookie(){return [...cookies].map(([key,value])=>key+'='+value).join('; ');},async request(path,options={}){
   const response=await request(path,{...options,cookie:this.cookie});for(const header of response.headers.getSetCookie()){const pair=header.split(';',1)[0],index=pair.indexOf('=');const name=pair.slice(0,index),value=pair.slice(index+1);if(value)cookies.set(name,value);else cookies.delete(name);}
   return response;
  }};
 }
 async function login(account,password=account.password){const page=browser();const response=await page.request('/api/auth/sign-in/email',{method:'POST',body:{email:account.email,password}});assert.equal(response.status,200,'legitimate password sign-in succeeds');assert.ok(page.cookie);return page;}
 async function validSession(page){const response=await page.request('/api/auth/get-session');return response.status===200&&!!(await response.json())?.session;}
 async function tokens(page,clientId,resource=true,hold=false,clientSecret){
  const verifier=randomBytes(48).toString('base64url'),state=randomBytes(20).toString('base64url');
  const parameters={response_type:'code',client_id:clientId,redirect_uri:'https://client.example.test/callback',scope:scopes,state,nonce:randomBytes(20).toString('base64url'),code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256',...(resource?{resource:origin}:{})};
  let response=await page.request('/api/auth/oauth2/authorize?'+new URLSearchParams(parameters),{headers:{accept:'text/html','sec-fetch-mode':'navigate'}});
  let location=response.headers.get('location');if(!location&&response.status===200)location=(await response.json()).url;
  assert.ok(location,'real authorization returns its redirect');let destination=new URL(location,origin);
  if(destination.pathname==='/consent'){
   response=await page.request('/api/auth/oauth2/consent',{method:'POST',body:{accept:true,oauth_query:destination.search.slice(1)}});assert.equal(response.status,200,'real signed consent succeeds');destination=new URL((await response.json()).url);
  }
  assert.equal(destination.origin,'https://client.example.test');assert.equal(destination.searchParams.get('state'),state);const code=destination.searchParams.get('code');assert.ok(code);
  if(hold)return {code,verifier,clientId,resource};
  response=await request('/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{grant_type:'authorization_code',client_id:clientId,...(clientSecret?{client_secret:clientSecret}:{}),code,code_verifier:verifier,redirect_uri:parameters.redirect_uri,...(resource?{resource:origin}:{})}});
  assert.equal(response.status,200,'real PKCE code exchange succeeds');const issued=await response.json();assert.equal(typeof issued.refresh_token,'string');assert.equal(typeof issued.access_token,'string');return issued;
 }
 const refresh=(clientId,refreshToken,clientSecret)=>request('/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{grant_type:'refresh_token',client_id:clientId,...(clientSecret?{client_secret:clientSecret}:{}),refresh_token:refreshToken,resource:origin}});
 const current=accessToken=>request('/api/account/current',{headers:{authorization:'Bearer '+accessToken}});
 async function resetToken(email){const response=await request('/api/auth/request-password-reset',{method:'POST',body:{email,redirectTo:origin+'/reset-password'}});assert.equal(response.status,200);const mail=db.prepare("SELECT body FROM cake_email_outbox WHERE recipient=? AND subject LIKE 'Reset%' ORDER BY id DESC LIMIT 1").get(email);assert.ok(mail);const link=mail.body.match(/http:\/\/\S+/)?.[0];assert.ok(link);return new URL(link).pathname.split('/').at(-1);}
 return {db,env,auth,context,provider:options.plugins.find(plugin=>plugin.id==='oauth-provider'),accounts,login,validSession,tokens,refresh,current,request,resetToken,batchCalls,async close(){await new Promise(resolve=>{server.closeAllConnections();server.close(resolve);});db.close();}};
}

test(mode==='fixed'?'password recovery revokes old issuer cookies and OAuth families while preserving other accounts':`pre-patch ${mode} characterization of retained recovery credentials`,async()=>{
 const f=await fixture();
 try{
  const owner=await f.login(f.accounts.owner),other=await f.login(f.accounts.other),ownerSecond=await f.login(f.accounts.owner),detachedBrowser=await f.login(f.accounts.owner);
  const registration=await owner.request('/api/auth/oauth2/create-client',{method:'POST',body:{client_name:'Reset boundary fixture',redirect_uris:['https://client.example.test/callback'],token_endpoint_auth_method:'none',application_type:'web',grant_types:['authorization_code','refresh_token'],response_types:['code'],scope:scopes,require_pkce:true}});
  assert.equal(registration.status,201);const clientId=(await registration.json()).client_id;
  const ownerTokens=await f.tokens(owner,clientId),otherTokens=await f.tokens(other,clientId),opaque=await f.tokens(owner,clientId,false);
  let detached=await f.tokens(detachedBrowser,clientId);
  assert.equal((await f.current(ownerTokens.access_token)).status,200);assert.equal((await f.current(otherTokens.access_token)).status,200);
  assert.equal((await detachedBrowser.request('/api/auth/sign-out',{method:'POST',body:{}})).status,200);
  assert.equal(await f.validSession(detachedBrowser),false);
  const ordinaryLogoutRefresh=await f.refresh(clientId,detached.refresh_token);assert.equal(ordinaryLogoutRefresh.status,200,'normal logout retains provider offline-grant behavior');detached=await ordinaryLogoutRefresh.json();
  assert.equal((await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+detached.access_token}})).status,200,'ordinary offline userinfo remains available before reset');
  assert.ok(f.db.prepare('SELECT count(*) n FROM oauthRefreshToken WHERE userId=? AND sessionId IS NULL').get(f.accounts.owner.id).n>0,'actual detached offline refresh row exists');
  assert.ok(f.db.prepare('SELECT count(*) n FROM oauthAccessToken WHERE userId=?').get(f.accounts.owner.id).n>0,'actual opaque access row exists');
  const token=await f.resetToken(f.accounts.owner.email),newPassword=randomBytes(32).toString('base64url');
  const reset=await f.request('/api/auth/reset-password',{method:'POST',body:{token,newPassword}});assert.equal(reset.status,200,'valid reset succeeds');
  const cookieStillValid=await f.validSession(owner),accessStatus=(await f.current(ownerTokens.access_token)).status;
  const retainedRefresh=await f.refresh(clientId,ownerTokens.refresh_token);
  if(mode!=='fixed'){
   assert.equal(cookieStillValid,mode==='baseline');assert.equal(accessStatus,mode==='baseline'?200:401);assert.equal(retainedRefresh.status,200,'pre-patch reset leaves offline refresh usable');
   assert.ok(f.db.prepare('SELECT count(*) n FROM oauthRefreshToken WHERE userId=?').get(f.accounts.owner.id).n>0);
   console.log(JSON.stringify({probe:mode,oldCookieValid:cookieStillValid,oldAccessStatus:accessStatus,oldRefreshStatus:retainedRefresh.status,otherAccountPreserved:await f.validSession(other)}));
  }else{
   assert.equal(cookieStillValid,false,'old primary cookie is revoked');assert.equal(await f.validSession(ownerSecond),false,'all old issuer cookies are revoked');assert.equal(accessStatus,401,'old signed access is denied');
   assert.ok([400,401].includes(retainedRefresh.status),'old resource refresh grant is denied');assert.ok([400,401].includes((await f.refresh(clientId,detached.refresh_token)).status),'detached offline refresh is denied');
   assert.equal((await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+opaque.access_token}})).status,401,'old stored opaque access is denied');
   const detachedUserInfo=await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+detached.access_token}});
   console.log(JSON.stringify({probe:'detached-jwt-after-reset',userinfoStatus:detachedUserInfo.status,resourceStatus:(await f.current(detached.access_token)).status}));
   assert.equal(detachedUserInfo.status,401,'old detached signed access is denied by userinfo');
   for(const table of ['session','oauthAccessToken','oauthRefreshToken'])assert.equal(f.db.prepare(`SELECT count(*) n FROM ${table} WHERE userId=?`).get(f.accounts.owner.id).n,0,'reset removes only owner '+table);
   assert.ok(f.batchCalls.some(statements=>statements.length===4&&statements.every(statement=>statement.values.length===1&&statement.values[0]===f.accounts.owner.id)),'cleanup and generation batch bind the verified canonical account');
  }
  assert.equal(await f.validSession(other),true,'other account cookie remains valid');assert.equal((await f.current(otherTokens.access_token)).status,200,'other account access remains valid');assert.equal((await f.refresh(clientId,otherTokens.refresh_token)).status,200,'other account refresh remains valid');
  const fresh=await f.login(f.accounts.owner,newPassword);assert.equal(await f.validSession(fresh),true);
  const replay=await f.request('/api/auth/reset-password',{method:'POST',body:{token,newPassword:randomBytes(32).toString('base64url')}});assert.equal(replay.status,400);assert.equal(await f.validSession(fresh),true,'replaying reset does not revoke the new session');
  const invalid=await f.request('/api/auth/reset-password',{method:'POST',body:{token:'invalid-reset-token',newPassword:randomBytes(32).toString('base64url')}});assert.equal(invalid.status,400);assert.equal(await f.validSession(fresh),true,'invalid reset does not revoke a legitimate session');
 }finally{await f.close();}
});

if(mode==='fixed')test('an offline refresh paused after rotation CAS cannot survive a completed password reset',async()=>{
 const f=await fixture();let release,arrived,timer,pending;const proceed=new Promise(resolve=>{release=resolve;}),reached=new Promise(resolve=>{arrived=resolve;});
 const adapter=f.context.adapter,original=adapter.incrementOne;
 try{
  const owner=await f.login(f.accounts.owner);
  const registration=await owner.request('/api/auth/oauth2/create-client',{method:'POST',body:{client_name:'Reset race fixture',redirect_uris:['https://client.example.test/callback'],token_endpoint_auth_method:'none',application_type:'web',grant_types:['authorization_code','refresh_token'],response_types:['code'],scope:scopes,require_pkce:true}});
  assert.equal(registration.status,201);const clientId=(await registration.json()).client_id;
  const issued=await f.tokens(owner,clientId);assert.equal((await owner.request('/api/auth/sign-out',{method:'POST',body:{}})).status,200);
  const resetToken=await f.resetToken(f.accounts.owner.email);
  adapter.incrementOne=async function(input){const result=await original.call(this,input);if(input.model==='oauthRefreshToken'&&result){arrived();await proceed;}return result;};
  pending=f.refresh(clientId,issued.refresh_token);
  await Promise.race([reached,new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('refresh did not reach the instrumented actual CAS')),5000);})]);clearTimeout(timer);
  const newPassword=randomBytes(32).toString('base64url');
  const reset=await f.request('/api/auth/reset-password',{method:'POST',body:{token:resetToken,newPassword}});assert.equal(reset.status,200);
  const legitimateBrowser=await f.login(f.accounts.owner,newPassword),legitimateTokens=await f.tokens(legitimateBrowser,clientId);
  release();const response=await pending;const tokens=response.status===200?await response.json():null;
  const userinfo=tokens?await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+tokens.access_token}}):null;
  const resource=tokens?await f.current(tokens.access_token):null;
  console.log(JSON.stringify({probe:'refresh-after-cas-reset',refreshStatus:response.status,userinfoStatus:userinfo?.status??null,resourceStatus:resource?.status??null,remainingRefreshRows:f.db.prepare('SELECT count(*) n FROM oauthRefreshToken WHERE userId=?').get(f.accounts.owner.id).n}));
  assert.notEqual(response.status,200,'reset cannot leave an in-flight pre-reset offline refresh grant usable');
  assert.equal(await f.validSession(legitimateBrowser),true,'cleanup of rejected old grant preserves a new-password session');
  assert.equal((await f.current(legitimateTokens.access_token)).status,200);
  assert.equal((await f.refresh(clientId,legitimateTokens.refresh_token)).status,200,'only the rejected raced grant is removed');
 }finally{clearTimeout(timer);release();adapter.incrementOne=original;await pending?.catch(()=>{});await f.close();}
});

if(mode==='fixed')test('an old-password email or username login cannot create a surviving session after a completed reset',async()=>{
 for(const method of ['email','username']){
  const f=await fixture();let release,arrived,timer,pending,paused=false;const proceed=new Promise(resolve=>{release=resolve;}),reached=new Promise(resolve=>{arrived=resolve;});
  const adapter=f.context.internalAdapter,original=adapter.createSession;
  try{
   await f.login(f.accounts.owner);const resetToken=await f.resetToken(f.accounts.owner.email);
   adapter.createSession=async function(userId,...args){if(userId===f.accounts.owner.id&&!paused){paused=true;arrived();await proceed;}return original.call(this,userId,...args);};
   pending=f.request('/api/auth/sign-in/'+method,{method:'POST',body:{[method]:f.accounts.owner[method],password:f.accounts.owner.password}});
   await Promise.race([reached,new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('password login did not reach actual session creation')),5000);})]);clearTimeout(timer);
   const newPassword=randomBytes(32).toString('base64url');assert.equal((await f.request('/api/auth/reset-password',{method:'POST',body:{token:resetToken,newPassword}})).status,200);
   const legitimate=await f.login(f.accounts.owner,newPassword);
   release();const stale=await pending;assert.equal(stale.status,401,'stale '+method+' password proof is refused');
   assert.equal(await f.validSession(legitimate),true);assert.equal(f.db.prepare('SELECT count(*) n FROM session WHERE userId=?').get(f.accounts.owner.id).n,1);
   assert.equal(f.db.prepare('SELECT credentialGeneration FROM user WHERE id=?').get(f.accounts.owner.id).credentialGeneration,1);
  }finally{clearTimeout(timer);release();adapter.createSession=original;await pending?.catch(()=>{});await f.close();}
 }
});

if(mode==='fixed')test('legacy generation-zero JWTs keep working before reset, then reset also invalidates held authorization codes',async()=>{
 const f=await fixture();
 try{
  const owner=await f.login(f.accounts.owner),other=await f.login(f.accounts.other);
  const registration=await owner.request('/api/auth/oauth2/create-client',{method:'POST',body:{client_name:'Legacy recovery fixture',redirect_uris:['https://client.example.test/callback'],token_endpoint_auth_method:'none',application_type:'web',grant_types:['authorization_code','refresh_token'],response_types:['code'],scope:scopes,require_pkce:true}});
  assert.equal(registration.status,201);const clientId=(await registration.json()).client_id;
  const claims=f.provider.options.customAccessTokenClaims;
  // Issue genuine provider JWTs using the pre-fence claim shape, without
  // changing signing, token storage, grants or authentication.
  f.provider.options.customAccessTokenClaims=undefined;
  const ownerLegacy=await f.tokens(owner,clientId),otherLegacy=await f.tokens(other,clientId);
  f.provider.options.customAccessTokenClaims=claims;
  assert.equal(decodeJwt(ownerLegacy.access_token)['https://9to1.uk/credential_generation'],undefined);
  assert.equal((await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+ownerLegacy.access_token}})).status,200);
  const held=await f.tokens(owner,clientId,true,true);
  const resetToken=await f.resetToken(f.accounts.owner.email);assert.equal((await f.request('/api/auth/reset-password',{method:'POST',body:{token:resetToken,newPassword:randomBytes(32).toString('base64url')}})).status,200);
  assert.equal((await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+ownerLegacy.access_token}})).status,401);
  assert.equal((await f.request('/api/auth/oauth2/userinfo',{headers:{authorization:'Bearer '+otherLegacy.access_token}})).status,200,'unreset legacy user remains compatible');
  assert.equal((await f.refresh(clientId,otherLegacy.refresh_token)).status,200);
  const exchange=await f.request('/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{grant_type:'authorization_code',client_id:clientId,code:held.code,code_verifier:held.verifier,redirect_uri:'https://client.example.test/callback',resource:f.env.AUTH_BASE_URL}});
  assert.ok([400,401].includes(exchange.status),'authorization created by a revoked pre-reset session is rejected');
 }finally{await f.close();}
});

if(mode==='fixed')test('confidential-client introspection marks old detached JWTs inactive without breaking current, other-user or machine tokens',async()=>{
 const f=await fixture();
 try{
  const owner=await f.login(f.accounts.owner),other=await f.login(f.accounts.other);
  const registered=await owner.request('/api/auth/oauth2/create-client',{method:'POST',body:{client_name:'Introspection recovery fixture',redirect_uris:['https://client.example.test/callback'],token_endpoint_auth_method:'client_secret_post',application_type:'web',grant_types:['authorization_code','refresh_token'],response_types:['code'],scope:scopes,require_pkce:true}});
  assert.equal(registered.status,201);const client=await registered.json();assert.equal(typeof client.client_secret,'string');
  const original=await f.tokens(owner,client.client_id,true,false,client.client_secret),otherTokens=await f.tokens(other,client.client_id,true,false,client.client_secret);
  const opaque=await f.tokens(owner,client.client_id,false,false,client.client_secret);
  const introspect=async(token,hint='access_token',target=client)=>{const r=await f.request('/api/auth/oauth2/introspect',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{token,token_type_hint:hint,client_id:target.client_id,client_secret:target.client_secret}});assert.equal(r.status,200);return r.json();};
  assert.equal((await introspect(opaque.access_token)).active,true,'opaque access introspection works before logout/reset');
  assert.equal((await owner.request('/api/auth/sign-out',{method:'POST',body:{}})).status,200);
  const refreshed=await f.refresh(client.client_id,original.refresh_token,client.client_secret);assert.equal(refreshed.status,200);const detached=await refreshed.json();
  assert.equal((await introspect(detached.access_token)).active,true);
  const resetToken=await f.resetToken(f.accounts.owner.email),password=randomBytes(32).toString('base64url');assert.equal((await f.request('/api/auth/reset-password',{method:'POST',body:{token:resetToken,newPassword:password}})).status,200);
  assert.equal((await introspect(detached.access_token)).active,false,'the authenticated introspection surface enforces reset generation');
  assert.equal((await introspect(opaque.access_token)).active,false,'old opaque access stays inactive');
  assert.equal((await introspect(otherTokens.access_token)).active,true);
  const freshOwner=await f.login(f.accounts.owner,password),fresh=await f.tokens(freshOwner,client.client_id,true,false,client.client_secret);
  assert.equal((await introspect(fresh.access_token)).active,true);assert.equal((await introspect(fresh.refresh_token,'refresh_token')).active,true,'fresh opaque refresh introspection remains usable');
  const freshOpaque=await f.tokens(freshOwner,client.client_id,false,false,client.client_secret);
  assert.equal((await introspect(freshOpaque.access_token)).active,true,'fresh opaque access introspection remains usable');
  assert.equal((await introspect(freshOpaque.refresh_token,'refresh_token')).active,true);
  const machine=await f.auth.api.adminCreateOAuthClient({headers:new Headers({cookie:freshOwner.cookie,origin:f.env.AUTH_BASE_URL}),body:{client_name:'Machine control',token_endpoint_auth_method:'client_secret_post',grant_types:['client_credentials'],client_credentials_scopes:['cake:account:read'],scope:'cake:account:read',redirect_uris:['https://machine.example.test/callback']}});
  const machineGrant=await f.request('/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{grant_type:'client_credentials',client_id:machine.client_id,client_secret:machine.client_secret,scope:'cake:account:read',resource:f.env.AUTH_BASE_URL}});assert.equal(machineGrant.status,200);
  assert.equal((await introspect((await machineGrant.json()).access_token,'access_token',machine)).active,true,'machine authority is independent of an account reset');
 }finally{await f.close();}
});

if(mode==='fixed')test('optional pairwise introspection uses the verified canonical JWT subject for reset fencing',async()=>{
 const f=await fixture();
 try{
  // Pairwise is not enabled in production. Exercise the maintained optional
  // mode in this isolated fixture without changing the live configuration.
  assert.equal(f.provider.options.pairwiseSecret,undefined);f.provider.options.pairwiseSecret=randomBytes(32).toString('base64url');
  const owner=await f.login(f.accounts.owner),other=await f.login(f.accounts.other);
  const client=await f.auth.api.adminCreateOAuthClient({headers:new Headers({cookie:owner.cookie,origin:f.env.AUTH_BASE_URL}),body:{client_name:'Pairwise control',subject_type:'pairwise',token_endpoint_auth_method:'client_secret_post',application_type:'web',grant_types:['authorization_code','refresh_token'],scope:scopes,redirect_uris:['https://client.example.test/callback'],require_pkce:true}});
  const original=await f.tokens(owner,client.client_id,true,false,client.client_secret),otherToken=await f.tokens(other,client.client_id,true,false,client.client_secret);
  await owner.request('/api/auth/sign-out',{method:'POST',body:{}});const detachedResponse=await f.refresh(client.client_id,original.refresh_token,client.client_secret);assert.equal(detachedResponse.status,200);const detached=await detachedResponse.json();
  const introspect=async(token)=>{const r=await f.request('/api/auth/oauth2/introspect',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{token,client_id:client.client_id,client_secret:client.client_secret}});assert.equal(r.status,200);return r.json();};
  const before=await introspect(detached.access_token);assert.equal(before.active,true);assert.notEqual(before.sub,f.accounts.owner.id);assert.equal(decodeJwt(detached.access_token).sub,f.accounts.owner.id);
  const resetToken=await f.resetToken(f.accounts.owner.email);assert.equal((await f.request('/api/auth/reset-password',{method:'POST',body:{token:resetToken,newPassword:randomBytes(32).toString('base64url')}})).status,200);
  assert.equal((await introspect(detached.access_token)).active,false);assert.equal((await introspect(otherToken.access_token)).active,true);
 }finally{await f.close();}
});
