import { build } from "esbuild";
import { mkdir } from "node:fs/promises";

await mkdir("public", { recursive: true });
await build({
  entryPoints: ["src/browser.ts"],
  bundle: true,
  minify: true,
  format: "iife",
  target: "es2022",
  outfile: "public/auth-ui.txt",
});
