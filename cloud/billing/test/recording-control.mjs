// Actual exclusive-output collision: no monkeypatching of persistence/custody.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtempSync,readdirSync,existsSync,writeFileSync,readFileSync} from 'node:fs';
import {resolve,join} from 'node:path';
const parent=process.argv[2];
if(!parent || !parent.startsWith('/'))throw new Error('Explicit external evidence parent required');
const evidence=mkdtempSync(join(parent,'billing-recording-control-'));
const child=spawn(process.execPath,[resolve('test/run-workerd.mjs'),evidence],{cwd:process.cwd(),stdio:['pipe','pipe','pipe']});
let stdout='',stderr='',collision;
child.stdout.on('data',data=>stdout+=data);
child.stderr.on('data',data=>stderr+=data);
const poll=setInterval(()=>{
 const dirs=readdirSync(evidence).filter(name=>name.startsWith('billing-workerd-'));
 if(dirs.length===1 && !collision){const dir=join(evidence,dirs[0]);if(existsSync(join(dir,'custody.json'))){collision=join(dir,'stdout.log');writeFileSync(collision,'retained-collision-control\n',{flag:'wx',mode:0o600});}}
},10);
let error;
try{
 const exit=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',(code,signal)=>resolve({code,signal}));});
 assert.equal(exit.code,1);assert.equal(exit.signal,null);assert.ok(collision);
 assert.equal(readFileSync(collision,'utf8'),'retained-collision-control\n');
 const receipt=JSON.parse(readFileSync(join(collision,'..','custody.json'),'utf8'));
 assert.equal(receipt.strictReaped,true);assert.equal(receipt.originalsDisappeared,true);
 assert.ok(receipt.reaped.some(row=>row.code===1 && row.status===0));
 assert.equal(stdout.includes('"passed":true'),false,'No PASS marker may precede failed output persistence');
}catch(failure){error=failure;}
finally{
 clearInterval(poll);child.stdin.end();
 writeFileSync(join(evidence,'outer-stdout.log'),stdout,{flag:'wx',mode:0o600});
 writeFileSync(join(evidence,'outer-stderr.log'),stderr,{flag:'wx',mode:0o600});
}
if(error)throw new AggregateError([error],`Recording control failed; originals ${evidence}`);
console.log(JSON.stringify({recordingFailureRefused:true,evidence}));
