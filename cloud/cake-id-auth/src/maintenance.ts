import type { Env } from "./env";
import { LOGIN_LIMITS } from "./contract";

// Chosen operational cleanup thresholds, not statutory retention periods.
// Call only from a trusted scheduled handler; this module exposes no HTTP route.
export const TRANSIENT_RETENTION = Object.freeze({
  turnstileGraceMilliseconds: 24 * 60 * 60 * 1000,
  invitationGraceMilliseconds: 30 * 24 * 60 * 60 * 1000,
  attemptGraceSeconds: 24 * 60 * 60,
  emailAttemptWindowSeconds: 60 * 60,
  maximumRowsPerTable: 500,
});

export interface MaintenanceCounts {
  turnstileDigests: number;
  invitations: number;
  loginAttemptCounters: number;
}
type MaintenanceEnv = Pick<Env, "DB"> & { LOGIN_WINDOW_SECONDS?: string };

function longestAttemptWindow(value: string | undefined): number {
  const configured = Number(value);
  const loginWindow = Number.isSafeInteger(configured) && configured > 0 ? configured : LOGIN_LIMITS.windowSeconds;
  // Existing hashed buckets do not identify their action or retain their policy.
  // The only other maintained caller uses a one-hour email-action window.
  return Math.max(loginWindow, TRANSIENT_RETENTION.emailAttemptWindowSeconds);
}

export async function purgeTransientData(env: MaintenanceEnv, nowMilliseconds = Date.now()): Promise<MaintenanceCounts> {
  if (!Number.isSafeInteger(nowMilliseconds) || nowMilliseconds < 0)
    throw new Error("invalid_maintenance_clock");
  const turnstileCutoff = nowMilliseconds - TRANSIENT_RETENTION.turnstileGraceMilliseconds;
  const invitationCutoff = nowMilliseconds - TRANSIENT_RETENTION.invitationGraceMilliseconds;
  const attemptCutoff = Math.floor(nowMilliseconds / 1000) - TRANSIENT_RETENTION.attemptGraceSeconds;
  const attemptWindowCutoff = attemptCutoff - longestAttemptWindow(env.LOGIN_WINDOW_SECONDS);
  if (!Number.isSafeInteger(attemptWindowCutoff)) throw new Error("invalid_maintenance_window");
  const limit = TRANSIENT_RETENTION.maximumRowsPerTable;

  // A consumed/revoked invitation is no longer usable even if its original
  // expiry lies in the future. If both terminal markers exist, keep it until
  // the later marker is thirty days old. Unused invitations age from expiry.
  const invitationEndedAt = `CASE
    WHEN consumedAt IS NOT NULL AND revokedAt IS NOT NULL THEN max(consumedAt, revokedAt)
    WHEN consumedAt IS NOT NULL THEN consumedAt
    WHEN revokedAt IS NOT NULL THEN revokedAt
    ELSE expiresAt END`;

  try {
    const results = await env.DB.batch([
      env.DB.prepare(`DELETE FROM cake_turnstile_redemptions WHERE tokenHash IN (
        SELECT tokenHash FROM cake_turnstile_redemptions
        WHERE expiresAt BETWEEN 0 AND ? ORDER BY expiresAt, tokenHash LIMIT ?)`)
        .bind(turnstileCutoff, limit),
      env.DB.prepare(`DELETE FROM cake_account_invitations WHERE id IN (
        SELECT id FROM cake_account_invitations
        WHERE createdAt BETWEEN 0 AND ? AND (${invitationEndedAt}) BETWEEN 0 AND ?
        ORDER BY (${invitationEndedAt}), id LIMIT ?)`)
        .bind(invitationCutoff, invitationCutoff, limit),
      env.DB.prepare(`DELETE FROM cake_login_attempts WHERE keyHash IN (
        SELECT keyHash FROM cake_login_attempts
        WHERE windowStartedAt BETWEEN 0 AND ? AND lockedUntil BETWEEN 0 AND ?
          AND updatedAt BETWEEN 0 AND ?
        ORDER BY updatedAt, keyHash LIMIT ?)`)
        .bind(attemptWindowCutoff, attemptCutoff, attemptCutoff, limit),
    ]);
    if (results.length !== 3 || results.some(result => !result.success ||
      !Number.isSafeInteger(result.meta?.changes) || result.meta.changes < 0 || result.meta.changes > limit))
      throw new Error("unexpected_maintenance_result");
    // Return only counts. Never return/log keys, invitation emails or digests.
    return { turnstileDigests: results[0].meta.changes, invitations: results[1].meta.changes,
      loginAttemptCounters: results[2].meta.changes };
  } catch {
    // Do not forward database/provider diagnostics into scheduled status logs.
    throw new Error("transient_retention_failed");
  }
}
