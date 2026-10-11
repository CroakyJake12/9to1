import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,readFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {git,sha,dirty,preserve,createTaskWorktree,reviewPaths,publish} from '../git.mjs';
test('Dirty and untracked source survive checkpoint and isolated exact-commit reconstruction',async()=>{
  const root=await mkdtemp(path.join(process.env.RELAY_TEST_ROOT??os.tmpdir(),'relay-git-')),repo=path.join(root,'repo');await mkdir(repo);
  await git(repo,['init']);await git(repo,['config','core.autocrlf','false']);await git(repo,['config','user.name','Relay Temporary Test']);await git(repo,['config','user.email','relay-test@example.invalid']);
  await mkdir(path.join(repo,'owned'));await writeFile(path.join(repo,'owned','feature.txt'),'original\n');await git(repo,['add','owned/feature.txt']);await git(repo,['commit','-m','Temporary baseline']);const base=await sha(repo);
  await writeFile(path.join(repo,'owned','feature.txt'),'dirty preserved\n');await writeFile(path.join(repo,'owned','untracked file.txt'),'private unsaved source\n');
  const c=await preserve(repo,path.join(root,'checkpoints'),'checkpoint');assert.equal(c.manifest.files.length,2);assert.equal(await readFile(path.join(c.directory,'source','owned','feature.txt'),'utf8'),'dirty preserved\n');assert.equal(await sha(repo),base);
  const task={id:'owned-task',source:{sha:base},paths:['owned/'],objective:'Implement a durable feature'};const config={repository:repo,worktrees:path.join(root,'worktrees'),remote:'origin'};
  const w=await createTaskWorktree(config,task,1);assert.equal(await readFile(path.join(w.root,'owned','feature.txt'),'utf8'),'original\n');assert.equal((await dirty(repo)).paths.length,2);
  await writeFile(path.join(w.root,'owned','feature.txt'),'actual new feature\n');const bare=path.join(root,'remote.git');await mkdir(bare);await git(bare,['init','--bare']);await git(repo,['remote','add','origin',bare]);
  const source=await publish(config,w,task);const receiving=path.join(root,'receiver');await git(repo,['clone','--no-checkout',bare,receiving]);await git(receiving,['config','core.autocrlf','false']);await git(receiving,['checkout','--detach',source.sha]);
  assert.equal(await sha(receiving),source.sha);assert.equal(await readFile(path.join(receiving,'owned','feature.txt'),'utf8'),'actual new feature\n');assert.equal(await readFile(path.join(repo,'owned','feature.txt'),'utf8'),'dirty preserved\n');
});
test('Unowned and private changes are refused before staging',()=>{assert.throws(()=>reviewPaths(['other/file.cs'],['owned/']));for(const p of ['owned/.env','owned/key.pem','owned/state.private.json','owned/node_modules/x.js'])assert.throws(()=>reviewPaths([p],['owned/']));assert.doesNotThrow(()=>reviewPaths(['owned/feature.cs'],['owned/']));});
