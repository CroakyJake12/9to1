-- Shared CAKE ID guardrails used by the local test entrypoint and future D1 environments.
CREATE TABLE IF NOT EXISTS cake_reserved_usernames (
  username TEXT PRIMARY KEY COLLATE NOCASE,
  ownerUserId TEXT NULL,
  reason TEXT NOT NULL,
  createdAt INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS cake_login_attempts (
  keyHash TEXT PRIMARY KEY,
  windowStartedAt INTEGER NOT NULL,
  attempts INTEGER NOT NULL,
  lockedUntil INTEGER NOT NULL,
  updatedAt INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS cake_email_outbox (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  recipient TEXT NOT NULL,
  subject TEXT NOT NULL,
  body TEXT NOT NULL,
  createdAt INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS cake_email_outbox_recipient_created
  ON cake_email_outbox(recipient, createdAt DESC);

INSERT OR IGNORE INTO cake_reserved_usernames (username, ownerUserId, reason, createdAt)
VALUES ('croakyjake', NULL, 'Reserved identity; bind only to a separately verified canonical AccountID', 0);
