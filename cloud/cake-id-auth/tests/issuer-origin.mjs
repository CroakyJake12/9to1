import nodeAssert from "node:assert/strict";
import { randomBytes, createHash } from "node:crypto";
import { createLocalJWKSet, jwtVerify } from "jose";

// Actual maintained fixture/library routes only; no token construction or response doubles.
export async function testIssuerOrigin(ctx) {
  const { authClient, response, realFetch, cookies, baseURL, authPath,
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
    const statuses = [];
    for (const withCookie of [false, true]) {
      const reply = await realFetch(discovery.token_endpoint, { method: "POST", credentials: "omit",
        headers: { origin: baseURL, "content-type": "application/x-www-form-urlencoded",
          ...(withCookie ? { cookie: [...cookies].map(([name,value]) => `${name}=${value}`).join("; ") } : {}) },
        body: new URLSearchParams({ grant_type: "authorization_code", code: "invalid-local-code", client_id: clientId,
          redirect_uri: callback, code_verifier: "invalid-local-verifier", resource }) });
      const body = await reply.json();
      statuses.push({ withCookie, status: reply.status, error: body.error, acao: reply.headers.get("access-control-allow-origin") });
    }
    console.log(JSON.stringify({ case: "issuer-origin-only-client-allowlist", actual: statuses }));
    for (const actual of statuses) {
      assert.equal(actual.status, 400, "same-origin request reaches maintained OAuth invalid-code validation");
      assert.equal(actual.error, "invalid_grant");
      assert.equal(actual.acao, null, "issuer origin needs no cross-origin CORS grant");
    }
    const grant = await authorizationCode(discovery, clientId, "openid cake:account:read", false);
    const tokenReply = await globalThis.fetch(discovery.token_endpoint, { method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ grant_type: "authorization_code", code: grant.code, client_id: clientId,
        redirect_uri: callback, code_verifier: grant.verifier, resource }) });
    assert.equal(tokenReply.status, 200, "same-origin cookie-bearing genuine S256 exchange succeeds");
    assert.equal(tokenReply.headers.get("access-control-allow-origin"), null);
    const token = await tokenReply.json();
    const keys = await (await realFetch(discovery.jwks_uri)).json();
    const access = (await jwtVerify(token.access_token, createLocalJWKSet(keys), { issuer: discovery.issuer, audience: resource })).payload;
    assert.equal(access.sub, testAccount.accountId);
    assert.ok(access.sid);
    const current = await response("/api/account/current", { headers: { authorization: `Bearer ${token.access_token}` } });
    assert.equal(current.status, 200);
    assert.equal((await current.json()).sessionId, access.sid);
    console.log(JSON.stringify({ result: "issuer_origin_passed", assertionsExecuted: count,
      environment: "actual Workerd/D1 with only client origin configured; genuine issuer-origin cookie/PKCE preserved" }));
  } finally { globalThis.window = savedWindow; }
}
