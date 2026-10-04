// Read-only recovery over the existing receipt lease. These provider reads are
// neither a verified settlement nor an entitlement/business-effect commit.
const stripeOrigin = 'https://api.stripe.com';
const eventID = value => typeof value === 'string' && /^evt_[A-Za-z0-9]+$/.test(value) && value.length <= 255;
const merchantID = value => typeof value === 'string' && /^acct_[A-Za-z0-9]+$/.test(value) && value.length <= 255;

export class ProviderReadError extends Error {
  constructor(code, retryable = false) {
    super(code);
    this.name = 'ProviderReadError';
    this.code = code;
    this.retryable = retryable;
  }
}

// The exported error class is inspectable, but external capabilities cannot
// mint a trusted diagnostic code by constructing it themselves.
const internalErrors = new WeakSet();
function refusal(code, retryable = false) {
  const error = new ProviderReadError(code, retryable);
  internalErrors.add(error);
  return Object.freeze(error);
}

function validateConfiguration(config) {
  if (!config || typeof config.transport?.fetch !== 'function' || !merchantID(config.expectedMerchantID) ||
      !/^\d{4}-\d{2}-\d{2}(?:\.[a-z][a-z0-9_]*)?$/.test(config.apiVersion ?? '') ||
      !Number.isSafeInteger(config.maxResponseBytes) || config.maxResponseBytes < 1 || config.maxResponseBytes > 1048576 ||
      !Number.isSafeInteger(config.timeoutMs) || config.timeoutMs < 1 || config.timeoutMs > 120000 ||
      typeof config.clock !== 'function') throw refusal('ProviderReadConfigurationUnavailable');
}

function epoch(clock) {
  const value = clock();
  if (!Number.isSafeInteger(value) || value < 0) throw refusal('ProviderReadClockInvalid');
  return value;
}

function objectRoute(type) {
  if (type.startsWith('invoice.')) return { object: 'invoice', prefix: 'in_', collection: 'invoices' };
  if (type.startsWith('customer.subscription.')) return { object: 'subscription', prefix: 'sub_', collection: 'subscriptions' };
  throw refusal('ProviderEventTypeUnsupported');
}

async function currentClaim(db, mode, id, token, now) {
  const row = await db.prepare(`SELECT event_id,event_type,provider_created,payload_sha256,accepted_at
    FROM stripe_event_inbox WHERE mode=? AND event_id=? AND state='claimed'
      AND claim_token=? AND lease_until>?`).bind(mode, id, token, now).first();
  if (!row) throw refusal('ProviderReadClaimNotCurrent');
  return row;
}

// transport.fetch is a server-configured, authenticated capability. It must
// honour Request.signal and redirect='error'. This module receives no API key,
// creates no transport itself and never forwards a lease token to the provider.
export async function readClaimedStripeState(db, mode, id, token, config, { signal } = {}) {
  let captured;
  try {
    validateConfiguration(config);
    if (!['test', 'live'].includes(mode) || !eventID(id) || typeof token !== 'string' || token.length < 1 || token.length > 255 ||
        typeof db?.prepare !== 'function') throw refusal('ProviderReadClaimInvalid');
    captured = { fetch: config.transport.fetch.bind(config.transport), expectedMerchantID: config.expectedMerchantID,
      apiVersion: config.apiVersion, maxResponseBytes: config.maxResponseBytes, timeoutMs: config.timeoutMs, clock: config.clock };
  } catch (error) {
    if (internalErrors.has(error)) throw error;
    throw refusal('ProviderReadConfigurationUnavailable');
  }
  const controller = new AbortController();
  let reason = 'ProviderReadCancelled';
  const abort = () => controller.abort();
  if (signal?.aborted) abort();
  else signal?.addEventListener('abort', abort, { once: true });
  const timer = setTimeout(() => { reason = 'ProviderReadTimedOut'; controller.abort(); }, captured.timeoutMs);
  const checkAbort = () => { if (controller.signal.aborted) throw refusal(reason, reason === 'ProviderReadTimedOut'); };
  async function bounded(operation) {
    checkAbort();
    let stop;
    const cancelled = new Promise((_, reject) => {
      stop = () => reject(refusal(reason, reason === 'ProviderReadTimedOut'));
      controller.signal.addEventListener('abort', stop, { once: true });
    });
    try { return await Promise.race([operation(), cancelled]); }
    finally { controller.signal.removeEventListener('abort', stop); }
  }
  async function get(path) {
    const request = new Request(stripeOrigin + path, { method: 'GET', redirect: 'error', signal: controller.signal,
      headers: { Accept: 'application/json', 'Stripe-Version': captured.apiVersion } });
    let response;
    try { response = await bounded(() => captured.fetch(request)); }
    catch (error) {
      if (internalErrors.has(error)) throw error;
      throw refusal('ProviderReadTransportFailed', true);
    }
    if (!(response instanceof Response)) throw refusal('ProviderReadResponseInvalid');
    if (response.redirected || (response.url && response.url !== request.url)) {
      void response.body?.cancel().catch(() => {});
      throw refusal('ProviderReadResponseOriginMismatch');
    }
    if (response.status !== 200) {
      void response.body?.cancel().catch(() => {});
      if (response.status === 404) throw refusal('ProviderReadObjectUnavailable');
      throw refusal('ProviderReadRejected', response.status === 429 || response.status >= 500);
    }
    if (!/^application\/json(?:\s*;|$)/i.test(response.headers.get('content-type') ?? '')) {
      void response.body?.cancel().catch(() => {});
      throw refusal('ProviderReadResponseInvalid');
    }
    const reader = response.body?.getReader();
    if (!reader) throw refusal('ProviderReadResponseInvalid');
    const chunks = []; let total = 0;
    try {
      while (true) {
        const { value, done } = await bounded(() => reader.read());
        if (done) break;
        total += value.byteLength;
        if (total > captured.maxResponseBytes) throw refusal('ProviderReadResponseTooLarge');
        chunks.push(value);
      }
      const bytes = new Uint8Array(total); let offset = 0;
      for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
      const value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
      if (!value || typeof value !== 'object' || Array.isArray(value)) throw refusal('ProviderReadResponseInvalid');
      return value;
    } catch (error) {
      // Discard provider bodies and transport exception text, including on errors.
      if (internalErrors.has(error)) throw error;
      throw refusal('ProviderReadResponseInvalid');
    } finally {
      void reader.cancel().catch(() => {});
      try { reader.releaseLock(); } catch { /* A cancelled outstanding read cannot delay refusal. */ }
    }
  }
  try {
    const receipt = await bounded(() => currentClaim(db, mode, id, token, epoch(captured.clock)));
    if (typeof receipt.event_type !== 'string' || !Number.isSafeInteger(receipt.provider_created) || receipt.provider_created < 0)
      throw refusal('ProviderReadReceiptInvalid');
    const route = objectRoute(receipt.event_type);
    const merchant = await get('/v1/account');
    if (merchant.object !== 'account' || merchant.id !== captured.expectedMerchantID) throw refusal('ProviderReadMerchantMismatch');
    const event = await get(`/v1/events/${id}`);
    if (event.object !== 'event' || event.id !== id || event.type !== receipt.event_type || event.created !== receipt.provider_created ||
        event.livemode !== (mode === 'live') || (event.account != null && event.account !== captured.expectedMerchantID))
      throw refusal('ProviderReadEventMismatch');
    if (event.context != null) throw refusal('ProviderReadContextUnsupported');
    const object = event.data?.object;
    if (object?.object !== route.object || typeof object.id !== 'string' || object.id.length > 255 ||
        !new RegExp(`^${route.prefix}[A-Za-z0-9]+$`).test(object.id)) throw refusal('ProviderReadObjectIdentityInvalid');
    const current = await get(`/v1/${route.collection}/${object.id}`);
    if (current.object !== route.object || current.id !== object.id || current.livemode !== (mode === 'live'))
      throw refusal('ProviderReadCurrentObjectMismatch');
    const finalReceipt = await bounded(() => currentClaim(db, mode, id, token, epoch(captured.clock)));
    if (['event_id', 'event_type', 'provider_created', 'payload_sha256', 'accepted_at']
      .some(key => finalReceipt[key] !== receipt[key])) throw refusal('ProviderReadReceiptChanged');
    checkAbort();
    return { merchantID: captured.expectedMerchantID, mode, requestedAPIVersion: captured.apiVersion,
      eventAPIVersion: event.api_version ?? null, event, currentObject: current };
  } catch (error) {
    if (internalErrors.has(error)) throw error;
    throw refusal('ProviderReadPersistenceUnavailable', true);
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', abort);
  }
}
