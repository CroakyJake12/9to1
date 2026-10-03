import http from 'node:http';
import { AccountApiClient } from '/workspace/team-b-worktree/apps/Web/Auth/account-api-client.js';
const clears = [];
const server = http.createServer((request, response) => {
  response.writeHead(401, { 'Content-Type': 'application/json' });
  response.write('{"error":');
  setTimeout(() => response.end('"authentication_required"}'), 50);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
try {
  const client = new AccountApiClient({ apiResource: `http://127.0.0.1:${server.address().port}`,
    allowLoopbackForIsolatedTests: true, getAccessToken: async () => 'isolated-fixture-token',
    onPrivateContextInvalidated: async reason => clears.push(reason) });
  const result = await client.getCurrent();
  console.log(JSON.stringify({ scenario: 'Real native Fetch delayed401 body; isolated fixture, not production authentication',
    expected: 'authentication_required with private context cleared', observed: result, clears }));
} finally {
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
}
