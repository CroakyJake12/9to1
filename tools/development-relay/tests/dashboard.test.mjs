import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {LocalRunner} from '../runner.mjs';
test('Actual dashboard HTTP controls persist, fail closed, prevent CSRF and reject duplicate listener',async()=>{
  const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-ui-'));
  const config={state_db:path.join(root,'state.db'),dashboard_port:18891,checkpoints:path.join(root,'checkpoints'),logs:path.join(root,'logs'),schedule:{timezone:'Europe/London',weekdays:Object.fromEntries(['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'].map(d=>[d,{local_start:'00:00',local_end:'23:59'}]))}};
  let runner=await new LocalRunner(config).start();const base='http://127.0.0.1:18891';
  try{
    const html=await(await fetch(base)).text();const csrf=html.match(/const csrf="([^"]+)"/)[1];
    const control=async a=>fetch(base+'/control/'+a,{method:'POST',headers:{origin:base,'x-relay-csrf':csrf}});
    assert.equal((await fetch(base+'/control/home',{method:'POST'})).status,403);
    for(const [action,property,expected] of [['home','home',true],['away','home',false],['pause','paused',true],['resume','paused',false]]){assert.equal((await control(action)).status,200);assert.equal((await(await fetch(base+'/status')).json()).presence[property],expected);}
    assert.equal((await control('checkpoint')).status,200);assert.equal((await control('handover')).status,409);assert.equal(runner.active,null);
    const duplicate=new LocalRunner({...config,state_db:path.join(root,'other.db')});await assert.rejects(()=>duplicate.start(),{code:'EADDRINUSE'});duplicate.db.close();
    await control('home');await runner.stop();runner=await new LocalRunner(config).start();assert.equal(runner.presence.home,true);assert.equal(runner.remote,null);
  }finally{await runner.stop();}
});
