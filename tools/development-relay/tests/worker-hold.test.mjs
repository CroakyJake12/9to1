import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {DatabaseSync} from 'node:sqlite';
import {readWorkerReport} from '../worker-log.mjs';
import {queueSteering} from '../worker-view.mjs';
import {operationalState} from '../dashboard-model.mjs';

test('A verified human hold overrides idle supervision, preserves the session and blocks steering from resuming the worker',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-hold-')),report={at:new Date().toISOString(),goal:{status:'ACTIVE'},workers:[{worker:'strata',status:'IDLE'},{worker:'opencode',status:'WORKING',pid:2,task_id:'compiler'}],steering:[]};
 await writeFile(path.join(root,'worker-status.private.json'),JSON.stringify(report));await writeFile(path.join(root,'autonomous-worker-plan.private.json'),JSON.stringify({tasks:[],goal:{status:'ON_HOLD'},worker_holds:{strata:{status:'ON_HOLD',process_stopped:true,task_id:'preserved',session_id:'exact-session',worktree_id:'preserved-tree',title:'Shared CUI refactor',check_at:'2026-10-12T12:00:00Z',release_policy:'Human release or conditional check'}}}));
 const db=new DatabaseSync(path.join(root,'supervisor.db'));db.exec('CREATE TABLE steering(id TEXT,worker TEXT,task_id TEXT,message TEXT,interrupt INTEGER,status TEXT,at TEXT)');db.close();
 const held=await readWorkerReport(root);assert.equal(held.goal.status,'ON_HOLD');assert.equal(held.workers[0].status,'ON_HOLD');assert.equal(held.workers[0].session_id,'exact-session');assert.equal(held.workers[0].pid,null);assert.equal(held.workers[1].pid,2);assert.equal(operationalState({tasks:[],worker_supervision:held}).workers[0].state,'On hold');
 await assert.rejects(()=>queueSteering(root,{worker:'strata',message:'New direction',interrupt:true}),/on hold/);const saved=await queueSteering(root,{worker:'strata',message:'New direction',interrupt:false});assert.match(saved.message,/hold remains active/);const check=new DatabaseSync(path.join(root,'supervisor.db'),{readOnly:true});assert.equal(check.prepare('SELECT COUNT(*) AS count FROM steering').get().count,1);assert.equal(check.prepare('SELECT status FROM steering').get().status,'PENDING');check.close();
});
