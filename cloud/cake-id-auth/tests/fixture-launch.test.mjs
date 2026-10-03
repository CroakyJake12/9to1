import assert from 'node:assert/strict';
import { closeSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { launchFixtureCustodian } from './fixture-launch.mjs';
import { finishIntegrationCleanup } from './integration-lifecycle.mjs';
const folder=mkdtempSync(path.join(os.tmpdir(),'cake-fixture-launch-control-'));
const custodian=new URL('./linux-fixture-custodian.py',import.meta.url).pathname;
const spawnFailure=new Error('isolated synchronous spawn failure');
const closeFailure=new Error('isolated close failure');
const closeAndFail=fd=>{closeSync(fd);throw closeFailure};
let worker, exited, primaryFailure, receipt='';
try {
  assert.throws(()=>launchFixtureCustodian(custodian,['/bin/true'],folder,path.join(folder,'no-child.json'),{
    spawn:()=>{throw spawnFailure},close:closeAndFail,
  }),error=>error instanceof AggregateError && error.errors[0]===spawnFailure && error.errors[1]===closeFailure &&
    error.fixtureJournalCloseError===closeFailure);
  worker=launchFixtureCustodian(custodian,[process.execPath,'-e','setInterval(()=>{},1000)'],folder,
    path.join(folder,'real-child.json'),{close:closeAndFail});
  // Install original-child control before assertions: their failure must still stop/reap this actual child.
  for(const stream of [worker.stdout,worker.stderr]) stream.on('data',()=>{});
  worker.stdio[3].setEncoding('utf8').on('data',chunk=>{receipt+=chunk});
  worker.stdin.on('error',()=>{});
  exited=new Promise((resolve,reject)=>{worker.once('error',reject);worker.once('exit',resolve)});
  assert.ok(worker.pid,'actual controlled custodian started before injected close failure');
  assert.equal(worker.fixtureLaunchError,closeFailure,'owned handle retains exact original setup error');
} catch(error) { primaryFailure=error; }
finally {
  await finishIntegrationCleanup(primaryFailure,async()=>{
    if(worker) {
      worker.stdin.end('stop\n');
      const timer=setTimeout(()=>{},15000);timer.unref();
      try { assert.equal(await exited,0); } finally { clearTimeout(timer); }
      const proof=JSON.parse(receipt);
      assert.equal(proof.strictReaped,true);assert.equal(proof.originalsDisappeared,true);
      assert.deepEqual(JSON.parse(readFileSync(path.join(folder,'real-child.json'),'utf8')),proof);
      // Injection actually closed its known fd before throwing; strict proof exists for this owned test directory only.
      rmSync(folder,{recursive:true});
    }
  });
}
if(primaryFailure) throw primaryFailure;
console.log('PASS: actual spawned handle retained and strictly drained after close error; original spawn+close objects retained');
