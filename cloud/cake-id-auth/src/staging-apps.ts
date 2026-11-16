import { AUTHORIZATION_SCOPES } from "./contract";
import type { Env } from "./env";
import { accountManagementPage } from "./pages";

const CLIENT_NAME = "9to1 staging apps";
const REDIRECT_URI = "https://9to1-apps-staging-20261005.jcbailey008.workers.dev/oauth/callback";

export type ClientDefinition = {
  name: string; redirectUri: string; applicationType: "web" | "native";
  grants: readonly string[]; scopes: readonly string[];
  expectedClientId?: string;
  allowSkipConsent?: boolean;
  legacyName?: string;
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
const PRESENT_CLIENT: ClientDefinition = {
  name: "9to1 Present preview",
  redirectUri: "https://present-4581358882177bc7.9to1.uk/oauth/callback",
  applicationType: "web", grants: ["authorization_code", "refresh_token"],
  scopes: AUTHORIZATION_SCOPES,
};
export type PublicClientState =
  | { kind: "conflict" }
  | { kind: "ready"; clientId: string; directSignIn?: boolean; displayNameCurrent?: boolean }
  | { kind: "missing"; registration: ReturnType<typeof registration> };
type ClientState = { kind: "not-admin" } | PublicClientState;
export type PresentAppState = ClientState;
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
export async function readClientState(env: Env, userId: string, client: ClientDefinition): Promise<ClientState> {
  const admin = await env.DB.prepare("SELECT 1 FROM user WHERE id = ? AND role = 'admin' LIMIT 1")
    .bind(userId).first();
  if (admin === null) return { kind: "not-admin" };
  const legacyName = client.expectedClientId ? client.legacyName ?? null : null;
  const result = await env.DB.prepare(
    'SELECT clientId, userId, name, redirectUris, tokenEndpointAuthMethod, applicationType, ' +
    'grantTypes, responseTypes, scopes, disabled, skipConsent, requirePKCE, ' +
    '(clientSecret IS NULL) AS noSecret FROM oauthClient WHERE userId = ? AND (name = ? OR (? IS NOT NULL AND name = ?)) AND (? IS NULL OR clientId = ?) LIMIT 2')
    .bind(userId, client.name, legacyName, legacyName, client.expectedClientId ?? null, client.expectedClientId ?? null).all<PublicRow>();
  if (!result.success) throw new Error("Staging client metadata lookup failed.");
  const rows = result.results;
  if (rows.length === 0) return client.expectedClientId ? { kind: "conflict" } : { kind: "missing", registration: registration(client) };
  if (rows.length !== 1) return { kind: "conflict" };
  const row = rows[0];
  const maySkipConsent = client.allowSkipConsent === true && !!client.expectedClientId && row.clientId === client.expectedClientId;
  const acceptedName = row.name === client.name || (legacyName !== null && row.clientId === client.expectedClientId && row.name === legacyName);
  if (typeof row.clientId !== "string" || !row.clientId || row.clientId.length > 256 ||
      row.userId !== userId || !acceptedName || row.noSecret !== 1 ||
      (row.disabled ?? 0) !== 0 || ((row.skipConsent ?? 0) !== 0 && !(maySkipConsent && row.skipConsent === 1)) ||
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
  return stillAdmin === null ? { kind: "not-admin" } : { kind: "ready", clientId: row.clientId,
    ...(client.expectedClientId ? { directSignIn: row.skipConsent === 1, displayNameCurrent: row.name === client.name } : {}) };
}

// GET only observes public metadata; the existing session-owned POST remains the sole creator.
export async function readStagingAppsState(env: Env, userId: string): Promise<StagingAppsState> {
  const web = await readClientState(env, userId, WEB_CLIENT);
  if (web.kind === "not-admin") return web;
  const native = await readClientState(env, userId, NATIVE_CLIENT);
  if (native.kind === "not-admin") return native;
  return { ...web, native };
}

// This is an independent registration, never an edit or alias of the staging clients.
export async function readPresentAppState(env: Env, userId: string): Promise<PresentAppState> {
  return readClientState(env, userId, PRESENT_CLIENT);
}

function html(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}

export function stagingAppsAccountPage(name: string, state: StagingAppsState,
  present: PresentAppState = { kind: "not-admin" }): Response {
  const describe = (label: string, client: PublicClientState): string => client.kind === "ready"
    ? `<p>${label} connected. Public client ID: <code>${html(client.clientId)}</code></p>`
    : client.kind === "conflict" ? `<p>${label} metadata needs review. No new client was created.</p>` : "";
  const connected = state.kind === "not-admin" ? "" :
    describe("Staging web app client", state) + describe("Windows native app client", state.native) +
    (state.kind !== "conflict" && state.native.kind !== "conflict" &&
      (state.kind === "missing" || state.native.kind === "missing")
      ? '<form id="connect-staging-apps"><button class="secondary" type="submit">Connect staging apps</button></form>' : "");
  const presentConnection = present.kind === "not-admin" ? "" :
    `<section aria-labelledby="present-connection-heading"><h2 id="present-connection-heading">Present preview</h2><p>Public browser client for <code>${html(PRESENT_CLIENT.redirectUri)}</code>.</p>` +
    describe("Present preview client", present) +
    (present.kind === "missing" ? '<form id="connect-present-app"><button class="secondary" type="submit">Connect Present preview</button></form>' : "") +
    '<p id="present-app-status" role="status" aria-live="polite"></p></section>';
  return accountManagementPage("Your 9to1 Account", `<div class="card-heading"><p class="eyebrow">9to1 Account</p><h1 id="account-title">Your 9to1 Account.</h1><p>Signed in as ${html(name)}.</p></div><p>Your profile and preferences belong to one 9to1 Account across CakeMods and 9to1.</p><nav class="management-actions" aria-label="Your account and websites"><a href="https://9to1.uk/auth/sign-in">Open your 9to1 Account →</a><a href="https://cakemods.com/">Visit CakeMods →</a></nav><section class="management-section"><h2>Sign-in security</h2><p>Manage your devices in your 9to1 Account, or sign out of this shared session here.</p><form method="post" action="/api/auth/sign-out"><button class="secondary" type="submit">Sign out of 9to1 Account</button></form></section>${state.kind === "not-admin" ? "" : `<section class="management-section"><h2>Website administration</h2><p>Manage the website connections and prepare invitations for testing.</p><nav class="management-actions" aria-label="Account administration"><a href="/account/production-sites">Website sign-in</a><a href="/account/invitations">Invitations</a></nav><details class="management-details"><summary>Development connections</summary>${connected}<p id="staging-apps-status" role="status" aria-live="polite"></p>${presentConnection}</details></section>`}`);
}
