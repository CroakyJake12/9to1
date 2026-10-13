import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile} from 'node:fs/promises';
import {execFile,spawn} from 'node:child_process';
import {promisify} from 'node:util';
import {createServer} from 'node:net';
import {fileURLToPath} from 'node:url';
import os from 'node:os';
import path from 'node:path';
import {ensureService} from '../service-start.mjs';
import {processIdentity,sameProcessBirth} from '../worker-guard.mjs';
const execute=promisify(execFile),here=path.dirname(fileURLToPath(import.meta.url));
const powershell=process.platform==='win32'?path.join(process.env.SystemRoot,'System32/WindowsPowerShell/v1.0/powershell.exe'):null;
async function freePort(){const socket=createServer();await new Promise(r=>socket.listen(0,'127.0.0.1',r));const port=socket.address().port;await new Promise(r=>socket.close(r));return port;}
async function fixture(){const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-start-')),repository=path.join(root,'repo'),tools=path.join(repository,'tools/development-relay'),privateRoot=path.join(root,'private');await mkdir(tools,{recursive:true});await mkdir(privateRoot);const port=await freePort(),configPath=path.join(privateRoot,'config.json');await writeFile(configPath,JSON.stringify({repository,state_db:path.join(privateRoot,'state.db'),dashboard_port:port,powershell}));const program="const fs=require('node:fs'),http=require('node:http');const c=JSON.parse(fs.readFileSync(process.argv[2]));const s=http.createServer((q,r)=>{r.setHeader('content-type','application/json');r.end(JSON.stringify({installed:true,runner_pid:process.pid}));});s.listen(c.dashboard_port,'127.0.0.1');process.on('SIGTERM',()=>s.close(()=>process.exit(0)));";await writeFile(path.join(tools,'runner.mjs'),program.replace("const fs=require('node:fs'),http=require('node:http');","import fs from 'node:fs';import http from 'node:http';"));return {root,tools,port,configPath,program};}
async function cleanup(pid,identity){if(sameProcessBirth(identity,await processIdentity(pid,powershell)))process.kill(pid,'SIGTERM');}

test('Concurrent Windows startup creates one service and later invocations adopt its exact healthy identity',{skip:process.platform!=='win32',timeout:90000},async()=>{
 const f=await fixture();let identity;
 const args=[path.join(here,'../service-start.mjs'),f.configPath,'runner'];
 try{const results=await Promise.all([execute(process.execPath,args,{windowsHide:true,timeout:60000}),execute(process.execPath,args,{windowsHide:true,timeout:60000})]);const states=results.map(r=>JSON.parse(r.stdout));assert.equal(states.filter(s=>s.status==='STARTED').length,1);const started=states.find(s=>s.status==='STARTED');identity=await processIdentity(started.pid,powershell);assert.ok(identity);const again=await ensureService(f.configPath,'runner');assert.equal(again.status,'ALREADY_RUNNING');assert.equal(again.pid,started.pid);const actual=await(await fetch('http://127.0.0.1:'+f.port+'/status')).json();assert.equal(actual.runner_pid,started.pid);
 }finally{if(identity)await cleanup(identity.pid,identity);}
});

test('A foreign process on the configured port is neither adopted, replaced nor stopped',{skip:process.platform!=='win32',timeout:90000},async()=>{
 const f=await fixture(),foreign=path.join(f.root,'foreign.mjs');await writeFile(foreign,f.program.replace("const fs=require('node:fs'),http=require('node:http');","import fs from 'node:fs';import http from 'node:http';"));const child=spawn(process.execPath,[foreign,f.configPath],{windowsHide:true,stdio:'ignore'});await new Promise((r,j)=>{child.once('spawn',r);child.once('error',j);});const identity=await processIdentity(child.pid,powershell);
 try{for(let i=0;i<50;i++){try{await fetch('http://127.0.0.1:'+f.port+'/status');break;}catch{await new Promise(r=>setTimeout(r,100));}}await assert.rejects(()=>ensureService(f.configPath,'runner'),/identity differs/);assert.equal(child.exitCode,null);assert.equal((await(await fetch('http://127.0.0.1:'+f.port+'/status')).json()).runner_pid,child.pid);assert.ok(sameProcessBirth(identity,await processIdentity(child.pid,powershell)));}finally{await cleanup(child.pid,identity);}
});
