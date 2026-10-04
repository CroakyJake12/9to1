import assert from 'node:assert/strict';
import {mkdtemp,readFile,writeFile,chmod,symlink,rm,stat} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {execFileSync} from 'node:child_process';
import {prepare,resourcePrecondition} from './prepare-fixture.mjs';
import {privateFixture,authenticated,runAuthenticated} from './authenticated.mjs';
import {origin,defaultDeploymentVersion} from './validate.mjs';
const receipt={databaseId:'fb094287-efab-4d3c-b9d5-c588a83d6f42',response:{status:200,success:true,result:[{success:true,results:[{identifier:origin}]}]}};
// This synthetic readback is an offline structural control, never provider evidence.
for(const value of [{},{...receipt,databaseId:'foreign'},{...receipt,response:{...receipt.response,result:[]}},{...receipt,response:{...receipt.response,result:[{success:true,results:[{identifier:'https://foreign.example.test'}]}]}}])assert.throws(()=>resourcePrecondition(value));
const owned=await mkdtemp(join(tmpdir(),'cake-https-fixture-control-'));let primary;
try {
  const directory=join(owned,'new');const manifest=await prepare(directory,receipt);assert.deepEqual(manifest.rows,{users:2,credentials:2,clients:1,resourceLinks:1});
  await assert.rejects(prepare(directory,receipt),error=>error.code==='EEXIST');
  const path=join(directory,'fixture.json');const fixture=await privateFixture(path);assert.equal(fixture.accounts.length,2);
  const newVersion='8c60f7a9-9f8a-4e4c-97d3-d806d1507e31';let pinRequests=0,pinJourneys=0;
  for(const deploymentVersion of ['',null,42,newVersion.toUpperCase(),` ${newVersion}`,`${newVersion}\n`,'00000000-0000-0000-0000-000000000000','https://foreign.example.test']) {
    await assert.rejects(authenticated(fixture,{deploymentVersion,request:async()=>{pinRequests++;throw new Error('Unexpected request');}}),/Deployment version/);
    await assert.rejects(runAuthenticated(path,join(owned,'invalid-pin-result.json'),{deploymentVersion,journey:async()=>{pinJourneys++;}}),/Deployment version/);
  }
  assert.equal(pinRequests,0);assert.equal(pinJourneys,0);await assert.rejects(stat(join(owned,'invalid-pin-result.json')),error=>error.code==='ENOENT');
  for(const [label,deploymentVersion,expected] of [['default',undefined,defaultDeploymentVersion],['explicit',newVersion,newVersion]]) {
    const output=join(owned,`pin-${label}-result.json`);
    const result=await runAuthenticated(path,output,{deploymentVersion,journey:async(actual,options)=>{assert.equal(actual.clientId,fixture.clientId);assert.equal(options.deploymentVersion,expected);return {result:'passed',operatorPinnedDeploymentVersion:options.deploymentVersion,qualification:'Offline propagation control only'};}});
    assert.equal(result.operatorPinnedDeploymentVersion,expected);assert.equal(JSON.parse(await readFile(output)).operatorPinnedDeploymentVersion,expected);
  }
  await chmod(path,0o644);await assert.rejects(privateFixture(path),/private bounded/);await chmod(path,0o600);
  const alias=join(owned,'alias');await symlink(path,alias);await assert.rejects(privateFixture(alias),error=>error.code==='ELOOP');
  const existingOutput=join(owned,'existing-result.json');await writeFile(existingOutput,'preserved');let journeys=0;await assert.rejects(runAuthenticated(path,existingOutput,{journey:async()=>{journeys++;}}),error=>error.code==='EEXIST');assert.equal(journeys,0);assert.equal(await readFile(existingOutput,'utf8'),'preserved');
  const journeyFailure=new Error('Controlled original journey failure');await assert.rejects(runAuthenticated(path,join(owned,'failed-result.json'),{journey:async()=>{throw journeyFailure;}}),error=>error===journeyFailure);
  const savedFetch=globalThis.fetch,savedWindow=globalThis.window;const original=new Error('Controlled offline HTTPS transport loss');
  await assert.rejects(authenticated(fixture,{request:async()=>{throw original;}}),error=>error===original);
  assert.equal(globalThis.fetch,savedFetch);assert.equal(globalThis.window,savedWindow);
  const bad=join(directory,'foreign.json');await writeFile(bad,JSON.stringify({...fixture,origin:'https://foreign.example.test'}),{mode:0o600});await assert.rejects(privateFixture(bad));
  const script=`import json,sqlite3,pathlib,sys
root=pathlib.Path.cwd();folder=pathlib.Path(sys.argv[1]);db=sqlite3.connect(':memory:');db.execute('PRAGMA foreign_keys=ON')
for p in sorted((root/'migrations').glob('*.sql')):db.executescript(p.read_text())
origin='https://cake-id-release-validation.jcbailey008.workers.dev'
db.execute('INSERT INTO oauthResource(id,identifier,name) VALUES (?,?,?)',('offline-resource-id',origin,'Offline actual schema fixture'));db.commit()
batch=json.loads((folder/'seed-query.json').read_text())['batch']
with db:
 for q in batch:db.execute(q['sql'],q['params'])
sql_db=sqlite3.connect(':memory:');sql_db.execute('PRAGMA foreign_keys=ON')
for p in sorted((root/'migrations').glob('*.sql')):sql_db.executescript(p.read_text())
sql_db.execute('INSERT INTO oauthResource(id,identifier,name) VALUES (?,?,?)',('offline-resource-id',origin,'Offline actual schema fixture'));sql_db.commit();sql_db.executescript((folder/'seed.sql').read_text())
for table in ['user','account','oauthClient','oauthClientResource']:
 assert db.execute('SELECT * FROM '+table+' ORDER BY id').fetchall()==sql_db.execute('SELECT * FROM '+table+' ORDER BY id').fetchall(),table
assert db.execute('SELECT count(*) FROM user').fetchone()[0]==2
assert db.execute('SELECT count(*) FROM account WHERE providerId="credential"').fetchone()[0]==2
assert db.execute('SELECT count(*) FROM user WHERE role IS NOT NULL').fetchone()[0]==0
assert db.execute('SELECT count(*) FROM oauthClient WHERE requirePKCE=1 AND skipConsent=0 AND clientSecret IS NULL AND tokenEndpointAuthMethod="none"').fetchone()[0]==1
assert db.execute('SELECT count(*) FROM oauthClientResource WHERE resourceId=?',(origin,)).fetchone()[0]==1
try:
 with db:
  for q in batch:db.execute(q['sql'],q['params'])
except sqlite3.IntegrityError:pass
else:raise AssertionError('Duplicate seed accepted')
assert db.execute('SELECT count(*) FROM user').fetchone()[0]==2
assert db.execute('SELECT ownerUserId FROM cake_reserved_usernames WHERE username="croakyjake"').fetchone()[0] is None
db.executescript((folder/'rollback.sql').read_text())
assert db.execute('SELECT count(*) FROM user').fetchone()[0]==0
assert db.execute('SELECT count(*) FROM account').fetchone()[0]==0
assert db.execute('SELECT count(*) FROM oauthClient').fetchone()[0]==0
assert db.execute('SELECT count(*) FROM oauthClientResource').fetchone()[0]==0
assert db.execute('SELECT count(*) FROM oauthResource').fetchone()[0]==1
assert db.execute('SELECT ownerUserId FROM cake_reserved_usernames WHERE username="croakyjake"').fetchone()[0] is None
print('PASS: actual pinned SQLite schema seed/duplicate/rollback and reserved owner controls; local only')`;
  process.stdout.write(execFileSync('python3',['-c',script,directory],{encoding:'utf8',timeout:15000}));
  console.log('PASS: offline real library password hashing, private custody and driver transport/authority refusal controls; no remote seeding/HTTPS acceptance');
}catch(error){primary=error;}
let cleanup;try{await rm(owned,{recursive:true});}catch(error){cleanup=error;}
if(primary&&cleanup)throw new AggregateError([primary,cleanup],'Fixture control and cleanup failed');if(primary)throw primary;if(cleanup)throw cleanup;
