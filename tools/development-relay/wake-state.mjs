import {readFile} from 'node:fs/promises';
import {DatabaseSync} from 'node:sqlite';
import {selectShift} from './policy.mjs';
const c=JSON.parse(await readFile(process.argv[2],'utf8'));const db=new DatabaseSync(c.state_db,{readOnly:true});const r=db.prepare("SELECT value FROM state WHERE key='presence'").get();db.close();const p=r?JSON.parse(r.value):{home:false,paused:false};console.log(JSON.stringify({...p,permitted:selectShift(c.schedule,{...p,healthy:true}).shift==='local'}));
