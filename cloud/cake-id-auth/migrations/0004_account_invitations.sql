CREATE TABLE cake_account_invitations (
  id TEXT PRIMARY KEY NOT NULL,
  tokenHash TEXT NOT NULL UNIQUE,
  email TEXT NOT NULL,
  createdBy TEXT REFERENCES user(id) ON DELETE SET NULL,
  createdAt INTEGER NOT NULL,
  expiresAt INTEGER NOT NULL,
  consumedAt INTEGER,
  revokedAt INTEGER
);
CREATE UNIQUE INDEX cake_account_invitations_active_email ON cake_account_invitations(email)
  WHERE consumedAt IS NULL AND revokedAt IS NULL;
CREATE INDEX cake_account_invitations_expiry ON cake_account_invitations(expiresAt);
