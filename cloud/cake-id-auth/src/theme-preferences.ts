import type { Env } from "./env";
import { json } from "./pages";

export interface ThemePrincipal { accountId: string; sessionId: string; accessExpiresAt?: number }
type ObjectValue = Record<string, unknown>;
// Shared wire validator is used by the browser cache and this authenticated API.
import { validThemePreference } from "../../../apps/Web/Auth/theme-preference-contract.js";

// Each read and commit is joined to the live original session inside its SQL
// statement. A preliminary JWT check alone cannot authorize a delayed write.
const activeSession = "EXISTS (SELECT 1 FROM session WHERE id = ? AND userId = ? AND julianday(expiresAt) > julianday('now')) AND (? IS NULL OR ? > unixepoch('now'))";

export async function readThemePreference(env: Env, principal: ThemePrincipal): Promise<Response> {
  const row = await env.DB.prepare(`SELECT p.preferenceJson FROM user u LEFT JOIN accountThemePreference p ON p.accountId = u.id WHERE u.id = ? AND ${activeSession}`)
    .bind(principal.accountId, principal.sessionId, principal.accountId, principal.accessExpiresAt ?? null, principal.accessExpiresAt ?? null).first<{ preferenceJson: string | null }>();
  if (!row) return json({ error: "session_revoked_or_expired" }, 401);
  if (row.preferenceJson === null) return json({ preference: null });
  const preference: unknown = JSON.parse(row.preferenceJson);
  if (!validThemePreference(preference, principal.accountId)) return json({ error: "theme_schema_unsupported" }, 409);
  return json({ preference });
}

export async function writeThemePreference(env: Env, principal: ThemePrincipal, body: ObjectValue | null): Promise<Response> {
  const expected = body?.expectedRevision;
  const candidate = body?.preference as ObjectValue | undefined;
  if (!Number.isSafeInteger(expected) || Number(expected) < 0 || Number(expected) >= Number.MAX_SAFE_INTEGER ||
      !validThemePreference(candidate, principal.accountId) || candidate?.revision !== Number(expected) + 1)
    return json({ error: "invalid_theme_preference" }, 400);
  const existing = await env.DB.prepare("SELECT preferenceJson FROM accountThemePreference WHERE accountId = ?")
    .bind(principal.accountId).first<{ preferenceJson: string }>();
  let preserved: ObjectValue = {};
  if (existing) {
    const previous: unknown = JSON.parse(existing.preferenceJson);
    if (!validThemePreference(previous, principal.accountId)) return json({ error: "theme_schema_unsupported" }, 409);
    preserved = previous as ObjectValue;
    if(preserved.revision !== expected) return json({ error: "theme_revision_conflict", revision: preserved.revision },409);
  }
  if(!existing && expected !== 0) return json({ error: "theme_revision_conflict" },409);
  const merged = { ...preserved, ...candidate, accessibility: { ...(preserved.accessibility as ObjectValue ?? {}), ...(candidate.accessibility as ObjectValue) } };
  const serialized = JSON.stringify(merged);
  if (new TextEncoder().encode(serialized).byteLength > 2_200_000) return json({ error: "theme_preference_too_large" }, 400);
  // INSERT ... SELECT ... ON CONFLICT is one atomic D1 statement. Missing rows
  // have revision zero; existing rows must match exactly. Preserve future fields
  // from the existing envelope/accessibility when older clients update settings.
  const row = await env.DB.prepare(`INSERT INTO accountThemePreference(accountId, revision, preferenceJson, updatedAt)
    SELECT ?, ?, ?, strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE ${activeSession}
      AND (? = 0 OR EXISTS (SELECT 1 FROM accountThemePreference WHERE accountId = ? AND revision = ?))
    ON CONFLICT(accountId) DO UPDATE SET revision = excluded.revision,
      preferenceJson = excluded.preferenceJson, updatedAt = excluded.updatedAt
    WHERE accountThemePreference.revision = ?
    RETURNING preferenceJson`)
    .bind(principal.accountId, candidate.revision, serialized, principal.sessionId, principal.accountId, principal.accessExpiresAt ?? null, principal.accessExpiresAt ?? null,
      expected, principal.accountId, expected, expected).first<{ preferenceJson: string }>();
  if (row) return json({ preference: JSON.parse(row.preferenceJson) });
  const current = await env.DB.prepare(`SELECT p.revision FROM user u LEFT JOIN accountThemePreference p ON p.accountId = u.id WHERE u.id = ? AND ${activeSession}`)
    .bind(principal.accountId, principal.sessionId, principal.accountId, principal.accessExpiresAt ?? null, principal.accessExpiresAt ?? null).first<{ revision: number | null }>();
  if (!current) return json({ error: "session_revoked_or_expired" }, 401);
  return json({ error: "theme_revision_conflict", ...(current.revision === null ? {} : { revision: current.revision }) }, 409);
}
