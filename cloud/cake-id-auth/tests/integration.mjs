import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { randomBytes, createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createAuthClient } from "better-auth/client";
import { oauthProviderResourceClient } from "@better-auth/oauth-provider/resource-client";
import { oauthProviderClient } from "@better-auth/oauth-provider/client";
import { createRemoteJWKSet, jwtVerify } from "jose";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

if (process.platform === "win32" && process.arch === "arm64") {
  console.error("SKIP: the installed Cloudflare Workerd runtime has no native Windows ARM64 build. Its x64-emulated Worker starts here, but D1 calls fail inside the runtime before auth flows run. No test secrets or database state were created. Run this integration suite on a supported native Workerd host.");
  process.exit(77);
}

const varsPath = path.join(root, ".dev.vars");
const runId = randomBytes(8).toString("hex");
const persistPath = path.join(root, ".local-run", runId);
const baseURL = "http://127.0.0.1:8798";
const authPath = "/api/auth";
const resource = "http://127.0.0.1:5095";
const testKey = randomBytes(32).toString("base64url");
const devVars = [
  `AUTH_SECRET=${randomBytes(48).toString("base64url")}`,
  `LOGIN_LIMITER_KEY=${randomBytes(48).toString("base64url")}`,
  `LOCAL_TEST_KEY=${testKey}`,
].join("\n") + "\n";

if (existsSync(varsPath)) {
  throw new Error("Refusing to overwrite an existing local .dev.vars file. Move it temporarily, then rerun this isolated test.");
}

mkdirSync(persistPath, { recursive: true });
writeFileSync(varsPath, devVars, { flag: "wx", mode: 0o600 });

let worker;
let serverOutput = "";
let activeURL = new URL(baseURL);
const cookies = new Map();
const realFetch = globalThis.fetch.bind(globalThis);

async function waitForWorkerExit(timeoutMs = 5000) {
  if (!worker || worker.exitCode !== null) return true;
  return Promise.race([
    new Promise((resolve) => worker.once("exit", () => resolve(true))),
    new Promise((resolve) => setTimeout(() => resolve(false), timeoutMs)),
  ]);
}

async function stopWorker() {
  if (!worker || worker.exitCode !== null) return;
  worker.kill("SIGINT");
  if (await waitForWorkerExit()) return;
  if (process.platform === "win32" && worker.pid) {
    const killer = spawn("taskkill", ["/PID", String(worker.pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" });
    await new Promise((resolve) => killer.once("exit", resolve));
    await waitForWorkerExit();
  } else {
    worker.kill("SIGKILL");
    await waitForWorkerExit();
  }
}

const browserLocation = {
  get href() { return activeURL.href; },
  set href(value) { activeURL = new URL(value, baseURL); },
  get search() { return activeURL.search; },
  get pathname() { return activeURL.pathname; },
  assign(value) { activeURL = new URL(value, baseURL); },
};
globalThis.window = { location: browserLocation };

function updateCookies(response) {
  const setCookies = response.headers.getSetCookie?.() ?? [response.headers.get("set-cookie")].filter(Boolean);
  for (const setCookie of setCookies) {
    const pair = setCookie.split(";", 1)[0];
    const separator = pair.indexOf("=");
    if (separator < 1) continue;
    const name = pair.slice(0, separator);
    if (/max-age=0/i.test(setCookie) || /expires=Thu, 01 Jan 1970/i.test(setCookie)) cookies.delete(name);
    else cookies.set(name, pair.slice(separator + 1));
  }
}

globalThis.fetch = async (input, init = {}) => {
  const incoming = input instanceof Request ? input : new Request(input, init);
  const headers = new Headers(incoming.headers);
  if (new URL(incoming.url).origin === baseURL) {
    if (cookies.size) headers.set("cookie", [...cookies].map(([name, value]) => `${name}=${value}`).join("; "));
    headers.set("origin", baseURL);
  }
  const request = new Request(incoming, { ...init, headers, redirect: "manual" });
  const response = await realFetch(request);
  updateCookies(response);
  return response;
};

const authClient = createAuthClient({ baseURL: `${baseURL}${authPath}`, fetchOptions: { customFetchImpl: globalThis.fetch }, plugins: [oauthProviderClient()] });

async function waitForWorker() {
  for (let attempt = 0; attempt < 20; attempt++) {
    if (worker.exitCode !== null) throw new Error(`Wrangler exited before readiness.\n${serverOutput.slice(-12000)}`);
    try {
      const response = await globalThis.fetch(`${baseURL}/__test/health`, { headers: { "x-local-test-key": testKey }, signal: AbortSignal.timeout(1500) });
      const body = await response.json();
      if (response.ok && body.ready === true) return;
      if (response.status >= 500) throw new Error(`Local worker returned ${response.status}: ${JSON.stringify(body)}\n${serverOutput.slice(-12000)}`);
    } catch (error) {
      if (error.message?.includes("Local worker returned")) throw error;
    }
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  throw new Error(`Local Worker did not become ready.\n${serverOutput.slice(-12000)}`);
}

async function response(pathname, options = {}) {
  return globalThis.fetch(new URL(pathname, baseURL), options);
}

async function jsonRequest(pathname, method, body, headers = {}) {
  return response(pathname, {
    method,
    headers: { "content-type": "application/json", ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

async function createSyntheticAccount(prefix, username, password) {
  const email = `${prefix}-${runId}@example.test`;
  const result = await authClient.$fetch("/sign-up/email", {
    method: "POST",
    body: { name: `Synthetic ${prefix}`, username, email, password },
  });
  assert.equal(result.error, null, `Synthetic registration failed: ${JSON.stringify(result.error)}`);
  const user = result.data?.user;
  assert.ok(user?.id, "registration returned a canonical user id");
  assert.match(user.id, /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i, "user ID is a UUID");

  const outboxResponse = await response(`/__test/outbox?recipient=${encodeURIComponent(email)}`, { headers: { "x-local-test-key": testKey } });
  assert.equal(outboxResponse.status, 200, "local email capture is reachable");
  const outbox = await outboxResponse.json();
  const verification = outbox.messages.find((message) => message.subject.includes("Verify"));
  assert.ok(verification, "verification email callback wrote to the local capture sink");
  const verificationURL = verification.body.match(/https?:\/\/[^\s]+/)?.[0];
  assert.ok(verificationURL, "verification message contains a link");
  const verificationResponse = await globalThis.fetch(verificationURL, { redirect: "manual" });
  assert.ok([200, 302, 303].includes(verificationResponse.status), `verification route responded: ${verificationResponse.status}`);
  return { id: user.id, email, username, password };
}

async function currentDiscovery() {
  for (const pathName of [`${authPath}/.well-known/openid-configuration`, "/.well-known/openid-configuration"]) {
    const result = await response(pathName);
    if (result.ok) return result.json();
  }
  throw new Error("OpenID discovery was not returned at the issuer or auth path");
}

function admitOAuthRedirectURL(value) {
  assert.equal(typeof value, "string");
  assert.ok(value.length > 0 && value.length <= 16384, "OAuth redirect URL is bounded and nonempty");
  const target = new URL(value, baseURL);
  assert.ok([baseURL, "http://127.0.0.1:5096"].includes(target.origin), "OAuth redirect remains on exact issuer or registered callback");
  assert.equal(target.username, "");
  assert.equal(target.password, "");
  return target.href;
}

async function readOAuthRedirect(result, required = true) {
  const location = result.headers.get("location");
  if ([302, 303].includes(result.status)) {
    assert.ok(location, "OAuth HTTP redirect requires Location");
    return admitOAuthRedirectURL(location);
  }
  assert.equal(location, null, "OAuth JSON or document response has no HTTP Location");
  const mediaType = result.headers.get("content-type")?.split(";", 1)[0].trim().toLowerCase();
  if (result.status === 200 && mediaType === "application/json") {
    const payload = await result.json();
    assert.deepEqual(Object.keys(payload).sort(), ["redirect", "url"], "OAuth fetch redirect has the maintained exact JSON shape");
    assert.equal(payload.redirect, true, "OAuth fetch response explicitly requests redirect");
    return admitOAuthRedirectURL(payload.url);
  }
  assert.ok(!required, `OAuth endpoint requires an actual HTTP or maintained JSON redirect; received ${result.status}`);
  assert.equal(result.status, 200, "optional continuation is a successful actual document");
  assert.equal(mediaType, "text/html", "optional nonredirect continuation is actual HTML");
  return null;
}

async function authorizationCode(discovery, clientId, scopes, useSessionCookie) {
  const verifier = randomBytes(48).toString("base64url");
  const challenge = createHash("sha256").update(verifier).digest("base64url");
  const state = randomBytes(20).toString("base64url");
  const nonce = randomBytes(20).toString("base64url");
  const authorizeURL = new URL(discovery.authorization_endpoint);
  authorizeURL.search = new URLSearchParams({
    response_type: "code",
    client_id: clientId,
    redirect_uri: "http://127.0.0.1:5096/callback",
    scope: scopes,
    state,
    nonce,
    code_challenge: challenge,
    code_challenge_method: "S256",
    resource,
  }).toString();

  if (!useSessionCookie) cookies.clear();
  const authorizeResponse = await globalThis.fetch(authorizeURL, { redirect: "manual" });
  const redirectLocation = await readOAuthRedirect(authorizeResponse);
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

  if (activeURL.pathname !== "/consent") {
    const continuation = await globalThis.fetch(activeURL, { redirect: "manual" });
    const next = await readOAuthRedirect(continuation, false);
    if (next) activeURL = new URL(next, baseURL);
  }
  assert.equal(activeURL.pathname, "/consent", `authorization flow should arrive at the consent screen; got ${activeURL.pathname}`);
  const consentPage = await globalThis.fetch(activeURL, { redirect: "manual" });
  assert.equal(consentPage.status, 200, "consent screen verifies the signed OAuth query");
  const consentHtml = await consentPage.text();
  assert.match(consentHtml, /Authorize application/);

  const consent = await authClient.oauth2.consent({ accept: true });
  assert.equal(consent.error, null, `OAuth consent failed: ${JSON.stringify(consent.error)}`);
  if (consent.data?.url) activeURL = new URL(consent.data.url, baseURL);
  else if (consent.data?.redirect && activeURL.pathname === "/consent") {
    const consentResponse = await globalThis.fetch(activeURL, { redirect: "manual" });
    const location = await readOAuthRedirect(consentResponse);
    if (location) activeURL = new URL(location, baseURL);
  }

  const callback = activeURL;
  assert.equal(callback.origin, "http://127.0.0.1:5096", "successful consent redirects to the registered callback");
  assert.equal(callback.searchParams.get("state"), state, "OAuth state is returned unchanged");
  assert.ok(callback.searchParams.get("code"), "authorization code is returned");
  return { code: callback.searchParams.get("code"), verifier, state, nonce, callbackURL: "http://127.0.0.1:5096/callback" };
}

async function exchangeCode(discovery, clientId, code, verifier, expectedStatus = 200) {
  const body = new URLSearchParams({
    grant_type: "authorization_code",
    code,
    redirect_uri: "http://127.0.0.1:5096/callback",
    client_id: clientId,
    code_verifier: verifier,
    resource,
  });
  const result = await globalThis.fetch(discovery.token_endpoint, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded", origin: baseURL },
    body,
    redirect: "manual",
  });
  assert.equal(result.status, expectedStatus, `token endpoint status: ${await result.clone().text()}`);
  return result.json();
}

let testAccount;
let durationMs = {};
try {
  const wrangler = path.join(root, "node_modules", "wrangler", "bin", "wrangler.js");
  worker = spawn(process.execPath, [
    wrangler, "dev", "--local", "--config", "wrangler.local.jsonc", "--ip", "127.0.0.1", "--port", "8798", "--persist-to", persistPath,
  ], { cwd: root, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
  worker.stdout.setEncoding("utf8").on("data", (chunk) => { serverOutput = (serverOutput + chunk).slice(-16000); });
  worker.stderr.setEncoding("utf8").on("data", (chunk) => { serverOutput = (serverOutput + chunk).slice(-16000); });
  await waitForWorker();

  const schema = await response("/__test/schema", { headers: { "x-local-test-key": testKey } });
  assert.equal(schema.status, 200, "actual Better Auth D1 schema was generated");
  const schemaSql = await schema.text();
  assert.match(schemaSql, /oauth/i, "OAuth provider tables are included in generated D1 schema");
  assert.match(schemaSql, /jwks/i, "signing key table is included in generated D1 schema");

  const loginPage = await response("/sign-in");
  assert.equal(loginPage.status, 200);
  assert.match(await loginPage.text(), /id="sign-in-form"/);
  const browserBundle = await response("/assets/auth-ui.js");
  assert.equal(browserBundle.status, 200);
  assert.match(await browserBundle.text(), /dataset\.busy|\.disabled/);

  testAccount = await createSyntheticAccount("auth-main", `synthetic_${runId}`, "Test-passphrase-9!NoSharedAccount");
  const reserved = await authClient.$fetch("/sign-up/email", {
    method: "POST",
    body: { name: "Reserved handle test", username: "CroakyJake", email: `reserved-${runId}@example.test`, password: "Test-passphrase-9!NoSharedAccount" },
  });
  assert.ok(reserved.error, "reserved croakyjake username cannot be claimed");

  const ordinaryLogin = await authClient.signIn.email({ email: testAccount.email, password: testAccount.password });
  assert.equal(ordinaryLogin.error, null, "verified ordinary synthetic account has a genuine session");
  const ordinarySessionResponse = await response(`${authPath}/get-session`);
  assert.equal(ordinarySessionResponse.status, 200);
  const ordinarySession = await ordinarySessionResponse.json();
  assert.equal(ordinarySession.user.id, testAccount.id);
  assert.equal(Object.hasOwn(ordinarySession.user, "role"), false, "ordinary private role is not exposed");
  const ordinaryClient = await jsonRequest(`${authPath}/oauth2/create-client`, "POST", {
    client_name: "denied ordinary synthetic client",
    redirect_uris: ["http://127.0.0.1:5096/callback"],
    token_endpoint_auth_method: "none", application_type: "native",
    grant_types: ["authorization_code", "refresh_token"], response_types: ["code"], scope: "openid profile email",
  });
  assert.equal(ordinaryClient.status, 401, "genuine ordinary session cannot provision OAuth clients");
  const ordinaryResource = await response("/__test/resource-privilege", { headers: { "x-local-test-key": testKey } });
  assert.equal(ordinaryResource.status, 401, "real ordinary session is denied by maintained resource privilege API");


  const promoted = await jsonRequest("/__test/promote-admin", "POST", { userId: testAccount.id, email: testAccount.email }, { "x-local-test-key": testKey });
  assert.equal(promoted.status, 200);
  assert.equal((await promoted.json()).promoted, true, "only verified synthetic local identity was promoted for test client provisioning");

  const startHash = performance.now();
  const resetRequest = await authClient.requestPasswordReset({ email: testAccount.email, redirectTo: `${baseURL}/reset-password` });
  assert.equal(resetRequest.error, null, "password reset request accepted");
  const resetOutbox = await (await response(`/__test/outbox?recipient=${encodeURIComponent(testAccount.email)}`, { headers: { "x-local-test-key": testKey } })).json();
  const resetMessage = resetOutbox.messages.find((message) => message.subject.includes("Reset"));
  assert.ok(resetMessage, "password reset callback wrote to the local capture sink");
  const resetURL = new URL(resetMessage.body.match(/https?:\/\/[^\s]+/)?.[0]);
  assert.equal(resetURL.origin, new URL(baseURL).origin, "reset email points to the exact local issuer");
  assert.ok(resetURL.pathname.startsWith(`${authPath}/reset-password/`), "reset email uses the maintained token-validation route");
  assert.equal(resetURL.searchParams.get("callbackURL"), `${baseURL}/reset-password`, "reset email retains the requested callback");
  const resetValidation = await realFetch(resetURL, { redirect: "manual" });
  assert.equal(resetValidation.status, 302, "reset token validation redirects to its callback");
  const resetLocation = resetValidation.headers.get("location");
  assert.ok(resetLocation, "reset validation supplies a callback location");
  const resetCallback = new URL(resetLocation, baseURL);
  assert.equal(resetCallback.origin, new URL(baseURL).origin, "validated reset callback stays on the exact local issuer");
  assert.equal(resetCallback.pathname, "/reset-password", "validated reset callback uses the requested path");
  assert.equal(resetCallback.searchParams.has("error"), false, "reset callback reports no validation error");
  assert.equal(resetCallback.searchParams.getAll("token").length, 1, "validated callback carries exactly one reset token");
  const resetToken = resetCallback.searchParams.get("token");
  assert.ok(resetToken, "validated reset callback carries a single-use token");
  const resetResponse = await jsonRequest(`${authPath}/reset-password`, "POST", { token: resetToken, newPassword: "New-test-passphrase-8!LocallyVerified" });
  assert.equal(resetResponse.status, 200, `password reset accepted: ${await resetResponse.clone().text()}`);
  const login = await authClient.signIn.email({ email: testAccount.email, password: "New-test-passphrase-8!LocallyVerified" });
  assert.equal(login.error, null, "the new password verifies through the Worker password hasher");
  durationMs.passwordResetAndLogin = Math.round(performance.now() - startHash);
  testAccount.password = "New-test-passphrase-8!LocallyVerified";

  assert.ok([...cookies.keys()].some((name) => name.endsWith("session_token")), "real sign-in supplies a session cookie to the local harness");
  const currentSessionResponse = await response(`${authPath}/get-session`);
  assert.equal(currentSessionResponse.status, 200, "real session endpoint accepts the cookie harness");
  const currentSession = await currentSessionResponse.json();
  assert.ok(currentSession?.session?.id, "real current session exists before privileged client registration");
  assert.equal(currentSession.user.id, testAccount.id, "real session belongs to the verified synthetic account");
  assert.equal(Object.hasOwn(currentSession.user, "role"), false, "private role remains excluded from public session output");

  const adminResource = await response("/__test/resource-privilege", { headers: { "x-local-test-key": testKey } });
  assert.equal(adminResource.status, 200, "real promoted session is admitted by maintained resource privilege API");
  assert.equal((await adminResource.json()).authorized, true);

  const clientResponse = await jsonRequest(`${authPath}/oauth2/create-client`, "POST", {
    client_name: "9to1 local integration test",
    redirect_uris: ["http://127.0.0.1:5096/callback"],
    token_endpoint_auth_method: "none",
    application_type: "native",
    grant_types: ["authorization_code", "refresh_token"],
    response_types: ["code"],
    scope: "openid profile email cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke",
    skip_consent: false,
    require_pkce: true,
  });
  assert.equal(clientResponse.status, 201, `public client registration failed: ${await clientResponse.clone().text()}`);
  const client = await clientResponse.json();
  assert.ok(client.client_id, "public client has a registration ID");
  assert.equal(client.token_endpoint_auth_method, "none", "client is public and holds no secret");
  assert.equal(client.application_type, "native", "exact loopback redirect exercises a native public PKCE client, not web HTTPS integration");

  const discovery = await currentDiscovery();
  assert.ok(discovery.issuer && discovery.authorization_endpoint && discovery.token_endpoint && discovery.jwks_uri, "local discovery supplies actual issuer and endpoints");
  assert.equal(discovery.registration_endpoint, undefined, "Dynamic Client Registration is not published");

  // Exercise Better Auth's client hook from the unauthenticated authorization redirect.
  const profileFlow = await authorizationCode(discovery, client.client_id, "openid profile email cake:account:read cake:profile:read", false);
  const startedExchange = performance.now();
  const wrongVerifier = await exchangeCode(discovery, client.client_id, profileFlow.code, randomBytes(32).toString("base64url"), 400);
  assert.ok(wrongVerifier.error, "wrong PKCE verifier is rejected");
  const profileToken = await exchangeCode(discovery, client.client_id, profileFlow.code, profileFlow.verifier);
  durationMs.passwordLoginAndTokenExchange = Math.round(performance.now() - startedExchange);
  assert.ok(profileToken.access_token && profileToken.id_token, "authorization code produced OIDC and API tokens");

  const jwks = createRemoteJWKSet(new URL(discovery.jwks_uri));
  const { payload: idClaims } = await jwtVerify(profileToken.id_token, jwks, { issuer: discovery.issuer, audience: client.client_id });
  assert.match(String(idClaims.sub), /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i, "ID token subject is canonical UUID");
  assert.ok(typeof idClaims.sid === "string" && idClaims.sid !== idClaims.sub, "ID token sid is an independent session identifier");
  const { payload: accessClaims } = await jwtVerify(profileToken.access_token, jwks, { issuer: discovery.issuer, audience: resource });
  assert.equal(accessClaims.sub, idClaims.sub, "API token identifies the same account");
  assert.ok(typeof accessClaims.sid === "string" && accessClaims.sid.length > 0, "API token has an independent session id");
  assert.match(String(accessClaims.scope), /cake:profile:read/, "API token carries the explicit requested scope");
  assert.doesNotMatch(String(accessClaims.scope), /business|entitlement|role/i, "tokens do not embed plans or roles");

  const current = await response("/api/account/current", { headers: { authorization: `Bearer ${profileToken.access_token}` } });
  assert.equal(current.status, 200, `current account API matches the 9to1 contract: ${await current.clone().text()}`);
  assert.equal((await current.json()).accountId, testAccount.id, "current account binds to canonical token subject");
  const profileRead = await response("/api/account/profile", { headers: { authorization: `Bearer ${profileToken.access_token}` } });
  assert.equal(profileRead.status, 200, `authorized profile read: ${await profileRead.clone().text()}`);
  const profileBody = await profileRead.json();
  assert.equal(profileBody.profile.accountId, testAccount.id, "resource route binds token subject to its own account row");
  assert.equal(profileBody.profile.revision, 1, "new profile begins with a server-owned revision");
  const deniedProfileWrite = await jsonRequest("/api/account/profile", "PATCH", { expectedRevision: 1, fields: { name: "Must not update" } }, { authorization: `Bearer ${profileToken.access_token}` });
  assert.equal(deniedProfileWrite.status, 401, "resource profile update requires its distinct write scope");
  const profileWriteFlow = await authorizationCode(discovery, client.client_id, "openid cake:profile:write", true);
  const profileWriteToken = await exchangeCode(discovery, client.client_id, profileWriteFlow.code, profileWriteFlow.verifier);
  const profileWrite = await jsonRequest("/api/account/profile", "PATCH", { expectedRevision: 1, fields: { name: "Updated synthetic profile" } }, { authorization: `Bearer ${profileWriteToken.access_token}` });
  assert.equal(profileWrite.status, 200, `resource profile update succeeds under its own bearer token: ${await profileWrite.clone().text()}`);
  const updatedProfile = (await profileWrite.json()).profile;
  assert.equal(updatedProfile.name, "Updated synthetic profile", "resource profile update binds to the token subject without requiring a browser cookie");
  assert.equal(updatedProfile.revision, 2, "profile mutation advances its server revision");
  const staleWrite = await jsonRequest("/api/account/profile", "PATCH", { expectedRevision: 1, fields: { name: "Stale update" } }, { authorization: `Bearer ${profileWriteToken.access_token}` });
  assert.equal(staleWrite.status, 409, "profile compare-and-swap rejects a stale revision");
  const missingScope = await response("/api/account/sessions", { headers: { authorization: `Bearer ${profileToken.access_token}` } });
  assert.equal(missingScope.status, 401, "server refuses a route without its required OAuth scope");
  const standaloneVerifier = oauthProviderResourceClient();
  await assert.rejects(
    standaloneVerifier.getActions().verifyAccessTokenRequest(new Request(`${baseURL}/api/account/profile`, { headers: { authorization: `Bearer ${profileToken.access_token}` } }), {
      verifyOptions: { audience: "https://wrong.example/api", issuer: discovery.issuer },
      jwksUrl: discovery.jwks_uri,
    }),
    "resource verifier rejects a correctly signed token for another audience",
  );

  const sessionsFlow = await authorizationCode(discovery, client.client_id, "openid profile cake:sessions:read cake:sessions:revoke", true);
  const sessionToken = await exchangeCode(discovery, client.client_id, sessionsFlow.code, sessionsFlow.verifier);
  const sessions = await response("/api/account/sessions", { headers: { authorization: `Bearer ${sessionToken.access_token}` } });
  assert.equal(sessions.status, 200, `session listing checks its scope: ${await sessions.clone().text()}`);
  const listed = await sessions.json();
  assert.ok(listed.sessions.some((item) => item.sessionId === accessClaims.sid && item.accountId === testAccount.id), "session listing exposes the matching live session, not its secret cookie");
  const revoked = await response(`/api/account/sessions/${encodeURIComponent(String(accessClaims.sid))}`, {
    method: "DELETE",
    headers: { authorization: `Bearer ${sessionToken.access_token}` },
  });
  assert.equal(revoked.status, 204, "self-session revoke succeeds under its explicit scope");
  const afterRevocation = await response("/api/account/profile", { headers: { authorization: `Bearer ${profileToken.access_token}` } });
  assert.equal(afterRevocation.status, 401, "revoked session immediately invalidates its otherwise correctly signed access token");

  // A mismatched resource is rejected by the OAuth authorization layer before token issuance.
  const wrongResourceURL = new URL(discovery.authorization_endpoint);
  wrongResourceURL.search = new URLSearchParams({
    response_type: "code", client_id: client.client_id, redirect_uri: "http://127.0.0.1:5096/callback",
    scope: "openid cake:profile:read", state: randomBytes(16).toString("base64url"),
    code_challenge: createHash("sha256").update(randomBytes(32).toString("base64url")).digest("base64url"),
    code_challenge_method: "S256", resource: "https://wrong.example/api",
  }).toString();
  const wrongResource = await globalThis.fetch(wrongResourceURL, { redirect: "manual" });
  const wrongResourceRedirect = new URL(await readOAuthRedirect(wrongResource), baseURL);
  assert.equal(wrongResourceRedirect.origin, "http://127.0.0.1:5096");
  assert.equal(wrongResourceRedirect.pathname, "/callback");
  assert.equal(wrongResourceRedirect.searchParams.has("code"), false, "unregistered resource receives no authorization code");
  assert.equal(wrongResourceRedirect.searchParams.get("error"), "invalid_target", "real authorization rejects the unregistered resource");
  assert.equal(wrongResourceRedirect.searchParams.get("state"), wrongResourceURL.searchParams.get("state"));
  assert.equal(wrongResourceRedirect.searchParams.get("iss"), discovery.issuer);

  const limited = await createSyntheticAccount("auth-limit", `limit_${runId}`, "Test-passphrase-9!NoSharedAccount");
  for (let attempt = 1; attempt <= 8; attempt++) {
    const bad = await jsonRequest(`${authPath}/sign-in/email`, "POST", { email: limited.email, password: "incorrect password" });
    assert.notEqual(bad.status, 429, `attempt ${attempt} is not blocked before the configured threshold`);
    assert.ok([400, 401, 422].includes(bad.status), `bad password is rejected: ${bad.status}`);
  }
  const ninth = await jsonRequest(`${authPath}/sign-in/email`, "POST", { email: limited.email, password: "incorrect password" });
  assert.equal(ninth.status, 429, "ninth request is rejected by the D1 limiter before password verification");
  assert.ok(Number(ninth.headers.get("retry-after")) > 0, "limiter returns a retry delay");
  for (let attempt = 1; attempt <= 8; attempt++) {
    const bad = await jsonRequest(`${authPath}/sign-in/username`, "POST", { username: limited.username, password: "incorrect password" });
    assert.notEqual(bad.status, 429, `username attempt ${attempt} is not blocked before the configured threshold`);
    assert.ok([400, 401, 422].includes(bad.status), `username bad password is rejected: ${bad.status}`);
  }
  const ninthUsername = await jsonRequest(`${authPath}/sign-in/username`, "POST", { username: limited.username, password: "incorrect password" });
  assert.equal(ninthUsername.status, 429, "username password route also uses the D1 limiter");

  console.log(JSON.stringify({
    result: "passed",
    environment: "local Wrangler Workerd + local D1 only",
    issuer: discovery.issuer,
    authorizationEndpoint: discovery.authorization_endpoint,
    tokenEndpoint: discovery.token_endpoint,
    jwksUri: discovery.jwks_uri,
    publicClientRegistration: "synthetic and local only; client id intentionally not printed",
    publicClientApplicationType: "native (exact synthetic loopback PKCE protocol only; no web HTTPS or .NET integration claim)",
    redirectUri: "http://127.0.0.1:5096/callback (synthetic test only)",
    passwordResetAndLoginMs: durationMs.passwordResetAndLogin,
    passwordLoginAndTokenExchangeMs: durationMs.passwordLoginAndTokenExchange,
    externalEmailDelivery: "not tested; messages captured in local D1 outbox",
    cloudflareDeployment: "none",
  }, null, 2));
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
} finally {
  globalThis.fetch = realFetch;
  delete globalThis.window;
  await stopWorker();
  if (existsSync(varsPath) && readFileSync(varsPath, "utf8") === devVars) rmSync(varsPath, { force: true });
  const resolvedRunPath = path.resolve(persistPath);
  if (resolvedRunPath.startsWith(`${path.resolve(root, ".local-run")}${path.sep}`)) {
    for (let attempt = 0; attempt < 10 && existsSync(resolvedRunPath); attempt++) {
      try { rmSync(resolvedRunPath, { recursive: true, force: true }); }
      catch (error) {
        if (!error || typeof error !== "object" || !["EPERM", "EBUSY"].includes(error.code) || attempt === 9) throw error;
        await new Promise((resolve) => setTimeout(resolve, 300));
      }
    }
  }
  const parent = path.resolve(root, ".local-run");
  if (existsSync(parent)) {
    try {
      const entries = await (await import("node:fs/promises")).readdir(parent);
      // Remove only an empty parent created for this isolated run.
      if (entries.length === 0) rmSync(parent, { recursive: true, force: true });
    } catch (error) {
      if (error?.code !== "ENOENT") throw error;
    }
  }
}
