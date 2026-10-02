import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { createServer } from "node:net";
import { existsSync, rmSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const runId = randomBytes(8).toString("hex");
const persistPath = path.join(root, ".local-run", `password-${runId}`);
const wrangler = path.join(root, "node_modules", "wrangler", "bin", "wrangler.js");

const portServer = createServer();
await new Promise((resolve, reject) => portServer.once("error", reject).listen(0, "127.0.0.1", resolve));
const { port } = portServer.address();
await new Promise((resolve, reject) => portServer.close((error) => error ? reject(error) : resolve()));

let worker;
let output = "";
let cleanupPath = path.resolve(persistPath);

async function stopWorker() {
  if (!worker || worker.exitCode !== null) return;
  worker.kill("SIGINT");
  await Promise.race([
    new Promise((resolve) => worker.once("exit", resolve)),
    new Promise((resolve) => setTimeout(resolve, 5000)),
  ]);
  if (worker.exitCode === null && process.platform === "win32" && worker.pid) {
    const killer = spawn("taskkill", ["/PID", String(worker.pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" });
    await new Promise((resolve) => killer.once("exit", resolve));
  } else if (worker.exitCode === null) {
    worker.kill("SIGKILL");
  }
}

try {
  worker = spawn(process.execPath, [
    wrangler, "dev", "--local", "--config", "wrangler.password-test.jsonc", "--ip", "127.0.0.1", "--port", String(port), "--persist-to", persistPath,
  ], { cwd: root, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
  worker.stdout.setEncoding("utf8").on("data", (chunk) => { output = (output + chunk).slice(-12000); });
  worker.stderr.setEncoding("utf8").on("data", (chunk) => { output = (output + chunk).slice(-12000); });

  let result;
  for (let attempt = 0; attempt < 40; attempt++) {
    if (worker.exitCode !== null) throw new Error(`Workerd exited before readiness.\n${output}`);
    try {
      const response = await fetch(`http://127.0.0.1:${port}/__test/password-hash`, { signal: AbortSignal.timeout(1500) });
      if (response.ok) { result = await response.json(); break; }
    } catch { }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  assert.ok(result, `Workerd password hashing route did not become ready.\n${output}`);
  assert.equal(result.correctPasswordAccepted, true, "Better Auth's hash verifies the original password in Workerd");
  assert.equal(result.wrongPasswordRejected, true, "Better Auth's hash rejects a different password in Workerd");
  assert.equal(result.saltBytes, 16, "Better Auth's default uses a 16-byte random salt");
  assert.equal(result.derivedKeyBytes, 64, "Better Auth's default derives a 64-byte key");
  assert.ok(Number.isFinite(result.hashDurationMs) && result.hashDurationMs >= 0, "Workerd reports hash duration");
  assert.ok(Number.isFinite(result.verifyDurationMs) && result.verifyDurationMs >= 0, "Workerd reports verification duration");
  console.log(JSON.stringify({
    result: "passed",
    runtime: "local Cloudflare Workerd",
    algorithm: "Better Auth default scrypt",
    saltBytes: result.saltBytes,
    derivedKeyBytes: result.derivedKeyBytes,
    hashDurationMs: Math.round(result.hashDurationMs),
    twoPasswordVerificationsDurationMs: Math.round(result.verifyDurationMs),
    database: "none",
    externalService: "none",
  }, null, 2));
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
} finally {
  await stopWorker();
  const localRoot = path.resolve(root, ".local-run");
  if (cleanupPath.startsWith(`${localRoot}${path.sep}`) && existsSync(cleanupPath)) rmSync(cleanupPath, { recursive: true, force: true });
}
