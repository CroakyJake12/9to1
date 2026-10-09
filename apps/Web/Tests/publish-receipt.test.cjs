// Filesystem support controls only; never launch or modify a published host.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { verifyPublishReceipt } = require('./verify-publish-receipt.cjs');
assert.equal(process.argv.length, 7,
  'Usage: node publish-receipt.test.cjs <actualReceipt> <actualBundle> <expectedCommit> <expectedReceiptSHA> <newOutput>');
const [receiptPath, bundlePath, expectedCommit, expectedReceiptSha256, output] = process.argv.slice(2);
assert(!fs.existsSync(output), 'Require a new evidence directory');
fs.mkdirSync(output, { recursive: true });
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const report = { scope: 'Actual sealed file receipt verification plus isolated small filesystem controls; no SDK/browser/app-manifest authority.',
  runnerSha256: hash(fs.readFileSync(__filename)), verifierSha256: hash(fs.readFileSync(path.join(__dirname, 'verify-publish-receipt.cjs'))),
  outcomes: [], sourceBundleWritten: false, browserStarted: false, sdkStarted: false };
const save = () => fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
function check(name, action) {
  let observed;
  try { observed = action(); report.outcomes.push({ name, outcome: 'PASS', observed }); }
  catch (error) { report.outcomes.push({ name, outcome: 'FAIL', error: error.stack }); save(); throw error; }
  save();
  return observed;
}
function rejected(name, options, pattern) {
  check(name, () => {
    let failure;
    try { verifyPublishReceipt(options); } catch (error) { failure = error; }
    assert(failure, 'The controlled defect must be rejected');
    assert.match(failure.message, pattern, 'Reject for the intended defect');
    return { rejected: true, message: failure.message };
  });
}
const options = { receiptPath, bundlePath, expectedCommit, expectedReceiptSha256 };
let temporary;
try {
  report.actualBefore = check('actual independently pinned sealed receipt and all files', () => verifyPublishReceipt(options));
  const actualReceipt = JSON.parse(fs.readFileSync(receiptPath, 'utf8'));
  temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'b1-publish-receipt-controls-'));
  const extracted = ['index.html', 'main.js', '_framework/dotnet.js'];
  function fixture(name, mutateReceipt = () => {}) {
    const root = path.join(temporary, name); fs.mkdirSync(root);
    // Copies contain only three actual small bootstrap files. No links to sealed
    // assets, full-output simulation, runtime execution, or claimed publication.
    const files = extracted.map(file => {
      const source = path.join(bundlePath, file), target = path.join(root, file);
      fs.mkdirSync(path.dirname(target), { recursive: true }); fs.copyFileSync(source, target);
      const pinned = actualReceipt.publishFiles.find(item => item.path === file);
      assert.equal(hash(fs.readFileSync(target)), pinned.sha256);
      return { ...pinned };
    });
    const receipt = { sourceCommit: expectedCommit, scope: 'CONTROLLED bootstrap extraction; not a real publication receipt',
      exitCode: 0, changedSourceInputs: [], trackedChangesAfter: [], publishRoot: root,
      publishFiles: files, fileCount: files.length, totalBytes: files.reduce((sum, file) => sum + file.bytes, 0) };
    mutateReceipt(receipt);
    const file = path.join(temporary, name + '.receipt.json');
    const bytes = Buffer.from(JSON.stringify(receipt, null, 2)); fs.writeFileSync(file, bytes);
    return { receiptPath: file, bundlePath: root, expectedCommit, expectedReceiptSha256: hash(bytes) };
  }
  check('isolated exact bootstrap extraction transport control', () => verifyPublishReceipt(fixture('baseline')));
  const changed = fixture('changed'); fs.appendFileSync(path.join(changed.bundlePath, 'main.js'), '\n// controlled changed byte\n');
  rejected('changed real file bytes fail', changed, /Published files are missing, extra, changed/);
  const extra = fixture('extra'); fs.writeFileSync(path.join(extra.bundlePath, 'old-fingerprint.wasm'), 'CONTROLLED extra file; never served');
  rejected('extra stale fingerprint fails', extra, /Published files are missing, extra, changed/);
  const missing = fixture('missing'); fs.unlinkSync(path.join(missing.bundlePath, '_framework/dotnet.js'));
  rejected('missing actual bootstrap file fails', missing, /Published files are missing, extra, changed/);
  rejected('wrong independently supplied commit fails', { ...fixture('wrong-commit'), expectedCommit: '0'.repeat(40) }, /source commit differs/);
  rejected('altered receipt file hash fails', fixture('wrong-file-hash', receipt => { receipt.publishFiles[1].sha256 = '0'.repeat(64); }), /Published files are missing, extra, changed/);
  rejected('wrong independently supplied receipt hash fails', { ...fixture('wrong-receipt-hash'), expectedReceiptSha256: '0'.repeat(64) }, /Receipt bytes differ/);
  const linked = fixture('link'); fs.symlinkSync(path.join(bundlePath, 'main.js'), path.join(linked.bundlePath, 'linked-main.js'));
  rejected('read-through symlink is rejected', linked, /Links are not publication files/);
  rejected('publication source changes reject', fixture('source-change', receipt => { receipt.changedSourceInputs = ['owned.cs']; }), /source inputs changed/);
  rejected('unsafe receipt path rejects', fixture('path-escape', receipt => { receipt.publishFiles[0].path = '../index.html'; }), /Unsafe publication path/);
  rejected('unsuccessful publication receipt rejects', fixture('publish-failed', receipt => { receipt.exitCode = 1; }), /Publication did not succeed/);
  report.actualAfter = check('sealed receipt and all files remain unchanged after controls', () => verifyPublishReceipt(options));
  assert.deepEqual(report.actualAfter, report.actualBefore);
} catch (error) { report.error = error.stack; process.exitCode = 1; }
finally {
  if (temporary) { fs.rmSync(temporary, { recursive: true, force: true }); report.temporaryControlsRemoved = !fs.existsSync(temporary); }
  report.completedAt = new Date().toISOString(); save();
  console.log(JSON.stringify({ outcomes: report.outcomes, actual: report.actualBefore, temporaryControlsRemoved: report.temporaryControlsRemoved }, null, 2));
}
