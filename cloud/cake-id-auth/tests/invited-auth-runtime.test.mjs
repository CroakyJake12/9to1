import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {randomBytes} from 'node:crypto';
import {build} from 'esbuild';
const compiled=await build({stdin:{contents:`export {createAuthOptions} from './src/auth.ts';export {createInvitation} from './src/invitations.ts';export {betterAuth} from 'better-auth';`,resolveDir:process.cwd()},bundle:true,write:false,platform:'node',format:'esm',banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"}});
const {createAuthOptions,createInvitation,betterAuth}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'));

test('real pinned provider creates one invited user, records canonical policy, and requires real email verification before password sign-in',async()=>{
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 for(const name of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql','0003_turnstile_redemptions.sql','0004_account_invitations.sql','0005_public_account_policy.sql','0006_password_reset_fence.sql'])db.exec(readFileSync('migrations/'+name,'utf8'));
 const admin=crypto.randomUUID(),origin='https://issuer.example.test',mail=[];
 db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,role) VALUES(?,?,?,1,?,?,?)').run(admin,'Operator','operator@example.test','2026-01-01','2026-01-01','admin');
 const DB={prepare(sql){return {bind(...values){return {sql,values,first:async()=>db.prepare(sql).get(...values)??null,run:async()=>({success:true,meta:db.prepare(sql).run(...values)}),all:async()=>({success:true,results:db.prepare(sql).all(...values)})};}};},async batch(statements){db.exec('BEGIN');try{const r=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return r;}catch(e){db.exec('ROLLBACK');throw e;}}};
 const env={DB,APP_MODE:'production',AUTH_BASE_URL:origin,API_RESOURCE:origin,AUTH_SECRET:randomBytes(48).toString('base64url'),PUBLIC_TERMS_VERSION:'fixture-terms-v1',PUBLIC_PRIVACY_VERSION:'fixture-privacy-v1',EMAIL_MODE:'cloudflare-email-service',EMAIL_FROM:'identity@example.test',EMAIL_DOMAIN:'example.test',EMAIL:{send:async message=>{mail.push(message);}}};
 const options=createAuthOptions(env);
 // Actual provider and its hooks, using Node SQLite instead of a remote D1
 // transport. Only the mail binding captures delivery; no external network.
 const auth=betterAuth({...options,database:db});
 const call=(path,body)=>auth.handler(new Request(origin+'/api/auth'+path,{method:'POST',headers:{origin,'content-type':'application/json'},body:JSON.stringify(body)}));
 const password='  '+randomBytes(24).toString('base64url')+'  ';
 try{
  const invitation=await (await createInvitation(env,admin,'invited@example.test')).json();
  const payload={name:'Invited Test',username:'invited_test',email:'invited@example.test',password,invitationCode:invitation.code,ageBand:'16_17',acceptTerms:true,termsVersion:env.PUBLIC_TERMS_VERSION,privacyVersion:env.PUBLIC_PRIVACY_VERSION,callbackURL:origin+'/sign-in'};
  const denied=await call('/sign-up/email',{...payload,ageBand:'under_13'});assert.equal(denied.status,403);assert.equal(db.prepare('SELECT consumedAt FROM cake_account_invitations').get().consumedAt,null);
  const response=await call('/sign-up/email',payload);assert.equal(response.status,200);const created=await response.json();assert.equal(created.token,null);
  const account=db.prepare('SELECT id,emailVerified,role FROM user WHERE email=?').get(payload.email);assert.equal(account.id,created.user.id);assert.equal(account.emailVerified,0);assert.equal(account.role,'user');
  const policy=db.prepare('SELECT * FROM cake_public_account_policy WHERE accountId=?').get(account.id);assert.equal(policy.ageBand,'16_17');assert.equal(policy.termsVersion,env.PUBLIC_TERMS_VERSION);assert.ok(policy.acceptedAt>Date.now()-60000);
  assert.equal((await call('/sign-in/email',{email:payload.email,password})).status,403);
  assert.equal(mail.length,1);assert.equal(mail[0].to,payload.email);
  const verificationUrl=mail[0].text.match(/https:\/\/\S+/)?.[0];assert.ok(verificationUrl);assert.equal(new URL(verificationUrl).origin,origin);
  const verified=await auth.handler(new Request(verificationUrl));assert.equal(verified.status,302);assert.equal(db.prepare('SELECT emailVerified FROM user WHERE id=?').get(account.id).emailVerified,1);
  assert.equal((await call('/sign-in/email',{email:payload.email,password})).status,200);
  const replay=await call('/sign-up/email',{...payload,email:'other@example.test',username:'other_invited'});assert.equal(replay.status,403);
  assert.equal(db.prepare('SELECT count(*) n FROM user').get().n,2);assert.equal(db.prepare('SELECT count(*) n FROM cake_account_invitations WHERE consumedAt IS NOT NULL').get().n,1);
 }finally{db.close();}
});
