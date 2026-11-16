import assert from 'node:assert/strict';
import { mkdtempSync, rmdirSync, unlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import test from 'node:test';
import { DEFAULT_DRIVE_LIMITS, DRIVE_UNIT_COSTS, DriveQuota, QuotaError } from '../src/quota.mjs';

const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;
const T0 = Date.UTC(2026, 10, 15, 18);

// Exercise the production SQL with SQLite transactions. Only the Cloudflare
// storage wrapper/output gate is substituted; tests do not substitute a ledger.
function dbStorage(path) {
  const db = new DatabaseSync(path);
  const storage = {
    beforeQuery: undefined,
    beforeSync: undefined,
    sql: {
      exec(sql, ...args) {
        storage.beforeQuery?.(sql);
        return { toArray: () => db.prepare(sql).all(...args) };
      },
    },
    transactionSync(work) {
      db.exec('BEGIN IMMEDIATE');
      try {
        const result = work();
        db.exec('COMMIT');
        return result;
      } catch (error) {
        db.exec('ROLLBACK');
        throw error;
      }
    },
    async sync() { await storage.beforeSync?.(); },
    close() { db.close(); },
  };
  return storage;
}

function fixture(t, options) {
  const directory = mkdtempSync(join(tmpdir(), '9to1-drive-quota-'));
  const path = join(directory, 'quota.sqlite');
  const open = new Set();
  const time = { current: T0 };
  function attach(config = options) {
    const storage = dbStorage(path);
    open.add(storage);
    return {
      quota: new DriveQuota(storage, { now: () => time.current, ...config }),
      storage,
      close() { storage.close(); open.delete(storage); },
    };
  }
  t.after(() => {
    for (const storage of open) storage.close();
    unlinkSync(path);
    rmdirSync(directory);
  });
  return { time, attach, ...attach() };
}

function quotaError(code, expected = {}) {
  return error => {
    assert.ok(error instanceof QuotaError);
    assert.equal(error.code, code);
    for (const [key, value] of Object.entries(expected)) assert.equal(error[key], value);
    return true;
  };
}

async function start(quota, attempt) {
  await quota.reserve(attempt);
  return quota.issue(attempt.id);
}

test('default ceilings bound both weighted units and legacy request counts', async t => {
  const { quota, storage } = fixture(t);
  const limits = await quota.status();
  assert.equal(limits.unitsPerMinute.limit, 50_000);
  assert.ok(limits.unitsPerMinute.limit < 325_000 && limits.unitsPerMinute.limit < 1_000_000);
  const maximumAttempts = limits.unitsPerMinute.limit / Math.min(...Object.values(DRIVE_UNIT_COSTS));
  assert.equal(maximumAttempts, 10_000);
  assert.ok(maximumAttempts < 12_000);
  assert.equal(limits.unitsPerDay.limit, 0.025 * 400_000_000);
  assert.equal(limits.uploadBytesPerDay.limit, 100_000_000_000);
  assert.equal(limits.downloadBytesPerDay.limit, 200_000_000_000);
  assert.doesNotThrow(() => new DriveQuota(storage, { maxEntries: 200_000 }));
  assert.throws(() => new DriveQuota(storage, { maxEntries: 200_001 }));
});

test('minute admission never hides a concurrent daily refusal with an earlier reset', async t => {
  const { quota, time } = fixture(t, { limits: { unitsPerMinute: 100, unitsPerDay: 190 } });
  await start(quota, { id: 'first-window', kind: 'list' });
  time.current = T0 + DAY + 30_000;
  for (let i = 0; i < 12; i++) await start(quota, { id: 'new-window-' + i, kind: 'metadata' });
  await assert.rejects(quota.reserve({ id: 'both', kind: 'mutate' }), error => {
    quotaError('quotaExceeded', { budget: 'unitsPerMinute' })(error);
    assert.deepEqual(error.budgets, ['unitsPerMinute', 'unitsPerDay']);
    return true;
  });
});

test('all Drive operation classes charge their published unit cost; auth draws from the local cap', async t => {
  const { quota } = fixture(t);
  const costs = [
    ['metadata', 5], ['list', 100], ['watch', 100], ['mutate', 50],
    ['upload', 50], ['download', 200], ['export', 200], ['auth', 5],
  ];
  let total = 0;
  for (const [kind, units] of costs) {
    const id = `attempt-${kind}`;
    await start(quota, {
      id, kind, uploadBytes: kind === 'upload' ? 100 : 0, downloadBytes: 10,
    });
    await quota.settle(id, 'completed');
    total += units;
    const status = await quota.status();
    assert.equal(status.unitsPerMinute.used, total);
    assert.equal(status.unitsPerDay.remaining, DEFAULT_DRIVE_LIMITS.unitsPerDay - total);
  }
  assert.equal((await quota.status()).uploadBytesPerDay.used, 100);
  assert.equal((await quota.status()).downloadBytesPerDay.used, 80);
});

test('independent instances, concurrent tabs and duplicate issue calls cannot exceed a single allowance', async t => {
  const first = fixture(t, { limits: { unitsPerMinute: 300 } });
  const second = first.attach().quota;
  const requests = Array.from({ length: 12 }, (_, i) =>
    (i % 2 ? second : first.quota).reserve({ id: `tab-${i}`, kind: 'list' }));
  const results = await Promise.allSettled(requests);
  const accepted = results.filter(result => result.status === 'fulfilled');
  assert.equal(accepted.length, 3);
  for (const refused of results.filter(result => result.status === 'rejected')) {
    quotaError('quotaExceeded', { status: 429, budget: 'unitsPerMinute' })(refused.reason);
  }
  const id = accepted[0].value.id;
  const duplicateIssues = await Promise.allSettled([first.quota.issue(id), second.issue(id)]);
  assert.equal(duplicateIssues.filter(item => item.status === 'fulfilled').length, 1);
  quotaError('quotaAttemptConflict')(duplicateIssues.find(item => item.status === 'rejected').reason);
  assert.equal((await second.status()).unitsPerDay.used, 300);
});

test('minute and day windows roll independently and never expire before the latest possible dispatch', async t => {
  const { quota, time } = fixture(t, { limits: { unitsPerMinute: 100, unitsPerDay: 200 } });
  time.current++;
  const first = await start(quota, { id: 'first', kind: 'list' });
  assert.equal(first.startBy, T0 + 5_001);
  const minuteReset = T0 + 66_000;
  const dayReset = T0 + DAY + MINUTE;
  assert.equal((await quota.status()).unitsPerMinute.resetAt, minuteReset);
  time.current = minuteReset - 1;
  await assert.rejects(quota.reserve({ id: 'second', kind: 'list' }),
    quotaError('quotaExceeded', { budget: 'unitsPerMinute', retryAt: minuteReset }));
  time.current++;
  await start(quota, { id: 'second', kind: 'list' });
  time.current += 2 * MINUTE;
  await assert.rejects(quota.reserve({ id: 'third', kind: 'list' }),
    quotaError('quotaExceeded', { budget: 'unitsPerDay', retryAt: dayReset }));
  time.current = dayReset;
  await start(quota, { id: 'third', kind: 'list' });
  assert.equal((await quota.status()).unitsPerDay.used, 200);
});

test('retry time clears enough of the limiting bucket, including when multiple windows block', async t => {
  const { quota, time } = fixture(t, { limits: { unitsPerMinute: 400, downloadBytesPerDay: 100 } });
  await start(quota, { id: 'a', kind: 'download', downloadBytes: 40 });
  time.current += MINUTE;
  await start(quota, { id: 'b', kind: 'download', downloadBytes: 55 });
  const status = await quota.status();
  assert.equal(status.downloadBytesPerDay.resetAt, T0 + DAY + 2 * MINUTE);
  await assert.rejects(quota.reserve({ id: 'c', kind: 'download', downloadBytes: 50 }),
    quotaError('quotaExceeded', { budget: 'downloadBytesPerDay', retryAt: T0 + DAY + 3 * MINUTE }));
});

test('unissued reservations release capacity without permitting replay, even across restart', async t => {
  const first = fixture(t, { limits: { unitsPerMinute: 50 } });
  const change = { id: 'never-sent', kind: 'upload', uploadBytes: 41 };
  const held = await first.quota.reserve(change);
  assert.deepEqual(await first.quota.reserve(change), held);
  await assert.rejects(first.quota.reserve({ ...change, uploadBytes: 42 }), quotaError('quotaAttemptConflict'));
  await first.quota.settle(change.id, 'not-issued');
  first.close();

  const { quota } = first.attach();
  assert.deepEqual(await quota.reserve(change), { id: change.id, state: 'not-issued' });
  await assert.rejects(quota.issue(change.id), quotaError('quotaAttemptConflict'));
  assert.equal((await quota.status()).uploadBytesPerDay.used, 0);
  const expired = await quota.reserve({ id: 'expired', kind: 'upload', uploadBytes: 1 });
  first.time.current = expired.reserveUntil;
  await assert.rejects(quota.issue(expired.id), quotaError('quotaAttemptExpired'));
  assert.equal((await quota.status()).unitsPerMinute.used, 0);
  assert.equal((await quota.status()).uploadBytesPerDay.used, 0);
  assert.equal((await quota.settle(expired.id, 'not-issued')).state, 'expired');
});

test('ambiguous attempts and their retries each keep units and bounded transfer bytes through their respective deadlines', async t => {
  const first = fixture(t, { limits: { uploadBytesPerDay: 150 } });
  const aborted = await start(first.quota, { id: 'upload-1', kind: 'upload', uploadBytes: 70 });
  await first.quota.settle(aborted.id, 'unknown');
  await assert.rejects(first.quota.settle(aborted.id, 'not-issued'), quotaError('quotaAttemptConflict'));
  first.time.current += MINUTE;
  const retry = await start(first.quota, { id: 'upload-2', kind: 'upload', uploadBytes: 70 });
  first.close(); // crash without settling the second attempt
  const { quota } = first.attach();
  const status = await quota.status();
  assert.equal(status.uploadBytesPerDay.used, 140);
  assert.equal(status.unitsPerDay.used, 100);
  assert.deepEqual(await quota.reserve({ id: retry.id, kind: 'upload', uploadBytes: 70 }),
    { id: retry.id, state: 'issued' });
  await assert.rejects(quota.issue(retry.id), quotaError('quotaAttemptConflict'));
  await assert.rejects(quota.reserve({ id: 'upload-3', kind: 'upload', uploadBytes: 70 }),
    quotaError('quotaExceeded', { budget: 'uploadBytesPerDay', retryAt: aborted.deadlineAt + DAY }));
  first.time.current = retry.deadlineAt;
  assert.equal((await quota.settle(retry.id, 'completed', { uploadBytes: 0 })).state, 'unknown');
  assert.equal((await quota.settle(retry.id, 'completed', { uploadBytes: 0 })).state, 'unknown');
  first.time.current = aborted.deadlineAt + DAY - 1;
  assert.equal((await quota.status()).uploadBytesPerDay.used, 140);
  first.time.current++;
  assert.equal((await quota.status()).uploadBytesPerDay.used, 70);
  first.time.current = retry.deadlineAt + DAY;
  assert.equal((await quota.status()).uploadBytesPerDay.used, 0);
});

test('completed measurements can reconcile bytes once, from completion rather than dispatch', async t => {
  const { quota, time } = fixture(t, { limits: { downloadBytesPerDay: 100 } });
  const attempt = await start(quota, { id: 'export', kind: 'export', downloadBytes: 100 });
  time.current += MINUTE;
  await assert.rejects(quota.settle(attempt.id, 'completed', { downloadBytes: 101 }), quotaError('quotaInvalidCost'));
  assert.equal((await quota.status()).downloadBytesPerDay.used, 100);
  await quota.settle(attempt.id, 'completed', { downloadBytes: 60 });
  await quota.settle(attempt.id, 'completed', { downloadBytes: 0 });
  const status = await quota.status();
  assert.equal(status.downloadBytesPerDay.used, 60);
  assert.equal(status.downloadBytesPerDay.resetAt, T0 + DAY + MINUTE);
  assert.equal(status.unitsPerDay.used, 200);
  await assert.rejects(quota.settle(attempt.id, 'unknown'), quotaError('quotaAttemptConflict'));
});

test('rollback across restart cannot create a fresh clock epoch or refill rolling budgets', async t => {
  const first = fixture(t, { limits: { unitsPerMinute: 100 } });
  first.time.current += DAY;
  await start(first.quota, { id: 'before-rollback', kind: 'list' });
  const reset = (await first.quota.status()).unitsPerMinute.resetAt;
  first.close();
  first.time.current = T0;
  const { quota } = first.attach();
  const status = await quota.status();
  assert.equal(status.asOf, T0 + DAY);
  assert.equal(status.unitsPerMinute.remaining, 0);
  await assert.rejects(quota.reserve({ id: 'rolled-back', kind: 'list' }),
    quotaError('quotaExceeded', { retryAt: reset }));
  first.time.current = reset;
  await start(quota, { id: 'recovered', kind: 'list' });
});

test('expired attempt retention is bounded and published retry time allows new entries', async t => {
  const { quota, time } = fixture(t, { maxEntries: 2 });
  for (const id of ['a', 'b']) {
    await quota.reserve({ id, kind: 'metadata' });
    await quota.settle(id, 'not-issued');
  }
  const retryAt = T0 + DAY + MINUTE;
  await assert.rejects(quota.reserve({ id: 'c', kind: 'metadata' }),
    quotaError('quotaLedgerFull', { status: 429, retryAt }));
  assert.equal((await quota.status()).unitsPerDay.used, 0);
  time.current = retryAt;
  await start(quota, { id: 'c', kind: 'metadata' });
  await assert.rejects(quota.issue('a'), quotaError('quotaAttemptExpired'));
});

test('failures at the storage boundary never grant an uncharged dispatch permit', async t => {
  const first = fixture(t);
  const { quota, storage } = first;
  storage.beforeQuery = sql => {
    if (sql.includes('INSERT INTO drive_quota_attempts')) throw new Error('write failed');
  };
  const attempt = { id: 'bad-storage', kind: 'upload', uploadBytes: 100 };
  await assert.rejects(quota.reserve(attempt), /write failed/);
  storage.beforeQuery = undefined;
  assert.equal((await quota.status()).uploadBytesPerDay.used, 0);
  await assert.rejects(quota.issue(attempt.id), quotaError('quotaAttemptExpired'));

  await quota.reserve(attempt);
  storage.beforeSync = () => { throw new Error('sync failed'); };
  await assert.rejects(quota.issue(attempt.id), /sync failed/);
  storage.beforeSync = undefined;
  // Even if the issue commit survived an output-gate error, it cannot be issued
  // again or released by treating the lost acknowledgment as definitely unsent.
  assert.equal((await quota.status()).uploadBytesPerDay.used, 100);
  await assert.rejects(quota.issue(attempt.id), quotaError('quotaAttemptConflict'));
  await assert.rejects(quota.settle(attempt.id, 'not-issued'), quotaError('quotaAttemptConflict'));
});

test('a slow durability confirmation cannot grant a dispatch beyond its start-by time', async t => {
  const { quota, storage, time } = fixture(t);
  await quota.reserve({ id: 'late', kind: 'list' });
  storage.beforeSync = () => { time.current += 5_000; };
  await assert.rejects(quota.issue('late'), quotaError('quotaAttemptExpired'));
  await assert.rejects(quota.issue('late'), quotaError('quotaAttemptConflict'));
  assert.equal((await quota.status()).unitsPerMinute.used, 100);
});

test('malformed, unknown and oversized costs never reserve capacity', async t => {
  const { quota, storage } = fixture(t, { limits: { downloadBytesPerDay: 10 } });
  const id = 'malformed';
  for (const invalid of [null, false, NaN, Infinity, 0.5, -1, '10', Number.MAX_SAFE_INTEGER + 1, undefined]) {
    await assert.rejects(quota.reserve({ id, kind: 'download', downloadBytes: invalid }), quotaError('quotaInvalidCost'));
  }
  await assert.rejects(quota.reserve({ id, kind: 'upload' }), quotaError('quotaInvalidCost'));
  await assert.rejects(quota.reserve({ id, kind: 'export' }), quotaError('quotaInvalidCost'));
  await assert.rejects(quota.reserve({ id, kind: 'metadata', uploadBytes: 1 }), quotaError('quotaInvalidCost'));
  await assert.rejects(quota.reserve({ id, kind: 'constructor' }), quotaError('quotaInvalidCost'));
  await assert.rejects(quota.reserve({ id: 'x'.repeat(97), kind: 'metadata' }), quotaError('quotaInvalidCost'));
  await assert.rejects(quota.reserve(null), quotaError('quotaInvalidCost'));
  await assert.rejects(quota.reserve({ id, kind: 'download', downloadBytes: 11 }),
    quotaError('quotaTransferTooLarge', { status: 413, budget: 'downloadBytesPerDay', retryAt: undefined }));
  assert.equal((await quota.status()).unitsPerDay.used, 0);

  for (const limits of [
    { unitsPerMinute: DEFAULT_DRIVE_LIMITS.unitsPerMinute + 1 }, { unitsPerDay: Infinity },
    { downloadBytesPerDay: -1 }, { unknown: 1 },
  ]) {
    assert.throws(() => new DriveQuota(storage, { limits }), /Invalid Drive quota limit/);
  }
  const invalidClock = new DriveQuota(storage, { now: () => NaN });
  await assert.rejects(invalidClock.status(), /clock is unavailable/);
});

test('limits lowered after admission are enforced before any issue permit', async t => {
  const first = fixture(t);
  await first.quota.reserve({ id: 'old-settings', kind: 'download', downloadBytes: 100 });
  first.close();
  const { quota } = first.attach({ limits: { downloadBytesPerDay: 50 } });
  await assert.rejects(quota.issue('old-settings'), quotaError('quotaExceeded', { budget: 'downloadBytesPerDay' }));
  assert.equal((await quota.status()).downloadBytesPerDay.remaining, 0);
  await quota.settle('old-settings', 'not-issued');
  assert.equal((await quota.status()).downloadBytesPerDay.remaining, 50);
});
