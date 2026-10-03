import http from 'node:http';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { AccountApiClient } from '/workspace/team-b-worktree/apps/Web/Auth/account-api-client.js';
const abort = new AbortController();
const server = http.createServer((_request, response) => {
  response.writeHead(200, { 'Content-Type': 'application/json' });
  response.write('{"accountId":');
  setTimeout(() => response.end('"12345678-1234-4234-8234-123456789abc","displayName":"Isolated fixture"}'), 100);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
try {
  const client = new AccountApiClient({ apiResource: `http://127.0.0.1:${server.address().port}`,
    allowLoopbackForIsolatedTests: true, getAccessToken: async () => 'isolated-fixture-token',
    onPrivateContextInvalidated: async () => {}, fetch: async (...args) => {
      const response = await fetch(...args); setTimeout(() => abort.abort(), 10); return response;
    }});
  const result = await client.getCurrent({signal: abort.signal});
  console.log(JSON.stringify({scenario:'Real native Fetch200 body-phase external abort; isolated transport fixture',
    expected:'Cancelled',observed:result,source_sha256:createHash('sha256').update(readFileSync('/workspace/team-b-worktree/apps/Web/Auth/account-api-client.js')).digest('hex')}));
} finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
