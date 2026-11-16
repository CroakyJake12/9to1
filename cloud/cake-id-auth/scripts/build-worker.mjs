import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from "node:fs";
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { verifyRuntimeDependencies } from "./verify-runtime-dependencies.mjs";

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repositoryRoot = path.resolve(packageRoot, "..", "..");
const configPath = path.join(packageRoot, "deployment", "wrangler.build.json");

function outputPathFromArgs(args) {
  if (args.length === 1 && args[0].startsWith("--outdir=")) return args[0].slice("--outdir=".length);
  if (args.length === 2 && args[0] === "--outdir") return args[1];
  throw new Error("Usage: npm run build:worker -- --outdir <fresh absolute path outside the repository>");
}

function pathIsInside(candidate, directory) {
  const relative = path.relative(directory, candidate);
  return relative === "" || (!relative.startsWith(`..${path.sep}`) && relative !== ".." && !path.isAbsolute(relative));
}

function buildEnvironment(configHome, logDirectory) {
  const environment = { ...process.env };
  const blocked = new Set([
    "NODE_OPTIONS", "NODE_PATH", "NODE_REPL_EXTERNAL_MODULE",
    "CLOUDFLARE_API_TOKEN", "CLOUDFLARE_API_KEY", "CLOUDFLARE_EMAIL", "CLOUDFLARE_ACCOUNT_ID", "CF_API_TOKEN",
  ]);
  for (const name of Object.keys(environment)) if (blocked.has(name.toUpperCase())) delete environment[name];
  environment.XDG_CONFIG_HOME = configHome;
  environment.WRANGLER_LOG_PATH = logDirectory;
  environment.WRANGLER_SEND_METRICS = "false";
  return environment;
}

async function main() {
  const requested = outputPathFromArgs(process.argv.slice(2));
  if (!path.isAbsolute(requested)) throw new Error("Worker output directory must be absolute.");
  const outputDirectory = path.resolve(requested);
  const realRepositoryRoot = realpathSync(repositoryRoot);
  const realOutputParent = realpathSync(path.dirname(outputDirectory));
  const finalOutputDirectory = path.join(realOutputParent, path.basename(outputDirectory));
  if (pathIsInside(finalOutputDirectory, realRepositoryRoot)) {
    throw new Error("Worker output directory must be outside the repository.");
  }

  const preflightPath = `${finalOutputDirectory}.runtime-dependency-preflight.json`;
  const configHome = `${finalOutputDirectory}.wrangler-config`;
  const logDirectory = `${finalOutputDirectory}.wrangler-logs`;
  for (const pathToCheck of [finalOutputDirectory, preflightPath, configHome, logDirectory]) {
    if (existsSync(pathToCheck)) throw new Error(`Refusing to reuse existing build output: ${pathToCheck}`);
  }

  const runtime = verifyRuntimeDependencies(packageRoot);
  const configSha256 = createHash("sha256").update(readFileSync(configPath)).digest("hex");
  mkdirSync(configHome, { recursive: false });
  writeFileSync(preflightPath, `${JSON.stringify({ createdAt: new Date().toISOString(), runtime, configPath, configSha256 }, null, 2)}\n`, { flag: "wx" });

  console.log(JSON.stringify({ runtimeDependencyPreflight: runtime, outputDirectory: finalOutputDirectory, dryRun: true, configPath }, null, 2));
  const wranglerPath = path.join(packageRoot, "node_modules", "wrangler", "bin", "wrangler.js");
  const args = [wranglerPath, "deploy", "--dry-run", "--config", configPath, "--outdir", finalOutputDirectory, "--metafile"];
  const child = spawn(process.execPath, args, {
    cwd: packageRoot,
    env: buildEnvironment(configHome, logDirectory),
    shell: false,
    windowsHide: true,
    stdio: "inherit",
  });
  const result = await new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", (code, signal) => resolve({ code, signal }));
  });
  if (result.code !== 0) throw new Error(`Pinned Wrangler dry run failed with code=${result.code}, signal=${result.signal}.`);
}

main().catch(error => {
  console.error(`Worker build stopped: ${error.message}`);
  process.exitCode = 1;
});
