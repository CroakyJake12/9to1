import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';

const compiled=await build({stdin:{contents:`export {prepareTurnstile,verifyTurnstile} from './src/turnstile.ts';
 export {flushSecurityEvents} from './src/security-telemetry.ts';`,resolveDir:process.cwd()},
 bundle:true,write:false,platform:'node',format:'esm'});
const poison='private@example.test SECRET_DO_NOT_LOG challenge_DO_NOT_LOG';
const origin='https://issuer.example.test';
const cases=[
 ['invalid issuer','configuration',env=>{env.AUTH_BASE_URL=poison;}],
 ['missing secret','configuration',env=>{delete env.TURNSTILE_SECRET_KEY;}],
 ['replay read error','replay_read',env=>{env.DB.prepare=()=>{throw new Error(poison);};}],
 ['provider transport error','siteverify_transport',()=>{},async()=>{throw new Error(poison);}],
 ['provider HTTP error','siteverify_response',()=>{},async()=>new Response(poison,{status:503})],
 ['unsuccessful replay commit','replay_commit',env=>{env.DB.batch=async()=>[{success:false,results:[]},{success:true,results:[]}];}],
 ['replay commit exception','replay_commit',env=>{env.DB.batch=async()=>{throw new Error(poison);};}],
];
for(const [label,stage,mutate,transport] of cases)test(`CAPTCHA ${label} emits only the bounded ${stage} reason`,async()=>{
 const db=new DatabaseSync(':memory:');db.exec(readFileSync('migrations/0003_turnstile_redemptions.sql','utf8'));
 const DB={prepare(sql){return {bind(...values){return {first:async()=>db.prepare(sql).get(...values)??null,sql,values};}};},
  async batch(statements){db.exec('BEGIN');try{const result=statements.map(({sql,values})=>({success:true,results:db.prepare(sql).all(...values)}));db.exec('COMMIT');return result;}catch(error){db.exec('ROLLBACK');throw error;}}};
 const env={DB,APP_MODE:'production',AUTH_BASE_URL:origin,TURNSTILE_SITE_KEY:'synthetic-site-key',TURNSTILE_SECRET_KEY:poison};
 mutate(env);
 const request=new Request(origin+'/api/auth/request-password-reset',{method:'POST',headers:{origin,'content-type':'application/json'},
  body:JSON.stringify({email:poison,'cf-turnstile-response':poison})});
 const original=console.warn,captured=[];console.warn=value=>captured.push(JSON.parse(value));
 try{
  const {prepareTurnstile,verifyTurnstile,flushSecurityEvents}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64')+'#'+crypto.randomUUID());
  const prepared=await prepareTurnstile(request,env);
  const response=prepared instanceof Response?prepared:await verifyTurnstile(prepared,request,env,transport??(async()=>Response.json({
   success:true,hostname:'issuer.example.test',action:'recovery',challenge_ts:new Date().toISOString(),untrusted:poison})));
  assert.equal(response.status,503);assert.deepEqual(await response.json(),{error:'verification_unavailable'});
  flushSecurityEvents();
  assert.deepEqual(captured,[{event:'account_security_events',counts:{captcha_unavailable:1,['captcha_unavailable_'+stage]:1}}]);
  assert.ok(!JSON.stringify(captured).includes(poison));
  assert.equal(db.prepare('SELECT count(*) n FROM cake_turnstile_redemptions').get().n,0);
 }finally{console.warn=original;db.close();}
});
