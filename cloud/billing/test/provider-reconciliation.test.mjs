import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { createHmac } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { receiveWebhook } from '../src/stripe-webhook.mjs';
import { claimEvent, completeEvent, failEvent } from '../src/inbox-processing.mjs';
import { readClaimedStripeState, ProviderReadError } from '../src/provider-reconciliation.mjs';

const now = 1900000000;
const secret = 'whsec_isolated_recovery_test';
const merchant = { object: 'account', id: 'acct_fixture' };
const json = value => Response.json(value);
function adapter(db) {
  return { prepare(sql) { return { bind(...args) { return {
    async run() { return db.prepare(sql).run(...args); },
    async first() { return db.prepare(sql).get(...args) ?? null; },
    async all() { return { results: db.prepare(sql).all(...args) }; }
  }; } }; } };
}
async function fixture(t, { object = 'invoice', type = 'invoice.paid', objectId = 'in_fixture', mode = 'test' } = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'billing-read-recovery-'));
  const file = join(directory, 'billing.sqlite');
  let db = new DatabaseSync(file), time = now;
  db.exec(readFileSync(new URL('../schema/0001-stripe-inbox.sql', import.meta.url), 'utf8'));
  const event = { object: 'event', id: 'evt_fixture', type, created: now - 100, livemode: mode === 'live',
    api_version: '2020-08-27', data: { object: { object, id: objectId, status: 'old_snapshot' } } };
  const bytes = JSON.stringify(event);
  const signature = `t=${now},v1=${createHmac('sha256', secret).update(`${now}.`).update(bytes).digest('hex')}`;
  let binding = adapter(db);
  const accepted = await receiveWebhook(new Request('https://fixture.invalid/webhooks/stripe', {
    method: 'POST', headers: { 'stripe-signature': signature }, body: bytes
  }), { STRIPE_MODE: mode, STRIPE_WEBHOOK_SECRET: secret, BILLING_DB: binding,
    SIGNATURE_TOLERANCE_SECONDS: 300, MAX_WEBHOOK_BYTES: 65536 }, now);
  assert.equal(accepted.status, 202);
  const claim = await claimEvent(binding, mode, event.id, now, 60);
  const requests = [];
  const current = { object, id: objectId, livemode: mode === 'live', status: 'current_provider_state' };
  let respond = request => {
    const path = new URL(request.url).pathname;
    if (path === '/v1/account') return json(merchant);
    if (path === '/v1/events/evt_fixture') return json(event);
    return json(current);
  };
  const config = { expectedMerchantID: merchant.id, apiVersion: '2026-09-30.endive', maxResponseBytes: 65536,
    timeoutMs: 1000, clock: () => time, transport: { async fetch(request) {
      requests.push(request); return respond(request);
    } } };
  t.after(() => { db.close(); rmSync(directory, { recursive: true }); });
  return { config, event, current, claim, requests, mode, file,
    get binding() { return binding; }, db: () => db,
    time(value) { time = value; }, respond(fn) { respond = fn; },
    restart() { db.close(); db = new DatabaseSync(file); binding = adapter(db); },
    rows() { return db.prepare('SELECT * FROM stripe_event_inbox').all(); },
    read(options) { return readClaimedStripeState(binding, mode, event.id, claim.claim_token, config, options); }
  };
}
async function rejectsCode(operation, code, retryable = false) {
  await assert.rejects(operation, error => error instanceof ProviderReadError && error.code === code && error.retryable === retryable);
}

test('C3-PR-01: real signed durable receipt recovers current invoice after restart with three bounded GETs and no effect', async t => {
  const f = await fixture(t); f.restart(); const before = f.rows();
  const result = await f.read();
  assert.equal(result.event.data.object.status, 'old_snapshot');
  assert.equal(result.currentObject.status, 'current_provider_state');
  assert.equal(result.merchantID, merchant.id);
  assert.equal(result.eventAPIVersion, '2020-08-27');
  assert.equal(result.requestedAPIVersion, '2026-09-30.endive');
  assert.deepEqual(f.requests.map(r => new URL(r.url).pathname), ['/v1/account', '/v1/events/evt_fixture', '/v1/invoices/in_fixture']);
  for (const r of f.requests) {
    assert.equal(r.method, 'GET'); assert.equal(r.redirect, 'error');
    assert.equal(new URL(r.url).origin, 'https://api.stripe.com');
    assert.equal(r.headers.get('Stripe-Version'), '2026-09-30.endive');
    assert.equal(r.headers.get('Authorization'), null);
    assert.ok(!r.url.includes(f.claim.claim_token)); assert.ok(![...r.headers].flat().includes(f.claim.claim_token));
  }
  assert.deepEqual(f.rows(), before); f.restart(); assert.deepEqual(f.rows(), before);
  assert.equal(f.rows()[0].state, 'claimed'); // A provider read cannot acknowledge canonical effects.
});

test('C3-PR-02: delayed subscription event reads current subscription in explicit live mode without changing receipts', async t => {
  const f = await fixture(t, { object: 'subscription', objectId: 'sub_fixture', type: 'customer.subscription.deleted', mode: 'live' });
  f.current.status = 'canceled'; const before = f.rows();
  const result = await f.read();
  assert.equal(result.mode, 'live'); assert.equal(result.currentObject.status, 'canceled');
  assert.equal(new URL(f.requests[2].url).pathname, '/v1/subscriptions/sub_fixture');
  assert.deepEqual(f.rows(), before);
});

test('C3-PR-03: wrong, expired, failed or completed lease cannot trigger provider reads', async t => {
  const f = await fixture(t); const before = f.rows();
  await rejectsCode(() => readClaimedStripeState(f.binding, f.mode, f.event.id, 'wrong_token', f.config), 'ProviderReadClaimNotCurrent');
  assert.deepEqual(f.rows(), before);
  f.time(now + 60); await rejectsCode(() => f.read(), 'ProviderReadClaimNotCurrent');
  f.time(now); await failEvent(f.binding, f.mode, f.event.id, f.claim.claim_token);
  await rejectsCode(() => f.read(), 'ProviderReadClaimNotCurrent');
  const successor = await claimEvent(f.binding, f.mode, f.event.id, now, 60);
  assert.equal(await completeEvent(f.binding, f.mode, f.event.id, successor.claim_token, now), true);
  await rejectsCode(() => f.read(), 'ProviderReadClaimNotCurrent');
  assert.equal(f.requests.length, 0);
});

test('C3-PR-04: provider reads returning after lease expiry/reclaim cannot return stale ownership', async t => {
  const f = await fixture(t); let successor;
  f.respond(async request => {
    const path = new URL(request.url).pathname;
    if (path === '/v1/account') return json(merchant);
    if (path.startsWith('/v1/events/')) return json(f.event);
    f.time(now + 60);
    const otherProcess = new DatabaseSync(f.file);
    try { successor = await claimEvent(adapter(otherProcess), f.mode, f.event.id, now + 60, 60); }
    finally { otherProcess.close(); }
    return json(f.current);
  });
  await rejectsCode(() => f.read(), 'ProviderReadClaimNotCurrent');
  assert.equal(f.rows()[0].claim_token, successor.claim_token);
  assert.equal(f.rows()[0].attempts, 2); assert.equal(f.rows()[0].state, 'claimed');
});

test('C3-PR-05: authenticated merchant mismatch refuses before event fetch', async t => {
  const f = await fixture(t); const before = f.rows();
  f.respond(() => json({ ...merchant, id: 'acct_foreign' }));
  await rejectsCode(() => f.read(), 'ProviderReadMerchantMismatch');
  assert.equal(f.requests.length, 1); assert.deepEqual(f.rows(), before);
});

test('C3-PR-06: event identity/type/creation/mode/connected-account mismatches and unsupported context cannot fetch current state', async t => {
  const f = await fixture(t); const before = f.rows();
  for (const mutation of [{ id: 'evt_foreign' }, { type: 'invoice.voided' }, { created: now }, { livemode: true }, { account: 'acct_foreign' }, { object: 'invoice' }, { context: 'unconfigured_context' }]) {
    const start = f.requests.length;
    f.respond(request => json(new URL(request.url).pathname === '/v1/account' ? merchant : { ...f.event, ...mutation }));
    await rejectsCode(() => f.read(), mutation.context ? 'ProviderReadContextUnsupported' : 'ProviderReadEventMismatch');
    assert.equal(f.requests.length - start, 2); assert.deepEqual(f.rows(), before);
  }
});

test('C3-PR-07: object identity/path injection and wrong current object/mode fail without state changes', async t => {
  const f = await fixture(t); const before = f.rows();
  for (const object of [{ object: 'invoice', id: '../../customers/cus_foreign' }, { object: 'subscription', id: 'sub_foreign' }, { object: 'invoice', id: '' }]) {
    f.respond(request => json(new URL(request.url).pathname === '/v1/account' ? merchant : { ...f.event, data: { object } }));
    await rejectsCode(() => f.read(), 'ProviderReadObjectIdentityInvalid'); assert.deepEqual(f.rows(), before);
  }
  for (const mutation of [{ id: 'in_foreign' }, { object: 'subscription' }, { livemode: true }]) {
    f.respond(request => {
      const path = new URL(request.url).pathname;
      return json(path === '/v1/account' ? merchant : path.startsWith('/v1/events/') ? f.event : { ...f.current, ...mutation });
    });
    await rejectsCode(() => f.read(), 'ProviderReadCurrentObjectMismatch'); assert.deepEqual(f.rows(), before);
  }
});

test('C3-PR-08: provider unavailability/rate limit and secret-containing faults are redacted with no retry or completion', async t => {
  const f = await fixture(t); const before = f.rows();
  for (const [status, code, retryable] of [[404, 'ProviderReadObjectUnavailable', false], [401, 'ProviderReadRejected', false], [429, 'ProviderReadRejected', true], [503, 'ProviderReadRejected', true]]) {
    const start = f.requests.length;
    f.respond(() => new Response('private_provider_body', { status }));
    await rejectsCode(() => f.read(), code, retryable); assert.equal(f.requests.length - start, 1);
  }
  f.respond(() => { throw new Error('private_provider_token'); });
  await assert.rejects(() => f.read(), error => error.code === 'ProviderReadTransportFailed' && !JSON.stringify(error).includes('private_provider_token'));
  assert.deepEqual(f.rows(), before);
});

test('C3-PR-09: streamed response byte ceiling, invalid UTF-8/JSON and content type fail closed', async t => {
  const f = await fixture(t); const before = f.rows();
  for (const [response, code] of [
    [() => new Response(new Uint8Array(65537), { headers: { 'content-type': 'application/json' } }), 'ProviderReadResponseTooLarge'],
    [() => new Response(Uint8Array.from([0xff]), { headers: { 'content-type': 'application/json' } }), 'ProviderReadResponseInvalid'],
    [() => new Response('{', { headers: { 'content-type': 'application/json' } }), 'ProviderReadResponseInvalid'],
    [() => new Response('null', { headers: { 'content-type': 'application/json' } }), 'ProviderReadResponseInvalid'],
    [() => new Response('{}', { headers: { 'content-type': 'text/plain' } }), 'ProviderReadResponseInvalid']
  ]) {
    f.respond(response);
    await rejectsCode(() => f.read(), code);
    assert.deepEqual(f.rows(), before);
  }
});

test('C3-PR-10: cancellation and timeout abort bounded transport, preserve claim and never complete', async t => {
  const f = await fixture(t); const before = f.rows();
  const cancelled = new AbortController(); cancelled.abort();
  await rejectsCode(() => f.read({ signal: cancelled.signal }), 'ProviderReadCancelled'); assert.equal(f.requests.length, 0);
  f.config.timeoutMs = 20; f.respond(() => new Promise(() => {}));
  await rejectsCode(() => f.read(), 'ProviderReadTimedOut', true); assert.equal(f.requests.at(-1).signal.aborted, true);
  f.config.timeoutMs = 1000; const active = new AbortController();
  f.respond(() => { active.abort(); return new Promise(() => {}); });
  await rejectsCode(() => f.read({ signal: active.signal }), 'ProviderReadCancelled');
  assert.equal(f.requests.at(-1).signal.aborted, true); assert.deepEqual(f.rows(), before);
});

test('C3-PR-11: unsupported receipt/configuration refuses without requests; body/read failures do not expose diagnostics', async t => {
  const f = await fixture(t, { type: 'charge.succeeded' }); const before = f.rows();
  await rejectsCode(() => f.read(), 'ProviderEventTypeUnsupported');
  for (const [key, value] of [['transport', undefined], ['expectedMerchantID', ''], ['apiVersion', 'latest'], ['maxResponseBytes', 0], ['timeoutMs', 0], ['clock', undefined]]) {
    const saved = f.config[key]; f.config[key] = value;
    await rejectsCode(() => f.read(), 'ProviderReadConfigurationUnavailable'); f.config[key] = saved;
  }
  assert.equal(f.requests.length, 0); assert.deepEqual(f.rows(), before);
  f.db().prepare("UPDATE stripe_event_inbox SET event_type='invoice.paid'").run();
  f.respond(() => new Response(new ReadableStream({ pull(controller) { controller.error(new Error('private_stream_diagnostic')); } }), { headers: { 'content-type': 'application/json' } }));
  await rejectsCode(() => f.read(), 'ProviderReadResponseInvalid');
  f.db().exec('DROP TABLE stripe_event_inbox');
  await rejectsCode(() => f.read(), 'ProviderReadPersistenceUnavailable', true);
});

test('C3-PR-12: captured configuration cannot change merchant/version/transport across awaits', async t => {
  const f = await fixture(t); const originalFetch = f.config.transport.fetch;
  f.respond(request => {
    const path = new URL(request.url).pathname;
    if (path === '/v1/account') {
      f.config.expectedMerchantID = 'acct_changed'; f.config.apiVersion = '1999-01-01';
      f.config.transport.fetch = () => { throw new Error('replacement_transport'); };
      return json(merchant);
    }
    return json(path.startsWith('/v1/events/') ? f.event : f.current);
  });
  const result = await f.read();
  assert.equal(result.merchantID, merchant.id); assert.equal(result.requestedAPIVersion, '2026-09-30.endive');
  assert.equal(f.requests.length, 3); assert.ok(f.requests.every(r => r.headers.get('Stripe-Version') === '2026-09-30.endive'));
  f.config.transport.fetch = originalFetch;
});

test('C3-PR-13: timeout releases a stalled body read even when stream cancellation never settles', async t => {
  const f = await fixture(t); const before = f.rows(); let cancelled = false;
  f.config.timeoutMs = 20;
  f.respond(() => new Response(new ReadableStream({
    pull() { return new Promise(() => {}); },
    cancel() { cancelled = true; return new Promise(() => {}); }
  }), { headers: { 'content-type': 'application/json' } }));
  await rejectsCode(() => f.read(), 'ProviderReadTimedOut', true);
  assert.equal(cancelled, true); assert.equal(f.requests[0].signal.aborted, true); assert.deepEqual(f.rows(), before);
});

test('C3-PR-14: changed immutable receipt facts during provider fetch refuse without overwriting them', async t => {
  const f = await fixture(t);
  f.respond(request => {
    const path = new URL(request.url).pathname;
    if (path === '/v1/account') return json(merchant);
    if (path.startsWith('/v1/events/')) return json(f.event);
    f.db().prepare('UPDATE stripe_event_inbox SET payload_sha256=?').run('e'.repeat(64));
    return json(f.current);
  });
  await rejectsCode(() => f.read(), 'ProviderReadReceiptChanged');
  assert.equal(f.rows()[0].payload_sha256, 'e'.repeat(64)); assert.equal(f.rows()[0].claim_token, f.claim.claim_token);
});

test('C3-PR-15: redirected/unexpected response origin and non-Response transport result fail before event fetch', async t => {
  const f = await fixture(t); const before = f.rows();
  for (const metadata of [{ redirected: true }, { url: 'https://foreign.invalid/v1/account' }]) {
    f.respond(() => { const response = json(merchant); for (const [key, value] of Object.entries(metadata)) Object.defineProperty(response, key, { value }); return response; });
    await rejectsCode(() => f.read(), 'ProviderReadResponseOriginMismatch');
  }
  f.respond(() => ({ status: 200, body: merchant }));
  await rejectsCode(() => f.read(), 'ProviderReadResponseInvalid');
  assert.equal(f.requests.length, 3); assert.deepEqual(f.rows(), before);
});

test('C3-PR-16: externally constructed ProviderReadError from transport is redacted', async t => {
  const f = await fixture(t); const before = f.rows();
  f.respond(() => { throw new ProviderReadError('CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'); });
  await rejectsCode(() => f.read(), 'ProviderReadTransportFailed', true);
  assert.deepEqual(f.rows(), before);
});

test('C3-PR-17: externally constructed ProviderReadError from response stream is redacted', async t => {
  const f = await fixture(t); const before = f.rows();
  f.respond(() => new Response(new ReadableStream({ pull(controller) {
    controller.error(new ProviderReadError('CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'));
  } }), { headers: { 'content-type': 'application/json' } }));
  await rejectsCode(() => f.read(), 'ProviderReadResponseInvalid');
  assert.deepEqual(f.rows(), before);
});

test('C3-PR-18: externally constructed ProviderReadError from database is redacted', async t => {
  const f = await fixture(t); const before = f.rows();
  const externalDB = { prepare() { throw new ProviderReadError('CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'); } };
  await rejectsCode(() => readClaimedStripeState(externalDB, f.mode, f.event.id, f.claim.claim_token, f.config), 'ProviderReadPersistenceUnavailable', true);
  assert.equal(f.requests.length, 0); assert.deepEqual(f.rows(), before);
});

test('C3-PR-19: externally constructed ProviderReadError from clock is redacted', async t => {
  const f = await fixture(t); const before = f.rows();
  f.config.clock = () => { throw new ProviderReadError('CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'); };
  await rejectsCode(() => f.read(), 'ProviderReadPersistenceUnavailable', true);
  assert.equal(f.requests.length, 0); assert.deepEqual(f.rows(), before);
});

test('C3-PR-20: externally constructed ProviderReadError while reading configuration is redacted', async t => {
  const f = await fixture(t); const before = f.rows();
  Object.defineProperty(f.config, 'apiVersion', { get() { throw new ProviderReadError('CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'); } });
  await rejectsCode(() => f.read(), 'ProviderReadConfigurationUnavailable');
  assert.equal(f.requests.length, 0); assert.deepEqual(f.rows(), before);
});

test('C3-PR-21: a module-created refusal cannot be relabelled with a private diagnostic', async t => {
  const f = await fixture(t); const before = f.rows(); let refusal;
  try { await readClaimedStripeState(f.binding, f.mode, f.event.id, f.claim.claim_token, { ...f.config, timeoutMs: 0 }); }
  catch (error) { refusal = error; }
  assert.ok(refusal instanceof ProviderReadError); assert.equal(Object.isFrozen(refusal), true);
  assert.equal(Reflect.set(refusal, 'code', 'CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'), false);
  assert.equal(Reflect.set(refusal, 'message', 'CONTROLLED_PRIVATE_DIAGNOSTIC_SENTINEL'), false);
  assert.equal(refusal.code, 'ProviderReadConfigurationUnavailable');
  assert.equal(f.requests.length, 0); assert.deepEqual(f.rows(), before);
});
