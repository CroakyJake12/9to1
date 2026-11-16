import { oauthProviderResourceClient } from "@better-auth/oauth-provider/resource-client";
import type { createAuth } from "./auth";
import { AUTH_BASE_PATH, type CakeScope } from "./contract";
import type { Env } from "./env";
import { json } from "./pages";
import { readThemePreference, writeThemePreference } from "./theme-preferences";
import { validThemePreference } from "../shared/theme-preference-contract.js";
import { enforceAccountPolicy } from "./public-policy";
import { decodeJwt } from "jose";

type Auth = ReturnType<typeof createAuth>;
type Claims = Record<string, unknown> & { sub?: string; sid?: string; scope?: string; exp?: number };
interface Principal { accountId: string; sessionId: string; bearer: boolean; accessExpiresAt?: number }
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
      // The provider verifier normalizes client_id from azp. After its signature
      // verification, reject conflicting original signed client identifiers.
      const original = decodeJwt(match[1]);
      if (original.client_id !== undefined && original.azp !== undefined && original.client_id !== original.azp)
        return json({ error: "unauthorized" }, 401);
    } catch {
      return json({ error: "unauthorized" }, 401);
    }

    if (!isUuid(claims.sub) || !isUuid(claims.sid) || !Number.isSafeInteger(claims.exp)) return json({ error: "unauthorized" }, 401);
    const active = await env.DB.prepare(
      "SELECT 1 FROM session WHERE id = ? AND userId = ? AND expiresAt > ? LIMIT 1",
    ).bind(claims.sid, claims.sub, new Date().toISOString()).first();
    if (!active) return json({ error: "session_revoked_or_expired" }, 401);
    const clientId = claims.client_id ?? claims.azp;
    if (claims.client_id !== undefined && claims.azp !== undefined && claims.client_id !== claims.azp)
      return json({ error: "unauthorized" }, 401);
    const policyDenial = await enforceAccountPolicy(env, claims.sub, clientId);
    if (policyDenial) return policyDenial;
    return { accountId: claims.sub, sessionId: claims.sid, bearer: true, accessExpiresAt: claims.exp };
  }

  const session = await auth.api.getSession({ headers: request.headers });
  if (!session) return json({ error: "unauthorized" }, 401);
  return { accountId: session.user.id, sessionId: session.session.id, bearer: false };
}

function mutationOriginAllowed(request: Request, env: Env, principal: Principal): boolean {
  return principal.bearer || request.headers.get("origin") === new URL(env.AUTH_BASE_URL).origin;
}

async function readBody(request: Request, maxBytes = 65_536): Promise<Record<string, unknown> | null> {
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

interface ExportRow extends ProfileRow {
  email: string;
  emailVerified: number;
  displayUsername: string | null;
  createdAt: string;
  updatedAt: string;
  preferenceJson: string | null;
  providersJson: string;
}

function exportTheme(preference: Record<string, unknown> | null): Record<string, unknown> | null {
  if (!preference) return null;
  const accessibility = preference.accessibility as Record<string, unknown>;
  const result: Record<string, unknown> = {
    schemaVersion: preference.schemaVersion,
    themeName: preference.themeName,
    appearance: preference.appearance,
    revision: preference.revision,
    accessibility: {
      reduceMotion: accessibility.reduceMotion,
      highContrast: accessibility.highContrast,
      displayScale: accessibility.displayScale,
      interfaceFontFamily: accessibility.interfaceFontFamily,
      codeFontFamily: accessibility.codeFontFamily,
    },
  };
  if (preference.accent) {
    const accent = preference.accent as Record<string, unknown>;
    result.accent = { mode: accent.mode, allAppsColour: accent.allAppsColour, individualApps: accent.individualApps };
  }
  if (typeof preference.customThemesJson === "string") result.customThemesJson = preference.customThemesJson;
  return result;
}

/** Narrow profile export, not a complete subject-access response. Session
 * metadata retains its separate cake:sessions:read permission. Never export
 * entire database rows or provider token fields, even after schema additions. */
async function exportAccount(env: Env, principal: Principal): Promise<Response> {
  if (!isUuid(principal.accountId) || !isUuid(principal.sessionId)) return json({ error: "unauthorized" }, 401);
  // Read one owner-scoped snapshot and check the original session inside that
  // statement, including for cookie callers and revocation during authorization.
  const row = await env.DB.prepare(`SELECT u.id, u.name, u.email, u.emailVerified, u.username,
    u.displayUsername, u.image, u.pronouns, u.jobTitle, u.profileRevision, u.createdAt, u.updatedAt,
    p.preferenceJson,
    (SELECT json_group_array(providerId) FROM
      (SELECT DISTINCT providerId FROM account WHERE userId = u.id ORDER BY providerId)) AS providersJson
    FROM user u LEFT JOIN accountThemePreference p ON p.accountId = u.id
    WHERE u.id = ? AND EXISTS (SELECT 1 FROM session WHERE id = ? AND userId = ?
      AND julianday(expiresAt) > julianday('now'))
      AND (? IS NULL OR ? > unixepoch('now')) LIMIT 1`)
    .bind(principal.accountId, principal.sessionId, principal.accountId,
      principal.accessExpiresAt ?? null, principal.accessExpiresAt ?? null).first<ExportRow>();
  if (!row) return json({ error: "session_revoked_or_expired" }, 401);
  const preference = row.preferenceJson === null ? null : JSON.parse(row.preferenceJson) as Record<string, unknown>;
  if (preference !== null && !validThemePreference(preference, principal.accountId))
    return json({ error: "theme_schema_unsupported" }, 409);
  const providers: unknown = JSON.parse(row.providersJson);
  if (!Array.isArray(providers) || providers.some(provider => typeof provider !== "string"))
    return json({ error: "service_unavailable" }, 503);
  const document = {
    schemaVersion: 1,
    exportedAt: new Date().toISOString(),
    account: {
      id: row.id, name: row.name, username: row.username,
      email: row.email, emailVerified: row.emailVerified === 1, createdAt: row.createdAt,
    },
    profile: {
      displayUsername: row.displayUsername, icon: row.image, pronouns: row.pronouns,
      job: row.jobTitle, revision: Number(row.profileRevision ?? 1), updatedAt: row.updatedAt,
    },
    themePreference: exportTheme(preference),
    linkedSignInProviders: providers,
  };
  const body = JSON.stringify(document, null, 2);
  if (new TextEncoder().encode(body).byteLength > 2_500_000)
    return json({ error: "export_requires_support" }, 409);
  return new Response(body, { headers: {
    "content-type": "application/json; charset=utf-8",
    "content-disposition": 'attachment; filename="9to1-account-export.json"',
    "cache-control": "private, no-store",
    "pragma": "no-cache",
    "x-content-type-options": "nosniff",
    "content-security-policy": "default-src 'none'; frame-ancestors 'none'",
    "referrer-policy": "no-referrer",
    "vary": "Cookie, Authorization",
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

async function updateProfile(request: Request, env: Env, principal: Principal): Promise<Response> {
  const accountId = principal.accountId;
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
  // Body streaming and validation can outlive authorization. Bind the exact
  // original session and evaluate both expiries at the SQLite commit boundary.
  const activeSession = "EXISTS (SELECT 1 FROM session WHERE id = ? AND userId = ? AND julianday(expiresAt) > julianday('now')) AND (? IS NULL OR ? > unixepoch('now'))";
  let updated: ProfileRow | null;
  try {
    updated = await env.DB.prepare(
      `UPDATE "user" SET ${assignments}, "profileRevision" = COALESCE("profileRevision", 1) + 1, "updatedAt" = ? WHERE "id" = ? AND COALESCE("profileRevision", 1) = ? AND ${activeSession} RETURNING "id", "name", "username", "image", "pronouns", "jobTitle", "profileRevision"`,
    ).bind(...Object.values(patch), new Date().toISOString(), accountId, expectedRevision,
      principal.sessionId, accountId, principal.accessExpiresAt ?? null, principal.accessExpiresAt ?? null).first<ProfileRow>();
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

  const current = await env.DB.prepare(`SELECT profileRevision FROM user WHERE id = ? AND ${activeSession} LIMIT 1`)
    .bind(accountId, principal.sessionId, accountId, principal.accessExpiresAt ?? null, principal.accessExpiresAt ?? null)
    .first<{ profileRevision: number | null }>();
  return current ? json({ error: "profile_conflict", revision: Number(current.profileRevision ?? 1) }, 409) : json({ error: "session_revoked_or_expired" }, 401);
}

function deviceName(userAgent: string | null): string {
  if (!userAgent) return "Unknown device";
  const clean = userAgent.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, 120);
  return clean || "Unknown device";
}

async function listSessions(env: Env, accountId: string): Promise<Response> {
  const result = await env.DB.prepare(
    "SELECT id, userId, createdAt, expiresAt, userAgent FROM session WHERE userId = ? AND julianday(expiresAt) > julianday('now') ORDER BY createdAt DESC",
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
  const themeRead = request.method === "GET" && url.pathname === "/api/account/theme";
  const themeWrite = request.method === "PATCH" && url.pathname === "/api/account/theme";
  const profileRead = request.method === "GET" && url.pathname === "/api/account/profile";
  const exportRead = request.method === "GET" && url.pathname === "/api/account/export";
  const profileWrite = request.method === "PATCH" && url.pathname === "/api/account/profile";
  const sessionsRead = request.method === "GET" && url.pathname === "/api/account/sessions";
  const signOut = request.method === "POST" && url.pathname === "/api/account/signout";
  const revokeOthersRoute = request.method === "POST" && url.pathname === "/api/account/revoke-other-sessions";
  const revokeMatch = request.method === "DELETE" && url.pathname.match(/^\/api\/account\/sessions\/([0-9a-f-]{36})$/i);

  const scope: CakeScope | null = currentRoute ? "cake:account:read"
    : profileRead || themeRead || exportRead ? "cake:profile:read"
    : profileWrite || themeWrite ? "cake:profile:write"
    : sessionsRead ? "cake:sessions:read"
    : signOut || revokeOthersRoute || revokeMatch ? "cake:sessions:revoke"
    : null;
  if (!scope) return json({ error: "not_found" }, 404);

  const principal = await authorize(request, env, auth, scope);
  if (principal instanceof Response) return principal;
  if ((profileWrite || themeWrite || signOut || revokeOthersRoute || revokeMatch) && !mutationOriginAllowed(request, env, principal)) {
    return json({ error: "origin_not_allowed" }, 403);
  }

  if (currentRoute) return accountCurrent(env, principal.accountId);
  if (themeRead) return readThemePreference(env, principal);
  if (themeWrite) return writeThemePreference(env, principal, await readBody(request, 2_250_000));
  if (profileRead) return readProfile(env, principal.accountId);
  if (exportRead) return exportAccount(env, principal);
  if (profileWrite) return updateProfile(request, env, principal);
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
