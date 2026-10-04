import assert from 'node:assert/strict';
import {mkdtempSync,readFileSync,writeFileSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {launchFixtureCustodian} from '../../cake-id-auth/tests/fixture-launch.mjs';
import {installFixtureRelease,finishFixtureRelease} from '../../cake-id-auth/tests/fixture-control.mjs';
const evidence=process.argv[2];
const negative=process.argv[3];
if(negative && negative!=='--negative-control')throw new Error('Unknown fixture control');
if (!evidence || !evidence.startsWith('/')) throw new Error('Explicit external evidence parent required');
const dir=mkdtempSync(join(evidence,'billing-workerd-'));
const journal=join(dir,'custody.json');
const worker=launchFixtureCustodian(resolve('../cake-id-auth/tests/linux-fixture-custodian.py'),[process.execPath,resolve('test/workerd-d1.mjs'),join(dir,'d1'),...(negative?[negative]:[])],process.cwd(),journal);
let output='',errors='',result,stopPromise;
let exitResolve;
const exited=new Promise(resolve=>{exitResolve=resolve;});
worker.once('exit',(code,signal)=>exitResolve({code,signal}));
worker.once('error',error=>exitResolve({error}));
const stop=()=>stopPromise??=(async()=>{worker.stdin.end('stop\n');return exited;})();
const dispose=installFixtureRelease(stop);
worker.stdout.on('data',chunk=>{
 output+=chunk;
 const line=output.split('\n').find(line=>line.startsWith('BILLING_WORKER_RESULT='));
 if (line && !result) {
  try {result=JSON.parse(line.slice('BILLING_WORKER_RESULT='.length));} catch {}
  if(result)setTimeout(stop,10000); // Permit normal child exit; kernel receipt remains authoritative.
 }
});
worker.stderr.on('data',chunk=>{errors+=chunk;});
worker.stdio[3].resume();
const timeout=setTimeout(stop,120000);
let primary;
try {
 if(worker.fixtureLaunchError)throw worker.fixtureLaunchError;
 const exit=await exited;
 assert.equal(exit.code,0);assert.equal(exit.signal,null);
 assert.equal(result?.passed,true);
 const receipt=JSON.parse(readFileSync(journal,'utf8'));
 assert.equal(receipt.strictReaped,true);assert.equal(receipt.originalsDisappeared,true);
 assert.ok(receipt.reaped.length>=1);
 const original=receipt.reaped.find(row=>row.pid===result.pid);
 assert.ok(original && original.code===1 && original.status===0,'Actual workload must exit normally; strict receipt covers every descendant');
 console.log(JSON.stringify({passed:true,checks:result.checks,evidence:dir,strictReaped:true}));
} catch(error){primary=error;}
let cleanup;
try {await finishFixtureRelease(stop,dispose);}catch(error){cleanup=error;}
clearTimeout(timeout);
const recording=[];
for(const [name,bytes]of [['stdout.log',output],['stderr.log',errors]]) {
 try{writeFileSync(join(dir,name),bytes,{flag:'wx',mode:0o600});}catch(error){recording.push(error);}
}
if(primary||cleanup||recording.length)throw new AggregateError([primary,cleanup,...recording].filter(Boolean),`Billing fixture refused; evidence retained ${dir}`);
