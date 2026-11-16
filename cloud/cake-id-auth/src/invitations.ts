import type { Env } from "./env";
import { json } from "./pages";

export function invitationsRequired(env: Env): boolean {
  try {
    const url = new URL(env.AUTH_BASE_URL);
    return !(env.APP_MODE === "local-test-only" && url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname));
  } catch { return true; }
}
function emailAddress(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const email = value.trim().toLowerCase();
  return email.length <= 320 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? email : null;
}
async function digest(code: string): Promise<string> {
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(code))), byte => byte.toString(16).padStart(2, "0")).join("");
}
export async function isInvitationAdministrator(env: Env, userId: string): Promise<boolean> {
  return await env.DB.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1").bind(userId).first() !== null;
}
export async function consumeInvitation(env: Env, emailValue: unknown, code: unknown): Promise<boolean> {
  const email = emailAddress(emailValue);
  if (!email || typeof code !== "string" || !/^[A-Za-z0-9_-]{43}$/.test(code)) return false;
  const tokenHash = await digest(code);
  // One atomic claim; no code is accepted from a URL or trusted from a client flag.
  const row = await env.DB.prepare("UPDATE cake_account_invitations SET consumedAt = ? WHERE tokenHash = ? AND email = ? AND consumedAt IS NULL AND revokedAt IS NULL AND expiresAt > ? RETURNING id")
    .bind(Date.now(), tokenHash, email, Date.now()).first<{ id: string }>();
  return row !== null;
}
export async function invitationUsable(env: Env, emailValue: unknown, code: unknown): Promise<boolean> {
  const email = emailAddress(emailValue);
  if (!email || typeof code !== "string" || !/^[A-Za-z0-9_-]{43}$/.test(code)) return false;
  return await env.DB.prepare("SELECT 1 FROM cake_account_invitations WHERE tokenHash = ? AND email = ? AND consumedAt IS NULL AND revokedAt IS NULL AND expiresAt > ?")
    .bind(await digest(code), email, Date.now()).first() !== null;
}
export async function createInvitation(env: Env, administratorId: string, emailValue: unknown, expiryHours: unknown = 72): Promise<Response> {
  if (!await isInvitationAdministrator(env, administratorId)) return json({ error: "forbidden" }, 403);
  const email = emailAddress(emailValue);
  if (!email || !Number.isInteger(expiryHours) || Number(expiryHours) < 1 || Number(expiryHours) > 168)
    return json({ error: "invalid_invitation" }, 400);
  if (await env.DB.prepare("SELECT 1 FROM user WHERE lower(email) = ? LIMIT 1").bind(email).first())
    return json({ error: "account_already_exists" }, 409);
  const code = btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(32))))
    .replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
  const tokenHash = await digest(code), id = crypto.randomUUID(), now = Date.now(), expiresAt = now + Number(expiryHours) * 3600_000;
  try {
    const rows = await env.DB.batch([
      env.DB.prepare("UPDATE cake_account_invitations SET revokedAt = ? WHERE email = ? AND expiresAt <= ? AND consumedAt IS NULL AND revokedAt IS NULL AND EXISTS (SELECT 1 FROM user WHERE id = ? AND role = 'admin')").bind(now, email, now, administratorId),
      env.DB.prepare("INSERT INTO cake_account_invitations (id, tokenHash, email, createdBy, createdAt, expiresAt) SELECT ?, ?, ?, ?, ?, ? WHERE EXISTS (SELECT 1 FROM user WHERE id = ? AND role = 'admin') RETURNING id")
        .bind(id, tokenHash, email, administratorId, now, expiresAt, administratorId),
    ]);
    if (!rows.every(row => row.success) || rows[1].results.length !== 1) return json({ error: "forbidden" }, 403);
    const response = json({ invitation: { id, email, expiresAt }, code }, 201);
    response.headers.set("referrer-policy", "no-referrer");
    return response;
  } catch (error) {
    return /unique constraint/i.test(String(error)) ? json({ error: "invitation_not_created" }, 409)
      : json({ error: "invitation_service_unavailable" }, 503);
  }
}
export async function invitationRequest(request: Request, env: Env, administratorId: string): Promise<Response> {
  if (request.method !== "POST") return json({ error: "method_not_allowed" }, 405);
  if (request.headers.get("origin") !== new URL(env.AUTH_BASE_URL).origin ||
      request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json")
    return json({ error: "origin_not_allowed" }, 403);
  if (!await isInvitationAdministrator(env, administratorId)) return json({ error: "forbidden" }, 403);
  if (!request.body) return json({ error: "invalid_request" }, 400);
  const reader = request.body.getReader(); const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) { const { done, value } = await reader.read(); if (done) break; size += value.byteLength;
      if (size > 4096) { await reader.cancel(); return json({ error: "invalid_request" }, 400); } chunks.push(value); }
    const bytes = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    const body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)) as Record<string, unknown>;
    if (!body || typeof body !== "object" || Array.isArray(body)) return json({ error: "invalid_request" }, 400);
    return createInvitation(env, administratorId, body.email, body.expiryHours ?? 72);
  } catch { return json({ error: "invalid_request" }, 400); }
  finally { reader.releaseLock(); }
}
