import {DatabaseSync} from 'node:sqlite';
import {open} from 'node:fs/promises';
import path from 'node:path';
export function openCodeSession(privateRoot,root){let db;try{db=new DatabaseSync(path.join(privateRoot,'opencode-isolated/data/opencode/opencode.db'),{readOnly:true});return db.prepare('SELECT id,title,time_updated FROM session WHERE directory=? OR directory=? ORDER BY time_updated DESC LIMIT 1').get(root,root.replace(/\\/g,'/'));}finally{db?.close();}}
export function readActive(file){let db;try{db=new DatabaseSync(file,{readOnly:true});return JSON.parse(db.prepare('SELECT value FROM state WHERE key=?').get('active')?.value??'null');}catch(e){if(/unable to open|no such table/.test(e.message))return null;throw e;}finally{db?.close();}}
export async function logTail(file,max=48000){const handle=await open(file,'r');try{const info=await handle.stat(),size=Math.min(info.size,max),buffer=Buffer.alloc(size);await handle.read(buffer,0,size,info.size-size);return buffer.toString('utf8');}finally{await handle.close();}}
export function redactOutput(text){return text.replace(/-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?-----END [^-]*PRIVATE KEY-----/g,'[private key redacted]').replace(/\bBearer\s+[A-Za-z0-9._-]+/gi,'Bearer [redacted]').replace(/\bsk-[A-Za-z0-9_-]{12,}/g,'[credential redacted]');}
