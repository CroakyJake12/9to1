import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DatabaseSync } from 'node:sqlite';
import { build } from 'esbuild';

async function load(source) {
  const result = await build({ entryPoints: [source], bundle: true, write: false, platform: 'node', format: 'esm' });
  return import('data:text/javascript;base64,' + Buffer.from(result.outputFiles[0].text).toString('base64'));
}
const { readPresentAppState, readStagingAppsState, stagingAppsAccountPage } = await load('src/staging-apps.ts');
const { installPresentAppAction } = await load('src/staging-apps-browser.ts');
const callback = 'https://present-4581358882177bc7.9to1.uk/oauth/callback';
const scopes = ['openid','profile','email','offline_access','cake:account:read','cake:profile:read','cake:profile:write','cake:sessions:read','cake:sessions:revoke'];
function fixture() {
  const db = new DatabaseSync(':memory:');
  db.exec(`CREATE TABLE user(id TEXT PRIMARY KEY,role TEXT); INSERT INTO user VALUES('admin','admin'),('other','user');
    CREATE TABLE oauthClient(clientId TEXT,userId TEXT,name TEXT,redirectUris TEXT,tokenEndpointAuthMethod TEXT,applicationType TEXT,grantTypes TEXT,responseTypes TEXT,scopes TEXT,disabled INTEGER,skipConsent INTEGER,requirePKCE INTEGER,clientSecret TEXT);
    CREATE TABLE oauthClientResource(clientId TEXT,resourceId TEXT);`);
  let queries = [];
  const env = { API_RESOURCE: 'https://issuer.example.test', DB: { prepare(sql) {
    assert.match(sql, /^SELECT /, 'owner metadata reader is read-only'); queries.push(sql);
    return { bind(...args) { return { first: async () => db.prepare(sql).get(...args) ?? null,
      all: async () => ({success:true,results:db.prepare(sql).all(...args)}) }; } };
  } } };
  function seed(overrides = {}) {
    const row = {clientId:'public-present',userId:'admin',name:'9to1 Present preview',redirectUris:JSON.stringify([callback]),
      tokenEndpointAuthMethod:'none',applicationType:'web',grantTypes:JSON.stringify(['authorization_code','refresh_token']),
      responseTypes:'["code"]',scopes:JSON.stringify(scopes),disabled:0,skipConsent:0,requirePKCE:1,clientSecret:null,...overrides};
    db.prepare(`INSERT INTO oauthClient (${Object.keys(row).join(',')}) VALUES (${Object.keys(row).map(()=>'?').join(',')})`).run(...Object.values(row));
    db.prepare('INSERT INTO oauthClientResource VALUES(?,?)').run(row.clientId,env.API_RESOURCE);
  }
  return {db,env,seed,queries};
}
test('Present metadata requires the current canonical administrator role', async () => {
  const {db,env,queries} = fixture();
  assert.deepEqual(await readPresentAppState(env,'other'),{kind:'not-admin'});
  assert.equal(queries.length,1); db.close();
});
test('Missing Present has exact independent public client template; old clients retain their own callbacks', async () => {
  const {db,env} = fixture();
  const present = await readPresentAppState(env,'admin');
  assert.deepEqual(present,{kind:'missing',registration:{client_name:'9to1 Present preview',redirect_uris:[callback],
    token_endpoint_auth_method:'none',application_type:'web',grant_types:['authorization_code','refresh_token'],response_types:['code'],scope:scopes.join(' ')}});
  const staging = await readStagingAppsState(env,'admin');
  assert.equal(staging.registration.redirect_uris[0],'https://9to1-apps-staging-20261005.jcbailey008.workers.dev/oauth/callback');
  assert.equal(staging.native.registration.redirect_uris[0],'http://127.0.0.1:43821/cake-id/callback/');
  assert.equal(db.prepare('SELECT count(*) AS count FROM oauthClient').get().count,0); db.close();
});
test('Exact existing Present client is observed without creating one', async () => {
  const {db,env,seed} = fixture();seed();
  assert.deepEqual(await readPresentAppState(env,'admin'),{kind:'ready',clientId:'public-present'});db.close();
});
test('Mismatched secret, PKCE, consent, callback, scope, disabled, resource and duplicate metadata block registration', async () => {
  for(const mismatch of [{clientSecret:'not-a-public-client'},{requirePKCE:0},{skipConsent:1},{disabled:1},
    {redirectUris:'["https://wrong.example.test/oauth/callback"]'},{scopes:'["openid"]'},{tokenEndpointAuthMethod:'client_secret_basic'}]) {
    const {db,env,seed}=fixture();seed(mismatch);assert.equal((await readPresentAppState(env,'admin')).kind,'conflict');db.close();
  }
  for(const mutate of [db=>db.exec("UPDATE oauthClientResource SET resourceId='https://wrong.example.test'"),
    db=>db.exec('INSERT INTO oauthClient SELECT * FROM oauthClient')]) {
    const {db,env,seed}=fixture();seed();mutate(db);assert.equal((await readPresentAppState(env,'admin')).kind,'conflict');db.close();
  }
});
test('Account page exposes exact callback and its independent action only to an administrator', async () => {
  const {db,env}=fixture();const staging=await readStagingAppsState(env,'admin');const present=await readPresentAppState(env,'admin');
  const html=await stagingAppsAccountPage('Admin <unsafe>',staging,present).text();
  assert.match(html,/id="connect-present-app"/);assert.match(html,/id="connect-staging-apps"/);assert.ok(html.includes(callback));assert.ok(html.includes('Admin &lt;unsafe&gt;'));
  assert.doesNotMatch(await stagingAppsAccountPage('Other',{kind:'not-admin'},{kind:'not-admin'}).text(),/connect-present-app|connect-staging-apps/);db.close();
});

async function withBrowser(replies,run,{lock=true}={}) {
  const original={document:globalThis.document,navigator:Object.getOwnPropertyDescriptor(globalThis,'navigator'),fetch:globalThis.fetch};
  const button={disabled:false},status={textContent:''};let submit;const calls=[];
  globalThis.document={getElementById:id=>id==='connect-present-app'?{querySelector:()=>button,addEventListener:(_,callback)=>{submit=callback;}}:id==='present-app-status'?status:null};
  Object.defineProperty(globalThis,'navigator',{configurable:true,value:{locks:{request:async(name,options,body)=>{assert.equal(name,'cake-connect-present-preview');assert.deepEqual(options,{ifAvailable:true});return body(lock?{}:null);}}}});
  globalThis.fetch=async(url,options)=>{calls.push({url,options});const reply=replies.shift();if(reply instanceof Error)throw reply;if(!reply)throw new Error('Unexpected request');return new Response(JSON.stringify(reply.body),{status:reply.status??200,headers:{'content-type':'application/json'}});};
  try {installPresentAppAction();await run({button,status,calls,submit:()=>submit({preventDefault(){}})});}
  finally {globalThis.document=original.document;globalThis.fetch=original.fetch;if(original.navigator)Object.defineProperty(globalThis,'navigator',original.navigator);else delete globalThis.navigator;}
}
const missing={body:{kind:'missing',registration:{client_name:'9to1 Present preview',redirect_uris:[callback]}}};
const ready={body:{kind:'ready',clientId:'public-present'}};
test('Actual action uses one same-origin maintained POST then canonical readback, never staging registration', async () => {
  await withBrowser([missing,{status:201,body:{}},ready],async({button,status,calls,submit})=>{
    await submit();await submit();assert.equal(calls.length,3);assert.equal(calls[1].url,'/api/auth/oauth2/create-client');
    assert.equal(calls[1].options.credentials,'same-origin');assert.equal(calls[1].options.method,'POST');assert.deepEqual(JSON.parse(calls[1].options.body),missing.body.registration);
    assert.ok(calls.filter(c=>c.options.method!=='POST').every(c=>c.url==='/account/present-app'&&c.options.cache==='no-store'));
    assert.equal(button.disabled,true);assert.match(status.textContent,/connected.*public-present/);
  });
});
test('Lost registration reply reconciles success without retrying the write', async () => {
  await withBrowser([missing,new Error('reply lost'),ready],async({calls,status,submit})=>{await submit();await submit();assert.equal(calls.filter(c=>c.options.method==='POST').length,1);assert.match(status.textContent,/connected/);});
});
test('Unknown registration outcome stays disabled and never reports success', async () => {
  await withBrowser([missing,new Error('reply lost'),missing],async({button,calls,status,submit})=>{await submit();await submit();assert.equal(calls.length,3);assert.equal(button.disabled,true);assert.match(status.textContent,/could not be confirmed/);});
});
test('Existing ready, conflicting or expired administrator state never causes a POST', async () => {
  for(const reply of [ready,{body:{kind:'conflict'}},{status:401,body:{error:'unauthorized'}}])
    await withBrowser([reply],async({calls,submit})=>{await submit();assert.equal(calls.length,1);assert.ok(calls.every(c=>c.options.method!=='POST'));});
});
test('A competing tab lock prevents both reads and registration from this page', async () => {
  await withBrowser([],async({calls,status,submit})=>{await submit();assert.equal(calls.length,0);assert.match(status.textContent,/Another account tab/);},{lock:false});
});
