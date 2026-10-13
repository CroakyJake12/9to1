import { build } from 'esbuild';
const entry = process.argv[2] ?? 'relay-worker.mjs';
const output = process.argv[3] ?? 'relay-worker.bundle.mjs';
await build({entryPoints:[new URL(entry,import.meta.url).pathname],outfile:new URL(output,import.meta.url).pathname,bundle:true,format:'esm',target:'es2022',external:['cloudflare:workers'],legalComments:'inline'});
