import type { Env } from "./env";
import { json } from "./pages";
import { recordCaptchaUnavailable, recordSecurityEvent, type CaptchaUnavailableStage } from "./security-telemetry";

function reject(error: string, status: number, stage?: CaptchaUnavailableStage): Response {
  if (status === 503 && stage) recordCaptchaUnavailable(stage);
  else recordSecurityEvent(status === 503 ? "captcha_unavailable" : "captcha_rejected");
  return json({ error }, status);
}

const SITEVERIFY = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
const actions: Readonly<Record<string, string>> = {
  "/api/auth/sign-in/email": "login",
  "/api/auth/sign-in/username": "login",
  "/api/auth/sign-up/email": "signup",
  "/api/auth/request-password-reset": "recovery",
  "/api/auth/send-verification-email": "verify_email",
};
export interface TurnstileRequest { token: string; action: string; hostname: string }

function loopback(value: string): boolean {
  try { const url = new URL(value); return url.protocol === "http:" && ["127.0.0.1", "localhost", "[::1]"].includes(url.hostname); }
  catch { return false; }
}
export function turnstileRequired(request: Request, env: Env): boolean {
  return !(env.APP_MODE === "local-test-only" && loopback(env.AUTH_BASE_URL) && loopback(request.url));
}
export function turnstileSiteKey(env: Env): string | null {
  return typeof env.TURNSTILE_SITE_KEY === "string" && /^[a-zA-Z0-9_-]{10,128}$/.test(env.TURNSTILE_SITE_KEY) ? env.TURNSTILE_SITE_KEY : null;
}
async function boundedObject(message: { body: ReadableStream<Uint8Array> | null }, maximum: number,
  cancelOtherBranch?: () => Promise<void>): Promise<Record<string, unknown> | null> {
  if (!message.body) return null;
  const reader = message.body.getReader(); const chunks: Uint8Array[] = []; let count = 0;
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      count += value.byteLength;
      if (count > maximum) {
        // A cloned Request tees its stream: cancelling only the clone can wait
        // forever for the original branch. An oversized request is rejected, so
        // cancel both branches together before returning.
        await Promise.allSettled([reader.cancel(), cancelOtherBranch?.()]);
        return null;
      }
      chunks.push(value);
    }
    const bytes = new Uint8Array(count); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    const parsed: unknown = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
    return parsed !== null && typeof parsed === "object" && !Array.isArray(parsed) ? parsed as Record<string, unknown> : null;
  } catch { return null; }
  finally { reader.releaseLock(); }
}

// Bound the body before existing identifier/rate-limit parsing. Unsupported
// routes never acquire a CAPTCHA requirement through a prefix match.
export async function prepareTurnstile(request: Request, env: Env): Promise<TurnstileRequest | Response | null> {
  const action = request.method === "POST" ? actions[new URL(request.url).pathname] : undefined;
  if (!action || !turnstileRequired(request, env)) return null;
  let base: URL;
  try { base = new URL(env.AUTH_BASE_URL); } catch { return reject("verification_unavailable", 503, "configuration"); }
  if (base.protocol !== "https:" || base.origin !== new URL(request.url).origin || base.username || base.password ||
      !turnstileSiteKey(env) || typeof env.TURNSTILE_SECRET_KEY !== "string" || !env.TURNSTILE_SECRET_KEY.trim())
    return reject("verification_unavailable", 503, "configuration");
  const origin = request.headers.get("origin");
  if (origin !== null && origin !== base.origin) return reject("origin_not_allowed", 403);
  if (request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json")
    return reject("invalid_request", 400);
  const body = await boundedObject(request.clone(), 16_384, () => request.body?.cancel() ?? Promise.resolve());
  if (!body) return reject("invalid_request", 400);
  const token = body["cf-turnstile-response"];
  if (typeof token !== "string" || !token.trim() || token.length > 2048)
    return reject("verification_required", 403);
  return { token, action, hostname: base.hostname };
}

export async function verifyTurnstile(prepared: TurnstileRequest, request: Request, env: Env,
  transport: typeof fetch = fetch, timeoutMilliseconds = 8000): Promise<Response | null> {
  const bytes = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(prepared.token));
  const tokenHash = Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, "0")).join("");
  const now = Date.now();
  try {
    const used = await env.DB.prepare("SELECT 1 FROM cake_turnstile_redemptions WHERE tokenHash = ? AND expiresAt > ?")
      .bind(tokenHash, now).first();
    if (used) return reject("verification_required", 403);
  } catch { return reject("verification_unavailable", 503, "replay_read"); }
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMilliseconds);
  let result: Record<string, unknown> | null;
  try {
    const form = new URLSearchParams({ secret: env.TURNSTILE_SECRET_KEY!, response: prepared.token });
    const address = request.headers.get("cf-connecting-ip");
    if (address) form.set("remoteip", address);
    const reply = await transport(SITEVERIFY, { method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" },
      body: form.toString(), signal: controller.signal, redirect: "manual" });
    // Workerd does not support redirect: "error". Manual retains the no-follow
    // boundary, and every non-2xx response (including redirects) fails closed.
    if (!reply.ok) { await reply.body?.cancel(); return reject("verification_unavailable", 503, "siteverify_response"); }
    result = await boundedObject(reply, 4096);
  } catch { return reject("verification_unavailable", 503, "siteverify_transport"); }
  finally { clearTimeout(timeout); }
  const issued = typeof result?.challenge_ts === "string" ? Date.parse(result.challenge_ts) : NaN;
  if (result?.success !== true || result.action !== prepared.action || result.hostname !== prepared.hostname ||
      !Number.isFinite(issued) || Date.now() - issued > 300_000 || issued - Date.now() > 30_000)
    return reject("verification_required", 403);
  try {
    // Siteverify replay detection is eventually consistent. A unique D1 claim
    // allows only one handler to proceed even if concurrent verifications pass.
    const results = await env.DB.batch([
      env.DB.prepare("DELETE FROM cake_turnstile_redemptions WHERE tokenHash IN (SELECT tokenHash FROM cake_turnstile_redemptions WHERE expiresAt < ? ORDER BY expiresAt LIMIT 100)").bind(Date.now()),
      env.DB.prepare("INSERT INTO cake_turnstile_redemptions (tokenHash, expiresAt) VALUES (?, ?) ON CONFLICT(tokenHash) DO NOTHING RETURNING tokenHash").bind(tokenHash, Date.now() + 600_000),
    ]);
    if (!results.every(result => result.success)) return reject("verification_unavailable", 503, "replay_commit");
    return results[1].results.length === 1 ? null : reject("verification_required", 403);
  } catch { return reject("verification_unavailable", 503, "replay_commit"); }
}
