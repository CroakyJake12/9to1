import assert from 'node:assert/strict';
import {mkdtemp,writeFile,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {validate,origin} from './validate.mjs';
const owned=await mkdtemp(join(tmpdir(),'cake-https-driver-'));
let primary;
try {
  const transport=new Error('controlled offline transport failure');
  const path=join(owned,'transport.json');
  await assert.rejects(validate({output:path,request:async url=>{assert.equal(url.origin,origin);throw transport;}}),error=>error===transport);
  assert.equal(JSON.parse(await readFile(path)).result,'failed');
  await assert.rejects(validate({request:async()=>new Response(null,{status:302,headers:{Location:'https://foreign.example.test'}})}),/Unexpected redirect/);
  await assert.rejects(validate({request:async()=>new Response('{}',{status:200,headers:{'Access-Control-Allow-Origin':'*'}})}));
  await assert.rejects(validate({request:async()=>Response.json({issuer:'https://foreign.example.test'},{headers:{'Access-Control-Allow-Origin':origin,Vary:'Origin'}})}));
  const existing=join(owned,'existing.json');await writeFile(existing,'preserved');
  await assert.rejects(validate({output:existing,request:async()=>{throw transport;}}),error=>error instanceof AggregateError&&error.errors[0]===transport&&error.errors[1].code==='EEXIST');
  assert.equal(await readFile(existing,'utf8'),'preserved');
  console.log('PASS: 7 offline driver refusal/failure-preservation controls; no deployed-service acceptance');
}catch(error){primary=error;}
let cleanup;try{await rm(owned,{recursive:true});}catch(error){cleanup=error;}
if(primary&&cleanup)throw new AggregateError([primary,cleanup],'Driver control and cleanup failed');
if(primary)throw primary;if(cleanup)throw cleanup;
