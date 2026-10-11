import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import path from 'node:path';
import os from 'node:os';
import {supervise} from '../supervisor.mjs';
import {processIdentity} from '../worker-guard.mjs';
test('A duplicate supervisor exits before network requests or dispatch and preserves its existing owner record',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-supervisor-')),config=path.join(root,'config.json'),plan=path.join(root,'plan.json'),powershell=process.platform==='win32'?path.join(process.env.SystemRoot,'System32/WindowsPowerShell/v1.0/powershell.exe'):undefined,identity=await processIdentity(process.pid,powershell);assert.ok(identity);
 await writeFile(config,JSON.stringify({powershell,coordinator_url:'http://127.0.0.1:1/never-contact',state_db:path.join(root,'unused.db')}));await writeFile(plan,JSON.stringify({tasks:[],goal:{}}));const db=new DatabaseSync(path.join(root,'supervisor.db'));db.exec('CREATE TABLE state(key TEXT PRIMARY KEY,value TEXT)');db.prepare('INSERT INTO state VALUES (?,?)').run('supervisor-owner',JSON.stringify(identity));db.close();
 const result=await supervise(config,plan);assert.equal(result.alreadyRunning,true);assert.equal(result.pid,process.pid);const check=new DatabaseSync(path.join(root,'supervisor.db'),{readOnly:true});assert.equal(check.prepare('SELECT COUNT(*) AS count FROM events').get().count,0);assert.deepEqual(JSON.parse(check.prepare("SELECT value FROM state WHERE key='supervisor-owner'").get().value),identity);check.close();
});
