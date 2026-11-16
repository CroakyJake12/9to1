import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {randomBytes} from 'node:crypto';
import {resolve} from 'node:path';
import {build} from 'esbuild';

async function load(options){const built=await build({bundle:true,write:false,platform:'node',format:'esm',loader:{'.txt':'text'},banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"},...options});return import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64')+'#'+crypto.randomUUID());}
const {createAuthOptions,registrationHeld,createInvitation,betterAuth}=await load({stdin:{contents:`export {createAuthOptions,registrationHeld} from './src/auth.ts';export {createInvitation} from './src/invitations.ts';export {betterAuth} from 'better-auth';`,resolveDir:process.cwd()}});
const origin='https://issuer.example.test';
const versions={PUBLIC_TERMS_VERSION:'published-owner-test-v1',PUBLIC_PRIVACY_VERSION:'published-owner-test-v1'};
function fixture(){
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 for(const name of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql','0003_turnstile_redemptions.sql','0004_account_invitations.sql','0005_public_account_policy.sql','0006_password_reset_fence.sql'])db.exec(readFileSync('migrations/'+name,'utf8'));
 const ownerId=crypto.randomUUID(),mail=[];
 db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,role) VALUES(?,?,?,1,?,?,?)').run(ownerId,'Existing owner','owner@example.test','2026-01-01','2026-01-01','admin');
 const DB={prepare(sql){return {bind(...values){return {sql,values,first:async()=>db.prepare(sql).get(...values)??null,all:async()=>({success:true,results:db.prepare(sql).all(...values)}),run:async()=>({success:true,meta:db.prepare(sql).run(...values)})};}};},async batch(statements){db.exec('BEGIN');try{const r=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return r;}catch(e){db.exec('ROLLBACK');throw e;}}};
 const env={DB,APP_MODE:'production',ACCOUNT_REGISTRATION_HOLD:'true',AUTH_BASE_URL:origin,API_RESOURCE:origin,AUTH_SECRET:randomBytes(48).toString('base64url'),...versions,EMAIL_MODE:'cloudflare-email-service',EMAIL_FROM:'identity@example.test',EMAIL_DOMAIN:'example.test',EMAIL:{send:async message=>{mail.push(message);}},GOOGLE_CLIENT_ID:'synthetic.apps.googleusercontent.com',GOOGLE_CLIENT_SECRET:'synthetic-provider-setting'};
 return {db,env,ownerId,mail};
}

test('registration hold applies to every nonlocal environment while retaining the explicit loopback test exemption',()=>{
 const base={AUTH_BASE_URL:origin,ACCOUNT_REGISTRATION_HOLD:'true'};
 for(const mode of ['production','development',undefined,'local-test-only'])assert.equal(registrationHeld({...base,APP_MODE:mode}),true);
 assert.equal(registrationHeld({...base,APP_MODE:'local-test-only',AUTH_BASE_URL:'http://127.0.0.1:8798'}),false);
 assert.equal(registrationHeld({...base,APP_MODE:'production',AUTH_BASE_URL:'http://127.0.0.1:8798'}),true);
 assert.equal(registrationHeld({...base,ACCOUNT_REGISTRATION_HOLD:'false'}),false);
 assert.equal(registrationHeld({AUTH_BASE_URL:origin,APP_MODE:'production'}),false);
});

test('real provider refuses a valid invitation without consuming it, while existing owner login remains available',async()=>{
 const {db,env,ownerId,mail}=fixture();
 const options=createAuthOptions(env),auth=betterAuth({...options,database:db}),context=await auth.$context;
 const password=randomBytes(32).toString('base64url');
 db.prepare('INSERT INTO account(id,accountId,providerId,userId,password,createdAt,updatedAt) VALUES(?,?,?,?,?,?,?)').run(crypto.randomUUID(),ownerId,'credential',ownerId,await context.password.hash(password),'2026-01-01','2026-01-01');
 const call=(path,body)=>auth.handler(new Request(origin+'/api/auth'+path,{method:'POST',headers:{origin,'content-type':'application/json','cf-connecting-ip':'198.51.100.231'},body:JSON.stringify(body)}));
 try{
  const invitationResponse=await createInvitation(env,ownerId,'invited@example.test');assert.equal(invitationResponse.status,201);const invitation=await invitationResponse.json();
  const payload={name:'Invited person',username:'invited_person',email:'invited@example.test',password,invitationCode:invitation.code,ageBand:'18_plus',acceptTerms:true,termsVersion:versions.PUBLIC_TERMS_VERSION,privacyVersion:versions.PUBLIC_PRIVACY_VERSION,ACCOUNT_REGISTRATION_HOLD:'false'};
  const denied=await call('/sign-up/email',payload);assert.equal(denied.status,403);assert.equal((await denied.json()).code,'account_registration_held');
  assert.equal(db.prepare('SELECT consumedAt FROM cake_account_invitations').get().consumedAt,null);assert.equal(db.prepare('SELECT count(*) n FROM user').get().n,1);assert.equal(mail.length,0);
  const signedIn=await call('/sign-in/email',{email:'owner@example.test',password});assert.equal(signedIn.status,200);assert.equal((await signedIn.json()).user.id,ownerId);
  // Lifting only the trusted fixture setting demonstrates the prior invitation
  // workflow remains intact. This never changes a deployed setting.
  env.ACCOUNT_REGISTRATION_HOLD='false';const admitted=await call('/sign-up/email',payload);assert.equal(admitted.status,200);assert.equal((await admitted.json()).token,null);
  assert.equal(db.prepare('SELECT count(*) n FROM user').get().n,2);assert.ok(db.prepare('SELECT consumedAt FROM cake_account_invitations').get().consumedAt);assert.equal(mail.length,1);
 }finally{db.close();}
});

test('hold blocks all provisioning adapters but preserves verified existing Google sign-in and linking',async()=>{
 const {db,env,ownerId}=fixture(),options=createAuthOptions(env),auth=betterAuth({...options,database:db});
 try{
  assert.ok(options.socialProviders.google.clientId);assert.equal(options.socialProviders.google.disableSignUp,true);assert.equal(options.account.accountLinking.enabled,true);
  for(const action of ['sign-in','link-account'])assert.equal(await options.user.validateUserInfo({user:{id:ownerId,email:'owner@example.test',emailVerified:true},source:{action,method:'oauth',oauth:{providerId:'google'}}},{}),undefined);
  for(const method of ['email-password','oauth','admin','sso-oidc'])assert.equal((await options.user.validateUserInfo({user:{email:'new@example.test',emailVerified:true},source:{action:'create-user',method,oauth:{providerId:'google'}}},{path:'/sign-up/email',body:{}})).error,'account_registration_held');
  const context=await auth.$context;
  await assert.rejects(()=>context.internalAdapter.createOAuthUser({name:'New social user',email:'new@example.test',emailVerified:true},{providerId:'google',accountId:'synthetic-google-subject'}),error=>error.statusCode===403&&error.body?.code==='account_registration_held');
  assert.equal(db.prepare('SELECT count(*) n FROM user').get().n,1);assert.equal(db.prepare("SELECT count(*) n FROM account WHERE providerId='google'").get().n,0);
 }finally{db.close();}
});

test('holding signup does not hide approved versions from the existing-owner onboarding route',async()=>{
 const {db,env,ownerId}=fixture();const original=globalThis.__registrationHoldUiAuth;
 globalThis.__registrationHoldUiAuth={api:{getSession:async()=>({user:{id:ownerId},session:{id:'synthetic-owner-session'}})}};
 const authPath=JSON.stringify(resolve('src/auth.ts').replaceAll('\\','/'));
 const plugin={name:'isolated-ui-session',setup(b){b.onResolve({filter:/^\.\/auth$/},args=>({path:args.path,namespace:'hold-ui'}));b.onLoad({filter:/.*/,namespace:'hold-ui'},()=>({loader:'js',resolveDir:process.cwd(),contents:`export {registrationHeld,googleCredentials} from ${authPath};export function createAuth(){return globalThis.__registrationHoldUiAuth;}`}));}};
 try{
  const {fetchRequest}=await load({entryPoints:['src/index.ts'],plugins:[plugin]});
  for(const path of ['/sign-in','/sign-up','/forgot-password']){
   const response=await fetchRequest(new Request(origin+path),env,{});assert.equal(response.status,200);const html=await response.text();
   const signup=html.match(/<form id="sign-up-form"[\s\S]*?<\/form>/)?.[0];assert.ok(signup);assert.match(signup,/data-policy-unavailable="true"/);assert.match(signup,/<button[^>]*disabled/);
   assert.ok(!signup.includes(versions.PUBLIC_TERMS_VERSION));
  }
  const response=await fetchRequest(new Request(origin+'/account/onboarding?app=cake'),env,{});assert.equal(response.status,200);const html=await response.text();
  assert.ok(html.includes(`name="termsVersion" value="${versions.PUBLIC_TERMS_VERSION}"`));assert.ok(html.includes(`name="privacyVersion" value="${versions.PUBLIC_PRIVACY_VERSION}"`));assert.match(html,/public-onboarding-form/);
 }finally{globalThis.__registrationHoldUiAuth=original;db.close();}
});
