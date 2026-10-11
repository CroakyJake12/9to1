import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {DatabaseSync} from 'node:sqlite';
import {git} from '../git.mjs';
import {sourceSnapshot,recordSourceMilestones} from '../source-activity.mjs';
import {openCodeSession} from '../worker-log.mjs';
test('Source activity counts untracked edits, hashes actual bytes and persists deduplicated milestones separately from tool activity',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-source-'));await git(root,['init']);await git(root,['config','core.autocrlf','false']);await git(root,['config','user.name','Fixture']);await git(root,['config','user.email','fixture@example.test']);await writeFile(path.join(root,'source.cs'),'original');await git(root,['add','source.cs']);await git(root,['commit','-m','Fixture']);await writeFile(path.join(root,'source.cs'),'updated');await writeFile(path.join(root,'tests.cs'),'new test');
 const before=await sourceSnapshot(root);assert.equal(before.files.length,2);assert.ok(before.last_source_edit_at);await writeFile(path.join(root,'tests.cs'),'modified untracked test');const after=await sourceSnapshot(root);assert.notEqual(before.snapshot_sha,after.snapshot_sha);
 const view={worker:'opencode',task_id:'owned',work_output:after,entries:[{at:new Date().toISOString(),kind:'Output',text:'Implemented canonical declaration validation.'},{at:new Date().toISOString(),kind:'Tool',text:'read source.cs'}]};const events=recordSourceMilestones(root,[view]);assert.equal(events.filter(e=>e.event==='SOURCE_EDIT').length,2);assert.equal(events.filter(e=>e.event==='PUBLIC_UPDATE').length,1);assert.equal(recordSourceMilestones(root,[view]).length,3);
});
test('OpenCode recovery finds the exact worktree session even when the last log is a failed test',async()=>{
 const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-session-'));await mkdir(path.join(root,'opencode-isolated/data/opencode'),{recursive:true});const db=new DatabaseSync(path.join(root,'opencode-isolated/data/opencode/opencode.db'));db.exec('CREATE TABLE session(id TEXT,title TEXT,directory TEXT,time_updated INTEGER)');db.prepare('INSERT INTO session VALUES (?,?,?,?)').run('ses_owned123','owned','C:/owned/worktree',100);db.prepare('INSERT INTO session VALUES (?,?,?,?)').run('ses_other123','other','C:/other/worktree',200);db.close();assert.equal(openCodeSession(root,'C:\\owned\\worktree').id,'ses_owned123');assert.equal(openCodeSession(root,'C:/unknown'),undefined);
});
