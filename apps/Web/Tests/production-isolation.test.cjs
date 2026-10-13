// Actual immutable-bundle scan plus controlled leaks in disposable directories.
// No provider, account, browser, SDK or production-record mutation is performed.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const assert = require('node:assert/strict');
const { verifyProductionAssets, verifyProductionProjectClosure } = require('./verify-production-isolation.cjs');
const { verifyPublishReceipt } = require('./verify-publish-receipt.cjs');
assert.equal(process.argv.length, 6,
  'Usage: node production-isolation.test.cjs <actual-wwwroot> <actual-project.assets.json> <entry.csproj> <new-evidence-directory>');
const [bundle, projectAssets, entryProject, output] = process.argv.slice(2);
assert(!fs.existsSync(output), 'A new evidence directory is required'); fs.mkdirSync(output, { recursive: true });
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const report = { scope: 'Actual published assets and current restored project graph; controlled failure samples only. No authenticated/runtime/fresh-account acceptance.',
  runnerSha256: hash(fs.readFileSync(__filename)),
  verifierSha256: hash(fs.readFileSync(path.join(__dirname, 'verify-production-isolation.cjs'))),
  receiptVerifierSha256: hash(fs.readFileSync(path.join(__dirname, 'verify-publish-receipt.cjs'))),
  startedAt: new Date().toISOString(), checks: [], accountStorageRead: false, sourceBundleWritten: false, sdkStarted: false };
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'production-isolation-controls-'));
const save = () => fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
function check(name, action) {
  try { const observed = action(); report.checks.push({ name, status: 'PASS', observed }); save(); return observed; }
  catch (error) { report.checks.push({ name, status: 'FAIL', error: error.stack }); save(); throw error; }
}
function rejects(name, action, expected) {
  return check(name, () => { let error; try { action(); } catch (actual) { error = actual; }
    assert(error, 'The controlled leak must fail'); assert.match(error.message, expected);
    return { rejected: true, reason: error.message }; });
}
let fixtureNumber = 0;
function fixture(files) {
  const root = path.join(temporary, String(++fixtureNumber)); fs.mkdirSync(root);
  for (const [relative, data] of Object.entries(files)) {
    const target = path.join(root, relative); fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, data);
  }
  return root;
}
try {
  report.actualAssetsBefore = check('actual supplied immutable production bundle excludes known visual fixture', () => verifyProductionAssets(bundle));
  report.actualClosure = check('actual restored production project-reference closure excludes visual/test projects',
    () => verifyProductionProjectClosure(projectAssets, entryProject));
  const ownership = 'RestaurantVisualBackend';
  const encodings = { utf8: Buffer.from(ownership), utf16le: Buffer.from(ownership, 'utf16le'),
    utf16be: Buffer.from(ownership, 'utf16le').swap16() };
  for (const [encoding, bytes] of Object.entries(encodings)) {
    for (const [compression, extension, transform] of [
      ['raw', '.wasm', value => value], ['gzip', '.wasm.gz', value => zlib.gzipSync(value)],
      ['brotli', '.wasm.br', value => zlib.brotliCompressSync(value)]]) {
      const root = fixture({ ['_framework/ordinary-name' + extension]: transform(bytes) });
      rejects(`${encoding} ${compression} renamed fixture backend is rejected`, () => verifyProductionAssets(root), /fixture backend/);
    }
  }
  rejects('gzip magic is checked even with a renamed suffix',
    () => verifyProductionAssets(fixture({ 'opaque.bin': zlib.gzipSync(Buffer.from(ownership)) })), /fixture backend/);
  rejects('fixture assembly is rejected by artifact ownership even without readable contents',
    () => verifyProductionAssets(fixture({ '_framework/BrowserSpaces.Visual.a1b2c3.wasm': Buffer.from([0,97,115,109]) })), /test\/visual ownership/);
  rejects('renamed split data seed is rejected independently of backend/assembly names',
    () => verifyProductionAssets(fixture({ 'one.js': 'const title="Nearby Restaurants";', 'two.dat': Buffer.from('Can you look for nearby restaurants?', 'utf16le') })),
    /combined visual reference data/);
  rejects('malformed compression cannot pass as clean production assets',
    () => verifyProductionAssets(fixture({ 'broken.wasm.br': Buffer.from([255,255,255]) })), /Cannot verify compressed/);
  check('genuine model names are not blacklisted', () => verifyProductionAssets(fixture({
    'models.json': JSON.stringify({ modelNames: ['Qwen 3.6', 'Qwen', 'qwen3:1.7b', 'hosted-ollama:qwen3:1.7b'] }) })));
  check('genuine production Visual directory is not treated as a fixture', () => verifyProductionAssets(fixture({
    'CUI/Visual/Rendering.js': '// genuine visual renderer', '_framework/Genuine.Cui.Visual.dll': 'actual product class name' })));
  const realAssets = JSON.parse(fs.readFileSync(projectAssets, 'utf8'));
  const contaminated = structuredClone(realAssets);
  contaminated.libraries['BrowserSpaces.Visual/1.0.0'] = { type: 'project',
    path: 'Spaces/Tests/Visual/BrowserSpaces.Visual.csproj', msbuildProject: 'Spaces/Tests/Visual/BrowserSpaces.Visual.csproj' };
  const badAssets = path.join(temporary, 'controlled-leaked-project.assets.json'); fs.writeFileSync(badAssets, JSON.stringify(contaminated));
  rejects('transitive visual project reference is rejected even if assets contain no seed text',
    () => verifyProductionProjectClosure(badAssets, entryProject), /test\/visual project/);
  const originalRestoreProject = realAssets.project.restore.projectPath;
  const otherAssets = structuredClone(realAssets); otherAssets.project.restore.projectPath = path.join(temporary, 'Different.csproj');
  const other = path.join(temporary, 'other.assets.json'); fs.writeFileSync(other, JSON.stringify(otherAssets));
  rejects('a clean graph for a different entry cannot qualify production',
    () => verifyProductionProjectClosure(other, entryProject), /different entry project/);
  const evaluated = { Items: { Compile: [{ FullPath: path.join(path.dirname(originalRestoreProject), 'Spaces/Tests/Visual/RestaurantVisualBackend.cs') }],
    ProjectReference: [{ FullPath: path.resolve(path.dirname(originalRestoreProject), Object.values(realAssets.libraries).find(item => item.type === 'project').path) }],
    ReferencePath: [{ FullPath: path.join(temporary, 'Genuine.dll') }] } };
  const evaluatedPath = path.join(temporary, 'controlled-evaluated-items.json'); fs.writeFileSync(evaluatedPath, JSON.stringify(evaluated));
  rejects('evaluated root linked Compile leak is rejected independently of project references',
    () => verifyProductionProjectClosure(projectAssets, entryProject, evaluatedPath), /Compile includes test\/visual input/);
  // A fully self-consistent receipt must still fail on a fixture leak. The guard
  // is on the existing publication verifier, not a separate optional assertion.
  const root = fixture({ 'index.html': '<html></html>', 'main.js': '// controlled bootstrap', '_framework/dotnet.js': '// loader',
    '_framework/renamed.wasm.br': zlib.brotliCompressSync(Buffer.from(ownership, 'utf16le')) });
  const publishFiles = ['index.html', 'main.js', '_framework/dotnet.js', '_framework/renamed.wasm.br'].map(name => {
    const bytes = fs.readFileSync(path.join(root, name)); return { path: name, bytes: bytes.length, sha256: hash(bytes) };
  });
  const receipt = { sourceCommit: 'a'.repeat(40), exitCode: 0, changedSourceInputs: [], trackedChangesAfter: [],
    scope: 'Controlled internally consistent leaked bundle; never published', publishRoot: root, publishFiles,
    fileCount: publishFiles.length, totalBytes: publishFiles.reduce((sum, file) => sum + file.bytes, 0) };
  const receiptPath = path.join(temporary, 'controlled-receipt.json'); fs.writeFileSync(receiptPath, JSON.stringify(receipt));
  rejects('existing receipt validator refuses an otherwise valid sealed fixture leak', () => verifyPublishReceipt({ receiptPath,
    bundlePath: root, expectedCommit: receipt.sourceCommit, expectedReceiptSha256: hash(fs.readFileSync(receiptPath)) }), /fixture backend/);
  report.actualAssetsAfter = check('actual immutable bundle remains unchanged after all controls', () => verifyProductionAssets(bundle));
  assert.deepEqual(report.actualAssetsAfter, report.actualAssetsBefore);
  assert.equal(hash(fs.readFileSync(projectAssets)), report.actualClosure.projectAssetsSha256);
} catch (error) { report.error = error.stack; process.exitCode = 1; }
finally {
  fs.rmSync(temporary, { recursive: true, force: true });
  report.temporaryControlsRemoved = !fs.existsSync(temporary); report.finishedAt = new Date().toISOString(); save();
  console.log(JSON.stringify({ passed: report.checks.filter(row => row.status === 'PASS').length,
    failed: report.checks.filter(row => row.status === 'FAIL').length, actual: report.actualAssetsBefore,
    projectClosure: report.actualClosure, temporaryControlsRemoved: report.temporaryControlsRemoved }, null, 2));
}
