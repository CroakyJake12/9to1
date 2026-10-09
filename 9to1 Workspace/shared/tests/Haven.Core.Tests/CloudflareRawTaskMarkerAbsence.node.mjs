import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
// Extract the actual production body, not an independently implemented observation.
const source=readFileSync(process.argv[2]??new URL('CloudflareRawTaskMarkerAbsenceTransport.cs',import.meta.url),'utf8');
const body=source.match(/public const string OriginalReadBody = """\n([\s\S]*?)\n""";/)[1];
const ns='a'.repeat(32);
const code=body.replaceAll('__NAMESPACE_URL__',JSON.stringify('https://api.cloudflare.com/client/v4/accounts/'+ 'b'.repeat(32)+'/storage/kv/namespaces/'+ns))
 .replaceAll('__KEY_URL__',JSON.stringify('https://api.cloudflare.com/client/v4/accounts/'+ 'b'.repeat(32)+'/storage/kv/namespaces/'+ns+'/values/9to1-task/owned/run/proof'))
 .replaceAll('__NAMESPACE_ID__',JSON.stringify(ns)).replaceAll('__OPERATION_KEY__',JSON.stringify('owned-operation'));
function metadata(title='controlled',id=ns){return new Response(JSON.stringify({success:true,result:{id,title}}),{status:200});}
async function invoke(responses){let calls=0;const fetch=async()=>{assert.ok(calls<responses.length);return responses[calls++];};
 const original=Function('fetch','return ('+code+')')(fetch);try{return {value:await original(),calls};}catch(error){error.calls=calls;throw error;}}
test('actual 404 between exact healthy namespace reads issues detached absence',async()=>{
 let cleanup=0;const key={status:404,ok:false,body:{cancel:async()=>{cleanup++;}}};
 const actual=await invoke([metadata(),key,metadata()]);assert.equal(actual.calls,3);assert.equal(cleanup,1);
 assert.deepEqual(actual.value,{operation_key:'owned-operation',ok:true,status:404,data:{absent:true,namespace_id:ns}});
});
test('wrong key status never reaches the final namespace read',async()=>{
 for(const status of [200,403,429,500]){let cleanup=0;
  await assert.rejects(invoke([metadata(),{status,ok:status===200,body:{cancel:async()=>{cleanup++;}}}]),error=>error.calls===2);
  assert.equal(cleanup,1);}
});
test('original key body cleanup fault is retained and blocks absence',async()=>{
 const cause=new Error('controlled original cancel');
 await assert.rejects(invoke([metadata(),{status:404,ok:false,body:{cancel:async()=>{throw cause;}}}]),
  error=>error instanceof AggregateError&&error.errors.includes(cause)&&error.calls===2);
});
test('foreign namespace or changed final namespace refuses the actual 404',async()=>{
 await assert.rejects(invoke([metadata('controlled','c'.repeat(32))]),error=>error.calls===1);
 await assert.rejects(invoke([metadata(),{status:404,ok:false,body:null},metadata('changed')]),error=>error.calls===3);
});
test('simultaneous namespace read and cleanup faults retain both exact originals',async()=>{
 const read=new Error('controlled original read'),cancel=new Error('controlled original cancel');
 const bad={status:200,ok:true,body:{getReader:()=>({read:async()=>{throw read;},cancel:async()=>{throw cancel;},releaseLock:()=>{}})}};
 await assert.rejects(invoke([bad]),error=>error instanceof AggregateError&&error.errors.includes(read)&&error.errors.includes(cancel)&&error.calls===1);
});
