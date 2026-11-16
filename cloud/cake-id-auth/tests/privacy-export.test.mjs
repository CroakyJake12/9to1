import assert from 'node:assert/strict';
import {after, test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {createServer} from 'node:http';
import {build} from 'esbuild';
import {generateKeyPair, exportJWK, SignJWT} from 'jose';

// Real pinned token verification against ephemeral, loopback-only public JWKS.
// No live identity endpoint, account data or credentials are accessed.
const {publicKey, privateKey} = await generateKeyPair('EdDSA');
const wrongKey = (await generateKeyPair('EdDSA')).privateKey;
const jwk = {...await exportJWK(publicKey), kid:'privacy-export-test', alg:'EdDSA', use:'sig'};
const jwksServer = createServer((req, res) => {
  if (req.url !== '/jwks') {res.writeHead(404); res.end(); return;}
  res.writeHead(200, {'content-type':'application/json'}); res.end(JSON.stringify({keys:[jwk]}));
});
await new Promise((resolve, reject) => {jwksServer.once('error', reject); jwksServer.listen(0, '127.0.0.1', resolve);});
after(() => new Promise(resolve => {jwksServer.closeAllConnections(); jwksServer.close(resolve);}));
const jwksUrl = `http://127.0.0.1:${jwksServer.address().port}/jwks`;
const built = await build({entryPoints:['src/resource-api.ts'], bundle:true, write:false, platform:'node', format:'esm',
  banner:{js:"import {createRequire as __createRequire} from 'node:module';const require=__createRequire(process.cwd()+'/package.json');"}});
const {handleResourceApi} = await import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'));

const origin = 'https://issuer.example.test';
const accountA = '11111111-1111-4111-8111-111111111111';
const accountB = '22222222-2222-4222-8222-222222222222';
const sessionA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const sessionB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const secretValues = ['synthetic-password-hash','synthetic-access-token','synthetic-refresh-token',
  'synthetic-id-token','synthetic-session-token-a','synthetic-session-token-b','198.51.100.41',
  'synthetic-client-secret','synthetic-future-credential'];
const preference = (id) => ({schemaVersion:1,scope:{scope:1,ownerId:id,organizationId:null},themeName:'Professional',
  appearance:'Dark',revision:1,accessibility:{reduceMotion:true,highContrast:false,displayScale:1,
    interfaceFontFamily:null,codeFontFamily:null,futureCredential:'synthetic-future-credential'},
  accent:{mode:2,allAppsColour:null,individualApps:{'app.present':'#FF0000'},privateToken:'synthetic-future-credential'},
  customThemesJson:'[]',futureCredential:'synthetic-future-credential'});

function fixture() {
  const db = new DatabaseSync(':memory:'); db.exec('PRAGMA foreign_keys=ON');
  for (const migration of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql','0005_public_account_policy.sql'])
    db.exec(readFileSync(new URL('../migrations/'+migration, import.meta.url), 'utf8'));
  const created = '2026-01-01T00:00:00.000Z', future = new Date(Date.now()+3600000).toISOString();
  for (const [id,sid,label,token] of [[accountA,sessionA,'Alice','synthetic-session-token-a'],[accountB,sessionB,'Bob','synthetic-session-token-b']]) {
    db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt,username,displayUsername,pronouns,jobTitle,role,profileRevision) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)')
      .run(id,label,label.toLowerCase()+'@example.test',1,created,created,label.toLowerCase(),label,'they/them',label+' private work','admin',4);
    db.prepare('INSERT INTO session(id,userId,token,expiresAt,createdAt,updatedAt,ipAddress,userAgent) VALUES(?,?,?,?,?,?,?,?)')
      .run(sid,id,token,future,created,created,'198.51.100.41','Synthetic browser');
    for (const provider of ['credential','google']) db.prepare('INSERT INTO account(id,accountId,providerId,userId,password,accessToken,refreshToken,idToken,createdAt,updatedAt) VALUES(?,?,?,?,?,?,?,?,?,?)')
      .run(id+'-'+provider,id+'-external',provider,id,'synthetic-password-hash','synthetic-access-token','synthetic-refresh-token','synthetic-id-token',created,created);
    db.prepare('INSERT INTO accountThemePreference VALUES(?,?,?,?)').run(id,1,JSON.stringify(preference(id)),created);
  }
  db.prepare('INSERT INTO oauthClient(id,clientId,clientSecret,userId,redirectUris) VALUES(?,?,?,?,?)')
    .run('private-client','private-client','synthetic-client-secret',accountA,'["https://client.example.test/callback"]');
  const queries = []; let beforeExport;
  const env = {AUTH_BASE_URL:origin, API_RESOURCE:origin, DB:{prepare(sql) {
    let values=[]; return {bind(...args){values=args;return this;},async first(){
      queries.push({sql, values});
      if (sql.startsWith('SELECT u.id')) beforeExport?.();
      return db.prepare(sql).get(...values) ?? null;
    }};
  }}};
  let sessionCalls=0;
  const auth = (id=accountA,sid=sessionA) => ({options:{baseURL:origin,basePath:'/api/auth'},
    $context:Promise.resolve({getPlugin(name){return name==='jwt'?{options:{jwks:{remoteUrl:jwksUrl}}}:{options:{}};}}),
    api:{async getSession(){sessionCalls++;return id ? {user:{id},session:{id:sid}} : null;}}});
  return {db,env,queries,auth,setBeforeExport(fn){beforeExport=fn;},getSessionCalls(){return sessionCalls;},close(){db.close();}};
}
const request = (query='',authorization,method='GET') => new Request(origin+'/api/account/export'+query,
  {method,headers:authorization===undefined?{}:{authorization}});
async function token(claims={},key=privateKey) {
  return new SignJWT({sub:accountA,sid:sessionA,client_id:'private-client',azp:'private-client',scope:'cake:profile:read',...claims})
    .setProtectedHeader({alg:'EdDSA',kid:'privacy-export-test',typ:'at+jwt'})
    .setIssuer(claims.iss ?? origin+'/api/auth').setAudience(claims.aud ?? origin)
    .setIssuedAt().setExpirationTime(claims.exp ?? Math.floor(Date.now()/1000)+300).sign(key);
}

test('two cookie-authenticated accounts receive only their own allowlisted export',async()=>{
  const f=fixture(); try {
    for (const [id,sid,name,other] of [[accountA,sessionA,'Alice','Bob'],[accountB,sessionB,'Bob','Alice']]) {
      const r=await handleResourceApi(request('?accountId='+ (id===accountA?accountB:accountA)+'&userId=other'),f.env,f.auth(id,sid));
      assert.equal(r.status,200); const raw=await r.text(), data=JSON.parse(raw);
      assert.equal(data.schemaVersion,1); assert.ok(Number.isFinite(Date.parse(data.exportedAt)));
      assert.deepEqual(data.account,{id,name,username:name.toLowerCase(),email:name.toLowerCase()+'@example.test',emailVerified:true,createdAt:'2026-01-01T00:00:00.000Z'});
      assert.equal(data.profile.job,name+' private work'); assert.equal(data.themePreference.appearance,'Dark');
      assert.deepEqual(data.linkedSignInProviders,['credential','google']); assert.ok(!raw.includes(other));
      for(const secret of secretValues)assert.ok(!raw.includes(secret),secret);
      assert.deepEqual(Object.keys(data).sort(),['account','exportedAt','linkedSignInProviders','profile','schemaVersion','themePreference']);
      assert.equal(data.sessions,undefined); assert.equal(data.themePreference.futureCredential,undefined);
      assert.equal(data.themePreference.accessibility.futureCredential,undefined); assert.equal(data.themePreference.accent.privateToken,undefined);
      assert.equal(r.headers.get('content-disposition'),'attachment; filename="9to1-account-export.json"');
      assert.equal(r.headers.get('cache-control'),'private, no-store'); assert.equal(r.headers.get('x-content-type-options'),'nosniff');
      assert.equal(r.headers.get('vary'),'Cookie, Authorization');
    }
  } finally {f.close();}
});

test('valid signed bearer uses verified subject and profile scope without cookie fallback',async()=>{
  const f=fixture(); try {
    const r=await handleResourceApi(request('?accountId='+accountB,'Bearer '+await token()),f.env,f.auth(accountB,sessionB));
    assert.equal(r.status,200); assert.equal((await r.json()).account.id,accountA); assert.equal(f.getSessionCalls(),0);
  } finally {f.close();}
});

test('production website bearer cannot bypass current account policy with a valid token',async()=>{
  const f=fixture();try{
    f.db.prepare('INSERT INTO oauthClient(id,clientId,redirectUris) VALUES(?,?,?)').run('public-cake','public-cake','["https://cakemods.com/oauth/callback"]');
    const authorization='Bearer '+await token({client_id:'public-cake',azp:'public-cake'});
    assert.equal((await handleResourceApi(request('',authorization),f.env,f.auth())).status,503);
    f.env.PUBLIC_TERMS_VERSION='fixture-v1';f.env.PUBLIC_PRIVACY_VERSION='fixture-v1';
    assert.equal((await handleResourceApi(request('',authorization),f.env,f.auth())).status,403);
    f.db.prepare('INSERT INTO cake_public_account_policy VALUES(?,?,?,?,?,?)').run(accountA,'16_17','fixture-v1','fixture-v1',Date.now(),Date.now());
    assert.equal((await handleResourceApi(request('',authorization),f.env,f.auth())).status,200);
    f.env.PUBLIC_TERMS_VERSION='fixture-v2';assert.equal((await handleResourceApi(request('',authorization),f.env,f.auth())).status,403);
    assert.equal((await handleResourceApi(request('','Bearer '+await token({client_id:'public-cake',azp:'private-client'})),f.env,f.auth())).status,401);
  }finally{f.close();}
});

test('missing authentication, malformed headers, forged or incorrectly scoped JWTs are denied',async()=>{
  const f=fixture(); try {
    assert.equal((await handleResourceApi(request(),f.env,f.auth(null))).status,401);
    for(const authorization of ['Basic abc','Bearer','Bearer not-a-jwt','Bearer '+await token({},wrongKey),
      'Bearer '+await token({scope:'cake:account:read'}),'Bearer '+await token({aud:'https://other.example.test'}),
      'Bearer '+await token({iss:'https://other.example.test/api/auth'}),'Bearer '+await token({exp:1})]) {
      const r=await handleResourceApi(request('',authorization),f.env,f.auth());
      assert.equal(r.status,401); assert.ok(!(await r.text()).includes('Alice'));
    }
    assert.equal(f.queries.length,0);
  } finally {f.close();}
});

test('revoked, expired, mismatched or noncanonical cookie principals cannot export',async()=>{
  for(const mode of ['revoked','expired','foreign-session','invalid-subject']) {
    const f=fixture(); try {
      if(mode==='revoked')f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA);
      if(mode==='expired')f.db.prepare('UPDATE session SET expiresAt=? WHERE id=?').run('2000-01-01T00:00:00.000Z',sessionA);
      const r=await handleResourceApi(request(),f.env,f.auth(mode==='invalid-subject'?'not-a-canonical-id':accountA,mode==='foreign-session'?sessionB:sessionA));
      assert.equal(r.status,401); assert.ok(!(await r.text()).includes('Alice'));
    } finally {f.close();}
  }
});

test('revocation after preliminary bearer authorization is caught inside export SQL',async()=>{
  const f=fixture(); try {
    f.setBeforeExport(()=>f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA));
    const r=await handleResourceApi(request('','Bearer '+await token()),f.env,f.auth());
    assert.equal(r.status,401); assert.ok(!(await r.text()).includes('Alice'));
  } finally {f.close();}
});

test('optional absent theme and providers export as null and an empty list',async()=>{
  const f=fixture(); try {
    f.db.prepare('DELETE FROM accountThemePreference WHERE accountId=?').run(accountA);
    f.db.prepare('DELETE FROM account WHERE userId=?').run(accountA);
    const r=await handleResourceApi(request(),f.env,f.auth()); assert.equal(r.status,200);
    const data=await r.json(); assert.equal(data.themePreference,null); assert.deepEqual(data.linkedSignInProviders,[]);
  } finally {f.close();}
});

test('unsupported stored theme fails visibly without returning a partial account export',async()=>{
  const f=fixture(); try {
    const future={...preference(accountA),schemaVersion:2};
    f.db.prepare('UPDATE accountThemePreference SET preferenceJson=? WHERE accountId=?').run(JSON.stringify(future),accountA);
    const r=await handleResourceApi(request(),f.env,f.auth()); assert.equal(r.status,409);
    assert.deepEqual(await r.json(),{error:'theme_schema_unsupported'});
  } finally {f.close();}
});

test('export accepts only the exact GET route and performs no database mutations',async()=>{
  const f=fixture(); try {
    const before=f.db.prepare('SELECT total_changes() n').get().n;
    for(const method of ['POST','PATCH','DELETE','HEAD'])assert.equal((await handleResourceApi(request('',undefined,method),f.env,f.auth())).status,404);
    assert.equal((await handleResourceApi(new Request(origin+'/api/account/export/'+accountB),f.env,f.auth())).status,404);
    assert.equal((await handleResourceApi(request(),f.env,f.auth())).status,200);
    assert.equal(f.db.prepare('SELECT total_changes() n').get().n,before);
    assert.ok(f.queries.every(q=>q.sql.startsWith('SELECT')));
  } finally {f.close();}
});
