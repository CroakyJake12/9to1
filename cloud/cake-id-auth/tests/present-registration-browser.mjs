// Actual local administrator UI journey. Uses only an already-owned synthetic fixture; never starts or contacts a hosted issuer.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFileSync,statSync,mkdirSync,writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {createHash} from 'node:crypto';
const require=createRequire(import.meta.url),{chromium}=require('playwright');
const [manifestPath,outPath]=process.argv.slice(2);
if(!manifestPath||!outPath)throw Error('Pass an existing private synthetic fixture manifest and evidence directory.');
assert.equal(statSync(manifestPath).mode&0o077,0,'synthetic fixture credentials must stay private');
const manifest=JSON.parse(readFileSync(manifestPath,'utf8')),issuer=new URL(manifest.issuer);
assert.equal(manifest.fixtureOnly,true);assert.equal(issuer.protocol,'http:');assert.equal(issuer.hostname,'127.0.0.1');assert.equal(issuer.pathname,'/api/auth');
assert.ok(manifest.syntheticOperator?.email?.endsWith('@example.test'));assert.ok(manifest.syntheticOperator.password);assert.ok(manifest.accounts[0]?.email?.endsWith('@example.test'));
const origin=issuer.origin,out=resolve(outPath);mkdirSync(out,{recursive:true});
const report={kind:'actual local Workerd/Chromium administrator registration; synthetic accounts only',started:new Date().toISOString(),issuer:origin,steps:[],requests:[],screenshots:[],errors:[]};
let browser,step='launch',passed=false;
async function screenshot(page,name){const file=resolve(out,name+'.png');await page.screenshot({path:file});report.screenshots.push({name,sha256:createHash('sha256').update(readFileSync(file)).digest('hex')});}
try {
 browser=await chromium.launch({executablePath:process.env.CHROMIUM_EXECUTABLE??'/usr/bin/chromium',headless:true,args:['--no-sandbox','--disable-dev-shm-usage']});
 async function pageFor(account){
  const context=await browser.newContext({viewport:{width:1100,height:750}});
  await context.route('**/*',route=>new URL(route.request().url()).origin===origin?route.continue():route.abort());
  const page=await context.newPage();page.on('pageerror',()=>report.errors.push('browser page error'));
  page.on('response',response=>{const url=new URL(response.url());report.requests.push({method:response.request().method(),path:url.pathname,status:response.status()});});
  await page.goto(origin+'/sign-in');await page.getByLabel('Email',{exact:true}).fill(account.email);await page.getByLabel('Password',{exact:true}).fill(account.password);
  await Promise.all([page.waitForURL(origin+'/account'),page.getByRole('button',{name:'Sign in',exact:true}).click()]);
  return {page,context};
 }
 step='ordinary account denial';const ordinary=await pageFor(manifest.accounts[0]);
 assert.equal(await ordinary.page.getByRole('button',{name:'Connect Present preview',exact:true}).count(),0);
 assert.equal((await ordinary.context.request.get(origin+'/account/present-app')).status(),403);
 report.steps.push('A real signed-in nonadministrator sees no registration action and receives403 from metadata endpoint.');await ordinary.context.close();
 step='administrator sign-in';const admin=await pageFor(manifest.syntheticOperator);const {page,context}=admin;
 const before=(await(context.request.get(origin+'/account/staging-apps')).then(r=>r.json()));
 assert.equal(before.kind,'missing');assert.equal(before.native.kind,'missing');
 const presentBefore=await context.request.get(origin+'/account/present-app');assert.equal(presentBefore.status(),200);
 assert.equal((await presentBefore.json()).kind,'missing');
 await screenshot(page,'admin-present-before');
 report.steps.push('Administrator signs in through the maintained form; exact protected callback is visible and Present is missing.');
 step='physical registration click';await page.getByRole('button',{name:'Connect Present preview',exact:true}).click();
 await page.locator('#present-app-status').filter({hasText:'Present preview connected. Public client ID:'}).waitFor();
 const afterResponse=await context.request.get(origin+'/account/present-app');assert.equal(afterResponse.status(),200);
 const after=await afterResponse.json();assert.equal(after.kind,'ready');assert.equal(typeof after.clientId,'string');assert.ok(after.clientId);
 assert.equal(await page.getByRole('button',{name:'Connect Present preview',exact:true}).isDisabled(),true);
 assert.deepEqual(await(context.request.get(origin+'/account/staging-apps')).then(r=>r.json()),before);
 await screenshot(page,'admin-present-connected');report.publicClientId=after.clientId;
 report.steps.push('Physical Connect Present preview click invokes maintained create-client once, then exact canonical owner readback confirms the public client. Staging and native clients remain missing.');
 step='reload reconciliation';await page.reload();assert.equal(await page.getByRole('button',{name:'Connect Present preview',exact:true}).count(),0);
 assert.ok((await page.locator('body').innerText()).includes(after.clientId));
 assert.deepEqual(await(context.request.get(origin+'/account/present-app')).then(r=>r.json()),after);
 await screenshot(page,'admin-present-reloaded');
 step='independent administrator tab';const second=await pageFor(manifest.syntheticOperator);
 assert.equal(await second.page.getByRole('button',{name:'Connect Present preview',exact:true}).count(),0);
 assert.deepEqual(await(second.context.request.get(origin+'/account/present-app')).then(r=>r.json()),after);
 await second.context.close();await context.close();
 assert.equal(report.requests.filter(r=>r.method==='POST'&&r.path==='/api/auth/oauth2/create-client').length,1);
 assert.ok(report.requests.some(r=>r.method==='POST'&&r.path==='/api/auth/oauth2/create-client'&&r.status===201));assert.deepEqual(report.errors,[]);
 report.steps.push('Reload and a separately signed-in browser context retain the same ID without another registration.');passed=true;
} catch {report.failure='Local administrator journey failed during: '+step;process.exitCode=1;}
finally {await browser?.close();report.completed=new Date().toISOString();report.passed=passed;writeFileSync(resolve(out,'receipt.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({passed,receipt:resolve(out,'receipt.json'),failure:report.failure}));}
