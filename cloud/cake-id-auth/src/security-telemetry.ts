const kinds = ["captcha_rejected", "captcha_unavailable", "rate_limited", "signup_rejected", "policy_rejected", "service_error",
  "captcha_unavailable_configuration", "captcha_unavailable_replay_read", "captcha_unavailable_siteverify_transport",
  "captcha_unavailable_siteverify_response", "captcha_unavailable_replay_commit"] as const;
export type SecurityEvent = typeof kinds[number];
const captchaUnavailableKinds = {
  configuration: "captcha_unavailable_configuration",
  replay_read: "captcha_unavailable_replay_read",
  siteverify_transport: "captcha_unavailable_siteverify_transport",
  siteverify_response: "captcha_unavailable_siteverify_response",
  replay_commit: "captcha_unavailable_replay_commit",
} as const;
export type CaptchaUnavailableStage = keyof typeof captchaUnavailableKinds;
// Best-effort per-isolate aggregates, never an identity/audit record. The key
// set and count range are bounded; no request values enter the log payload.
const counts: Partial<Record<SecurityEvent, number>> = {};
let lastEmission = 0;
export function flushSecurityEvents(now = Date.now()): void {
  if (!Object.keys(counts).length) return;
  console.warn(JSON.stringify({ event: "account_security_events", counts: { ...counts } }));
  for (const kind of kinds) delete counts[kind];
  lastEmission = now;
}
function recordKinds(events: readonly SecurityEvent[]): void {
  for (const kind of events) {
    if (!kinds.includes(kind)) continue;
    counts[kind] = Math.min(1_000_000, (counts[kind] ?? 0) + 1);
  }
  const now = Date.now();
  if (now - lastEmission >= 60_000) flushSecurityEvents(now);
}
export function recordSecurityEvent(kind: SecurityEvent): void { recordKinds([kind]); }
export function recordCaptchaUnavailable(stage: CaptchaUnavailableStage): void {
  if (!Object.hasOwn(captchaUnavailableKinds, stage)) return;
  // Record the aggregate and its static stage together, including the first
  // request in an isolate, without accepting exception or request text.
  recordKinds(["captcha_unavailable", captchaUnavailableKinds[stage]]);
}
