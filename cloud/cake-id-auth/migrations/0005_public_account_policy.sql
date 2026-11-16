CREATE TABLE cake_public_account_policy (
  accountId TEXT PRIMARY KEY NOT NULL REFERENCES user(id) ON DELETE CASCADE,
  ageBand TEXT NOT NULL CHECK(ageBand IN ('13_15', '16_17', '18_plus')),
  termsVersion TEXT NOT NULL,
  privacyVersion TEXT NOT NULL,
  acceptedAt INTEGER NOT NULL,
  createdAt INTEGER NOT NULL
);
