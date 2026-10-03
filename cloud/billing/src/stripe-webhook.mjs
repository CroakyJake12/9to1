const encoder = new TextEncoder();
const reply = (status, code, retryable = false) => Response.json({ code, retryable }, { status });

// Uses the raw bytes, never reserialised JSON. Rotated v1 signatures are supported.
export async function verifySignature(raw, header, secret, now, tolerance) {
  if (!header || header.length > 8192) return false;
  const fields = header.split(',').map(value => value.trim().split('='));
  const timestamps = fields.filter(([key]) => key === 't');
  const signatures = fields.filter(([key]) => key === 'v1').map(([, value]) => value);
  if (timestamps.length !== 1 || !/^\d+$/.test(timestamps[0][1] ?? '')) return false;
  const timestamp = Number(timestamps[0][1]);
  if (!Number.isSafeInteger(timestamp) || Math.abs(now - timestamp) > tolerance) return false;
  const prefix = encoder.encode(`${timestamps[0][1]}.`);
  const signed = new Uint8Array(prefix.length + raw.length);
  signed.set(prefix); signed.set(raw, prefix.length);
  const key = await crypto.subtle.importKey('raw', encoder.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['verify']);
  for (const signature of signatures) {
    if (!/^[a-fA-F0-9]{64}$/.test(signature ?? '')) continue;
    const bytes = Uint8Array.from(signature.match(/../g), value => parseInt(value, 16));
    if (await crypto.subtle.verify('HMAC', key, bytes, signed)) return true;
  }
  return false;
}

async function boundedBody(request, limit) {
  const reader = request.body?.getReader();
  if (!reader) return new Uint8Array();
  const chunks = []; let size = 0;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      size += value.length;
      if (size > limit) { await reader.cancel(); return null; }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const raw = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { raw.set(chunk, offset); offset += chunk.length; }
  return raw;
}

export async function receiveWebhook(request, env, now = Math.floor(Date.now() / 1000)) {
  if (new URL(request.url).pathname !== '/webhooks/stripe') return reply(404, 'NotFound');
  if (request.method !== 'POST') return reply(405, 'MethodNotAllowed');
  if (!['test', 'live'].includes(env.STRIPE_MODE) || !env.STRIPE_WEBHOOK_SECRET?.startsWith('whsec_') || !env.BILLING_DB ||
      !Number.isSafeInteger(env.SIGNATURE_TOLERANCE_SECONDS) || env.SIGNATURE_TOLERANCE_SECONDS <= 0 ||
      !Number.isSafeInteger(env.MAX_WEBHOOK_BYTES) || env.MAX_WEBHOOK_BYTES <= 0) return reply(503, 'ConfigurationUnavailable', true);
  try {
    const raw = await boundedBody(request, env.MAX_WEBHOOK_BYTES);
    if (!raw) return reply(413, 'PayloadTooLarge');
    if (!await verifySignature(raw, request.headers.get('stripe-signature'), env.STRIPE_WEBHOOK_SECRET, now, env.SIGNATURE_TOLERANCE_SECONDS)) return reply(400, 'InvalidSignature');
    let event;
    try { event = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(raw)); }
    catch { return reply(400, 'InvalidEvent'); }
    if (!event || event.object !== 'event' || typeof event.id !== 'string' || !/^evt_[A-Za-z0-9]+$/.test(event.id) || event.id.length > 255 ||
        typeof event.type !== 'string' || event.type.length === 0 || event.type.length > 255 ||
        !Number.isSafeInteger(event.created) || event.created < 0 || typeof event.livemode !== 'boolean' ||
        typeof event.data?.object?.id !== 'string') return reply(400, 'InvalidEvent');
    if (event.livemode !== (env.STRIPE_MODE === 'live')) return reply(400, 'ModeMismatch');
    const digest = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', raw)), byte => byte.toString(16).padStart(2, '0')).join('');
    // INSERT is a single durable transaction. Receipt only: reconciliation fetches current
    // Stripe objects later; event ordering can never update a subscription through this route.
    await env.BILLING_DB.prepare(`INSERT INTO stripe_event_inbox
      (mode,event_id,event_type,provider_created,payload_sha256,accepted_at)
      VALUES (?,?,?,?,?,?) ON CONFLICT(mode,event_id) DO NOTHING`)
      .bind(env.STRIPE_MODE, event.id, event.type, event.created, digest, now).run();
    const stored = await env.BILLING_DB.prepare('SELECT payload_sha256 FROM stripe_event_inbox WHERE mode=? AND event_id=?')
      .bind(env.STRIPE_MODE, event.id).first();
    if (!stored) return reply(503, 'PersistenceUnavailable', true);
    if (stored.payload_sha256 !== digest) return reply(409, 'EventIdentityConflict');
    return reply(202, 'AcceptedPendingReconciliation');
  } catch { return reply(503, 'PersistenceUnavailable', true); }
}

export default { fetch(request, env) { return receiveWebhook(request, env); } };
