import assert from 'node:assert/strict';
import {mkdtemp,writeFile,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {validate,origin,defaultDeploymentVersion,deploymentPin} from './validate.mjs';
const owned=await mkdtemp(join(tmpdir(),'cake-https-driver-'));
let primary;
try {
  const transport=new Error('controlled offline transport failure');
  const path=join(owned,'transport.json');
  await assert.rejects(validate({output:path,request:async url=>{assert.equal(url.origin,origin);throw transport;}}),error=>error===transport);
  assert.equal(JSON.parse(await readFile(path)).result,'failed');
  assert.equal(JSON.parse(await readFile(path)).operatorPinnedDeploymentVersion,defaultDeploymentVersion);
  const newVersion='8c60f7a9-9f8a-4e4c-97d3-d806d1507e31';
  assert.equal(deploymentPin(),defaultDeploymentVersion);assert.equal(deploymentPin(newVersion),newVersion);
  let requests=0;
  for(const deploymentVersion of ['',null,42,newVersion.toUpperCase(),` ${newVersion}`,`${newVersion}\n`,'00000000-0000-0000-0000-000000000000','https://foreign.example.test'])await assert.rejects(validate({deploymentVersion,request:async()=>{requests++;throw transport;}}),/Deployment version/);
  assert.equal(requests,0,'Invalid deployment pins refuse before requests');
  const pinned=join(owned,'new-version-transport.json');
  await assert.rejects(validate({deploymentVersion:newVersion,output:pinned,request:async()=>{throw transport;}}),error=>error===transport);
  assert.equal(JSON.parse(await readFile(pinned)).operatorPinnedDeploymentVersion,newVersion,'Failure evidence carries the explicitly reviewed pin');
  await assert.rejects(validate({request:async()=>new Response(null,{status:302,headers:{Location:'https://foreign.example.test'}})}),/Unexpected redirect/);
  await assert.rejects(validate({request:async()=>new Response('{}',{status:200,headers:{'Access-Control-Allow-Origin':'*'}})}));
  await assert.rejects(validate({request:async()=>Response.json({issuer:'https://foreign.example.test'},{headers:{'Access-Control-Allow-Origin':origin,Vary:'Origin'}})}));
  const existing=join(owned,'existing.json');await writeFile(existing,'preserved');
  await assert.rejects(validate({output:existing,request:async()=>{throw transport;}}),error=>error instanceof AggregateError&&error.errors[0]===transport&&error.errors[1].code==='EEXIST');
  assert.equal(await readFile(existing,'utf8'),'preserved');
  console.log('PASS: original 7 refusal/failure-preservation controls plus deployment pin preflight/propagation; no deployed-service acceptance');
}catch(error){primary=error;}
let cleanup;try{await rm(owned,{recursive:true});}catch(error){cleanup=error;}
if(primary&&cleanup)throw new AggregateError([primary,cleanup],'Driver control and cleanup failed');
if(primary)throw primary;if(cleanup)throw cleanup;
