-- Existing users and credentials remain generation zero until a real reset.
ALTER TABLE user ADD COLUMN credentialGeneration INTEGER NOT NULL DEFAULT 0;
