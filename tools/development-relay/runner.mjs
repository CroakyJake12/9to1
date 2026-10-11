import { createServer } from 'node:http';
import { DatabaseSync } from 'node:sqlite';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { randomUUID, timingSafeEqual } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { selectShift } from './policy.mjs';
import { request } from './client.mjs';
import { createTaskWorktree, preserve, publish, git, sha } from './git.mjs';

const here=path.dirname(fileURLToPath(import.meta.url));
export class LocalRunner {
  constructor(config) {
    this.config=config;this.db=new DatabaseSync(config.state_db);
    this.db.exec('PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, at TEXT NOT NULL, event TEXT NOT NULL, value TEXT NOT NULL)');
    this.presence=this.get('presence')??{home:false,paused:false};this.remote=null;this.active=null;this.busy=false;this.draining=false;this.error=null;this.strata=null;
    this.recovery=this.get('active');if(this.recovery){this.draining=true;this.error='Interrupted worker requires preserved-source recovery; replacement worker is blocked';}
    this.csrf=randomUUID();this.server=null;this.retryAt=0;
  }
  get(k){const r=this.db.prepare('SELECT value FROM state WHERE key=?').get(k);return r?JSON.parse(r.value):null;}
  put(k,v){this.db.prepare('INSERT INTO state VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run(k,JSON.stringify(v));}
  event(event,value={}){this.db.prepare('INSERT INTO events(at,event,value) VALUES (?,?,?)').run(new Date().toISOString(),event,JSON.stringify(value));}
  async refreshConfiguration(){
    if(!this.config.config_path)return;
    const latest=JSON.parse(await readFile(this.config.config_path,'utf8'));
    selectShift(latest.schedule,{...this.presence,healthy:true});
    this.config.schedule=latest.schedule;
    this.config.cloud_environment_id=latest.cloud_environment_id;
    this.config.cloud_commissioning=latest.cloud_commissioning;
    this.config.coordinator_url=latest.coordinator_url;
  }
  async health(){
    try{const r=await fetch('http://127.0.0.1:8081/health',{signal:AbortSignal.timeout(3000)});const h=await r.json();return {...h,ready:r.ok&&h.loaded===true&&/iq2_xs/i.test(h.model??'')&&Number(h.max_context)===524288};}
    catch{return {ready:false,status:'UNREACHABLE'};}
  }
  status(){const cloudTask=this.remote?.lease?.worker==='cloud'&&this.remote.lease.expires_at>Date.now()?this.remote.tasks.find(t=>t.owner==='cloud'&&t.fence===this.remote.lease.fence&&t.status==='WORKING'):null;const cloud=this.get('cloud')??this.config.cloud_commissioning??{status:'NOT_CONFIGURED'};return {installed:true,runner_pid:process.pid,at:new Date().toISOString(),presence:this.presence,
    planned_shift:selectShift(this.config.schedule,{...this.presence,healthy:true}),coordinator:this.remote?'CONNECTED':'BLOCKED',
    active_shift:this.remote?.lease?.worker??'NONE',active_worker:this.active?.worker??(cloudTask?'Codex Cloud (owned task)':'NONE'),strata:this.strata,
    opencode:this.active?.worker==='OpenCode'?'RUNNING':'STOPPED',cloud:{...cloud,status:cloudTask?'TASK_OWNED':cloud.status,owned_task:cloudTask?.id??null},
    tasks:this.remote?.tasks??[],latest_handover:this.remote?.handovers?.at(-1)??null,
    checkpoint:this.get('checkpoint'),recovery:this.recovery,error:this.error,unattended_switching:'NOT_VERIFIED',
    events:this.db.prepare('SELECT at,event,value FROM events ORDER BY id DESC LIMIT 30').all().map(x=>({...x,value:JSON.parse(x.value)}))};}
  async control(action){
    if(!['home','away','pause','resume','checkpoint','handover'].includes(action))throw new Error('Unsupported control');
    if(action==='home'){this.presence.home=true;if(!this.recovery&&!this.presence.paused)this.draining=false;}if(action==='away')this.presence.home=false;
    if(action==='pause')this.presence.paused=true;if(action==='resume')this.presence.paused=false;
    this.put('presence',this.presence);this.event('CONTROL',{action});
    if(['away','pause','checkpoint','handover'].includes(action)){
      this.draining=true;
      if(this.active){const c=await preserve(this.active.root,this.config.checkpoints,randomUUID());this.put('checkpoint',c);}
      if(this.remote?.lease?.worker==='local')await request(this.config,'checkpoint',{});
      if(action==='handover')return await this.handover();
    }
    if(action==='resume'&&!this.recovery)this.draining=false;
    await this.tick();return {ok:true,message:['checkpoint','away','pause'].includes(action)&&this.active?'Checkpoint captured; bounded worker is draining. Ownership remains local.':action==='checkpoint'?'No active writer; no source checkpoint was needed.':'Control saved; observe status for coordinator and worker readiness',status:this.status()};
  }
  async handover(){
    if(this.active)throw new Error('Bounded writer is still running; handover is blocked until it drains');
    if(!this.remote?.lease || this.remote.lease.worker!=='local')throw new Error('No valid local lease/source is available for handover');
    const tasks=this.remote.tasks.filter(t=>t.owner==='local'&&['COMPLETED','CHECKPOINT','BLOCKED'].includes(t.status));
    if(!tasks.length)throw new Error('No released source checkpoint; handover was not published');
    // One handover per exact branch; a multi-branch shift publishes each independently.
    const task=tasks.find(t=>t.result?.source);if(!task)throw new Error('No remotely verified source commit');
    const h={id:randomUUID(),from:'local',to:'cloud',source:task.result.source,artifact_hashes:task.result.source.artifact_hashes??{},deleted_paths:task.result.source.deleted_paths??[],hash_scope:'committed Git blob bytes',tasks:[task.id],tests:task.evidence,blockers:task.result.blockers??[],spec:this.config.spec,next_actions:task.result.remaining??[]};
    this.remote=await request(this.config,'handover',{fence:this.remote.lease.fence,handover:h});
    await this.dispatchCloud(h);return {ok:true,message:'READY handover published; local ownership retained until exact cloud ACK',status:this.status()};
  }
  async dispatchCloud(h){
    if(this.config.cloud_commissioning?.daily_resume?.enabled&&this.config.cloud_commissioning.daily_resume.thread_id===this.config.cloud_commissioning.thread_id){this.put('cloud',{...this.config.cloud_commissioning,status:'AWAITING_SCHEDULED_RESUME',handover_id:h.id});return;}
    if(!this.config.cloud_environment_id){this.put('cloud',{status:'BLOCKED',reason:'No published Codex Cloud environment configured'});return;}
    const previous=this.get('cloud');if(previous&&['SUBMITTING','SUBMITTED','UNKNOWN'].includes(previous.status))throw new Error('An outstanding cloud submission must be reconciled before retry');
    this.put('cloud',{status:'SUBMITTING',handover_id:h.id});
    const prompt=await readFile(path.join(here,'CLOUD-WORKER.md'),'utf8');
    const args=['cloud','exec','--env',this.config.cloud_environment_id,'--branch',h.source.branch,'--attempts','1',`${prompt}\nExact handover: ${JSON.stringify(h)}\nCoordinator URL: ${this.config.coordinator_url}. Use the preconfigured cloud signing identity; never assume missing credentials.`];
    const result=await this.command(this.config.codex,args,this.config.repository,60000,'cloud-submit');
    const url=result.stdout.match(/https:\/\/[^\s]+/g)?.find(u=>new URL(u).hostname==='chatgpt.com');
    this.put('cloud',{status:result.code===0&&url?'SUBMITTED':'UNKNOWN',handover_id:h.id,url:url??null,evidence:result.log});
    if(result.code!==0||!url)throw new Error('Cloud submission has no confirmed task URL; reconcile before retry');
  }
  async command(executable,args,cwd,timeout,label,env={}){
    if(this.active){if(this.draining&&label.endsWith('-task'))throw new Error('Runner is draining; no new worker command may start');await request(this.config,'heartbeat',{fence:this.active.fence});}
    const log=path.join(this.config.logs,`${Date.now()}-${label}.log`);await mkdir(this.config.logs,{recursive:true});
    return await new Promise((resolve,reject)=>{
      // Noninteractive CLIs read redirected stdin to EOF before processing positional prompts.
      const child=spawn(executable,args,{cwd,env:{...process.env,...env},windowsHide:true,shell:false,stdio:['ignore','pipe','pipe']});let stdout='',stderr='',timedOut=false;
      if(this.active){this.child=child;this.active.pid=child.pid;this.put('active',this.active);}
      let overflow=false;const bound=()=>{if(!overflow){overflow=true;timedOut=true;void this.stopOwnedChild(child).catch(e=>{this.error=e.message;});}};
      child.stdout.on('data',b=>{stdout+=b;if(stdout.length>8*1024*1024){stdout=stdout.slice(0,8*1024*1024);bound();}});child.stderr.on('data',b=>{stderr+=b;if(stderr.length>8*1024*1024){stderr=stderr.slice(0,8*1024*1024);bound();}});
      const timer=setTimeout(()=>{timedOut=true;void this.stopOwnedChild(child).catch(e=>{this.error=e.message;});},timeout);child.once('error',e=>{clearTimeout(timer);reject(e);});
      child.once('close',async(code,signal)=>{clearTimeout(timer);if(this.child===child)this.child=null;await writeFile(log,JSON.stringify({executable,args,code,signal,timedOut},null,2)+'\n'+stdout+'\n'+stderr);resolve({code:timedOut?-1:code,stdout,stderr,log});});
    });
  }
  async stopOwnedChild(child){
    if(child.exitCode!==null||child.signalCode!==null)return;
    if(this.active){this.put('checkpoint',await preserve(this.active.root,this.config.checkpoints,randomUUID()));this.recovery={...this.active};this.put('active',this.recovery);}
    if(child.exitCode!==null||child.signalCode!==null)return;
    if(process.platform==='win32'){
      const {execFile}=await import('node:child_process');const {promisify}=await import('node:util');
      await promisify(execFile)('taskkill.exe',['/PID',String(child.pid),'/T','/F'],{windowsHide:true});
    }else child.kill('SIGTERM');
    this.event('OWNED_WORKER_STOPPED',{pid:child.pid,reason:'Lease loss or bounded timeout; source preserved first'});
  }
  async renew(){
    if(!this.remote?.lease||this.remote.lease.worker!=='local')return;
    try{await this.refreshConfiguration();const planned=selectShift(this.config.schedule,{...this.presence,healthy:true});await request(this.config,'presence',{...this.presence,healthy:true,local_permitted:planned.shift==='local',local_until:planned.shift==='local'?Date.now()+30000:0});await request(this.config,'heartbeat',{fence:this.remote.lease.fence});}
    catch(e){this.draining=true;this.error='Lease heartbeat failed: '+e.message;this.event('LEASE_LOST',{error:e.message});if(this.child)await this.stopOwnedChild(this.child);}
  }
  async startTask(task,fence){
    this.remote=await request(this.config,'claim',{fence,task_id:task.id});
    const worktree=await createTaskWorktree(this.config,task,fence);
    this.active={...worktree,task_id:task.id,fence,worker:task.executor==='strata'?'Strata coordinator':'OpenCode',started_at:Date.now()};this.put('active',this.active);this.event('WORKER_STARTED',{task:task.id,worker:this.active.worker});
    void this.executeTask(task,fence).catch(e=>{this.error=e.message;this.event('WORKER_ERROR',{task:task.id,error:e.message});});
  }
  async executeTask(task,fence){
    const worktree=this.active,evidence=[];let result,status='BLOCKED';
    try{
      const owned=task.paths.join('\n');const prompt=`${await readFile(path.join(here,'LOCAL-WORKER.md'),'utf8')}\nTask: ${task.id}\nObjective: ${task.objective}\nOwned paths:\n${owned}\nSource: ${task.source.sha}\nRequirements: ${JSON.stringify(task.requirements??[])}\nDo not commit or push; the runner reviews paths and records actual tests. Do not access other worktrees. Stop at a bounded checkpoint.\n`;
      if(task.executor==='strata'){
        const inventory=await this.command(this.config.codex,['plugin','list','--json'],worktree.root,30000,'plugin-inventory');if(inventory.code!==0)throw new Error('Codex plugin inventory unavailable');
        const installed=JSON.parse(inventory.stdout).installed;
        const args=['exec','--profile','strata','--sandbox','workspace-write','-C',worktree.root,'-c','model_context_window=524288','-c','model_auto_compact_token_limit=450560','-c','apps._default.enabled=false','-c','apps.google_drive.enabled=true','-c','apps.github.enabled=true'];
        for(const item of installed)args.push('-c',`plugins."${item.pluginId}".enabled=${this.config.plugin_allowlist.includes(item.pluginId)}`);
        args.push(prompt);result=await this.command(this.config.codex,args,worktree.root,task.timeout_ms??1800000,'strata-task');
      }else{
        const args=['-NoProfile','-File',path.join(here,'Run-OpenCode.ps1'),'-Config',this.config.config_path,'-Worktree',worktree.root,'-Fence',String(fence),'-TaskId',task.id,'-OwnedPaths',JSON.stringify(task.paths),'-Prompt',prompt];
        result=await this.command(this.config.powershell,args,worktree.root,task.timeout_ms??1800000,'opencode-task');
      }
      evidence.push({kind:'IMPLEMENTATION',exit_code:result.code,status:result.code===0?'PASS':'FAIL',log:result.log});if(result.code!==0)throw new Error('Implementation worker did not exit successfully');
      const checkpoint=await preserve(worktree.root,this.config.checkpoints,randomUUID());this.put('checkpoint',checkpoint);
      for(const test of task.tests){
        if(!this.config.allowed_test_executables.includes(test.executable) || !Array.isArray(test.args))throw new Error('Unapproved test executable');
        const r=await this.command(test.executable,test.args,worktree.root,Math.min(test.timeout_ms??300000,1800000),'test');
        evidence.push({kind:test.kind??'TARGETED_TEST',command:[test.executable,...test.args],exit_code:r.code,status:r.code===0?'PASS':'FAIL',log:r.log});if(r.code!==0)throw new Error('Applicable verification failed; source is preserved');
      }
      await request(this.config,'heartbeat',{fence});
      const source=await publish(this.config,worktree,task);status='COMPLETED';
      await request(this.config,'result',{fence,task_id:task.id,status,evidence,acceptance:'BUILD_AND_TARGETED_TESTS',result:{source,remaining:[],blockers:[],independent_acceptance:'NOT_RUN',RCVP:'NOT_RUN',FULL:'NOT_RUN'}});
    }catch(e){
      const checkpoint=await preserve(worktree.root,this.config.checkpoints,randomUUID());this.put('checkpoint',checkpoint);
      try{await request(this.config,'result',{fence,task_id:task.id,status:'BLOCKED',evidence,acceptance:'NOT_RUN',result:{blockers:[e.message],checkpoint:checkpoint.directory}});}catch{this.recovery=worktree;this.draining=true;}
      this.error=e.message;
    }finally{this.active=null;this.put('active',this.recovery??null);this.event('WORKER_FINISHED',{task:task.id,status});}
  }
  async tick(){
    if(this.busy)return;this.busy=true;
    try{
      await this.refreshConfiguration();
      this.strata=await this.health();const planned=selectShift(this.config.schedule,{...this.presence,healthy:true});
      if(!this.config.coordinator_url){this.error='Shared coordinator not configured; development cannot start';return;}
      this.remote=await request(this.config,'presence',{...this.presence,healthy:true,local_permitted:planned.shift==='local',local_until:planned.shift==='local'?Date.now()+30000:0});
      const lease=this.remote.lease;
      if(lease?.worker==='local')this.remote=await request(this.config,'heartbeat',{fence:lease.fence});
      if(planned.shift!=='local'||this.presence.paused||this.recovery||this.draining||(this.remote.checkpoint_requested&&lease?.worker==='local')||this.remote.handovers.some(h=>h.status==='READY'&&h.from==='local')){
        if(planned.shift!=='local'||this.presence.paused)this.draining=true;
        if(!this.active&&!this.recovery&&lease?.worker==='local'){
          const owned=this.remote.tasks.filter(t=>t.owner==='local');
          if(!owned.length)this.remote=await request(this.config,'release',{fence:lease.fence});
          else if(!this.remote.handovers.some(h=>h.status==='READY'&&h.from==='local'))await this.handover();
        }
        return;
      }
      if(lease?.worker==='cloud'){this.error='Cloud owns the shift; awaiting validated cloud-to-local handover';return;}
      if(!lease){this.remote=await request(this.config,'acquire',{});}
      if(this.active)return;
      const task=this.remote.tasks.filter(t=>t.status==='QUEUED'&&(!t.lane||t.lane==='local')).sort((a,b)=>(a.part??1)-(b.part??1)||(b.priority??0)-(a.priority??0))[0];
      if(!task){this.error='No owned, unblocked development task is queued';return;}
      if(!this.strata.ready){
        if(Date.now()<this.retryAt)return;
        this.retryAt=Date.now()+30*60000;
        const r=await this.command(this.config.powershell,['-NoProfile','-File',path.join(here,'Start-StrataServer.ps1'),'-Config',this.config.config_path,'-Fence',String(this.remote.lease.fence)],here,16*60000,'strata-server');
        this.strata=await this.health();if(r.code!==0||!this.strata.ready)throw new Error('Strata readiness failed; retry bounded to 30 minutes');
      }
      await this.startTask(task,this.remote.lease.fence);this.error=null;
    }catch(e){this.error=e.message;this.event('RUNNER_BLOCKED',{error:e.message});}
    finally{this.busy=false;}
  }
  async start(){
    const html=await readFile(path.join(here,'dashboard.html'),'utf8');
    this.server=createServer(async(req,res)=>{
      const host=`127.0.0.1:${this.config.dashboard_port}`;
      if(req.headers.host!==host){res.writeHead(403);res.end('Invalid host');return;}
      const respond=(code,data,type='application/json')=>{res.writeHead(code,{'content-type':type,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Content-Security-Policy':"default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'"});res.end(type==='application/json'?JSON.stringify(data):data);};
      try{
        if(req.method==='GET'&&req.url==='/')return respond(200,html.replace('__CSRF__',JSON.stringify(this.csrf)),'text/html; charset=utf-8');
        if(req.method==='GET'&&req.url==='/status')return respond(200,this.status());
        if(req.method==='POST'&&req.url?.startsWith('/control/')){
          if(req.headers.origin!==`http://${host}`||!req.headers['x-relay-csrf']||Buffer.byteLength(req.headers['x-relay-csrf'])!==Buffer.byteLength(this.csrf)||!timingSafeEqual(Buffer.from(req.headers['x-relay-csrf']),Buffer.from(this.csrf)))return respond(403,{ok:false,error:'Control must originate in the local dashboard'});
          return respond(200,await this.control(req.url.slice('/control/'.length)));
        }respond(404,{error:'Unknown route'});
      }catch(e){respond(409,{ok:false,error:e.message,status:this.status()});}
    });
    await new Promise((resolve,reject)=>{this.server.once('error',reject);this.server.listen(this.config.dashboard_port,'127.0.0.1',resolve);});
    this.event('RUNNER_STARTED',{pid:process.pid});this.timer=setInterval(()=>void this.tick(),15000);this.heartbeatTimer=setInterval(()=>void this.renew().catch(e=>{this.error=e.message;}),30000);await this.tick();return this;
  }
  async stop(){this.draining=true;this.event('RUNNER_STOP_REQUESTED');if(this.active){this.put('checkpoint',await preserve(this.active.root,this.config.checkpoints,randomUUID()));throw new Error('Bounded worker still running; runner shutdown deferred');}clearInterval(this.timer);clearInterval(this.heartbeatTimer);await new Promise(r=>this.server.close(r));this.db.close();}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const configPath=process.argv[2];if(!configPath)throw new Error('Usage: node runner.mjs ABSOLUTE_PRIVATE_CONFIG');
  const config=JSON.parse(await readFile(configPath,'utf8'));config.config_path=path.resolve(configPath);await mkdir(path.dirname(config.state_db),{recursive:true});
  const runner=await new LocalRunner(config).start();process.on('SIGTERM',()=>void runner.stop().then(()=>process.exit(0)));process.on('SIGINT',()=>void runner.stop().then(()=>process.exit(0)));
  console.log(`9to1 relay dashboard: http://127.0.0.1:${config.dashboard_port}`);
}
