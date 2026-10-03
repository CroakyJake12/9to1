import assert from 'node:assert/strict';
import { closeSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { launchFixtureCustodian } from './fixture-launch.mjs';
const folder=mkdtempSync(path.join(os.tmpdir(),'cake-fixture-launch-control-'));
const custodian=new URL('./linux-fixture-custodian.py',import.meta.url).pathname;
const spawnFailure=new Error('isolated synchronous spawn failure');
const closeFailure=new Error('isolated close failure');
const closeAndFail=fd=>{closeSync(fd);throw closeFailure};
assert.throws(()=>launchFixtureCustodian(custodian,['/bin/true'],folder,path.join(folder,'no-child.json'),{
  spawn:()=>{throw spawnFailure},close:closeAndFail,
}),error=>error instanceof AggregateError && error.errors[0]===spawnFailure && error.errors[1]===closeFailure);
const worker=launchFixtureCustodian(custodian,[process.execPath,'-e','setInterval(()=>{},1000)'],folder,
  path.join(folder,'real-child.json'),{close:closeAndFail});
assert.ok(worker.pid,'actual controlled custodian started before injected close failure');
assert.equal(worker.fixtureLaunchError,closeFailure,'owned handle retains exact original setup error');
let receipt='';
for(const stream of [worker.stdout,worker.stderr]) stream.on('data',()=>{});
worker.stdio[3].setEncoding('utf8').on('data',chunk=>{receipt+=chunk});
worker.stdin.on('error',()=>{});
const exited=new Promise((resolve,reject)=>{worker.once('error',reject);worker.once('exit',resolve)});
worker.stdin.end('stop\n');
assert.equal(await exited,0);
const proof=JSON.parse(receipt);
assert.equal(proof.strictReaped,true);assert.equal(proof.originalsDisappeared,true);
assert.deepEqual(JSON.parse(readFileSync(path.join(folder,'real-child.json'),'utf8')),proof);
console.log('PASS: actual spawned handle retained and strictly drained after close error; original spawn+close objects retained');
// Controlled injection closed its real fd before throwing; strict proof exists, so only this owned test folder can be removed.
rmSync(folder,{recursive:true});
