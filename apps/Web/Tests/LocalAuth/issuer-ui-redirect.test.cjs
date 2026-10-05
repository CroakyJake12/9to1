'use strict';
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const path = require('node:path');
const test = require('node:test');
// Actual maintained compiled issuer UI and pinned client code, with synthetic
// DOM/HTTP responses only. No browser, issuer, network or authentication grant.
const input = path.resolve(__dirname, '../../../../cloud/cake-id-auth/public/auth-ui.txt');
const bytes = fs.readFileSync(input);
async function probe(kind, responseKind) {
  const moves = [], requests = [], timers = [];
  const handlers = new Map();
  const destination = 'https://client.example.test:5096/callback?code=synthetic-code&state=synthetic-state';
  const status = { textContent: '' }, submit = { disabled: false };
  const form = { dataset: {}, querySelector: () => submit, addEventListener: (event, fn) => handlers.set('form:' + event, fn) };
  const buttons = Object.fromEntries(['allow', 'deny'].map(id => [id, { disabled: false, addEventListener: (event, fn) => handlers.set(id + ':' + event, fn) }]));
  let href = 'http://127.0.0.1:8799/' + (kind === 'signin' ? 'sign-in' : 'consent');
  const location = { origin: 'http://127.0.0.1:8799', search: '', assign(value) { moves.push({ source: 'manualAssign', target: value === destination ? 'CALLBACK' : value === '/account' ? 'ACCOUNT' : 'OTHER' }); } };
  Object.defineProperty(location, 'href', { get() { return href; }, set(value) { moves.push({ source: 'defaultRedirectPlugin', target: value === destination ? 'CALLBACK' : 'OTHER' }); href = value; } });
  const document = { querySelector: () => status, getElementById(id) { if (id === 'sign-in-form' && kind === 'signin') return form; if (id === 'consent' && kind === 'consent') return { dataset: { clientId: 'synthetic-client', scopes: '[]' } }; if (kind === 'consent' && buttons[id]) return buttons[id]; return null; } };
  const fetch = async (url, options = {}) => {
    const pathname = new URL(String(url)).pathname;
    assert(['/api/auth/sign-in/email', '/api/auth/oauth2/consent', '/api/auth/oauth2/public-client'].includes(pathname));
    requests.push(pathname);
    if (pathname === '/api/auth/oauth2/public-client') return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } });
    const body = responseKind === 'redirect' ? { redirect: true, url: destination } : responseKind === 'unsafe' ? { redirect: true, url: 'javascript:synthetic-refused' } : responseKind === 'error' ? { code: 'SYNTHETIC_REFUSAL', message: 'Synthetic refusal' } : { redirect: false };
    return new Response(JSON.stringify(body), { status: responseKind === 'error' ? 400 : 200, headers: { 'content-type': 'application/json' } });
  };
  const sandbox = { window: { location }, location, document, fetch, Headers, Request, Response, URL, URLSearchParams, TextEncoder, TextDecoder, AbortController, AbortSignal, crypto: crypto.webcrypto, console: { warn() {}, error() {}, log() {} }, FormData: class { get(name) { return name === 'email' ? 'synthetic@example.test' : name === 'password' ? 'synthetic-offline-password' : ''; } }, setTimeout(fn, ms) { const timer = setTimeout(fn, ms); timer.unref(); timers.push(timer); return timer; }, clearTimeout, setInterval(fn, ms) { const timer = setInterval(fn, ms); timer.unref(); timers.push(timer); return timer; }, clearInterval };
  try {
    vm.runInNewContext(bytes.toString(), sandbox, { filename: 'actual-approved-auth-ui.txt', timeout: 1000 });
    if (kind === 'signin') await handlers.get('form:submit')({ preventDefault() {} }); else handlers.get('allow:click')();
    for (let i = 0; i < 8; i++) await new Promise(resolve => setImmediate(resolve));
    return { kind, responseKind, moves, actionRequests: requests.filter(p => p !== '/api/auth/oauth2/public-client').length, publicStatusSet: !!status.textContent, guardsReleased: kind === 'signin' ? form.dataset.busy === 'false' && !submit.disabled : !buttons.allow.disabled && !buttons.deny.disabled };
  } finally { for (const timer of timers) { clearTimeout(timer); clearInterval(timer); } }
}
for (const kind of ['signin', 'consent']) {
  for (const responseKind of ['redirect', 'nonredirect', 'error', 'unsafe']) {
    test(`${kind}: ${responseKind} preserves one standard-client redirect, fallback or refusal`, async () => {
      const result = await probe(kind, responseKind);
      assert.equal(result.actionRequests, 1, 'One controlled client action request');
      const expected = responseKind === 'redirect' ? [{ source: 'defaultRedirectPlugin', target: 'CALLBACK' }]
        : responseKind === 'nonredirect' ? [{ source: 'manualAssign', target: 'ACCOUNT' }] : [];
      assert.deepEqual(result.moves, expected, 'No second navigation and no unsafe protocol assignment');
      assert.equal(result.publicStatusSet, responseKind === 'error', 'Original error presentation retained');
      assert.equal(result.guardsReleased, true, 'Original completion/button guards retained');
    });
  }
}
