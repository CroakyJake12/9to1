import { verifyOAuthQueryParams } from "@better-auth/oauth-provider";
import { createAuth } from "./auth";
import { AUTH_BASE_PATH } from "./contract";
import type { Env } from "./env";
import { checkLoginLimit, checkRateLimit, readEmailIdentifier, readLoginIdentifier } from "./limiter";
import { consentPage, forgotPasswordPage, json, resetPasswordPage, signInPage, signUpPage } from "./pages";
import { handleResourceApi } from "./resource-api";
import AUTH_UI from "../public/auth-ui.txt";

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
  if (publicMethod === "POST" && request.method === "POST" && request.headers.has("origin") &&
      !publicCors!.has("access-control-allow-origin")) {
    return addCors(json({ error: "origin_not_allowed" }, 403), publicCors!);
  }
  let auth;
  try { auth = createAuth(env); }
  catch {
    const unavailable = json({ error: "service_unavailable" }, 503);
    return publicCors ? addCors(unavailable, publicCors) : unavailable;
  }

  if (request.method === "GET" && url.pathname === "/assets/auth-ui.js") {
    return new Response(AUTH_UI, { headers: { "content-type": "text/javascript; charset=utf-8", "cache-control": "public, max-age=3600", "x-content-type-options": "nosniff" } });
  }
  if (request.method === "GET" && url.pathname === "/sign-in") return signInPage();
  if (request.method === "GET" && url.pathname === "/sign-up") return signUpPage();
  if (request.method === "GET" && url.pathname === "/forgot-password") return forgotPasswordPage();
  if (request.method === "GET" && url.pathname === "/reset-password") return resetPasswordPage();
  if (request.method === "GET" && url.pathname === "/consent") {
    const query = emailConsentContext(url);
    const secret = (await auth.$context).secret;
    if (!query || !(await verifyOAuthQueryParams(url.search.slice(1), secret))) return json({ error: "invalid_authorization_request" }, 400);
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return Response.redirect(`${new URL(env.AUTH_BASE_URL).origin}/sign-in${url.search}`, 302);
    return consentPage(query.clientId, query.scopes);
  }
  if (request.method === "GET" && url.pathname === "/account") {
    const session = await auth.api.getSession({ headers: request.headers });
    if (!session) return Response.redirect(`${url.origin}/sign-in`, 302);
    const displayName = session.user.name.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
    return new Response(`<!doctype html><html lang="en"><meta charset="utf-8"><title>CAKE ID account</title><body><main><h1>CAKE ID</h1><p>Signed in as ${displayName}</p><p><a href="/api/account/profile">Profile API</a> · <a href="/api/account/sessions">Sessions API</a></p><form method="post" action="/api/auth/sign-out"><button type="submit">Sign out</button></form></main></body></html>`, { headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store", "content-security-policy": "default-src 'self'; base-uri 'none'; frame-ancestors 'none'" } });
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
        return new Response(JSON.stringify({ error: "too_many_attempts", message: "Too many account email requests. Try again later." }), {
          status: 429,
          headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "retry-after": String(limit.retryAfterSeconds) },
        });
      }
    } catch {
      return json({ error: "service_unavailable" }, 503);
    }
  }

  const reply = await auth.handler(request);
  return publicCors ? addCors(reply, publicCors) : reply;
}

const worker = {
  fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    return fetchRequest(request, env, ctx);
  },
};

export default worker;
