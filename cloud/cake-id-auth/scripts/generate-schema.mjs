import { build } from "esbuild";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const scratch = path.join(root, ".local-run", "schema-exporter.mjs");
const output = path.join(root, "migrations", "0001_better_auth.sql");

await mkdir(path.dirname(scratch), { recursive: true });
await mkdir(path.dirname(output), { recursive: true });
await build({
  entryPoints: [path.join(root, "scripts", "schema-exporter.ts")],
  bundle: true,
  packages: "external",
  platform: "node",
  format: "esm",
  target: "node24",
  outfile: scratch,
});

const { generateSchema } = await import(`${pathToFileURL(scratch).href}?v=${Date.now()}`);
await writeFile(output, await generateSchema(), { encoding: "utf8" });
console.log(`Wrote ${path.relative(root, output)} using the pinned Better Auth schema.`);
