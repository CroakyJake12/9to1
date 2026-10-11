import {readFile,writeFile,mkdir,copyFile} from 'node:fs/promises';
import {generateKeyPairSync} from 'node:crypto';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
const [supplied,destination,repository,powershell,codex,opencode,worktreeRoot]=process.argv.slice(2);if(!opencode)throw new Error('Supply package folder, private destination, isolated repository and executable paths; optionally a short worktree root');
await mkdir(destination,{recursive:true});
const configPath=path.join(destination,'relay.private.json');
try{await readFile(configPath);throw new Error('Private config already exists; refusing overwrite');}catch(e){if(e.code!=='ENOENT')throw e;}
for(const name of ['Launch-Strata.ps1','LOCAL-SHIFT-PROMPT.md','relay-schedule.private.json','local-presence.private.json'])await copyFile(path.join(supplied,name),path.join(destination,name));
const launcher=await readFile(path.join(supplied,'Launch-Strata.ps1'),'utf8');const value=n=>{const m=launcher.match(new RegExp('\\$'+n+" = '([^']+)'"));if(!m)throw new Error('Missing supplied '+n);return m[1];};
const keys=generateKeyPairSync('rsa',{modulusLength:2048});const keyPath=path.join(destination,'local-signing.pem');await writeFile(keyPath,keys.privateKey.export({type:'pkcs8',format:'pem'}),{mode:0o600});
const publicKey=keys.publicKey.export({type:'spki',format:'der'}).toString('base64');await writeFile(path.join(destination,'public-key.json'),JSON.stringify({local:publicKey}));
const c={actor:'local',repository,remote:'origin',dashboard_port:8794,coordinator_url:'',cloud_environment_id:'',
  signing_key:keyPath,state_db:path.join(destination,'runner.db'),logs:path.join(destination,'logs'),checkpoints:path.join(destination,'checkpoints'),worktrees:worktreeRoot?path.resolve(worktreeRoot):path.join(destination,'worktrees'),
  strata_root:value('strataRoot'),strata_data:value('strataData'),original_repository:value('strataRepo'),original_outputs:value('strataOutputs'),supplied_launcher:path.join(destination,'Launch-Strata.ps1'),
  powershell,codex,opencode,opencode_config:path.join(destination,'opencode.private.json'),
  plugin_allowlist:['google-drive@openai-curated-remote','github@openai-curated-remote','computer-use@openai-bundled','unified-computer-use@openai-bundled','app-6a99ceead2c481919fb44845267235c4@openai-curated-remote'],
  allowed_test_executables:['node','dotnet','python'],schedule:JSON.parse(await readFile(path.join(supplied,'relay-schedule.private.json'),'utf8')),
  spec:{document_id:'17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY',directive:'2026-10-10 four-part delivery'}};
if(process.platform==='win32'){
  await promisify(execFile)(powershell,['-NoProfile','-File',path.join(path.dirname(fileURLToPath(import.meta.url)),'Protect-SigningKey.ps1'),'-KeyPath',keyPath],{windowsHide:true});
  c.signing_key=path.join(destination,'local-signing.dpapi');c.key_format='dpapi';
}
await mkdir(c.checkpoints,{recursive:true});await mkdir(c.logs,{recursive:true});await writeFile(configPath,JSON.stringify(c,null,2));
await writeFile(c.opencode_config,JSON.stringify({$schema:'https://opencode.ai/config.json',model:'strata/iq2_xs',enabled_providers:['strata'],provider:{strata:{npm:'@ai-sdk/openai-compatible',name:'Strata loopback',options:{baseURL:'http://127.0.0.1:8081/v1'},models:{iq2_xs:{name:'Qwen IQ2_XS',limit:{context:524288,output:65536}}}}},compaction:{auto:true,prune:true},permission:{external_directory:'deny',webfetch:'deny',websearch:'deny',task:'deny',edit:'allow',bash:'ask'},share:'disabled',autoupdate:false},null,2));
console.log(JSON.stringify({configuration:configPath,public_keys_file:path.join(destination,'public-key.json'),private_key_created:true}));
