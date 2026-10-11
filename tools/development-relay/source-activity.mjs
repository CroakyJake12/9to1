import {readFile,lstat} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import {dirty,git,sha,reviewPaths} from './git.mjs';
import {redactOutput} from './worker-log.mjs';

// This measures authored bytes, separately from process/tool activity and acceptance.
export async function sourceSnapshot(root,owned=null){
 const changed=await dirty(root),files=[],newSource=[];if(owned)reviewPaths(changed.paths,owned);
 for(const name of changed.paths){const file=path.resolve(root,name);if(!file.startsWith(path.resolve(root)+path.sep))throw new Error('Source path escaped');let info;try{info=await lstat(file);}catch(e){if(e.code==='ENOENT'){files.push({path:name,deleted:true});continue;}throw e;}if(info.isSymbolicLink()||!info.isFile()||info.size>20*1024*1024)throw new Error('Source file requires review');const bytes=await readFile(file);files.push({path:name,sha256:createHash('sha256').update(bytes).digest('hex'),modified_at:info.mtime.toISOString(),bytes:bytes.length});if(changed.untracked.includes(name)&&!bytes.includes(0))newSource.push('New source file: '+name+'\n'+bytes.toString('utf8').slice(0,16000));}
 const diff=redactOutput([await git(root,['diff','--no-ext-diff','--unified=3','HEAD']),...newSource].join('\n\n')).slice(0,48000),snapshot_sha=createHash('sha256').update(JSON.stringify(files.map(f=>({path:f.path,sha256:f.sha256,deleted:f.deleted})))).digest('hex');
 return {base_sha:await sha(root),files:files.map(f=>f.path),file_receipts:files,diff,snapshot_sha,last_source_edit_at:files.filter(f=>f.modified_at).sort((a,b)=>Date.parse(b.modified_at)-Date.parse(a.modified_at))[0]?.modified_at??null,preview_status:'CAPTURE_PENDING'};
}
export function recordSourceMilestones(privateRoot,views){
 const db=new DatabaseSync(path.join(privateRoot,'source-activity.db'));try{
  db.exec('CREATE TABLE IF NOT EXISTS source_events(id TEXT PRIMARY KEY,at TEXT,task TEXT,worker TEXT,path TEXT,hash TEXT);CREATE TABLE IF NOT EXISTS public_updates(id TEXT PRIMARY KEY,at TEXT,task TEXT,worker TEXT,summary TEXT)');
  for(const view of views){if(!view.task_id)continue;for(const file of view.work_output?.file_receipts??[]){if(!file.sha256||!file.modified_at)continue;const id=createHash('sha256').update(view.task_id+'\0'+file.path+'\0'+file.sha256).digest('hex');db.prepare('INSERT OR IGNORE INTO source_events VALUES (?,?,?,?,?,?)').run(id,file.modified_at,view.task_id,view.worker,file.path,file.sha256);}for(const entry of view.entries??[]){if(entry.kind!=='Output'||!entry.at||!entry.text?.trim())continue;const summary=entry.text.slice(0,1200),id=createHash('sha256').update(view.task_id+'\0'+summary).digest('hex');db.prepare('INSERT OR IGNORE INTO public_updates VALUES (?,?,?,?,?)').run(id,entry.at,view.task_id,view.worker,summary);}}
  return [...db.prepare("SELECT at,task,worker,path,hash,'SOURCE_EDIT' AS event FROM source_events ORDER BY at DESC LIMIT 30").all(),...db.prepare("SELECT at,task,worker,summary,'PUBLIC_UPDATE' AS event FROM public_updates ORDER BY at DESC LIMIT 15").all()].sort((a,b)=>Date.parse(b.at)-Date.parse(a.at)).slice(0,40);
 }finally{db.close();}
}
