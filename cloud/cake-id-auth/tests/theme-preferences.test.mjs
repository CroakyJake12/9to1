import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { build } from 'esbuild';

const temp = mkdtempSync(path.join(tmpdir(),'9to1-theme-sql-'));
await build({ entryPoints:[new URL('../src/theme-preferences.ts',import.meta.url).pathname], bundle:true, platform:'node', format:'esm', outfile:path.join(temp,'theme.mjs') });
const {readThemePreference,writeThemePreference} = await import(pathToFileURL(path.join(temp,'theme.mjs')));
rmSync(temp,{recursive:true});
const accountA='11111111-1111-4111-8111-111111111111', accountB='22222222-2222-4222-8222-222222222222';
const sessionA='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',sessionB='bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const principal=(accountId=accountA,sessionId=sessionA)=>({accountId,sessionId,accessExpiresAt:Math.floor(Date.now()/1000)+300});
const preference=(id=accountA,revision=1)=>({schemaVersion:1,scope:{scope:1,ownerId:id,organizationId:null},themeName:'Professional',revision,
  accessibility:{reduceMotion:false,highContrast:false,displayScale:1,interfaceFontFamily:null,codeFontFamily:null},appearance:'Dark',
  accent:{mode:2,allAppsColour:null,individualApps:{'app.present':'#FF0000'}},customThemesJson:'[]'});
function fixture() {
  const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
  for(const name of ['0000_cake_guardrails.sql','0001_better_auth.sql','0002_account_theme_preference.sql']) db.exec(readFileSync(new URL(`../migrations/${name}`,import.meta.url),'utf8'));
  for(const [id,sid] of [[accountA,sessionA],[accountB,sessionB]]) {
    db.prepare('INSERT INTO user(id,name,email,emailVerified,createdAt,updatedAt) VALUES(?,?,?,?,?,?)').run(id,'Test',id+'@example.test',1,'2026-01-01','2026-01-01');
    db.prepare("INSERT INTO session(id,token,expiresAt,createdAt,updatedAt,userId) VALUES(?,?,datetime('now','+1 hour'),?,?,?)").run(sid,sid,'2026-01-01','2026-01-01',id);
  }
  let beforeCommit;
  const env={DB:{prepare(sql){let values;return {bind(...items){values=items;return this;},async first(){if(sql.startsWith('INSERT INTO accountThemePreference'))beforeCommit?.();return db.prepare(sql).get(...values)??null;}};}}};
  return {env,db,setBeforeCommit(callback){beforeCommit=callback;},close(){db.close();}};
}
test('account theme first save, exact CAS, custom and accent persistence, and account isolation',async()=>{
  const f=fixture();try {
    assert.deepEqual(await (await readThemePreference(f.env,principal())).json(),{preference:null});
    let reply=await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:preference()});assert.equal(reply.status,200);
    assert.deepEqual((await reply.json()).preference,preference());
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:preference()})).status,409);
    assert.deepEqual(await (await readThemePreference(f.env,principal(accountB,sessionB))).json(),{preference:null});
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:preference(accountB)})).status,400);
    const second=preference(accountA,2);second.accent.individualApps={};second.customThemesJson='[]';
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:1,preference:second})).status,200);
    assert.deepEqual((await (await readThemePreference(f.env,principal())).json()).preference.accent.individualApps,{});
  }finally{f.close();}
});
test('session revocation between preliminary reads and SQL commit refuses mutation',async()=>{
  const f=fixture();try {
    f.setBeforeCommit(()=>f.db.prepare('DELETE FROM session WHERE id=?').run(sessionA));
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:preference()})).status,401);
    assert.equal(f.db.prepare('SELECT COUNT(*) n FROM accountThemePreference').get().n,0);
  }finally{f.close();}
});
test('token expiry and expired or foreign sessions cannot read or commit',async()=>{
  const f=fixture();try {
    for(const actor of [{...principal(),accessExpiresAt:1},principal(accountA,sessionB)]) {
      assert.equal((await readThemePreference(f.env,actor)).status,401);
      assert.equal((await writeThemePreference(f.env,actor,{expectedRevision:0,preference:preference()})).status,401);
    }
    f.db.prepare("UPDATE session SET expiresAt='2000-01-01' WHERE id=?").run(sessionA);
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:preference()})).status,401);
  }finally{f.close();}
});
test('future envelope/accessibility fields survive while explicit app map removals take effect',async()=>{
  const f=fixture();try {
    const first=preference();first.future={retained:true};first.accessibility.futureAccess='keep';
    await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:first});
    const second=preference(accountA,2);second.accent.individualApps={};
    const saved=(await (await writeThemePreference(f.env,principal(),{expectedRevision:1,preference:second})).json()).preference;
    assert.deepEqual(saved.future,{retained:true});assert.equal(saved.accessibility.futureAccess,'keep');assert.deepEqual(saved.accent.individualApps,{});
  }finally{f.close();}
});
test('concurrent exact revision commits have only one winner',async()=>{
  const f=fixture();try {
    const replies=await Promise.all([writeThemePreference(f.env,principal(),{expectedRevision:0,preference:preference()}),writeThemePreference(f.env,principal(),{expectedRevision:0,preference:{...preference(),appearance:'Bright'}})]);
    assert.deepEqual(replies.map(value=>value.status).sort(),[200,409]);
    assert.equal(f.db.prepare('SELECT revision FROM accountThemePreference').get().revision,1);
  }finally{f.close();}
});
test('invalid custom library and unsupported stored schema are preserved, not overwritten',async()=>{
  const f=fixture();try {
    const bad=preference();bad.customThemesJson='[{"url":"https://example.test/executable"}]';
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:0,preference:bad})).status,400);
    const stored={...preference(),schemaVersion:2};
    f.db.prepare('INSERT INTO accountThemePreference VALUES(?,?,?,?)').run(accountA,1,JSON.stringify(stored),'2026-01-01');
    assert.equal((await writeThemePreference(f.env,principal(),{expectedRevision:1,preference:preference(accountA,2)})).status,409);
    assert.equal((await readThemePreference(f.env,principal())).status,409);
    assert.equal(JSON.parse(f.db.prepare('SELECT preferenceJson FROM accountThemePreference').get().preferenceJson).schemaVersion,2);
  }finally{f.close();}
});
