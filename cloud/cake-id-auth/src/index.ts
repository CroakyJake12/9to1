import { verifyOAuthQueryParams } from "@better-auth/oauth-provider";
import { createAuth, googleCredentials, registrationHeld } from "./auth";
import { AUTH_BASE_PATH } from "./contract";
import type { Env } from "./env";
import { checkLoginLimit, checkRateLimit, readEmailIdentifier, readLoginIdentifier } from "./limiter";
import { consentPage, forgotPasswordPage, invitationsPage, json, onboardingPage, resetPasswordPage, signInPage, signUpPage } from "./pages";
import { handleResourceApi } from "./resource-api";
import { readPresentAppState, readStagingAppsState, stagingAppsAccountPage } from "./staging-apps";
import { enableOwnedSignIn, readProductionSitesState, productionSitesPage } from "./production-sites";
import { publicAppDestination, readPublicApp } from "./public-app";
import { prepareTurnstile, turnstileRequired, turnstileSiteKey, verifyTurnstile } from "./turnstile";
import { invitationRequest, isInvitationAdministrator } from "./invitations";
import { acceptAccountPolicy, policyVersions } from "./public-policy";
import { purgeTransientData } from "./maintenance";
import { flushSecurityEvents, recordSecurityEvent } from "./security-telemetry";
import AUTH_UI from "../public/auth-ui.txt";
import { brandAssetResponse } from "./brand-assets";

// Provider logs attach the original request URL even to a static console event.
// Only fixed paths may emit a failure event; recovery tokens can be path segments.
const safeFailureLogPaths = new Set([
  "/sign-in", "/sign-up", "/forgot-password", "/reset-password", "/consent", "/account",
  "/account/onboarding", "/account/invitations", "/account/production-sites", "/account/production-sites/state",
  "/api/auth/sign-in/email", "/api/auth/sign-in/username", "/api/auth/sign-in/social", "/api/auth/sign-up/email",
  "/api/auth/request-password-reset", "/api/auth/reset-password", "/api/auth/send-verification-email", "/api/auth/verify-email",
  "/api/auth/callback/google", "/api/auth/oauth2/authorize", "/api/auth/oauth2/token", "/api/auth/oauth2/consent",
  "/api/auth/oauth2/userinfo", "/api/auth/oauth2/introspect", "/api/account/current", "/api/account/profile",
  "/api/account/theme", "/api/account/sessions", "/api/account/export", "/api/account/signout", "/api/account/revoke-other-sessions",
]);

function emailConsentContext(url: URL): { clientId: string; scopes: string[] } | null {
  const query = url.search.slice(1);
  const params = new URLSearchParams(query);
  const clientId = params.get("client_id");
  const scopes = params.get("scope")?.split(/\s+/).filter(Boolean) ?? [];
  return clientId && scopes.length ? { clientId, scopes } : null;
}

function corsHeaders(request: Request, env: Env): Headers {
  const headers = new Headers({ "access-control-allow-headers": "authorization, content-type", "access-control-allow-methods": "GET, PATCH, DELETE, POST, OPTIONS", "vary": "Origin" });
  const origin = request.headers.get("origin");
  const allowed = (env.ALLOWED_WEB_ORIGINS ?? "").split(",").map((item) => item.trim()).filter(Boolean);
  if (origin && allowed.includes(origin)) {
    headers.set("access-control-allow-origin", origin);
    headers.set("access-control-allow-credentials", "true");
  }
  return headers;
}

// Public browser OAuth transport only. Login/session-cookie routes retain the library's issuer-origin CSRF policy.
function publicOAuthMethod(pathname: string): "GET" | "POST" | null {
  if (pathname === `${AUTH_BASE_PATH}/.well-known/openid-configuration` || pathname === `${AUTH_BASE_PATH}/jwks`) return "GET";
  return pathname === `${AUTH_BASE_PATH}/oauth2/token` ? "POST" : null;
}

function publicOAuthCors(request: Request, env: Env, method: "GET" | "POST"): Headers {
  const headers = new Headers({ "vary": "Origin", "access-control-allow-methods": `${method}, OPTIONS`,
    "access-control-allow-headers": "content-type" });
  const origin = request.headers.get("origin");
  const allowed = (env.ALLOWED_WEB_ORIGINS ?? "").split(",").map(value => value.trim()).filter(Boolean);
  if (origin && allowed.includes(origin)) {
    try {
      const parsed = new URL(origin);
      if (parsed.origin === origin && ["https:", "http:"].includes(parsed.protocol)) headers.set("access-control-allow-origin", origin);
    } catch { /* Opaque, wildcard and malformed origins never receive a public grant. */ }
  }
  // Deliberately no Allow-Credentials: token requests use public-client PKCE, not cookie authority.
  return headers;
}

function addCors(response: Response, headers: Headers): Response {
  const merged = new Headers(response.headers);
  headers.forEach((value, key) => {
    if (key === "vary") {
      const values = [...(merged.get("vary") ?? "").split(","), ...value.split(",")].map(item => item.trim()).filter(Boolean);
      merged.set(key, [...new Map(values.map(item => [item.toLowerCase(), item])).values()].join(", "));
    } else merged.set(key, value);
  });
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers: merged });
}

export async function fetchRequest(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
  const url = new URL(request.url);
  const asset = brandAssetResponse(request); if (asset) return asset;
  const publicApp = readPublicApp(url.searchParams);
  const signupPolicy = registrationHeld(env) ? null : policyVersions(env);
  const preparedChallenge = await prepareTurnstile(request, env);
  if (preparedChallenge instanceof Response) return preparedChallenge;
  const publicMethod = publicOAuthMethod(url.pathname);
  const publicCors = publicMethod ? publicOAuthCors(request, env, publicMethod) : null;
  if (publicCors && request.method === "OPTIONS") {
    const requestedHeaders = (request.headers.get("access-control-request-headers") ?? "")
      .split(",").map(value => value.trim().toLowerCase()).filter(Boolean);
    if (!publicCors.has("access-control-allow-origin") || request.headers.get("access-control-request-method") !== publicMethod ||
        requestedHeaders.some(value => value !== "content-type")) {
      return addCors(json({ error: "origin_not_allowed" }, 403), publicCors);
    }
    return new Response(null, { status: 204, headers: publicCors });
  }
  if (publicMethod === "POST" && request.method === "POST" && request.headers.has("origin") && request.headers.get("origin") !== url.origin &&
      !publicCors!.has("access-control-allow-origin")) {
    return addCors(json({ error: "origin_not_allowed" }, 403), publicCors!);
  }
  let auth;
  try { auth = createAuth(env); }
  catch {
    const unavailable = json({ error: "service_unavailable" }, 503);
    return publicCors ? addCors(unavailable, publicCors) : unavailable;
  }

  if (request.method === "GET" && url.pathname === "/") {
    return new Response(null, { status: 303, headers: { "location": `${url.origin}/account`, "cache-control": "no-store" } });
  }
  if (request.method === "GET" && url.pathname === "/assets/auth-ui.js") {
    return new Response(AUTH_UI, { headers: { "content-type": "text/javascript; charset=utf-8", "cache-control": "public, max-age=0, must-revalidate", "x-content-type-options": "nosniff" } });
  }
  if (request.method === "GET" && url.pathname === "/sign-in") {
    if (url.searchParams.get("social") === "complete") {
      const session = await auth.api.getSession({ headers: request.headers });
      if (session) return new Response(null, { status: 303, headers: {
        location: new URL(publicAppDestination(publicApp), url.origin).href, "cache-control": "no-store" } });
    }
    return signInPage(publicApp, googleCredentials(env) !== null, turnstileSiteKey(env), turnstileRequired(request, env), signupPolicy);
  }
  if (request.method === "GET" && url.pathname === "/sign-up") return signUpPage(publicApp, googleCredentials(env) !== null, turnstileSiteKey(env), turnstileRequired(request, env), signupPolicy);
  if (request.method === "GET" && url.pathname === "/forgot-password") return forgotPasswordPage(publicApp, googleCredentials(env) !== null, turnstileSiteKey(env), turnstileRequired(request, env), signupPolicy);
  if (request.method === "GET" && url.pathname === "/reset-password") return resetPasswordPage(publicApp);
  if (request.method === "GET" && url.pathname === "/consent") {
    const query = emailConsentContext(url);
    const secret = (await auth.$context).secret;
    if (!query || !(await verifyOAuthQueryParams(url.search.slice(1), secret))) return json({ error: "invalid_authorization_request" }, 400);
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return Response.redirect(`${new URL(env.AUTH_BASE_URL).origin}/sign-in${url.search}`, 302);
    return consentPage(query.clientId, query.scopes);
  }
  if (url.pathname === "/account/invitations" && ["GET", "POST"].includes(request.method)) {
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return json({ error: "unauthorized" }, 401);
    if (!await isInvitationAdministrator(env, session.user.id)) return json({ error: "forbidden" }, 403);
    if (request.method === "GET") return invitationsPage();
    return invitationRequest(request, env, session.user.id);
  }
  if (url.pathname === "/account/onboarding" && ["GET", "POST"].includes(request.method)) {
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return json({ error: "unauthorized" }, 401);
    const policy = policyVersions(env);
    if (!policy) return json({ error: "account_policy_unavailable" }, 503);
    if (request.method === "POST") {
      try { return await acceptAccountPolicy(request, env, session.user.id, session.session.id); }
      catch { return json({ error: "account_policy_unavailable" }, 503); }
    }
    if (url.searchParams.has("client_id") || url.searchParams.has("sig")) {
      if (!await verifyOAuthQueryParams(url.search.slice(1), (await auth.$context).secret))
        return json({ error: "invalid_authorization_request" }, 400);
    }
    return onboardingPage(publicApp, policy);
  }
  if (url.pathname === "/account/production-sites" && request.method === "POST") {
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return json({ error: "unauthorized" }, 401);
    return enableOwnedSignIn(request, env, session.user.id, auth.api);
  }
  if (request.method === "GET" && ["/account", "/account/staging-apps", "/account/present-app", "/account/production-sites", "/account/production-sites/state"].includes(url.pathname)) {
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return url.pathname === "/account"
      ? Response.redirect(`${url.origin}/sign-in`, 302) : json({ error: "unauthorized" }, 401);
    try {
      if (url.pathname === "/account/production-sites" || url.pathname === "/account/production-sites/state") {
        const production = await readProductionSitesState(env, session.user.id);
        return url.pathname === "/account/production-sites"
          ? productionSitesPage(production)
          : json(production, production.kind === "not-admin" ? 403 : 200);
      }
      if (url.pathname === "/account/present-app") {
        const present = await readPresentAppState(env, session.user.id);
        return json(present, present.kind === "not-admin" ? 403 : 200);
      }
      const state = await readStagingAppsState(env, session.user.id);
      if (url.pathname === "/account/staging-apps")
        return json(state, state.kind === "not-admin" ? 403 : 200);
      return stagingAppsAccountPage(session.user.name, state, await readPresentAppState(env, session.user.id));
    } catch { return json({ error: "service_unavailable" }, 503); }
  }

  if (url.pathname.startsWith("/api/account/")) {
    const headers = corsHeaders(request, env);
    if (request.method === "OPTIONS") {
      if (!headers.has("access-control-allow-origin")) return json({ error: "origin_not_allowed" }, 403);
      return new Response(null, { status: 204, headers });
    }
    return addCors(await handleResourceApi(request, env, auth), headers);
  }

  if (request.method === "POST" && [
    `${AUTH_BASE_PATH}/sign-in/email`,
    `${AUTH_BASE_PATH}/sign-in/username`,
  ].includes(url.pathname)) {
    try {
      const identifier = await readLoginIdentifier(request);
      const limit = await checkLoginLimit(env, request, identifier);
      if (!limit.allowed) {
        recordSecurityEvent("rate_limited");
        return new Response(JSON.stringify({ error: "too_many_attempts", message: "Too many sign-in attempts. Try again later." }), {
          status: 429,
          headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "retry-after": String(limit.retryAfterSeconds) },
        });
      }
    } catch {
      return json({ error: "service_unavailable" }, 503);
    }
  }

  if (request.method === "POST" && [
    `${AUTH_BASE_PATH}/sign-up/email`,
    `${AUTH_BASE_PATH}/request-password-reset`,
    `${AUTH_BASE_PATH}/send-verification-email`,
  ].includes(url.pathname)) {
    try {
      const identifier = await readEmailIdentifier(request);
      const limit = await checkRateLimit(env, request, identifier, url.pathname, {
        maximumAttempts: 3,
        windowSeconds: 60 * 60,
        lockSeconds: 60 * 60,
      });
      if (!limit.allowed) {
        recordSecurityEvent("rate_limited");
        return new Response(JSON.stringify({ error: "too_many_attempts", message: "Too many account email requests. Try again later." }), {
          status: 429,
          headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "retry-after": String(limit.retryAfterSeconds) },
        });
      }
    } catch {
      return json({ error: "service_unavailable" }, 503);
    }
  }

  if (preparedChallenge) {
    const denial = await verifyTurnstile(preparedChallenge, request, env);
    if (denial) return denial;
  }
  const reply = await auth.handler(request);
  return publicCors ? addCors(reply, publicCors) : reply;
}

const worker = {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try { return await fetchRequest(request, env, ctx); }
    catch {
      if (safeFailureLogPaths.has(new URL(request.url).pathname)) recordSecurityEvent("service_error");
      return json({ error: "service_unavailable" }, 503);
    }
  },
  async scheduled(_controller: ScheduledController, env: Env): Promise<void> {
    try {
      const counts = await purgeTransientData(env);
      console.info(JSON.stringify({ event: "account_transient_retention", counts }));
      flushSecurityEvents();
    } catch {
      console.error(JSON.stringify({ event: "account_transient_retention_failed" }));
      throw new Error("account_transient_retention_failed");
    }
  },
};

export default worker;
