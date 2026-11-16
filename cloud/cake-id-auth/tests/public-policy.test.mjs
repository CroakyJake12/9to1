import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';
const output=await build({stdin:{contents:`export * from './src/public-policy.ts';export {createAuthOptions} from './src/auth.ts';export {createInvitation} from './src/invitations.ts';export {runWithEndpointContext} from '@better-auth/core/context';`,resolveDir:process.cwd()},bundle:true,write:false,platform:'node',format:'esm',banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"}});
const {policyVersions,checkPolicyAcknowledgement,classifyAccountClient,accountPolicyCurrent,acceptAccountPolicy,createAuthOptions,createInvitation,runWithEndpointContext}=await import('data:text/javascript;base64,'+Buffer.from(output.outputFiles[0].text).toString('base64'));
const origin='https://issuer.example.test',account='11111111-1111-4111-8111-111111111111',sid='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
function fixture(){
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 for(const name of ['0000_cake_guardrails.sql','0001_better_auth.sql','0004_account_invitations.sql','0005_public_account_policy.sql'])db.exec(readFileSync('migrations/'+name,'utf8'));
 db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,role) VALUES(?,?,?,?,?,?,?)').run(account,'Existing','existing@example.test',1,'2026-01-01','2026-01-01','admin');
 db.prepare('INSERT INTO session(id,userId,token,expiresAt,createdAt,updatedAt) VALUES(?,?,?,datetime(\'now\',\'+1 hour\'),?,?)').run(sid,account,'synthetic','2026-01-01','2026-01-01');
 for(const [id,uri] of [['cake','https://cakemods.com/oauth/callback'],['nine','https://9to1.uk/oauth/callback'],['legacy','https://present-4581358882177bc7.9to1.uk/oauth/callback'],['native','http://127.0.0.1:43821/cake-id/callback/']])
  db.prepare('INSERT INTO oauthClient(id,clientId,redirectUris,disabled) VALUES(?,?,?,0)').run(id,id,JSON.stringify([uri]));
 let beforeWrite;
 const DB={prepare(sql){return {bind(...values){return {sql,values,first:async()=>{if(sql.startsWith('INSERT INTO cake_public_account_policy'))beforeWrite?.();return db.prepare(sql).get(...values)??null;},run:async()=>({success:true,meta:db.prepare(sql).run(...values)})};}};},async batch(statements){db.exec('BEGIN');try{const r=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return r;}catch(e){db.exec('ROLLBACK');throw e;}}};
 return {db,env:{DB,APP_MODE:'production',AUTH_BASE_URL:origin,API_RESOURCE:origin,AUTH_SECRET:'synthetic-config-secret-'.repeat(3),PUBLIC_TERMS_VERSION:'fixture-terms-v1',PUBLIC_PRIVACY_VERSION:'fixture-privacy-v1'},beforeWrite:fn=>{beforeWrite=fn;}};
}
const body=(extra={})=>({ageBand:'13_15',acceptTerms:true,termsVersion:'fixture-terms-v1',privacyVersion:'fixture-privacy-v1',...extra});
const request=(value=body(),requestOrigin=origin)=>new Request(origin+'/account/onboarding',{method:'POST',headers:{origin:requestOrigin,'content-type':'application/json'},body:JSON.stringify(value)});
test('policy versions must be explicitly configured and acknowledgements match server authority',()=>{
 assert.equal(policyVersions({}),null);assert.equal(checkPolicyAcknowledgement({},body()),'account_policy_unavailable');
 const {db,env}=fixture();for(const wrong of [{acceptTerms:false},{acceptTerms:'true'},{ageBand:''},{termsVersion:'old'},{privacyVersion:'old'}])assert.equal(checkPolicyAcknowledgement(env,body(wrong)),'account_policy_acknowledgement_required');
 assert.equal(checkPolicyAcknowledgement(env,body({ageBand:'under_13'})),'minimum_age_required');assert.equal(checkPolicyAcknowledgement(env,body()),null);db.close();
});
test('only canonical exact production callback metadata selects the public policy gate',async()=>{
 const {db,env}=fixture();assert.equal(await classifyAccountClient(env,'cake'),'production');assert.equal(await classifyAccountClient(env,'nine'),'production');assert.equal(await classifyAccountClient(env,'legacy'),'legacy');assert.equal(await classifyAccountClient(env,'native'),'legacy');
 assert.equal(await classifyAccountClient(env,'missing'),'invalid');db.exec("UPDATE oauthClient SET disabled=1 WHERE clientId='cake'");assert.equal(await classifyAccountClient(env,'cake'),'invalid');
 db.prepare('UPDATE oauthClient SET redirectUris=? WHERE clientId=?').run('["https://cakemods.com.evil.example/oauth/callback"]','legacy');assert.equal(await classifyAccountClient(env,'legacy'),'legacy');db.close();
});
test('onboarding records owner, age and server time; current acceptance admits the account',async()=>{
 const {db,env}=fixture();assert.equal(await accountPolicyCurrent(env,account),false);
 const r=await acceptAccountPolicy(request(body({accountId:'someone-else',acceptedAt:1,aiAllowed:true})),env,account,sid);assert.equal(r.status,200);assert.equal((await r.json()).aiAccess,'unavailable');
 const row=db.prepare('SELECT * FROM cake_public_account_policy').get();assert.equal(row.accountId,account);assert.equal(row.ageBand,'13_15');assert.ok(row.acceptedAt>Date.now()-10000);assert.equal(row.aiAllowed,undefined);assert.equal(await accountPolicyCurrent(env,account),true);
 env.PUBLIC_TERMS_VERSION='fixture-terms-v2';assert.equal(await accountPolicyCurrent(env,account),false);db.close();
});
test('age band cannot be changed to remove under18 safeguards; policy renewal retains age',async()=>{
 const {db,env}=fixture();assert.equal((await acceptAccountPolicy(request(),env,account,sid)).status,200);
 for(const ageBand of ['18_plus','16_17'])assert.equal((await acceptAccountPolicy(request(body({ageBand})),env,account,sid)).status,409);
 env.PUBLIC_TERMS_VERSION='fixture-terms-v2';assert.equal((await acceptAccountPolicy(request(body({termsVersion:'fixture-terms-v2'})),env,account,sid)).status,200);assert.equal(db.prepare('SELECT ageBand FROM cake_public_account_policy').get().ageBand,'13_15');db.close();
});
test('under13, unverified email, foreign origins and revoked sessions cannot write acceptance',async()=>{
 for(const failure of ['age','email','origin','session']){
  const {db,env,beforeWrite}=fixture();if(failure==='email')db.exec('UPDATE user SET emailVerified=0');if(failure==='session')beforeWrite(()=>db.exec('DELETE FROM session'));
  const r=await acceptAccountPolicy(request(body(failure==='age'?{ageBand:'under_13'}:{}),failure==='origin'?'https://foreign.example':origin),env,account,sid);
  assert.ok([403,409].includes(r.status));assert.equal(db.prepare('SELECT count(*) n FROM cake_public_account_policy').get().n,0);db.close();
 }
});
test('real provider postLogin callbacks gate direct and resumed production flows while retaining legacy clients',async()=>{
 const {db,env}=fixture();const provider=createAuthOptions(env).plugins.find(p=>p.id==='oauth-provider'),info={user:{id:account},session:{id:sid},scopes:['openid']};
 const invoke=(client,callback,consent=false)=>runWithEndpointContext(consent?{body:{oauth_query:new URLSearchParams({client_id:client}).toString()}}:{query:{client_id:client}},()=>provider.options.postLogin[callback](info));
 assert.equal(await invoke('cake','shouldRedirect'),true);await assert.rejects(()=>invoke('cake','consentReferenceId',true));
 assert.equal(await invoke('legacy','shouldRedirect'),false);assert.equal(await invoke('native','consentReferenceId',true),undefined);
 await assert.rejects(()=>runWithEndpointContext({query:{client_id:'legacy'},body:{oauth_query:'client_id=cake'}},()=>provider.options.postLogin.consentReferenceId(info)));
 await assert.rejects(()=>runWithEndpointContext({body:{client_id:'legacy',oauth_query:'client_id=cake'}},()=>provider.options.postLogin.consentReferenceId(info)));
 await assert.rejects(()=>runWithEndpointContext({body:{oauth_query:'client_id=legacy&client_id=cake'}},()=>provider.options.postLogin.consentReferenceId(info)));
 await acceptAccountPolicy(request(),env,account,sid);assert.equal(await invoke('cake','shouldRedirect'),false);assert.equal(await invoke('cake','consentReferenceId',true),undefined);
 delete env.PUBLIC_TERMS_VERSION;assert.equal(await invoke('cake','shouldRedirect'),true);await assert.rejects(()=>invoke('cake','consentReferenceId',true));assert.equal(await invoke('legacy','shouldRedirect'),false);db.close();
});
test('signup legal/age failure never consumes an invitation; accepted policy is recorded against canonical created user',async()=>{
 const {db,env}=fixture(),invite=await (await createInvitation(env,account,'new@example.test')).json();const options=createAuthOptions(env),gate=options.user.validateUserInfo;
 const user={id:'22222222-2222-4222-8222-222222222222',email:'new@example.test',emailVerified:false};const source={action:'create-user',method:'email-password'};
 for(const wrong of [{ageBand:'under_13'},{acceptTerms:false},{termsVersion:'old'}]){
  const result=await gate({user,source},{path:'/sign-up/email',body:{...body(wrong),invitationCode:invite.code}});assert.ok(result.error);assert.equal(db.prepare('SELECT consumedAt FROM cake_account_invitations').get().consumedAt,null);
 }
 const current=env.PUBLIC_TERMS_VERSION;delete env.PUBLIC_TERMS_VERSION;assert.equal((await gate({user,source},{path:'/sign-up/email',body:{...body(),invitationCode:invite.code}})).error,'account_policy_unavailable');assert.equal(db.prepare('SELECT consumedAt FROM cake_account_invitations').get().consumedAt,null);env.PUBLIC_TERMS_VERSION=current;
 const context={path:'/sign-up/email',body:{...body(),invitationCode:invite.code,accountId:'forged'}};assert.equal(await gate({user,source},context),undefined);
 db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt) VALUES(?,?,?,0,?,?)').run(user.id,'New',user.email,'2026-01-01','2026-01-01');
 await options.databaseHooks.user.create.after(user,context);assert.equal(db.prepare('SELECT accountId FROM cake_public_account_policy').get().accountId,user.id);assert.equal(await accountPolicyCurrent(env,user.id),false);db.close();
});
