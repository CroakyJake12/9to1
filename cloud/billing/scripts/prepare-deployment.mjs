import {deploymentConfig} from './deployment-config.mjs';
import {createRequire} from 'node:module';
import {readFileSync,mkdirSync,openSync,writeSync,fsyncSync,closeSync,lstatSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {createHash} from 'node:crypto';
const [inputPath,outputPath]=process.argv.slice(2);
if (!inputPath || !outputPath || !process.env.BILLING_TOOLCHAIN_ROOT) throw new Error('Explicit input, fresh output directory and pinned toolchain root required');
const output=resolve(outputPath);
for(let p=dirname(output);;p=dirname(p)) {if(lstatSync(p).isSymbolicLink())throw new Error('Symlink output ancestor refused');if(p===dirname(p))break;}
const config=deploymentConfig(JSON.parse(readFileSync(inputPath,'utf8')));
const require=createRequire(resolve(process.env.BILLING_TOOLCHAIN_ROOT,'package.json'));
const esbuild=require('esbuild');
if(esbuild.version!=='0.28.1' || require('wrangler/package.json').version!=='4.146.0')throw new Error('Pinned toolchain mismatch');
const built=await esbuild.build({entryPoints:[resolve('src/stripe-webhook.mjs')],bundle:true,format:'esm',platform:'browser',target:'es2022',write:false,metafile:true});
mkdirSync(output,{mode:0o700});mkdirSync(join(output,'schema'),{mode:0o700});
function save(path,bytes) {
 const fd=openSync(path,'wx',0o600);let body,close;
 try {let offset=0;while(offset<bytes.length){const n=writeSync(fd,bytes,offset);if(n<=0)throw new Error('Incomplete bundle write');offset+=n;}fsyncSync(fd);}catch(error){body=error;}
 try{closeSync(fd);}catch(error){close=error;}
 if(body||close)throw new AggregateError([body,close].filter(Boolean),'Bundle persistence failed');
}
const files={'worker.mjs':built.outputFiles[0].contents,'wrangler.json':Buffer.from(JSON.stringify(config,null,2)+'\n'),'schema/0001-stripe-inbox.sql':readFileSync('schema/0001-stripe-inbox.sql')};
const manifest={kind:'prepared-only-no-provider-actions',toolchain:{esbuild:esbuild.version,wrangler:'4.146.0'},inputs:Object.keys(built.metafile.inputs).map(path=>({path,sha256:createHash('sha256').update(readFileSync(path)).digest('hex')})),files:[]};
for(const [path,bytes]of Object.entries(files)){save(join(output,path),bytes);manifest.files.push({path,bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')});}
save(join(output,'manifest.json'),Buffer.from(JSON.stringify(manifest,null,2)+'\n'));
console.log(JSON.stringify({prepared:output,files:manifest.files}));
