// Actual Chromium transport controls with a local denial-only provider.
// This never produces a token or account and does not establish hosted sign-in acceptance.
const fs = require('node:fs'), path = require('node:path'), https = require('node:https'), crypto = require('node:crypto'), assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const [output, certificate, key] = process.argv.slice(2);
assert(output && certificate && key && !fs.existsSync(output)); fs.mkdirSync(output, { recursive: true });
const bundle = fs.readFileSync(path.resolve(__dirname, '../Auth/configured-accounts.bundle.js'));
const tls = { cert: fs.readFileSync(certificate), key: fs.readFileSync(key) };
const report = { startedAt: new Date().toISOString(), scope: 'Actual Chromium same-tab redirect and denial under unchanged COOP/COEP; local denial-only provider, no authenticated acceptance', bundleSha256: crypto.createHash('sha256').update(bundle).digest('hex'), checks: [] };
fs.copyFileSync(__filename, output + '/runner.cjs');
let clientOrigin, providerOrigin, browser, tokenRequests = 0;
const initialRequests = [];
const sourceRoot = path.resolve(__dirname, '../wwwroot');
const initialAssets = new Map([
 ['/host-configuration.js', { file: path.join(sourceRoot, 'host-configuration.js'), type: 'text/javascript' }],
 ['/main.js', { file: path.join(sourceRoot, 'main.js'), type: 'text/javascript' }],
 ['/cui-bootstrap.css', { file: path.resolve(__dirname, '../../../framework/CUI/Themes/BrowserBootstrap.css'), type: 'text/css' }],
]);
const headers = { 'Content-Type': 'text/html', 'Cache-Control': 'no-store', 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp' };
const client = https.createServer(tls, (req, res) => {
 if (req.url.startsWith('/source-host?')) { res.writeHead(200, headers); res.end(fs.readFileSync(path.join(sourceRoot, 'index.html'))); return; }
 if (initialAssets.has(req.url)) {
  const asset = initialAssets.get(req.url); initialRequests.push({ path: req.url, referer: req.headers.referer ?? null });
  res.writeHead(200, { ...headers, 'Content-Type': asset.type }); res.end(fs.readFileSync(asset.file)); return;
 }
 if (req.url === '/bundle.js') { res.writeHead(200, { ...headers, 'Content-Type': 'text/javascript' }); res.end(bundle); return; }
 const config = { issuer: providerOrigin + '/api/auth', apiResource: providerOrigin, clientId: 'isolated-denial-only-client', redirectUri: clientOrigin + '/oauth/callback', scopes: 'openid cake:account:read' };
 res.writeHead(200, headers);
 res.end(`<!doctype html><link rel="icon" href="data:,"><button id="sign-in">Sign in</button><output id="result">Starting</output><script type="module">
 import { BrowserPublicClient, captureOAuthRedirectCallback } from '/bundle.js';
 const config=${JSON.stringify(config)}, captured=captureOAuthRedirectCallback(config);
 const owner=new BrowserPublicClient({configuration:config,signInMode:'redirect',canRedirect:()=>true,
  onBeforeSignIn:async()=>{},verifyCurrentAccount:()=>{throw Error('No test account exists');},onVerifiedIdentity:()=>{throw Error('No test account exists');},
  onSignInFailed:async()=>{},onTokenExpired:async()=>{},onFailure:()=>{}});
 window.owner=owner;
 const result=document.getElementById('result');
 document.getElementById('sign-in').onclick=()=>owner.signIn().catch(e=>result.textContent=e.code);
 result.textContent='Ready';
 if(captured)try{await owner.resumeRedirect(captured);result.textContent='Unexpected success';}catch(e){result.textContent=e.code;}
 </script>`);
});
const provider = https.createServer(tls, (req, res) => {
 const url = new URL(req.url, providerOrigin);
 if (url.pathname.endsWith('/.well-known/openid-configuration')) {
  res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': clientOrigin, 'Cache-Control': 'no-store' });
  res.end(JSON.stringify({ issuer: providerOrigin + '/api/auth', authorization_endpoint: providerOrigin + '/authorize', token_endpoint: providerOrigin + '/token', jwks_uri: providerOrigin + '/jwks' }));
 } else if (url.pathname === '/authorize') {
  const target = new URL(clientOrigin + '/oauth/callback'); target.search = new URLSearchParams({ error: 'access_denied', state: url.searchParams.get('state') });
  res.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' });
  res.end('<!doctype html><link rel="icon" href="data:,"><a id="deny" href="' + target.href.replaceAll('&', '&amp;') + '">Return denied request</a>');
 } else { if (url.pathname === '/token') tokenRequests++; res.writeHead(400); res.end(); }
});
const listen = server => new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve('https://127.0.0.1:' + server.address().port)));
(async () => {
 try {
  clientOrigin = await listen(client); providerOrigin = await listen(provider);
  browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || '/usr/bin/chromium', headless: true, args: ['--no-sandbox'] });
  const context = await browser.newContext({ ignoreHTTPSErrors: true }), page = await context.newPage();
  await page.goto(clientOrigin + '/#/app.spaces'); await page.waitForFunction(() => document.getElementById('result').textContent === 'Ready');
  assert.equal(await page.evaluate(() => crossOriginIsolated), true);
  await page.locator('#sign-in').click(); await page.waitForURL(providerOrigin + '/authorize?**');
  const auth = new URL(page.url());
  assert.equal(auth.searchParams.get('code_challenge_method'), 'S256'); assert.equal(context.pages().length, 1);
  report.checks.push({ name: 'same-tab-S256-with-existing-isolation', result: 'PASS', authorizationOrigin: auth.origin, pageCount: context.pages().length });
  await page.locator('#deny').click(); await page.waitForFunction(() => document.getElementById('result')?.textContent === 'AuthorizationDenied');
  const state = await page.evaluate(() => ({ isolated: crossOriginIsolated, href: location.href, storageCount: sessionStorage.length, token: owner.getAccessToken() }));
  assert.equal(state.isolated, true); assert.equal(state.href, clientOrigin + '/#/app.spaces'); assert.equal(state.storageCount, 0); assert.equal(state.token, null); assert.equal(tokenRequests, 0);
  report.checks.push({ name: 'denial-consumed-storage-query-before-exchange', result: 'PASS', isolated: state.isolated, storageCount: state.storageCount, tokenRequests });
  await page.reload(); await page.waitForFunction(() => document.getElementById('result').textContent === 'Ready'); assert.equal(await page.evaluate(() => owner.getAccessToken()), null);
  report.checks.push({ name: 'reload-remains-signed-out-without-replaying-callback', result: 'PASS' });
  await page.goto(clientOrigin + '/oauth/callback?state=' + auth.searchParams.get('state') + '&error=access_denied');
  await page.waitForFunction(() => document.getElementById('result').textContent === 'RedirectTransactionUnavailable');
  assert.equal(page.url(), clientOrigin + '/'); assert.equal(tokenRequests, 0);
  report.checks.push({ name: 'repeated-callback-has-no-one-use-transaction', result: 'PASS', tokenRequests });
  // Actual source HTML resource policy only: no full application operation is claimed
  // from this fixture, which does not serve .NET or all application dependencies.
  await page.goto(clientOrigin + '/source-host?code=negative-code&state=negative-state');
  for (let attempt = 0; attempt < 50 && initialRequests.length < 3; attempt++) await new Promise(resolve => setTimeout(resolve, 20));
  assert.deepEqual(initialRequests.map(item => item.path).sort(), [...initialAssets.keys()].sort());
  assert(initialRequests.every(item => item.referer === null));
  report.checks.push({ name: 'actual-source-host-initial-assets-carry-no-callback-referrer-without-response-policy', result: 'PASS', initialRequests });
  report.passed = true;
 } catch (error) { report.error = String(error); process.exitCode = 1; }
 finally { await browser?.close(); client.close(); provider.close(); report.finishedAt = new Date().toISOString(); fs.writeFileSync(output + '/report.json', JSON.stringify(report, null, 2) + '\n'); console.log(JSON.stringify(report, null, 2)); }
})();
