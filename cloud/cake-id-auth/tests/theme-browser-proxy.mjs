import http from 'node:http';
import https from 'node:https';

export const fixtureBrowserOrigin = 'https://client.example.test:5096';
export const fixtureCallback = `${fixtureBrowserOrigin}/callback`;
const publicConfigPath = '/__fixture/account-config.js';

// Public routing configuration only: never serialize accounts, test keys or cookies.
export function publicAccountConfiguration(manifest) {
  const api = new URL(manifest.apiResource);
  if (manifest.fixtureOnly !== true || api.protocol !== 'http:' || api.hostname !== '127.0.0.1' ||
      !api.port || api.username || api.password || api.pathname !== '/' || api.search || api.hash ||
      manifest.issuer !== `${api.origin}/api/auth` || manifest.browserOrigin !== fixtureBrowserOrigin ||
      manifest.redirectUri !== fixtureCallback || typeof manifest.clientId !== 'string' || !manifest.clientId ||
      typeof manifest.scopes !== 'string' || !manifest.scopes.split(' ').includes('openid'))
    throw new Error('Only the maintained isolated browser fixture configuration is accepted.');
  return Object.freeze({ issuer: manifest.issuer, apiResource: api.origin, clientId: manifest.clientId,
    redirectUri: fixtureCallback, scopes: manifest.scopes, allowLoopbackForIsolatedTests: true });
}

export function validateAppUpstream(value = 'http://127.0.0.1:8765') {
  const upstream = new URL(value);
  if (upstream.protocol !== 'http:' || upstream.hostname !== '127.0.0.1' ||
      !upstream.port || Number(upstream.port) < 1024 || upstream.port === '5096' ||
      upstream.pathname !== '/' || upstream.username || upstream.password || upstream.search || upstream.hash)
    throw new Error('App upstream must be an explicit unprivileged HTTP 127.0.0.1 origin.');
  return upstream;
}

export function createThemeBrowserProxy({ key, cert, manifest, upstream: value }) {
  const upstream = validateAppUpstream(value);
  const account = publicAccountConfiguration(manifest);
  const browserHost = new URL(fixtureBrowserOrigin).host;
  const configScript = `window.nineToOneBrowserConfiguration = Object.freeze({ account: Object.freeze(${JSON.stringify(account)}) });\n`;
  const requests = new Set();
  const server = https.createServer({ key, cert }, (req, res) => {
    const end = (status, body) => { res.writeHead(status, { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' }); res.end(body); };
    if (req.headers.host !== browserHost || !req.url?.startsWith('/') || req.url.startsWith('//')) return end(421, 'Isolated fixture host required.');
    if (!['GET', 'HEAD'].includes(req.method)) return end(405, 'Read-only application assets.');
    const target = new URL(req.url, fixtureBrowserOrigin);
    if (target.pathname === publicConfigPath) {
      res.writeHead(200, { 'content-type': 'text/javascript; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' });
      return res.end(req.method === 'HEAD' ? undefined : configScript);
    }
    if (target.pathname.startsWith('/__fixture/')) return end(404, 'Not found.');
    // Callback loads the same actual application host, whose maintained callback
    // owner runs before .NET. No callback codes, tokens or credentials are logged.
    const isDocument = ['/', '/index.html', '/callback'].includes(target.pathname);
    const requestPath = isDocument ? '/index.html' : `${target.pathname}${target.search}`;
    const headers = { 'accept-encoding': 'identity' };
    for (const name of ['accept', 'range', 'if-none-match', 'if-modified-since'])
      if (req.headers[name]) headers[name] = req.headers[name];
    // Do not forward cookies, Authorization or caller-supplied origin/host headers.
    const outgoing = http.request(new URL(requestPath, upstream), { method: req.method, headers }, incoming => {
      const responseHeaders = { ...incoming.headers, 'cache-control': 'no-store' };
      delete responseHeaders.connection;
      delete responseHeaders['transfer-encoding'];
      delete responseHeaders['set-cookie'];
      if (!isDocument || req.method === 'HEAD' || incoming.statusCode !== 200) {
        res.writeHead(incoming.statusCode ?? 502, responseHeaders); incoming.pipe(res); return;
      }
      if (!String(incoming.headers['content-type']).includes('text/html') || incoming.headers['content-encoding']) {
        incoming.resume(); return end(502, 'Expected uncompressed actual application HTML.');
      }
      let size = 0;
      const chunks = [];
      incoming.on('data', chunk => {
        size += chunk.length;
        if (size > 2 * 1024 * 1024) { outgoing.destroy(); if (!res.headersSent) end(502, 'Application HTML exceeded fixture bound.'); }
        else chunks.push(chunk);
      });
      incoming.on('end', () => {
        if (res.writableEnded) return;
        const original = Buffer.concat(chunks).toString('utf8');
        if (!original.includes('</head>') || !original.includes('main.js')) return end(502, 'Actual application entrypoint is required.');
        const html = original.replace('</head>', `<script src="${publicConfigPath}"></script>\n</head>`);
        delete responseHeaders['content-length']; delete responseHeaders.etag;
        res.writeHead(200, responseHeaders); res.end(html);
      });
      incoming.on('error', () => { if (!res.headersSent) end(502, 'Application asset stream unavailable.'); else res.destroy(); });
    });
    requests.add(outgoing);
    outgoing.once('close', () => requests.delete(outgoing));
    outgoing.setTimeout(30000, () => outgoing.destroy());
    outgoing.on('error', () => { if (!res.headersSent) end(502, 'Local application unavailable.'); else res.destroy(); });
    res.once('close', () => { if (!res.writableEnded) outgoing.destroy(); });
    outgoing.end();
  });
  return { server, async close() {
    for (const request of requests) request.destroy();
    await new Promise((resolve, reject) => {
      server.close(error => error && error.code !== 'ERR_SERVER_NOT_RUNNING' ? reject(error) : resolve());
      server.closeAllConnections();
    });
  } };
}
