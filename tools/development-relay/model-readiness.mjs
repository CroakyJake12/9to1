import path from 'node:path';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));

// Startup is guarded by Start-StrataServer's signed local eligibility/fence check.
// A listening unknown or unhealthy server is preserved and reported, never killed.
export async function ensureModelReady(runner,{fence,permitted,retryAt=0,now=Date.now()}={}){
 if(!permitted)return {ready:false,retryAt,reason:'Outside the permitted local shift'};
 const health=await runner.health();if(health.ready)return {ready:true,retryAt:0,reused:true};
 if(now<retryAt)return {ready:false,retryAt,reason:'Model startup retry is cooling down'};
 const nextRetry=now+30*60000;
 try{const result=await runner.command(runner.config.powershell,['-NoProfile','-File',path.join(here,'Start-StrataServer.ps1'),'-Config',runner.config.config_path,'-Fence',String(fence)],here,16*60000,'strata-server');const observed=await runner.health();if(result.code!==0||!observed.ready)return {ready:false,retryAt:nextRetry,reason:'Guarded model startup did not establish readiness',log:result.log};return {ready:true,retryAt:0,reused:false,log:result.log};}
 catch(e){return {ready:false,retryAt:nextRetry,reason:e.message};}
}
