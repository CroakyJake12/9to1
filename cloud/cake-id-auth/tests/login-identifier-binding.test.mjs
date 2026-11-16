import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';

const compiled = await build({entryPoints:['src/limiter.ts'],bundle:true,write:false,platform:'node',format:'esm'});
const {readLoginIdentifier,checkLoginLimit} = await import('data:text/javascript;base64,' + Buffer.from(compiled.outputFiles[0].text).toString('base64'));
const origin = 'https://identity.example.test';
const emailRoute = '/api/auth/sign-in/email';
const usernameRoute = '/api/auth/sign-in/username';
const initialTime = 1_900_000_000;

function fixture(t) {
  const db = new DatabaseSync(':memory:');
  db.exec(readFileSync('migrations/0000_cake_guardrails.sql','utf8'));
  const DB = {prepare(sql) {return {bind(...values) {return {
    first: async () => db.prepare(sql).get(...values) ?? null,
    run: async () => db.prepare(sql).run(...values),
  };}};}};
  let now = initialTime;
  t.mock.method(Date,'now',() => now * 1000);
  t.after(() => db.close());
  return {
    db,
    env: {DB,LOGIN_LIMITER_KEY:'synthetic-limiter-key-for-local-sqlite-test-only',LOGIN_MAX_ATTEMPTS:'8',LOGIN_WINDOW_SECONDS:'900',LOGIN_LOCK_SECONDS:'1800'},
    time: value => {now = value;},
    rows: () => db.prepare('SELECT * FROM cake_login_attempts ORDER BY keyHash').all(),
  };
}

function request(path, body, ip = '203.0.113.10') {
  return new Request(origin + path,{method:'POST',headers:{'content-type':'application/json','CF-Connecting-IP':ip},body:JSON.stringify(body)});
}

async function attempt(f,path,body,ip) {
  const req = request(path,body,ip);
  const identifier = await readLoginIdentifier(req);
  assert.deepEqual(await req.json(),body,'identifier inspection must not consume or rewrite the provider request body');
  return checkLoginLimit(f.env,req,identifier);
}

test('username attempts cannot rotate the limiter bucket with an extraneous email',async t => {
  const f = fixture(t), results = [];
  for(let i = 0; i < 9; i++) results.push(await attempt(f,usernameRoute,{username:' Same_User ',email:`unused-${i}@example.test`,password:'wrong-password'}));
  assert.deepEqual(results.map(result => result.allowed),[true,true,true,true,true,true,true,true,false]);
  assert.equal(results[8].retryAfterSeconds,1800);
  assert.equal(f.rows().length,1);
  assert.equal(f.rows()[0].attempts,8);
  assert.equal(f.rows()[0].lockedUntil,initialTime + 1800);
});

test('legitimate username-only control retains the existing eight-attempt allowance',async t => {
  const f = fixture(t);
  for(let i = 0; i < 8; i++) assert.equal((await attempt(f,usernameRoute,{username:'same_user',password:'wrong-password'})).allowed,true);
  assert.equal((await attempt(f,usernameRoute,{username:'same_user',password:'wrong-password'})).allowed,false);
  assert.equal(f.rows().length,1);
});

test('email sign-in ignores an extraneous rotating username and keeps email normalization',async t => {
  const f = fixture(t), results = [];
  for(let i = 0; i < 9; i++) results.push(await attempt(f,emailRoute,{email:i % 2 ? ' Person@Example.Test ' : 'person@example.test',username:`unused_${i}`}));
  assert.deepEqual(results.map(result => result.allowed),[true,true,true,true,true,true,true,true,false]);
  assert.equal(f.rows().length,1);
});

test('missing, wrong-type, blank and malformed expected fields use the unknown source bucket',async t => {
  const f = fixture(t);
  const bodies = [
    {email:'ignored1@example.test'},
    {username:null,email:'ignored2@example.test'},
    {username:42,email:'ignored3@example.test'},
    {username:[],email:'ignored4@example.test'},
    {username:{value:'same_user'},email:'ignored5@example.test'},
    {username:'',email:'ignored6@example.test'},
    {username:' \t\r\n ',email:'ignored7@example.test'},
    null,
    ['same_user'],
  ];
  for(const body of bodies) {
    const req = request(usernameRoute,body);
    assert.equal(await readLoginIdentifier(req),'unknown:');
    await checkLoginLimit(f.env,req,await readLoginIdentifier(req));
    assert.deepEqual(await req.json(),body);
  }
  assert.equal(f.rows().length,1);
  assert.equal(f.rows()[0].lockedUntil,initialTime + 1800);
  for(const body of [{username:'ignored_user'},{email:null,username:'ignored_user'},{email:[],username:'ignored_user'},{email:'   ',username:'ignored_user'},false]) {
    assert.equal(await readLoginIdentifier(request(emailRoute,body)),'unknown:');
  }
  const malformed = new Request(origin + usernameRoute,{method:'POST',body:'{"username":'});
  assert.equal(await readLoginIdentifier(malformed),'unknown:');
  assert.equal(await malformed.text(),'{"username":');
});

test('identifier selection requires the exact authentication route and retains prefixes and length bound',async () => {
  assert.equal(await readLoginIdentifier(request(usernameRoute + '?email=ignored',{username:' Mixed_CASE ',email:'ignored@example.test'})),'username:mixed_case');
  assert.equal(await readLoginIdentifier(request(emailRoute,{email:' Mixed@Example.Test ',username:'ignored'})),'email:mixed@example.test');
  assert.equal(await readLoginIdentifier(request(emailRoute,{email:'A'.repeat(321)})),'email:' + 'a'.repeat(320));
  for(const path of ['/sign-in/username',usernameRoute + '/extra',usernameRoute + '/',emailRoute.toUpperCase(),'/api/auth/sign-up/email']) {
    assert.equal(await readLoginIdentifier(request(path,{email:'person@example.test',username:'same_user'})),'unknown:');
  }
});

test('different users and source IPs keep independent HMAC buckets without plaintext identifiers',async t => {
  const f = fixture(t);
  for(let i = 0; i < 9; i++) await attempt(f,usernameRoute,{username:'first_user'},'203.0.113.10');
  assert.equal((await attempt(f,usernameRoute,{username:'second_user'},'203.0.113.10')).allowed,true);
  assert.equal((await attempt(f,usernameRoute,{username:'first_user'},'203.0.113.11')).allowed,true);
  assert.equal(f.rows().length,3);
  for(const row of f.rows()) {
    assert.match(row.keyHash,/^[a-f0-9]{64}$/);
    assert.ok(!JSON.stringify(row).includes('first_user'));
    assert.ok(!JSON.stringify(row).includes('203.0.113'));
  }
});

test('lock duration, no-write locked retries and window reset remain unchanged',async t => {
  const f = fixture(t);
  for(let i = 0; i < 9; i++) await attempt(f,usernameRoute,{username:'same_user'});
  const locked = f.rows()[0];
  f.time(initialTime + 1);
  const retry = await attempt(f,usernameRoute,{username:' SAME_USER '});
  assert.deepEqual(retry,{allowed:false,retryAfterSeconds:1799});
  assert.deepEqual(f.rows()[0],locked);
  f.time(initialTime + 1800);
  assert.deepEqual(await attempt(f,usernameRoute,{username:'same_user'}),{allowed:true,retryAfterSeconds:0});
  assert.equal(f.rows()[0].attempts,1);
  assert.equal(f.rows()[0].lockedUntil,0);
  assert.equal(f.rows()[0].windowStartedAt,initialTime + 1800);
});
