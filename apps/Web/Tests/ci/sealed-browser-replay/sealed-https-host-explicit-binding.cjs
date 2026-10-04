const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const https = require('node:https');
const assert = require('node:assert/strict');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function verifySeal(manifestPath, expected) {
  assert(expected && /^[0-9a-f]{64}$/.test(expected.manifestSha256) && /^[0-9a-f]{40}$/.test(expected.sourceCommit) && Number.isSafeInteger(expected.fileCount) && expected.fileCount>0);
  const manifestBytes=fs.readFileSync(manifestPath);
  assert.equal(sha(manifestBytes),expected.manifestSha256);
  const manifest = JSON.parse(manifestBytes);
  assert.equal(manifest.sourceCommit, expected.sourceCommit);
  assert.equal(manifest.exitCode, 0);
  const root = fs.realpathSync(manifest.publishRoot);
  const files = new Set();
  for (const entry of manifest.publishFiles) {
    assert.equal(typeof entry.path,'string');
    assert(entry.path.length>0 && !entry.path.startsWith('/') && !entry.path.includes('\\') && !entry.path.includes('\0') && entry.path.split('/').every(part=>part!==''&&part!=='.'&&part!=='..'));
    assert(!files.has(entry.path),'Duplicate published path.');
    const bytes = fs.readFileSync(path.join(root, entry.path));
    assert.equal(bytes.length, entry.bytes); assert.equal(sha(bytes), entry.sha256); files.add(entry.path);
  }
  assert.equal(files.size, manifest.fileCount);
  assert.equal(files.size,expected.fileCount);
  const actual=[];
  function walk(folder, prefix='') {
    for(const entry of fs.readdirSync(folder,{withFileTypes:true})) {
      assert(!entry.isSymbolicLink(),'Published symlink is not an immutable asset.');
      const relative=prefix+entry.name;
      if(entry.isDirectory())walk(path.join(folder,entry.name),relative+'/');
      else {assert(entry.isFile(),'Unexpected published special file.');actual.push(relative);}
    }
  }
  walk(root);assert.deepEqual(actual.sort(),[...files].sort(),'Exact full published inventory required.');
  return { root, files, sourceCommit: manifest.sourceCommit, fileCount: files.size, manifestSha256: sha(fs.readFileSync(manifestPath)) };
}
async function startSealedHost(seal) {
  const fixture = fs.realpathSync(process.env.B5_CLIENT_TLS_ROOT);
  const certPath = process.env.B5_CLIENT_TLS_CERT;
  const keyPath = process.env.B5_CLIENT_TLS_KEY;
  assert((fs.statSync(fixture).mode & 0o777) === 0o700, 'Owned TLS fixture directory mode required');
  for (const file of [certPath, keyPath]) {
    assert(typeof file === 'string' && !fs.lstatSync(file).isSymbolicLink() && fs.statSync(file).isFile());
    assert(path.dirname(fs.realpathSync(file)) === fixture, 'Exact owned TLS fixture path required');
  }
  assert((fs.statSync(keyPath).mode & 0o777) === 0o600, 'Owned fixture key mode required');
  const cert = fs.readFileSync(certPath);
  const key = fs.readFileSync(keyPath);
  const types = { '.html':'text/html', '.js':'text/javascript', '.json':'application/json', '.wasm':'application/wasm', '.css':'text/css', '.woff2':'font/woff2' };
  const server = https.createServer({ cert, key }, (req, res) => {
    // No logs of callback query strings, headers or request bodies.
    if (!['GET','HEAD'].includes(req.method) || req.headers.host !== 'client.example.test:5096') { res.writeHead(403); return res.end(); }
    let url; try { url = new URL(req.url, 'https://client.example.test:5096'); } catch { res.writeHead(400); return res.end(); }
    let relative; try { relative = decodeURIComponent(url.pathname).slice(1); } catch { res.writeHead(400); return res.end(); }
    if (relative === '' || relative === 'callback') relative = 'index.html';
    if (!seal.files.has(relative)) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'Content-Type':types[path.extname(relative)] || 'application/octet-stream', 'Cache-Control':'no-store' });
    if (req.method === 'HEAD') return res.end();
    fs.createReadStream(path.join(seal.root, relative)).pipe(res);
  });
  await new Promise((yes,no) => { server.once('error',no); server.listen(5096,'127.0.0.1',yes); });
  return server;
}
module.exports = { verifySeal, startSealedHost, sha };
