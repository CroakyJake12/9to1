import type { Env } from "./env";
import { json } from "./pages";

export type AgeBand = "13_15" | "16_17" | "18_plus";
export type PolicyVersions = { termsVersion: string; privacyVersion: string };
const callbacks = new Set(["https://cakemods.com/oauth/callback", "https://9to1.uk/oauth/callback"]);
export function policyVersions(env: Env): PolicyVersions | null {
  const termsVersion = env.PUBLIC_TERMS_VERSION, privacyVersion = env.PUBLIC_PRIVACY_VERSION;
  return typeof termsVersion === "string" && /^[A-Za-z0-9._-]{1,128}$/.test(termsVersion) &&
    typeof privacyVersion === "string" && /^[A-Za-z0-9._-]{1,128}$/.test(privacyVersion) ? { termsVersion, privacyVersion } : null;
}
export function validAgeBand(value: unknown): value is AgeBand { return value === "13_15" || value === "16_17" || value === "18_plus"; }
export function checkPolicyAcknowledgement(env: Env, body: Record<string, unknown> | undefined): string | null {
  const policy = policyVersions(env);
  if (!policy) return "account_policy_unavailable";
  if (body?.ageBand === "under_13") return "minimum_age_required";
  if (!validAgeBand(body?.ageBand) || body?.acceptTerms !== true || body?.termsVersion !== policy.termsVersion || body?.privacyVersion !== policy.privacyVersion)
    return "account_policy_acknowledgement_required";
  return null;
}
export async function classifyAccountClient(env: Env, clientId: unknown): Promise<"production" | "legacy" | "invalid"> {
  if (typeof clientId !== "string" || !clientId || clientId.length > 256) return "invalid";
  const client = await env.DB.prepare("SELECT redirectUris, disabled FROM oauthClient WHERE clientId = ? LIMIT 1")
    .bind(clientId).first<{ redirectUris: string; disabled: number | null }>();
  if (!client || client.disabled) return "invalid";
  try {
    const uris: unknown = JSON.parse(client.redirectUris);
    if (!Array.isArray(uris) || uris.some(uri => typeof uri !== "string")) return "invalid";
    return uris.some(uri => callbacks.has(uri)) ? "production" : "legacy";
  } catch { return "invalid"; }
}
export async function accountPolicyCurrent(env: Env, accountId: string): Promise<boolean> {
  const policy = policyVersions(env); if (!policy) return false;
  const row = await env.DB.prepare("SELECT p.ageBand, p.termsVersion, p.privacyVersion FROM cake_public_account_policy p JOIN user u ON u.id = p.accountId WHERE p.accountId = ? AND u.emailVerified = 1")
    .bind(accountId).first<{ ageBand: string; termsVersion: string; privacyVersion: string }>();
  return !!row && validAgeBand(row.ageBand) && row.termsVersion === policy.termsVersion && row.privacyVersion === policy.privacyVersion;
}
export async function recordSignupPolicy(env: Env, accountId: string, body: Record<string, unknown>): Promise<void> {
  if (checkPolicyAcknowledgement(env, body)) throw new Error("Account policy acknowledgement unavailable");
  const policy = policyVersions(env)!; const now = Date.now();
  await env.DB.prepare("INSERT INTO cake_public_account_policy (accountId, ageBand, termsVersion, privacyVersion, acceptedAt, createdAt) VALUES (?, ?, ?, ?, ?, ?)")
    .bind(accountId, body.ageBand, policy.termsVersion, policy.privacyVersion, now, now).run();
}
export async function enforceAccountPolicy(env: Env, accountId: string, clientId: unknown): Promise<Response | null> {
  try {
    const kind = await classifyAccountClient(env, clientId);
    if (kind === "invalid") return json({ error: "unauthorized" }, 401);
    if (kind === "legacy") return null;
    if (!policyVersions(env)) return json({ error: "account_policy_unavailable" }, 503);
    if (!await accountPolicyCurrent(env, accountId)) return json({ error: "account_setup_required", onboardingPath: "/account/onboarding" }, 403);
    return null;
  } catch { return json({ error: "account_policy_unavailable" }, 503); }
}
export async function acceptAccountPolicy(request: Request, env: Env, accountId: string, sessionId: string): Promise<Response> {
  if (!policyVersions(env)) return json({ error: "account_policy_unavailable" }, 503);
  if (request.headers.get("origin") !== new URL(env.AUTH_BASE_URL).origin ||
      request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json")
    return json({ error: "origin_not_allowed" }, 403);
  if (!request.body) return json({ error: "invalid_request" }, 400);
  const reader = request.body.getReader(); const chunks: Uint8Array[] = []; let size = 0; let body: Record<string, unknown>;
  try {
    while (true) { const { done, value } = await reader.read(); if (done) break; size += value.byteLength;
      if (size > 4096) { await reader.cancel(); return json({ error: "invalid_request" }, 400); } chunks.push(value); }
    const bytes = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)) as Record<string, unknown>;
    if (!body || typeof body !== "object" || Array.isArray(body)) return json({ error: "invalid_request" }, 400);
  } catch { return json({ error: "invalid_request" }, 400); }
  finally { reader.releaseLock(); }
  const error = checkPolicyAcknowledgement(env, body);
  if (error) return json({ error }, error === "minimum_age_required" ? 403 : 400);
  const policy = policyVersions(env)!; const now = Date.now();
  const row = await env.DB.prepare(`INSERT INTO cake_public_account_policy (accountId, ageBand, termsVersion, privacyVersion, acceptedAt, createdAt)
    SELECT ?, ?, ?, ?, ?, ? WHERE EXISTS (SELECT 1 FROM user u JOIN session s ON s.userId = u.id WHERE u.id = ? AND u.emailVerified = 1 AND s.id = ? AND julianday(s.expiresAt) > julianday('now'))
    ON CONFLICT(accountId) DO UPDATE SET termsVersion = excluded.termsVersion, privacyVersion = excluded.privacyVersion, acceptedAt = excluded.acceptedAt
    WHERE cake_public_account_policy.ageBand = excluded.ageBand RETURNING accountId`)
    .bind(accountId, body.ageBand, policy.termsVersion, policy.privacyVersion, now, now, accountId, sessionId).first();
  if (!row) return json({ error: "policy_not_saved", message: "Your session or age record needs review. Age groups cannot be changed here." }, 409);
  return json({ ready: true, aiAccess: "unavailable" });
}
