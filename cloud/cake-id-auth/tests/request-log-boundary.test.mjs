import assert from 'node:assert/strict';
import { test } from 'node:test';
import { build } from 'esbuild';

test('unexpected recovery and unknown-path errors cannot attach credential-bearing URLs to console events', async () => {
  const compiled = await build({ entryPoints: ['src/index.ts'], bundle: true, write: false,
    platform: 'node', format: 'esm', loader: { '.txt': 'text' }, plugins: [{ name: 'error-boundary-auth', setup(b) {
      b.onResolve({ filter: /^\.\/auth$/ }, () => ({ path: 'auth', namespace: 'error-boundary-test' }));
      b.onLoad({ filter: /.*/, namespace: 'error-boundary-test' }, () => ({ loader: 'js', contents:
        'export const googleCredentials=()=>null; export const registrationHeld=()=>false; export const createAuth=()=>({handler:async()=>{throw new Error("synthetic-private-exception");}});' }));
    } }] });
  const worker = (await import('data:text/javascript;base64,' + Buffer.from(compiled.outputFiles[0].text).toString('base64'))).default;
  const captured = [], original = console.warn;
  console.warn = value => captured.push(JSON.parse(value));
  try {
    for (const path of ['/api/auth/reset-password/synthetic-private-token', '/unexpected/synthetic-private-value']) {
      const result = await worker.fetch(new Request('https://identity.example.test' + path), {}, {});
      assert.equal(result.status, 503);
      assert.deepEqual(await result.json(), { error: 'service_unavailable' });
      assert.equal(captured.length, 0);
    }
    const safe = await worker.fetch(new Request('https://identity.example.test/api/auth/oauth2/token'), {}, {});
    assert.equal(safe.status, 503);
    assert.deepEqual(captured, [{ event: 'account_security_events', counts: { service_error: 1 } }]);
    assert.ok(!JSON.stringify(captured).includes('private'));
  } finally { console.warn = original; }
});
