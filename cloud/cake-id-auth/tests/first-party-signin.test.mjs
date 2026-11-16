import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {randomBytes,createHash} from 'node:crypto';
import {build} from 'esbuild';
const built=await build({stdin:{contents:`export {createAuthOptions} from './src/auth.ts';export {enableOwnedSignIn,readProductionSitesState,PRODUCTION_SITES} from './src/production-sites.ts';export {readPresentAppState} from './src/staging-apps.ts';export {acceptAccountPolicy} from './src/public-policy.ts';export {createInvitation} from './src/invitations.ts';export {AUTHORIZATION_SCOPES} from './src/contract.ts';export {betterAuth} from 'better-auth';`,resolveDir:process.cwd()},bundle:true,write:false,platform:'node',format:'esm',banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"}});
const {createAuthOptions,enableOwnedSignIn,readProductionSitesState,PRODUCTION_SITES,readPresentAppState,acceptAccountPolicy,createInvitation,AUTHORIZATION_SCOPES,betterAuth}=await import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'));
const origin='https://issuer.example.test',scopes=AUTHORIZATION_SCOPES.join(' ');
let fixtureId=0;
async function fixture(){
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 for(const name of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql','0003_turnstile_redemptions.sql','0004_account_invitations.sql','0005_public_account_policy.sql','0006_password_reset_fence.sql'])db.exec(readFileSync('migrations/'+name,'utf8'));
 const ip=`198.51.100.${140+(++fixtureId)}`,metadataReads=[];
 const DB={prepare(sql){return {bind(...values){return {sql,values,first:async()=>{metadataReads.push(sql);return db.prepare(sql).get(...values)??null;},all:async()=>{metadataReads.push(sql);return {success:true,results:db.prepare(sql).all(...values)};},run:async()=>({success:true,meta:db.prepare(sql).run(...values)})};}};},async batch(statements){db.exec('BEGIN');try{const result=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return result;}catch(e){db.exec('ROLLBACK');throw e;}}};
 const env={DB,AUTH_BASE_URL:origin,API_RESOURCE:origin,APP_MODE:'production',ACCOUNT_REGISTRATION_HOLD:'true',AUTH_SECRET:randomBytes(48).toString('base64url'),PUBLIC_TERMS_VERSION:'fixture-terms-v1',PUBLIC_PRIVACY_VERSION:'fixture-privacy-v1'};
 const options=createAuthOptions(env),provider=options.plugins.find(p=>p.id==='oauth-provider');
 const ids=[PRODUCTION_SITES.cake.expectedClientId,PRODUCTION_SITES.nine.expectedClientId,'unrelated-client','present-client'];provider.options.generateClientId=()=>ids.shift()??crypto.randomUUID();
 const auth=betterAuth({...options,database:db}),context=await auth.$context,actors={};
 for(const[label,role]of [['owner','admin'],['otheradmin','admin'],['ordinary','user']]){
  const id=crypto.randomUUID(),email=label+'@example.test',password=randomBytes(24).toString('base64url');
  db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,role) VALUES(?,?,?,1,?,?,?)').run(id,label,email,'2026-01-01','2026-01-01',role);
  db.prepare('INSERT INTO account(id,accountId,providerId,userId,password,createdAt,updatedAt) VALUES(?,?,?,?,?,?,?)').run(crypto.randomUUID(),id,'credential',id,await context.password.hash(password),'2026-01-01','2026-01-01');actors[label]={id,email,password};
 }
 async function request(path,{method='GET',body,headers={},cookie=''}={}){
  return auth.handler(new Request(origin+path,{method,headers:{'cf-connecting-ip':ip,...(method==='POST'?{origin,'content-type':'application/json'}:{}),...(cookie?{cookie}:{}),...headers},...(body===undefined?{}:{body:headers['content-type']==='application/x-www-form-urlencoded'?new URLSearchParams(body).toString():JSON.stringify(body)})}));
 }
 function browser(){const cookies=new Map();return {get cookie(){return [...cookies].map(([key,value])=>key+'='+value).join('; ');},async request(path,args={}){const response=await request(path,{...args,cookie:this.cookie});for(const h of response.headers.getSetCookie()){const pair=h.split(';',1)[0],i=pair.indexOf('=');cookies.set(pair.slice(0,i),pair.slice(i+1));}return response;}};}
 async function login(actor){const page=browser();assert.equal((await page.request('/api/auth/sign-in/email',{method:'POST',body:{email:actor.email,password:actor.password}})).status,200);return page;}
 const owner=await login(actors.owner),ordinary=await login(actors.ordinary),otheradmin=await login(actors.otheradmin);
 const create=async(name,callback)=>{const r=await owner.request('/api/auth/oauth2/create-client',{method:'POST',body:{client_name:name,redirect_uris:[callback],token_endpoint_auth_method:'none',application_type:'web',grant_types:['authorization_code','refresh_token'],response_types:['code'],scope:scopes}});assert.equal(r.status,201);return r.json();};
 const clients={};for(const site of ['cake','nine']){const def=PRODUCTION_SITES[site];clients[site]=await create(def.name,def.redirectUri);assert.equal(clients[site].client_id,def.expectedClientId);}
 clients.unrelated=await create('Unrelated app','https://unrelated.example.test/callback');
 clients.present=await create('9to1 Present preview','https://present-4581358882177bc7.9to1.uk/oauth/callback');
 const adminRequest=(site,page=owner,extra={},requestOrigin=origin)=>new Request(origin+'/account/production-sites',{method:'POST',headers:{origin:requestOrigin,'content-type':'application/json',cookie:page.cookie},body:JSON.stringify({site,...extra})});
 const enable=(site,page=owner,actor=actors.owner,api=auth.api,extra={})=>enableOwnedSignIn(adminRequest(site,page,extra),env,actor.id,api);
 async function policy(page=owner,actor=actors.owner){const session=await (await page.request('/api/auth/get-session')).json();return acceptAccountPolicy(new Request(origin+'/account/onboarding',{method:'POST',headers:{origin,'content-type':'application/json'},body:JSON.stringify({ageBand:'18_plus',acceptTerms:true,termsVersion:env.PUBLIC_TERMS_VERSION,privacyVersion:env.PUBLIC_PRIVACY_VERSION})}),env,actor.id,session.session.id);}
 async function authorize(clientId,callback,changes={},page=owner){
  const verifier=randomBytes(48).toString('base64url'),state=randomBytes(20).toString('base64url');
  const params={client_id:clientId,redirect_uri:callback,response_type:'code',scope:scopes,state,nonce:randomBytes(20).toString('base64url'),code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256',resource:origin,...changes};
  for(const key of Object.keys(params))if(params[key]===null)delete params[key];
  const response=await page.request('/api/auth/oauth2/authorize?'+new URLSearchParams(params),{headers:{accept:'text/html','sec-fetch-mode':'navigate'}});
  let location=response.headers.get('location');if(!location&&response.status===200)location=(await response.json()).url;
  return {response,destination:location?new URL(location,origin):null,verifier,state};
 }
 async function exchange(flow,clientId,callback,verifier=flow.verifier){return request('/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:{grant_type:'authorization_code',client_id:clientId,redirect_uri:callback,code:flow.destination.searchParams.get('code'),code_verifier:verifier,resource:origin}});}
 return {db,env,auth,actors,owner,ordinary,otheradmin,clients,metadataReads,adminRequest,enable,policy,authorize,exchange,request,browser};
}

test('real provider issues direct first-party codes only after policy, while unrelated apps still require consent',async()=>{
 const f=await fixture();try{
  assert.equal((await f.policy()).status,200);
  const before=await f.authorize(f.clients.cake.client_id,PRODUCTION_SITES.cake.redirectUri);assert.equal(before.destination.pathname,'/consent');
  for(const site of ['cake','nine']){
   const original=f.db.prepare('SELECT * FROM oauthClient WHERE clientId=?').get(f.clients[site].client_id);f.metadataReads.length=0;
   const changed=await f.enable(site);assert.equal(changed.status,200);assert.equal((await changed.json()).directSignIn,true);
   assert.ok(f.metadataReads.every(sql=>sql.startsWith('SELECT ')),'the helper uses only reads; the maintained admin API owns writes');
   const after=f.db.prepare('SELECT * FROM oauthClient WHERE clientId=?').get(f.clients[site].client_id);
   assert.equal(after.skipConsent,1);for(const key of Object.keys(original))if(!['skipConsent','updatedAt'].includes(key))assert.deepEqual(after[key],original[key],key);
   const flow=await f.authorize(f.clients[site].client_id,PRODUCTION_SITES[site].redirectUri);assert.equal(flow.destination.origin,new URL(PRODUCTION_SITES[site].redirectUri).origin);assert.equal(flow.destination.pathname,'/oauth/callback');assert.ok(flow.destination.searchParams.get('code'));assert.equal(flow.destination.searchParams.get('state'),flow.state);
   assert.equal((await f.exchange(flow,f.clients[site].client_id,PRODUCTION_SITES[site].redirectUri)).status,200);
  }
  const freshBrowser=f.browser(),freshFlow=await f.authorize(f.clients.nine.client_id,PRODUCTION_SITES.nine.redirectUri,{},freshBrowser);
  assert.equal(freshFlow.destination.pathname,'/sign-in');
  const resumed=await freshBrowser.request('/api/auth/sign-in/email',{method:'POST',body:{email:f.actors.owner.email,password:f.actors.owner.password,oauth_query:freshFlow.destination.search.slice(1)}});assert.equal(resumed.status,200);
  freshFlow.destination=new URL((await resumed.json()).url);assert.equal(freshFlow.destination.origin,'https://9to1.uk');assert.equal(freshFlow.destination.pathname,'/oauth/callback');assert.ok(freshFlow.destination.searchParams.get('code'),'first-party login resumes directly without a consent page');
  assert.equal((await f.exchange(freshFlow,f.clients.nine.client_id,PRODUCTION_SITES.nine.redirectUri)).status,200);
  const unrelated=await f.authorize(f.clients.unrelated.client_id,'https://unrelated.example.test/callback');assert.equal(unrelated.destination.pathname,'/consent');
  assert.equal(f.db.prepare('SELECT skipConsent FROM oauthClient WHERE clientId=?').get(f.clients.unrelated.client_id).skipConsent??0,0);
  delete f.env.PUBLIC_TERMS_VERSION;const held=await f.authorize(f.clients.nine.client_id,PRODUCTION_SITES.nine.redirectUri);assert.equal(held.destination.pathname,'/account/onboarding','owned direct sign-in never bypasses policy');assert.equal(held.destination.searchParams.get('code'),null);
 }finally{f.db.close();}
});

test('direct sign-in preserves exact callback and S256 PKCE enforcement',async()=>{
 const f=await fixture();try{
  await f.policy();assert.equal((await f.enable('cake')).status,200);const id=f.clients.cake.client_id,callback=PRODUCTION_SITES.cake.redirectUri;
  const foreign=await f.authorize(id,'https://foreign.example.test/callback');assert.ok(!foreign.destination?.searchParams.get('code'));assert.notEqual(foreign.destination?.hostname,'foreign.example.test');
  const missing=await f.authorize(id,callback,{code_challenge:null,code_challenge_method:null});assert.ok(!missing.destination?.searchParams.get('code'));
  const flow=await f.authorize(id,callback);assert.ok(flow.destination.searchParams.get('code'));const denied=await f.exchange(flow,id,callback,randomBytes(48).toString('base64url'));assert.ok([400,401].includes(denied.status));
  const fresh=await f.authorize(id,callback);assert.equal((await f.exchange(fresh,id,callback)).status,200);
 }finally{f.db.close();}
});

test('administrator endpoint accepts only the site enum and never upgrades foreign, mismatched or legacy clients',async()=>{
 const f=await fixture();try{
  assert.equal((await f.enable('cake',f.ordinary,f.actors.ordinary)).status,403);
  assert.equal((await f.enable('cake',f.otheradmin,f.actors.otheradmin)).status,409);
  assert.equal((await enableOwnedSignIn(f.adminRequest('cake',f.owner,{},'https://foreign.example.test'),f.env,f.actors.owner.id,f.auth.api)).status,403);
  for(const [site,extra]of [['unrelated',{}],['cake',{client_id:f.clients.unrelated.client_id}],['cake',{skip_consent:true}],['cake',{scope:'admin'}],['cake',{client_name:'Arbitrary name'}]])assert.equal((await f.enable(site,f.owner,f.actors.owner,f.auth.api,extra)).status,400);
  assert.equal((await f.enable('cake',f.owner,f.actors.owner,{})).status,503,'missing maintained capability is not substituted');
  await f.auth.api.adminUpdateOAuthClient({headers:new Headers({origin,cookie:f.owner.cookie}),body:{client_id:f.clients.nine.client_id,update:{redirect_uris:['https://foreign.example.test/callback']}}});
  assert.equal((await f.enable('nine')).status,409);assert.equal(f.db.prepare('SELECT skipConsent FROM oauthClient WHERE clientId=?').get(f.clients.nine.client_id).skipConsent??0,0);
  await f.auth.api.adminUpdateOAuthClient({headers:new Headers({origin,cookie:f.owner.cookie}),body:{client_id:f.clients.present.client_id,update:{skip_consent:true}}});
  assert.equal((await readPresentAppState(f.env,f.actors.owner.id)).kind,'conflict','legacy observation stays consent-strict');
 }finally{f.db.close();}
});

test('a lost update reply is reconciled without replay, and a revoked administrator cannot enable direct sign-in',async()=>{
 const f=await fixture();try{
  let calls=0;const uncertain={adminUpdateOAuthClient:async args=>{calls++;assert.deepEqual(args.body,{client_id:PRODUCTION_SITES.cake.expectedClientId,update:{skip_consent:true}});await f.auth.api.adminUpdateOAuthClient(args);throw Error('reply lost');}};
  assert.equal((await f.enable('cake',f.owner,f.actors.owner,uncertain)).status,200);assert.equal((await f.enable('cake',f.owner,f.actors.owner,uncertain)).status,200);assert.equal(calls,1);
  let deniedCalls=0;const revoked={adminUpdateOAuthClient:async args=>{deniedCalls++;f.db.prepare("UPDATE user SET role='user' WHERE id=?").run(f.actors.owner.id);return f.auth.api.adminUpdateOAuthClient(args);}};
  assert.equal((await f.enable('nine',f.owner,f.actors.owner,revoked)).status,403);assert.equal(deniedCalls,1);assert.equal(f.db.prepare('SELECT skipConsent FROM oauthClient WHERE clientId=?').get(f.clients.nine.client_id).skipConsent??0,0);
 }finally{f.db.close();}
});

test('enabling owned-site sign-in does not lift the separate registration hold',async()=>{
 const f=await fixture();try{
  await f.policy();assert.equal((await f.enable('cake')).status,200);
  const invitation=await (await createInvitation(f.env,f.actors.owner.id,'invited@example.test')).json();
  const r=await f.request('/api/auth/sign-up/email',{method:'POST',body:{name:'Invited',username:'invited_person',email:'invited@example.test',password:randomBytes(24).toString('base64url'),invitationCode:invitation.code,ageBand:'18_plus',acceptTerms:true,termsVersion:f.env.PUBLIC_TERMS_VERSION,privacyVersion:f.env.PUBLIC_PRIVACY_VERSION}});
  assert.equal(r.status,403);assert.equal((await r.json()).code,'account_registration_held');assert.equal(f.db.prepare('SELECT consumedAt FROM cake_account_invitations').get().consumedAt,null);assert.equal(f.db.prepare('SELECT count(*) n FROM user').get().n,3);
 }finally{f.db.close();}
});

test('approved CakeMods display rename changes only the pinned name and preserves enabled shared sign-in',async()=>{
 const f=await fixture();try{
  assert.equal((await f.enable('cake')).status,200);assert.equal((await f.enable('nine')).status,200);
  await f.auth.api.adminUpdateOAuthClient({headers:new Headers({origin,cookie:f.owner.cookie}),body:{client_id:PRODUCTION_SITES.cake.expectedClientId,update:{client_name:'CAKE website'}}});
  const before=f.db.prepare('SELECT * FROM oauthClient WHERE clientId=?').get(PRODUCTION_SITES.cake.expectedClientId),nineBefore=f.db.prepare('SELECT * FROM oauthClient WHERE clientId=?').get(PRODUCTION_SITES.nine.expectedClientId);
  const observed=await readProductionSitesState(f.env,f.actors.owner.id);assert.equal(observed.cake.displayNameCurrent,false);assert.equal(observed.cake.directSignIn,true);
  let calls=0;const updater={adminUpdateOAuthClient:async args=>{calls++;assert.deepEqual(args.body,{client_id:PRODUCTION_SITES.cake.expectedClientId,update:{client_name:'CakeMods website'}});await f.auth.api.adminUpdateOAuthClient(args);throw Error('committed response lost');}};
  const changed=await f.enable('cake',f.owner,f.actors.owner,updater);assert.equal(changed.status,200);assert.equal((await changed.json()).displayNameCurrent,true);
  assert.equal((await f.enable('cake',f.owner,f.actors.owner,updater)).status,200);assert.equal((await f.enable('nine',f.owner,f.actors.owner,updater)).status,200);assert.equal(calls,1);
  const after=f.db.prepare('SELECT * FROM oauthClient WHERE clientId=?').get(PRODUCTION_SITES.cake.expectedClientId);assert.equal(after.name,'CakeMods website');assert.equal(after.skipConsent,1);
  for(const key of Object.keys(before))if(!['name','updatedAt'].includes(key))assert.deepEqual(after[key],before[key],key);
  assert.deepEqual(f.db.prepare('SELECT * FROM oauthClient WHERE clientId=?').get(PRODUCTION_SITES.nine.expectedClientId),nineBefore);
 }finally{f.db.close();}
});

test('brand synchronisation refuses a legacy client whose permission state changed',async()=>{
 const f=await fixture();try{
  await f.auth.api.adminUpdateOAuthClient({headers:new Headers({origin,cookie:f.owner.cookie}),body:{client_id:PRODUCTION_SITES.cake.expectedClientId,update:{client_name:'CAKE website',skip_consent:false}}});
  let calls=0;const updater={adminUpdateOAuthClient:async()=>{calls++;}};
  assert.equal((await f.enable('cake',f.owner,f.actors.owner,updater)).status,409);assert.equal(calls,0);
  const row=f.db.prepare('SELECT name,skipConsent FROM oauthClient WHERE clientId=?').get(PRODUCTION_SITES.cake.expectedClientId);assert.equal(row.name,'CAKE website');assert.equal(row.skipConsent,0);
 }finally{f.db.close();}
});
