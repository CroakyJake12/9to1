import assert from 'node:assert/strict';
import {after, test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {createServer} from 'node:http';
import {build} from 'esbuild';
import {generateKeyPair, exportJWK, SignJWT} from 'jose';

// Use real SQLite and the installed bearer verifier; only the cookie-session
// lookup is supplied by this fixture. Keys and records are synthetic and local.
const keys=await generateKeyPair('EdDSA');
const jwk={...await exportJWK(keys.publicKey),kid:'profile-commit-test',alg:'EdDSA',use:'sig'};
const server=createServer((req,res)=>{res.writeHead(200,{'content-type':'application/json'});res.end(JSON.stringify({keys:[jwk]}));});
await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
after(()=>new Promise(resolve=>{server.closeAllConnections();server.close(resolve);}));
const jwksUrl=`http://127.0.0.1:${server.address().port}/jwks`;
const result=await build({entryPoints:['src/resource-api.ts'],bundle:true,write:false,platform:'node',format:'esm',
  banner:{js:"import {createRequire} from 'node:module';const require=createRequire(process.cwd()+'/package.json');"}});
const {handleResourceApi}=await import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64'));
const origin='https://issuer.example.test',accountA='11111111-1111-4111-8111-111111111111',accountB='22222222-2222-4222-8222-222222222222';
const sessionA='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',sessionB='bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const replacement='cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const created='2026-01-01T00:00:00.000Z';
const body=(fields={name:'Changed name'},expectedRevision=4)=>({expectedRevision,fields});
const waitUntil=async time=>{const delay=time-Date.now();if(delay>0)await new Promise(resolve=>setTimeout(resolve,delay));};

function fixture(){
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 for(const file of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql','0005_public_account_policy.sql'])
  db.exec(readFileSync(new URL('../migrations/'+file,import.meta.url),'utf8'));
 for(const [id,sid,name] of [[accountA,sessionA,'Alice'],[accountB,sessionB,'Bob']]){
  db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,username,image,pronouns,jobTitle,profileRevision) VALUES(?,?,?,?,?,?,?,?,?,?,?)')
   .run(id,name,name.toLowerCase()+'@example.test',1,created,created,name.toLowerCase(),'https://images.example.test/avatar.png','they/them','Original job',4);
  db.prepare('INSERT INTO session(id,userId,token,expiresAt,createdAt,updatedAt) VALUES(?,?,?,?,?,?)')
   .run(sid,id,'synthetic-'+sid,new Date(Date.now()+3600000).toISOString(),created,created);
 }
 db.prepare('INSERT INTO oauthClient(id,clientId,redirectUris,disabled) VALUES(?,?,?,0)')
  .run('native-client','native-client','["http://127.0.0.1:43821/cake-id/callback/"]');
 let beforeUpdate,updates=0;
 const env={AUTH_BASE_URL:origin,API_RESOURCE:origin,DB:{prepare(sql){let values=[];return {bind(...args){values=args;return this;},async first(){
   if(sql.startsWith('UPDATE "user" SET')){updates++;beforeUpdate?.();}
   return db.prepare(sql).get(...values)??null;
 }}}}};
 const auth={options:{baseURL:origin,basePath:'/api/auth'},$context:Promise.resolve({getPlugin(name){return name==='jwt'?{options:{jwks:{remoteUrl:jwksUrl}}}:{options:{}};}}),
  api:{async getSession(){return {user:{id:accountA},session:{id:sessionA}};}}};
 return {db,env,auth,updates:()=>updates,setBeforeUpdate(fn){beforeUpdate=fn;},profile(id=accountA){return db.prepare('SELECT * FROM user WHERE id=?').get(id);},close(){db.close();}};
}
async function bearer(exp=Math.floor(Date.now()/1000)+300){
 return new SignJWT({sub:accountA,sid:sessionA,scope:'cake:profile:write',client_id:'native-client',azp:'native-client'})
  .setProtectedHeader({alg:'EdDSA',kid:'profile-commit-test',typ:'at+jwt'}).setIssuer(origin+'/api/auth').setAudience(origin)
  .setIssuedAt().setExpirationTime(exp).sign(keys.privateKey);
}
function request(payload,token){return new Request(origin+'/api/account/profile',{method:'PATCH',headers:{origin,'content-type':'application/json',...(token?{authorization:'Bearer '+token}:{})},body:JSON.stringify(payload)});}

async function pausedPatch(f,change,{token,payload=body()}={}){
 const reading=Promise.withResolvers(),release=Promise.withResolvers();
 const stream=new ReadableStream({async pull(controller){
  reading.resolve();await release.promise;controller.enqueue(new TextEncoder().encode(JSON.stringify(payload)));controller.close();
 }},{highWaterMark:0});
 const req=new Request(origin+'/api/account/profile',{method:'PATCH',duplex:'half',headers:{origin,'content-type':'application/json',...(token?{authorization:'Bearer '+token}:{})},body:stream});
 const pending=handleResourceApi(req,f.env,f.auth);
 let timer;
 try{
  // With highWaterMark 0, pull starts only when readBody asks for body bytes,
  // which is after real bearer/session authorization in the resource handler.
  await Promise.race([reading.promise,pending.then(r=>{throw Error('handler ended before reading the body: '+r.status);}),
   new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('body-read checkpoint timeout')),4000);})]);
  clearTimeout(timer);await change();
 }finally{clearTimeout(timer);release.resolve();}
 return pending;
}
async function assertDeniedWithoutChange(f,response,before){
 const data=await response.json();
 assert.equal(response.status,401,'lost authority must be rejected at commit');
 assert.deepEqual(data,{error:'session_revoked_or_expired'});
 assert.deepEqual(f.profile(),before,'no profile field or revision may change after authority is lost');
}

test('legitimate streamed bearer update preserves normalization and optional clearing',async()=>{
 const f=fixture();try{
  const other=f.profile(accountB);
  const r=await pausedPatch(f,async()=>{}, {token:await bearer(),payload:body({name:'  Alice Updated  ',username:'  ALICE_NEW  ',pronouns:null,job:null,icon:null})});
  assert.equal(r.status,200);const data=(await r.json()).profile;
  assert.equal(data.name,'Alice Updated');assert.equal(data.username,'alice_new');assert.equal(data.pronouns,null);assert.equal(data.job,null);assert.equal(data.icon,null);assert.equal(data.revision,5);
  assert.deepEqual(f.profile(accountB),other);
 }finally{f.close();}
});

test('paused bearer cannot commit after the original session is deleted',async()=>{
 const f=fixture();try{const before=f.profile();const token=await bearer();
  const r=await pausedPatch(f,async()=>{f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA);},{token});
  await assertDeniedWithoutChange(f,r,before);
 }finally{f.close();}
});

test('a replacement session for the same account does not revive a paused old token',async()=>{
 const f=fixture();try{const before=f.profile();const token=await bearer();
  const r=await pausedPatch(f,async()=>{
   f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA);
   f.db.prepare('INSERT INTO session(id,userId,token,expiresAt,createdAt,updatedAt) VALUES(?,?,?,?,?,?)').run(replacement,accountA,'synthetic-replacement',new Date(Date.now()+3600000).toISOString(),created,created);
  },{token});await assertDeniedWithoutChange(f,r,before);
 }finally{f.close();}
});

test('original session natural expiry during streamed body prevents commit',async()=>{
 const f=fixture();try{const before=f.profile(),expires=Date.now()+1200;
  f.db.prepare('UPDATE session SET expiresAt=? WHERE id=?').run(new Date(expires).toISOString(),sessionA);
  const token=await bearer();const r=await pausedPatch(f,async()=>{assert.ok(Date.now()<expires);await waitUntil(expires+30);},{token});
  await assertDeniedWithoutChange(f,r,before);
 }finally{f.close();}
});

test('bearer natural expiry during streamed body prevents commit while session stays active',async()=>{
 const f=fixture();try{const before=f.profile(),expires=Math.floor(Date.now()/1000)+2,token=await bearer(expires);
  const r=await pausedPatch(f,async()=>{assert.ok(Date.now()<expires*1000);await waitUntil(expires*1000+30);},{token});
  await assertDeniedWithoutChange(f,r,before);
  assert.ok(f.db.prepare("SELECT 1 FROM session WHERE id=? AND julianday(expiresAt)>julianday('now')").get(sessionA));
 }finally{f.close();}
});

test('cookie-authorized streamed update also requires its original session at commit',async()=>{
 const f=fixture();try{const before=f.profile();const r=await pausedPatch(f,async()=>{f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA);});
  await assertDeniedWithoutChange(f,r,before);
 }finally{f.close();}
});

test('an original session reassigned to another account cannot authorize the old account update',async()=>{
 const f=fixture();try{const before=f.profile(),other=f.profile(accountB),token=await bearer();
  const r=await pausedPatch(f,async()=>{f.db.prepare('UPDATE session SET userId=? WHERE id=?').run(accountB,sessionA);},{token});
  await assertDeniedWithoutChange(f,r,before);assert.deepEqual(f.profile(accountB),other);
 }finally{f.close();}
});

test('revocation immediately before SQLite UPDATE still prevents mutation',async()=>{
 const f=fixture();try{const before=f.profile();f.setBeforeUpdate(()=>f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA));
  const r=await handleResourceApi(request(body(),await bearer()),f.env,f.auth);
  assert.equal(f.updates(),1);await assertDeniedWithoutChange(f,r,before);
 }finally{f.close();}
});

test('legitimate stale revision remains 409 but lost authority does not reveal a revision',async()=>{
 const f=fixture();try{const before=f.profile();
  const valid=await handleResourceApi(request(body({name:'New name'},3),await bearer()),f.env,f.auth);
  assert.equal(valid.status,409);assert.deepEqual(await valid.json(),{error:'profile_conflict',revision:4});assert.deepEqual(f.profile(),before);
  const denied=await pausedPatch(f,async()=>{f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA);},{token:await bearer(),payload:body({name:'New name'},3)});
  await assertDeniedWithoutChange(f,denied,before);
 }finally{f.close();}
});

test('reserved and duplicate usernames retain their existing behavior',async()=>{
 const f=fixture();try{const before=f.profile();
  for(const username of ['croakyjake','bob']){
   const r=await handleResourceApi(request(body({username}),await bearer()),f.env,f.auth);
   assert.equal(r.status,409);assert.deepEqual(await r.json(),{error:'username_unavailable'});assert.deepEqual(f.profile(),before);
  }
  f.db.prepare('UPDATE cake_reserved_usernames SET ownerUserId=? WHERE username=?').run(accountA,'croakyjake');
  const r=await handleResourceApi(request(body({username:' CroakyJake '}),await bearer()),f.env,f.auth);
  assert.equal(r.status,200);assert.equal((await r.json()).profile.username,'croakyjake');
 }finally{f.close();}
});
