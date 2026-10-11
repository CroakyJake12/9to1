import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, readFile, writeFile, copyFile, lstat } from 'node:fs/promises';
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
  const dest=path.resolve(config.worktrees,`${task.id}-${fence}`),branch=`relay/${task.id}-${fence}`;
  await mkdir(path.dirname(dest),{recursive:true});await git(config.repository,['worktree','add','-b',branch,dest,task.source.sha]);
  return {root:dest,branch,base:task.source.sha};
}
export function reviewPaths(paths,owned) {
  for(const p of paths){
    const lower=p.toLowerCase();if(!owned.some(o=>(lower+'/').startsWith(o.toLowerCase())))throw new Error(`Unowned change: ${p}`);
    if(/(^|\/)(\.env(?:\..*)?|node_modules|bin|obj|\.wrangler|private|logs)(\/|$)|\.(pem|key|pfx|p12|db)(?:-|$)|\.private\.json$/.test(lower))throw new Error(`Private or generated change requires manual review: ${p}`);
  }
}
export async function publish(config,worktree,task) {
  const d=await dirty(worktree.root);reviewPaths(d.paths,task.paths);
  if(!d.paths.length&&!task.verification_only)throw new Error('No implementation source changes; task completion is unverified');
  if(d.paths.length){await git(worktree.root,['add','--',...d.paths]);await git(worktree.root,['diff','--cached','--check']);await git(worktree.root,['commit','-m',`Relay ${task.id}: ${task.objective.replace(/[\r\n]/g,' ').slice(0,120)}`]);}
  const commit=await sha(worktree.root);await git(worktree.root,['push',config.remote??'origin',`HEAD:refs/heads/${worktree.branch}`]);
  const remote=(await git(worktree.root,['ls-remote',config.remote??'origin',`refs/heads/${worktree.branch}`])).trim().split(/\s/)[0];
  if(remote!==commit)throw new Error('Remote source identity differs');const artifact_hashes={},deleted_paths=[];
  for(const file of d.paths){try{artifact_hashes[file]=await blobHash(worktree.root,commit,file);}catch(e){if((await git(worktree.root,['ls-tree','--name-only',commit,'--',file])).trim())throw e;deleted_paths.push(file);}}
  return {repository:'CroakyJake12/9to1',branch:worktree.branch,sha:commit,artifact_hashes,deleted_paths};
}
