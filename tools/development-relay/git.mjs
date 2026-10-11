import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, readFile, writeFile, copyFile, lstat, unlink } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import {normalizePaths} from './policy.mjs';
const run=promisify(execFile);
export async function git(root,args){return (await run('git',['-c',`safe.directory=${root}`,'-C',root,...args],{maxBuffer:20*1024*1024,windowsHide:true})).stdout;}
export const sha=async root=>(await git(root,['rev-parse','HEAD'])).trim();
export async function blobHash(root,commit,file){
  normalizePaths([file]);if(!/^[0-9a-f]{40}$/.test(commit))throw new Error('Exact artifact commit required');
  if((await git(root,['cat-file','-t',commit+':'+file])).trim()!=='blob')throw new Error('Artifact is not a committed file');
  const {stdout}=await run('git',['-c',`safe.directory=${root}`,'-C',root,'show',commit+':'+file],{encoding:'buffer',maxBuffer:64*1024*1024,windowsHide:true});return createHash('sha256').update(stdout).digest('hex');
}
export async function verifyArtifactHashes(root,commit,artifacts={}){
  if(await sha(root)!==commit||(await dirty(root)).paths.length)throw new Error('Receiving source is not the exact clean commit');
  if(!artifacts||Array.isArray(artifacts)||typeof artifacts!=='object'||Object.keys(artifacts).length>200)throw new Error('Invalid declared artifact hashes');
  const verified={};for(const [file,digest] of Object.entries(artifacts)){if(!/^[0-9a-f]{64}$/.test(digest))throw new Error('Invalid artifact digest');const actual=await blobHash(root,commit,file);if(actual!==digest)throw new Error('Receiving artifact hash differs: '+file);verified[file]=actual;}return verified;
}
export async function dirty(root) {
  const changed=(await git(root,['diff','--name-only','-z','HEAD'])).split('\0').filter(Boolean);
  const untracked=(await git(root,['ls-files','--others','--exclude-standard','-z'])).split('\0').filter(Boolean);
  return {changed,untracked,paths:[...new Set([...changed,...untracked])]};
}
export async function preserve(root,checkpointRoot,id) {
  const d=await dirty(root),dest=path.resolve(checkpointRoot,id);await mkdir(dest,{recursive:true});
  const manifest={head:await sha(root),branch:(await git(root,['branch','--show-current'])).trim(),...d,files:[],created_at:new Date().toISOString()};
  await writeFile(path.join(dest,'changes.patch'),await git(root,['diff','--binary','HEAD']));
  for(const p of d.paths){
    const source=path.resolve(root,p);if(!source.startsWith(path.resolve(root)+path.sep))throw new Error('Checkpoint path escapes root');
    let st;try{st=await lstat(source);}catch(e){if(e.code==='ENOENT'){manifest.files.push({path:p,deleted:true});continue;}throw e;}
    if(st.isSymbolicLink())throw new Error('Checkpoint symlink requires explicit review');
    const target=path.join(dest,'source',p);await mkdir(path.dirname(target),{recursive:true});await copyFile(source,target);
    const hash=createHash('sha256').update(await readFile(target)).digest('hex');manifest.files.push({path:p,sha256:hash});
  }
  await writeFile(path.join(dest,'manifest.json'),JSON.stringify(manifest,null,2));return {directory:dest,manifest};
}
export async function createTaskWorktree(config,task,fence) {
  if(await sha(config.repository)!==task.source.sha && !(await git(config.repository,['cat-file','-t',task.source.sha])).includes('commit'))throw new Error('Exact source commit unavailable');
  if((task.codex_session_id||task.opencode_session_id)&&!task.resume_worktree_id)throw new Error('A session continuation requires its verified worktree locator');
  if(task.resume_worktree_id){
    const session=task.codex_session_id?.match(/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i)||task.opencode_session_id?.match(/^ses_[a-zA-Z0-9]{8,80}$/);
    if(!(task.local_only||config.branch_strategy==='main')||!session||!task.resume_worktree_id.match(/^[a-z0-9][a-z0-9-]{0,99}$/)||!task.resume_branch)throw new Error('Verified continuation scope required');
    const dest=path.resolve(config.worktrees,task.resume_worktree_id);
    if((await lstat(dest)).isSymbolicLink())throw new Error('Continuation worktree must not redirect through a link');
    const branch=(await git(dest,['branch','--show-current'])).trim();
    const detachedMain=config.branch_strategy==='main'&&task.resume_branch==='main'&&!branch&&task.source.branch==='main';
    if(!detachedMain&&branch!==task.resume_branch||await sha(dest)!==task.source.sha)throw new Error('Continuation worktree source changed; preserve and review it first');
    const changes=await dirty(dest);if(changes.paths.length){
      if(!task.continuation_checkpoint_id?.match(/^[a-zA-Z0-9-]{1,100}$/))throw new Error('Continuation worktree source changed; preserve and review it first');
      const checkpoint=path.join(config.checkpoints,task.continuation_checkpoint_id);if((await lstat(checkpoint)).isSymbolicLink())throw new Error('Checkpoint link refused');
      const manifest=JSON.parse(await readFile(path.join(checkpoint,'manifest.json'),'utf8'));reviewPaths(changes.paths,task.paths);
      if(manifest.head!==task.source.sha||manifest.branch!==branch||JSON.stringify([...changes.paths].sort())!==JSON.stringify([...manifest.paths].sort()))throw new Error('Continuation differs from the preserved checkpoint');
      for(const file of manifest.files){normalizePaths([file.path]);const target=path.resolve(dest,file.path);if(!target.startsWith(dest+path.sep))throw new Error('Continuation path escaped');if(file.deleted){try{await lstat(target);throw new Error('Deleted source reappeared');}catch(e){if(e.code!=='ENOENT')throw e;}}else{if((await lstat(target)).isSymbolicLink()||createHash('sha256').update(await readFile(target)).digest('hex')!==file.sha256)throw new Error('Continuation source bytes changed');}}
    }
    return {root:dest,branch:detachedMain?'main':branch,base:task.source.sha};
  }
  const dest=path.resolve(config.worktrees,`${task.id}-${fence}`),branch=`relay/${task.id}-${fence}`;
  await mkdir(path.dirname(dest),{recursive:true});if(config.branch_strategy==='main'){if(task.source.branch!=='main')throw new Error('Workers must use canonical main');await git(config.repository,['worktree','add','--detach',dest,task.source.sha]);return {root:dest,branch:'main',base:task.source.sha};}await git(config.repository,['worktree','add','-b',branch,dest,task.source.sha]);
  return {root:dest,branch,base:task.source.sha};
}
export async function restoreOwnedCheckpoint(config,worktree,task,reference){
 const root=path.resolve(config.checkpoints),checkpoint=path.isAbsolute(reference)?path.resolve(reference):path.resolve(root,reference);
 if(!checkpoint.toLowerCase().startsWith(root.toLowerCase()+path.sep)||(await lstat(checkpoint)).isSymbolicLink())throw new Error('Known private checkpoint required');
 const manifest=JSON.parse(await readFile(path.join(checkpoint,'manifest.json'),'utf8'));if(manifest.head!==task.source.sha||await sha(worktree.root)!==task.source.sha||(await dirty(worktree.root)).paths.length)throw new Error('Checkpoint base or destination differs');
 reviewPaths(manifest.paths,task.paths);
 const checked=[];for(const file of manifest.files){normalizePaths([file.path]);const source=path.resolve(checkpoint,'source',file.path),destination=path.resolve(worktree.root,file.path);if(!destination.startsWith(path.resolve(worktree.root)+path.sep))throw new Error('Checkpoint target escaped');if(!file.deleted){if((await lstat(source)).isSymbolicLink())throw new Error('Checkpoint source is a link');const bytes=await readFile(source);if(createHash('sha256').update(bytes).digest('hex')!==file.sha256)throw new Error('Checkpoint bytes changed');checked.push({file,destination,bytes});}else checked.push({file,destination});}
 // Verify every byte before applying the delta to a fresh isolated worktree.
 for(const {file,destination,bytes}of checked){if(file.deleted){try{await unlink(destination);}catch(e){if(e.code!=='ENOENT')throw e;}}else{await mkdir(path.dirname(destination),{recursive:true});await writeFile(destination,bytes);}}
 return manifest;
}
export async function verifyEmbeddedCheckpoint(config,worktree,task,reference){
 const root=path.resolve(config.checkpoints),checkpoint=path.isAbsolute(reference)?path.resolve(reference):path.resolve(root,reference);if(!checkpoint.toLowerCase().startsWith(root.toLowerCase()+path.sep)||(await lstat(checkpoint)).isSymbolicLink())throw new Error('Known private predecessor required');
 const manifest=JSON.parse(await readFile(path.join(checkpoint,'manifest.json'),'utf8')),artifacts=task.source.artifact_hashes??{},deleted=task.source.deleted_paths??[];
 if(!manifest.files.length||manifest.files.some(file=>file.deleted?!deleted.includes(file.path):!artifacts[file.path]))throw new Error('Every predecessor change requires a committed source receipt');
 await verifyArtifactHashes(worktree.root,task.source.sha,artifacts);
 const tracked=new Set((await git(worktree.root,['ls-files','-z'])).split('\0'));for(const file of manifest.files.filter(f=>f.deleted))if(tracked.has(file.path))throw new Error('Deleted predecessor source reappeared');
 return manifest;
}
export async function verifyCheckpointFiles(root,manifest){
 const changed=await dirty(root);if(changed.paths.some(file=>!manifest.paths.includes(file)))throw new Error('Source changed after its verification checkpoint');
 for(const file of manifest.files){normalizePaths([file.path]);const target=path.resolve(root,file.path);if(!target.startsWith(path.resolve(root)+path.sep))throw new Error('Predecessor source escaped');if(file.deleted){try{await lstat(target);throw new Error('Deleted checkpoint source reappeared');}catch(e){if(e.code!=='ENOENT')throw e;}}else if((await lstat(target)).isSymbolicLink()||createHash('sha256').update(await readFile(target)).digest('hex')!==file.sha256)throw new Error('Source bytes changed after verification');}
}
export function reviewPaths(paths,owned) {
  for(const p of paths){
    const lower=p.toLowerCase();if(!owned.some(o=>(lower+'/').startsWith(o.toLowerCase())))throw new Error(`Unowned change: ${p}`);
    if(/(^|\/)(\.env(?:\..*)?|node_modules|bin|obj|\.wrangler|private|logs)(\/|$)|\.(pem|key|pfx|p12|db)(?:-|$)|\.private\.json$/.test(lower))throw new Error(`Private or generated change requires manual review: ${p}`);
  }
}
export async function publish(config,worktree,task) {
  if(config.branch_strategy==='main'){const {publishMain}=await import('./main-source.mjs');return await publishMain(config,worktree,task);}
  const d=await dirty(worktree.root);reviewPaths(d.paths,task.paths);
  if(!d.paths.length&&!task.verification_only)throw new Error('No implementation source changes; task completion is unverified');
  if(d.paths.length){await git(worktree.root,['add','--',...d.paths]);await git(worktree.root,['diff','--cached','--check']);await git(worktree.root,['commit','-m',`Relay ${task.id}: ${task.objective.replace(/[\r\n]/g,' ').slice(0,120)}`]);}
  const commit=await sha(worktree.root);await git(worktree.root,['push',config.remote??'origin',`HEAD:refs/heads/${worktree.branch}`]);
  const remote=(await git(worktree.root,['ls-remote',config.remote??'origin',`refs/heads/${worktree.branch}`])).trim().split(/\s/)[0];
  if(remote!==commit)throw new Error('Remote source identity differs');const artifact_hashes={},deleted_paths=[];
  for(const file of d.paths){try{artifact_hashes[file]=await blobHash(worktree.root,commit,file);}catch(e){if((await git(worktree.root,['ls-tree','--name-only',commit,'--',file])).trim())throw e;deleted_paths.push(file);}}
  return {repository:'CroakyJake12/9to1',branch:worktree.branch,sha:commit,artifact_hashes,deleted_paths};
}
