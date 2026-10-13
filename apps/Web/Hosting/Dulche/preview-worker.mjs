// Proposed transport only; NOT DEPLOYED. The original asset host remains protected by Access.
const fail = (code, status) => Response.json({ error: code }, { status, headers: { 'Cache-Control': 'no-store' } });
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
async function readBounded(body, limit) {
  if (!body) return new Uint8Array();
  const reader = body.getReader(), chunks = []; let size = 0;
  try {
    for (;;) { const next = await reader.read(); if (next.done) break;
      size += next.value.byteLength; if (size > limit) { await reader.cancel(); throw new Error('RequestTooLarge'); }
      chunks.push(next.value);
    }
  } finally { reader.releaseLock(); }
  const result = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.byteLength; }
  return result;
}
export async function verifyAccount(request, env, signal) {
  const authorization = request.headers.get('authorization');
  if (!authorization || !/^Bearer [^\s]{1,16384}$/.test(authorization)) return null;
  const response = await env.ACCOUNT_AUTH.fetch(new Request(env.ACCOUNT_ORIGIN + '/api/account/current', {
    headers: { Authorization: authorization, Origin: env.PREVIEW_ORIGIN }, redirect: 'manual', signal,
  }));
  if (response.status !== 200 || !response.headers.get('content-type')?.startsWith('application/json')) { await response.body?.cancel(); return null; }
  let current;
  try { current = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(await readBounded(response.body, 16384))); }
  catch { return null; }
  return uuid(current?.accountId) ? current.accountId : null;
}
export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith('/api/dulche/')) return env.ASSETS.fetch(request);
    if (url.origin !== env.PREVIEW_ORIGIN || url.search || request.headers.get('sec-fetch-site') === 'cross-site' ||
        (request.headers.has('origin') && request.headers.get('origin') !== env.PREVIEW_ORIGIN)) return fail('origin_not_allowed', 403);
    const path = url.pathname.slice('/api/dulche/'.length);
    if (!((request.method === 'GET' && path === 'api/tags') || (request.method === 'POST' && path === 'api/chat'))) return fail('route_unavailable', 404);
    if (request.method === 'POST' && request.headers.get('origin') !== env.PREVIEW_ORIGIN) return fail('origin_required', 403);
    const controller = new AbortController();
    let upstream, reader, closeTask, finished = false;
    const close = reason => {
      if (closeTask) return closeTask;
      finished = true;
      let cancellation;
      try { cancellation = reader ? reader.cancel(reason) : upstream?.body?.cancel(reason); } catch (error) { cancellation = Promise.reject(error); }
      controller.abort();
      closeTask = Promise.resolve(cancellation).finally(() => { reader?.releaseLock(); request.signal.removeEventListener('abort', abort); });
      closeTask.catch(() => {});
      return closeTask;
    };
    const abort = () => { if (reader) close('Original request aborted').catch(() => {}); else controller.abort(); };
    request.signal.addEventListener('abort', abort, { once: true });
    if (request.signal.aborted) abort();
    try {
      if (controller.signal.aborted) return fail('cancelled', 499);
      const accountId = await verifyAccount(request, env, controller.signal);
      if (!accountId || controller.signal.aborted) return fail('unauthorized', 401);
      let body;
      if (path === 'api/chat') {
        if (!request.headers.get('content-type')?.startsWith('application/json')) return fail('content_type_required', 415);
        body = await readBounded(request.body, 1_048_576);
        let chat;
        try { chat = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(body)); } catch { return fail('invalid_request', 400); }
        if (!chat || chat.model !== env.MODEL_ID || !Array.isArray(chat.messages) || !chat.messages.length || typeof chat.stream !== 'boolean') return fail('model_or_request_unavailable', 400);
      }
      const origin = new URL(env.DULCHE_ORIGIN);
      if (origin.protocol !== 'https:' || origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash) return fail('runtime_unconfigured', 503);
      if (controller.signal.aborted) return fail('cancelled', 499);
      upstream = await fetch(new URL('/' + path, origin), { method: request.method, body,
        headers: { 'Content-Type': 'application/json', Authorization: request.headers.get('authorization'),
          'CF-Access-Client-Id': env.RUNTIME_ACCESS_CLIENT_ID, 'CF-Access-Client-Secret': env.RUNTIME_ACCESS_CLIENT_SECRET },
        redirect: 'manual', signal: controller.signal });
      if (upstream.body) reader = upstream.body.getReader();
      if (controller.signal.aborted || !upstream.ok) { await close('Original request refused'); return fail('runtime_unavailable', 502); }
      if (!reader) return fail('runtime_response_invalid', 502);
      const stream = new ReadableStream({
        async pull(target) {
          try {
            // The maintained authority rechecks both signed token and actual live session.
            const next = await reader.read();
            if (await verifyAccount(request, env, controller.signal) !== accountId) throw new Error('SessionContextChanged');
            if (controller.signal.aborted) throw new Error('Cancelled');
            if (next.done) { finished = true; target.close(); reader.releaseLock(); request.signal.removeEventListener('abort', abort); }
            else target.enqueue(next.value);
          } catch (error) {
            let cause = error;
            try { await close('Account or transport closed'); } catch (cleanupError) { cause = new AggregateError([error, cleanupError], 'Original runtime stream and cleanup failed'); }
            target.error(cause);
          }
        },
        cancel: close,
      });
      return new Response(stream, { headers: { 'Content-Type': upstream.headers.get('content-type') ?? 'application/octet-stream',
        'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'X-Content-Type-Options': 'nosniff' } });
    } catch (error) {
      try { await close('Original transport failed'); } catch (cleanupError) { throw new AggregateError([error, cleanupError], 'Original runtime transport and cleanup failed'); }
      return fail(error?.message === 'RequestTooLarge' ? 'request_too_large' : 'runtime_unavailable', error?.message === 'RequestTooLarge' ? 413 : 502);
    } finally {
      // The response stream retains cancellation until its own terminal outcome.
      if (!upstream?.body?.locked) request.signal.removeEventListener('abort', abort);
    }
  },
};
