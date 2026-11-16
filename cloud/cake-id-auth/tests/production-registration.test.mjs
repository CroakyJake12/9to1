import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DatabaseSync } from 'node:sqlite';
import { build } from 'esbuild';

async function load(source) {
  const result = await build({ entryPoints: [source], bundle: true, write: false, platform: 'node', format: 'esm' });
  return import('data:text/javascript;base64,' + Buffer.from(result.outputFiles[0].text).toString('base64'));
}
const { readProductionSitesState, productionSitesPage } = await load('src/production-sites.ts');
const { installProductionSitesActions } = await load('src/production-sites-browser.ts');
const scopes = ['openid','profile','email','offline_access','cake:account:read','cake:profile:read','cake:profile:write','cake:sessions:read','cake:sessions:revoke'];
const sites = {cake: ['CakeMods website','https://cakemods.com/oauth/callback'],nine: ['9to1 website','https://9to1.uk/oauth/callback']};
const ids = {cake:'hOJlgeZQJbqMuMvTyVyVylYngztBvFSI',nine:'NWcyYbnuasNizDzUBSbKWwBqkFdKXCFE'};
function fixture() {
  const db = new DatabaseSync(':memory:');
  db.exec(`CREATE TABLE user(id TEXT PRIMARY KEY,role TEXT); INSERT INTO user VALUES('admin','admin'),('other','user');
    CREATE TABLE oauthClient(clientId TEXT,userId TEXT,name TEXT,redirectUris TEXT,tokenEndpointAuthMethod TEXT,applicationType TEXT,grantTypes TEXT,responseTypes TEXT,scopes TEXT,disabled INTEGER,skipConsent INTEGER,requirePKCE INTEGER,clientSecret TEXT);
    CREATE TABLE oauthClientResource(clientId TEXT,resourceId TEXT);`);
  const env = {API_RESOURCE:'https://issuer.example.test',DB:{prepare(sql){
    assert.match(sql,/^SELECT /,'metadata observation must never provision');
    return {bind(...args){return {first:async()=>db.prepare(sql).get(...args)??null,
      all:async()=>({success:true,results:db.prepare(sql).all(...args)})};}};
  }}};
  function seed(site,overrides={}) {
    const row={clientId:ids[site],userId:'admin',name:sites[site][0],redirectUris:JSON.stringify([sites[site][1]]),
      tokenEndpointAuthMethod:'none',applicationType:'web',grantTypes:'["authorization_code","refresh_token"]',
      responseTypes:'["code"]',scopes:JSON.stringify(scopes),disabled:0,skipConsent:0,requirePKCE:1,clientSecret:null,...overrides};
    db.prepare(`INSERT INTO oauthClient (${Object.keys(row).join(',')}) VALUES (${Object.keys(row).map(()=>'?').join(',')})`).run(...Object.values(row));
    db.prepare('INSERT INTO oauthClientResource VALUES(?,?)').run(row.clientId,env.API_RESOURCE);
  }
  return {db,env,seed};
}

test('non-administrators receive no production client metadata or action',async()=>{
  const {db,env}=fixture();
  assert.deepEqual(await readProductionSitesState(env,'other'),{kind:'not-admin'});
  assert.equal(productionSitesPage({kind:'not-admin'}).status,403);db.close();
});
test('missing pinned production clients require review without creating replacements',async()=>{
  const {db,env}=fixture();const state=await readProductionSitesState(env,'admin');
  assert.equal(state.kind,'admin');
  for(const site of ['cake','nine'])assert.deepEqual(state[site],{kind:'conflict'});
  assert.equal(db.prepare('SELECT count(*) AS count FROM oauthClient').get().count,0);
  assert.equal(db.prepare('SELECT count(*) AS count FROM user').get().count,2);db.close();
});
test('existing exact clients are reused and a revoked role is rejected on fresh observation',async()=>{
  const {db,env,seed}=fixture();seed('cake');seed('nine');
  assert.deepEqual(await readProductionSitesState(env,'admin'),{kind:'admin',cake:{kind:'ready',clientId:ids.cake,directSignIn:false,displayNameCurrent:true},nine:{kind:'ready',clientId:ids.nine,directSignIn:false,displayNameCurrent:true}});
  db.exec("UPDATE user SET role='user' WHERE id='admin'");
  assert.deepEqual(await readProductionSitesState(env,'admin'),{kind:'not-admin'});db.close();
});
test('conflicting callbacks, privilege, credentials and duplicate registrations are refused',async()=>{
  for(const wrong of [{redirectUris:'["https://www.cakemods.com/oauth/callback"]'},
    {requirePKCE:0},{skipConsent:2},{clientId:'unapproved-id'},{disabled:1},{clientSecret:'unexpected'},
    {scopes:'["openid"]'},{applicationType:'native'},{grantTypes:'["client_credentials"]'}]){
    const {db,env,seed}=fixture();seed('cake',wrong);
    assert.equal((await readProductionSitesState(env,'admin')).cake.kind,'conflict');db.close();
  }
  for(const mutate of [db=>db.exec('INSERT INTO oauthClient SELECT * FROM oauthClient'),
    db=>db.exec("UPDATE oauthClientResource SET resourceId='https://foreign.example.test'")]){
    const {db,env,seed}=fixture();seed('cake');mutate(db);
    assert.equal((await readProductionSitesState(env,'admin')).cake.kind,'conflict');db.close();
  }
});
test('pinned production definitions can observe enabled direct sign-in',async()=>{
  const {db,env,seed}=fixture();seed('cake',{skipConsent:1});seed('nine',{skipConsent:1});
  const state=await readProductionSitesState(env,'admin');assert.equal(state.cake.directSignIn,true);assert.equal(state.nine.directSignIn,true);db.close();
});
test('only the exact pinned CakeMods client may temporarily retain the legacy display name',async()=>{
  const {db,env,seed}=fixture();seed('cake',{name:'CAKE website',skipConsent:1});seed('nine',{skipConsent:1});
  const state=await readProductionSitesState(env,'admin');assert.equal(state.cake.kind,'ready');assert.equal(state.cake.directSignIn,true);assert.equal(state.cake.displayNameCurrent,false);assert.equal(state.nine.displayNameCurrent,true);db.close();
  for(const mismatch of [{clientId:'unapproved',name:'CAKE website'},{userId:'other',name:'CAKE website'},{name:'CAKE Website'},
    {name:'CAKE website',redirectUris:'["https://foreign.example/callback"]'},{name:'CAKE website',scopes:'["openid"]'},{name:'CAKE website',requirePKCE:0}]){
    const f=fixture();f.seed('cake',mismatch);assert.equal((await readProductionSitesState(f.env,'admin')).cake.kind,'conflict');f.db.close();
  }
  const f=fixture();f.seed('nine',{name:'CAKE website'});assert.equal((await readProductionSitesState(f.env,'admin')).nine.kind,'conflict');f.db.close();
});
test('page offers only the required direct-signin action and escapes public metadata',async()=>{
  const response=productionSitesPage({kind:'admin',cake:{kind:'ready',clientId:'unsafe<id>',directSignIn:false,displayNameCurrent:true},nine:{kind:'ready',clientId:ids.nine,directSignIn:true,displayNameCurrent:true}});
  const html=await response.text();
  assert.ok(html.includes('unsafe&lt;id&gt;'));assert.ok(html.includes(sites.cake[1]));assert.ok(html.includes(sites.nine[1]));
  assert.match(html,/id="connect-production-cake"/);assert.doesNotMatch(html,/id="connect-production-nine"/);
  assert.equal(response.headers.get('cache-control'),'no-store');
  assert.ok(response.headers.get('content-security-policy').includes("frame-ancestors 'none'"));
});

async function browser(replies,run,{site='cake',lock=true,locks=true}={}) {
  const original={document:globalThis.document,navigator:Object.getOwnPropertyDescriptor(globalThis,'navigator'),fetch:globalThis.fetch};
  const button={disabled:false},status={textContent:''};let submit;const calls=[];
  globalThis.document={getElementById:id=>id==='connect-production-'+site?{querySelector:()=>button,addEventListener:(_,fn)=>{submit=fn;}}:id==='production-'+site+'-status'?status:null};
  Object.defineProperty(globalThis,'navigator',{configurable:true,value:locks?{locks:{request:async(name,options,body)=>{
    assert.equal(name,'cake-connect-production-'+site);assert.deepEqual(options,{ifAvailable:true});return body(lock?{}:null);}}}:{}});
  globalThis.fetch=async(url,options)=>{calls.push({url,options});const reply=replies.shift();if(reply instanceof Error)throw reply;
    if(!reply)throw new Error('Unexpected request');return new Response(JSON.stringify(reply.body),{status:reply.status??200,headers:{'content-type':'application/json'}});};
  try{installProductionSitesActions();await run({button,status,calls,submit:()=>submit({preventDefault(){}})});}
  finally{globalThis.document=original.document;globalThis.fetch=original.fetch;if(original.navigator)Object.defineProperty(globalThis,'navigator',original.navigator);else delete globalThis.navigator;}
}
const missing={body:{kind:'admin',cake:{kind:'ready',clientId:ids.cake,directSignIn:false,displayNameCurrent:true},nine:{kind:'ready',clientId:ids.nine,directSignIn:false,displayNameCurrent:true}}};
const ready={body:{kind:'admin',cake:{kind:'ready',clientId:ids.cake,directSignIn:true,displayNameCurrent:true},nine:{kind:'ready',clientId:ids.nine,directSignIn:true,displayNameCurrent:true}}};
test('selected website uses one narrow administrator POST and exact canonical readback',async()=>{
  for(const site of ['cake','nine'])await browser([missing,{status:201,body:{}},ready],async({calls,button,status,submit})=>{
    await submit();await submit();assert.equal(calls.length,3);
    assert.equal(calls[1].url,'/account/production-sites');assert.equal(calls[1].options.method,'POST');
    assert.equal(calls[1].options.credentials,'same-origin');assert.deepEqual(JSON.parse(calls[1].options.body),{site});
    assert.ok(calls.filter(c=>c.options.method!=='POST').every(c=>c.url==='/account/production-sites/state'&&c.options.cache==='no-store'));
    assert.equal(button.disabled,true);assert.match(status.textContent,/Website details are synchronised/);
  },{site});
});
test('lost successful direct-signin change is reconciled once without a duplicate',async()=>{
  await browser([missing,new Error('lost response'),ready],async({calls,status,submit})=>{
    await submit();await submit();assert.equal(calls.filter(c=>c.options.method==='POST').length,1);assert.match(status.textContent,/Website details are synchronised/);
  });
});
test('browser cannot report branding complete while the canonical name is still pending',async()=>{
  const legacy={body:{...ready.body,cake:{...ready.body.cake,displayNameCurrent:false}}};
  await browser([legacy,{status:200,body:{}},legacy],async({submit,calls,status,button})=>{
    await submit();await submit();assert.equal(calls.filter(c=>c.options.method==='POST').length,1);assert.equal(button.disabled,true);assert.match(status.textContent,/could not be confirmed/);assert.doesNotMatch(status.textContent,/details are synchronised/);
  });
});
test('unknown POST result remains disabled and never reports success',async()=>{
  await browser([missing,new Error('lost response'),missing],async({calls,button,status,submit})=>{
    await submit();await submit();assert.equal(calls.length,3);assert.equal(button.disabled,true);assert.match(status.textContent,/could not be confirmed/);
  });
});
test('existing, conflicting and lost-administrator states never POST',async()=>{
  for(const reply of [ready,{body:{kind:'admin',cake:{kind:'conflict'}}},{body:{kind:'not-admin'}},{status:401,body:{error:'unauthorized'}}]){
    await browser([reply],async({calls,submit})=>{await submit();assert.equal(calls.length,1);assert.ok(calls.every(c=>c.options.method!=='POST'));});
  }
});
test('cross-tab conflict or unavailable lock suppresses reads and registration',async()=>{
  for(const options of [{lock:false},{locks:false}])await browser([],async({calls,submit})=>{await submit();assert.equal(calls.length,0);},options);
});
