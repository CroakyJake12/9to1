'use strict';
// Preflight filesystem controls only. No browser, issuer, tokens or simulated sign-in state.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const { verifyPublicBinding, verifyCheckoutSource, readOwnedFixture, withoutCredentialDiagnostics, classifyFormError, classifyLaunchError, verifyChromiumSocketTempRoot, probeNativeFetchReceiver, installPublicStatusObserver, createPublicNetworkDiagnostics } = require('./run-local-auth-current.cjs');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const contract = JSON.parse(fs.readFileSync(path.join(__dirname, 'source-contract.json')));
const repo = path.resolve(__dirname, '../../../..');
const sandbox = fs.mkdtempSync(path.join(os.tmpdir(), 'c2-local-auth-preflight-'));
const fixtureCheckout = path.join(sandbox, 'fixture-checkout');
const fixtureRoot = path.join(fixtureCheckout, 'cloud/cake-id-auth');
const bundle = path.join(sandbox, 'public-assets');
fs.mkdirSync(bundle, { recursive: true });
const fixtures = contract.requiredFixtureSource.map(row => {
  const file = path.join(fixtureRoot, row.path);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.copyFileSync(path.join(repo, 'cloud/cake-id-auth', row.path), file);
  return { path: file, bytes: row.bytes, sha256: row.sha256 };
});
const dependencyRoot = path.join(sandbox, 'public-dependency-preflight-only'); fs.mkdirSync(dependencyRoot);
fs.symlinkSync(dependencyRoot, path.join(fixtureRoot, 'node_modules'));
const git = (checkout, args) => {
  const result = spawnSync('git', ['-C', checkout, ...args], { encoding: 'utf8', env: withoutCredentialDiagnostics(process.env) });
  assert.equal(result.status, 0); return result.stdout.trim();
};
git(fixtureCheckout, ['init', '--quiet']);
git(fixtureCheckout, ['add', '--', ...contract.requiredFixtureSource.map(row => 'cloud/cake-id-auth/' + row.path)]);
git(fixtureCheckout, ['-c', 'user.name=Public preflight control', '-c', 'user.email=preflight@example.test', 'commit', '--quiet', '-m', 'Public preflight source only']);
const fixtureCommit = git(fixtureCheckout, ['rev-parse', 'HEAD']);
const testCommit = git(repo, ['rev-parse', 'HEAD']);
const testSourcePins = ['README.md', 'preflight.test.cjs', 'preparation_test.py', 'prepare-local-auth-plan.py', 'run-local-auth-current.cjs', 'source-contract.json'].map(name => {
  const file = path.join(__dirname, name), bytes = fs.readFileSync(file);
  return { path: 'apps/Web/Tests/LocalAuth/' + name, bytes: bytes.length, sha256: sha(bytes) };
}).concat(contract.requiredSource);
fs.writeFileSync(path.join(bundle, 'index.html'), '<!-- preflight toy asset, never launched -->');
fs.writeFileSync(path.join(bundle, 'main.js'), '// preflight toy asset, no authentication simulation');
const manifestPath = path.join(sandbox, 'manifest.json');
const catalogPath = path.join(sandbox, 'source-before.json');
const afterPath = path.join(sandbox, 'source-after.json');
const bindingPath = path.join(sandbox, 'binding.json');
const sealPath = path.join(sandbox, 'seal.json'), archivePath = path.join(sandbox, 'wwwroot.zip');
const commit = 'a'.repeat(40);
const manifest = { sourceCommit: commit, sourceCommitAfter: commit, exitCode: 0, sourceInputs: contract.requiredSource.length, sourceInputsAfter: contract.requiredSource.length, addedSourceInputs: [], removedSourceInputs: [], changedSourceInputs: [], trackedChangesAfter: [], publishRoot: bundle, fileCount: 2, publishFiles: ['index.html', 'main.js'].map(name => { const bytes = fs.readFileSync(path.join(bundle, name)); return { path: name, bytes: bytes.length, sha256: sha(bytes) }; }) };
const originalCatalog = JSON.stringify(contract.requiredSource, null, 2) + '\n';
fs.writeFileSync(catalogPath, originalCatalog); fs.writeFileSync(afterPath, originalCatalog);
fs.writeFileSync(manifestPath, JSON.stringify(manifest));
const zipResult = spawnSync('python3', ['-B', '-c', 'import pathlib,sys,zipfile\nroot=pathlib.Path(sys.argv[1])\nwith zipfile.ZipFile(sys.argv[2],"x",zipfile.ZIP_DEFLATED) as z:\n for name in ["index.html","main.js"]: z.write(root/name,name)', bundle, archivePath]);
assert.equal(zipResult.status, 0);
const originalSeal = { sourceCommit: commit, receiptSha256: sha(fs.readFileSync(manifestPath)), zipSha256: sha(fs.readFileSync(archivePath)), zipBytes: fs.statSync(archivePath).size, fileCount: 2 };
fs.writeFileSync(sealPath, JSON.stringify(originalSeal));
const originalBinding = { sourceCommit: commit, manifestPath, manifestSha256: sha(fs.readFileSync(manifestPath)), originalManifestPath: manifestPath, originalManifestSha256: sha(fs.readFileSync(manifestPath)), originalSealPath: sealPath, originalSealSha256: sha(fs.readFileSync(sealPath)), originalArchivePath: archivePath, originalArchiveSha256: sha(fs.readFileSync(archivePath)), fileCount: 2, sourceCatalogPath: catalogPath, sourceCatalogAfterPath: afterPath, sourceCatalogSha256: sha(fs.readFileSync(catalogPath)), sourceCatalogAfterSha256: sha(fs.readFileSync(afterPath)), runnerSha256: sha(fs.readFileSync(path.join(__dirname, 'run-local-auth-current.cjs'))), hostSha256: sha(fs.readFileSync(path.join(__dirname, '../ci/sealed-browser-replay/sealed-https-host-explicit-binding.cjs'))), contractSha256: sha(fs.readFileSync(path.join(__dirname, 'source-contract.json'))), testSourceCommit: testCommit, testCheckout: repo, testSourcePins, fixtureSourceCommit: fixtureCommit, fixtureCheckout, fixtureSourcePins: fixtures, fixtureDependencyRoot: dependencyRoot, fixtureDependencyLinks: [] };
const writeBinding = change => fs.writeFileSync(bindingPath, JSON.stringify({ ...originalBinding, ...change }));
const gate = change => { writeBinding(change); return verifyPublicBinding(bindingPath, commit); };
test.after(() => fs.rmSync(sandbox, { recursive: true }));

test('public preflight accepts exact inventories only; no browser or issuer execution', () => {
  const result = gate(); assert.equal(result.seal.fileCount, 2); assert.equal(result.fixtureRoot, fixtureRoot);
});
test('credential-bearing PW debug/preload inheritance is disabled before browser launch', () => {
  const inherited = { DEBUG: 'pw:api', PWDEBUG: '1', NODE_DEBUG: '*', NODE_OPTIONS: '--require unreviewed-hook', PATH: 'original-public-tool-path' };
  const safe = withoutCredentialDiagnostics(inherited);
  for (const name of ['DEBUG', 'PWDEBUG', 'NODE_DEBUG', 'NODE_OPTIONS']) assert.equal(safe[name], '');
  assert.equal(safe.PATH, inherited.PATH); assert.equal(inherited.DEBUG, 'pw:api');
});
test('invalid explicit candidate refuses before even reading public binding', () => {
  const original = fs.readFileSync; let reads = 0;
  fs.readFileSync = (...args) => { reads++; return original(...args); };
  try { assert.throws(() => verifyPublicBinding(bindingPath, commit + '\n')); assert.equal(reads, 0); }
  finally { fs.readFileSync = original; }
});
test('actual CLI invalid candidate redacts errors and precedes private read/PW import/listener', () => {
  const output = path.join(sandbox, 'never-created');
  const marker = 'PRIVATE_TRIPWIRE_NOT_FOR_OUTPUT';
  const tripwire = path.join(sandbox, 'playwright-tripwire.cjs');
  fs.writeFileSync(tripwire, `throw new Error('${marker}');`);
  const result = spawnSync(process.execPath, [path.join(__dirname, 'run-local-auth-current.cjs'), path.join(sandbox, 'absent-binding'), output, commit + '\n', path.join(sandbox, marker), path.join(sandbox, marker)], { encoding: 'utf8', env: { ...process.env, PLAYWRIGHT_MODULE: tripwire } });
  assert.equal(result.status, 1); assert.equal(result.stderr, ''); assert(!result.stdout.includes(marker)); assert(!fs.existsSync(output));
  const safe = JSON.parse(result.stdout.trim()); assert.equal(safe.setupFailure.stage, 'public-preflight'); assert.equal(safe.counts.passed, 0);
});
test('mismatched candidate and publication receipt hash refuse', () => {
  assert.throws(() => gate({ sourceCommit: 'b'.repeat(40) }));
  assert.throws(() => gate({ manifestSha256: '0'.repeat(64) }));
});
test('unreviewed runner or source contract refuses', () => {
  assert.throws(() => gate({ runnerSha256: '0'.repeat(64) }));
  assert.throws(() => gate({ contractSha256: '0'.repeat(64) }));
});
test('separate actual test and fixture HEADs refuse a caller-invented label', () => {
  assert.throws(() => gate({ testSourceCommit: 'b'.repeat(40) }));
  assert.throws(() => gate({ fixtureSourceCommit: 'b'.repeat(40) }));
});
test('all exact105 source bodies remain required despite separate source identities', () => {
  assert.throws(() => gate({ testSourcePins: testSourcePins.slice(1) }));
  const changed = structuredClone(testSourcePins); changed.at(-1).sha256 = '0'.repeat(64);
  assert.throws(() => gate({ testSourcePins: changed }));
});
test('actual Git body check refuses rehashed working-tree edits', () => {
  const row = contract.requiredFixtureSource.find(row => row.path === 'tests/browser-fixture.mjs');
  const file = path.join(fixtureRoot, row.path), bytes = fs.readFileSync(file);
  fs.appendFileSync(file, '\n// alternate current body\n');
  try { assert.throws(() => verifyCheckoutSource(fixtureCheckout, fixtureCommit, [{ path: 'cloud/cake-id-auth/' + row.path, bytes: fs.statSync(file).size, sha256: sha(fs.readFileSync(file)) }])); }
  finally { fs.writeFileSync(file, bytes); }
});
test('redirected selected source refuses before any target contents are read', () => {
  const row = contract.requiredFixtureSource.find(row => row.path === 'tests/browser-fixture.mjs');
  const file = path.join(fixtureRoot, row.path), bytes = fs.readFileSync(file), redirected = path.join(sandbox, 'redirected-public-control.js');
  fs.writeFileSync(redirected, 'Synthetic redirected control only; no private material.');
  fs.rmSync(file); fs.symlinkSync(redirected, file);
  const original = fs.readFileSync; let selectedReads = 0;
  fs.readFileSync = (...args) => { if (args[0] === file || args[0] === redirected) selectedReads++; return original(...args); };
  try {
    assert.throws(() => verifyCheckoutSource(fixtureCheckout, fixtureCommit, [{ ...row, path: 'cloud/cake-id-auth/' + row.path }]));
    assert.equal(selectedReads, 0);
  } finally { fs.readFileSync = original; fs.rmSync(file); fs.writeFileSync(file, bytes); fs.rmSync(redirected); }
});
test('form errors produce fixed categories only, even with credential-bearing text or getters', () => {
  const marker = 'PRIVATE_TRIPWIRE_NOT_FOR_OUTPUT';
  assert.equal(classifyFormError(new Error('Target page, context or browser has been closed ' + marker)), 'TARGET_CLOSED');
  assert.equal(classifyFormError(Object.assign(new Error(marker), { name: 'TimeoutError' })), 'TIMEOUT');
  assert.equal(classifyFormError(new Error(marker)), 'OTHER');
  assert.equal(classifyFormError({ get message() { throw new Error(marker); } }), 'OTHER');
});
test('actual canonical Chromium TMPDIR accepts62 UTF8 bytes and refuses63 before spawn', () => {
  const base = Buffer.byteLength(sandbox) + 1;
  const safe = path.join(sandbox, 'a'.repeat(62 - base)), long = path.join(sandbox, 'b'.repeat(63 - base));
  fs.mkdirSync(safe); fs.mkdirSync(long);
  assert.equal(verifyChromiumSocketTempRoot(safe).socketPathBytes, 107);
  assert.throws(() => verifyChromiumSocketTempRoot(long));
  const redirected = path.join(sandbox, 'tmp-alias'); fs.symlinkSync(safe, redirected);
  assert.throws(() => verifyChromiumSocketTempRoot(redirected));
});
test('actual Unicode TMPDIR is measured by UTF8 bytes rather than character count', () => {
  const name = path.join(sandbox, 'é'.repeat(Math.ceil((63 - Buffer.byteLength(sandbox) - 1) / 2)));
  fs.mkdirSync(name);
  assert(name.length < 63 && Buffer.byteLength(name) >= 63);
  assert.throws(() => verifyChromiumSocketTempRoot(name));
});
test('only exact singleton launch marker survives otherwise private error output', () => {
  const marker = 'PRIVATE_TRIPWIRE_NOT_FOR_OUTPUT';
  assert.equal(classifyLaunchError(new Error('Socket path too long: /' + marker + '/SingletonSocket.')), 'SINGLETON_SOCKET_PATH_TOO_LONG');
  assert.equal(classifyLaunchError(new Error(marker)), 'OTHER');
  assert.equal(classifyLaunchError({ get message() { throw new Error(marker); } }), 'OTHER');
});
test('fetch receiver probe uses only an invalid absolute URL and redacts both error boundaries', async () => {
  const vm = require('node:vm'); const marker = 'PRIVATE_TRIPWIRE_NOT_FOR_OUTPUT'; let calls = 0;
  const scope = { URL, isWindowReceiver: true, fetch: function (value) {
    calls++; assert.equal(value, 'http://['); assert.throws(() => new URL(value));
    if (!this?.isWindowReceiver) throw new TypeError('Illegal invocation ' + marker);
    return Promise.reject(new TypeError('Failed to parse URL ' + marker));
  } };
  const observed = await vm.runInNewContext('(' + probeNativeFetchReceiver.toString() + ')()', scope);
  assert.equal(calls, 2); assert.equal(observed.invalidURLRejected, true);
  assert.equal(observed.window.classification, 'INVALID_URL'); assert.equal(observed.window.returnedPromise, true);
  assert.equal(observed.other.classification, 'ILLEGAL_INVOCATION'); assert.equal(observed.other.synchronousThrow, true);
  assert(!JSON.stringify(observed).includes(marker));
});
test('fetch receiver probe refuses a supposedly parseable URL before calling any fetch', async () => {
  const vm = require('node:vm'); let calls = 0;
  const observed = await vm.runInNewContext('(' + probeNativeFetchReceiver.toString() + ')()', { URL: class {}, fetch() { calls++; throw new Error('Unexpected request'); } });
  assert.equal(observed.invalidURLRejected, false); assert.equal(observed.window, null); assert.equal(observed.other, null); assert.equal(calls, 0);
});
test('read-only public status observer excludes unknown text and bounds its history', () => {
  const vm = require('node:vm'); let capture;
  const current = { dataset: { code: 'Loading' } }, win = {};
  const scope = { location: { origin: 'https://client.example.test:5096' }, window: win, document: { querySelector: () => current }, MutationObserver: class { constructor(callback) { capture = callback; } observe() {} } };
  vm.runInNewContext('(' + installPublicStatusObserver.toString() + ')({origin:"https://client.example.test:5096"})', scope);
  current.dataset.code = 'PRIVATE_TRIPWIRE_NOT_FOR_OUTPUT'; capture();
  for (let index = 0; index < 40; index++) { current.dataset.code = index % 2 ? 'Ready' : 'PermissionRequired'; capture(); }
  const state = win.__teamCLocalAuthPublicStatus;
  assert.equal(state.codes.length, 32); assert.equal(state.truncated, true); assert(state.codes.includes('OTHER'));
  assert(!JSON.stringify(state).includes('PRIVATE_TRIPWIRE'));
  const foreign = {}; vm.runInNewContext('(' + installPublicStatusObserver.toString() + ')({origin:"https://client.example.test:5096"})', { ...scope, location: { origin: 'http://127.0.0.1:8799' }, window: foreign });
  assert.equal(foreign.__teamCLocalAuthPublicStatus, undefined);
});
test('changed receiving critical source refuses even if caller rehashes both catalogs', () => {
  const changed = structuredClone(contract.requiredSource); changed[0].sha256 = '0'.repeat(64);
  const bytes = JSON.stringify(changed); fs.writeFileSync(catalogPath, bytes); fs.writeFileSync(afterPath, bytes);
  try { assert.throws(() => gate({ sourceCatalogSha256: sha(bytes), sourceCatalogAfterSha256: sha(bytes) })); }
  finally { fs.writeFileSync(catalogPath, originalCatalog); fs.writeFileSync(afterPath, originalCatalog); }
});
test('before/after source mismatch and recorded source mutation refuse', () => {
  fs.writeFileSync(afterPath, originalCatalog + ' ');
  try { assert.throws(() => gate({ sourceCatalogAfterSha256: sha(fs.readFileSync(afterPath)) })); }
  finally { fs.writeFileSync(afterPath, originalCatalog); }
  const bytes = JSON.stringify({ ...manifest, changedSourceInputs: ['critical-source'] }); fs.writeFileSync(manifestPath, bytes);
  const sealBytes = JSON.stringify({ ...originalSeal, receiptSha256: sha(bytes) }); fs.writeFileSync(sealPath, sealBytes);
  try { assert.throws(() => gate({ manifestSha256: sha(bytes), originalManifestSha256: sha(bytes), originalSealSha256: sha(sealBytes) })); }
  finally { fs.writeFileSync(manifestPath, JSON.stringify(manifest)); fs.writeFileSync(sealPath, JSON.stringify(originalSeal)); }
});
test('actual full asset body mutation and unlisted asset refuse', () => {
  const file = path.join(bundle, 'main.js'), bytes = fs.readFileSync(file); fs.appendFileSync(file, 'changed');
  try { assert.throws(() => gate()); } finally { fs.writeFileSync(file, bytes); }
  const extra = path.join(bundle, 'unlisted.js'); fs.writeFileSync(extra, 'unlisted');
  try { assert.throws(() => gate()); } finally { fs.rmSync(extra); }
});
test('public asset symlink refuses even if its target bytes match', () => {
  const file = path.join(bundle, 'main.js'), target = path.join(sandbox, 'outside.js'), bytes = fs.readFileSync(file);
  fs.writeFileSync(target, bytes); fs.rmSync(file); fs.symlinkSync(target, file);
  try { assert.throws(() => gate()); } finally { fs.rmSync(file); fs.writeFileSync(file, bytes); }
});
test('fixture source missing body or caller-rehashed alternate implementation refuses', () => {
  assert.throws(() => gate({ fixtureSourcePins: fixtures.slice(1) }));
  const file = fixtures.find(row => row.path.endsWith('/tests/browser-fixture.mjs')), bytes = fs.readFileSync(file.path);
  fs.appendFileSync(file.path, '\n// unreviewed alternate\n');
  try { assert.throws(() => gate({ fixtureSourcePins: fixtures.map(row => row === file ? { ...row, bytes: fs.statSync(row.path).size, sha256: sha(fs.readFileSync(row.path)) } : row) })); }
  finally { fs.writeFileSync(file.path, bytes); }
});

const runRoot = path.join(fixtureRoot, '.local-run', '0123456789abcdef');
const privateManifest = path.join(runRoot, 'browser-fixture-private.json');
fs.mkdirSync(runRoot, { recursive: true, mode: 0o700 }); fs.chmodSync(runRoot, 0o700);
const privateBody = { fixtureOnly: true, runId: path.basename(runRoot), persistPath: runRoot, issuer: 'http://127.0.0.1:8799/api/auth', apiResource: 'http://127.0.0.1:8799', browserOrigin: 'https://client.example.test:5096', redirectUri: 'https://client.example.test:5096/callback', clientId: 'preflight-only-not-issued', scopes: 'openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke', discovery: { issuer: 'http://127.0.0.1:8799/api/auth', authorization_endpoint: 'http://127.0.0.1:8799/api/auth/oauth2/authorize', token_endpoint: 'http://127.0.0.1:8799/api/auth/oauth2/token', jwks_uri: 'http://127.0.0.1:8799/api/auth/jwks' }, accounts: [0, 1].map(index => ({ id: crypto.randomUUID(), email: `preflight-${index}@example.test`, password: 'preflight-only-never-used-in-a-browser' })) };
const writePrivate = change => { fs.writeFileSync(privateManifest, JSON.stringify({ ...privateBody, ...change }), { mode: 0o600 }); fs.chmodSync(privateManifest, 0o600); };
test('owned private-file contract reads only exact restrictive run, without authenticating', () => {
  writePrivate(); const owned = readOwnedFixture(privateManifest, runRoot, { fixtureRoot }); assert.equal(owned.fixture.accounts.length, 2); owned.unchanged();
});
test('foreign path and permissive private mode refuse', () => {
  writePrivate(); assert.throws(() => readOwnedFixture(privateManifest, path.dirname(runRoot), { fixtureRoot }));
  fs.chmodSync(privateManifest, 0o644); try { assert.throws(() => readOwnedFixture(privateManifest, runRoot, { fixtureRoot })); } finally { fs.chmodSync(privateManifest, 0o600); }
});
test('wrong port, external discovery and non-fictional account refuse', () => {
  writePrivate({ issuer: 'http://127.0.0.1:8798/api/auth' }); assert.throws(() => readOwnedFixture(privateManifest, runRoot, { fixtureRoot }));
  writePrivate({ discovery: { ...privateBody.discovery, jwks_uri: 'https://outside.example/jwks' } }); assert.throws(() => readOwnedFixture(privateManifest, runRoot, { fixtureRoot }));
  writePrivate({ accounts: [{ ...privateBody.accounts[0], email: 'person@outside.example' }, privateBody.accounts[1]] }); assert.throws(() => readOwnedFixture(privateManifest, runRoot, { fixtureRoot }));
});
test('private mutation remains an observable failure after read', () => {
  writePrivate(); const owned = readOwnedFixture(privateManifest, runRoot, { fixtureRoot }); fs.appendFileSync(privateManifest, ' '); assert.throws(() => owned.unchanged());
});

const publicEndpoints = { issuer: 'http://127.0.0.1:8799/api/auth', jwks: 'http://127.0.0.1:8799/api/auth/jwks', authorize: 'http://127.0.0.1:8799/api/auth/oauth2/authorize', token: 'http://127.0.0.1:8799/api/auth/oauth2/token' };
const privateTripwire = 'PRIVATE_TRIPWIRE_NOT_FOR_OUTPUT';
const syntheticRequest = (url, method = 'GET', resource = 'document', failure = 'net::ERR_ABORTED') => ({ url: () => url, method: () => method, resourceType: () => resource, failure: () => ({ errorText: failure }) });
test('public boundary diagnostics classify callback and each current public endpoint without query retention', () => {
  const observer = createPublicNetworkDiagnostics(publicEndpoints);
  const paths = [['https://client.example.test:5096/callback', 'CALLBACK'], [publicEndpoints.issuer + '/.well-known/openid-configuration', 'DISCOVERY'], [publicEndpoints.jwks, 'JWKS'], [publicEndpoints.authorize, 'AUTHORIZE'], [publicEndpoints.token, 'TOKEN'], ['http://127.0.0.1:8799/api/account/current', 'CURRENT'], ['http://127.0.0.1:8799/api/account/profile', 'PROFILE'], ['http://127.0.0.1:8799/api/account/sessions', 'SESSIONS'], ['http://127.0.0.1:8799/api/account/signout', 'SIGNOUT'], ['http://127.0.0.1:8799/assets/auth-ui.js', 'ISSUER_UI']];
  for (const [url, expected] of paths) assert.equal(observer.classify(url + '?code=' + privateTripwire + '&state=' + privateTripwire), expected);
  assert.equal(observer.classify('https://foreign.example/' + privateTripwire), 'OTHER');
  assert.equal(observer.classify('https://user:' + privateTripwire + '@client.example.test:5096/callback'), 'OTHER');
  assert(!JSON.stringify(observer.state).includes(privateTripwire));
});
test('callback diagnostics distinguish document/fetch methods, HTTP statuses and exact fixed cancellation phase', () => {
  const observer = createPublicNetworkDiagnostics(publicEndpoints), url = 'https://client.example.test:5096/callback?code=' + privateTripwire;
  observer.record('request', syntheticRequest(url), null, 'PKCE');
  observer.record('request', syntheticRequest(url, 'OPTIONS', 'fetch'), null, 'PKCE');
  observer.record('response', syntheticRequest(url), 200, 'PKCE');
  observer.record('response', syntheticRequest(url), 307, 'PKCE');
  observer.record('finished', syntheticRequest(url), null, 'PKCE');
  observer.record('failed', syntheticRequest(url), null, 'PKCE');
  assert.deepEqual(observer.state.boundaries.CALLBACK, { events: { request: 2, response: 2, finished: 1, failed: 1 }, methods: { GET: 1, OPTIONS: 1 }, resources: { document: 1, fetch: 1 }, statuses: { 200: 1, 307: 1 }, failures: { ERR_ABORTED: 1 }, failedPhases: { PKCE: 1 } });
  assert(!JSON.stringify(observer.state).includes(privateTripwire));
});
test('unknown network fields and throwing getters cannot print private errors, URL or failure text', () => {
  const observer = createPublicNetworkDiagnostics(publicEndpoints);
  const request = syntheticRequest('https://foreign.example/' + privateTripwire, privateTripwire, privateTripwire, privateTripwire);
  observer.record('request', request, null, privateTripwire); observer.record('failed', request, null, privateTripwire); observer.record('response', request, privateTripwire, privateTripwire);
  const throws = () => { throw new Error(privateTripwire); };
  observer.record('failed', { url: throws, method: throws, resourceType: throws, failure: throws }, null, privateTripwire);
  const other = observer.state.boundaries.OTHER;
  assert.equal(other.methods.OTHER, 1); assert.equal(other.resources.OTHER, 1); assert.equal(other.statuses.OTHER, 1); assert.equal(other.failures.OTHER, 2); assert.equal(other.failedPhases.OTHER, 2);
  assert(!JSON.stringify(observer.state).includes(privateTripwire));
});
test('console diagnostics retain only fixed startup/resource codes and known origin categories', () => {
  const observer = createPublicNetworkDiagnostics(publicEndpoints);
  for (const text of ['9to1 browser startup failed. ' + privateTripwire, 'Failed to load resource: ' + privateTripwire, privateTripwire]) observer.consoleError({ text: () => text, location: () => ({ url: 'https://client.example.test:5096/main.js?token=' + privateTripwire }) });
  observer.consoleError({ text() { throw new Error(privateTripwire); }, location() { throw new Error(privateTripwire); } });
  assert.deepEqual(observer.state.consoleCodes, { CLIENT_STARTUP_FAILED: 1, RESOURCE_LOAD_FAILED: 1, OTHER: 2 });
  assert.deepEqual(observer.state.consoleBoundaries, { CLIENT_ASSET: 3, OTHER: 1 });
  assert(!JSON.stringify(observer.state).includes(privateTripwire));
});
test('network diagnostics refuse unknown events and bound repeated counts', () => {
  const observer = createPublicNetworkDiagnostics(publicEndpoints), request = syntheticRequest('https://client.example.test:5096/callback');
  assert.throws(() => observer.record(privateTripwire, request, null, 'PKCE'));
  for (let count = 0; count < 10002; count++) observer.record('failed', request, null, 'PKCE');
  assert.equal(observer.state.boundaries.CALLBACK.events.failed, 10000); assert.equal(observer.state.saturated, true);
  assert(!JSON.stringify(observer.state).includes(privateTripwire));
});
test('both revised issuer bodies refuse an alternate caller-rehashed fixture implementation', () => {
  for (const ending of ['/src/browser.ts', '/public/auth-ui.txt']) {
    const row = fixtures.find(item => item.path.endsWith(ending)), bytes = fs.readFileSync(row.path);
    fs.appendFileSync(row.path, '\n// alternate source ' + privateTripwire + '\n');
    try { assert.throws(() => gate({ fixtureSourcePins: fixtures.map(item => item === row ? { ...item, bytes: fs.statSync(row.path).size, sha256: sha(fs.readFileSync(row.path)) } : item) })); }
    finally { fs.writeFileSync(row.path, bytes); }
  }
});
