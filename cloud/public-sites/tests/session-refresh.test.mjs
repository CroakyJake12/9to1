import assert from 'node:assert/strict';
import {test} from 'node:test';
import {SessionEngine} from '../src/session-engine.mjs';

const origin='https://cakemods.com';
const nextTokens={access_token:'new-access',refresh_token:'new-refresh',expires_in:300};
function fixture(handler,overrides={}){
 const initial={access:'old-access',refresh:'old-refresh',clientId:'cake-client',origin,expires:Date.now()-1000,deadline:Date.now()+600000,...overrides};
 let stored=structuredClone(initial);const writes=[],calls=[];
 const storage={get:async()=>structuredClone(stored),put:async(key,value)=>{assert.equal(key,'session');stored=structuredClone(value);writes.push(structuredClone(value));},deleteAll:async()=>{stored=undefined;},setAlarm:async()=>{}};
 const env={IDENTITY:{fetch:async(url,options)=>{calls.push({url,options});return handler(url,options,stored);}}};
 const engine=new SessionEngine(storage,env);
 return {engine,storage,env,calls,writes,read:()=>structuredClone(stored),set:value=>{stored=structuredClone(value);},request:()=>engine.request('/api/account/current','GET',null,origin)};
}
test('concurrent expired requests rotate once and persist before using the new access token',async()=>{
 let rotations=0;
 const f=fixture(async(url,options,stored)=>{
  if(url.endsWith('/token')){rotations++;assert.equal(stored.refreshing,true);assert.equal(stored.refresh,'old-refresh');return Response.json(nextTokens);}
  assert.equal(stored.refreshing,false);assert.equal(stored.refresh,'new-refresh');assert.equal(options.headers.authorization,'Bearer new-access');
  return Response.json({accountId:'account'});
 });
 const results=await Promise.all([f.request(),f.request(),f.request()]);
 assert.deepEqual(results.map(r=>r.status),[200,200,200]);assert.equal(rotations,1);assert.equal(f.writes[0].refreshing,true);
});
test('restarted object refuses an unfinished rotation without replaying the refresh token',async()=>{
 const f=fixture(()=>{throw Error('must not contact provider');},{refreshing:true});
 const restarted=new SessionEngine(f.storage,f.env);
 assert.equal((await restarted.request('/api/account/current','GET',null,origin)).status,401);
 assert.equal(f.read(),undefined);assert.equal(f.calls.length,0);
});
test('lost refresh response expires local session and never retries the consumed token',async()=>{
 const f=fixture(async()=>{throw Error('response lost after rotation');});
 assert.equal((await f.request()).status,401);assert.equal(f.read(),undefined);
 assert.equal((await f.request()).status,401);assert.equal(f.calls.length,1);
});
test('failed response stream is an unknown rotation result and expires local session',async()=>{
 const f=fixture(async()=>new Response(new ReadableStream({start(controller){controller.error(Error('stream interrupted'));}}),{status:200}));
 assert.equal((await f.request()).status,401);assert.equal(f.read(),undefined);
 assert.equal((await f.request()).status,401);assert.equal(f.calls.length,1);
});
test('rate limiting and server outages retain credentials and apply bounded retry backoff',async()=>{
 for(const code of [429,500,503]){
  let attempts=0;
  const f=fixture(async(url)=>{
   if(!url.endsWith('/token'))return Response.json({accountId:'account'});
   attempts++;return attempts===1?new Response('unavailable',{status:code,headers:{'retry-after':'60'}}):Response.json(nextTokens);
  });
  const first=await f.request();assert.equal(first.status,503);assert.ok(Number(first.headers.get('retry-after'))>0);
  const state=f.read();assert.equal(state.access,'old-access');assert.equal(state.refresh,'old-refresh');assert.equal(state.refreshing,false);
  assert.equal((await f.request()).status,503);assert.equal(attempts,1);
  f.set({...state,retryAfter:Date.now()-1});assert.equal((await f.request()).status,200);assert.equal(attempts,2);
 }
});
test('invalid grant, rejected client and invalid successful token replies expire local session',async()=>{
 for(const reply of [()=>Response.json({error:'invalid_grant'},{status:400}),()=>Response.json({error:'unauthorized'},{status:401}),
   ()=>Response.json({access_token:'new-access',expires_in:300}),()=>Response.json({access_token:'new-access',refresh_token:'new-refresh',expires_in:0})]){
  const f=fixture(async()=>reply());assert.equal((await f.request()).status,401);assert.equal(f.read(),undefined);
  assert.equal((await f.request()).status,401);assert.equal(f.calls.length,1);
 }
});
