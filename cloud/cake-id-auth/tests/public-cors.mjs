import nodeAssert from "node:assert/strict";
import { randomBytes, createHash } from "node:crypto";
import { createLocalJWKSet, jwtVerify } from "jose";

// Actual maintained fixture/library routes only; no token construction or response doubles.
export async function testPublicCors(ctx) {
  const { authClient, response, jsonRequest, realFetch, cookies, baseURL, authPath,
    resource, browserOrigin, callback, clientId, testAccount, discovery } = ctx;
  let count = 0;
  const originalAssert = nodeAssert;
  const check = new Proxy(originalAssert, { get(target, key) {
    const value = target[key];
    return typeof value === "function" ? (...args) => { count++; return value(...args); } : value;
  } });
  const assert = check;
  let activeURL = new URL(baseURL);
  const savedWindow = globalThis.window;
  globalThis.window = { location: {
    get href() { return activeURL.href; }, set href(value) { activeURL = new URL(value, baseURL); },
    get search() { return activeURL.search; }, get pathname() { return activeURL.pathname; },
    assign(value) { activeURL = new URL(value, baseURL); },
  } };
  const publicRequest = (pathname, init = {}, origin = browserOrigin) => realFetch(new URL(pathname, baseURL), {
    ...init, headers: { ...init.headers, Origin: origin }, credentials: "omit", redirect: "manual",
  });
  const cors = (reply, origin = browserOrigin) => {
    assert.equal(reply.headers.get("access-control-allow-origin"), origin, "exact public allow-origin");
    assert.ok(reply.headers.get("vary")?.toLowerCase().split(/\s*,\s*/).includes("origin"), "Vary includes Origin");
    assert.equal(reply.headers.get("access-control-allow-credentials"), null, "public token/metadata CORS grants no cookie authority");
  };
async function authorizationCode(discovery, clientId, scopes, useSessionCookie) {
  const verifier = randomBytes(48).toString("base64url");
  const challenge = createHash("sha256").update(verifier).digest("base64url");
  const state = randomBytes(20).toString("base64url");
  const nonce = randomBytes(20).toString("base64url");
  const authorizeURL = new URL(discovery.authorization_endpoint);
  authorizeURL.search = new URLSearchParams({
    response_type: "code",
    client_id: clientId,
    redirect_uri: callback,
    scope: scopes,
    state,
    nonce,
    code_challenge: challenge,
    code_challenge_method: "S256",
    resource,
  }).toString();

  if (!useSessionCookie) cookies.clear();
  const authorizeResponse = await globalThis.fetch(authorizeURL, { redirect: "manual", headers: { accept: "text/html", "sec-fetch-mode": "navigate" } });
  let redirectLocation = authorizeResponse.headers.get("location");
  if (!redirectLocation && authorizeResponse.status === 200) {
    const navigation = await authorizeResponse.json();
    assert.equal(navigation.redirect, true, "OAuth fetch response declares navigation");
    assert.equal(typeof navigation.url, "string", "OAuth fetch response returns a navigation URL");
    redirectLocation = navigation.url;
  }
  assert.ok(redirectLocation, `authorization endpoint should redirect to login/consent, received ${authorizeResponse.status}`);
  activeURL = new URL(redirectLocation, baseURL);

  if (!useSessionCookie) {
    assert.equal(activeURL.pathname, "/sign-in", "unauthenticated authorization reaches CAKE ID sign-in");
    const signedIn = await authClient.signIn.email({ email: testAccount.email, password: testAccount.password });
    assert.equal(signedIn.error, null, `password login failed: ${JSON.stringify(signedIn.error)}`);
    assert.ok(cookies.size > 0, "password login established a server session cookie");
    const continuationURL = signedIn.data?.url;
    if (continuationURL) activeURL = new URL(continuationURL, baseURL);
    else if (activeURL.pathname === "/sign-in") {
      const session = await response(`${authPath}/get-session`);
      assert.equal(session.status, 200, "login session is available");
    }
  }

  if (activeURL.origin === browserOrigin) {
    assert.equal(activeURL.pathname, "/callback", "remembered consent uses the registered callback");
    assert.equal(activeURL.searchParams.get("state"), state, "remembered consent preserves OAuth state");
    assert.ok(activeURL.searchParams.get("code"), "remembered consent returns an authorization code");
    return { code: activeURL.searchParams.get("code"), verifier, state, nonce, callbackURL: callback };
  }
  if (activeURL.pathname !== "/consent") {
    const continuation = await globalThis.fetch(activeURL, { redirect: "manual", headers: { accept: "text/html", "sec-fetch-mode": "navigate" } });
    const next = continuation.headers.get("location");
    if (next) activeURL = new URL(next, baseURL);
  }
  assert.equal(activeURL.pathname, "/consent", `authorization flow should arrive at the consent screen; got ${activeURL.pathname}`);
  const consentPage = await globalThis.fetch(activeURL, { redirect: "manual", headers: { accept: "text/html", "sec-fetch-mode": "navigate" } });
  assert.equal(consentPage.status, 200, "consent screen verifies the signed OAuth query");
  const consentHtml = await consentPage.text();
  assert.match(consentHtml, /Authorize application/);

  const consent = await authClient.oauth2.consent({ accept: true });
  assert.equal(consent.error, null, `OAuth consent failed: ${JSON.stringify(consent.error)}`);
  if (consent.data?.url) activeURL = new URL(consent.data.url, baseURL);
  else if (consent.data?.redirect && activeURL.pathname === "/consent") {
    const consentResponse = await globalThis.fetch(activeURL, { redirect: "manual", headers: { accept: "text/html", "sec-fetch-mode": "navigate" } });
    const location = consentResponse.headers.get("location");
    if (location) activeURL = new URL(location, baseURL);
  }

  const arrivedCallback = activeURL;
  assert.equal(arrivedCallback.origin, browserOrigin, "successful consent redirects to the registered callback");
  assert.equal(arrivedCallback.searchParams.get("state"), state, "OAuth state is returned unchanged");
  assert.ok(arrivedCallback.searchParams.get("code"), "authorization code is returned");
  return { code: arrivedCallback.searchParams.get("code"), verifier, state, nonce, callbackURL: callback };
}

  try {
    const metadata = await publicRequest(`${authPath}/.well-known/openid-configuration`);
    assert.equal(metadata.status, 200);
    cors(metadata);
    const actualDiscovery = await metadata.json();
    assert.equal(actualDiscovery.issuer, discovery.issuer);
    const keysResponse = await publicRequest(new URL(discovery.jwks_uri).pathname);
    assert.equal(keysResponse.status, 200); cors(keysResponse);
    const keys = await keysResponse.json();
    assert.ok(keys.keys.length > 0);
    for (const pathname of [`${authPath}/.well-known/openid-configuration`, new URL(discovery.jwks_uri).pathname]) {
      const foreign = await publicRequest(pathname, {}, "https://wrong.example.test");
      assert.equal(foreign.headers.get("access-control-allow-origin"), null);
      assert.ok(foreign.headers.get("vary")?.toLowerCase().includes("origin"));
      const preflight = await publicRequest(pathname, { method: "OPTIONS", headers: { "Access-Control-Request-Method": "GET" } });
      assert.equal(preflight.status, 204); cors(preflight);
      assert.equal(preflight.headers.get("access-control-allow-methods"), "GET, OPTIONS");
    }
    for (const pathname of [`${authPath}/jwks/extra`, `${authPath}/oauth2/token/extra`, `${authPath}/get-session`]) {
      const privateRoute = await publicRequest(pathname);
      assert.equal(privateRoute.headers.get("access-control-allow-origin"), null, "public CORS is exact-path only");
    }
    for (const opaque of ["null", "*"]) {
      const untrusted = await publicRequest(`${authPath}/.well-known/openid-configuration`, {}, opaque);
      assert.equal(untrusted.headers.get("access-control-allow-origin"), null);
    }
    const tokenPath = new URL(discovery.token_endpoint).pathname;
    const preflight = await publicRequest(tokenPath, { method: "OPTIONS", headers: {
      "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "content-type" } });
    assert.equal(preflight.status, 204); cors(preflight);
    assert.equal(preflight.headers.get("access-control-allow-methods"), "POST, OPTIONS");
    for (const [origin, headers] of [["https://wrong.example.test", { "Access-Control-Request-Method": "POST" }],
      [browserOrigin, { "Access-Control-Request-Method": "DELETE" }],
      [browserOrigin, { "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "authorization" }]]) {
      assert.equal((await publicRequest(tokenPath, { method: "OPTIONS", headers }, origin)).status, 403);
    }
    const invalidForm = new URLSearchParams({ grant_type: "authorization_code", client_id: clientId,
      code: "invalid-isolated-fixture-code", code_verifier: randomBytes(48).toString("base64url"), redirect_uri: callback, resource });
    const invalid = await publicRequest(tokenPath, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: invalidForm });
    assert.equal(invalid.status, 400); cors(invalid);
    assert.equal((await invalid.json()).error, "invalid_grant", "real library invalid-code error remains intact");
    const foreignToken = await publicRequest(tokenPath, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: invalidForm }, "https://wrong.example.test");
    assert.equal(foreignToken.status, 403);
    assert.equal(foreignToken.headers.get("access-control-allow-origin"), null);
    const scopes = "openid profile email cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke";
    const flow = await authorizationCode(discovery, clientId, scopes, false);
    const exchange = async (code, verifier) => publicRequest(tokenPath, { method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: new URLSearchParams({
        grant_type: "authorization_code", client_id: clientId, code, code_verifier: verifier, redirect_uri: callback, resource }) });
    const wrongPkce = await exchange(flow.code, randomBytes(48).toString("base64url"));
    assert.equal(wrongPkce.status, 401); cors(wrongPkce);
    assert.equal((await exchange(flow.code, flow.verifier)).status, 400, "wrong verifier consumed code remains consumed");
    const fresh = await authorizationCode(discovery, clientId, scopes, true);
    const exchanged = await exchange(fresh.code, fresh.verifier);
    assert.equal(exchanged.status, 200); cors(exchanged);
    const tokens = await exchanged.json();
    const jwks = createLocalJWKSet(keys);
    const { payload: id } = await jwtVerify(tokens.id_token, jwks, { issuer: discovery.issuer, audience: clientId });
    const { payload: access } = await jwtVerify(tokens.access_token, jwks, { issuer: discovery.issuer, audience: resource });
    assert.equal(id.sub, testAccount.id); assert.equal(access.sub, testAccount.id);
    assert.equal(id.nonce, fresh.nonce); assert.ok(access.sid);
    const api = (pathname, init = {}, origin = browserOrigin) => publicRequest(pathname, { ...init,
      headers: { ...init.headers, Authorization: `Bearer ${tokens.access_token}` } }, origin);
    const current = await api("/api/account/current");
    assert.equal(current.status, 200); assert.equal(current.headers.get("access-control-allow-origin"), browserOrigin);
    assert.equal((await current.json()).accountId, testAccount.id);
    const profile = await api("/api/account/profile"); assert.equal(profile.status, 200);
    assert.equal((await profile.json()).profile.accountId, testAccount.id);
    const sessions = await api("/api/account/sessions"); assert.equal(sessions.status, 200);
    assert.ok((await sessions.json()).sessions.some(row => row.sessionId === access.sid && row.accountId === testAccount.id));
    const wrongAudience = await publicRequest("/api/account/current", { headers: { Authorization: `Bearer ${tokens.id_token}` } });
    assert.equal(wrongAudience.status, 401, "genuine public-client ID token is not a resource access token");
    assert.equal(wrongAudience.headers.get("access-control-allow-origin"), browserOrigin);
    const readOnlyFlow = await authorizationCode(discovery, clientId, "openid cake:account:read", true);
    const readOnlyExchange = await exchange(readOnlyFlow.code, readOnlyFlow.verifier);
    assert.equal(readOnlyExchange.status, 200); cors(readOnlyExchange);
    const readOnlyTokens = await readOnlyExchange.json();
    const insufficient = await publicRequest("/api/account/profile", { headers: { Authorization: `Bearer ${readOnlyTokens.access_token}` } });
    assert.equal(insufficient.status, 401, "real resource token without profile scope remains denied");
    assert.equal(insufficient.headers.get("access-control-allow-origin"), browserOrigin);
    const deniedAccount = await publicRequest("/api/account/current");
    assert.equal(deniedAccount.status, 401); assert.equal(deniedAccount.headers.get("access-control-allow-origin"), browserOrigin);
    const foreignApi = await api("/api/account/current", {}, "https://wrong.example.test");
    assert.equal(foreignApi.headers.get("access-control-allow-origin"), null);
    assert.equal((await publicRequest("/api/account/current", { method: "OPTIONS", headers: { "Access-Control-Request-Method": "GET" } }, "https://wrong.example.test")).status, 403);
    const cookie = [...cookies].map(([name, value]) => `${name}=${value}`).join("; ");
    assert.ok(cookie);
    const cookieToken = await publicRequest(tokenPath, { method: "POST", headers: { Cookie: cookie,
      "Content-Type": "application/x-www-form-urlencoded" }, body: invalidForm });
    assert.equal(cookieToken.status, 403, "cookie-bearing public-origin token preserves library CSRF rejection"); cors(cookieToken);
    assert.equal((await cookieToken.json()).code, "INVALID_ORIGIN");
    for (const includeCookie of [false, true]) {
      const login = await publicRequest(`${authPath}/sign-in/email`, { method: "POST",
        headers: { "Content-Type": "application/json", ...(includeCookie ? { Cookie: cookie } : {}) },
        body: JSON.stringify({ email: testAccount.email, password: testAccount.password }) });
      assert.equal(login.status, 403, "login-cookie/first-login CSRF remains issuer-origin only");
      assert.equal(login.headers.get("access-control-allow-origin"), null);
      assert.equal((await login.json()).code, "INVALID_ORIGIN");
    }
    const revoked = await api("/api/account/signout", { method: "POST" });
    assert.equal(revoked.status, 204);
    const old = await api("/api/account/current");
    assert.equal(old.status, 401); assert.equal(old.headers.get("access-control-allow-origin"), browserOrigin);
    assert.equal((await old.json()).error, "session_revoked_or_expired");
    console.log(JSON.stringify({ result: "public_cors_passed", assertionsExecuted: count,
      environment: "actual local Workerd/D1/Better Auth OAuth, Node transport only; no full browser journey",
      issuer: discovery.issuer, apiResource: resource, consumerOrigin: browserOrigin }));
  } finally { globalThis.window = savedWindow; }
}
