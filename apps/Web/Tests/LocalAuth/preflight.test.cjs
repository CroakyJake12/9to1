'use strict';
// Preflight filesystem controls only. No browser, issuer, tokens or simulated sign-in state.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const { verifyPublicBinding, readOwnedFixture, withoutCredentialDiagnostics } = require('./run-local-auth-current.cjs');
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
const originalBinding = { sourceCommit: commit, manifestPath, manifestSha256: sha(fs.readFileSync(manifestPath)), originalManifestPath: manifestPath, originalManifestSha256: sha(fs.readFileSync(manifestPath)), originalSealPath: sealPath, originalSealSha256: sha(fs.readFileSync(sealPath)), originalArchivePath: archivePath, originalArchiveSha256: sha(fs.readFileSync(archivePath)), fileCount: 2, sourceCatalogPath: catalogPath, sourceCatalogAfterPath: afterPath, sourceCatalogSha256: sha(fs.readFileSync(catalogPath)), sourceCatalogAfterSha256: sha(fs.readFileSync(afterPath)), runnerSha256: sha(fs.readFileSync(path.join(__dirname, 'run-local-auth-current.cjs'))), hostSha256: sha(fs.readFileSync(path.join(__dirname, '../ci/sealed-browser-replay/sealed-https-host-explicit-binding.cjs'))), contractSha256: sha(fs.readFileSync(path.join(__dirname, 'source-contract.json'))), fixtureSourceCommit: commit, fixtureCheckout, fixtureSourcePins: fixtures, fixtureDependencyRoot: dependencyRoot, fixtureDependencyLinks: [] };
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
