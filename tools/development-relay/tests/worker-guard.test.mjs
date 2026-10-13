import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,readFile,writeFile,readdir} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {createServer} from 'node:http';
import {generateKeyPairSync} from 'node:crypto';
import {fileURLToPath,pathToFileURL} from 'node:url';
import path from 'node:path';
import os from 'node:os';
import {git,sha} from '../git.mjs';
import {processIdentity,sameProcess,sameProcessBirth} from '../worker-guard.mjs';
import {LocalRunner} from '../runner.mjs';
const here=path.dirname(fileURLToPath(import.meta.url));
const powershell=process.platform==='win32'?path.join(process.env.SystemRoot,'System32/WindowsPowerShell/v1.0/powershell.exe'):undefined;
async function until(read,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){try{const value=await read();if(value)return value;}catch(e){if(e.code!=='ENOENT')throw e;}await new Promise(r=>setTimeout(r,100));}throw new Error('Controlled worker observation timed out');}
async function fixture(timeout=60000){
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-guard-')),repo=path.join(root,'repo');await mkdir(repo);await git(repo,['init']);await git(repo,['config','user.name','Relay Temporary Test']);await git(repo,['config','user.email','relay-test@example.invalid']);await writeFile(path.join(repo,'owned.txt'),'baseline\n');await git(repo,['add','owned.txt']);await git(repo,['commit','-m','Guard fixture']);
 const pair=generateKeyPairSync('rsa',{modulusLength:2048}),key=path.join(root,'fixture.pem');await writeFile(key,pair.privateKey.export({type:'pkcs8',format:'pem'}));
 let state={lease:{worker:'local',fence:1,expires_at:Date.now()+90000},tasks:[{id:'fixture',owner:'local',fence:1,status:'WORKING'}]};
 const server=createServer((req,res)=>{req.resume();res.setHeader('content-type','application/json');res.end(JSON.stringify({state}));});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 const config={state_db:path.join(root,'state.db'),logs:path.join(root,'logs'),checkpoints:path.join(root,'checkpoints'),actor:'local',signing_key:key,powershell,coordinator_url:'http://127.0.0.1:'+server.address().port};
 const configPath=path.join(root,'config.json');await writeFile(configPath,JSON.stringify(config));
 const ready=path.join(root,'ready.json'),fixtureFile=path.join(root,'parent.mjs');
 const script="import {readFile,writeFile} from 'node:fs/promises';import {LocalRunner} from "+JSON.stringify(pathToFileURL(path.join(here,'../runner.mjs')).href)+";const c=JSON.parse(await readFile("+JSON.stringify(configPath)+",'utf8'));const r=new LocalRunner(c);r.active={root:"+JSON.stringify(repo)+",base:"+JSON.stringify(await sha(repo))+",branch:'fixture',task_id:'fixture',fence:1};r.event=(event,value)=>{if(event==='INDEPENDENT_WORKER_GUARD_READY')void writeFile("+JSON.stringify(ready)+",JSON.stringify(value));};const result=await r.command(process.execPath,['-e',"+JSON.stringify("const fs=require('node:fs');setInterval(()=>fs.appendFileSync('owned.txt','tick\\n'),100);")+"],"+JSON.stringify(repo)+","+JSON.stringify(timeout)+",'strata-task');r.db.close();process.exitCode=result.code===0?0:2;";
 await writeFile(fixtureFile,script);
 const parent=spawn(process.execPath,[fixtureFile],{cwd:root,windowsHide:true,stdio:'ignore'});await until(async()=>JSON.parse(await readFile(ready,'utf8')));
 const receipt=()=>until(async()=>{const names=await readdir(config.logs);const name=names.find(n=>n.endsWith('-guard-receipt.json'));return name?JSON.parse(await readFile(path.join(config.logs,name),'utf8')):null;});
 return {root,repo,parent,ready,receipt,config,setState:s=>state=s,cleanup:async()=>{if(parent.exitCode===null&&parent.signalCode===null){parent.kill('SIGTERM');await until(()=>parent.exitCode!==null||parent.signalCode!==null);}await new Promise(r=>server.close(r));}};
}
test('Parent exit stops the owned writer and a fresh runner preserves its final source while retaining the recovery gate',async()=>{
 const f=await fixture();try{const owned=JSON.parse(await readFile(f.ready,'utf8'));const identity=await processIdentity(owned.child_pid,powershell);assert.equal(identity.parent,f.parent.pid);f.parent.kill('SIGTERM');await until(async()=>!sameProcessBirth(identity,await processIdentity(owned.child_pid,powershell)));const recovered=new LocalRunner(f.config);let checkpoint;try{assert.ok(recovered.recovery);checkpoint=await recovered.recoverInterruptedWorker();assert.ok(checkpoint);assert.ok(recovered.recovery);assert.equal(recovered.draining,true);}finally{recovered.db.close();}const bytes=await readFile(path.join(f.repo,'owned.txt'),'utf8');assert.match(bytes,/tick/);assert.equal(await readFile(path.join(checkpoint.directory,'source/owned.txt'),'utf8'),bytes);await new Promise(r=>setTimeout(r,300));assert.equal(await readFile(path.join(f.repo,'owned.txt'),'utf8'),bytes);}finally{await f.cleanup();}
});
test('Independent polling stops a live parent writer after the coordinator changes its lease fence',async()=>{
 const f=await fixture();try{f.setState({lease:{worker:'cloud',fence:2,expires_at:Date.now()+90000},tasks:[]});const receipt=await f.receipt();assert.equal(receipt.killed,true);assert.match(receipt.reason,/lease lost/);assert.equal(receipt.error,null);assert.ok(receipt.checkpoint);}finally{await f.cleanup();}
});
test('PID reuse or changed ancestry cannot match an owned process identity',()=>{const expected={pid:123,parent:99,created:'first'};assert.equal(sameProcess(expected,expected),true);assert.equal(sameProcess(expected,{...expected,created:'second'}),false);assert.equal(sameProcess(expected,{...expected,parent:98}),false);assert.equal(sameProcess(expected,null),false);});
test('Original process birth survives reparenting, while PID reuse and a stopped zombie do not',()=>{const expected={pid:123,parent:99,created:'first'};assert.equal(sameProcessBirth(expected,{...expected,parent:1}),true);assert.equal(sameProcessBirth(expected,{...expected,parent:1,created:'replacement'}),false);assert.equal(sameProcessBirth(expected,{...expected,state:'Z'}),false);assert.equal(sameProcessBirth(expected,null),false);});
test('Continuous development has no deadline and an operator checkpoint still stops and preserves its writer',async()=>{
 const f=await fixture(null);try{const plans=(await readdir(f.config.logs)).filter(n=>n.endsWith('-guard-plan.json'));assert.equal(JSON.parse(await readFile(path.join(f.config.logs,plans[0]),'utf8')).deadline,null);const before=await readFile(path.join(f.repo,'owned.txt'),'utf8');await new Promise(r=>setTimeout(r,1200));assert.ok((await readFile(path.join(f.repo,'owned.txt'),'utf8')).length>before.length);assert.equal(f.parent.exitCode,null);f.setState({lease:{worker:'local',fence:1,expires_at:Date.now()+90000},tasks:[{id:'fixture',owner:'local',fence:1,status:'WORKING'}],checkpoint_requested:true});const result=await f.receipt();assert.equal(result.killed,true);assert.match(result.reason,/checkpoint/);assert.ok(result.checkpoint);assert.equal(result.error,null);}finally{await f.cleanup();}
});
