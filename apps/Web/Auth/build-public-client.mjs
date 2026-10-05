// Reproducible isolated bundle from the exact maintained fixture package lock; no dependency install or configuration defaults.
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const dependencies = process.argv[2];
if (!dependencies || !path.isAbsolute(dependencies)) throw new Error('Supply the absolute acknowledged fixture node_modules path.');
const jose = JSON.parse(await readFile(path.join(dependencies, 'jose/package.json'), 'utf8'));
const esbuild = JSON.parse(await readFile(path.join(dependencies, 'esbuild/package.json'), 'utf8'));
if (jose.version !== '6.2.12' || esbuild.version !== '0.28.1') throw new Error('Maintained dependency versions differ.');
const { build } = await import(pathToFileURL(path.join(dependencies, 'esbuild/lib/main.js')));
const directory = path.dirname(fileURLToPath(import.meta.url));
await build({ entryPoints: [path.join(directory, 'configured-accounts.js')], outfile: path.join(directory, 'configured-accounts.bundle.js'),
  bundle: true, format: 'esm', target: 'es2022', minify: false, legalComments: 'inline',
  plugins: [{ name: 'acknowledged-jose', setup(context) { context.onResolve({ filter: /^jose$/ }, () => ({ path: path.join(dependencies, 'jose/dist/webapi/index.js') })); } }] });
console.log('Built public-client bundle from jose 6.2.12 / esbuild 0.28.1. No issuer/client/redirect configuration included.');
