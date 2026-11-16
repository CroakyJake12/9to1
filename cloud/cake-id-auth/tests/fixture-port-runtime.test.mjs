import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { spawn } from 'node:child_process';
const root = new URL('../', import.meta.url);
const vars = new URL('.dev.vars', root);
const state = new URL('.local-run', root);
assert.equal(existsSync(vars), false);
assert.equal(existsSync(state), false);
async function run(port) {
  const child=spawn(process.execPath,['tests/browser-fixture.mjs'], {cwd:root,
    env:{...process.env,CAKE_BROWSER_FIXTURE_PORT:port},windowsHide:true,stdio:['ignore','pipe','pipe']});
  let log='';for(const pipe of [child.stdout,child.stderr]) pipe.on('data',data=>{log+=data});
  const code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve)});
  assert.notEqual(code,0);assert.equal(existsSync(vars),false);assert.equal(existsSync(state),false);
  return log;
}
assert.match(await run('http://127.0.0.1:8799'),/Fixture port/);
let listener, busyPort;
do {
  listener=createServer();
  await new Promise((resolve,reject)=>{listener.once('error',reject);listener.listen(0,'127.0.0.1',resolve)});
  busyPort=listener.address().port;
  if(busyPort===5096) await new Promise(resolve=>listener.close(resolve));
} while(busyPort===5096);
try{assert.match(await run(String(busyPort)),/EADDRINUSE/)}finally{await new Promise(resolve=>listener.close(resolve))}
console.log(`PASS: invalid port and dynamically discovered busy loopback port ${busyPort} refused before state/secrets; owned listener closed`);
