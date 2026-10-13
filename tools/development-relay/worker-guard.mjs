import {readFile,writeFile} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {request} from './client.mjs';
import {preserve} from './git.mjs';
const execute=promisify(execFile);
export async function processIdentity(pid,powershell){
  if(!Number.isSafeInteger(pid)||pid<=0)throw new Error('Exact positive owned PID required');
  if(process.platform==='win32'){
    const script=`$p=Get-CimInstance Win32_Process -Filter 'ProcessId=${pid}'; if($p){[pscustomobject]@{pid=[int]$p.ProcessId;parent=[int]$p.ParentProcessId;created=$p.CreationDate.ToUniversalTime().ToString('o');executable=$p.ExecutablePath}|ConvertTo-Json -Compress}`;
    const result=await execute(powershell,['-NoProfile','-Command',script],{windowsHide:true,timeout:5000,maxBuffer:8192});return result.stdout.trim()?JSON.parse(result.stdout):null;
  }
  try{const stat=await readFile(`/proc/${pid}/stat`,'utf8'),fields=stat.slice(stat.lastIndexOf(')')+2).split(' ');return {pid,parent:Number(fields[1]),created:fields[19],state:fields[0]};}catch(e){if(e.code==='ENOENT')return null;throw e;}
}
export function sameProcess(expected,actual){return !!expected&&!!actual&&actual.pid===expected.pid&&actual.parent===expected.parent&&actual.created===expected.created;}
export function sameProcessBirth(expected,actual){return !!expected&&!!actual&&actual.state!=='Z'&&actual.pid===expected.pid&&actual.created===expected.created;}
export async function runGuard(planPath){
  let parentGone=!process.connected;process.once('disconnect',()=>{parentGone=true;});
  const plan=JSON.parse(await readFile(planPath,'utf8'));
  if(typeof process.send!=='function'||!Number.isSafeInteger(plan.fence)||!plan.task_id||!plan.identity||!plan.parent_identity||plan.parent_identity.pid!==plan.parent_pid||plan.identity.parent!==plan.parent_pid||plan.identity.pid!==plan.child_pid||!plan.root||!plan.receipt||plan.deadline!==null&&!Number.isFinite(plan.deadline))throw new Error('Private owned process plan and parent IPC required');
  let ending=false,renewing=false,current=null,seenAt=0;
  const finish=async(reason,stop)=>{
    if(ending)return;ending=true;clearInterval(poll);clearInterval(watch);
    let checkpoint=null,error=null,killed=false;
    try{
      if(stop){const actual=await processIdentity(plan.child_pid,plan.config.powershell),originalParentGone=!sameProcess(plan.parent_identity,await processIdentity(plan.parent_pid,plan.config.powershell));if(actual&&!sameProcess(plan.identity,actual)&&!(originalParentGone&&sameProcessBirth(plan.identity,actual)))throw new Error('Owned PID identity changed; replacement process left untouched');
        const signal=kind=>{try{process.kill(plan.child_pid,kind);}catch(e){if(e.code!=='ESRCH')throw e;}};
        if(actual){if(process.platform==='win32')await execute('taskkill.exe',['/PID',String(plan.child_pid),'/T','/F'],{windowsHide:true,timeout:10000,maxBuffer:8192});else signal('SIGTERM');killed=true;}
        const end=Date.now()+5000;let observed;while(sameProcessBirth(plan.identity,observed=await processIdentity(plan.child_pid,plan.config.powershell))){if(Date.now()>=end)throw new Error('Owned writer did not stop; final checkpoint refused');if(process.platform!=='win32'&&Date.now()>end-3000)signal('SIGKILL');await new Promise(r=>setTimeout(r,100));}
        // Preserve after the writer has stopped; copying before termination races its last writes.
        checkpoint=await preserve(plan.root,plan.checkpoints,'guard-'+plan.id);
      }
    }catch(e){error=e.message;}
    await writeFile(plan.receipt,JSON.stringify({at:new Date().toISOString(),task_id:plan.task_id,fence:plan.fence,child_pid:plan.child_pid,identity:plan.identity,reason,killed,checkpoint:checkpoint?.directory??null,error},null,2));
    process.exitCode=error?2:0;if(process.connected)process.disconnect();
  };
  const verify=async()=>{
    if(ending||renewing)return;renewing=true;
    try{if(!plan.parent_identity||!sameProcess(plan.parent_identity,await processIdentity(plan.parent_pid,plan.config.powershell)))throw new Error('Parent runner exited or identity changed');const state=await request(plan.config,'state'),task=state.tasks.find(t=>t.id===plan.task_id);if(state.lease?.worker!=='local'||state.lease.fence!==plan.fence||state.lease.expires_at<=Date.now()||task?.owner!=='local'||task.fence!==plan.fence||task.status!=='WORKING')throw new Error('Owned task lease lost');if(state.checkpoint_requested||state.presence&&(state.presence.paused||!state.presence.home))throw new Error('Permitted shift ended or operator requested a checkpoint');current=state.lease;seenAt=Date.now();}
    catch(e){await finish(e.message,true);}finally{renewing=false;}
  };
  const poll=setInterval(()=>void verify(),2000),watch=setInterval(()=>{const limited=plan.deadline!==null&&Date.now()>=plan.deadline;if(!ending&&(!process.connected||limited||current&&Date.now()>=current.expires_at||seenAt&&Date.now()-seenAt>15000))void finish(!process.connected?'Parent runner exited':limited?'Bounded verification/commissioning deadline':'Lease verification expired',true);},100);
  process.once('disconnect',()=>void finish('Parent runner exited',true));process.on('message',message=>{if(message?.action==='complete')void finish('Owned command completed',false);});
  if(parentGone)await finish('Parent runner exited during guard startup',true);else await verify();if(!ending&&process.connected)process.send({ready:true,child_pid:plan.child_pid});
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))await runGuard(process.argv[2]);
