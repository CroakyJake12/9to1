import { AUTHORIZATION_SCOPES } from "./contract";
import type { Env } from "./env";

const CLIENT_NAME = "9to1 staging apps";
const REDIRECT_URI = "https://9to1-apps-staging-20261005.jcbailey008.workers.dev/oauth/callback";

export type StagingAppsState =
  | { kind: "not-admin" | "conflict" }
  | { kind: "ready"; clientId: string }
  | { kind: "missing"; registration: ReturnType<typeof registration> };

function registration() {
  return {
    client_name: CLIENT_NAME, redirect_uris: [REDIRECT_URI],
    token_endpoint_auth_method: "none", application_type: "web",
    grant_types: ["authorization_code", "refresh_token"], response_types: ["code"],
    scope: AUTHORIZATION_SCOPES.join(" "),
  };
}

type PublicRow = {
  clientId: string; userId: string; name: string; redirectUris: string;
  tokenEndpointAuthMethod: string; applicationType: string;
  grantTypes: string; responseTypes: string; scopes: string;
  disabled: number | null; skipConsent: number | null;
  requirePKCE: number | null; noSecret: number;
};

function exactArray(encoded: string, expected: readonly string[]): boolean {
  try {
    const value: unknown = JSON.parse(encoded);
    return Array.isArray(value) && value.length === expected.length &&
      value.every(item => typeof item === "string") &&
      new Set(value).size === value.length && expected.every(item => value.includes(item));
  } catch { return false; }
}

// Read-only metadata observation. Authority remains the maintained create-client endpoint.
export async function readStagingAppsState(env: Env, userId: string): Promise<StagingAppsState> {
  const admin = await env.DB.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1")
    .bind(userId).first();
  if (admin === null) return { kind: "not-admin" };
  const result = await env.DB.prepare(
    'SELECT clientId, userId, name, redirectUris, tokenEndpointAuthMethod, applicationType, ' +
    'grantTypes, responseTypes, scopes, disabled, skipConsent, requirePKCE, ' +
    '(clientSecret IS NULL) AS noSecret FROM oauthClient WHERE userId = ? AND name = ? LIMIT 2')
    .bind(userId, CLIENT_NAME).all<PublicRow>();
  if (!result.success) throw new Error("Staging client metadata lookup failed.");
  const rows = result.results;
  if (rows.length === 0) return { kind: "missing", registration: registration() };
  if (rows.length !== 1) return { kind: "conflict" };
  const row = rows[0];
  if (typeof row.clientId !== "string" || !row.clientId || row.clientId.length > 256 ||
      row.userId !== userId || row.name !== CLIENT_NAME || row.noSecret !== 1 ||
      (row.disabled ?? 0) !== 0 || (row.skipConsent ?? 0) !== 0 ||
      row.tokenEndpointAuthMethod !== "none" || row.applicationType !== "web" ||
      (row.requirePKCE !== null && row.requirePKCE !== 1) ||
      !exactArray(row.redirectUris, [REDIRECT_URI]) ||
      !exactArray(row.grantTypes, ["authorization_code", "refresh_token"]) ||
      !exactArray(row.responseTypes, ["code"]) ||
      !exactArray(row.scopes, AUTHORIZATION_SCOPES)) return { kind: "conflict" };
  const resources = await env.DB.prepare(
    "SELECT resourceId FROM oauthClientResource WHERE clientId = ? LIMIT 2")
    .bind(row.clientId).all<{ resourceId: string }>();
  if (!resources.success) throw new Error("Staging client resource lookup failed.");
  if (resources.results.length !== 1 || resources.results[0].resourceId !== env.API_RESOURCE)
    return { kind: "conflict" };
  // Recheck the canonical role after the metadata reads; do not expose cached session roles.
  const stillAdmin = await env.DB.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1")
    .bind(userId).first();
  return stillAdmin === null ? { kind: "not-admin" } : { kind: "ready", clientId: row.clientId };
}

function html(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}

export function stagingAppsAccountPage(name: string, state: StagingAppsState): Response {
  const connected = state.kind === "ready"
    ? `<p>Staging app OAuth client connected.</p><p>Public client ID: <code>${html(state.clientId)}</code></p>`
    : state.kind === "missing"
    ? '<form id="connect-staging-apps"><button type="submit">Connect staging apps</button></form>'
    : state.kind === "conflict"
    ? "<p>Existing staging client metadata needs review. No new client was created.</p>" : "";
  return new Response(`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>CAKE ID account</title><body><main><h1>CAKE ID</h1><p>Signed in as ${html(name)}</p><p><a href="/api/account/profile">Profile API</a> · <a href="/api/account/sessions">Sessions API</a></p>${connected}<p id="staging-apps-status" role="status" aria-live="polite"></p><form method="post" action="/api/auth/sign-out"><button type="submit">Sign out</button></form><script src="/assets/auth-ui.js?staging=20261005-01" defer></script></main></body></html>`, {
    headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store",
      "x-content-type-options": "nosniff", "referrer-policy": "no-referrer",
      "content-security-policy": "default-src 'self'; script-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'" },
  });
}
