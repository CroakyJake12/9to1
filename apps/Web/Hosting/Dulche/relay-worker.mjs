import { DurableObject } from 'cloudflare:workers';
import { createRemoteJWKSet, jwtVerify } from 'jose';
const json = (status, value) => Response.json(value, { status, headers: { 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'X-Content-Type-Options': 'nosniff' } });
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
const deferred = () => { let resolve, reject; const promise = new Promise((r,j) => { resolve = r; reject = j; }); return { promise, resolve, reject }; };
async function bytes(request, limit) {
  const reader = request.body?.getReader(); if (!reader) return new Uint8Array();
  const chunks = []; let size = 0;
  try { for (;;) { const part = await reader.read(); if (part.done) break; size += part.value.length; if (size > limit) { await reader.cancel(); throw new Error('request_too_large'); } chunks.push(part.value); } } finally { reader.releaseLock(); }
  const result = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; } return result;
}
async function current(env, authorization) {
  if (typeof authorization !== 'string' || !/^Bearer [^\s]{1,16384}$/.test(authorization)) return null;
  const response = await env.ACCOUNT_AUTH.fetch(new Request(env.ACCOUNT_ORIGIN + '/api/account/current', { headers: { Authorization: authorization, Origin: env.PREVIEW_ORIGIN }, redirect: 'manual' }));
  if (response.status !== 200) { await response.body?.cancel(); return null; }
  let value; try { value = JSON.parse(new TextDecoder().decode(await bytes(response, 16384))); } catch { return null; }
  return uuid(value?.accountId) ? value.accountId : null;
}
let accessKeys;
export default { async fetch(request, env, ctx) {
  try {
    const url = new URL(request.url);
    if (url.origin !== env.RUNTIME_ORIGIN || url.search) return json(404, { error: 'route_unavailable' });
    const assertion = request.headers.get('Cf-Access-Jwt-Assertion');
    if (!assertion || assertion.length > 16384) return json(401, { error: 'service_authentication_required' });
    accessKeys ??= createRemoteJWKSet(new URL('/cdn-cgi/access/certs', env.ACCESS_ISSUER));
    try { await jwtVerify(assertion, accessKeys, { issuer: env.ACCESS_ISSUER, audience: env.ACCESS_AUDIENCE, requiredClaims: ['exp', 'iat'] }); }
    catch { return json(401, { error: 'service_authentication_required' }); }
    return await dispatchAuthorized(request, env, ctx);
  } catch { return json(502, { error: 'runtime_unavailable' }); }
} };
export async function dispatchAuthorized(request, env, ctx) {
  const stub = env.RUNTIME_RELAY.getByName('ollama-qwen3-runtime-20261013');
  if (new URL(request.url).pathname.startsWith('/connector/')) {
    if (request.signal.aborted) return json(499, { error: 'connector_cancelled' });
    const waitId = crypto.randomUUID(), headers = new Headers(request.headers); headers.set('X-Original-Connector-Wait', waitId);
    const abort = () => { const joined = stub.cancelConnectorWait(waitId, request.headers.get('X-Connector-Lease')); joined.catch(() => {}); ctx.waitUntil(joined); };
    request.signal.addEventListener('abort', abort, { once: true });
    try { return await stub.dispatch(new Request(request, { headers })); }
    finally { request.signal.removeEventListener('abort', abort); }
  }
  if (request.signal.aborted) return json(499, { error: 'client_cancelled' });
  const id = crypto.randomUUID(), headers = new Headers(request.headers); headers.set('X-Original-Relay-Job', id);
  let retirement, reader, closeTask, closed = false;
  const retire = () => {
    if (!retirement) { retirement = stub.retireOriginal(id); retirement.catch(() => {}); ctx.waitUntil(retirement); }
    return retirement;
  };
  const abort = () => { retire().catch(() => {}); };
  request.signal.addEventListener('abort', abort, { once: true });
  try {
    const response = await stub.dispatch(new Request(request, { headers }));
    if (!response.ok || !response.body) { request.signal.removeEventListener('abort', abort); return response; }
    reader = response.body.getReader();
    const close = reason => {
      if (closeTask) return closeTask;
      if (closed) return Promise.resolve(); closed = true;
      closeTask = (async () => {
      const originalCancellation = reader.cancel(reason);
      const results = await Promise.allSettled([originalCancellation, retire()]);
      if (results[1].status === 'fulfilled') { try { await stub.releaseOriginal(id); } catch (error) { results.push({status:'rejected',reason:error}); } }
      reader.releaseLock(); request.signal.removeEventListener('abort', abort);
      const errors = results.filter(result => result.status === 'rejected').map(result => result.reason);
      if (errors.length === 1) throw errors[0];
      if (errors.length > 1) throw new AggregateError(errors, 'Original relay cleanup failed');
      })(); closeTask.catch(() => {}); return closeTask;
    };
    return new Response(new ReadableStream({
      async pull(controller) {
        try { const next = await reader.read(); if (next.done) { await stub.releaseOriginal(id); closed = true; reader.releaseLock(); request.signal.removeEventListener('abort', abort); controller.close(); } else controller.enqueue(next.value); }
        catch (error) { let cause = error; try { await close(error); } catch (cleanupError) { cause = new AggregateError([error, cleanupError], 'Original relay response failed'); } controller.error(cause); }
      }, cancel: close,
    }), { status: response.status, headers: response.headers });
  } catch (error) { request.signal.removeEventListener('abort', abort); throw error; }
}
// One coordination atom per actual runtime, not per application. Transport jobs
// are deliberately ephemeral: no prompt, bearer, or generated byte is written
// to Durable Object storage. Canonical conversation persistence lives elsewhere.
export class RuntimeRelay extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    ctx.storage.sql.exec('CREATE TABLE IF NOT EXISTS uncertain_cleanup (job_id TEXT PRIMARY KEY)');
    ctx.storage.sql.exec('CREATE TABLE IF NOT EXISTS original_custody (job_id TEXT PRIMARY KEY, connector_lease TEXT)');
    ctx.storage.sql.exec('CREATE TABLE IF NOT EXISTS terminal_receipts (job_id TEXT PRIMARY KEY, acknowledged_at INTEGER NOT NULL)');
    // Any original admitted by an earlier isolate remains unconfirmed until its
    // exact connector joins and acknowledges it. Only opaque IDs are persisted.
    ctx.storage.sql.exec('INSERT OR IGNORE INTO uncertain_cleanup(job_id) SELECT job_id FROM original_custody WHERE connector_lease IS NOT NULL');
    // Persisted but unclaimed queue entries never entered a connector.
    ctx.storage.sql.exec('INSERT OR REPLACE INTO terminal_receipts(job_id, acknowledged_at) SELECT job_id, ? FROM original_custody WHERE connector_lease IS NULL', Date.now());
    ctx.storage.sql.exec('DELETE FROM original_custody WHERE connector_lease IS NULL');
    this.uncertain = new Set([...ctx.storage.sql.exec('SELECT job_id FROM uncertain_cleanup')].map(row => row.job_id));
    this.jobs = new Map(); this.admitting = new Map(); this.admissions = 0; this.lease = null; this.leaseAt = 0; this.poll = null; this.events = null; this.notices = [];
  }
  wake() { this.poll?.resolve(); this.events?.resolve(); }
  expire() {
    if (this.lease && Date.now() - this.leaseAt > 35000) {
      for (const job of this.jobs.values()) this.cancel(job, 'connector_unavailable');
      this.lease = null; this.notices = [];
    }
  }
  remove(job, acknowledged = true) {
    clearTimeout(job.timer); clearTimeout(job.retireTimer); this.jobs.delete(job.id); job.authorization = null; job.body = null; job.credit?.resolve();
    if (acknowledged) {
      this.ctx.storage.sql.exec('DELETE FROM original_custody WHERE job_id = ?', job.id);
      this.ctx.storage.sql.exec('INSERT OR REPLACE INTO terminal_receipts(job_id, acknowledged_at) VALUES (?, ?)', job.id, Date.now());
      job.terminal.resolve();
    }
    else { this.uncertain.add(job.id); this.ctx.storage.sql.exec('INSERT OR IGNORE INTO uncertain_cleanup(job_id) VALUES (?)', job.id); job.terminal.reject(new Error('Original runtime cleanup unconfirmed')); }
  }
  cancel(job, reason) {
    if (job.cancelled || !this.jobs.has(job.id)) return job.terminal.promise;
    job.cancelled = true; job.body = null; job.authorization = null; clearTimeout(job.timer); job.credit?.resolve();
    if (!job.headersSent) job.ready.resolve(json(502, { error: reason }));
    job.error = new Error(reason); job.available?.resolve();
    if (job.claimed) { this.notices.push({ jobId: job.id, cancel: true }); this.wake(); job.retireTimer = setTimeout(() => this.remove(job, false), 5000); }
    else this.remove(job);
    return job.terminal.promise;
  }
  cancelConnectorWait(id, lease) {
    if (lease !== this.lease) throw new Error('Connector ownership unavailable');
    for (const slot of ['poll', 'events']) {
      const waiter = this[slot];
      if (waiter?.id === id) { waiter.cancelled = true; waiter.resolve(); return; }
    }
  }
  releaseOriginal(id) {
    const receipt = [...this.ctx.storage.sql.exec('SELECT job_id FROM terminal_receipts WHERE job_id = ?', id)][0];
    if (!receipt) throw new Error('Original terminal acknowledgement unavailable');
    this.ctx.storage.sql.exec('DELETE FROM terminal_receipts WHERE job_id = ?', id);
  }
  async retireOriginal(id) {
    const job = this.jobs.get(id); if (job) return this.cancel(job, 'client_cancelled');
    const pending = this.admitting.get(id); if (pending) { pending.cancelled = true; return pending.completion.promise; }
    const receipt = [...this.ctx.storage.sql.exec('SELECT acknowledged_at FROM terminal_receipts WHERE job_id = ?', id)][0];
    if (receipt) return;
    throw new Error('Original runtime custody unavailable');
  }
  async dispatch(request) {
    this.expire(); const path = new URL(request.url).pathname; let admission = false, admissionId, pending;
    try {
      if (path.startsWith('/connector/')) return await this.connector(request, path);
      if (!((request.method === 'GET' && path === '/api/tags') || (request.method === 'POST' && path === '/api/chat'))) return json(404, { error: 'route_unavailable' });
      if (!this.lease) return json(503, { error: 'connector_unavailable' });
      if (this.uncertain.size) return json(503, { error: 'original_runtime_cleanup_unconfirmed' });
      if (this.jobs.size + this.admissions + [...this.ctx.storage.sql.exec('SELECT COUNT(*) AS count FROM terminal_receipts')][0].count >= 4) return json(429, { error: 'runtime_busy' });
      this.admissions++; admission = true; admissionId = request.headers.get('X-Original-Relay-Job');
      if (!uuid(admissionId)) admissionId = crypto.randomUUID();
      pending = { cancelled: false, completion: deferred() }; this.admitting.set(admissionId, pending);
      const authorization = request.headers.get('Authorization'); const accountId = await current(this.env, authorization);
      if (!accountId) return json(401, { error: 'account_authentication_required' });
      if (this.jobs.size >= 4) return json(429, { error: 'runtime_busy' });
      let body = null;
      if (request.method === 'POST') {
        body = new TextDecoder().decode(await bytes(request, 1048576)); let payload;
        try { payload = JSON.parse(body); } catch { return json(400, { error: 'invalid_request' }); }
        if (payload?.model !== this.env.MODEL_ID || !Array.isArray(payload.messages) || !payload.messages.length || typeof payload.stream !== 'boolean') return json(400, { error: 'model_or_request_unavailable' });
      }
      if (this.jobs.size >= 4) return json(429, { error: 'runtime_busy' });
      if (this.uncertain.size) return json(503, { error: 'original_runtime_cleanup_unconfirmed' });
      if (pending.cancelled) return json(499, { error: 'client_cancelled' });
      const job = { id: admissionId, accountId, authorization, method: request.method, path, body, ready: deferred(), terminal: deferred(), claimed: false,
        cancelled: false, headersSent: false, uploading: false, sequence: 0, outputBytes: 0, credit: null, controller: null, timer: null, retireTimer: null };
      job.terminal.promise.catch(() => {});
      const stream = new ReadableStream({
        async pull(controller) {
          if (!job.pending && !job.done && !job.error) { job.available = deferred(); await job.available.promise; job.available = null; }
          if (job.error) {
            if (job.path === '/api/chat' && job.headersSent) { controller.enqueue(new TextEncoder().encode(JSON.stringify({ error: job.error.message }) + '\n')); controller.close(); }
            else controller.error(job.error);
            return;
          }
          if (job.pending) { const data = job.pending; job.pending = null; controller.enqueue(data); job.credit?.resolve(); job.credit = null; }
          else if (job.done) controller.close();
        },
        cancel: () => this.cancel(job, 'client_cancelled'),
      }, { highWaterMark: 0 });
      this.ctx.storage.sql.exec('INSERT INTO original_custody(job_id) VALUES (?)', job.id);
      job.stream = stream; this.admissions--; admission = false; this.admitting.delete(admissionId); pending.completion.resolve(); this.jobs.set(job.id, job); job.timer = setTimeout(() => this.cancel(job, 'runtime_deadline_exceeded'), 120000);
      request.signal.addEventListener('abort', () => this.cancel(job, 'client_cancelled'), { once: true });
      if (request.signal.aborted) this.cancel(job, 'client_cancelled'); else this.wake();
      return await job.ready.promise;
    } catch { return json(502, { error: 'runtime_unavailable' }); }
    finally { if (admission) { this.admissions--; this.admitting.delete(admissionId); pending?.completion.resolve(); } }
  }
  async connector(request, path) {
    if (request.method !== 'POST') return json(404, { error: 'route_unavailable' });
    const lease = request.headers.get('X-Connector-Lease'); if (!uuid(lease)) return json(403, { error: 'connector_lease_required' });
    if (!this.lease && path === '/connector/poll') { this.lease = lease; this.leaseAt = Date.now(); }
    const original = path === '/connector/terminal' ? [...this.ctx.storage.sql.exec('SELECT connector_lease FROM original_custody WHERE job_id = ?', request.headers.get('X-Job-Id'))][0] : null;
    const ownsRecoveredTerminal = original?.connector_lease === lease;
    if (original && !ownsRecoveredTerminal) return json(409, { error: 'original_connector_lease_required' });
    if (this.lease !== lease && !ownsRecoveredTerminal) return json(409, { error: 'connector_lease_unavailable' });
    this.leaseAt = Date.now();
    if (path === '/connector/poll' || path === '/connector/events') {
      const slot = path.endsWith('/poll') ? 'poll' : 'events';
      if (this[slot]) return json(409, { error: 'connector_wait_in_progress' });
      const find = () => slot === 'events' ? this.notices.length > 0 : [...this.jobs.values()].some(j => !j.claimed && !j.cancelled);
      if (!find()) {
        const waiter = deferred(); waiter.id = request.headers.get('X-Original-Connector-Wait'); this[slot] = waiter; const timer = setTimeout(waiter.resolve, 20000);
        try { await waiter.promise; } finally { clearTimeout(timer); if (this[slot] === waiter) this[slot] = null; }
        if (waiter.cancelled) return json(499, { error: 'connector_cancelled' });
      }
      if (this.lease !== lease) return json(409, { error: 'connector_lease_unavailable' });
      this.leaseAt = Date.now();
      if (slot === 'events') return json(200, { events: this.notices.splice(0) });
      const job = [...this.jobs.values()].find(j => !j.claimed && !j.cancelled);
      if (!job) return json(200, { job: null });
      this.ctx.storage.sql.exec('UPDATE original_custody SET connector_lease = ? WHERE job_id = ?', lease, job.id);
      job.claimed = true;
      const result = json(200, { job: { id: job.id, method: job.method, path: job.path, body: job.body, authorization: job.authorization, accountId: job.accountId } });
      job.body = null; return result;
    }
    const id = request.headers.get('X-Job-Id'); const job = this.jobs.get(id);
    let result;
    if (path === '/connector/terminal') {
      result = JSON.parse(new TextDecoder().decode(await bytes(request, 128)));
      if (result.cleanupSettled !== true) {
        if (job) { if (!job.cancelled) this.cancel(job, 'original_runtime_cleanup_failed'); this.remove(job, false); }
        return json(502, { error: 'original_runtime_cleanup_unconfirmed' });
      }
      if (!job && this.uncertain.has(id)) {
        this.ctx.storage.sql.exec('DELETE FROM uncertain_cleanup WHERE job_id = ?', id); this.uncertain.delete(id);
        this.ctx.storage.sql.exec('DELETE FROM original_custody WHERE job_id = ?', id);
        this.ctx.storage.sql.exec('INSERT OR REPLACE INTO terminal_receipts(job_id, acknowledged_at) VALUES (?, ?)', id, Date.now());
          return json(200, { terminal: true, lateAcknowledgement: true });
      }
    }
    if (!job || !job.claimed) return json(404, { error: 'original_job_unavailable' });
    if (path === '/connector/terminal') {
      if (result.completed !== true && !job.cancelled) this.cancel(job, 'original_runtime_failed');
      if (!job.cancelled) { if (!job.headersSent) job.ready.resolve(json(502, { error: 'runtime_unavailable' })); else { job.done = true; job.available?.resolve(); } }
      this.remove(job); return json(200, { terminal: true });
    }
    if (job.cancelled) return json(410, { error: 'original_job_retired' });
    if (path === '/connector/headers') {
      if (job.headersSent) return json(409, { error: 'headers_already_owned' });
      const value = JSON.parse(new TextDecoder().decode(await bytes(request, 1024)));
      if (value.status !== 200 || !['application/json', 'application/x-ndjson'].includes(value.contentType)) { this.cancel(job, 'runtime_unavailable'); return json(400, { error: 'invalid_runtime_headers' }); }
      job.headersSent = true; job.ready.resolve(new Response(job.stream, { status: 200, headers: { 'Content-Type': value.contentType, 'Content-Encoding': 'identity', 'Cache-Control': 'no-store, no-transform', 'Referrer-Policy': 'no-referrer' } }));
      return json(200, { ready: true });
    }
    if (path === '/connector/chunk') {
      if (!job.headersSent || job.uploading || request.headers.get('X-Chunk-Sequence') !== String(job.sequence)) return json(409, { error: 'original_chunk_order_required' });
      job.uploading = true;
      try {
        const data = await bytes(request, 65536);
        if (!data.length || job.outputBytes + data.length > 8388608) { this.cancel(job, 'runtime_output_limit'); return json(413, { error: 'runtime_output_limit' }); }
        const accountId = await current(this.env, job.authorization);
        if (job.cancelled || accountId !== job.accountId) { this.cancel(job, 'account_context_changed'); return json(401, { error: 'account_authentication_required' }); }
        job.outputBytes += data.length; job.sequence++;
        job.credit = deferred(); const credit = job.credit; job.pending = data; job.available?.resolve();
        await credit.promise;
        return job.cancelled ? json(410, { error: 'original_job_retired' }) : json(200, { sequence: job.sequence });
      } finally { job.uploading = false; }
    }
    return json(404, { error: 'route_unavailable' });
  }
}
