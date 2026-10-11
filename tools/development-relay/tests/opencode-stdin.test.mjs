import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,mkdir} from 'node:fs/promises';
import {createServer} from 'node:http';
import {generateKeyPairSync} from 'node:crypto';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import path from 'node:path';
import os from 'node:os';
import {fileURLToPath} from 'node:url';
test('Windows OpenCode wrapper transmits quoted diagnostics, option-like text and Unicode through stdin', {skip:process.platform!=='win32'?'Windows PowerShell transport test':false},async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-stdin-')),tree=path.join(root,'owned');await mkdir(tree);const pair=generateKeyPairSync('rsa',{modulusLength:2048}),key=path.join(root,'fixture.pem');await writeFile(key,pair.privateKey.export({type:'pkcs8',format:'pem'}));
 const server=createServer((req,res)=>{res.setHeader('content-type','application/json');res.end(JSON.stringify({state:{presence:{home:true,paused:false,healthy:true,seen_at:Date.now()},lease:{worker:'local',fence:12,expires_at:Date.now()+60000},checkpoint_requested:false,tasks:[{id:'fixture',owner:'local',fence:12,status:'WORKING'}]}}));});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 const weekdays=Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}])),provider=path.join(root,'provider.json'),config=path.join(root,'config.json');await writeFile(provider,JSON.stringify({permission:{bash:'deny',edit:'deny'}}));await writeFile(config,JSON.stringify({actor:'local',home:true,paused:false,coordinator_url:'http://127.0.0.1:'+server.address().port,signing_key:key,opencode_config:provider,opencode:process.execPath,schedule:{timezone:'Europe/London',weekdays}}));
 await writeFile(path.join(tree,'run'),"let data='';process.stdin.setEncoding('utf8');process.stdin.on('data',c=>data+=c);process.stdin.on('end',()=>console.log(JSON.stringify({data,args:process.argv.slice(2)})));");
 const prompt='Fix the real test. JSON: {"args":["dotnet","--verbosity","minimal"]}\nCUI → Dulche; preserve résumé characters. '+('Quoted "source" --not-a-cli-flag\n'.repeat(150));
 try{const powershell=path.join(process.env.SystemRoot,'System32/WindowsPowerShell/v1.0/powershell.exe'),wrapper=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../Run-OpenCode.ps1'),result=await promisify(execFile)(powershell,['-NoProfile','-File',wrapper,'-Config',config,'-Worktree',tree,'-Fence','12','-TaskId','fixture','-OwnedPaths','["owned/"]','-SessionId','ses_fixture123','-Prompt',prompt],{maxBuffer:100000,windowsHide:true});const data=JSON.parse(result.stdout.trim().split(/\r?\n/).at(-1));assert.equal(data.data.trimEnd(),prompt.trimEnd());assert.ok(data.args.includes('--session'));assert.ok(data.args.includes('ses_fixture123'));assert.equal(data.args.includes('--not-a-cli-flag'),false);assert.equal(data.args.includes('--verbosity'),false);}
 finally{await new Promise(r=>server.close(r));}
});
