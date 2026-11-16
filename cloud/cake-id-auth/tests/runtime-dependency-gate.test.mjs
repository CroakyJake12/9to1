import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { assertZodRuntimeVersions, FIXED_ZOD_MINIMUM, verifyRuntimeDependencies } from "../scripts/verify-runtime-dependencies.mjs";

test("runtime dependency gate rejects stale installed Zod despite a fixed override and lock", () => {
  assert.equal(FIXED_ZOD_MINIMUM, "4.6.6");
  assert.throws(() => assertZodRuntimeVersions({
    overrideVersion: "4.6.6",
    lockVersion: "4.6.6",
    installedVersion: "4.6.5",
    resolvedPath: "C:/fixture/node_modules/zod/package.json",
  }), /Installed Zod 4\.6\.5 .* below the fixed minimum 4\.6\.6/);
});

test("runtime dependency gate records the actual resolved installed package", () => {
  const result = verifyRuntimeDependencies();
  assert.equal(result.overrideVersion, "4.6.6");
  assert.equal(result.lockVersion, "4.6.6");
  assert.equal(result.installedVersion, "4.6.6");
  assert.equal(path.basename(result.resolvedPath), "package.json");
  assert.ok(path.isAbsolute(result.resolvedPath));
  assert.match(result.installedPackageJsonSha256, /^[a-f0-9]{64}$/);
});
