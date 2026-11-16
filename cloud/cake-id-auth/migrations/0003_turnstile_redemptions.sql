-- Short-lived one-use claims. Store only SHA-256 challenge digests, never tokens.
CREATE TABLE cake_turnstile_redemptions (
  tokenHash TEXT PRIMARY KEY NOT NULL,
  expiresAt INTEGER NOT NULL
);
CREATE INDEX cake_turnstile_redemptions_expiry ON cake_turnstile_redemptions(expiresAt);
