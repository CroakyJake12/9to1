import { getMigrations } from "better-auth/db/migration";
import worker, { fetchRequest } from "./index";
import { createAuthOptions } from "./auth";
import type { Env } from "./env";
import { json } from "./pages";
import GUARDRAILS_MIGRATION from "../migrations/0000_cake_guardrails.sql";
import AUTH_MIGRATION from "../migrations/0001_better_auth.sql";
import THEME_MIGRATION from "../migrations/0002_account_theme_preference.sql";
import TURNSTILE_MIGRATION from "../migrations/0003_turnstile_redemptions.sql";
import INVITATIONS_MIGRATION from "../migrations/0004_account_invitations.sql";
import POLICY_MIGRATION from "../migrations/0005_public_account_policy.sql";
import RESET_FENCE_MIGRATION from "../migrations/0006_password_reset_fence.sql";

let initialization: Promise<void> | undefined;
let initializedSql = "";
let initializedInstanceId = "";
const localMigrations = [
  ["0000_cake_guardrails.sql", GUARDRAILS_MIGRATION],
  ["0001_better_auth.sql", AUTH_MIGRATION],
  ["0002_account_theme_preference.sql", THEME_MIGRATION],
  ["0003_turnstile_redemptions.sql", TURNSTILE_MIGRATION],
  ["0004_account_invitations.sql", INVITATIONS_MIGRATION],
  ["0005_public_account_policy.sql", POLICY_MIGRATION],
  ["0006_password_reset_fence.sql", RESET_FENCE_MIGRATION],
] as const;

async function initialize(env: Env): Promise<void> {
  console.log("[local-test] creating local-only D1 tables");
  const probe = await env.DB.prepare("SELECT 1 AS ok").first<{ ok: number }>();
  if (probe?.ok !== 1) throw new Error("D1 local health query failed");
  console.log("[local-test] D1 health query succeeded");
  await env.DB.prepare("CREATE TABLE IF NOT EXISTS cake_local_test_migrations (name TEXT PRIMARY KEY NOT NULL)").run();
  for (const [name, sql] of localMigrations) {
    const applied = await env.DB.prepare("SELECT name FROM cake_local_test_migrations WHERE name = ?").bind(name).first();
    if (applied) continue;
    // These pinned files end each statement at a line boundary; semicolons
    // inside their single-line string literals must remain part of the SQL.
    const statements = sql.replace(/^[ \t]*--[^\r\n]*$/gm, "")
      .split(/;[ \t]*(?:\r?\n|$)/).map(statement => statement.trim()).filter(Boolean);
    if (!statements.length) throw new Error(`Empty local migration: ${name}`);
    const results = await env.DB.batch([
      ...statements.map(statement => env.DB.prepare(statement)),
      env.DB.prepare("INSERT INTO cake_local_test_migrations (name) VALUES (?)").bind(name),
    ]);
    if (results.some(result => !result.success)) throw new Error(`Local migration failed: ${name}`);
    console.log(`[local-test] applied canonical migration ${name}`);
  }

  // Inspect the maintained provider schema without silently changing the
  // canonical versioned schema used by production migrations.
  const migration = await getMigrations(createAuthOptions(env));
  if (migration.unsafeChanges.length || migration.schemaProblems.length) {
    throw new Error("Local auth schema does not match the pinned Better Auth version");
  }
  // The pinned generator emits a lone terminator even for an empty plan.
  if (!/^[\s;]*$/.test(await migration.compileMigrations())) {
    throw new Error("Canonical local migrations leave unapplied Better Auth schema changes");
  }
  initializedSql = localMigrations.map(([, sql]) => sql).join("\n\n");
  initializedInstanceId = crypto.randomUUID();
  console.log("[local-test] canonical D1 schema ready and provider schema checked");
}

function sameSecret(a: string | null, b: string | undefined): boolean {
  if (!a || !b || a.length !== b.length) return false;
  let difference = 0;
  for (let i = 0; i < a.length; i++) difference |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return difference === 0;
}

function isLoopback(request: Request): boolean {
  const hostname = new URL(request.url).hostname;
  return hostname === "127.0.0.1" || hostname === "localhost" || hostname === "[::1]";
}

async function testEndpoint(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  if (!url.pathname.startsWith("/__test/")) return null;
  if (env.APP_MODE !== "local-test-only" || !isLoopback(request)) return json({ error: "not_found" }, 404);
  if (!sameSecret(request.headers.get("x-local-test-key"), env.LOCAL_TEST_KEY)) return json({ error: "not_found" }, 404);

  if (request.method === "GET" && url.pathname === "/__test/health") return json({ ready: true, mode: "local-test-only" });
  if (request.method === "GET" && url.pathname === "/__test/schema") {
    const sql = initializedSql;
    return sql ? new Response(sql, { headers: { "content-type": "text/plain; charset=utf-8", "cache-control": "no-store" } }) : json({ error: "schema_unavailable" }, 503);
  }
  if (request.method === "GET" && url.pathname === "/__test/schema-state") {
    const migrations = await env.DB.prepare("SELECT name FROM cake_local_test_migrations ORDER BY name").all<{ name: string }>();
    const columns = await env.DB.prepare("PRAGMA table_info(user)").all<{ name: string; type: string; notnull: number; dflt_value: string | null }>();
    return json({ instanceId: initializedInstanceId, migrations: (migrations.results ?? []).map(row => row.name),
      credentialGeneration: columns.results?.find(column => column.name === "credentialGeneration") ?? null });
  }
  if (request.method === "GET" && url.pathname === "/__test/outbox") {
    const recipient = (url.searchParams.get("recipient") ?? "").toLowerCase();
    if (!/^[^@]+@example\.test$/.test(recipient)) return json({ error: "invalid_recipient" }, 400);
    const rows = await env.DB.prepare("SELECT subject, body, createdAt FROM cake_email_outbox WHERE recipient = ? ORDER BY id DESC LIMIT 10")
      .bind(recipient)
      .all<{ subject: string; body: string; createdAt: number }>();
    return json({ messages: rows.results ?? [] });
  }
  if (request.method === "POST" && url.pathname === "/__test/promote-admin") {
    const body = await request.json().catch(() => null) as { userId?: unknown; email?: unknown } | null;
    if (typeof body?.userId !== "string" || typeof body.email !== "string" || !body.email.endsWith("@example.test")) return json({ error: "invalid_test_user" }, 400);
    const result = await env.DB.prepare("UPDATE user SET role = 'admin' WHERE id = ? AND email = ? AND emailVerified = 1")
      .bind(body.userId, body.email.toLowerCase())
      .run();
    return json({ promoted: result.meta.changes === 1 });
  }
  if (request.method === "POST" && url.pathname === "/__test/reserve-synthetic-username") {
    const body = await request.json().catch(() => null) as { ownerUserId?: unknown; username?: unknown } | null;
    if (typeof body?.ownerUserId !== "string" || typeof body.username !== "string"
        || !/^fixture_[a-f0-9]{16}$/.test(body.username)) return json({ error: "invalid_test_reservation" }, 400);
    const owner = await env.DB.prepare("SELECT email FROM user WHERE id = ? AND emailVerified = 1 LIMIT 1")
      .bind(body.ownerUserId).first<{ email: string }>();
    if (!owner || !/^[^@]+@example\.test$/.test(owner.email)) return json({ error: "invalid_test_owner" }, 400);
    await env.DB.prepare("INSERT INTO cake_reserved_usernames (username, ownerUserId, reason, createdAt) VALUES (?, ?, ?, ?)")
      .bind(body.username, body.ownerUserId, "Isolated synthetic owner-binding fixture only", Date.now()).run();
    return json({ reserved: true });
  }
  return json({ error: "not_found" }, 404);
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    console.log(`[local-test] request ${new URL(request.url).pathname}`);
    if (env.APP_MODE !== "local-test-only") return json({ error: "local_test_mode_required" }, 503);
    initialization ??= initialize(env);
    try { await initialization; }
    catch (error) {
      console.error(`[local-test] schema initialization failed: ${error instanceof Error ? error.message : String(error)}`);
      initialization = undefined;
      return json({ error: "local_schema_initialization_failed", reason: error instanceof Error ? error.message : "unknown" }, 503);
    }

    const test = await testEndpoint(request, env);
    if (test) return test;
    return fetchRequest(request, env, ctx);
  },
};
