import { AUTHORIZATION_SCOPES } from "./contract";
import type { Env } from "./env";

const CLIENT_NAME = "9to1 staging apps";
const REDIRECT_URI = "https://9to1-apps-staging-20261005.jcbailey008.workers.dev/oauth/callback";

type ClientDefinition = {
  name: string; redirectUri: string; applicationType: "web" | "native";
  grants: readonly string[]; scopes: readonly string[];
};
const WEB_CLIENT: ClientDefinition = {
  name: CLIENT_NAME, redirectUri: REDIRECT_URI, applicationType: "web",
  grants: ["authorization_code", "refresh_token"], scopes: AUTHORIZATION_SCOPES,
};
const NATIVE_CLIENT: ClientDefinition = {
  name: "9to1WindowsNative", redirectUri: "http://127.0.0.1:43821/cake-id/callback/",
  applicationType: "native", grants: ["authorization_code"],
  scopes: ["openid", "profile", "cake:account:read", "cake:profile:read",
    "cake:sessions:read", "cake:sessions:revoke"],
};
type PublicClientState =
  | { kind: "conflict" }
  | { kind: "ready"; clientId: string }
  | { kind: "missing"; registration: ReturnType<typeof registration> };
type ClientState = { kind: "not-admin" } | PublicClientState;
// Existing web kind/clientId/registration retain their meaning; native is independent.
export type StagingAppsState = { kind: "not-admin" } | (PublicClientState & { native: PublicClientState });

function registration(client: ClientDefinition) {
  return {
    client_name: client.name, redirect_uris: [client.redirectUri],
    token_endpoint_auth_method: "none", application_type: client.applicationType,
    grant_types: [...client.grants], response_types: ["code"],
    scope: client.scopes.join(" "),
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
async function readClientState(env: Env, userId: string, client: ClientDefinition): Promise<ClientState> {
  const admin = await env.DB.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1")
    .bind(userId).first();
  if (admin === null) return { kind: "not-admin" };
  const result = await env.DB.prepare(
    'SELECT clientId, userId, name, redirectUris, tokenEndpointAuthMethod, applicationType, ' +
    'grantTypes, responseTypes, scopes, disabled, skipConsent, requirePKCE, ' +
    '(clientSecret IS NULL) AS noSecret FROM oauthClient WHERE userId = ? AND name = ? LIMIT 2')
    .bind(userId, client.name).all<PublicRow>();
  if (!result.success) throw new Error("Staging client metadata lookup failed.");
  const rows = result.results;
  if (rows.length === 0) return { kind: "missing", registration: registration(client) };
  if (rows.length !== 1) return { kind: "conflict" };
  const row = rows[0];
  if (typeof row.clientId !== "string" || !row.clientId || row.clientId.length > 256 ||
      row.userId !== userId || row.name !== client.name || row.noSecret !== 1 ||
      (row.disabled ?? 0) !== 0 || (row.skipConsent ?? 0) !== 0 ||
      row.tokenEndpointAuthMethod !== "none" || row.applicationType !== client.applicationType ||
      (row.requirePKCE !== null && row.requirePKCE !== 1) ||
      !exactArray(row.redirectUris, [client.redirectUri]) ||
      !exactArray(row.grantTypes, client.grants) ||
      !exactArray(row.responseTypes, ["code"]) ||
      !exactArray(row.scopes, client.scopes)) return { kind: "conflict" };
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

// GET only observes public metadata; the existing session-owned POST remains the sole creator.
export async function readStagingAppsState(env: Env, userId: string): Promise<StagingAppsState> {
  const web = await readClientState(env, userId, WEB_CLIENT);
  if (web.kind === "not-admin") return web;
  const native = await readClientState(env, userId, NATIVE_CLIENT);
  if (native.kind === "not-admin") return native;
  return { ...web, native };
}

function html(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}

export function stagingAppsAccountPage(name: string, state: StagingAppsState): Response {
  const describe = (label: string, client: PublicClientState): string => client.kind === "ready"
    ? `<p>${label} connected. Public client ID: <code>${html(client.clientId)}</code></p>`
    : client.kind === "conflict" ? `<p>${label} metadata needs review. No new client was created.</p>` : "";
  const connected = state.kind === "not-admin" ? "" :
    describe("Staging web app client", state) + describe("Windows native app client", state.native) +
    (state.kind !== "conflict" && state.native.kind !== "conflict" &&
      (state.kind === "missing" || state.native.kind === "missing")
      ? '<form id="connect-staging-apps"><button type="submit">Connect staging apps</button></form>' : "");
  return new Response(`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>CAKE ID account</title><body><main><h1>CAKE ID</h1><p>Signed in as ${html(name)}</p><p><a href="/api/account/profile">Profile API</a> · <a href="/api/account/sessions">Sessions API</a></p>${connected}<p id="staging-apps-status" role="status" aria-live="polite"></p><form method="post" action="/api/auth/sign-out"><button type="submit">Sign out</button></form><script src="/assets/auth-ui.js?staging=20261005-02" defer></script></main></body></html>`, {
    headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store",
      "x-content-type-options": "nosniff", "referrer-policy": "no-referrer",
      "content-security-policy": "default-src 'self'; script-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'" },
  });
}
