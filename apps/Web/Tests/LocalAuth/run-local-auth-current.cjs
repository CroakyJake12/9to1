'use strict';
// New bounded receiver test. This is neither B's undelivered auth07 nor its anonymous interop7.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const net = require('node:net');
const hostPath = path.resolve(__dirname, '../ci/sealed-browser-replay/sealed-https-host-explicit-binding.cjs');
const { verifySeal, startSealedHost, sha } = require(hostPath);
const origin = 'https://client.example.test:5096';
const issuerOrigin = 'http://127.0.0.1:8799';
const scopes = 'openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke';
const hashPattern = /^[0-9a-f]{64}$/;
const commitPattern = /^[0-9a-f]{40}$/;
const validCommit = value => typeof value === 'string' && value.length === 40 && commitPattern.test(value);
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const fixed = condition => { if (!condition) throw new Error('Bound local-auth assertion refused.'); };
const jsonFile = file => JSON.parse(fs.readFileSync(file));
const contractPath = path.join(__dirname, 'source-contract.json');
const privateNames = ['Profile name', 'Canonical account ID', 'Canonical session ID', 'Current profile revision', 'Session change confirmation'];
const privateAbsent = snapshot => !snapshot.elements.some(peer => privateNames.includes(peer.name));
const withoutCredentialDiagnostics = environment => ({ ...environment, DEBUG: '', PWDEBUG: '', NODE_DEBUG: '', NODE_OPTIONS: '' });

function verifyPublicBinding(bindingPath, expectedCommit) {
  // These complete public gates precede private-file reads, Playwright import, listeners and network.
  fixed(validCommit(expectedCommit));
  const bindingBytes = fs.readFileSync(bindingPath);
  const binding = JSON.parse(bindingBytes);
  fixed(binding.sourceCommit === expectedCommit && hashPattern.test(binding.manifestSha256 || ''));
  fixed(binding.runnerSha256 === sha(fs.readFileSync(__filename)) && binding.hostSha256 === sha(fs.readFileSync(hostPath)));
  fixed(binding.contractSha256 === sha(fs.readFileSync(contractPath)));
  for (const [file, digest] of [[binding.originalManifestPath, binding.originalManifestSha256], [binding.originalSealPath, binding.originalSealSha256], [binding.originalArchivePath, binding.originalArchiveSha256]]) fixed(path.isAbsolute(file || '') && hashPattern.test(digest || '') && sha(fs.readFileSync(file)) === digest);
  const original = jsonFile(binding.originalManifestPath), producerSeal = jsonFile(binding.originalSealPath);
  fixed(producerSeal.sourceCommit === expectedCommit && producerSeal.receiptSha256 === binding.originalManifestSha256 && producerSeal.zipSha256 === binding.originalArchiveSha256 && producerSeal.zipBytes === fs.statSync(binding.originalArchivePath).size && producerSeal.fileCount === binding.fileCount);
  fixed(hashPattern.test(binding.sourceCatalogSha256 || '') && hashPattern.test(binding.sourceCatalogAfterSha256 || ''));
  const before = fs.readFileSync(binding.sourceCatalogPath), after = fs.readFileSync(binding.sourceCatalogAfterPath);
  fixed(sha(before) === binding.sourceCatalogSha256 && sha(after) === binding.sourceCatalogAfterSha256 && before.equals(after));
  const catalog = JSON.parse(before), manifest = jsonFile(binding.manifestPath);
  fixed(JSON.stringify({ ...original, publishRoot: manifest.publishRoot }) === JSON.stringify(manifest));
  fixed(Array.isArray(catalog) && manifest.sourceCommitAfter === expectedCommit && manifest.sourceInputs === catalog.length && manifest.sourceInputsAfter === catalog.length);
  for (const key of ['addedSourceInputs', 'removedSourceInputs', 'changedSourceInputs', 'trackedChangesAfter']) fixed(Array.isArray(manifest[key]) && manifest[key].length === 0);
  const contract = jsonFile(contractPath);
  const indexed = new Map(catalog.map(row => [row.path, row]));
  fixed(indexed.size === catalog.length);
  for (const row of contract.requiredSource) {
    const actual = indexed.get(row.path);
    fixed(actual && actual.sha256 === row.sha256 && actual.bytes === row.bytes);
  }
  fixed(validCommit(binding.fixtureSourceCommit) && path.isAbsolute(binding.fixtureCheckout || ''));
  fixed(Array.isArray(binding.fixtureSourcePins) && binding.fixtureSourcePins.length > 0);
  const fixtureRoot = path.join(fs.realpathSync(binding.fixtureCheckout), 'cloud/cake-id-auth');
  const selected = new Set();
  for (const row of binding.fixtureSourcePins) {
    fixed(typeof row.path === 'string' && path.isAbsolute(row.path) && hashPattern.test(row.sha256 || ''));
    const relative = path.relative(fixtureRoot, row.path);
    fixed(relative && !relative.startsWith('..') && !path.isAbsolute(relative) && !selected.has(relative));
    fixed(!fs.lstatSync(row.path).isSymbolicLink() && fs.statSync(row.path).isFile());
    fixed(fs.realpathSync(row.path) === row.path && sha(fs.readFileSync(row.path)) === row.sha256);
    selected.add(relative);
  }
  for (const name of ['tests/browser-fixture.mjs', 'tests/linux-fixture-custodian.py', 'src/auth.ts', 'src/index.ts', 'src/local.ts', 'package.json', 'package-lock.json', 'wrangler.local.jsonc']) fixed(selected.has(name));
  fixed(selected.size === contract.requiredFixtureSource.length);
  const fixturePins = new Map(binding.fixtureSourcePins.map(row => [path.relative(fixtureRoot, row.path), row]));
  for (const row of contract.requiredFixtureSource) {
    const actual = fixturePins.get(row.path);
    fixed(actual && actual.sha256 === row.sha256 && actual.bytes === row.bytes);
  }
  fixed(path.isAbsolute(binding.fixtureDependencyRoot || '') && fs.realpathSync(path.join(fixtureRoot, 'node_modules')) === binding.fixtureDependencyRoot);
  fixed(Array.isArray(binding.fixtureDependencyLinks));
  for (const row of binding.fixtureDependencyLinks) {
    const relative = path.relative(binding.fixtureDependencyRoot, row.path);
    fixed(relative && !relative.startsWith('..') && !path.isAbsolute(relative) && fs.lstatSync(row.path).isSymbolicLink() && fs.realpathSync(row.path) === row.target);
  }
  const seal = verifySeal(binding.manifestPath, binding);
  return { binding, seal, bindingSha256: sha(bindingBytes), fixtureRoot };
}

function readOwnedFixture(manifestPath, runRoot, publicGate) {
  // Root supplies a fresh READY path from its own still-held original TTY. Paths are not recovered from ports/PIDs.
  fixed(path.isAbsolute(manifestPath || '') && path.isAbsolute(runRoot || ''));
  fixed(path.dirname(runRoot) === path.join(publicGate.fixtureRoot, '.local-run') && /^[0-9a-f]{16}$/.test(path.basename(runRoot)));
  fixed(manifestPath === path.join(runRoot, 'browser-fixture-private.json'));
  const identities = [];
  for (const [file, mode, directory] of [[runRoot, 0o700, true], [manifestPath, 0o600, false]]) {
    const stat = fs.lstatSync(file);
    fixed(!stat.isSymbolicLink() && (directory ? stat.isDirectory() : stat.isFile()) && (stat.mode & 0o777) === mode);
    fixed(stat.uid === process.getuid() && fs.realpathSync(file) === file);
    identities.push({ file, dev: stat.dev, ino: stat.ino, uid: stat.uid, mode });
  }
  const bytes = fs.readFileSync(manifestPath);
  fixed(bytes.length <= 65536);
  const fixture = JSON.parse(bytes);
  fixed(fixture.fixtureOnly === true && fixture.runId === path.basename(runRoot) && fixture.persistPath === runRoot);
  fixed(fixture.issuer === issuerOrigin + '/api/auth' && fixture.apiResource === issuerOrigin && fixture.browserOrigin === origin && fixture.redirectUri === origin + '/callback' && fixture.scopes === scopes);
  fixed(typeof fixture.clientId === 'string' && fixture.clientId.length > 0 && fixture.clientId.length <= 512 && !/\s/.test(fixture.clientId));
  fixed(fixture.discovery?.issuer === fixture.issuer && Array.isArray(fixture.accounts) && fixture.accounts.length === 2);
  for (const name of ['authorization_endpoint', 'token_endpoint', 'jwks_uri']) {
    const endpoint = new URL(fixture.discovery[name]);
    fixed(endpoint.href === fixture.discovery[name] && endpoint.origin === issuerOrigin && !endpoint.username && !endpoint.password && !endpoint.search && !endpoint.hash && endpoint.pathname !== '/');
  }
  for (const account of fixture.accounts) fixed(uuidPattern.test(account.id || '') && typeof account.email === 'string' && /^[A-Za-z0-9_.+-]+@example\.test$/.test(account.email) && typeof account.password === 'string' && account.password.length >= 12 && account.password.length <= 128);
  fixed(fixture.accounts[0].id !== fixture.accounts[1].id);
  return { fixture, unchanged: () => {
    fixed(fs.readFileSync(manifestPath).equals(bytes));
    for (const identity of identities) { const stat = fs.lstatSync(identity.file); fixed(!stat.isSymbolicLink() && stat.dev === identity.dev && stat.ino === identity.ino && stat.uid === identity.uid && (stat.mode & 0o777) === identity.mode && fs.realpathSync(identity.file) === identity.file); }
  }};
}

async function run(bindingPath, output, expectedCommit, manifestPath, runRoot) {
  // The prepared invocation also clears these BEFORE Node starts. No PW API logger may print fill values.
  for (const name of ['DEBUG', 'PWDEBUG', 'NODE_DEBUG', 'NODE_OPTIONS']) delete process.env[name];
  const names = ['current-native-anonymous-ready', 'physical-native-local-PKCE-and-consent', 'actual-current-profile-sessions-bound-to-fictional-subject', 'physical-native-confirmed-server-signout', 'retained-peer-refusal-and-tokenless-native-check', 'terminal-native-and-broker-join', 'immutable-public-assets-and-normal-browser-close'];
  const report = {
    scope: 'NEW_LOCAL_PKCE: current sealed browser-WASM, actual native CUI and existing JOSE broker against a fresh root-owned local issuer only. No hosted/.NET RSA/auth_revision/global acceptance; no B auth07 or anonymous7 relabeling.',
    outcomes: Object.fromEntries(names.map(name => [name, { status: 'NOT_RUN' }])),
    unexpected: { pageError: 0, consoleError: 0, requestFailed: 0, foreignRequest: 0, observationFailure: 0 },
    cleanupFailures: [], hostedIdentityAccepted: false, strictDotnetIdentityAccepted: false
  };
  let gate, owned, browser, context, page, server, active = 'public-preflight', substep = 'entered', started = Date.now(), oldId, tokenlessRequests, keyMetadata;
  const write = () => fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2) + '\n', { mode: 0o600 });
  const mark = stage => { substep = stage; };
  const safeError = error => ({ stage: active, substep, type: ['Error', 'TypeError', 'AssertionError', 'TimeoutError'].includes(error?.name) ? error.name : 'Error', elapsedMs: Math.min(240000, Math.max(0, Date.now() - started)) });
  const observed = { discovery: 0, jwks: 0, authorize: 0, callback: 0, token: 0, current: 0, profile: 0, sessions: 0, signout: 0, apiRequests: 0, protocolValid: true, sameSubject: true };
  const pending = new Set();
  let state, nonce, challenge, callbackCode;
  async function drainObservations() { while (pending.size) await Promise.all([...pending]); }
  async function snapshot() { return page.evaluate(async () => { const runtime = getDotnetRuntime(0); const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName); return JSON.parse(exports.NineToOne.Web.Program.ReadAccessibility()); }); }
  async function native(operation, id) { return page.evaluate(async ({ operation, id }) => { const runtime = getDotnetRuntime(0); const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName); const owner = exports.NineToOne.Web.Program; return operation === 'stale' ? owner.PerformAccessibility(id, 'invoke', null) : await owner[operation](); }, { operation, id }); }
  async function wait(predicate, timeout = 15000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
      let timer;
      try { if (await Promise.race([predicate(), new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Bound native deadline.')), Math.max(1, deadline - Date.now())); })]) === true) return; }
      finally { clearTimeout(timer); }
      if (Date.now() < deadline) await page.waitForTimeout(Math.min(50, deadline - Date.now()));
    }
    throw new Error('Actual native predicate timed out.');
  }
  async function peer(name) { let selected; await wait(async () => { const current = await snapshot(); const found = current.elements.filter(item => item.role === 'button' && item.name === name && item.enabled); if (found.length !== 1) return false; selected = { snapshot: current, item: found[0] }; return true; }); return selected; }
  async function physical(name) {
    const selected = await peer(name), current = selected.snapshot, item = selected.item;
    const canvas = await page.evaluate(() => { const list = [...document.querySelectorAll('#nine-to-one-root canvas')].filter(item => item.width && item.height); if (list.length !== 1) return null; const rect = list[0].getBoundingClientRect(); return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }; });
    const b = item.bounds;
    fixed(canvas && current.viewport && Math.abs(canvas.width - current.viewport.width) <= 1 && Math.abs(canvas.height - current.viewport.height) <= 1);
    fixed(b && [b.x, b.y, b.width, b.height].every(Number.isFinite) && b.x >= 0 && b.y >= 0 && b.width > 0 && b.height > 0 && b.x + b.width <= canvas.width + 1 && b.y + b.height <= canvas.height + 1);
    const point = { x: canvas.x + b.x + b.width / 2, y: canvas.y + b.y + b.height / 2 };
    fixed(await page.evaluate(p => !!document.elementFromPoint(p.x, p.y)?.closest('#nine-to-one-root'), point));
    await page.mouse.click(point.x, point.y);
    return selected;
  }
  async function test(name, action) { active = name; substep = 'entered'; started = Date.now(); write(); try { report.outcomes[name] = { status: 'PASS', observed: await action() }; } catch (error) { report.outcomes[name] = { status: 'FAIL', error: safeError(error) }; throw error; } finally { write(); } }
  try {
    gate = verifyPublicBinding(bindingPath, expectedCommit);
    fixed(process.env.B5_ACCOUNT_INTEROP_GUI_GRANTED === 'granted');
    fixed(typeof process.env.PLAYWRIGHT_MODULE === 'string' && typeof process.env.CHROMIUM_EXECUTABLE === 'string' && /^[A-Za-z0-9+/]{43}=$/.test(process.env.B5_CLIENT_TLS_SPKI || ''));
    fixed(!fs.existsSync(output)); fs.mkdirSync(output, { mode: 0o700 });
    active = 'owned-fixture-preflight'; owned = readOwnedFixture(manifestPath, runRoot, gate);
    const fixture = owned.fixture;
    report.sourceCommit = expectedCommit; report.fixtureSourceCommit = gate.binding.fixtureSourceCommit; report.bindingSha256 = gate.bindingSha256;
    report.seal = { sourceCommit: gate.seal.sourceCommit, manifestSha256: gate.seal.manifestSha256, fileCount: gate.seal.fileCount };
    const configuration = { issuer: fixture.issuer, apiResource: fixture.apiResource, clientId: fixture.clientId, redirectUri: fixture.redirectUri, scopes: fixture.scopes, allowLoopbackForIsolatedTests: true };
    const { chromium } = require(process.env.PLAYWRIGHT_MODULE);
    const keyStat = fs.lstatSync(process.env.B5_CLIENT_TLS_KEY);
    fixed(keyStat.isFile() && !keyStat.isSymbolicLink() && keyStat.uid === process.getuid() && (keyStat.mode & 0o777) === 0o600);
    keyMetadata = { dev: keyStat.dev, ino: keyStat.ino, size: keyStat.size, mtimeMs: keyStat.mtimeMs, uid: keyStat.uid };
    server = await startSealedHost(gate.seal);
    const append = value => [...new Set([...(value || '').split(',').map(x => x.trim()).filter(Boolean), 'client.example.test', '127.0.0.1', 'localhost'])].join(',');
    browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE, headless: true, env: { ...withoutCredentialDiagnostics(process.env), NO_PROXY: append(process.env.NO_PROXY), no_proxy: append(process.env.no_proxy) }, args: ['--proxy-bypass-list=client.example.test;127.0.0.1;localhost', '--host-resolver-rules=MAP client.example.test 127.0.0.1', '--ignore-certificate-errors-spki-list=' + process.env.B5_CLIENT_TLS_SPKI, '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
    report.browserVersion = browser.version();
    context = await browser.newContext({ viewport: { width: 1440, height: 2200 }, serviceWorkers: 'block' });
    // Public host configuration only. No credentials, native export/function override, fake state or asset edits.
    await context.addInitScript(({ configuration, origin }) => { if (location.origin === origin) window.nineToOneBrowserConfiguration = { account: configuration }; }, { configuration, origin });
    await context.route('**/*', async route => { let allowed = false; try { allowed = [origin, issuerOrigin].includes(new URL(route.request().url()).origin); } catch {} if (allowed) await route.continue(); else { report.unexpected.foreignRequest++; await route.abort('blockedbyclient'); } });
    context.on('page', opened => { opened.on('pageerror', () => report.unexpected.pageError++); opened.on('console', message => { if (message.type() === 'error') report.unexpected.consoleError++; }); });
    context.on('requestfailed', () => report.unexpected.requestFailed++);
    context.on('request', request => {
      try {
        const url = new URL(request.url());
        if (url.origin === issuerOrigin && url.pathname.startsWith('/api/account/')) observed.apiRequests++;
        if (url.href.split('?')[0] === fixture.discovery.authorization_endpoint) {
          observed.authorize++; observed.protocolValid &&= request.method() === 'GET';
          const query = url.searchParams;
          observed.protocolValid &&= ['response_type', 'client_id', 'redirect_uri', 'scope', 'state', 'nonce', 'code_challenge', 'code_challenge_method', 'resource'].every(key => query.getAll(key).length === 1);
          observed.protocolValid &&= query.get('response_type') === 'code' && query.get('client_id') === fixture.clientId && query.get('redirect_uri') === fixture.redirectUri && query.get('scope') === scopes && query.get('resource') === issuerOrigin && query.get('code_challenge_method') === 'S256';
          state = query.get('state'); nonce = query.get('nonce'); challenge = query.get('code_challenge');
          observed.protocolValid &&= /^[A-Za-z0-9_-]{43}$/.test(state || '') && /^[A-Za-z0-9_-]{43}$/.test(nonce || '') && /^[A-Za-z0-9_-]{43}$/.test(challenge || '');
        }
        if (url.origin === origin && url.pathname === '/callback') {
          observed.callback++;
          observed.protocolValid &&= url.searchParams.getAll('code').length === 1 && url.searchParams.getAll('state').length === 1 && url.searchParams.get('state') === state;
          callbackCode = url.searchParams.get('code');
          observed.protocolValid &&= typeof callbackCode === 'string' && callbackCode.length > 0 && !url.searchParams.has('error');
        }
        if (url.href === fixture.discovery.token_endpoint && request.method() === 'POST') {
          const form = new URLSearchParams(request.postData() || '');
          const verifier = form.get('code_verifier');
          observed.protocolValid &&= ['grant_type', 'client_id', 'redirect_uri', 'code', 'code_verifier', 'resource'].every(key => form.getAll(key).length === 1);
          observed.protocolValid &&= form.get('grant_type') === 'authorization_code' && form.get('client_id') === fixture.clientId && form.get('redirect_uri') === fixture.redirectUri && form.get('resource') === issuerOrigin && form.get('code') === callbackCode && /^[A-Za-z0-9_-]{64}$/.test(verifier || '') && crypto.createHash('sha256').update(verifier || '').digest('base64url') === challenge;
        }
      } catch { report.unexpected.observationFailure++; }
    });
    context.on('response', response => {
      const observation = (async () => {
        const url = new URL(response.url()), method = response.request().method();
        if (method === 'GET' && url.href === fixture.issuer + '/.well-known/openid-configuration' && response.status() === 200) observed.discovery++;
        if (method === 'GET' && url.href === fixture.discovery.jwks_uri && response.status() === 200) observed.jwks++;
        if (method === 'POST' && url.href === fixture.discovery.token_endpoint && response.status() === 200) observed.token++;
        if (url.origin !== issuerOrigin || response.status() !== 200 || method !== 'GET') { if (url.origin === issuerOrigin && url.pathname === '/api/account/signout' && method === 'POST' && response.status() === 204) observed.signout++; return; }
        if (!['/api/account/current', '/api/account/profile', '/api/account/sessions'].includes(url.pathname)) return;
        const body = await response.json();
        if (url.pathname === '/api/account/current') { observed.current++; observed.sameSubject &&= body.accountId === fixture.accounts[0].id; }
        if (url.pathname === '/api/account/profile') { observed.profile++; observed.sameSubject &&= body.profile?.accountId === fixture.accounts[0].id && Number.isSafeInteger(body.profile?.revision) && body.profile.revision > 0; }
        if (url.pathname === '/api/account/sessions') { observed.sessions++; observed.sameSubject &&= Array.isArray(body.sessions) && body.sessions.length > 0 && body.sessions.every(session => session.accountId === fixture.accounts[0].id && uuidPattern.test(session.sessionId || '')); }
      })().catch(() => { report.unexpected.observationFailure++; });
      pending.add(observation); observation.finally(() => pending.delete(observation));
    });
    page = await context.newPage();
    await test(names[0], async () => {
      const deadline = Date.now() + 45000;
      await page.goto(origin + '/#/home.settings', { waitUntil: 'domcontentloaded', timeout: Math.max(1, deadline - Date.now()) });
      await wait(() => page.evaluate(async () => { if (typeof getDotnetRuntime !== 'function') return false; const runtime = getDotnetRuntime(0); const config = runtime?.getConfig?.(); if (!config?.mainAssemblyName) return false; const exports = await runtime.getAssemblyExports(config.mainAssemblyName); return typeof exports.NineToOne?.Web?.Program?.ReadAccessibility === 'function'; }), Math.max(1, deadline - Date.now()));
      await wait(() => page.evaluate(() => document.querySelector('#browser-status')?.dataset.code === 'Ready'), Math.max(1, deadline - Date.now()));
      const current = await snapshot(), signs = current.elements.filter(peer => peer.name === 'Open trusted CAKE ID sign-in' && peer.role === 'button');
      fixed(signs.length === 1 && signs[0].enabled && privateAbsent(current));
      return { actualNativeReady: true, privateDataAbsent: true, actualConfiguredSignInEnabled: true };
    });
    await test(names[1], async () => {
      mark('physical-native-signin');
      const popupPromise = page.waitForEvent('popup', { timeout: 15000 });
      popupPromise.catch(() => {}); // A failed physical click must not leave an unhandled URL-bearing timeout.
      await physical('Open trusted CAKE ID sign-in'); const popup = await popupPromise;
      mark('actual-issuer-signin-form');
      await popup.locator('#sign-in-form').waitFor({ state: 'visible', timeout: 20000 }); fixed(new URL(popup.url()).origin === issuerOrigin);
      await popup.locator('#email').fill(fixture.accounts[0].email); await popup.locator('#password').fill(fixture.accounts[0].password);
      await popup.locator('#sign-in-form button[type=submit]').click();
      mark('actual-issuer-consent');
      await popup.locator('#consent').waitFor({ state: 'visible', timeout: 20000 }); fixed(new URL(popup.url()).origin === issuerOrigin);
      const closed = popup.waitForEvent('close', { timeout: 30000 }); closed.catch(() => {}); await popup.locator('#allow').click();
      mark('actual-callback-exchange-and-broker-close'); await closed;
      await wait(async () => { await drainObservations(); return observed.token > 0 && observed.current > 0; });
      fixed(observed.protocolValid && observed.sameSubject && observed.authorize > 0 && observed.callback === 1 && observed.discovery > 0 && observed.jwks > 0 && observed.token === 1);
      return { physicalNativeSignIn: true, genuineIssuerUIAndConsent: true, freshS256StateNonceAndCodeBinding: true, realTokenExchangeAndJOSEConsumer: true, realSelfVerification: true };
    });
    await test(names[2], async () => {
      await wait(async () => { const actual = await snapshot(); await drainObservations(); return observed.profile > 0 && observed.sessions > 0 && ['Profile name', 'Canonical account ID', 'Canonical session ID', 'Current profile revision'].every(name => actual.elements.some(peer => peer.name === name)); });
      fixed(observed.sameSubject); const current = await peer('Check current account, profile and sessions'); oldId = current.item.id;
      return { freshNativePrivateOwner: true, realCanonicalCurrentProfileSessions: true, allReadbacksSameFictionalSubject: true };
    });
    await test(names[3], async () => {
      mark('physical-review'); await physical('Review sign out of the current session');
      await wait(async () => (await snapshot()).elements.some(peer => peer.name === 'Session change confirmation'));
      mark('physical-confirm'); await physical('Confirm the reviewed session change');
      await wait(async () => { await drainObservations(); const current = await snapshot(); return observed.signout === 1 && privateAbsent(current) && current.elements.some(peer => peer.name === 'Open trusted CAKE ID sign-in' && peer.enabled); });
      return { actualReviewedConfirmation: true, actualServer204: true, nativePrivateDataCleared: true, freshRevalidationRequired: true };
    });
    await test(names[4], async () => {
      fixed(await native('stale', oldId) === false); fixed(privateAbsent(await snapshot()));
      tokenlessRequests = observed.apiRequests; await physical('Check current account, profile and sessions');
      await peer('Check current account, profile and sessions'); await drainObservations();
      fixed(privateAbsent(await snapshot()) && observed.apiRequests === tokenlessRequests);
      return { oldPeerRefused: true, physicalTokenlessNativeCheck: true, noNewBearerRequestOrPrivateGrant: true };
    });
    await test(names[5], async () => {
      fixed(await native('CloseShell') === true); await drainObservations();
      fixed(observed.apiRequests === tokenlessRequests); // The actual owner/broker join also covers the preceding native check.
      fixed((await snapshot()).elements.length === 0 && await native('stale', oldId) === false && await page.locator('#native-control-semantics').count() === 0);
      fixed(await native('CloseShell') === true);
      return { actualNativeCloseAndExistingBrokerJoinReturnedTrue: true, nativeAXEmpty: true, projectionRemoved: true, repeatCloseTrue: true };
    });
  } catch (error) {
    if (active.endsWith('preflight')) report.setupFailure = safeError(error);
    process.exitCode = 1;
  } finally {
    active = names[6]; const failures = [];
    for (const [name, close] of [['context', () => context?.close()], ['browser', () => browser?.close()], ['server', () => server ? new Promise((yes, no) => server.close(error => error ? no(error) : yes())) : null]]) try { await close(); } catch { failures.push(name); }
    try { await drainObservations(); } catch { failures.push('observations'); }
    try { fixed(gate && verifyPublicBinding(bindingPath, expectedCommit).bindingSha256 === gate.bindingSha256); owned?.unchanged(); } catch { failures.push('source-seal-or-owned-fixture-changed'); }
    if (keyMetadata) try { const stat = fs.lstatSync(process.env.B5_CLIENT_TLS_KEY); fixed(!stat.isSymbolicLink() && stat.isFile() && (stat.mode & 0o777) === 0o600 && Object.entries(keyMetadata).every(([name, value]) => stat[name] === value)); } catch { failures.push('private-tls-key-metadata'); }
    if (server) try { const proof = net.createServer(); await new Promise((yes, no) => { proof.once('error', no); proof.listen(5096, '127.0.0.1', yes); }); await new Promise((yes, no) => proof.close(error => error ? no(error) : yes())); report.port5096RebindClosed = true; } catch { failures.push('owned-port-closure'); }
    report.cleanupFailures = failures;
    const clean = !!browser && !failures.length && Object.values(report.unexpected).every(value => value === 0);
    report.outcomes[names[6]] = { status: clean ? 'PASS' : 'FAIL', observed: { exactAssetsAndNormalBrowserClose: clean, issuerCustodyOwnedBySeparateOriginalRootTTY: true } };
    report.protocolAndReadCounts = observed; // Counts/booleans only, never credentials, tokens, IDs, URLs or bodies.
    report.counts = { discovered: names.length, executed: Object.values(report.outcomes).filter(item => item.status !== 'NOT_RUN').length, passed: Object.values(report.outcomes).filter(item => item.status === 'PASS').length, failed: Object.values(report.outcomes).filter(item => item.status === 'FAIL').length, notRun: Object.values(report.outcomes).filter(item => item.status === 'NOT_RUN').length };
    if (report.counts.passed !== names.length) process.exitCode = 1;
    if (fs.existsSync(output)) write();
    console.log(JSON.stringify({ scope: report.scope, counts: report.counts, setupFailure: report.setupFailure, unexpected: report.unexpected, cleanupFailures: failures }));
    state = nonce = challenge = callbackCode = null;
  }
}
module.exports = { verifyPublicBinding, readOwnedFixture, privateAbsent, withoutCredentialDiagnostics };
if (require.main === module) run(...process.argv.slice(2)).catch(() => { console.log(JSON.stringify({ status: 'FAIL', stage: 'bounded-runner' })); process.exitCode = 1; });
