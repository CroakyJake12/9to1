// HTTPS-only connector. No raw inference listener or prompt logging.
import fs from 'node:fs';
import { randomUUID } from 'node:crypto';
const runtimeOrigin = process.env.RUNTIME_ORIGIN;
const accountOrigin = process.env.ACCOUNT_ORIGIN;
const previewOrigin = process.env.PREVIEW_ORIGIN;
const modelId = process.env.MODEL_ID;
if (![runtimeOrigin, accountOrigin, previewOrigin].every(value => { try { const u = new URL(value); return u.protocol === 'https:' && u.origin === value; } catch { return false; } }) || !modelId) throw new Error('Exact configured origins/model required');
const credentialPath = process.env.RUNTIME_SERVICE_CREDENTIAL_FILE;
if (!credentialPath) throw new Error('Private service credential file required');
const credential = JSON.parse(fs.readFileSync(credentialPath, 'utf8'));
if (!credential.client_id || !credential.client_secret) throw new Error('Service credential unavailable');
const lease = randomUUID(), stop = new AbortController(), active = new Map();
let requests = 0, completed = 0, cancelled = 0, failed = 0;
function logStatus() { console.log(JSON.stringify({ time: new Date().toISOString(), event: 'connector_status', active: active.size, requests, completed, cancelled, failed })); }
async function relay(path, { body, jobId, sequence, signal = stop.signal } = {}) {
  const response = await fetch(runtimeOrigin + path, { method: 'POST', headers: {
    'CF-Access-Client-ID': credential.client_id, 'CF-Access-Client-Secret': credential.client_secret, 'X-Connector-Lease': lease,
    ...(jobId ? { 'X-Job-Id': jobId } : {}), ...(sequence !== undefined ? { 'X-Chunk-Sequence': String(sequence) } : {}),
    ...(typeof body === 'string' ? { 'Content-Type': 'application/json' } : {}),
  }, body, redirect: 'error', cache: 'no-store', signal });
  if (!response.ok) { await response.body?.cancel(); const error = new Error('RelayUnavailable'); error.status = response.status; throw error; }
  return response.json();
}
async function run(job, owner) {
  requests++; let succeeded = false, cleanupSettled = true, reader, bodyCancellation;
  owner.cancel = () => {
    if (reader && !bodyCancellation) {
      try { bodyCancellation = Promise.resolve(reader.cancel()); } catch (error) { bodyCancellation = Promise.reject(error); }
      bodyCancellation.catch(() => {});
    }
    owner.abort();
  };
  const deadline = setTimeout(() => owner.cancel(), 120000);
  try {
    if (!/^Bearer [^\s]{1,16384}$/.test(job.authorization) || !((job.method === 'GET' && job.path === '/api/tags' && job.body === null) || (job.method === 'POST' && job.path === '/api/chat' && typeof job.body === 'string' && job.body.length <= 1048576))) throw new Error('Invalid runtime job');
    if (job.method === 'POST') { const payload = JSON.parse(job.body); if (payload?.model !== modelId || !Array.isArray(payload.messages) || !payload.messages.length || typeof payload.stream !== 'boolean') throw new Error('Invalid model request'); }
    const account = await fetch(accountOrigin + '/api/account/current', { headers: { Authorization: job.authorization, Origin: previewOrigin }, redirect: 'error', signal: owner.signal });
    if (account.status !== 200) { await account.body?.cancel(); throw new Error('Account context unavailable'); }
    if ((await account.json()).accountId !== job.accountId) throw new Error('Account context unavailable');
    const response = await fetch('http://127.0.0.1:11434' + job.path, { method: job.method, headers: job.method === 'POST' ? { 'Content-Type': 'application/json' } : {}, body: job.body ?? undefined, signal: owner.signal });
    job.authorization = null; job.body = null;
    if (response.body) reader = response.body.getReader();
    if (!response.ok || !reader) throw new Error('Ollama unavailable');
    await relay('/connector/headers', { jobId: job.id, body: JSON.stringify({ status: 200, contentType: job.method === 'GET' ? 'application/json' : 'application/x-ndjson' }), signal: owner.signal });
    let sequence = 0;
    if (job.method === 'GET') {
      const parts = []; let total = 0;
      for (;;) { const part = await reader.read(); if (part.done) break; total += part.value.length; if (total > 1048576) throw new Error('Model catalogue too large'); parts.push(part.value); }
      const catalogue = JSON.parse(Buffer.concat(parts).toString('utf8')); if (!Array.isArray(catalogue.models)) throw new Error('Invalid actual model catalogue');
      const data = new TextEncoder().encode(JSON.stringify({ ...catalogue, models: catalogue.models.filter(m => m.name === modelId || m.model === modelId) }));
      if (data.length > 65536) throw new Error('Model catalogue too large');
      await relay('/connector/chunk', { jobId: job.id, sequence: sequence++, body: data, signal: owner.signal });
    } else {
      {
        for (;;) {
          const part = await reader.read(); if (part.done) break;
          for (let offset = 0; offset < part.value.length; offset += 65536)
            await relay('/connector/chunk', { jobId: job.id, sequence: sequence++, body: part.value.subarray(offset, offset + 65536), signal: owner.signal });
        }
      }
    }
    completed++; succeeded = true;
  } catch {
    if (owner.signal.aborted) cancelled++; else failed++;
    owner.cancel();
  } finally {
    clearTimeout(deadline);
    job.authorization = null; job.body = null;
    owner.cancel();
    try { await bodyCancellation; } catch { failed++; succeeded = false; cleanupSettled = false; }
    try { reader?.releaseLock(); } catch { failed++; succeeded = false; cleanupSettled = false; }
    // Terminal acknowledgement follows the original fetch/read/cancel task,
    // never just the act of sending AbortSignal.
    try { await relay('/connector/terminal', { jobId: job.id, body: JSON.stringify({ completed: succeeded, cleanupSettled }), signal: AbortSignal.timeout(10000) }); } catch { failed++; }
    active.delete(job.id); logStatus();
  }
}
async function poll() {
  while (!stop.signal.aborted) {
    try {
      if (active.size >= 2) { await Promise.race([...active.values()].map(value => value.task)); continue; }
      const { job } = await relay('/connector/poll');
      if (!job) continue;
      if (active.has(job.id)) throw new Error('Duplicate original job');
      const controller = new AbortController(); const task = run(job, controller); active.set(job.id, { controller, task });
    } catch { if (!stop.signal.aborted) { failed++; logStatus(); await new Promise(resolve => setTimeout(resolve, 2000)); } }
  }
}
async function events() {
  while (!stop.signal.aborted) {
    try { const reply = await relay('/connector/events'); for (const item of reply.events ?? []) if (item.cancel) active.get(item.jobId)?.controller.cancel(); }
    catch { if (!stop.signal.aborted) await new Promise(resolve => setTimeout(resolve, 2000)); }
  }
}
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { stop.abort(); for (const entry of active.values()) entry.controller.cancel(); });
logStatus(); await Promise.all([poll(), events()]); await Promise.allSettled([...active.values()].map(entry => entry.task)); logStatus();
