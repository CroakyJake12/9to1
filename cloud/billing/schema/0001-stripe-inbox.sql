-- Receipt authority only: this table cannot grant entitlements or credit balances.
CREATE TABLE IF NOT EXISTS stripe_event_inbox (
  mode TEXT NOT NULL CHECK(mode IN ('test','live')),
  event_id TEXT NOT NULL,
  event_type TEXT NOT NULL,
  provider_created INTEGER NOT NULL,
  payload_sha256 TEXT NOT NULL,
  accepted_at INTEGER NOT NULL,
  state TEXT NOT NULL DEFAULT 'pending' CHECK(state IN ('pending','claimed','processed','reconciliation_required')),
  claim_token TEXT,
  lease_until INTEGER,
  attempts INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY(mode, event_id)
);
