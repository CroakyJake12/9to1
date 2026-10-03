import { LOGIN_LIMITS } from "./contract";
import type { Env } from "./env";

interface LimitResult {
  allowed: boolean;
  retryAfterSeconds: number;
}

export interface AttemptPolicy {
  maximumAttempts: number;
  windowSeconds: number;
  lockSeconds: number;
}

async function keyedHash(key: string, value: string): Promise<string> {
  const encoder = new TextEncoder();
  const secret = await crypto.subtle.importKey("raw", encoder.encode(key), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const bytes = await crypto.subtle.sign("HMAC", secret, encoder.encode(value));
  return Array.from(new Uint8Array(bytes), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function integerSetting(value: string | undefined, fallback: number): number {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback;
}

export async function checkRateLimit(env: Env, request: Request, normalizedEmail: string, action: string, policy: AttemptPolicy): Promise<LimitResult> {
  if (!env.LOGIN_LIMITER_KEY || env.LOGIN_LIMITER_KEY.length < 32) {
    throw new Error("Login limiter key is not configured");
  }

  const address = request.headers.get("CF-Connecting-IP") ?? "local-unknown";
  const hash = await keyedHash(env.LOGIN_LIMITER_KEY, `${action}\n${normalizedEmail}\n${address}`);
  const now = Math.floor(Date.now() / 1000);
  const max = policy.maximumAttempts;
  const window = policy.windowSeconds;
  const lock = policy.lockSeconds;

  const existing = await env.DB.prepare("SELECT lockedUntil FROM cake_login_attempts WHERE keyHash = ?")
    .bind(hash)
    .first<{ lockedUntil: number }>();
  if (existing && existing.lockedUntil > now) {
    return { allowed: false, retryAfterSeconds: Math.max(1, existing.lockedUntil - now) };
  }

  await env.DB.prepare(`
    INSERT INTO cake_login_attempts (keyHash, windowStartedAt, attempts, lockedUntil, updatedAt)
    VALUES (?, ?, 1, 0, ?)
    ON CONFLICT(keyHash) DO UPDATE SET
      windowStartedAt = CASE
        WHEN cake_login_attempts.lockedUntil > ? THEN cake_login_attempts.windowStartedAt
        WHEN ? - cake_login_attempts.windowStartedAt >= ? THEN ?
        ELSE cake_login_attempts.windowStartedAt
      END,
      attempts = CASE
        WHEN cake_login_attempts.lockedUntil > ? THEN cake_login_attempts.attempts
        WHEN ? - cake_login_attempts.windowStartedAt >= ? THEN 1
        WHEN cake_login_attempts.attempts >= ? THEN cake_login_attempts.attempts
        ELSE cake_login_attempts.attempts + 1
      END,
      lockedUntil = CASE
        WHEN cake_login_attempts.lockedUntil > ? THEN cake_login_attempts.lockedUntil
        WHEN ? - cake_login_attempts.windowStartedAt >= ? THEN 0
        WHEN cake_login_attempts.attempts >= ? THEN ? + ?
        ELSE 0
      END,
      updatedAt = ?
  `).bind(hash, now, now, now, now, window, now, now, now, window, max, now, now, window, max, now, lock, now).run();

  const row = await env.DB.prepare("SELECT attempts, lockedUntil FROM cake_login_attempts WHERE keyHash = ?")
    .bind(hash)
    .first<{ attempts: number; lockedUntil: number }>();

  if (!row || row.lockedUntil <= now) return { allowed: true, retryAfterSeconds: 0 };
  return { allowed: false, retryAfterSeconds: Math.max(1, row.lockedUntil - now) };
}

export async function checkLoginLimit(env: Env, request: Request, identifier: string): Promise<LimitResult> {
  return checkRateLimit(env, request, identifier, "login", {
    maximumAttempts: integerSetting(env.LOGIN_MAX_ATTEMPTS, LOGIN_LIMITS.maximumAttempts),
    windowSeconds: integerSetting(env.LOGIN_WINDOW_SECONDS, LOGIN_LIMITS.windowSeconds),
    lockSeconds: integerSetting(env.LOGIN_LOCK_SECONDS, LOGIN_LIMITS.lockSeconds),
  });
}

export async function readLoginIdentifier(request: Request): Promise<string> {
  try {
    const body = await request.clone().json() as { email?: unknown; username?: unknown };
    if (typeof body.email === "string") return `email:${body.email.trim().toLowerCase().slice(0, 320)}`;
    if (typeof body.username === "string") return `username:${body.username.trim().toLowerCase().slice(0, 320)}`;
  } catch {
    // Treat malformed or absent identifiers as one generic bucket per source address.
  }
  return "unknown:";
}

export async function readEmailIdentifier(request: Request): Promise<string> {
  try {
    const body = await request.clone().json() as { email?: unknown };
    if (typeof body.email === "string") return body.email.trim().toLowerCase().slice(0, 320);
  } catch {
    // Treat malformed or absent email as one generic identifier for this source address.
  }
  return "";
}
