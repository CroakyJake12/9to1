import { createHash } from "node:crypto";
import { createRequire } from "node:module";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const FIXED_ZOD_MINIMUM = "4.6.6";
const scriptPath = fileURLToPath(import.meta.url);
const packageRoot = path.resolve(path.dirname(scriptPath), "..");

function stableVersionParts(version) {
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(version ?? "");
  return match ? match.slice(1).map(Number) : null;
}

function compareStableVersions(left, right) {
  const a = stableVersionParts(left);
  const b = stableVersionParts(right);
  if (!a || !b) throw new Error(`Unsupported Zod version format: ${left} or ${right}`);
  for (let index = 0; index < 3; index++) {
    if (a[index] !== b[index]) return a[index] < b[index] ? -1 : 1;
  }
  return 0;
}

export function assertZodRuntimeVersions({ overrideVersion, lockVersion, installedVersion, resolvedPath }) {
  for (const [label, version] of Object.entries({ overrideVersion, lockVersion, installedVersion })) {
    if (!stableVersionParts(version)) throw new Error(`${label} must be a stable numeric version.`);
  }
  if (compareStableVersions(installedVersion, FIXED_ZOD_MINIMUM) < 0) {
    throw new Error(`Installed Zod ${installedVersion} at ${resolvedPath} is below the fixed minimum ${FIXED_ZOD_MINIMUM}.`);
  }
  if (overrideVersion !== lockVersion || installedVersion !== overrideVersion) {
    throw new Error(`Zod version mismatch: override=${overrideVersion}, lock=${lockVersion}, installed=${installedVersion} at ${resolvedPath}.`);
  }
  return { minimumFixedVersion: FIXED_ZOD_MINIMUM, overrideVersion, lockVersion, installedVersion, resolvedPath };
}

function packageJsonFor(entryPath, expectedName) {
  let directory = path.dirname(entryPath);
  while (true) {
    const candidate = path.join(directory, "package.json");
    if (existsSync(candidate)) {
      const json = JSON.parse(readFileSync(candidate, "utf8"));
      if (json.name === expectedName) return { path: candidate, json };
    }
    const parent = path.dirname(directory);
    if (parent === directory) throw new Error(`Could not locate ${expectedName}/package.json from ${entryPath}.`);
    directory = parent;
  }
}

export function verifyRuntimeDependencies(root = packageRoot) {
  const projectRoot = path.resolve(root);
  const manifestPath = path.join(projectRoot, "package.json");
  const lockPath = path.join(projectRoot, "package-lock.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  const lock = JSON.parse(readFileSync(lockPath, "utf8"));
  const lockEntry = lock.packages?.["node_modules/zod"];
  if (typeof manifest.overrides?.zod !== "string" || typeof lockEntry?.version !== "string" || typeof lockEntry?.integrity !== "string") {
    throw new Error("Zod must have an exact package override and a complete package-lock entry.");
  }

  const requireFromPackage = createRequire(manifestPath);
  const resolvedEntry = requireFromPackage.resolve("zod");
  const installed = packageJsonFor(resolvedEntry, "zod");
  const expectedPackageJson = path.join(projectRoot, "node_modules", "zod", "package.json");
  if (path.resolve(installed.path).toLowerCase() !== path.resolve(expectedPackageJson).toLowerCase()) {
    throw new Error(`Zod resolved outside this package's installed dependency: ${installed.path}`);
  }
  const versionCheck = assertZodRuntimeVersions({
    overrideVersion: manifest.overrides.zod,
    lockVersion: lockEntry.version,
    installedVersion: installed.json.version,
    resolvedPath: installed.path,
  });
  const sha256 = (filePath) => createHash("sha256").update(readFileSync(filePath)).digest("hex");

  return {
    ...versionCheck,
    resolvedEntry,
    installedPackageJsonSha256: sha256(installed.path),
    lockIntegrity: lockEntry.integrity,
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  console.log(JSON.stringify(verifyRuntimeDependencies(), null, 2));
}
