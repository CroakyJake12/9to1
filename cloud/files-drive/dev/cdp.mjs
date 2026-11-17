// Development-only minimal Chrome DevTools Protocol client. It uses Node's
// built-in WebSocket and fetch and nothing outside this repository checkout.
import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

export async function launchChrome({ chrome, profileDir, args = [] }) {
  mkdirSync(profileDir, { recursive: true });
  const child = spawn(chrome, [
    '--headless=new', '--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage',
    '--no-first-run', '--no-default-browser-check', '--test-type', '--ignore-certificate-errors',
    `--user-data-dir=${profileDir}`, '--remote-debugging-port=0', ...args,
  ], { stdio: ['ignore', 'pipe', 'pipe'] });
  const port = await new Promise((resolve, reject) => {
    const started = Date.now();
    const timer = setInterval(() => {
      try {
        const value = JSON.parse(readFileSync(join(profileDir, 'DevToolsActivePort'), 'utf8').split('\n')[0]);
        if (Number.isSafeInteger(value)) { clearInterval(timer); resolve(value); }
      } catch { /* the file appears once Chrome has bound the port */ }
      if (Date.now() - started > 30000) { clearInterval(timer); reject(new Error('Chrome did not expose a debug port')); }
    }, 100);
    child.on('exit', code => { clearInterval(timer); reject(new Error('Chrome exited early with code ' + code)); });
  });
  const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
  const socket = new WebSocket(version.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = () => reject(new Error('CDP connect failed')); });
  const client = createClient(socket);
  return { port, client, async close() { try { await client.send('Browser.close'); } catch { /* dying */ } child.kill(); } };
}

function createClient(socket) {
  let nextId = 1;
  const pending = new Map();
  const listeners = new Set();
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id !== undefined) {
      const entry = pending.get(message.id);
      pending.delete(message.id);
      if (!entry) return;
      if (message.error) entry.reject(new Error(message.error.message)); else entry.resolve(message.result);
      return;
    }
    for (const listener of listeners) listener(message);
  });
  function send(method, params = {}, sessionId = undefined) {
    const id = nextId++;
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
  }
  return {
    send,
    on(predicate, handler) {
      const listener = message => { if (predicate(message)) handler(message.params ?? {}); };
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    close() { socket.close(); },
  };
}

export async function attachPage(client, url) {
  const { targetId } = await client.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await client.send('Target.attachToTarget', { targetId, flatten: true });
  const session = {
    send: (method, params = {}) => client.send(method, params, sessionId),
    on: (method, handler) => client.on(message => message.sessionId === sessionId && message.method === method,
      params => handler(params)),
  };
  await session.send('Page.enable');
  await session.send('Runtime.enable');
  await session.send('DOM.enable');
  await session.send('Network.enable');
  await new Promise(resolve => {
    const stop = session.on('Page.loadEventFired', () => { stop(); resolve(); });
    session.send('Page.navigate', { url });
  });
  return session;
}

export async function evaluate(session, expression, userGesture = true) {
  // userGesture grants transient activation so the app's own anchor-based
  // downloads and pickers behave as they do under a real click.
  const result = await session.send('Runtime.evaluate', { expression, awaitPromise: true,
    returnByValue: true, userGesture });
  if (result.exceptionDetails) throw new Error('Page evaluation failed: ' + JSON.stringify(result.exceptionDetails).slice(0, 400));
  return result.result.value;
}

export async function pollFor(session, expression, timeoutMs = 30000, intervalMs = 250) {
  const deadline = Date.now() + timeoutMs;
  let last;
  for (;;) {
    last = await evaluate(session, expression);
    if (last === true || (last && typeof last === 'object' && last.done === true)) return last;
    if (Date.now() > deadline) return last && typeof last === 'object' ? last : { done: false, last };
    await new Promise(resolve => setTimeout(resolve, intervalMs));
  }
}
