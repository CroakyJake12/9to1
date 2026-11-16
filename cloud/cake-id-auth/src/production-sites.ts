import { AUTHORIZATION_SCOPES } from "./contract";
import type { Env } from "./env";
import { accountManagementPage, json } from "./pages";
import { readClientState, type ClientDefinition, type PublicClientState } from "./staging-apps";

export const PRODUCTION_SITES = {
  cake: { name: "CakeMods website", legacyName: "CAKE website", redirectUri: "https://cakemods.com/oauth/callback", expectedClientId: "hOJlgeZQJbqMuMvTyVyVylYngztBvFSI" },
  nine: { name: "9to1 website", redirectUri: "https://9to1.uk/oauth/callback", expectedClientId: "NWcyYbnuasNizDzUBSbKWwBqkFdKXCFE" },
} as const;
export type ProductionSite = keyof typeof PRODUCTION_SITES;
export type ProductionSitesState = { kind: "not-admin" } | {
  kind: "admin"; cake: PublicClientState; nine: PublicClientState;
};

function definition(site: ProductionSite): ClientDefinition {
  return { ...PRODUCTION_SITES[site], applicationType: "web",
    grants: ["authorization_code", "refresh_token"], scopes: AUTHORIZATION_SCOPES, allowSkipConsent: true };
}

/** Fixed owned-site changes only: direct sign-in, or the approved CakeMods name. */
export async function enableOwnedSignIn(request: Request, env: Env, userId: string, api: object): Promise<Response> {
  if (request.method !== "POST") return json({ error: "method_not_allowed" }, 405);
  if (request.headers.get("origin") !== new URL(env.AUTH_BASE_URL).origin ||
      request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json")
    return json({ error: "origin_not_allowed" }, 403);
  if (!request.body) return json({ error: "invalid_request" }, 400);
  const reader = request.body.getReader(); const chunks: Uint8Array[] = []; let size = 0; let body: unknown;
  try {
    while (true) { const { done, value } = await reader.read(); if (done) break; size += value.byteLength;
      if (size > 2048) { await reader.cancel(); return json({ error: "invalid_request" }, 400); } chunks.push(value); }
    const bytes = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  } catch { return json({ error: "invalid_request" }, 400); }
  finally { reader.releaseLock(); }
  if (!body || typeof body !== "object" || Array.isArray(body) || Object.keys(body).length !== 1 ||
      !("site" in body) || (body.site !== "cake" && body.site !== "nine")) return json({ error: "invalid_request" }, 400);
  const site = body.site, client = definition(site);
  const confirmed = () => json({ site, clientId: client.expectedClientId, directSignIn: true, displayNameCurrent: true });
  try {
    let state = await readClientState(env, userId, client);
    if (state.kind === "not-admin") return json({ error: "forbidden" }, 403);
    if (state.kind !== "ready") return json({ error: "client_metadata_conflict" }, 409);
    if (state.directSignIn && state.displayNameCurrent) return confirmed();
    if (!state.displayNameCurrent && !state.directSignIn) return json({ error: "client_metadata_conflict" }, 409);
    // Recheck ownership, canonical role and every metadata boundary immediately
    // before the maintained API performs its own administrator authorization.
    state = await readClientState(env, userId, client);
    if (state.kind === "not-admin") return json({ error: "forbidden" }, 403);
    if (state.kind !== "ready") return json({ error: "client_metadata_conflict" }, 409);
    if (state.directSignIn && state.displayNameCurrent) return confirmed();
    if (!state.displayNameCurrent && !state.directSignIn) return json({ error: "client_metadata_conflict" }, 409);
    const changes = state.displayNameCurrent ? { skip_consent: true } : { client_name: client.name };
    const update: unknown = Reflect.get(api, "adminUpdateOAuthClient");
    if (typeof update !== "function") return json({ error: "direct_signin_unavailable" }, 503);
    try {
      await Reflect.apply(update, api, [{ headers: request.headers,
        body: { client_id: client.expectedClientId!, update: changes } }]);
    } catch { /* A committed mutation can lose its reply. Read back; never replay. */ }
    const observed = await readClientState(env, userId, client);
    if (observed.kind === "not-admin") return json({ error: "forbidden" }, 403);
    if (observed.kind !== "ready") return json({ error: "client_metadata_conflict" }, 409);
    return observed.directSignIn && observed.displayNameCurrent ? confirmed() : json({ error: "direct_signin_not_confirmed" }, 503);
  } catch { return json({ error: "direct_signin_not_confirmed" }, 503); }
}

// Metadata observation only. The maintained authenticated provider is the sole writer.
export async function readProductionSitesState(env: Env, userId: string): Promise<ProductionSitesState> {
  const cake = await readClientState(env, userId, definition("cake"));
  if (cake.kind === "not-admin") return cake;
  const nine = await readClientState(env, userId, definition("nine"));
  if (nine.kind === "not-admin") return nine;
  return { kind: "admin", cake, nine };
}

function html(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}

export function productionSitesPage(state: ProductionSitesState): Response {
  if (state.kind !== "admin") return new Response("Forbidden", { status: 403,
    headers: { "content-type": "text/plain; charset=utf-8", "cache-control": "no-store" } });
  const section = (site: ProductionSite) => {
    const definition = PRODUCTION_SITES[site];
    const current = state[site];
    const status = current.kind === "ready"
      ? `${current.directSignIn ? "Direct sign-in enabled." : "Connected; permission prompt still enabled."} ${current.displayNameCurrent ? "Website details are up to date." : "Website details need synchronising."} Public client ID: <code>${html(current.clientId)}</code>`
      : current.kind === "conflict" ? "Existing client metadata needs review. No registration is available."
      : "No matching client is registered for this administrator.";
    return `<section class="management-section" aria-labelledby="${site}-heading"><h2 id="${site}-heading">${html(definition.name)}</h2><p>Callback: <code>${html(definition.redirectUri)}</code></p><p>${status}</p>` +
      (current.kind === "ready" && (!current.directSignIn || !current.displayNameCurrent) ? `<form id="connect-production-${site}" method="post"><button class="secondary" type="submit">Synchronise website details</button></form>` : "") +
      `<p id="production-${site}-status" role="status" aria-live="polite"></p></section>`;
  };
  return accountManagementPage("Connected websites", `<h1 id="account-title">Connected websites</h1><p>One 9to1 Account signs you into CakeMods and 9to1 directly. Synchronise the verified website details below. Other applications continue to request permission.</p>${section("cake")}${section("nine")}<p><a href="https://9to1.uk/auth/sign-in">Open your 9to1 Account</a></p>`);
}
