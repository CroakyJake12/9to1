import {DatabaseSync} from 'node:sqlite';
import {readFile,mkdir,open,lstat} from 'node:fs/promises';
import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomUUID} from 'node:crypto';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {processIdentity,sameProcessBirth} from './worker-guard.mjs';
const execute=promisify(execFile),roles=new Set(['runner','supervisor','dashboard']);
const pause=ms=>new Promise(r=>setTimeout(r,ms));
const tokens=value=>[...value.matchAll(/"([^"\r\n]*)"|([^\s"]+)/g)].map(m=>m[1]??m[2]);
function sameArgument(a,b){return a.replaceAll('/','\\').toLowerCase()===b.replaceAll('/','\\').toLowerCase();}
async function inspect(config,{pid,port}={}){
  const selector=port?`$connection=Get-NetTCPConnection -State Listen -LocalAddress '127.0.0.1' -LocalPort ${port} -ErrorAction SilentlyContinue | Select-Object -First 1; $ownedPid=if($connection){[int]$connection.OwningProcess}else{0};`:`$ownedPid=${pid};`;
  const script=selector+"if($ownedPid){$p=Get-CimInstance Win32_Process -Filter ('ProcessId='+$ownedPid);if($p){[pscustomobject]@{pid=[int]$p.ProcessId;parent=[int]$p.ParentProcessId;created=$p.CreationDate.ToUniversalTime().ToString('o');executable=$p.ExecutablePath;command=$p.CommandLine}|ConvertTo-Json -Compress}}";
  const result=await execute(config.powershell,['-NoProfile','-Command',script],{windowsHide:true,timeout:10000,maxBuffer:16384});
  return result.stdout.trim()?JSON.parse(result.stdout):null;
}
async function specification(configPath,role,planPath){
  if(process.platform!=='win32')throw new Error('This startup adapter requires the Windows task host');
  if(!roles.has(role))throw new Error('Known relay service role required');
  const config=JSON.parse(await readFile(configPath,'utf8')),root=path.dirname(configPath),tools=path.join(config.repository,'tools/development-relay');
  if(!path.isAbsolute(config.repository)||!path.isAbsolute(config.state_db)||path.dirname(config.state_db).toLowerCase()!==root.toLowerCase()||!config.powershell)throw new Error('Private service configuration differs');
  if(!Number.isSafeInteger(config.dashboard_port)||config.dashboard_port<1024||config.dashboard_port>65535)throw new Error('Valid private backend port required');
  const file=path.join(tools,role==='dashboard'?'dashboard-server.mjs':role+'.mjs');
  if(!(await lstat(file)).isFile()||(await lstat(file)).isSymbolicLink())throw new Error('Known regular service script required');
  const port=role==='runner'?config.dashboard_port:role==='dashboard'?(config.display_port??8795):null;
  if(port!==null&&(!Number.isSafeInteger(port)||port<1024||port>65535))throw new Error('Valid loopback service port required');
  if(role==='dashboard'&&port===config.dashboard_port)throw new Error('Display and backend require distinct ports');
  const args=role==='dashboard'?[file,String(port),String(config.dashboard_port),root]:[file,configPath];
  if(role==='supervisor'){planPath=path.resolve(planPath??path.join(root,'autonomous-worker-plan.private.json'));if(path.dirname(planPath).toLowerCase()!==root.toLowerCase()||(await lstat(planPath)).isSymbolicLink())throw new Error('Known private supervisor plan required');args.push(planPath);}
  return {config,root,role,port,args};
}
async function probe(spec){
  let actual;
  if(spec.port)actual=await inspect(spec.config,{port:spec.port});
  else{
    let db;try{db=new DatabaseSync(path.join(spec.root,'supervisor.db'),{readOnly:true});const row=db.prepare("SELECT value FROM state WHERE key='supervisor-owner'").get();if(row){const expected=JSON.parse(row.value),observed=await inspect(spec.config,{pid:expected.pid});if(sameProcessBirth(expected,observed))actual=observed;}}catch(e){if(!/unable to open|no such table/.test(e.message))throw e;}finally{db?.close();}
  }
  if(!actual)return null;
  const command=tokens(actual.command??'');
  if(!sameArgument(actual.executable??'',process.execPath)||command.length!==spec.args.length+1||!sameArgument(command[0],process.execPath)||spec.args.some((arg,i)=>!sameArgument(command[i+1],arg)))throw new Error('Service identity differs; occupied endpoint/process left untouched');
  if(spec.port){const url=`http://127.0.0.1:${spec.port}`;if(spec.role==='runner'){const response=await fetch(url+'/status',{signal:AbortSignal.timeout(5000)}),state=await response.json();if(!response.ok||state.installed!==true||state.runner_pid!==actual.pid)throw new Error('Owned backend health has not been established');}else{const response=await fetch(url,{signal:AbortSignal.timeout(5000)});if(!response.ok||!(await response.text()).includes('<title>9to1'))throw new Error('Owned display health has not been established');}}
  return actual;
}
export async function ensureService(configPath,role,planPath){
  configPath=path.resolve(configPath);const spec=await specification(configPath,role,planPath);await mkdir(spec.root,{recursive:true});
  const db=new DatabaseSync(path.join(spec.root,'startup.db'));db.exec('PRAGMA busy_timeout=12000;CREATE TABLE IF NOT EXISTS starters(role TEXT PRIMARY KEY,value TEXT NOT NULL)');const owner=await processIdentity(process.pid,spec.config.powershell),nonce=randomUUID();let held=false,child;
  try{
    if(!owner)throw new Error('Starter process identity is unavailable');
    db.exec('BEGIN IMMEDIATE');
    try{const row=db.prepare('SELECT value FROM starters WHERE role=?').get(role);if(row){const prior=JSON.parse(row.value);if(sameProcessBirth(prior.identity,await processIdentity(prior.identity.pid,spec.config.powershell))){db.exec('COMMIT');return {status:'START_IN_PROGRESS',role,pid:prior.identity.pid};}}
      db.prepare('INSERT INTO starters VALUES(?,?) ON CONFLICT(role) DO UPDATE SET value=excluded.value').run(role,JSON.stringify({identity:owner,nonce}));db.exec('COMMIT');held=true;
    }catch(e){db.exec('ROLLBACK');throw e;}
    const existing=await probe(spec);if(existing)return {status:'ALREADY_RUNNING',role,pid:existing.pid};
    let stdout,stderr;
    try{stdout=await open(path.join(spec.root,`startup-${role}-${Date.now()}-${nonce}.stdout.log`),'wx');stderr=await open(path.join(spec.root,`startup-${role}-${Date.now()}-${nonce}.stderr.log`),'wx');child=spawn(process.execPath,spec.args,{cwd:path.dirname(spec.args[0]),detached:true,windowsHide:true,shell:false,stdio:['ignore',stdout.fd,stderr.fd]});await new Promise((resolve,reject)=>{child.once('spawn',resolve);child.once('error',reject);});child.unref();}finally{await stdout?.close();await stderr?.close();}
    const end=Date.now()+20000;let last;
    while(Date.now()<end){try{const observed=await probe(spec);if(observed){if(observed.pid!==child.pid)throw new Error('Another service owns readiness; spawned identity was not adopted');return {status:'STARTED',role,pid:observed.pid};}}catch(e){last=e;}
      if(child.exitCode!==null||child.signalCode!==null)throw new Error('Owned startup exited before readiness: '+child.exitCode);await pause(200);
    }
    throw new Error('Owned service readiness not confirmed: '+(last?.message??'no endpoint'));
  }finally{if(held)db.prepare('DELETE FROM starters WHERE role=? AND json_extract(value,\'$.nonce\')=?').run(role,nonce);db.close();}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  try{const [,configPath,role,planPath]=process.argv.slice(1);console.log(JSON.stringify(await ensureService(configPath,role,planPath)));}catch(e){console.error(e.message);process.exitCode=1;}
}
