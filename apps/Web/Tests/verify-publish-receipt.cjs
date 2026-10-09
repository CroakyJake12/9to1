// Read-only transport verification of the coordinator's publication receipt.
// This is not an app manifest, signature authority, or runtime acceptance gate.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');

const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const compare = (a, b) => a < b ? -1 : a > b ? 1 : 0;
function requireRegularFile(file) {
  assert(fs.lstatSync(file).isFile(), `Regular file required: ${file}`);
}
function inventory(directory, relative = '') {
  assert(fs.lstatSync(directory).isDirectory(), `Real directory required: ${directory}`);
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    const name = relative ? relative + '/' + entry.name : entry.name;
    const stat = fs.lstatSync(full);
    assert(!stat.isSymbolicLink(), `Links are not publication files: ${name}`);
    if (stat.isDirectory()) files.push(...inventory(full, name));
    else {
      assert(stat.isFile(), `Unsupported publication entry: ${name}`);
      const bytes = fs.readFileSync(full);
      files.push({ path: name, bytes: bytes.length, sha256: sha256(bytes) });
    }
  }
  return files.sort((a, b) => compare(a.path, b.path));
}
function safeRelative(name) {
  return typeof name === 'string' && name.length > 0 && !name.includes('\\') && !name.includes('\0')
    && !path.posix.isAbsolute(name) && !/^[A-Za-z]:/.test(name)
    && name.split('/').every(part => part !== '' && part !== '.' && part !== '..');
}
function verifyPublishReceipt({ receiptPath, bundlePath, expectedCommit, expectedReceiptSha256 }) {
  assert.match(expectedCommit ?? '', /^[0-9a-f]{40}$/, 'An independently supplied exact commit is required');
  assert.match(expectedReceiptSha256 ?? '', /^[0-9a-f]{64}$/, 'An independently supplied receipt SHA-256 is required');
  requireRegularFile(receiptPath);
  const raw = fs.readFileSync(receiptPath);
  assert.equal(sha256(raw), expectedReceiptSha256, 'Receipt bytes differ from the supplied trusted hash');
  const receipt = JSON.parse(raw);
  assert.equal(receipt.sourceCommit, expectedCommit, 'Receipt source commit differs from the supplied identity');
  assert.equal(receipt.exitCode, 0, 'Publication did not succeed');
  assert(Array.isArray(receipt.changedSourceInputs) && receipt.changedSourceInputs.length === 0,
    'Publication source inputs changed or their comparison is absent');
  assert(Array.isArray(receipt.trackedChangesAfter) && receipt.trackedChangesAfter.length === 0,
    'Publication has tracked source changes or their comparison is absent');
  assert(Array.isArray(receipt.publishFiles) && receipt.publishFiles.length > 0, 'File receipt required');
  const declared = receipt.publishFiles.map(file => {
    assert(safeRelative(file.path), `Unsafe publication path: ${file.path}`);
    assert(Number.isSafeInteger(file.bytes) && file.bytes >= 0, 'Exact nonnegative file size required');
    assert.match(file.sha256 ?? '', /^[0-9a-f]{64}$/, 'Exact file SHA-256 required');
    return { path: file.path, bytes: file.bytes, sha256: file.sha256 };
  }).sort((a, b) => compare(a.path, b.path));
  assert.equal(new Set(declared.map(file => file.path)).size, declared.length, 'Duplicate file identity');
  for (const bootstrap of ['index.html', 'main.js', '_framework/dotnet.js'])
    assert(declared.some(file => file.path === bootstrap), `Owned browser bootstrap is absent: ${bootstrap}`);
  assert.equal(receipt.fileCount, declared.length, 'Receipt file count differs');
  const totalBytes = declared.reduce((sum, file) => sum + file.bytes, 0);
  assert(Number.isSafeInteger(totalBytes), 'Exact total file size required');
  assert.equal(receipt.totalBytes, totalBytes, 'Receipt total byte count differs');
  const actual = inventory(path.resolve(bundlePath));
  assert.deepEqual(actual, declared, 'Published files are missing, extra, changed, or have different sizes');
  return { sourceCommit: expectedCommit, receiptSha256: expectedReceiptSha256,
    receiptPath: path.resolve(receiptPath), bundlePath: path.resolve(bundlePath),
    originalPublishRoot: receipt.publishRoot, scope: receipt.scope,
    fileCount: actual.length, totalBytes, transportVerified: true,
    runtimeVerified: false, deploymentVerified: false, packageAuthorityVerified: false };
}
module.exports = { verifyPublishReceipt };
if (require.main === module) {
  try {
    assert.equal(process.argv.length, 6,
      'Usage: node verify-publish-receipt.cjs <receipt.json> <wwwroot> <expected40hexCommit> <expectedReceiptSHA256>');
    console.log(JSON.stringify(verifyPublishReceipt({ receiptPath: process.argv[2], bundlePath: process.argv[3],
      expectedCommit: process.argv[4], expectedReceiptSha256: process.argv[5] }), null, 2));
  } catch (error) { console.error(error.stack); process.exitCode = 1; }
}
