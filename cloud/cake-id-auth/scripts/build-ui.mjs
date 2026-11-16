import { build } from "esbuild";
import { mkdir } from "node:fs/promises";
import { verifyRuntimeDependencies } from "./verify-runtime-dependencies.mjs";

const runtime = verifyRuntimeDependencies();
console.log(`Runtime dependency gate: Zod ${runtime.installedVersion} at ${runtime.resolvedPath}`);
await mkdir("public", { recursive: true });
await build({
  entryPoints: ["src/browser.ts"],
  bundle: true,
  minify: true,
  format: "iife",
  target: "es2022",
  outfile: "public/auth-ui.txt",
});
