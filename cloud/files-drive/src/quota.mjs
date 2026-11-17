// One ledger in the same SQLite-backed Durable Object for every attempt made by Files.
// These app ceilings leave headroom under Google's published Drive limits. They cannot
// account for other clients/projects or replace verification of the actual project quota.
// https://developers.google.com/workspace/drive/api/guides/limits
export const DEFAULT_DRIVE_LIMITS = Object.freeze({
  unitsPerMinute: 50_000,
  unitsPerDay: 10_000_000,
  uploadBytesPerDay: 100_000_000_000,
  downloadBytesPerDay: 200_000_000_000,
});

export const DRIVE_UNIT_COSTS = Object.freeze({
  metadata: 5,
  list: 100,
  watch: 100,
  mutate: 50,
  upload: 50,
  download: 200,
  export: 200,
  thumbnail: 5,
  // An app allowance for OAuth user-info, token and revocation requests, not a Drive cost.
  auth: 5,
});

const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;
const HOLD_MS = 30_000;
const START_MS = 5_000;
const TRANSFER_MS = 2 * MINUTE;
// Allow full transfer use of the byte budget with multiple requests per range.
const MAX_ENTRIES = 200_000;
const GC_BATCH = 128;
const BUDGETS = Object.keys(DEFAULT_DRIVE_LIMITS);
const ATTEMPT_ID = /^[a-zA-Z0-9_-]{1,96}$/;

export class QuotaError extends Error {
  constructor(code, status, message, { budget, retryAt, budgets = budget ? [budget] : [] } = {}) {
    super(message);
    this.name = 'QuotaError';
    this.code = code;
    this.status = status;
    this.budget = budget;
    this.budgets = Object.freeze([...budgets]);
    this.retryAt = retryAt;
  }
}

function invalidCost(message) {
  return new QuotaError('quotaInvalidCost', 400, message);
}

function conflict() {
  return new QuotaError('quotaAttemptConflict', 409, 'This quota attempt cannot be issued or changed again.');
}

function expired() {
  return new QuotaError('quotaAttemptExpired', 409, 'This quota attempt has expired.');
}

function attemptId(id) {
  if (typeof id !== 'string' || !ATTEMPT_ID.test(id)) throw invalidCost('Invalid quota attempt identifier.');
  return id;
}

function byteCost(value, required, field) {
  if (value === undefined && !required) return 0;
  if (!Number.isSafeInteger(value) || value < 0) throw invalidCost(`A bounded ${field} is required.`);
  return value;
}

function costs(attempt) {
  if (!attempt || typeof attempt !== 'object' || Array.isArray(attempt)) {
    throw invalidCost('A quota attempt is required.');
  }
  const { id, kind, uploadBytes, downloadBytes } = attempt;
  attemptId(id);
  if (typeof kind !== 'string' || !Object.hasOwn(DRIVE_UNIT_COSTS, kind)) {
    throw invalidCost('Unrecognized Drive operation cost.');
  }
  const upload = byteCost(uploadBytes, kind === 'upload', 'upload byte count');
  const download = byteCost(downloadBytes, kind === 'download' || kind === 'export', 'download byte count');
  if (upload > 0 && kind !== 'upload') throw invalidCost('Only an upload can reserve upload bytes.');
  return { id, kind, units: DRIVE_UNIT_COSTS[kind], upload, download };
}

function limitsWithinCeilings(overrides) {
  if (!overrides || typeof overrides !== 'object' || Array.isArray(overrides)) {
    throw new TypeError('Invalid Drive quota limits.');
  }
  for (const [name, value] of Object.entries(overrides)) {
    if (!Object.hasOwn(DEFAULT_DRIVE_LIMITS, name) || !Number.isSafeInteger(value) ||
        value < 0 || value > DEFAULT_DRIVE_LIMITS[name]) {
      throw new TypeError(`Invalid Drive quota limit: ${name}`);
    }
  }
  return Object.freeze({ ...DEFAULT_DRIVE_LIMITS, ...overrides });
}

// Round expiry up, never down. This bounds each counter to about 60 buckets
// (minute units) or 1,440 buckets (day units/bytes) with at most a bucket of delay.
function minuteExpiry(timestamp) {
  return Math.ceil((timestamp + MINUTE) / 1_000) * 1_000;
}

function dayExpiry(timestamp) {
  return Math.ceil((timestamp + DAY) / MINUTE) * MINUTE;
}

function transferExpiries(unitsAt, bytesAt) {
  return [minuteExpiry(unitsAt), dayExpiry(unitsAt), dayExpiry(bytesAt)];
}

function receipt(row) {
  const result = { id: row.id, state: row.state };
  if (row.state === 'reserved') result.reserveUntil = row.hold_until;
  return result;
}

export class DriveQuota {
  #storage;
  #limits;
  #now;
  #maxEntries;

  constructor(storage, { limits = {}, now = Date.now, maxEntries = MAX_ENTRIES } = {}) {
    if (!storage?.sql || typeof storage.transactionSync !== 'function' || typeof storage.sync !== 'function') {
      throw new TypeError('Drive quota requires transactional SQLite Durable Object storage.');
    }
    if (typeof now !== 'function' || !Number.isSafeInteger(maxEntries) || maxEntries < 1 || maxEntries > MAX_ENTRIES) {
      throw new TypeError('Invalid Drive quota storage options.');
    }
    this.#storage = storage;
    this.#limits = limitsWithinCeilings(limits);
    this.#now = now;
    this.#maxEntries = maxEntries;
    storage.transactionSync(() => {
      this.#query(`CREATE TABLE IF NOT EXISTS drive_quota_clock (
        singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
        last_now INTEGER NOT NULL CHECK (last_now >= 0),
        entry_count INTEGER NOT NULL CHECK (entry_count >= 0)
      )`);
      this.#query('INSERT OR IGNORE INTO drive_quota_clock VALUES (1, 0, 0)');
      this.#query(`CREATE TABLE IF NOT EXISTS drive_quota_attempts (
        id TEXT PRIMARY KEY NOT NULL,
        kind TEXT NOT NULL,
        units INTEGER NOT NULL,
        upload INTEGER NOT NULL,
        download INTEGER NOT NULL,
        state TEXT NOT NULL CHECK (state IN ('reserved', 'issued', 'expired', 'not-issued', 'unknown', 'completed')),
        hold_until INTEGER NOT NULL,
        start_by INTEGER,
        deadline_at INTEGER,
        minute_expiry INTEGER NOT NULL,
        unit_day_expiry INTEGER NOT NULL,
        byte_day_expiry INTEGER NOT NULL,
        purge_at INTEGER NOT NULL
      )`);
      this.#query('CREATE INDEX IF NOT EXISTS drive_quota_holds ON drive_quota_attempts(state, hold_until)');
      this.#query('CREATE INDEX IF NOT EXISTS drive_quota_purge ON drive_quota_attempts(purge_at)');
      this.#query(`CREATE TABLE IF NOT EXISTS drive_quota_charges (
        budget TEXT NOT NULL,
        expires_at INTEGER NOT NULL,
        amount INTEGER NOT NULL CHECK (amount >= 0),
        PRIMARY KEY (budget, expires_at)
      ) WITHOUT ROWID`);
      this.#query('CREATE INDEX IF NOT EXISTS drive_quota_expiry ON drive_quota_charges(expires_at)');
    });
  }

  // Attempt IDs are generated by the gateway for each fetch and retained for one
  // day plus the short hold/transfer window. This does not replace write intent
  // idempotency: a retried fetch has its own attempt ID and pays again.
  async reserve(attempt) {
    const requested = costs(attempt);
    return this.#transact((now) => {
      const existing = this.#attempt(requested.id);
      if (existing) {
        if (existing.kind !== requested.kind ||
            existing.upload !== requested.upload || existing.download !== requested.download) throw conflict();
        if (existing.state === 'reserved' && existing.hold_until <= now) throw expired();
        return receipt(existing);
      }
      this.#checkBudgets(requested);
      const { entry_count } = this.#query('SELECT entry_count FROM drive_quota_clock WHERE singleton = 1')[0];
      if (entry_count >= this.#maxEntries) {
        const [{ retryAt }] = this.#query('SELECT MIN(purge_at) AS retryAt FROM drive_quota_attempts');
        throw new QuotaError('quotaLedgerFull', 429, 'The Drive quota ledger is temporarily full.', { retryAt });
      }
      const holdUntil = now + HOLD_MS;
      const expiry = transferExpiries(holdUntil, holdUntil);
      this.#addCharges(requested, expiry, 1, now);
      this.#query(`INSERT INTO drive_quota_attempts
        (id, kind, units, upload, download, state, hold_until, minute_expiry, unit_day_expiry, byte_day_expiry, purge_at)
        VALUES (?, ?, ?, ?, ?, 'reserved', ?, ?, ?, ?, ?)`, requested.id, requested.kind, requested.units,
      requested.upload, requested.download, holdUntil, ...expiry, expiry[2]);
      this.#query('UPDATE drive_quota_clock SET entry_count = entry_count + 1 WHERE singleton = 1');
      return { id: requested.id, state: 'reserved', reserveUntil: holdUntil };
    });
  }

  // A permit is granted once. The caller must start exactly one upstream request
  // before startBy and cap the transport by the reserved bytes and deadlineAt.
  async issue(id) {
    attemptId(id);
    const issued = this.#transact((now) => {
      const row = this.#attempt(id);
      if (!row || row.state === 'expired' || (row.state === 'reserved' && row.hold_until <= now)) throw expired();
      if (row.state !== 'reserved') throw conflict();
      // A lower configured limit after restart must not allow an old held permit through.
      this.#checkBudgets({ units: 0, upload: 0, download: 0 });
      const startBy = now + START_MS;
      const deadlineAt = now + TRANSFER_MS;
      const expiry = transferExpiries(startBy, deadlineAt);
      this.#addCharges(row, this.#expiry(row), -1, now);
      this.#addCharges(row, expiry, 1, now);
      this.#query(`UPDATE drive_quota_attempts SET state = 'issued', start_by = ?, deadline_at = ?,
        minute_expiry = ?, unit_day_expiry = ?, byte_day_expiry = ?, purge_at = ? WHERE id = ?`,
      startBy, deadlineAt, ...expiry, expiry[2], id);
      return { id, state: 'issued', startBy, deadlineAt };
    });
    // Do not return an upstream dispatch permit while the charge is still buffered.
    await this.#storage.sync();
    if (Math.max(this.#clock(), this.#lastNow()) >= issued.startBy) throw expired();
    return issued;
  }

  // Unknown/failed sends keep the full charge. A definitively completed transfer can
  // optionally report a verified count; omitted counts keep the full amount.
  async settle(id, disposition, actual) {
    attemptId(id);
    if (!['completed', 'unknown', 'not-issued'].includes(disposition) ||
        (actual !== undefined && (disposition !== 'completed' || !actual || typeof actual !== 'object' ||
          Array.isArray(actual) || Object.keys(actual).some(key => !['uploadBytes', 'downloadBytes'].includes(key))))) {
      throw invalidCost('Invalid quota settlement.');
    }
    return this.#transact((now) => {
      const row = this.#attempt(id);
      if (!row) throw expired();
      if (row.state === disposition || (row.state === 'expired' && disposition === 'not-issued')) return receipt(row);
      if (row.state === 'unknown' && disposition === 'completed' && now >= row.deadline_at) return receipt(row);
      if (row.state === 'reserved' && disposition === 'not-issued') {
        this.#release(row, 'not-issued', now);
        return { id, state: 'not-issued' };
      }
      if (row.state !== 'issued' || disposition === 'not-issued') throw conflict();
      if (now >= row.deadline_at) disposition = 'unknown';
      if (disposition === 'completed') {
        const upload = actual?.uploadBytes === undefined ? row.upload : byteCost(actual.uploadBytes, true, 'upload byte count');
        const download = actual?.downloadBytes === undefined
          ? row.download : byteCost(actual.downloadBytes, true, 'download byte count');
        if (upload > row.upload || download > row.download) {
          throw invalidCost('Settled bytes cannot exceed the reserved transfer bound.');
        }
        const expiry = dayExpiry(now);
        this.#changeCharge('uploadBytesPerDay', row.byte_day_expiry, -row.upload, now);
        this.#changeCharge('downloadBytesPerDay', row.byte_day_expiry, -row.download, now);
        this.#changeCharge('uploadBytesPerDay', expiry, upload, now);
        this.#changeCharge('downloadBytesPerDay', expiry, download, now);
      }
      this.#query('UPDATE drive_quota_attempts SET state = ? WHERE id = ?', disposition, id);
      return { id, state: disposition };
    });
  }

  async status() {
    return this.#transact((now) => {
      const status = { asOf: now };
      for (const budget of BUDGETS) {
        const { used, resetAt } = this.#usage(budget);
        const limit = this.#limits[budget];
        status[budget] = { limit, used, remaining: Math.max(0, limit - used), resetAt };
      }
      return status;
    });
  }

  #query(sql, ...bindings) {
    return this.#storage.sql.exec(sql, ...bindings).toArray();
  }

  #clock() {
    const now = this.#now();
    if (!Number.isSafeInteger(now) || now < 0 || now > 8_640_000_000_000_000 - 2 * DAY) {
      throw new Error('Drive quota clock is unavailable.');
    }
    return now;
  }

  #lastNow() {
    return this.#query('SELECT last_now FROM drive_quota_clock WHERE singleton = 1')[0].last_now;
  }

  #transact(action) {
    const result = this.#storage.transactionSync(() => {
      const lastNow = this.#lastNow();
      const now = Math.max(lastNow, this.#clock());
      if (now > lastNow) this.#query('UPDATE drive_quota_clock SET last_now = ? WHERE singleton = 1', now);
      this.#cleanup(now);
      // Controlled refusals happen before mutation; persist the clock and cleanup
      // even when an attempt is refused. Unexpected errors roll back all work.
      try {
        return action(now);
      } catch (error) {
        if (error instanceof QuotaError) return error;
        throw error;
      }
    });
    if (result instanceof QuotaError) throw result;
    return result;
  }

  #attempt(id) {
    return this.#query('SELECT * FROM drive_quota_attempts WHERE id = ?', id)[0];
  }

  #expiry(row) {
    return [row.minute_expiry, row.unit_day_expiry, row.byte_day_expiry];
  }

  #usage(budget) {
    return this.#query(`SELECT COALESCE(SUM(amount), 0) AS used, MIN(expires_at) AS resetAt
      FROM drive_quota_charges WHERE budget = ?`, budget)[0];
  }

  #checkBudgets({ units, upload, download }) {
    const amounts = [units, units, upload, download];
    const budgets = [];
    let refusal;
    for (const [i, budget] of BUDGETS.entries()) {
      const amount = amounts[i];
      const limit = this.#limits[budget];
      if (amount > limit) {
        const isBytes = i >= 2;
        throw new QuotaError(isBytes ? 'quotaTransferTooLarge' : 'quotaExceeded', isBytes ? 413 : 429,
          'This attempt exceeds the app allowance even with an empty window.', { budget });
      }
      const { used } = this.#usage(budget);
      const excess = used + amount - limit;
      if (excess <= 0) continue;
      budgets.push(budget);
      const [{ retryAt }] = this.#query(`SELECT MIN(expires_at) AS retryAt FROM (
        SELECT expires_at, SUM(amount) OVER (ORDER BY expires_at ROWS UNBOUNDED PRECEDING) AS cleared
        FROM drive_quota_charges WHERE budget = ?
      ) WHERE cleared >= ?`, budget, excess);
      if (!refusal || retryAt > refusal.retryAt) refusal = { budget, retryAt };
    }
    if (refusal) throw new QuotaError('quotaExceeded', 429, 'The app’s Drive allowance is temporarily exhausted.', { ...refusal, budgets });
  }

  #addCharges({ units, upload, download }, expiry, sign, now) {
    this.#changeCharge('unitsPerMinute', expiry[0], sign * units, now);
    this.#changeCharge('unitsPerDay', expiry[1], sign * units, now);
    this.#changeCharge('uploadBytesPerDay', expiry[2], sign * upload, now);
    this.#changeCharge('downloadBytesPerDay', expiry[2], sign * download, now);
  }

  #changeCharge(budget, expiresAt, amount, now) {
    if (!amount || expiresAt <= now) return;
    if (amount > 0) {
      this.#query(`INSERT INTO drive_quota_charges (budget, expires_at, amount) VALUES (?, ?, ?)
        ON CONFLICT (budget, expires_at) DO UPDATE SET amount = amount + excluded.amount`,
      budget, expiresAt, amount);
    } else {
      const changed = this.#query(`UPDATE drive_quota_charges SET amount = amount + ?
        WHERE budget = ? AND expires_at = ? RETURNING amount`, amount, budget, expiresAt);
      if (changed.length !== 1) throw new Error('Drive quota ledger is inconsistent.');
      if (changed[0].amount === 0) {
        this.#query('DELETE FROM drive_quota_charges WHERE budget = ? AND expires_at = ?', budget, expiresAt);
      }
    }
  }

  #release(row, state, now) {
    this.#addCharges(row, this.#expiry(row), -1, now);
    this.#query('UPDATE drive_quota_attempts SET state = ? WHERE id = ?', state, row.id);
  }

  #cleanup(now) {
    this.#query('DELETE FROM drive_quota_charges WHERE expires_at <= ?', now);
    const holds = this.#query(`SELECT * FROM drive_quota_attempts
      WHERE state = 'reserved' AND hold_until <= ? ORDER BY hold_until LIMIT ?`, now, GC_BATCH);
    for (const hold of holds) this.#release(hold, 'expired', now);
    const removed = this.#query(`DELETE FROM drive_quota_attempts WHERE id IN (
      SELECT id FROM drive_quota_attempts WHERE purge_at <= ? ORDER BY purge_at LIMIT ?
    ) RETURNING id`, now, GC_BATCH);
    if (removed.length) {
      this.#query('UPDATE drive_quota_clock SET entry_count = entry_count - ? WHERE singleton = 1', removed.length);
    }
  }
}
