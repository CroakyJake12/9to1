import { oauthProviderResourceClient } from "@better-auth/oauth-provider/resource-client";
import type { createAuth } from "./auth";
import { AUTH_BASE_PATH, type CakeScope } from "./contract";
import type { Env } from "./env";
import { json } from "./pages";

type Auth = ReturnType<typeof createAuth>;
type Claims = Record<string, unknown> & { sub?: string; sid?: string; scope?: string };
interface Principal { accountId: string; sessionId: string; bearer: boolean }
interface ProfileRow { id: string; name: string; username: string | null; image: string | null; pronouns: string | null; jobTitle: string | null; profileRevision: number | null }

function isUuid(value: string | undefined): value is string {
  return typeof value === "string" && value === value.toLowerCase() && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
}

async function authorize(request: Request, env: Env, auth: Auth, scope: CakeScope): Promise<Principal | Response> {
  const authorization = request.headers.get("authorization");
  if (authorization !== null) {
    const match = authorization.match(/^Bearer\s+(\S+)$/i);
    if (!match) return json({ error: "unauthorized" }, 401);

    let claims: Claims;
    try {
      claims = await oauthProviderResourceClient(auth).getActions().verifyAccessTokenRequest(request, {
        verifyOptions: { audience: env.API_RESOURCE, issuer: `${env.AUTH_BASE_URL.replace(/\/$/, "")}${AUTH_BASE_PATH}` },
        requiredScopes: [scope],
      }) as Claims;
    } catch {
      return json({ error: "unauthorized" }, 401);
    }

    if (!isUuid(claims.sub) || !isUuid(claims.sid)) return json({ error: "unauthorized" }, 401);
    const active = await env.DB.prepare(
      "SELECT 1 FROM session WHERE id = ? AND userId = ? AND expiresAt > ? LIMIT 1",
    ).bind(claims.sid, claims.sub, new Date().toISOString()).first();
    if (!active) return json({ error: "session_revoked_or_expired" }, 401);
    return { accountId: claims.sub, sessionId: claims.sid, bearer: true };
  }

  const session = await auth.api.getSession({ headers: request.headers });
  if (!session) return json({ error: "unauthorized" }, 401);
  return { accountId: session.user.id, sessionId: session.session.id, bearer: false };
}

function mutationOriginAllowed(request: Request, env: Env, principal: Principal): boolean {
  return principal.bearer || request.headers.get("origin") === new URL(env.AUTH_BASE_URL).origin;
}

async function readBody(request: Request): Promise<Record<string, unknown> | null> {
  const maxBytes = 65_536;
  const contentLength = request.headers.get("content-length");
  if (contentLength && (!/^\d+$/.test(contentLength) || Number(contentLength) > maxBytes)) return null;
  if (!request.body) return null;

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > maxBytes) {
        await reader.cancel();
        return null;
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }

  try {
    const bytes = new Uint8Array(size);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    const parsed: unknown = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed as Record<string, unknown> : null;
  } catch {
    return null;
  }
}

async function accountCurrent(env: Env, accountId: string): Promise<Response> {
  const row = await env.DB.prepare("SELECT id, name FROM user WHERE id = ? LIMIT 1")
    .bind(accountId)
    .first<{ id: string; name: string }>();
  return row ? json({ accountId: row.id, displayName: row.name }) : json({ error: "account_not_found" }, 404);
}

async function readProfile(env: Env, accountId: string): Promise<Response> {
  const row = await env.DB.prepare(
    "SELECT id, name, username, image, pronouns, jobTitle, profileRevision FROM user WHERE id = ? LIMIT 1",
  ).bind(accountId).first<ProfileRow>();
  if (!row) return json({ error: "account_not_found" }, 404);
  return json({ profile: {
    accountId: row.id,
    name: row.name,
    username: row.username ?? "",
    icon: row.image,
    pronouns: row.pronouns,
    job: row.jobTitle,
    revision: Number(row.profileRevision ?? 1),
  } });
}

type ProfileField = "name" | "username" | "image" | "pronouns" | "jobTitle";
const profileFieldMap: Readonly<Record<string, ProfileField>> = {
  name: "name",
  username: "username",
  icon: "image",
  pronouns: "pronouns",
  job: "jobTitle",
};

async function updateProfile(request: Request, env: Env, accountId: string): Promise<Response> {
  const body = await readBody(request);
  if (!body || !Number.isSafeInteger(body.expectedRevision) || Number(body.expectedRevision) < 1
      || !body.fields || typeof body.fields !== "object" || Array.isArray(body.fields)) {
    return json({ error: "invalid_request" }, 400);
  }

  const expectedRevision = Number(body.expectedRevision);
  const input = body.fields as Record<string, unknown>;
  const entries = Object.entries(input);
  if (!entries.length) return json({ error: "empty_profile_update" }, 400);
  const patch: Partial<Record<ProfileField, string | null>> = {};

  for (const [apiField, value] of entries) {
    const column = Object.hasOwn(profileFieldMap, apiField) ? profileFieldMap[apiField] : undefined;
    if (!column) return json({ error: "invalid_profile" }, 400);
    if (value === null && column !== "name" && column !== "username") {
      patch[column] = null;
      continue;
    }
    if (typeof value !== "string") return json({ error: "invalid_profile" }, 400);
    const normalized = column === "username" ? value.trim().toLowerCase() : value.trim();
    const maximum = column === "image" ? 2048 : column === "pronouns" ? 60 : column === "jobTitle" ? 1000 : column === "username" ? 30 : 100;
    if (!normalized || normalized.length > maximum) return json({ error: "invalid_profile" }, 400);
    if (column === "username" && !/^[a-z0-9_]{3,30}$/.test(normalized)) return json({ error: "invalid_profile" }, 400);
    if (column === "image") {
      let icon: URL;
      try { icon = new URL(normalized); } catch { return json({ error: "invalid_profile" }, 400); }
      if (icon.protocol !== "https:" || icon.username || icon.password) return json({ error: "invalid_profile" }, 400);
    }
    patch[column] = normalized;
  }

  if (patch.username !== undefined) {
    const reserved = await env.DB.prepare("SELECT ownerUserId FROM cake_reserved_usernames WHERE username = ? COLLATE NOCASE LIMIT 1")
      .bind(patch.username)
      .first<{ ownerUserId: string | null }>();
    if (reserved && reserved.ownerUserId !== accountId) return json({ error: "username_unavailable" }, 409);
    const duplicate = await env.DB.prepare("SELECT 1 FROM user WHERE username = ? COLLATE NOCASE AND id <> ? LIMIT 1")
      .bind(patch.username, accountId)
      .first();
    if (duplicate) return json({ error: "username_unavailable" }, 409);
  }

  const assignments = Object.keys(patch).map((field) => `"${field}" = ?`).join(", ");
  let updated: ProfileRow | null;
  try {
    updated = await env.DB.prepare(
      `UPDATE "user" SET ${assignments}, "profileRevision" = COALESCE("profileRevision", 1) + 1, "updatedAt" = ? WHERE "id" = ? AND COALESCE("profileRevision", 1) = ? RETURNING "id", "name", "username", "image", "pronouns", "jobTitle", "profileRevision"`,
    ).bind(...Object.values(patch), new Date().toISOString(), accountId, expectedRevision).first<ProfileRow>();
  } catch (error) {
    if (patch.username !== undefined && /unique constraint/i.test(String(error))) return json({ error: "username_unavailable" }, 409);
    throw error;
  }
  if (updated) return json({ profile: {
    accountId: updated.id,
    name: updated.name,
    username: updated.username ?? "",
    icon: updated.image,
    pronouns: updated.pronouns,
    job: updated.jobTitle,
    revision: Number(updated.profileRevision ?? 1),
  } });

  const current = await env.DB.prepare("SELECT profileRevision FROM user WHERE id = ? LIMIT 1")
    .bind(accountId)
    .first<{ profileRevision: number | null }>();
  return current ? json({ error: "profile_conflict", revision: Number(current.profileRevision ?? 1) }, 409) : json({ error: "account_not_found" }, 404);
}

function deviceName(userAgent: string | null): string {
  if (!userAgent) return "Unknown device";
  const clean = userAgent.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, 120);
  return clean || "Unknown device";
}

async function listSessions(env: Env, accountId: string): Promise<Response> {
  const result = await env.DB.prepare(
    "SELECT id, userId, createdAt, expiresAt, userAgent FROM session WHERE userId = ? ORDER BY createdAt DESC",
  ).bind(accountId).all<{ id: string; userId: string; createdAt: string; expiresAt: string; userAgent: string | null }>();
  return json({ sessions: (result.results ?? []).map((row) => ({
    sessionId: row.id,
    accountId: row.userId,
    deviceName: deviceName(row.userAgent),
    createdAt: row.createdAt,
    expiresAt: row.expiresAt,
    revokedAt: null,
    registeredClientId: null,
  })) });
}

async function revokeOne(env: Env, accountId: string, sessionId: string): Promise<Response> {
  const existing = await env.DB.prepare("SELECT 1 FROM session WHERE id = ? AND userId = ? LIMIT 1")
    .bind(sessionId, accountId)
    .first();
  if (!existing) return json({ error: "session_not_found" }, 404);
  await env.DB.batch([
    env.DB.prepare("DELETE FROM oauthAccessToken WHERE sessionId = ?").bind(sessionId),
    env.DB.prepare("DELETE FROM oauthRefreshToken WHERE sessionId = ?").bind(sessionId),
    env.DB.prepare("DELETE FROM session WHERE id = ? AND userId = ?").bind(sessionId, accountId),
  ]);
  return new Response(null, { status: 204, headers: { "cache-control": "no-store" } });
}

async function revokeOthers(env: Env, accountId: string, currentSessionId: string): Promise<Response> {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM oauthAccessToken WHERE sessionId IN (SELECT id FROM session WHERE userId = ? AND id <> ?)").bind(accountId, currentSessionId),
    env.DB.prepare("DELETE FROM oauthRefreshToken WHERE sessionId IN (SELECT id FROM session WHERE userId = ? AND id <> ?)").bind(accountId, currentSessionId),
    env.DB.prepare("DELETE FROM session WHERE userId = ? AND id <> ?").bind(accountId, currentSessionId),
  ]);
  return new Response(null, { status: 204, headers: { "cache-control": "no-store" } });
}

async function handle(request: Request, env: Env, auth: Auth): Promise<Response> {
  const url = new URL(request.url);
  const currentRoute = request.method === "GET" && url.pathname === "/api/account/current";
  const profileRead = request.method === "GET" && url.pathname === "/api/account/profile";
  const profileWrite = request.method === "PATCH" && url.pathname === "/api/account/profile";
  const sessionsRead = request.method === "GET" && url.pathname === "/api/account/sessions";
  const signOut = request.method === "POST" && url.pathname === "/api/account/signout";
  const revokeOthersRoute = request.method === "POST" && url.pathname === "/api/account/revoke-other-sessions";
  const revokeMatch = request.method === "DELETE" && url.pathname.match(/^\/api\/account\/sessions\/([0-9a-f-]{36})$/i);

  const scope: CakeScope | null = currentRoute ? "cake:account:read"
    : profileRead ? "cake:profile:read"
    : profileWrite ? "cake:profile:write"
    : sessionsRead ? "cake:sessions:read"
    : signOut || revokeOthersRoute || revokeMatch ? "cake:sessions:revoke"
    : null;
  if (!scope) return json({ error: "not_found" }, 404);

  const principal = await authorize(request, env, auth, scope);
  if (principal instanceof Response) return principal;
  if ((profileWrite || signOut || revokeOthersRoute || revokeMatch) && !mutationOriginAllowed(request, env, principal)) {
    return json({ error: "origin_not_allowed" }, 403);
  }

  if (currentRoute) return accountCurrent(env, principal.accountId);
  if (profileRead) return readProfile(env, principal.accountId);
  if (profileWrite) return updateProfile(request, env, principal.accountId);
  if (sessionsRead) return listSessions(env, principal.accountId);
  if (signOut) return revokeOne(env, principal.accountId, principal.sessionId);
  if (revokeOthersRoute) return revokeOthers(env, principal.accountId, principal.sessionId);
  if (revokeMatch) {
    const sessionId = revokeMatch[1].toLowerCase();
    if (!isUuid(sessionId)) return json({ error: "invalid_session_id" }, 400);
    return revokeOne(env, principal.accountId, sessionId);
  }
  return json({ error: "not_found" }, 404);
}

export async function handleResourceApi(request: Request, env: Env, auth: Auth): Promise<Response> {
  try {
    return await handle(request, env, auth);
  } catch {
    return json({ error: "service_unavailable" }, 503);
  }
}
