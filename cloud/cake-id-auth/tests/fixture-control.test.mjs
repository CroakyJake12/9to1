import assert from 'node:assert/strict';
import { installFixtureRelease, finishFixtureRelease } from './fixture-control.mjs';
const signals=['SIGINT','SIGTERM','SIGHUP'];
const before=signals.map(signal=>process.listenerCount(signal));
const inputBefore=process.stdin.listenerCount('data');
let released=0;
const dispose=installFixtureRelease(()=>{released++});
assert.deepEqual(signals.map(signal=>process.listenerCount(signal)),before.map(n=>n+1));
dispose();
assert.deepEqual(signals.map(signal=>process.listenerCount(signal)),before);
assert.equal(process.stdin.listenerCount('data'),inputBefore);
assert.equal(process.stdin.readableFlowing,false);
assert.equal(released,1,'closing own input releases once');
const stopFailure=new Error('strict owned drain failed');
const disposalFailure=new Error('independent listener disposal failed');
await assert.rejects(finishFixtureRelease(async()=>{throw stopFailure},()=>{throw disposalFailure}),
  error=>error instanceof AggregateError && error.errors[0]===stopFailure && error.errors[1]===disposalFailure);
let disposed=false;
await assert.rejects(finishFixtureRelease(async()=>{throw stopFailure},()=>{disposed=true}),
  error=>error.errors[0]===stopFailure);
assert.equal(disposed,true,'disposal runs even after failed strict drain');
console.log('PASS: exact listener/input disposal and original drain+disposal Error identities preserved');
