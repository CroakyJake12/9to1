import assert from "node:assert/strict";
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
const browserOrigin = "https://client.example.test:5096";
const callback = `${browserOrigin}/callback`;
const persistPath = path.join(root, ".local-run", runId);
const baseURL = "http://127.0.0.1:8798";
const authPath = "/api/auth";
const resource = baseURL;
const testKey = randomBytes(32).toString("base64url");
const devVars = [
  `AUTH_SECRET=${randomBytes(48).toString("base64url")}`,
  `LOGIN_LIMITER_KEY=${randomBytes(48).toString("base64url")}`,
  `LOCAL_TEST_KEY=${testKey}`,
  `API_RESOURCE=${resource}`,
  `ALLOWED_WEB_ORIGINS=${baseURL},${browserOrigin}`,
].join("\n") + "\n";

if (existsSync(varsPath)) {
  throw new Error("Refusing to overwrite an existing local .dev.vars file. Move it temporarily, then rerun this isolated test.");
}

mkdirSync(persistPath, { recursive: true, mode: 0o700 });
writeFileSync(varsPath, devVars, { flag: "wx", mode: 0o600 });

let worker;
let workerStartupError;
let custodyReceipt = "";
let release;
const released = new Promise(resolve => { release = resolve; });
process.once("SIGINT", release);
process.once("SIGTERM", release);
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
    if (workerStartupError) throw new Error("Wrangler spawn failed before readiness");
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


// Reuses the maintained local entrypoint and exact integration registration/verification path.
// State and credentials live only in ignored .local-run and .dev.vars with restrictive modes.
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
  const admin = await createSyntheticAccount("browser-operator", `operator_${runId}`, randomBytes(32).toString("base64url"));
  const promoted = await jsonRequest("/__test/promote-admin", "POST", { userId: admin.id, email: admin.email }, { "x-local-test-key": testKey });
  assert.equal((await promoted.json()).promoted, true);
  assert.equal((await authClient.signIn.email({ email: admin.email, password: admin.password })).error, null);
  const registration = await jsonRequest(`${authPath}/oauth2/create-client`, "POST", {
    client_name: "Isolated browser account adapter fixture", redirect_uris: [callback],
    token_endpoint_auth_method: "none", application_type: "web", grant_types: ["authorization_code", "refresh_token"],
    response_types: ["code"], scope: "openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke",
    skip_consent: false, require_pkce: true,
  });
  assert.equal(registration.status, 201, "registered exact synthetic HTTPS web callback");
  const client = await registration.json();
  cookies.clear();
  const accounts = [];
  for (let i = 0; i < 2; i++) accounts.push(await createSyntheticAccount(`browser-user-${i}`, `browser_${i}_${runId}`, randomBytes(32).toString("base64url")));
  cookies.clear();
  const discoveryResponse = await response(`${authPath}/.well-known/openid-configuration`);
  assert.equal(discoveryResponse.status, 200);
  const discovery = await discoveryResponse.json();
  assert.equal(discovery.issuer, `${baseURL}${authPath}`);
  const preflight = await realFetch(`${baseURL}/api/account/profile`, { method: "OPTIONS",
    headers: { origin: browserOrigin, "access-control-request-method": "PATCH", "access-control-request-headers": "authorization,content-type" } });
  assert.equal(preflight.status, 204);
  assert.equal(preflight.headers.get("access-control-allow-origin"), browserOrigin);
  const rejectedPreflight = await realFetch(`${baseURL}/api/account/profile`, { method: "OPTIONS", headers: { origin: "https://wrong.example.test" } });
  assert.equal(rejectedPreflight.status, 403);
  const manifest = { fixtureOnly: true, runId, issuer: discovery.issuer, apiResource: resource,
    browserOrigin, redirectUri: callback, clientId: client.client_id, discovery,
    accounts, localTestKey: testKey, persistPath,
    scopes: "openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke" };
  const manifestPath = path.join(persistPath, "browser-fixture-private.json");
  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2), { mode: 0o600, flag: "wx" });
  console.log(JSON.stringify({ status: "ready", fixtureOnly: true, manifestPath, issuer: discovery.issuer,
    apiResource: resource, browserOrigin, redirectUri: callback, accounts: accounts.length,
    credentials: "private file only; never print or commit", email: "local D1 capture only", port5095: "unused" }));
  if (process.env.CAKE_BROWSER_FIXTURE_SMOKE !== "1") await released;
} catch (error) {
  console.error(`Local browser fixture failed: ${error?.message ?? "unknown error"}`);
  process.exitCode = 1;
} finally {
  // A cleanup error deliberately skips all state removal and remains an observable nonzero failure.
  await stopWorker();
  globalThis.fetch = realFetch;
  delete globalThis.window;
  if (existsSync(varsPath) && readFileSync(varsPath, "utf8") === devVars) rmSync(varsPath);
  rmSync(persistPath, { recursive: true, force: true });
}
