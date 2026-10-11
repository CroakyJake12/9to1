import {readFile,writeFile,mkdir,unlink,lstat,open} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {git,dirty,sha,reviewPaths,blobHash} from './git.mjs';

async function blob(root,commit,file){try{return (await git(root,['rev-parse',commit+':'+file])).trim();}catch{return null;}}
export async function publishMain(config,tree,task){
 const main=config.repository;if((await git(main,['branch','--show-current'])).trim()!=='main'||(await dirty(main)).paths.length)throw new Error('Canonical main must be clean before integration');
 const lockPath=config.main_lock_file??path.join(path.dirname(config.state_db),'main-integration.lock');await mkdir(path.dirname(lockPath),{recursive:true});const lock=await open(lockPath,'wx');
 try{await lock.writeFile(JSON.stringify({pid:process.pid,task:task.id,at:new Date().toISOString()}));const changes=await dirty(tree.root);reviewPaths(changes.paths,task.paths);if(!changes.paths.length)throw new Error('No implementation changes to consolidate');
  const current=await sha(main);for(const file of changes.paths)if(await blob(main,current,file)!==await blob(tree.root,tree.base,file))throw new Error('Newer main implementation conflicts with '+file+'; source preserved for reconciliation');
  const artifacts={},deleted=[];for(const file of changes.paths){const from=path.resolve(tree.root,file),to=path.resolve(main,file);if(!to.startsWith(path.resolve(main)+path.sep))throw new Error('Main integration escaped');let info;try{info=await lstat(from);}catch(e){if(e.code!=='ENOENT')throw e;}
   if(!info){await unlink(to);deleted.push(file);continue;}if(info.isSymbolicLink())throw new Error('Source link requires review');const bytes=await readFile(from);await mkdir(path.dirname(to),{recursive:true});await writeFile(to,bytes);artifacts[file]=createHash('sha256').update(bytes).digest('hex');}
  await git(main,['add','--',...changes.paths]);await git(main,['diff','--cached','--check']);await git(main,['commit','-m','Implement '+task.id+' on canonical main']);const commit=await sha(main);for(const file of changes.paths.filter(f=>!deleted.includes(f)))artifacts[file]=await blobHash(main,commit,file);await git(main,['push',config.remote??'origin','HEAD:refs/heads/main']);
  const remote=(await git(main,['ls-remote',config.remote??'origin','refs/heads/main'])).split(/\s/)[0];if(remote!==commit)throw new Error('Main source publication was not independently observed');
  return {repository:'CroakyJake12/9to1',branch:'main',sha:commit,files:changes.paths,deleted_paths:deleted,artifact_hashes:artifacts,hash_scope:'committed Git blob bytes',remote_verified:true};
 }finally{await lock.close();await unlink(lockPath);}
}
