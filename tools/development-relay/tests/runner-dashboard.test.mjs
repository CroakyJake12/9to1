import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {LocalRunner} from '../runner.mjs';
import {registerCapture,listReviews} from '../reviews.mjs';
test('The primary runner serves app reviews and live activity after a fresh startup, with authenticated persisted decisions',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-primary-ui-')),file=path.join(root,'fixture.png');await writeFile(file,Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1l8AAAAASUVORK5CYII=','base64'));const id=await registerCapture(root,{title:'App fixture',app:'Fixture',part:1,platform:'web',source_sha:'c'.repeat(40),verification:{rendered:true,functional_checks:true}},file);
 const rule={local_start:'07:30',local_end:'22:00'},weekdays=Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(day=>[day,rule])),runner=new LocalRunner({state_db:path.join(root,'state.db'),dashboard_port:18944,schedule:{timezone:'Europe/London',weekdays}});
 // No coordinator/inference side effects: exercise the actual fresh HTTP/persistence flow.
 runner.tick=async()=>{};runner.renew=async()=>{};await runner.start();const url='http://127.0.0.1:18944';
 try{const html=await(await fetch(url)).text(),csrf=html.match(/name="relay-csrf" content="([^"]+)"/)[1],state=await(await fetch(url+'/status')).json();assert.ok(Array.isArray(state.source_activity));assert.ok(Array.isArray(state.live_source));const reviews=await(await fetch(url+'/reviews')).json();assert.equal(reviews.captures[0].id,id);assert.equal((await fetch(url+'/review-image/'+id)).headers.get('content-type'),'image/png');const body=JSON.stringify({capture_id:id,decision:'ACCEPTED',comment:'Fixture review only'});assert.equal((await fetch(url+'/review',{method:'POST',body})).status,403);assert.equal((await fetch(url+'/review',{method:'POST',headers:{origin:url,'x-relay-csrf':csrf,'content-type':'application/json'},body})).status,200);assert.equal(listReviews(root)[0].decision,'ACCEPTED');assert.equal(listReviews(root)[0].acceptance_scope,'INTERFACE_REVIEW');}
 finally{await runner.stop();}
});
