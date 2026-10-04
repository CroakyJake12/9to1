import {writeFileSync,mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
export function configuration(receipt) {
  if (receipt.database_name !== 'cake-id-release-validation' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(receipt.database_id ?? '') || receipt.database_id === '00000000-0000-0000-0000-000000000000') throw new Error('Actual isolated D1 creation receipt required');
  if (!/^[0-9a-f]{32}$/.test(receipt.account_id ?? '')) throw new Error('Actual Cloudflare account ID required');
  const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
  return {$schema:'../node_modules/wrangler/config-schema.json',name:'cake-id-release-validation',account_id:receipt.account_id,main:'../src/index.ts',compatibility_date:'2026-10-01',compatibility_flags:['nodejs_compat'],workers_dev:true,preview_urls:false,observability:{enabled:true,traces:{enabled:true}},secrets:{required:['AUTH_SECRET','LOGIN_LIMITER_KEY']},vars:{APP_MODE:'production',EMAIL_CAPTURE:'false',AUTH_BASE_URL:origin,API_RESOURCE:origin,ALLOWED_WEB_ORIGINS:origin,LOGIN_LIMIT_MAX:'8',LOGIN_LIMIT_WINDOW_SECONDS:'900',LOGIN_LIMIT_BLOCK_SECONDS:'1800'},d1_databases:[{binding:'DB',database_name:receipt.database_name,database_id:receipt.database_id,migrations_dir:'../migrations'}]};
}
if (process.argv[1] && resolve(process.argv[1])===resolve(import.meta.dirname,'prepare.mjs')) {
  const {readFileSync}=await import('node:fs');
  if(process.argv.length!==4) throw new Error('Usage: node deployment/prepare.mjs ACTUAL_D1_RECEIPT.json OUTPUT_CONFIG.json');
  const result=configuration(JSON.parse(readFileSync(process.argv[2],'utf8')));
  // Relative paths are deliberately bound to this deployment directory.
  const output=resolve(process.argv[3]);
  if(resolve(output,'..')!==import.meta.dirname) throw new Error('Output must remain in the reviewed deployment directory');
  writeFileSync(output,JSON.stringify(result,null,2)+'\n',{flag:'wx',mode:0o600});
}
