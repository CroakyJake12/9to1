import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const database = new DatabaseSync(":memory:");

try {
  for (const migration of ["0000_cake_guardrails.sql", "0001_better_auth.sql", "0002_account_theme_preference.sql", "0003_turnstile_redemptions.sql", "0004_account_invitations.sql", "0005_public_account_policy.sql", "0006_password_reset_fence.sql"]) {
    database.exec(readFileSync(path.join(root, "migrations", migration), "utf8"));
  }

  const tables = new Set(database.prepare("SELECT name FROM sqlite_master WHERE type = 'table'").all().map((row) => row.name));
  for (const table of ["user", "session", "account", "verification", "jwks", "oauthClient", "oauthResource", "oauthAccessToken", "oauthConsent", "cake_reserved_usernames", "cake_login_attempts", "cake_email_outbox", "accountThemePreference", "cake_turnstile_redemptions", "cake_account_invitations", "cake_public_account_policy"]) {
    assert.ok(tables.has(table), `migration created ${table}`);
  }

  const reserved = database.prepare("SELECT ownerUserId FROM cake_reserved_usernames WHERE username = ? COLLATE NOCASE").get("CROAKYJAKE");
  assert.ok(reserved, "reserved username is case-insensitive");
  assert.equal(reserved.ownerUserId, null, "reserved identity is not bound to an invented account");
  const generation = database.prepare("PRAGMA table_info(user)").all().find(column => column.name === "credentialGeneration");
  assert.ok(generation, "password reset generation column exists");
  assert.equal(generation.type, "INTEGER");
  assert.equal(generation.notnull, 1, "password reset generation cannot be null");
  assert.equal(generation.dflt_value, "0", "existing/new rows default to credential generation zero");
  console.log("All versioned SQL migrations execute in Node's in-memory SQLite and create the required auth, OAuth, and guardrail tables.");
} finally {
  database.close();
}
