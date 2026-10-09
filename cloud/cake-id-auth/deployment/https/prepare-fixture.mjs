import {randomBytes,randomUUID,createHash} from 'node:crypto';
import {mkdir,open,readFile} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {hashPassword,verifyPassword} from 'better-auth/crypto';
import {origin} from './validate.mjs';
const databaseId='fb094287-efab-4d3c-b9d5-c588a83d6f42';
export function resourcePrecondition(receipt) {
  if(receipt.databaseId!==databaseId||receipt.response?.status!==200||receipt.response?.success!==true)throw new Error('Actual isolated resource SELECT receipt required');
  const results=receipt.response.result;
  if(!Array.isArray(results)||results.length!==1||results[0].success!==true||results[0].results?.length!==1||results[0].results[0].identifier!==origin)throw new Error('Exactly initialized canonical resource identifier required');
}
export async function prepare(directory,receipt) {
  resourcePrecondition(receipt);
  const runId=randomBytes(8).toString('hex'),now=new Date().toISOString();
  const accounts=[1,2].map(index=>({id:randomUUID(),credentialId:randomUUID(),email:`cake-https-${runId}-${index}@example.test`,username:`cake_https_${runId}_${index}`,name:`Fictional HTTPS ${index}`,password:randomBytes(48).toString('base64url')}));
  const callback='https://client.example.test:5096/callback',clientId=`cake_https_${runId}`,clientRowId=randomUUID(),linkId=randomUUID();
  const scopes=['openid','profile','email','offline_access','cake:account:read','cake:profile:read','cake:profile:write','cake:sessions:read','cake:sessions:revoke'];
  const batch=[];
  for(const a of accounts) {
    const password=await hashPassword(a.password);
    if(!await verifyPassword({hash:password,password:a.password}))throw new Error('Pinned library password hash self-check failed');
    batch.push({sql:'INSERT INTO "user" (id,name,email,emailVerified,createdAt,updatedAt,username,displayUsername,role,profileRevision) VALUES (?,?,?,1,?,?,?,?,NULL,1)',params:[a.id,a.name,a.email,now,now,a.username,a.username]});
    batch.push({sql:'INSERT INTO account (id,accountId,providerId,userId,password,createdAt,updatedAt) VALUES (?,?,\'credential\',?,?,?,?)',params:[a.credentialId,a.id,a.id,password,now,now]});
  }
  batch.push({sql:'INSERT INTO oauthClient (id,clientId,clientSecret,disabled,skipConsent,scopes,clientCredentialsScopes,createdAt,updatedAt,name,redirectUris,tokenEndpointAuthMethod,applicationType,grantTypes,responseTypes,requirePKCE) VALUES (?,?,NULL,0,0,?,?,?,?,?,?,\'none\',\'web\',?,?,1)',params:[clientRowId,clientId,JSON.stringify(scopes),'[]',now,now,'Fictional isolated HTTPS fixture',JSON.stringify([callback]),JSON.stringify(['authorization_code','refresh_token']),JSON.stringify(['code'])]});
  batch.push({sql:'INSERT INTO oauthClientResource (id,clientId,resourceId,createdAt) VALUES (?,?,?,?)',params:[linkId,clientId,origin,now]});
  const literal=value=>value===null?'NULL':`'${String(value).replaceAll("'","''")}'`;
  const sql=batch.map(q=>{let i=0;const text=q.sql.replace(/\?/g,()=>literal(q.params[i++]));if(i!==q.params.length)throw new Error('SQL parameter count mismatch');return text+';';}).join('\n')+'\n';
  const rollback=[`DELETE FROM oauthClientResource WHERE id=${literal(linkId)} AND clientId=${literal(clientId)} AND resourceId=${literal(origin)};`,`DELETE FROM oauthClient WHERE id=${literal(clientRowId)} AND clientId=${literal(clientId)} AND name='Fictional isolated HTTPS fixture';`,...accounts.flatMap(a=>[`DELETE FROM account WHERE id=${literal(a.credentialId)} AND userId=${literal(a.id)} AND providerId='credential';`,`DELETE FROM "user" WHERE id=${literal(a.id)} AND email=${literal(a.email)} AND role IS NULL;`])].join('\n')+'\n';
  // No parent directory is created implicitly; caller supplies a private custody root.
  await mkdir(directory,{mode:0o700});
  async function write(name,value) {
    const f=await open(join(directory,name),'wx',0o600);let primary;
    try{await f.writeFile(value);await f.sync();}catch(error){primary=error;}
    let closing;try{await f.close();}catch(error){closing=error;}
    if(primary&&closing)throw new AggregateError([primary,closing],'Private fixture write and close failed');if(primary)throw primary;if(closing)throw closing;
  }
  // On failure retain created private state; never claim completed custody or delete it.
  await write('fixture.json',JSON.stringify({origin,callback,clientId,accounts},null,2)+'\n');
  await write('seed-query.json',JSON.stringify({batch},null,2)+'\n');await write('seed.sql',sql);await write('rollback.sql',rollback);
  const manifest={runId,databaseId,origin,callback,accounts:accounts.map(a=>({id:a.id,credentialId:a.credentialId})),clientId,clientRowId,linkId,rows:{users:2,credentials:2,clients:1,resourceLinks:1},seedBodySha256:createHash('sha256').update(JSON.stringify({batch},null,2)+'\n').digest('hex'),seedSqlSha256:createHash('sha256').update(sql).digest('hex'),rollbackSqlSha256:createHash('sha256').update(rollback).digest('hex'),qualification:'Prepared private fixture only; no provider mutation or account/client provisioning acceptance'};
  await write('manifest.json',JSON.stringify(manifest,null,2)+'\n');return manifest;
}
if(process.argv[1]&&pathToFileURL(process.argv[1]).href===import.meta.url) {
  if(process.argv.length!==4)throw new Error('Usage: node deployment/https/prepare-fixture.mjs ACTUAL_RESOURCE_SELECT.json NEW_PRIVATE_DIRECTORY');
  const manifest=await prepare(resolve(process.argv[3]),JSON.parse(await readFile(process.argv[2],'utf8')));console.log(JSON.stringify(manifest));
}
