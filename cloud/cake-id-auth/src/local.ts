import { getMigrations } from "better-auth/db/migration";
import worker, { fetchRequest } from "./index";
import { createAuthOptions } from "./auth";
import type { Env } from "./env";
import { json } from "./pages";

let initialization: Promise<void> | undefined;
let generatedSql = "";

async function initialize(env: Env): Promise<void> {
  console.log("[local-test] creating local-only D1 tables");
  const probe = await env.DB.prepare("SELECT 1 AS ok").first<{ ok: number }>();
  if (probe?.ok !== 1) throw new Error("D1 local health query failed");
  console.log("[local-test] D1 health query succeeded");
  const localStatements = [
    `CREATE TABLE IF NOT EXISTS cake_reserved_usernames (
      username TEXT PRIMARY KEY COLLATE NOCASE,
      ownerUserId TEXT NULL,
      reason TEXT NOT NULL,
      createdAt INTEGER NOT NULL
    )`,
    `CREATE TABLE IF NOT EXISTS cake_login_attempts (
      keyHash TEXT PRIMARY KEY,
      windowStartedAt INTEGER NOT NULL,
      attempts INTEGER NOT NULL,
      lockedUntil INTEGER NOT NULL,
      updatedAt INTEGER NOT NULL
    )`,
    `CREATE TABLE IF NOT EXISTS cake_email_outbox (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      recipient TEXT NOT NULL,
      subject TEXT NOT NULL,
      body TEXT NOT NULL,
      createdAt INTEGER NOT NULL
    )`,
    "CREATE INDEX IF NOT EXISTS cake_email_outbox_recipient_created ON cake_email_outbox(recipient, createdAt DESC)",
    "INSERT OR IGNORE INTO cake_reserved_usernames (username, ownerUserId, reason, createdAt) VALUES ('croakyjake', NULL, 'Reserved identity; bind only to a separately verified canonical AccountID', 0)",
  ];
  for (let index = 0; index < localStatements.length; index++) {
    console.log(`[local-test] applying local D1 statement ${index + 1}/${localStatements.length}`);
    await env.DB.prepare(localStatements[index]).run();
  }
  console.log("[local-test] custom D1 tables ready");

  const migration = await getMigrations(createAuthOptions(env));
  if (migration.unsafeChanges.length || migration.schemaProblems.length) {
    throw new Error("Local auth schema does not match the pinned Better Auth version");
  }
  generatedSql = await migration.compileMigrations();
  console.log("[local-test] Better Auth D1 migration plan compiled");
  await migration.runMigrations();
  console.log("[local-test] Better Auth D1 schema ready");
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
    const sql = generatedSql;
    return sql ? new Response(sql, { headers: { "content-type": "text/plain; charset=utf-8", "cache-control": "no-store" } }) : json({ error: "schema_unavailable" }, 503);
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
