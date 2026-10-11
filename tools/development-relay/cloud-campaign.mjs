import {request} from './client.mjs';
import {git,sha,dirty,blobHash} from './git.mjs';

export async function currentMainSource(config,tasks=[]){
 const root=config.repository,commit=await sha(root);
 if((await git(root,['branch','--show-current'])).trim()!=='main'||(await dirty(root)).paths.length)throw new Error('A clean canonical main checkpoint is required');
 if((await git(root,['ls-remote',config.remote??'origin','refs/heads/main'])).split(/\s/)[0]!==commit)throw new Error('Current main is not remotely reconstructable');
 const tracked=new Set((await git(root,['ls-files','-z'])).split('\0').filter(Boolean)),files=[...new Set(tasks.filter(t=>t.result?.source?.branch==='main').flatMap(t=>t.result.source.files??[]))].slice(-190),artifact_hashes={},deleted_paths=[];
 for(const file of files){if(tracked.has(file))artifact_hashes[file]=await blobHash(root,commit,file);else deleted_paths.push(file);}
 return {repository:'CroakyJake12/9to1',branch:'main',sha:commit,files,artifact_hashes,deleted_paths,hash_scope:'committed Git blob bytes',remote_verified:true};
}

export function campaignDue(planned,force=false){
 if(planned.shift!=='local'||!/^\d\d:\d\d$/.test(planned.next_boundary??''))return false;
 const minute=t=>Number(t.slice(0,2))*60+Number(t.slice(3,5)),remaining=minute(planned.next_boundary)-minute(planned.local_time);
 return force||remaining>=0&&remaining<=10;
}

export async function ensureAcceptanceCampaign(config,state,planned,force=false){
 if(!config.cloud_acceptance_campaign||!config.supervisor_managed||config.branch_strategy!=='main'||state.lease?.worker!=='local'||state.checkpoint_requested||!campaignDue(planned,force))return state;
 const id='cloud-acceptance-'+planned.date+'-'+state.lease.fence;if(state.tasks.some(t=>t.id===id))return state;
 if(!Array.isArray(config.cloud_acceptance_tests)||!config.cloud_acceptance_tests.length)throw new Error('Explicit cloud acceptance checks required');
 const source=await currentMainSource(config,state.tasks),task={id,executor:'coordinator',lane:'local',part:1,priority:900,source,paths:['tools/development-relay/acceptance-evidence/'],tests:config.cloud_acceptance_tests,verification_only:true,handover_campaign:true,readonly_product:true,display_title:'Overnight acceptance and deeper testing on main',objective:'Sol independently verifies the newest handed-over main snapshot against the complete four-part V2 specification. Continue bounded regression/integration, app journeys, persistence/reopen/import/export and donor coverage during the cloud shift. Preserve all source, underlying APIs and active owners. Product source is read-only for this campaign; write only redacted acceptance evidence within the assigned evidence path.',requirements:['Reconstruct/hash-check and ACK the exact newest main READY before acquiring and claiming this task. A preparation SHA is not a handover.','Strata remains on human hold; never restart it. Respect the active CUI/Home/Write/Present/Boards/Picture/Wave owners.','Run multiple bounded checks under a continuous exclusive heartbeat/stop-write guard. Record each subtask and raw evidence; report unavailable Windows/device/GUI checks NOT_RUN. Never infer FULL/RCVP acceptance from compilation or screenshots.','Preserve existing22:00Europe/London daily resume, account entitlement and saved signing identity. No paid API inference, new chats or publication branches. Prepare the exact morning handover.']};
 state=await request(config,'enqueue',{task});state=await request(config,'claim',{fence:state.lease.fence,task_id:id});
 return await request(config,'result',{fence:state.lease.fence,task_id:id,status:'CHECKPOINT',acceptance:'NOT_RUN',evidence:[{kind:'SOURCE_PREPARATION',status:'PASS',exit_code:0,source_sha:source.sha,scope:'Exact clean main SHA and committed artifact hashes; acceptance has not run'}],result:{source,remaining:['Run independent acceptance/deeper checks on the exact source assigned by ACK'],blockers:[],independent_acceptance:'NOT_RUN'}});
}
