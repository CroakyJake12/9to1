// Internal worker APIs only. Receipt state is not payment/entitlement state.
// Expired claims MUST reconcile provider/business state before applying effects.
export async function claimEvent(db, mode, eventId, now, leaseSeconds) {
  if (!['test','live'].includes(mode) || !Number.isSafeInteger(now) || !Number.isSafeInteger(leaseSeconds) || leaseSeconds <= 0 || !Number.isSafeInteger(now + leaseSeconds)) throw new TypeError('Invalid claim configuration');
  const token = crypto.randomUUID();
  const row = await db.prepare(`UPDATE stripe_event_inbox SET
    state='claimed',claim_token=?,lease_until=?,attempts=attempts+1
    WHERE mode=? AND event_id=? AND
      (state IN ('pending','reconciliation_required') OR (state='claimed' AND lease_until<=?))
    RETURNING event_id,event_type,provider_created,attempts,claim_token,lease_until`)
    .bind(token,now+leaseSeconds,mode,eventId,now).first();
  return row ? { ...row, reconciliationRequired: true } : null;
}

// Call only after the canonical consumer has durably committed/verified effects.
// Its own business-effect idempotency is REQUIRED and remains outside this inbox.
export async function completeEvent(db, mode, eventId, token, now) {
  const row = await db.prepare(`UPDATE stripe_event_inbox SET state='processed',claim_token=NULL,lease_until=NULL
    WHERE mode=? AND event_id=? AND state='claimed' AND claim_token=? AND lease_until>?
    RETURNING event_id`).bind(mode,eventId,token,now).first();
  return row !== null;
}
export async function failEvent(db, mode, eventId, token) {
  const row = await db.prepare(`UPDATE stripe_event_inbox SET state='reconciliation_required',claim_token=NULL,lease_until=NULL
    WHERE mode=? AND event_id=? AND state='claimed' AND claim_token=? RETURNING event_id`)
    .bind(mode,eventId,token).first();
  return row !== null;
}

// Discovery is read-only and never grants claim ownership or business effects.
// Every candidate still requires claimEvent and provider/canonical reconciliation.
export async function listReconciliationCandidates(db, mode, now, limit) {
  if (!['test', 'live'].includes(mode) || !Number.isSafeInteger(now) || now < 0 ||
      !Number.isSafeInteger(limit) || limit < 1 || limit > 100) {
    throw new TypeError('Invalid reconciliation discovery configuration');
  }
  const result = await db.prepare(`SELECT event_id,event_type,provider_created,accepted_at,state,attempts
    FROM stripe_event_inbox WHERE mode=? AND
      (state IN ('pending','reconciliation_required') OR (state='claimed' AND lease_until<=?))
    ORDER BY accepted_at,event_id LIMIT ?`).bind(mode, now, limit).all();
  if (!result || !Array.isArray(result.results)) throw new TypeError('Invalid reconciliation discovery result');
  return result.results;
}
