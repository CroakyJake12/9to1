import test from 'node:test';
import assert from 'node:assert/strict';
import {Miniflare,convertV4MiniflareOptions} from 'miniflare';
import {generateKeyPairSync,createHash,randomUUID,sign} from 'node:crypto';
import {mkdtemp,readFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const pair=generateKeyPairSync('rsa',{modulusLength:2048});const publicKey=pair.publicKey.export({type:'spki',format:'der'}).toString('base64');
function headers(method,route,body,nonce=randomUUID(),stamp=String(Date.now())){return {'content-type':'application/json','x-relay-key':'local','x-relay-time':stamp,'x-relay-nonce':nonce,'x-relay-signature':sign('RSA-SHA256',Buffer.from(`${stamp}\n${nonce}\n${method}\n${route}\n${createHash('sha256').update(body).digest('hex')}`),pair.privateKey).toString('base64')};}
test('Actual Durable Object runtime: authenticated exclusive arbitration, replay rejection and persistent recovery',async()=>{
  const persist=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-do-'));
  const options=convertV4MiniflareOptions({modules:[{type:'ESModule',path:path.join(here,'../coordinator.mjs')},{type:'ESModule',path:path.join(here,'../policy.mjs')}],modulesRoot:path.join(here,'..'),compatibilityDate:'2026-10-06',resourcePersistencePath:persist,durableObjects:{COORDINATOR:{className:'RelayCoordinator',useSQLite:true,unsafeUniqueKey:'relay-runtime-persistence-test'}},bindings:{PUBLIC_KEYS:JSON.stringify({local:publicKey})}});
  let mf=new Miniflare(options);
  try{
    const call=async(route,p,custom)=>{const method=p===undefined?'GET':'POST',body=p===undefined?'':JSON.stringify(p);return mf.dispatchFetch('http://relay.test'+route,{method,headers:custom??headers(method,route,body),...(p===undefined?{}:{body})});};
    assert.equal((await call('/state',undefined,{})).status,401);
    const policy={timezone:'Europe/London',weekdays:Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}]))};
    assert.equal((await call('/policy',{policy})).status,200);assert.equal((await call('/presence',{home:true,healthy:true})).status,200);
    const claims=await Promise.all(Array.from({length:20},()=>call('/acquire',{})));assert.equal(claims.filter(r=>r.status===200).length,1);assert.equal(claims.filter(r=>r.status===409).length,19);
    const body=JSON.stringify({fence:1}),signed=headers('POST','/heartbeat',body);assert.equal((await call('/heartbeat',{fence:1},signed)).status,200);assert.equal((await call('/heartbeat',{fence:1},signed)).status,409);
    assert.equal((await call('/heartbeat',{fence:999})).status,409);
    assert.equal((await call('/heartbeat',{fence:1},{...headers('POST','/heartbeat',body),'x-relay-time':'NaN'})).status,401);
    await mf.dispose();mf=new Miniflare(options);const recovered=await(await call('/state')).json();assert.equal(recovered.state.lease.fence,1);assert.equal(recovered.state.lease.worker,'local');
  }finally{await mf.dispose();}
});
