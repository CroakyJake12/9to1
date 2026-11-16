import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';
async function load(source,plugins=[]){const r=await build({entryPoints:[source],bundle:true,write:false,platform:'node',format:'esm',plugins,loader:{'.txt':'text'}});return import('data:text/javascript;base64,'+Buffer.from(r.outputFiles[0].text).toString('base64')+'#'+crypto.randomUUID());}
const {prepareTurnstile,verifyTurnstile,turnstileRequired}=await load('src/turnstile.ts');
const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
function fixture(){
 const db=new DatabaseSync(':memory:');db.exec(readFileSync('migrations/0003_turnstile_redemptions.sql','utf8'));
 const DB={prepare(sql){return {bind(...values){return {first:async()=>db.prepare(sql).get(...values)??null,sql,values};}};},async batch(statements){
  db.exec('BEGIN');try{const r=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return r;}catch(e){db.exec('ROLLBACK');throw e;}
 }};
 return {db,env:{DB,APP_MODE:'production',AUTH_BASE_URL:origin,TURNSTILE_SITE_KEY:'synthetic-site-key',TURNSTILE_SECRET_KEY:'synthetic-server-secret'}};
}
const req=(path='/api/auth/sign-up/email',token='challenge-token',extra={})=>new Request(origin+path,{method:'POST',headers:{'content-type':'application/json',origin,'cf-connecting-ip':'203.0.113.8','x-forwarded-for':'untrusted'},body:JSON.stringify({email:'test@example.test','cf-turnstile-response':token,...extra})});
const success=(action='signup',overrides={})=>({success:true,hostname:new URL(origin).hostname,action,challenge_ts:new Date().toISOString(),...overrides});
test('only exact protected POST routes require a challenge; OAuth and bearer APIs stay unchanged',async()=>{
 const {db,env}=fixture();delete env.TURNSTILE_SECRET_KEY;
 for(const path of ['/api/auth/oauth2/token','/api/auth/sign-in/social','/api/auth/callback/google','/api/account/profile','/api/auth/sign-up/email/extra'])assert.equal(await prepareTurnstile(req(path),env),null);
 assert.equal(await prepareTurnstile(new Request(origin+'/sign-up'),env),null);db.close();
});
test('production missing/partial CAPTCHA configuration is closed, with only explicit loopback test mode exempt',async()=>{
 const {db,env}=fixture();
 for(const missing of ['TURNSTILE_SITE_KEY','TURNSTILE_SECRET_KEY']){const e={...env};delete e[missing];assert.equal((await prepareTurnstile(req(),e)).status,503);}
 assert.equal(turnstileRequired(req(),{...env,APP_MODE:'local-test-only'}),true);
 assert.equal(turnstileRequired(new Request('http://127.0.0.1:8798'),{...env,APP_MODE:'local-test-only',AUTH_BASE_URL:'http://127.0.0.1:8798'}),false);
 assert.equal(turnstileRequired(req(),{...env,APP_MODE:'local-test-only',AUTH_BASE_URL:'http://127.0.0.1:8798'}),true);db.close();
});
test('missing, oversized and malformed challenge requests stop before provider validation',async()=>{
 const {db,env}=fixture();for(const token of [null,'',' ','x'.repeat(2049),{}])assert.equal((await prepareTurnstile(req(undefined,token),env)).status,403);
 assert.equal((await prepareTurnstile(req(undefined,'valid',{padding:'x'.repeat(17000)}),env)).status,400);
 const cross=req();cross.headers.set('origin','https://foreign.example');assert.equal((await prepareTurnstile(cross,env)).status,403);db.close();
});
test('verified actions and hostnames must match exactly and the challenge must be fresh',async()=>{
 for(const changes of [{success:false},{success:'true'},{hostname:'foreign.example'},{hostname:'localhost'},
  {action:'login'},{challenge_ts:new Date(Date.now()-301000).toISOString()},{challenge_ts:'invalid'},
  {challenge_ts:new Date(Date.now()+31000).toISOString()}]){
  const {db,env}=fixture(),request=req(),prepared=await prepareTurnstile(request,env);
  assert.equal((await verifyTurnstile(prepared,request,env,async()=>Response.json(success('signup',changes)))).status,403);
  assert.equal(db.prepare('SELECT count(*) n FROM cake_turnstile_redemptions').get().n,0);db.close();
 }
});
test('provider errors, timeouts and malformed data fail closed without handler authority',async()=>{
 for(const transport of [async()=>{throw Error('transport failed');},async()=>new Response('down',{status:503}),
  async()=>new Response('not JSON'),async(_url,options)=>new Promise((resolve,reject)=>options.signal.addEventListener('abort',()=>reject(Error('aborted'))))]){
  const {db,env}=fixture(),request=req(),prepared=await prepareTurnstile(request,env);
  const denied=await verifyTurnstile(prepared,request,env,transport,5);assert.ok([403,503].includes(denied.status));db.close();
 }
});
test('one valid token permits one handler and stores only a short-lived digest',async()=>{
 const {db,env}=fixture(),request=req(),prepared=await prepareTurnstile(request,env);let calls=0;
 const transport=async(url,options)=>{calls++;assert.equal(url,'https://challenges.cloudflare.com/turnstile/v0/siteverify');const form=new URLSearchParams(options.body);
  assert.equal(form.get('secret'),env.TURNSTILE_SECRET_KEY);assert.equal(form.get('response'),'challenge-token');assert.equal(form.get('remoteip'),'203.0.113.8');return Response.json(success());};
 assert.equal(await verifyTurnstile(prepared,request,env,transport),null);
 assert.equal((await verifyTurnstile(prepared,request,env,transport)).status,403);assert.equal(calls,1);
 const row=db.prepare('SELECT * FROM cake_turnstile_redemptions').get();assert.match(row.tokenHash,/^[a-f0-9]{64}$/);assert.ok(row.expiresAt>Date.now());assert.ok(row.expiresAt<Date.now()+601000);assert.ok(!JSON.stringify(row).includes('challenge-token'));db.close();
});
test('concurrent successful Siteverify results still grant exactly one local handler',async()=>{
 const {db,env}=fixture(),request=req(),prepared=await prepareTurnstile(request,env);let release;let arrivals=0;
 const gate=new Promise(resolve=>{release=resolve;});
 const transport=async()=>{arrivals++;if(arrivals===2)release();await gate;return Response.json(success());};
 const results=await Promise.all([verifyTurnstile(prepared,request,env,transport),verifyTurnstile(prepared,request,env,transport)]);
 assert.equal(results.filter(r=>r===null).length,1);assert.equal(results.filter(r=>r?.status===403).length,1);db.close();
});
test('all protected actions map to the corresponding form and missing replay storage fails closed',async()=>{
 const {db,env}=fixture();for(const [path,action] of [['sign-in/email','login'],['sign-in/username','login'],['sign-up/email','signup'],['request-password-reset','recovery'],['send-verification-email','verify_email']])assert.equal((await prepareTurnstile(req('/api/auth/'+path),env)).action,action);
 db.exec('DROP TABLE cake_turnstile_redemptions');let called=false;const request=req();assert.equal((await verifyTurnstile(await prepareTurnstile(request,env),request,env,async()=>{called=true;})).status,503);assert.equal(called,false);db.close();
});
test('actual Worker boundary rejects invalid CAPTCHA before invoking the maintained auth handler',async()=>{
 let handled=0;globalThis.__captchaTestAuth={handler:async()=>{handled++;return Response.json({ok:true});}};
 const plugin={name:'isolated-identity-boundary',setup(b){b.onResolve({filter:/^\.\/(auth|limiter)$/},args=>({path:args.path,namespace:'captcha-test'}));b.onLoad({filter:/.*/,namespace:'captcha-test'},args=>({loader:'js',contents:args.path==='./auth'?'export function createAuth(){return globalThis.__captchaTestAuth;} export function googleCredentials(){return null;} export function registrationHeld(){return false;}':'export async function checkLoginLimit(){return {allowed:true};} export async function checkRateLimit(){return {allowed:true};} export async function readEmailIdentifier(){return "test@example.test";} export async function readLoginIdentifier(){return "test@example.test";}' }));}};
 const worker=await load('src/index.ts',[plugin]);const original=globalThis.fetch;
 try{
  const {db,env}=fixture();globalThis.fetch=async()=>Response.json(success('signup',{success:false}));
  assert.equal((await worker.fetchRequest(req(),env,{})).status,403);assert.equal(handled,0);
  globalThis.fetch=async()=>Response.json(success());assert.equal((await worker.fetchRequest(req(),env,{})).status,200);assert.equal(handled,1);
  assert.equal((await worker.fetchRequest(req(),env,{})).status,403);assert.equal(handled,1);db.close();
 }finally{globalThis.fetch=original;delete globalThis.__captchaTestAuth;}
});
