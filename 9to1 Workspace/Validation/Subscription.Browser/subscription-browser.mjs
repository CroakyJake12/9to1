import {spawn} from 'node:child_process';
import {readFile,mkdtemp,rm,readlink} from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import {createHash} from 'node:crypto';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import assert from 'node:assert/strict';
const fixture=JSON.parse(await readFile(process.argv[2],'utf8'));
assert.equal(new URL(fixture.url).origin,'http://127.0.0.1:5095');
const profile=await mkdtemp(join(tmpdir(),'astra-subscription-chrome-'));
const chrome=spawn(process.env.ASTRA_CHROME||'/usr/bin/google-chrome',['--headless=new','--disable-gpu','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{stdio:['ignore','ignore','pipe']});
let ws;
try{
 const endpoint=await new Promise((resolve,reject)=>{let errors='';const timer=setTimeout(()=>reject(Error('Chromium debug endpoint timeout')),15000);chrome.once('exit',()=>{clearTimeout(timer);reject(Error('Chromium exited before endpoint'));});chrome.stderr.on('data',chunk=>{errors+=chunk;const m=errors.match(/DevTools listening on (ws:\/\/[^\s]+)/);if(m){clearTimeout(timer);resolve(m[1]);}});});
 const browser=new URL(endpoint);const response=await fetch('http://'+browser.host+'/json/new?about:blank',{method:'PUT'});assert.ok(response.ok);const target=await response.json();ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
 let seq=0;const pending=new Map();ws.addEventListener('message',event=>{const result=JSON.parse(event.data);if(result.id){const promise=pending.get(result.id);pending.delete(result.id);clearTimeout(promise.timer);result.error?promise.reject(Error(JSON.stringify(result.error))):promise.resolve(result.result);}});
 const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++seq;pending.set(id,{resolve,reject,timer:setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout: '+method));},15000)});ws.send(JSON.stringify({id,method,params}));});
 const fingerprint=async path=>{const hash=createHash('sha256');for await(const bytes of createReadStream(path))hash.update(bytes);return hash.digest('hex');};
 const chromeExecutable=await readlink('/proc/'+chrome.pid+'/exe');assert.ok(chromeExecutable.endsWith('/chrome'));const chromeSHA256=await fingerprint(chromeExecutable);const browserIdentity=await send('Browser.getVersion');assert.ok(browserIdentity.product.startsWith('Chrome/'));
 const evaluate=async expression=>{const result=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw Error('Browser script exception');return result.result.value;};
 await send('Page.enable');await send('Runtime.enable');await send('Page.addScriptToEvaluateOnNewDocument',{source:'window.cakeAccessToken='+JSON.stringify(fixture.accessToken)+';'});await send('Page.navigate',{url:fixture.url});
 const wait=async expression=>{for(let attempt=0;attempt<150;attempt++){if(await evaluate(expression))return;await new Promise(resolve=>setTimeout(resolve,100));}throw Error('Browser assertion timeout: '+expression);};
 await wait("document.querySelector('button[type=submit]')?.disabled===false");
 assert.equal(await evaluate("document.querySelectorAll('#builder small[data-option]').length"),18);
 const serverMatch=await evaluate(`(async()=>{const chosen=selection();const opts=await request('/api/subscription/options',chosen);let correct=0;for(const[id,list,values]of[['ai',opts.ai,multipliers],['storage',opts.storage,sizes]])for(let i=0;i<values.length;i++){const row=list[i];if(!row.quote)throw Error('fixture quote blocked');const displayed=document.querySelector('#'+id+' small[data-option="'+values[i]+'"]').textContent;if(displayed!==money(row.quote.breakdown.finalMonthlyCharge,row.quote.currency)+' / month with current other selection')throw Error('browser/server option mismatch');if(id==='ai'&&document.querySelector('#ai span[data-dust="'+values[i]+'"]').textContent!==' · '+row.quote.actualMonthlyDust.toLocaleString()+' AI Dust')throw Error('Dust mismatch');correct++;}const quote=currentQuote,b=quote.breakdown;if(b.margin/b.taxExclusiveMonthlyPrice<.15-1e-9)throw Error('combined fee-inclusive margin mismatch');return correct;})()`);assert.equal(serverMatch,16);
 // Actual input event clears every prior quote immediately before delayed server calculation.
 await evaluate("document.querySelector('[name=ai][value=Custom]').checked=true;document.querySelector('#custom-dust').value='';document.querySelector('#custom-dust').dispatchEvent(new Event('input',{bubbles:true}));");
 assert.equal(await evaluate("document.querySelector('button[type=submit]').disabled && [...document.querySelectorAll('#builder small[data-option]')].every(p=>p.textContent==='Updating monthly price…') && [...document.querySelectorAll('#builder span[data-dust]')].every(p=>p.textContent==='')"),true);
 await wait("document.querySelector('#summary').textContent.includes('nonnegative whole')");assert.equal(await evaluate("[...document.querySelectorAll('#builder small[data-option]')].every(p=>p.textContent==='Monthly price unavailable') && document.querySelector('button[type=submit]').disabled"),true);
 await evaluate("document.querySelector('#custom-dust').value='20000';document.querySelector('#custom-dust').dispatchEvent(new Event('change',{bubbles:true}));");await wait("document.querySelector('button[type=submit]').disabled===false");
 assert.equal(await evaluate("document.querySelector('#ai small[data-option=Custom]').textContent.includes('/ month with current other selection')"),true);
 // Real authenticated server revocation, rather than a fabricated pricing response.
 assert.equal(await evaluate("(async()=>{const r=await fetch('/api/account/signout',{method:'POST',headers:{Authorization:'Bearer '+window.cakeAccessToken}});return r.ok;})()"),true);
 await evaluate('refresh()');await wait("document.querySelector('button[type=submit]').disabled && [...document.querySelectorAll('#builder small[data-option]')].every(p=>p.textContent==='Monthly price unavailable')");assert.equal(await evaluate("[...document.querySelectorAll('#builder span[data-dust]')].every(p=>p.textContent==='')"),true);
 assert.equal(await fingerprint(chromeExecutable),chromeSHA256);
 console.log(JSON.stringify({chromium:{executable:chromeExecutable,sha256:chromeSHA256,version:browserIdentity.product},status:'PASS',passed:6,failed:0,cases:['actual authenticated Web Chromium mount with18choices','16option prices/Dust match same authenticated server calculator','15percent combined tax-exclusive fee-inclusive margin from existing fictional fixture','input immediately clears stale prices/Dust','invalid custom blocks and valid custom recovers','real server session revocation clears old quote and disables checkout'],scope:'Existing explicit fictional pricing/legacy identity fixture; no production pricing/account/login/deployment acceptance'}));
}finally{ws?.close();chrome.kill('SIGTERM');await new Promise(resolve=>{chrome.once('exit',resolve);setTimeout(()=>{chrome.kill('SIGKILL');resolve();},3000);});await rm(profile,{recursive:true,force:true});}
