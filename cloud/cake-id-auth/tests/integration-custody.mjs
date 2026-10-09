import assert from "node:assert/strict";
import { finishIntegrationCleanup } from "./integration-lifecycle.mjs";
import { spawn, spawnSync } from "node:child_process";
import { randomBytes, createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createAuthClient } from "better-auth/client";
import { oauthProviderResourceClient } from "@better-auth/oauth-provider/resource-client";
import { oauthProviderClient } from "@better-auth/oauth-provider/client";
import { createRemoteJWKSet, jwtVerify } from "jose";

let assertionCount = 0;
for (const method of ["equal", "notEqual", "ok", "match", "doesNotMatch", "rejects"]) {
  const check = assert[method];
  assert[method] = (...args) => { assertionCount++; return check(...args); };
}

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

if (process.platform !== "linux") {
  console.error("SKIP: this custody-checked reusable fixture requires Linux /proc process-group inspection; no test secrets or database state created.");
  process.exit(77);
}

const custodianPath = path.join(root, "tests", "linux-fixture-custodian.py");
const authority = spawnSync("python3", [custodianPath, "--check"], { encoding: "utf8" });
if (authority.status !== 0) {
  console.error("HELD: Linux pidfd/subreaper authority unavailable; no fixture secrets created");
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

mkdirSync(persistPath, { recursive: true, mode: 0o700 });
writeFileSync(varsPath, devVars, { flag: "wx", mode: 0o600 });

let worker;
let workerStartupError;
let custodyReceipt = "";
let serverOutput = "";
let activeURL = new URL(baseURL);
const cookies = new Map();
let syntheticSource = 0;
const realFetch = globalThis.fetch.bind(globalThis);

async function waitForWorkerExit(timeoutMs = 5000) {
  if (!worker || worker.exitCode !== null) return true;
  return Promise.race([
    new Promise((resolve) => worker.once("exit", () => resolve(true))),
    new Promise((resolve) => setTimeout(() => resolve(false), timeoutMs)),
  ]);
}

async function stopWorker() {
  if (!worker) return;
  if (!worker.pid && workerStartupError) return; // Custodian never spawned, so no descendants exist.
  if (worker.exitCode === null) worker.stdin.end("stop\n");
  if (!(await waitForWorkerExit(15000)) || worker.exitCode !== 0) {
    throw new Error("Custodian exit/drain unproven; fixture state retained");
  }
  let receipt;
  try { receipt = JSON.parse(custodyReceipt.trim()); }
  catch { throw new Error("Custodian receipt unavailable; fixture state retained"); }
  if (receipt.strictReaped !== true || receipt.originalsDisappeared !== true) {
    throw new Error("Strict original descendant reaping unproven; fixture state retained");
  }
  console.log(JSON.stringify({ status: "drained", custody: receipt }));
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
    headers.set("cf-connecting-ip", `192.0.2.${syntheticSource || 1}`);
  }
  const request = new Request(incoming, { ...init, headers, redirect: "manual" });
  const response = await realFetch(request);
  updateCookies(response);
  return response;
};

const authClient = createAuthClient({ baseURL: `${baseURL}${authPath}`, plugins: [oauthProviderClient()] });

async function waitForWorker() {
  for (let attempt = 0; attempt < 20; attempt++) {
    if (workerStartupError) throw new Error("Custodian spawn failed before readiness");
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
  syntheticSource++;
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

async function authorizationCode(discovery, clientId, scopes, useSessionCookie) {
  const verifier = randomBytes(48).toString("base64url");
  const challenge = createHash("sha256").update(verifier).digest("base64url");
  const state = randomBytes(20).toString("base64url");
  const nonce = randomBytes(20).toString("base64url");
  const authorizeURL = new URL(discovery.authorization_endpoint);
  authorizeURL.search = new URLSearchParams({
    response_type: "code",
    client_id: clientId,
    redirect_uri: "https://client.example.test/callback",
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

  if (activeURL.origin === "https://client.example.test") {
    assert.equal(activeURL.pathname, "/callback", "remembered consent uses the registered callback");
    assert.equal(activeURL.searchParams.get("state"), state, "remembered consent preserves OAuth state");
    assert.ok(activeURL.searchParams.get("code"), "remembered consent returns an authorization code");
    return { code: activeURL.searchParams.get("code"), verifier, state, nonce, callbackURL: "https://client.example.test/callback" };
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

  const callback = activeURL;
  assert.equal(callback.origin, "https://client.example.test", "successful consent redirects to the registered callback");
  assert.equal(callback.searchParams.get("state"), state, "OAuth state is returned unchanged");
  assert.ok(callback.searchParams.get("code"), "authorization code is returned");
  return { code: callback.searchParams.get("code"), verifier, state, nonce, callbackURL: "https://client.example.test/callback" };
}

async function exchangeCode(discovery, clientId, code, verifier, expectedStatus = 200) {
  const body = new URLSearchParams({
    grant_type: "authorization_code",
    code,
    redirect_uri: "https://client.example.test/callback",
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

let mainFailure;
let testAccount;
let durationMs = {};
try {
  const wrangler = path.join(root, "node_modules", "wrangler", "bin", "wrangler.js");
  worker = spawn("python3", [custodianPath, process.execPath, wrangler, "dev", "--local", "--config", "wrangler.local.jsonc",
    "--ip", "127.0.0.1", "--port", "8798", "--persist-to", persistPath],
    { cwd: root, stdio: ["pipe", "pipe", "pipe", "pipe"] });
  worker.stdin.on("error", () => {}); // Broken control pipe is diagnosed by failed exit/receipt checks.
  worker.stdio[3].setEncoding("utf8").on("data", chunk => { custodyReceipt += chunk; });
  worker.once("error", error => { workerStartupError = error; });
  worker.stdout.setEncoding("utf8").on("data", chunk => { serverOutput = (serverOutput + chunk).slice(-16000); });
  worker.stderr.setEncoding("utf8").on("data", chunk => { serverOutput = (serverOutput + chunk).slice(-16000); });
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

  const missingUsername = await authClient.$fetch("/sign-up/email", {
    method: "POST", body: { name: "Required fields", email: `missing-${runId}@example.test`, password: "Test-passphrase-9!NoSharedAccount" },
  });
  assert.ok(missingUsername.error, "direct registration cannot omit required Username");
  const blankName = await authClient.$fetch("/sign-up/email", {
    method: "POST", body: { name: "   ", username: `blank_${runId}`, email: `blank-${runId}@example.test`, password: "Test-passphrase-9!NoSharedAccount" },
  });
  assert.ok(blankName.error, "direct registration cannot save a whitespace-only required Name");
  syntheticSource++;

  testAccount = await createSyntheticAccount("auth-main", `synthetic_${runId}`, "Test-passphrase-9!NoSharedAccount");
  const reserved = await authClient.$fetch("/sign-up/email", {
    method: "POST",
    body: { name: "Reserved handle test", username: "CroakyJake", email: `reserved-${runId}@example.test`, password: "Test-passphrase-9!NoSharedAccount" },
  });
  assert.ok(reserved.error, "reserved croakyjake username cannot be claimed");

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
  const resetCallback = await globalThis.fetch(resetURL, { redirect: "manual" });
  assert.equal(resetCallback.status, 302, "recovery email link validates its token and redirects to the approved reset screen");
  const resetLocation = resetCallback.headers.get("location");
  assert.ok(resetLocation, "recovery callback returns a reset screen location");
  const resetScreenURL = new URL(resetLocation, baseURL);
  assert.equal(resetScreenURL.origin, baseURL, "recovery returns to the trusted issuer origin");
  assert.equal(resetScreenURL.pathname, "/reset-password", "recovery returns to the configured reset screen");
  const resetToken = resetScreenURL.searchParams.get("token");
  assert.ok(resetToken, "reset message carries a single-use token");
  const resetResponse = await jsonRequest(`${authPath}/reset-password`, "POST", { token: resetToken, newPassword: "New-test-passphrase-8!LocallyVerified" });
  assert.equal(resetResponse.status, 200, `password reset accepted: ${await resetResponse.clone().text()}`);
  const replayReset = await jsonRequest(`${authPath}/reset-password`, "POST", { token: resetToken, newPassword: "Replay-passphrase-must-not-work!" });
  assert.equal(replayReset.status, 400, "recovery token is single use");
  const oldPassword = await authClient.signIn.email({ email: testAccount.email, password: testAccount.password });
  assert.ok(oldPassword.error, "old password no longer signs in after recovery");
  const login = await authClient.signIn.email({ email: testAccount.email, password: "New-test-passphrase-8!LocallyVerified" });
  assert.equal(login.error, null, "the new password verifies through the Worker password hasher");
  durationMs.passwordResetAndLogin = Math.round(performance.now() - startHash);
  testAccount.password = "New-test-passphrase-8!LocallyVerified";

  const privateSession = await (await response(`${authPath}/get-session`)).json();
  assert.equal(privateSession.user.role, undefined, "privileged role stays out of user/session output");

  const clientResponse = await jsonRequest(`${authPath}/oauth2/create-client`, "POST", {
    client_name: "9to1 local integration test",
    redirect_uris: ["https://client.example.test/callback"],
    token_endpoint_auth_method: "none",
    application_type: "web",
    grant_types: ["authorization_code", "refresh_token"],
    response_types: ["code"],
    scope: "openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke",
    skip_consent: false,
    require_pkce: true,
  });
  assert.equal(clientResponse.status, 201, `public client registration failed: ${await clientResponse.clone().text()}`);
  const client = await clientResponse.json();
  assert.ok(client.client_id, "public client has a registration ID");
  assert.equal(client.token_endpoint_auth_method, "none", "client is public and holds no secret");

  const discovery = await currentDiscovery();
  assert.ok(discovery.issuer && discovery.authorization_endpoint && discovery.token_endpoint && discovery.jwks_uri, "local discovery supplies actual issuer and endpoints");
  assert.equal(discovery.registration_endpoint, undefined, "Dynamic Client Registration is not published");

  // Exercise Better Auth's client hook from the unauthenticated authorization redirect.
  const profileFlow = await authorizationCode(discovery, client.client_id, "openid profile email cake:account:read cake:profile:read", false);
  const startedExchange = performance.now();
  const wrongVerifier = await exchangeCode(discovery, client.client_id, profileFlow.code, randomBytes(32).toString("base64url"), 401);
  assert.ok(wrongVerifier.error, "wrong PKCE verifier is rejected");
  const consumedCode = await exchangeCode(discovery, client.client_id, profileFlow.code, profileFlow.verifier, 400);
  assert.equal(consumedCode.error, "invalid_grant", "failed PKCE attempt consumes the authorization code");
  const validProfileFlow = await authorizationCode(discovery, client.client_id, "openid profile email offline_access cake:account:read cake:profile:read", true);
  const profileToken = await exchangeCode(discovery, client.client_id, validProfileFlow.code, validProfileFlow.verifier);
  durationMs.passwordLoginAndTokenExchange = Math.round(performance.now() - startedExchange);
  assert.ok(profileToken.access_token && profileToken.id_token, "authorization code produced OIDC and API tokens");

  assert.ok(profileToken.refresh_token, "authorized offline access provides a refresh token");
  const jwks = createRemoteJWKSet(new URL(discovery.jwks_uri));
  const { payload: idClaims } = await jwtVerify(profileToken.id_token, jwks, { issuer: discovery.issuer, audience: client.client_id });
  assert.match(String(idClaims.sub), /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i, "ID token subject is canonical UUID");
  // OIDC sid is emitted only for registered end-session/backchannel clients.
  // The resource token's independent sid below remains mandatory for API revocation.
  if (idClaims.sid !== undefined) {
    assert.equal(typeof idClaims.sid, "string", "optional OIDC session identifier is a string");
    assert.notEqual(idClaims.sid, idClaims.sub, "optional OIDC session identifier is independent of account identity");
  }
  assert.equal(idClaims.nonce, validProfileFlow.nonce, "ID token binds the exact authorization nonce");
  const { payload: accessClaims } = await jwtVerify(profileToken.access_token, jwks, { issuer: discovery.issuer, audience: resource });
  assert.equal(accessClaims.sub, idClaims.sub, "API token identifies the same account");
  assert.ok(typeof accessClaims.sid === "string" && accessClaims.sid.length > 0, "API token has an independent session id");
  assert.match(String(accessClaims.scope), /cake:profile:read/, "API token carries the explicit requested scope");
  assert.doesNotMatch(String(accessClaims.scope), /business|entitlement|role/i, "tokens do not embed plans or roles");

  const refreshed = await globalThis.fetch(discovery.token_endpoint, {
    method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "refresh_token", refresh_token: profileToken.refresh_token, client_id: client.client_id, resource }),
  });
  assert.equal(refreshed.status, 200, "active session can refresh its public-client token");
  const refreshedTokens = await refreshed.json();
  assert.ok(refreshedTokens.access_token && refreshedTokens.refresh_token, "refresh returns access and rotated refresh tokens");
  assert.notEqual(refreshedTokens.refresh_token, profileToken.refresh_token, "successful refresh rotates its refresh token");
  const { payload: refreshedClaims } = await jwtVerify(refreshedTokens.access_token, jwks, { issuer: discovery.issuer, audience: resource });
  assert.equal(refreshedClaims.sub, testAccount.id, "refreshed token preserves canonical account identity");
  assert.equal(refreshedClaims.sid, accessClaims.sid, "refreshed token remains bound to the same live session");
  const refreshedCurrent = await response("/api/account/current", { headers: { authorization: `Bearer ${refreshedTokens.access_token}` } });
  assert.equal(refreshedCurrent.status, 200, "rotated access token works through the production resource route");
  assert.equal((await refreshedCurrent.json()).accountId, testAccount.id);
  profileToken.refresh_token = refreshedTokens.refresh_token;

  const current = await response("/api/account/current", { headers: { authorization: `Bearer ${profileToken.access_token}` } });
  assert.equal(current.status, 200, `current account API matches the 9to1 contract: ${await current.clone().text()}`);
  assert.equal((await current.json()).accountId, testAccount.id, "current account binds to canonical token subject");
  const profileRead = await response("/api/account/profile", { headers: { authorization: `Bearer ${profileToken.access_token}` } });
  assert.equal(profileRead.status, 200, `authorized profile read: ${await profileRead.clone().text()}`);
  const profileBody = await profileRead.json();
  assert.equal(profileBody.profile.accountId, testAccount.id, "resource route binds token subject to its own account row");
  for (const optional of ["icon", "pronouns", "job"]) assert.equal(profileBody.profile[optional], null, `optional ${optional} starts unset`);
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
  const writeProfile = async (revision, fields) => jsonRequest("/api/account/profile", "PATCH", {
    expectedRevision: revision, fields,
  }, { authorization: `Bearer ${profileWriteToken.access_token}` });
  const optionalWrite = await writeProfile(2, { icon: "https://assets.example.test/icon.png", pronouns: "they/them", job: "Researcher" });
  assert.equal(optionalWrite.status, 200, "optional profile fields can be added");
  const optionalProfile = (await optionalWrite.json()).profile;
  assert.equal(optionalProfile.revision, 3);
  const preservedWrite = await writeProfile(3, { name: "Only name changed" });
  assert.equal(preservedWrite.status, 200);
  const preservedProfile = (await preservedWrite.json()).profile;
  for (const optional of ["icon", "pronouns", "job"]) assert.equal(preservedProfile[optional], optionalProfile[optional], `omitted ${optional} survives partial update`);
  const clearOptional = await writeProfile(4, { icon: null, pronouns: null, job: null });
  assert.equal(clearOptional.status, 200, "optional profile fields can be cleared");
  const clearedProfile = (await clearOptional.json()).profile;
  for (const optional of ["icon", "pronouns", "job"]) assert.equal(clearedProfile[optional], null, `optional ${optional} can be cleared`);
  const renamed = await writeProfile(5, { username: `renamed_${runId}` });
  assert.equal(renamed.status, 200, "username rename is an authorized profile update");
  const renamedProfile = (await renamed.json()).profile;
  assert.equal(renamedProfile.accountId, testAccount.id, "rename preserves canonical account identity");
  assert.equal(renamedProfile.username, `renamed_${runId}`);
  for (const username of ["CroakyJake", "CROAKYJAKE", "croakyjake"]) {
    assert.equal((await writeProfile(6, { username })).status, 409, "ordinary profile rename cannot claim reserved handle or case variants");
  }
  for (const field of ["name", "username"]) assert.equal((await writeProfile(6, { [field]: "   " })).status, 400, "required fields reject whitespace");
  assert.equal((await writeProfile(6, { constructor: "unregistered field" })).status, 400, "prototype-inherited names are not registered profile fields");
  const unchanged = (await (await response("/api/account/profile", { headers: { authorization: `Bearer ${profileToken.access_token}` } })).json()).profile;
  assert.equal(unchanged.revision, 6, "failed profile mutations preserve committed revision");
  assert.equal(unchanged.username, renamedProfile.username, "denied reservations preserve the approved username");

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
  const otherAccount = await createSyntheticAccount("auth-other", `other_${runId}`, "Test-passphrase-9!NoSharedAccount");
  const otherLogin = await authClient.signIn.email({ email: otherAccount.email, password: otherAccount.password });
  assert.equal(otherLogin.error, null, "independent account signs in without inherited authority");
  const otherSession = await (await response(`${authPath}/get-session`)).json();
  assert.equal(otherSession.user.id, otherAccount.id, "new cookie context belongs to the second account");
  const otherCurrent = await (await response("/api/account/current")).json();
  assert.equal(otherCurrent.accountId, otherAccount.id, "account lookup never exposes previous cookie context");
  const fixtureHandle = `fixture_${runId}`;
  const fixtureBody = { ownerUserId: otherAccount.id, username: fixtureHandle };
  const noFixtureAuthority = await jsonRequest("/__test/reserve-synthetic-username", "POST", fixtureBody);
  assert.equal(noFixtureAuthority.status, 404, "reservation fixture requires its isolated privileged key");
  const realHandleFixture = await jsonRequest("/__test/reserve-synthetic-username", "POST", { ownerUserId: otherAccount.id, username: "CroakyJake" }, { "x-local-test-key": testKey });
  assert.equal(realHandleFixture.status, 400, "synthetic fixture cannot provision the real reserved identity");
  const fixtureReservation = await jsonRequest("/__test/reserve-synthetic-username", "POST", fixtureBody, { "x-local-test-key": testKey });
  assert.equal(fixtureReservation.status, 200, "guarded fixture binds only its synthetic canonical account");
  assert.equal((await fixtureReservation.json()).reserved, true);
  const wrongReservedOwner = await writeProfile(6, { username: fixtureHandle.toUpperCase() });
  assert.equal(wrongReservedOwner.status, 409, "another canonical account cannot claim a trusted synthetic reservation");
  const acceptedReservedOwner = await jsonRequest("/api/account/profile", "PATCH", { expectedRevision: 1, fields: { username: fixtureHandle.toUpperCase() } });
  assert.equal(acceptedReservedOwner.status, 200, "only the canonical account bound by the reservation may rename to it");
  const reservedProfile = (await acceptedReservedOwner.json()).profile;
  assert.equal(reservedProfile.accountId, otherAccount.id, "trusted-owner reserved rename preserves canonical account identity");
  assert.equal(reservedProfile.username, fixtureHandle, "reserved handle normalizes without bypassing owner binding");
  assert.equal(reservedProfile.revision, 2, "trusted-owner rename commits one authoritative revision");
  const ownRealReservation = await jsonRequest("/api/account/profile", "PATCH", { expectedRevision: 2, fields: { username: "CroakyJake" } });
  assert.equal(ownRealReservation.status, 409, "synthetic trusted owner receives no authority over real CroakyJake reservation");
  const reservedRead = (await (await response("/api/account/profile")).json()).profile;
  assert.equal(reservedRead.username, fixtureHandle, "denied real reservation leaves synthetic reserved username intact");
  assert.equal(reservedRead.revision, 2, "denied real reservation preserves revision");

  const deniedRegistration = await jsonRequest(`${authPath}/oauth2/create-client`, "POST", {
    client_name: "unauthorized synthetic client", redirect_uris: ["https://client.example.test/callback"],
    token_endpoint_auth_method: "none", application_type: "web",
  });
  assert.equal(deniedRegistration.status, 401, "ordinary account cannot register a privileged public client");
  const crossAccountRevoke = await response(`/api/account/sessions/${otherSession.session.id}`, {
    method: "DELETE", headers: { authorization: `Bearer ${sessionToken.access_token}` },
  });
  assert.equal(crossAccountRevoke.status, 404, "even privileged first account cannot revoke another account session");
  assert.equal((await response("/api/account/current")).status, 200, "cross-account denial preserves the other live session");

  const previousOtherCookies = new Map(cookies);
  const anotherOtherLogin = await authClient.signIn.email({ email: otherAccount.email, password: otherAccount.password });
  assert.equal(anotherOtherLogin.error, null, "same account can inspect multiple independent sessions");
  const currentOtherSession = await (await response(`${authPath}/get-session`)).json();
  assert.notEqual(currentOtherSession.session.id, otherSession.session.id, "new sign-in receives a distinct canonical session identity");
  const currentOtherCookies = new Map(cookies);
  const beforeOtherRevocation = await (await response("/api/account/sessions")).json();
  assert.ok(beforeOtherRevocation.sessions.some((session) => session.sessionId === otherSession.session.id), "older session is present before revocation");
  assert.ok(beforeOtherRevocation.sessions.some((session) => session.sessionId === currentOtherSession.session.id), "current session is present before revocation");
  const revokedOthers = await jsonRequest("/api/account/revoke-other-sessions", "POST");
  assert.equal(revokedOthers.status, 204, "revoke-other-sessions uses the authenticated account route");
  const afterOtherRevocation = await (await response("/api/account/sessions")).json();
  assert.equal(afterOtherRevocation.sessions.length, 1, "only the requesting current session remains");
  assert.equal(afterOtherRevocation.sessions[0].sessionId, currentOtherSession.session.id);
  cookies.clear();
  for (const [name, value] of previousOtherCookies) cookies.set(name, value);
  assert.equal((await response("/api/account/current")).status, 401, "revoked older cookie cannot authorize account access");
  cookies.clear();
  for (const [name, value] of currentOtherCookies) cookies.set(name, value);
  assert.equal((await response("/api/account/current")).status, 200, "revoke-other preserves the current authenticated session");
  const signedOut = await jsonRequest("/api/account/signout", "POST");
  assert.equal(signedOut.status, 204, "sign-out goes through the authenticated canonical account route");
  assert.equal((await response("/api/account/current")).status, 401, "sign-out immediately removes current session authority");

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

  const refreshAfterRevocation = await globalThis.fetch(discovery.token_endpoint, {
    method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "refresh_token", refresh_token: profileToken.refresh_token, client_id: client.client_id, resource }),
  });
  assert.equal(refreshAfterRevocation.status, 400, "revoked session cannot refresh its OAuth token");
  assert.equal((await refreshAfterRevocation.json()).error, "invalid_grant", "revoked refresh token fails explicitly");

  // A mismatched resource is rejected by the OAuth authorization layer before token issuance.
  const wrongResourceURL = new URL(discovery.authorization_endpoint);
  wrongResourceURL.search = new URLSearchParams({
    response_type: "code", client_id: client.client_id, redirect_uri: "https://client.example.test/callback",
    scope: "openid cake:profile:read", state: randomBytes(16).toString("base64url"),
    code_challenge: createHash("sha256").update(randomBytes(32).toString("base64url")).digest("base64url"),
    code_challenge_method: "S256", resource: "https://wrong.example/api",
  }).toString();
  const wrongResource = await globalThis.fetch(wrongResourceURL, { redirect: "manual" });
  let deniedNavigation = wrongResource.headers.get("location");
  if (!deniedNavigation && wrongResource.headers.get("content-type")?.includes("application/json")) {
    const deniedBody = await wrongResource.json();
    if (deniedBody.redirect === true) deniedNavigation = deniedBody.url;
    else assert.ok(deniedBody.error, "invalid resource returns a structured error");
  }
  if (deniedNavigation) {
    const deniedURL = new URL(deniedNavigation, baseURL);
    assert.equal(deniedURL.searchParams.has("code"), false, "unregistered resource cannot mint an authorization code via header or JSON navigation");
  } else assert.ok(wrongResource.status >= 400, "invalid resource fails without navigation");

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
    assertionsExecuted: assertionCount,
    environment: "local Wrangler Workerd + local D1 only",
    issuer: discovery.issuer,
    authorizationEndpoint: discovery.authorization_endpoint,
    tokenEndpoint: discovery.token_endpoint,
    jwksUri: discovery.jwks_uri,
    publicClientRegistration: "synthetic and local only; client id intentionally not printed",
    redirectUri: "https://client.example.test/callback (synthetic test only)",
    passwordResetAndLoginMs: durationMs.passwordResetAndLogin,
    passwordLoginAndTokenExchangeMs: durationMs.passwordLoginAndTokenExchange,
    externalEmailDelivery: "not tested; messages captured in local D1 outbox",
    cloudflareDeployment: "none",
  }, null, 2));
} catch (error) {
  mainFailure = error;
  console.error(error instanceof Error ? error.message : String(error));
  if (error?.cause?.code) console.error(`failure cause code: ${error.cause.code}`);
  process.exitCode = 1;
} finally {
  globalThis.fetch = realFetch;
  delete globalThis.window;
  await finishIntegrationCleanup(mainFailure, async () => {
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
  });
}
