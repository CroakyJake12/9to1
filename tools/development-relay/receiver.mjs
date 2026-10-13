import {mkdir,readFile,writeFile,rename,lstat} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomUUID} from 'node:crypto';
import path from 'node:path';
import {request} from './client.mjs';
import {dirty,sha,verifyArtifactHashes} from './git.mjs';
const run=promisify(execFile);
const same=(a,b)=>JSON.stringify(Object.entries(a).sort())===JSON.stringify(Object.entries(b).sort());
function check(condition,message){if(!condition)throw new Error(message);}
export function validateReceive(state,expected,actor='local'){
 const h=state.handovers.find(x=>x.id===expected.id);
 check(h&&h.to===actor&&h.from!==actor&&['local','cloud'].includes(h.from),'Handover has wrong recipient or sender');
 check(h.revision===expected.revision&&h.source?.sha===expected.sha,'Handover revision or exact source changed');
 check(h.source.repository==='CroakyJake12/9to1'&&/^[0-9a-f]{40}$/.test(h.source.sha)&&typeof h.source.branch==='string'&&Number.isSafeInteger(h.revision)&&h.revision>0&&Number.isSafeInteger(h.fence)&&h.fence>0,'Invalid exact handover identity');
 check(!state.handovers.some(x=>x.to===actor&&x.revision>h.revision&&['READY','ACKNOWLEDGED'].includes(x.status)),'Handover source was superseded');
 check(!state.presence?.paused,'Relay paused');
 if(h.status==='ACKNOWLEDGED'){
  check(h.ack?.worker===actor&&h.ack.revision===h.revision&&h.ack.sha===h.source.sha,'Exact acknowledgement differs');return h;
 }
 check(h.status==='READY','No READY handover for this receiver');
 // ACK transfers released custody; it never renews or writes under the sender lease.
 check(state.lease?.worker===h.from&&state.lease.fence===h.fence&&state.fence===h.fence,'Sender custody changed; explicit recovery is required');
 check(!state.tasks.some(t=>['WORKING','RECOVERY_REQUIRED','READY'].includes(t.status)),'Active or recovery source writer blocks receiving');
 check(Array.isArray(h.tasks)&&h.tasks.length>0,'Released tasks are missing');
 for(const id of h.tasks){const t=state.tasks.find(t=>t.id===id);check(t&&t.owner===h.from&&t.fence===h.fence&&['CHECKPOINT','COMPLETED','BLOCKED'].includes(t.status),'Handover includes an unreleased task');}
 return h;
}
function declaredHashes(h){
 const a=h.artifact_hashes??h.source.artifact_hashes;
 check(a&&typeof a==='object'&&!Array.isArray(a)&&Object.keys(a).length>0&&Object.keys(a).length<=200,'Committed artifact hashes are required');
 if(h.artifact_hashes&&h.source.artifact_hashes)check(same(h.artifact_hashes,h.source.artifact_hashes),'Conflicting committed artifact declarations');
 for(const file of h.source.files??[])check(Object.hasOwn(a,file),'Declared source file lacks a committed hash');return a;
}
async function noLink(p){try{check(!(await lstat(p)).isSymbolicLink(),'Receiving storage must not redirect through a link');}catch(e){if(e.code!=='ENOENT')throw e;}}
async function persist(file,receipt){await noLink(file);const temp=file+'.'+randomUUID()+'.tmp';await writeFile(temp,JSON.stringify(receipt,null,2)+'\n',{mode:0o600,flag:'wx'});await rename(temp,file);}
export async function receiveHandover(config,id,options={}){
 check(/^[a-zA-Z0-9-]{1,100}$/.test(id),'Invalid handover locator');
 const actor=config.actor??'local',limit=config.receive_timeout_ms??120000;
 check(Number.isSafeInteger(limit)&&limit>0&&limit<=600000,'Invalid bounded receive deadline');
 const deadline=Date.now()+limit;
 const boundedGit=async(root,args)=>{const remaining=deadline-Date.now();check(remaining>0,'Receive deadline exceeded');return(await run('git',['-c',`safe.directory=${root}`,'-C',root,...args],{timeout:Math.min(remaining,60000),maxBuffer:20*1024*1024,windowsHide:true})).stdout.trim();};
 const repository=path.resolve(config.repository);await noLink(repository);
 check(!(await dirty(repository)).paths.length,'Canonical receiver is dirty; preserve source before receiving');
 const state=await request(config,'state'),original=state.handovers.find(h=>h.id===id);
 check(original,'No handover for this receiver');
 const expected={id,revision:options.revision??original.revision,sha:options.sha??original.source?.sha};
 const h=validateReceive(state,expected,actor),hashes=declaredHashes(h);
 await boundedGit(repository,['check-ref-format','refs/heads/'+h.source.branch]);
 const checkpoints=path.resolve(config.checkpoints);await noLink(checkpoints);await mkdir(checkpoints,{recursive:true});
 const receiptFile=path.join(checkpoints,'received-'+id+'.json');await noLink(receiptFile);
 let receipt;try{receipt=JSON.parse(await readFile(receiptFile,'utf8'));}catch(e){if(e.code!=='ENOENT')throw e;}
 if(receipt)check(receipt.handover_id===id&&receipt.revision===expected.revision&&receipt.source?.sha===expected.sha&&receipt.actor===actor&&same(receipt.artifact_hashes,hashes),'Preserved receive receipt differs; reconcile without overwrite');
 if(h.status==='ACKNOWLEDGED'){
  check(receipt,'Acknowledged source has no preserved reconstruction receipt');
  await verifyArtifactHashes(receipt.root,h.source.sha,hashes);receipt={...receipt,phase:'ACKNOWLEDGED',ack:h.ack};await persist(receiptFile,receipt);
  return {state,receipt,receiptFile,reused:true};
 }
 const fetchExact=async()=>{await boundedGit(repository,['fetch','--no-tags',config.remote??'origin','refs/heads/'+h.source.branch]);const head=await boundedGit(repository,['rev-parse','FETCH_HEAD']);
  if(h.source.branch==='main'){try{await boundedGit(repository,['merge-base','--is-ancestor',h.source.sha,head]);}catch{throw new Error('Released exact main is missing or diverges from the fetched head');}}
  else check(head===h.source.sha,'Remote handover source changed');return head;};
 const fetched=await fetchExact();
 if(!receipt){const worktrees=path.resolve(config.worktrees);await noLink(worktrees);const target=process.platform==='win32'?worktrees.toLowerCase():worktrees,canonicalPath=process.platform==='win32'?repository.toLowerCase():repository;check(target!==canonicalPath&&!target.startsWith(canonicalPath+path.sep),'Receiving snapshot must be outside canonical source');await mkdir(worktrees,{recursive:true});
  const root=path.join(worktrees,'received-'+randomUUID());await boundedGit(repository,['worktree','add','--detach',root,h.source.sha]);
  receipt={handover_id:id,revision:h.revision,sender_fence:h.fence,actor,source:h.source,root,artifact_hashes:hashes,fetched_head:fetched,phase:'RECONSTRUCTED',received_at:new Date().toISOString()};
 }
 const verify=async()=>{for(const file of Object.keys(hashes)){const entry=await boundedGit(receipt.root,['ls-tree',h.source.sha,'--',file]);check(/^100(644|755) blob /.test(entry),'Artifact must be a regular committed file');}
  return await verifyArtifactHashes(receipt.root,h.source.sha,hashes);};
 await verify();await persist(receiptFile,receipt);options.onEvent?.('RETURN_SOURCE_RECONSTRUCTED',{handover:id,revision:h.revision,sha:h.source.sha,root:receipt.root});
 let canonical=await sha(repository);
 if(options.advanceMain){check(config.branch_strategy==='main'&&h.source.branch==='main'&&(await boundedGit(repository,['branch','--show-current']))==='main','Automatic return requires clean canonical main');
  try{await boundedGit(repository,['merge-base','--is-ancestor',canonical,fetched]);}catch{throw new Error('Canonical main diverges or contains unpublished source; preserve it for reconciliation');}
  if(canonical!==h.source.sha){let contains=false;try{await boundedGit(repository,['merge-base','--is-ancestor',h.source.sha,canonical]);contains=true;}catch{}
   if(!contains){try{await boundedGit(repository,['merge-base','--is-ancestor',canonical,h.source.sha]);}catch{throw new Error('Canonical main diverges; preserve source for reconciliation');}
    check(!(await dirty(repository)).paths.length,'Canonical receiver changed before fast-forward');await boundedGit(repository,['merge','--ff-only',h.source.sha]);canonical=h.source.sha;}
  }
 }
 await options.beforeAck?.({root:receipt.root,handover:h});
 validateReceive(await request(config,'state'),expected,actor);
 check(!(await dirty(repository)).paths.length&&await sha(repository)===canonical,'Canonical receiver changed during reconstruction');
 await verify();const refreshed=await fetchExact();if(options.advanceMain){try{await boundedGit(repository,['merge-base','--is-ancestor',canonical,refreshed]);}catch{throw new Error('Canonical main no longer belongs to published history');}}validateReceive(await request(config,'state'),expected,actor);
 await options.beforeAck?.({root:receipt.root,handover:h});
 check(!(await dirty(repository)).paths.length&&await sha(repository)===canonical,'Canonical receiver changed before ACK');await verify();
 check(Date.now()<deadline,'Receive deadline exceeded');
 const result=await request(config,'ack',{handover_id:id,revision:h.revision,reconstructed_sha:h.source.sha});
 const acknowledged=validateReceive(result,expected,actor);
 check(acknowledged.status==='ACKNOWLEDGED'&&!result.lease,'ACK readback did not transfer exact custody');
 receipt={...receipt,phase:'ACKNOWLEDGED',ack:acknowledged.ack};await persist(receiptFile,receipt);
 options.onEvent?.('RETURN_ACKNOWLEDGED',{handover:id,revision:h.revision,sha:h.source.sha});return {state:result,receipt,receiptFile,reused:false};
}
