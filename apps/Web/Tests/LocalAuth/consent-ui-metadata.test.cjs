'use strict';
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const crypto = require('node:crypto');
const test = require('node:test');
const { spawnSync } = require('node:child_process');
// Actual page producer and compiled pinned client. Offline synthetic DOM/HTTP
// only; no browser/issuer/config/database, authority or signing substitute.
const root = path.resolve(__dirname, '../../../../cloud/cake-id-auth');
const ts = require(path.join(root, 'node_modules/typescript'));
const pages = fs.readFileSync(path.join(root, 'src/pages.ts'));
const bundle = fs.readFileSync(path.join(root, 'public/auth-ui.txt'));
const pageModule = { exports: {} };
vm.runInNewContext(ts.transpileModule(pages.toString(), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText,
  { module: pageModule, exports: pageModule.exports, Response });
async function probe(canonicalId, malformed = false) {
  const html = await pageModule.exports.consentPage(canonicalId, ['openid']).text();
  const parsed = spawnSync('python3', ['-B', '-c', `import sys,json
from html.parser import HTMLParser
class Page(HTMLParser):
 def handle_starttag(self,tag,attrs):
  a=dict(attrs)
  if a.get('id')=='consent': print(json.dumps({'clientId':a.get('data-client-id'),'scopes':a.get('data-scopes')}))
Page().feed(sys.stdin.read())`], { input: html, encoding: 'utf8' });
  assert.equal(parsed.status, 0); const dataset = JSON.parse(parsed.stdout);
  assert.equal(JSON.parse(dataset.clientId), canonicalId);
  if (malformed) dataset.clientId = '{malformed-json';
  const requests = [], timers = [], label = { textContent: '' }, status = { textContent: '' };
  const location = { origin: 'http://127.0.0.1:8799', search: '', href: 'http://127.0.0.1:8799/consent', assign() { throw new Error('Offline metadata test refuses navigation'); } };
  const fetch = async input => {
    const url = new URL(String(input)); assert.equal(url.origin, location.origin); assert.equal(url.pathname, '/api/auth/oauth2/public-client');
    const exact = url.searchParams.get('client_id') === canonicalId; requests.push(exact);
    return new Response(JSON.stringify(exact ? { client_name: 'Offline public application' } : { error: 'not_found' }),
      { status: exact ? 200 : 404, headers: { 'content-type': 'application/json' } });
  };
  const button = { disabled: false, addEventListener() {} };
  const sandbox = { window: { location }, location, document: { querySelector: () => status, createElement: () => ({ textContent: '', className: '' }), getElementById(id) {
    return id === 'consent' ? { dataset } : id === 'client-name' ? label : ['allow', 'deny'].includes(id) ? button : null;
  } }, fetch, Headers, Request, Response, URL, URLSearchParams, TextEncoder, TextDecoder, AbortController, AbortSignal, crypto: crypto.webcrypto,
    console: { log() {}, warn() {}, error() {} }, setTimeout(fn, ms) { const timer = setTimeout(fn, ms); timer.unref(); timers.push(timer); return timer; }, clearTimeout,
    setInterval(fn, ms) { const timer = setInterval(fn, ms); timer.unref(); timers.push(timer); return timer; }, clearInterval };
  try {
    if (malformed) assert.throws(() => vm.runInNewContext(bundle.toString(), sandbox, { timeout: 1000 }), error => error.name === 'SyntaxError');
    else vm.runInNewContext(bundle.toString(), sandbox, { timeout: 1000 });
    for (let turn = 0; turn < 8; turn++) await new Promise(resolve => setImmediate(resolve));
    if (malformed) { assert.equal(requests.length, 0); assert.equal(label.textContent, ''); }
    else { assert.deepEqual(requests, [true]); assert.equal(label.textContent, 'Offline public application'); }
  } finally { for (const timer of timers) { clearTimeout(timer); clearInterval(timer); } }
}
const cases = ['offline-public-client', 'quote"client', "apostrophe'&<client", '"already-quoted-id"'];
for (const [index, id] of cases.entries()) test('actual JSON/HTML metadata roundtrip preserves exact client ID case ' + (index + 1), () => probe(id));
test('malformed serialized consent client refuses before any metadata request', () => probe('offline-public-client', true));
