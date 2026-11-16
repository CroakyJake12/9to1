import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';
async function load(source){const r=await build({entryPoints:[source],bundle:true,write:false,platform:'node',format:'esm',banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"}});return import('data:text/javascript;base64,'+Buffer.from(r.outputFiles[0].text).toString('base64'));}
const {createInvitation,consumeInvitation,invitationRequest,invitationsRequired}=await load('src/invitations.ts');
const {createAuthOptions}=await load('src/auth.ts');
const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
function fixture(){
 const db=new DatabaseSync(':memory:');db.exec("CREATE TABLE user (id TEXT PRIMARY KEY,email TEXT,role TEXT); INSERT INTO user VALUES ('admin','admin@example.test','admin'),('ordinary','ordinary@example.test','user');");
 db.exec(readFileSync('migrations/0004_account_invitations.sql','utf8'));let beforeBatch;
 const DB={prepare(sql){return {bind(...values){return {sql,values,first:async()=>db.prepare(sql).get(...values)??null};}};},async batch(statements){
  beforeBatch?.();db.exec('BEGIN');try{const results=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return results;}catch(e){db.exec('ROLLBACK');throw e;}
 }};
 return {db,env:{DB,APP_MODE:'production',AUTH_BASE_URL:origin,API_RESOURCE:origin,AUTH_SECRET:'synthetic-only-configuration-secret'.repeat(2),PUBLIC_TERMS_VERSION:'fixture-terms-v1',PUBLIC_PRIVACY_VERSION:'fixture-privacy-v1'},setBeforeBatch:callback=>{beforeBatch=callback;}};
}
async function invite(env,email='invitee@example.test',hours=72){const r=await createInvitation(env,'admin',email,hours);assert.equal(r.status,201);return r.json();}
test('only a canonical administrator can create an expiring email-bound invitation',async()=>{
 const {db,env}=fixture();assert.equal((await createInvitation(env,'ordinary','invitee@example.test')).status,403);
 const r=await createInvitation(env,'admin','  Invitee@Example.test  ',72);assert.equal(r.status,201);assert.equal(r.headers.get('cache-control'),'no-store');
 const data=await r.json();assert.match(data.code,/^[a-zA-Z0-9_-]{43}$/);assert.equal(data.invitation.email,'invitee@example.test');
 const stored=db.prepare('SELECT * FROM cake_account_invitations').get();assert.match(stored.tokenHash,/^[a-f0-9]{64}$/);assert.ok(!JSON.stringify(stored).includes(data.code));assert.equal(stored.createdBy,'admin');assert.equal(stored.consumedAt,null);db.close();
});
test('administrator role is checked inside the creation write, not only at preflight',async()=>{
 const {db,env,setBeforeBatch}=fixture();setBeforeBatch(()=>db.exec("UPDATE user SET role='user' WHERE id='admin'"));
 assert.equal((await createInvitation(env,'admin','invitee@example.test')).status,403);assert.equal(db.prepare('SELECT count(*) n FROM cake_account_invitations').get().n,0);db.close();
});
test('invalid email, invalid expiry and existing accounts never produce invitations',async()=>{
 const {db,env}=fixture();for(const email of ['','invalid','two@wrong@host.test','with space@example.test'])assert.equal((await createInvitation(env,'admin',email)).status,400);
 for(const hours of [0,169,-1,'72',1.5])assert.equal((await createInvitation(env,'admin','invitee@example.test',hours)).status,400);
 assert.equal((await createInvitation(env,'admin','ordinary@example.test')).status,409);db.close();
});
test('wrong email, expired, revoked and already-used codes are refused',async()=>{
 const {db,env}=fixture();const data=await invite(env);
 assert.equal(await consumeInvitation(env,'foreign@example.test',data.code),false);
 assert.equal(await consumeInvitation(env,'invitee@example.test','bad-code'),false);
 assert.equal(await consumeInvitation(env,'INVITEE@example.test',data.code),true);
 assert.equal(await consumeInvitation(env,'invitee@example.test',data.code),false);
 const expired=await invite(env,'expired@example.test');db.prepare('UPDATE cake_account_invitations SET expiresAt=? WHERE id=?').run(Date.now()-1,expired.invitation.id);
 assert.equal(await consumeInvitation(env,'expired@example.test',expired.code),false);
 const revoked=await invite(env,'revoked@example.test');db.prepare('UPDATE cake_account_invitations SET revokedAt=? WHERE id=?').run(Date.now(),revoked.invitation.id);
 assert.equal(await consumeInvitation(env,'revoked@example.test',revoked.code),false);db.close();
});
test('concurrent attempts can consume one invitation only once',async()=>{
 const {db,env}=fixture();const data=await invite(env);const outcomes=await Promise.all(Array.from({length:8},()=>consumeInvitation(env,'invitee@example.test',data.code)));
 assert.equal(outcomes.filter(Boolean).length,1);db.close();
});
test('duplicate active invitation is refused; expiry permits a new code and retires the old one',async()=>{
 const {db,env}=fixture();const first=await invite(env);assert.equal((await createInvitation(env,'admin','invitee@example.test')).status,409);
 db.prepare('UPDATE cake_account_invitations SET expiresAt=? WHERE id=?').run(Date.now()-1,first.invitation.id);
 const second=await invite(env);assert.notEqual(first.code,second.code);assert.equal(await consumeInvitation(env,'invitee@example.test',first.code),false);assert.equal(await consumeInvitation(env,'invitee@example.test',second.code),true);db.close();
});
test('actual provisioning hook rejects direct/Google signup and requires a body invitation for new email accounts',async()=>{
 const {db,env}=fixture(),options=createAuthOptions(env),gate=options.user.validateUserInfo;
 const user={email:'invitee@example.test',emailVerified:false};const source={action:'create-user',method:'email-password'};
 const acknowledgement={ageBand:'18_plus',acceptTerms:true,termsVersion:env.PUBLIC_TERMS_VERSION,privacyVersion:env.PUBLIC_PRIVACY_VERSION};
 assert.equal((await gate({user,source},{path:'/sign-up/email',body:acknowledgement})).error,'invitation_required');
 const data=await invite(env);
 assert.equal((await gate({user,source},{path:'/sign-up/email',body:acknowledgement,query:{invitationCode:data.code}})).error,'invitation_required');
 assert.equal((await gate({user:{...user,emailVerified:true},source:{action:'create-user',method:'oauth',oauth:{providerId:'google'}}},{body:{invitationCode:data.code}})).error,'invitation_required');
 assert.equal(await gate({user,source},{path:'/sign-up/email',body:{...acknowledgement,invitationCode:data.code}}),undefined);
 assert.equal((await gate({user,source},{path:'/sign-up/email',body:{...acknowledgement,invitationCode:data.code}})).error,'invitation_required');
 assert.equal(options.emailAndPassword.requireEmailVerification,true);assert.equal(options.emailAndPassword.autoSignIn,false);db.close();
});
test('existing verified Google linking remains available; test exemption cannot be activated on a public origin',async()=>{
 const {db,env}=fixture();const options=createAuthOptions(env);
 assert.equal(await options.user.validateUserInfo({user:{email:'ordinary@example.test',emailVerified:true},source:{action:'link-account',method:'oauth',oauth:{providerId:'google'}}},{}),undefined);
 assert.equal(invitationsRequired({...env,APP_MODE:'local-test-only'}),true);
 assert.equal(invitationsRequired({...env,APP_MODE:'local-test-only',AUTH_BASE_URL:'http://127.0.0.1:8798'}),false);db.close();
});
test('administrative HTTP creation requires same-origin JSON and canonical authority',async()=>{
 const {db,env}=fixture();const request=(requestOrigin=origin)=>new Request(origin+'/account/invitations',{method:'POST',headers:{origin:requestOrigin,'content-type':'application/json'},body:JSON.stringify({email:'invitee@example.test'})});
 assert.equal((await invitationRequest(request('https://foreign.example'),env,'admin')).status,403);
 assert.equal((await invitationRequest(request(),env,'ordinary')).status,403);
 assert.equal((await invitationRequest(request(),env,'admin')).status,201);db.close();
});
