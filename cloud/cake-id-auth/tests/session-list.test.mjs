import assert from 'node:assert/strict';
import {test} from 'node:test';
import {DatabaseSync} from 'node:sqlite';
import {build} from 'esbuild';
const built=await build({entryPoints:['src/resource-api.ts'],bundle:true,write:false,platform:'node',format:'esm',banner:{js:"import {createRequire as __require} from 'node:module';const require=__require(process.cwd()+'/package.json');"}});
const {handleResourceApi}=await import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'));
test('signed-in devices contain only the owner active sessions, never expired or foreign sessions',async()=>{
 const db=new DatabaseSync(':memory:');db.exec('CREATE TABLE session(id TEXT,userId TEXT,createdAt TEXT,expiresAt TEXT,userAgent TEXT);');
 const rows=[['active','owner',new Date(Date.now()+60000).toISOString()],['expired','owner',new Date(Date.now()-60000).toISOString()],['invalid-date','owner','invalid'],['foreign','other',new Date(Date.now()+60000).toISOString()]];
 for(const[id,userId,expiresAt]of rows)db.prepare('INSERT INTO session VALUES(?,?,?,?,?)').run(id,userId,'2026-01-01T00:00:00Z',expiresAt,'Synthetic browser');
 const env={DB:{prepare(sql){return {bind(...values){return {all:async()=>({success:true,results:db.prepare(sql).all(...values)})};}};}}};
 const auth={api:{getSession:async()=>({user:{id:'owner'},session:{id:'active'}})}};
 const response=await handleResourceApi(new Request('https://issuer.example.test/api/account/sessions?accountId=other'),env,auth);
 assert.equal(response.status,200);assert.deepEqual((await response.json()).sessions.map(s=>s.sessionId),['active']);db.close();
});
