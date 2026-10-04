// Actual Chromium IndexedDB persistence controls. No substitute IDB implementation or service issuer.
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require('/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fixtureRaw = await fs.readFile(process.argv[2]);
const fixture = JSON.parse(fixtureRaw);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const evidence = process.argv[3]; await fs.mkdir(evidence, { recursive: true });
const modulePath=process.argv[4]?path.resolve(process.argv[4]):new URL('../../../Services/notes-indexeddb.js', import.meta.url);
const source = await fs.readFile(modulePath);
const server = http.createServer((request, response) => {
  if (request.url === '/notes.js') { response.setHeader('Content-Type','text/javascript'); response.end(source); }
  else { response.setHeader('Content-Type','text/html'); response.end('<!doctype html><title>Actual IndexedDB adapter controls</title><script type="module">import {createNotesModule} from "/notes.js"; window.createNotesModule=createNotesModule; window.module=createNotesModule(); window.invoke=(action,args)=>window.module.invoke(crypto.randomUUID(),action,JSON.stringify(args)).then(JSON.parse);</script>'); }
});
await new Promise(resolve => server.listen(18763, '127.0.0.1', resolve));
const origin = 'http://127.0.0.1:18763';
const profile = path.join(evidence, 'chromium-profile');
let context, chromiumVersion, fatal;
const integrityErrors=[];let integrityErrorCount=0;
const integrity = value => { integrityErrorCount++; if(integrityErrors.length<50)integrityErrors.push(String(value)); };
const runnerBefore=await fs.readFile(new URL(import.meta.url));
const expectedNames=[...runnerBefore.toString().matchAll(/await test\('([^']+)'/g)].map(match=>match[1]);
const hashesBefore={module:sha(source),fixture:sha(fixtureRaw),runner:sha(runnerBefore)};
const launch = () => chromium.launchPersistentContext(profile, { executablePath:'/usr/bin/chromium', headless:true, args:['--no-sandbox'], viewport:{width:1000,height:700} });
const observe = page => { page.on('pageerror',error=>integrity('pageerror '+error)); page.on('console',message=>{if(message.type()==='error')integrity('console '+message.text());}); page.on('requestfailed',request=>integrity('requestfailed '+request.url()+' '+JSON.stringify(request.failure()))); return page; };
const pageReady = async context => { const page = observe(await context.newPage()); await page.goto(origin); await page.waitForFunction(() => typeof window.invoke === 'function'); return page; };
const call = (page,action,args) => page.evaluate(([action,args]) => window.invoke(action,args), [action,args]);
const assert = (condition,message) => { if (!condition) throw new Error(message); };
const results=[];
async function test(name, run) { try { await run(); results.push({name,status:'PASS'}); console.log('PASS '+name); } catch(error) { results.push({name,status:'FAIL',error:String(error.stack)}); console.log('FAIL '+name+': '+error.stack); } }
try {
 context = await launch(); chromiumVersion=context.browser().version(); let page = await pageReady(context);
 await test('canonical full document save acknowledges actual transaction and history',async()=> {
  const result=await call(page,'Save',fixture.first); assert(result.ok && result.committed,'no commit');
  const loaded=await call(page,'Load',{documentId:fixture.documentId}); const doc=JSON.parse(loaded.value.documentJson);
  assert(doc.id===fixture.documentId && doc.version===1 && doc.sections[0].id===fixture.sectionId && doc.sections[0].pages[0].blocks[0].id===fixture.blockId,'canonical identity/content mismatch');
  const history=await call(page,'Versions',{documentId:fixture.documentId}); assert(history.value.length===1 && history.value[0].documentJson===loaded.value.documentJson,'document/history not exact');
 });
 await context.close(); context = await launch(); page=await pageReady(context);
 await test('fresh Chromium process and persistent profile reopen exact committed document',async()=> {
  const result=await call(page,'Load',{documentId:fixture.documentId}); assert(result.ok && result.value.documentJson===fixture.first.documentJson,'restart lost canonical document');
  assert((await call(page,'LoadVersion',{documentId:fixture.documentId,versionId:fixture.first.versionId})).value.documentJson===fixture.first.documentJson,'restart lost history');
 });
 await test('two independent tabs race one expected revision: exactly one commit and one CAS conflict',async()=> {
  const other=await pageReady(context); const current=await call(page,'Load',{documentId:fixture.documentId}); const packet={...fixture.second,expectedRecord:JSON.stringify(current.value)};
  const outcomes=await Promise.all([call(page,'Save',packet),call(other,'Save',packet)]);
  assert(outcomes.filter(x=>x.ok&&x.committed).length===1 && outcomes.filter(x=>!x.ok&&x.error.code==='RevisionConflict').length===1,'CAS accepted both or lost winner');
  const after=await call(page,'Load',{documentId:fixture.documentId}); assert(after.value.version==='2' && after.value.documentJson===fixture.second.documentJson,'winner not persisted');
  assert((await call(page,'Versions',{documentId:fixture.documentId})).value.length===2,'history duplicated/missing'); await other.close();
 });
 await test('real history uniqueness error aborts current and history together',async()=> {
  const db='atomic-failure'; await page.evaluate(async ([db,first,second])=> {
   const mod=window.createNotesModule({databaseName:db}); window.atomic=mod; const saved=JSON.parse(await mod.invoke('init','Save',JSON.stringify(first))); if(!saved.ok)throw Error(JSON.stringify(saved));
   const conn=await new Promise((resolve,reject)=>{const r=indexedDB.open(db,1);r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});
   await new Promise((resolve,reject)=>{const tx=conn.transaction('history','readwrite'); const record={documentId:first.documentId,versionId:second.versionId,id:first.documentId,version:first.version,documentJson:first.documentJson,sha256:first.sha256,versionInfoJson:first.versionInfoJson};tx.objectStore('history').add(record);tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});conn.close();
  },[db,fixture.first,fixture.second]);
  const before=await page.evaluate(async id=>JSON.parse(await window.atomic.invoke('load','Load',JSON.stringify({documentId:id}))),fixture.documentId);
  const historyBefore=await page.evaluate(async id=>JSON.parse(await window.atomic.invoke('hb','Versions',JSON.stringify({documentId:id}))),fixture.documentId);
  const failed=await page.evaluate(async packet=>JSON.parse(await window.atomic.invoke('fail','Save',JSON.stringify(packet))),{...fixture.second,expectedRecord:JSON.stringify(before.value)});
  assert(!failed.ok,'constraint failure acknowledged success');
  const after=await page.evaluate(async id=>JSON.parse(await window.atomic.invoke('after','Load',JSON.stringify({documentId:id}))),fixture.documentId);
  assert(after.value.documentJson===before.value.documentJson && after.value.version==='1','partial current commit after history failure');
  const historyAfter=await page.evaluate(async id=>JSON.parse(await window.atomic.invoke('ha','Versions',JSON.stringify({documentId:id}))),fixture.documentId);assert(JSON.stringify(historyAfter.value)===JSON.stringify(historyBefore.value),'history partially changed after abort');
 });
 await test('native request-success cancellation aborts current and history before commit',async()=> {
  const result=await page.evaluate(async packet=> {
   const mod=window.createNotesModule({databaseName:'cancel-active-transaction'}); const put=IDBObjectStore.prototype.put;
   IDBObjectStore.prototype.put=function(...args){const request=put.apply(this,args);if(this.name==='documents')request.addEventListener('success',()=>mod.cancel('active'));return request;};
   let reply;try{reply=JSON.parse(await mod.invoke('active','Save',JSON.stringify(packet)));}finally{IDBObjectStore.prototype.put=put;}
   const loaded=JSON.parse(await mod.invoke('cancel-load','Load',JSON.stringify({documentId:packet.documentId})));const history=JSON.parse(await mod.invoke('cancel-history','Versions',JSON.stringify({documentId:packet.documentId})));return{reply,loaded,history};
  },fixture.first);assert(!result.reply.ok&&result.reply.error.code==='Cancelled'&&result.loaded.value===null&&result.history.value.length===0,'request success became commit or abort left partial state');
 });
 await test('validated recovery saves canonical successor and retains corrupt original',async()=> {
  await page.evaluate(async ([first,recovered])=>{const mod=window.createNotesModule({databaseName:'recovery-save'});window.recovery=mod;const result=JSON.parse(await mod.invoke('first','Save',JSON.stringify(first)));if(!result.ok)throw Error(JSON.stringify(result));
   const db=await new Promise(r=>{const q=indexedDB.open('recovery-save');q.onsuccess=()=>r(q.result);});await new Promise((resolve,reject)=>{const tx=db.transaction('documents','readwrite');tx.objectStore('documents').put(JSON.parse(recovered.expectedRecord));tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});db.close();
  },[fixture.recoveryFirst,fixture.recoverySave]);
  const result=await page.evaluate(async packet=>JSON.parse(await window.recovery.invoke('recovery-save','Save',JSON.stringify(packet))),fixture.recoverySave);assert(result.ok&&result.committed,'recovery save failed');
  const stored=await page.evaluate(async id=>JSON.parse(await window.recovery.invoke('recovered-load','Load',JSON.stringify({documentId:id}))),fixture.recoverySave.documentId);assert(stored.value.version==='2'&&stored.value.documentJson===fixture.recoverySave.documentJson,'recovery invented revision');
  const trash=await page.evaluate(async()=>{const db=await new Promise(r=>{const q=indexedDB.open('recovery-save');q.onsuccess=()=>r(q.result);});const rows=await new Promise(r=>{const q=db.transaction('trash').objectStore('trash').getAll();q.onsuccess=()=>r(q.result);});db.close();return rows;});assert(trash.some(x=>JSON.stringify(x.current)===fixture.recoverySave.expectedRecord),'corrupt original lost');
 });
 await test('changed valid raw current rejects stale recovered admission without overwrite',async()=> {
  const valid=await page.evaluate(async id=>JSON.parse(await window.recovery.invoke('valid-before','Load',JSON.stringify({documentId:id}))),fixture.recoverySave.documentId);
  const result=await page.evaluate(async packet=>JSON.parse(await window.recovery.invoke('stale-recovery','Save',JSON.stringify(packet))),fixture.recoverySave);assert(!result.ok&&result.error.code==='RevisionConflict','stale recovered admission accepted');
  const after=await page.evaluate(async id=>JSON.parse(await window.recovery.invoke('valid-after','Load',JSON.stringify({documentId:id}))),fixture.recoverySave.documentId);assert(after.value.documentJson===valid.value.documentJson,'valid current replaced');
 });
 await test('canonical nonstandard nonempty Guid passes actual IndexedDB save and load',async()=> {
  const result=await call(page,'Save',fixture.imported);assert(result.ok&&result.committed,'canonical Guid denied');const loaded=await call(page,'Load',{documentId:fixture.imported.documentId});assert(JSON.parse(loaded.value.documentJson).id===fixture.imported.documentId,'ID changed');
 });
 await test('source-generated long values beyond 2^53 preserve opaque document metadata receipt and exact CAS',async()=> {
  await page.evaluate(async ([record,history])=>{const mod=window.createNotesModule({databaseName:'exact-long'});window.exactLong=mod;await mod.invoke('init','List','{}');const db=await new Promise(r=>{const q=indexedDB.open('exact-long');q.onsuccess=()=>r(q.result);});await new Promise((resolve,reject)=>{const tx=db.transaction(['documents','history'],'readwrite');tx.objectStore('documents').put(record);tx.objectStore('history').put(history);tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});db.close();},[fixture.longRecord,fixture.longHistory]);
  const result=await page.evaluate(async packet=>JSON.parse(await window.exactLong.invoke('long-save','Save',JSON.stringify(packet))),fixture.longSave);assert(result.ok&&result.committed&&result.value===fixture.longSave.receiptJson,'long receipt rounded');
  const loaded=await page.evaluate(async id=>JSON.parse(await window.exactLong.invoke('long-read','Load',JSON.stringify({documentId:id}))),fixture.longSave.documentId);
  assert(loaded.value.version==='9007199254740994'&&loaded.value.documentJson===fixture.longSave.documentJson&&loaded.value.versionInfoJson===fixture.longSave.versionInfoJson,'opaque long JSON changed');
  const stale=await page.evaluate(async packet=>JSON.parse(await window.exactLong.invoke('long-stale','Save',JSON.stringify(packet))),fixture.longSave);assert(!stale.ok&&stale.error.code==='RevisionConflict'&&stale.error.actualVersion==='9007199254740994','exact long CAS failed');
 });
 await test('missing-current history fence rejects new revision-zero overwrite',async()=> {
  await page.evaluate(async packet=>{const mod=window.createNotesModule({databaseName:'missing-current'});window.missingCurrent=mod;const saved=JSON.parse(await mod.invoke('first','Save',JSON.stringify(packet)));if(!saved.ok)throw Error(JSON.stringify(saved));const db=await new Promise(r=>{const q=indexedDB.open('missing-current');q.onsuccess=()=>r(q.result);});await new Promise((resolve,reject)=>{const tx=db.transaction('documents','readwrite');tx.objectStore('documents').delete(packet.documentId);tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});db.close();},fixture.first);
  const denied=await page.evaluate(async packet=>JSON.parse(await window.missingCurrent.invoke('wrong-new','Save',JSON.stringify(packet))),fixture.first);assert(!denied.ok&&denied.error.code==='StorageChanged','old history overwritten by revision zero');
  const records=await page.evaluate(async id=>JSON.parse(await window.missingCurrent.invoke('remaining','Versions',JSON.stringify({documentId:id}))),fixture.documentId);assert(records.value.length===1&&records.value[0].documentJson===fixture.first.documentJson,'history lost');
 });
 await test('readonly library snapshot preserves corrupt current and validated canonical history together',async()=> {
  const snapshot=await page.evaluate(async packet=>{const mod=window.createNotesModule({databaseName:'library-recovery'});window.library=mod;const saved=JSON.parse(await mod.invoke('create','Save',JSON.stringify(packet)));if(!saved.ok)throw Error(JSON.stringify(saved));const db=await new Promise(r=>{const q=indexedDB.open('library-recovery');q.onsuccess=()=>r(q.result);});await new Promise((resolve,reject)=>{const tx=db.transaction('documents','readwrite');tx.objectStore('documents').put({id:packet.documentId,version:'9',documentJson:'{}',sha256:'invalid',versionInfoJson:'{}'});tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});db.close();return JSON.parse(await mod.invoke('list','List','{}'));},fixture.first);
  assert(snapshot.ok&&snapshot.value.documents.length===1&&snapshot.value.documents[0].documentJson==='{}'&&snapshot.value.history.length===1&&snapshot.value.history[0].documentJson===fixture.first.documentJson,'library lost recovery source or rewrote original');
 });
 await test('same-module inspection waits for native hash phase settlement and sees late committed save',async()=> {
  const result=await page.evaluate(async packet=>{
   let release;const wait=new Promise(resolve=>release=resolve);const wrapped={randomUUID:()=>crypto.randomUUID(),subtle:{digest:async(...args)=>{await wait;return crypto.subtle.digest(...args);}}};const mod=window.createNotesModule({databaseName:'late-commit-inspection',crypto:wrapped});
   const open=IDBFactory.prototype.open;let opens=0;IDBFactory.prototype.open=function(...args){opens++;return open.apply(this,args);};
   try{const save=mod.invoke('late-save','Save',JSON.stringify(packet));const inspect=mod.invoke('inspect','Load',JSON.stringify({documentId:packet.documentId}));const before=opens;release();const [saved,loaded]=await Promise.all([save,inspect]);return{before,saved:JSON.parse(saved),loaded:JSON.parse(loaded)};}finally{IDBFactory.prototype.open=open;}
  },fixture.first);assert(result.before===0&&result.saved.ok&&result.saved.committed&&result.loaded.value.documentJson===fixture.first.documentJson,'inspection ran before pending mutation terminal state');
 });
 await test('pending hash mutation cancellation settles before inspection and read cancellation stays independent',async()=> {
  const result=await page.evaluate(async packet=>{
   let release;const wait=new Promise(resolve=>release=resolve);const wrapped={randomUUID:()=>crypto.randomUUID(),subtle:{digest:async(...args)=>{await wait;return crypto.subtle.digest(...args);}}};const mod=window.createNotesModule({databaseName:'cancelled-inspection',crypto:wrapped});
   const save=mod.invoke('cancel-save','Save',JSON.stringify(packet));const inspect=mod.invoke('inspect','Load',JSON.stringify({documentId:packet.documentId}));const cancelRead=mod.invoke('cancel-read','Load',JSON.stringify({documentId:packet.documentId}));mod.cancel('cancel-read');const cancelledRead=JSON.parse(await cancelRead);mod.cancel('cancel-save');release();const [saved,loaded]=await Promise.all([save,inspect]);return{cancelledRead,saved:JSON.parse(saved),loaded:JSON.parse(loaded)};
  },fixture.first);assert(!result.cancelledRead.ok&&result.cancelledRead.error.code==='Cancelled'&&!result.saved.ok&&result.saved.error.code==='Cancelled'&&result.loaded.ok&&result.loaded.value===null,'cancelled mutation or inspection guessed commit');
 });
 await test('actual origin quota denial rolls back document and history',async()=> {
  if(!fixture.large)throw Error('source generated large canonical fixture missing');
  const quotaOrigin='http://localhost:18763';
  const quotaPage=observe(await context.newPage());await quotaPage.goto(quotaOrigin);await quotaPage.waitForFunction(()=>typeof window.createNotesModule==='function');
  const session=await context.newCDPSession(quotaPage);await session.send('Storage.overrideQuotaForOrigin',{origin:quotaOrigin,quotaSize:65536});
  console.log('QUOTA_BEFORE '+JSON.stringify(await session.send('Storage.getUsageAndQuota',{origin:quotaOrigin})));
  await quotaPage.evaluate(async()=>{window.quota=window.createNotesModule({databaseName:'quota-failure'});const opened=JSON.parse(await window.quota.invoke('quota-init','List','{}'));if(!opened.ok)throw Error(JSON.stringify(opened));});
  const denied=await quotaPage.evaluate(async packet=>JSON.parse(await window.quota.invoke('quota','Save',JSON.stringify(packet))),fixture.large);
  console.log('QUOTA_AFTER '+JSON.stringify(await session.send('Storage.getUsageAndQuota',{origin:quotaOrigin})));
  assert(!denied.ok && denied.error.code==='QuotaExceeded','quota returned '+JSON.stringify(denied));
  const state=await quotaPage.evaluate(async id=> {const load=JSON.parse(await window.quota.invoke('ql','Load',JSON.stringify({documentId:id})));const history=JSON.parse(await window.quota.invoke('qh','Versions',JSON.stringify({documentId:id})));return {load,history};},fixture.large.documentId);
  assert(state.load.value===null && state.history.value.length===0,'quota partial commit'); await session.send('Storage.overrideQuotaForOrigin',{origin:quotaOrigin}); await session.detach();await quotaPage.close();
 });
 await test('cancellation before asynchronous hash/open prevents durable dispatch',async()=> {
  const result=await page.evaluate(async packet=>{const mod=window.createNotesModule({databaseName:'cancel-before'});const p=mod.invoke('cancelled','Save',JSON.stringify(packet));mod.cancel('cancelled');const result=JSON.parse(await p);const loaded=JSON.parse(await mod.invoke('check','Load',JSON.stringify({documentId:packet.documentId})));return{result,loaded};},fixture.first);
  assert(!result.result.ok && result.result.error.code==='Cancelled' && result.loaded.value===null,'cancel persisted');
 });
 await test('dispose while opening settles unavailable/cancelled rather than hanging',async()=> {
  const result=await page.evaluate(async()=> {const mod=window.createNotesModule({databaseName:'dispose-opening'});const p=mod.invoke('opening','List','{}');const close=mod.dispose();const result=JSON.parse(await p);await close;return result;}); assert(!result.ok,'disposed granted storage');
 });
 await test('delete commits active removal and retains exact recoverable trash',async()=> {
  const result=await call(page,'Delete',{documentId:fixture.documentId});assert(result.ok&&result.committed,'no delete commit');
  assert((await call(page,'Load',{documentId:fixture.documentId})).value===null && (await call(page,'Versions',{documentId:fixture.documentId})).value.length===0,'active not removed');
  const trash=await page.evaluate(async()=>{const db=await new Promise(r=>{const q=indexedDB.open('nine-to-one-write');q.onsuccess=()=>r(q.result);});const rows=await new Promise(r=>{const tx=db.transaction('trash');const q=tx.objectStore('trash').getAll();q.onsuccess=()=>r(q.result);});db.close();return rows;});
  assert(trash.some(x=>x.documentId===fixture.documentId&&x.current.documentJson===fixture.second.documentJson&&x.history.length===2),'trash lost canonical records');
 });
} catch(error) { fatal=String(error.stack); console.log('FATAL '+fatal); } finally { try { await context?.close(); } catch(error) { integrity('close '+error); } await new Promise(resolve=>server.close(resolve)); }
const hashesAfter={module:sha(await fs.readFile(modulePath)),fixture:sha(await fs.readFile(process.argv[2])),runner:sha(await fs.readFile(new URL(import.meta.url)))};
if(JSON.stringify(hashesBefore)!==JSON.stringify(hashesAfter))integrity('source/fixture/runner changed during run');
const summary={sourceUnderTest:String(modulePath),chromiumVersion,hashesBefore,hashesAfter,integrityErrorCount,integrityErrors,fatal,notRun:expectedNames.filter(name=>!results.some(result=>result.name===name)),scope:'ACTUAL Chromium IndexedDB adapter controls; not WASM/Write UI/provider acceptance',discovered:expectedNames.length,executed:results.length,passed:results.filter(x=>x.status==='PASS').length,failed:results.filter(x=>x.status==='FAIL').length,skipped:0,results};
await fs.writeFile(path.join(evidence,'browser-results.json'),JSON.stringify(summary,null,2));console.log(JSON.stringify(summary));process.exitCode=summary.failed||fatal||integrityErrorCount||results.length!==expectedNames.length||expectedNames.length!==17?1:0;
