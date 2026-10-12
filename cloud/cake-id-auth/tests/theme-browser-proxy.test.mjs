import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { createThemeBrowserProxy, publicAccountConfiguration, validateAppUpstream } from './theme-browser-proxy.mjs';

const manifest = { fixtureOnly: true, apiResource: 'http://127.0.0.1:8798', issuer: 'http://127.0.0.1:8798/api/auth',
  browserOrigin: 'https://client.example.test:5096', redirectUri: 'https://client.example.test:5096/callback',
  clientId: 'public-fixture-client', scopes: 'openid profile cake:profile:read cake:profile:write',
  accounts: [{ password: 'MUST-NOT-ENTER-PUBLIC-CONFIG' }], localTestKey: 'PRIVATE-KEY-MUST-NOT-ENTER-CONFIG' };

test('public configuration copies only routing fields and refuses nonfixture authority', () => {
  const config = publicAccountConfiguration(manifest);
  assert.equal(config.allowLoopbackForIsolatedTests, true);
  assert.equal(JSON.stringify(config).includes('MUST-NOT'), false);
  assert.deepEqual(Object.keys(config).sort(), ['allowLoopbackForIsolatedTests', 'apiResource', 'clientId', 'issuer', 'redirectUri', 'scopes'].sort());
  for (const change of [{ fixtureOnly: false }, { apiResource: 'https://real.example' },
    { redirectUri: 'http://127.0.0.1:8765/callback' }, { issuer: 'http://127.0.0.1:8888/api/auth' }])
    assert.throws(() => publicAccountConfiguration({ ...manifest, ...change }));
});

test('proxy upstream cannot target remote services, userinfo or another path', () => {
  assert.equal(validateAppUpstream().origin, 'http://127.0.0.1:8765');
  for (const value of ['https://example.com', 'http://127.0.0.1:5096', 'http://user@127.0.0.1:8765',
    'http://127.0.0.1:8765/path', 'http://127.0.0.1:8765/?query', 'http://localhost:8765'])
    assert.throws(() => validateAppUpstream(value));
});

test('real loopback proxy preserves app bytes, callback URL secrecy and host boundary', async () => {
  const folder = mkdtempSync(path.join(tmpdir(), '9to1-theme-proxy-test-'));
  const key = path.join(folder, 'key.pem'), cert = path.join(folder, 'cert.pem');
  const generated = spawnSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
    '-subj', '/CN=client.example.test', '-keyout', key, '-out', cert], { stdio: 'ignore', timeout: 15000 });
  assert.equal(generated.status, 0);
  const observed = [];
  const wasm = Buffer.from([0, 97, 115, 109, 1, 0, 0, 0]);
  const app = http.createServer((req, res) => {
    observed.push({ url: req.url, cookie: req.headers.cookie, authorization: req.headers.authorization });
    if (req.url === '/module.wasm') { res.writeHead(200, { 'content-type': 'application/wasm' }); res.end(wasm); }
    else { res.writeHead(200, { 'content-type': 'text/html' }); res.end('<html><head></head><body><script type="module" src="./main.js"></script></body></html>'); }
  });
  let proxy;
  try {
    await new Promise(resolve => app.listen(0, '127.0.0.1', resolve));
    proxy = createThemeBrowserProxy({ key: readFileSync(key), cert: readFileSync(cert), manifest,
      upstream: `http://127.0.0.1:${app.address().port}` });
    await new Promise(resolve => proxy.server.listen(0, '127.0.0.1', resolve));
    const request = (url, headers = {}, method = 'GET') => new Promise((resolve, reject) => {
      const outgoing = https.request({ hostname: '127.0.0.1', port: proxy.server.address().port,
        path: url, method, rejectUnauthorized: false, headers: { host: 'client.example.test:5096', ...headers } }, response => {
        const chunks = []; response.on('data', chunk => chunks.push(chunk));
        response.on('end', () => resolve({ status: response.statusCode, body: Buffer.concat(chunks) }));
      });
      outgoing.on('error', reject); outgoing.end();
    });
    const doc = await request('/callback?code=PRIVATE-CALLBACK-CODE&state=PRIVATE-STATE', { cookie: 'PRIVATE-COOKIE', authorization: 'Bearer PRIVATE-TOKEN' });
    assert.equal(doc.status, 200);
    assert.match(doc.body.toString(), /account-config\.js/);
    assert.match(doc.body.toString(), /main\.js/);
    assert.deepEqual(observed[0], { url: '/index.html', cookie: undefined, authorization: undefined });
    const config = await request('/__fixture/account-config.js');
    assert.equal(config.status, 200);
    assert.match(config.body.toString(), /public-fixture-client/);
    assert.doesNotMatch(config.body.toString(), /MUST-NOT|PRIVATE/);
    assert.deepEqual((await request('/module.wasm')).body, wasm);
    assert.equal((await request('/', { host: 'attacker.example' })).status, 421);
    assert.equal((await request('/', {}, 'POST')).status, 405);
    assert.equal((await request('/__fixture/private-manifest.json')).status, 404);
  } finally {
    await proxy?.close();
    await new Promise(resolve => { app.close(resolve); app.closeAllConnections(); });
    rmSync(folder, { recursive: true, force: true });
  }
});
