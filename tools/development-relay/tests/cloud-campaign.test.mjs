import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {createServer} from 'node:http';
import {generateKeyPairSync} from 'node:crypto';
import {campaignDue,ensureAcceptanceCampaign,currentMainSource} from '../cloud-campaign.mjs';
import {initialState,transition} from '../policy.mjs';
import {git,sha} from '../git.mjs';

test('Campaign preparation is inside the permitted local shift near its boundary',()=>{
 assert.equal(campaignDue({shift:'local',local_time:'21:49:00',next_boundary:'22:00'}),false);assert.equal(campaignDue({shift:'local',local_time:'21:50:00',next_boundary:'22:00'}),true);assert.equal(campaignDue({shift:'cloud',local_time:'22:00:00',next_boundary:'Next day'}),false);
});
test('A prepared acceptance checkpoint becomes a cloud-queued task only on exact ACK, using the fresh main handover rather than its earlier preparation SHA',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-campaign-')),repo=path.join(root,'main'),remote=path.join(root,'remote.git');await mkdir(repo);await git(repo,['init','-b','main']);await git(repo,['config','core.autocrlf','false']);await git(repo,['config','user.name','Fixture']);await git(repo,['config','user.email','fixture@example.test']);await writeFile(path.join(repo,'source.cs'),'first\n');await git(repo,['add','.']);await git(repo,['commit','-m','Fixture']);await git(root,['init','--bare',remote]);await git(repo,['remote','add','origin',remote]);await git(repo,['push','origin','main']);
 const at=Date.parse('2026-10-11T20:55:00Z'),state=initialState();transition(state,'presence',{home:true,paused:false,healthy:true,local_permitted:true,local_until:at+90000},'local',at);transition(state,'acquire',{},'local',at);
 const server=createServer((req,res)=>{let raw='';req.on('data',b=>raw+=b);req.on('end',()=>{try{transition(state,req.url.slice(1),JSON.parse(raw),'local',at+1);res.setHeader('content-type','application/json');res.end(JSON.stringify({state}));}catch(e){res.writeHead(409,{'content-type':'application/json'});res.end(JSON.stringify({error:e.message}));}});});await new Promise(r=>server.listen(0,'127.0.0.1',r));const key=path.join(root,'fixture.pem');await writeFile(key,generateKeyPairSync('rsa',{modulusLength:2048}).privateKey.export({type:'pkcs8',format:'pem'}));
 const config={repository:repo,branch_strategy:'main',supervisor_managed:true,cloud_acceptance_campaign:true,cloud_acceptance_tests:[{executable:'node',args:['--test','fixture.test.mjs']}],actor:'local',signing_key:key,coordinator_url:'http://127.0.0.1:'+server.address().port},planned={shift:'local',date:'2026-10-11',local_time:'21:55:00',next_boundary:'22:00'};
 try{const prepared=await ensureAcceptanceCampaign(config,state,planned),task=prepared.tasks.find(t=>t.handover_campaign);assert.equal(task.status,'CHECKPOINT');assert.equal(task.owner,'local');assert.equal(task.readonly_product,true);const old=task.source.sha;assert.equal((await ensureAcceptanceCampaign(config,prepared,planned)).tasks.length,1);
 await writeFile(path.join(repo,'source.cs'),'latest checkpoint\n');await git(repo,['add','source.cs']);await git(repo,['commit','-m','Latest fixture checkpoint']);await git(repo,['push','origin','main']);const source=await currentMainSource(config,prepared.tasks);assert.notEqual(source.sha,old);assert.equal(source.sha,await sha(repo));
 const handover={id:'campaign-handover',from:'local',to:'cloud',source,tasks:[task.id],tests:task.evidence,blockers:[]};transition(state,'handover',{fence:1,handover,remote_verified:true},'local',at+2);assert.equal(state.tasks[0].status,'CHECKPOINT');assert.throws(()=>transition(state,'ack',{handover_id:handover.id,revision:1,reconstructed_sha:old,remote_verified:true},'cloud',at+3),/unreconstructed/);transition(state,'ack',{handover_id:handover.id,revision:1,reconstructed_sha:source.sha,remote_verified:true},'cloud',at+4);assert.equal(state.tasks[0].status,'QUEUED');assert.equal(state.tasks[0].lane,'cloud');assert.equal(state.tasks[0].source.sha,source.sha);assert.equal(state.lease,null);
 }finally{await new Promise(r=>server.close(r));}
});
