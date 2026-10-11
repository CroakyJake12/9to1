import { readFile } from 'node:fs/promises';
import { createHash, randomUUID, sign } from 'node:crypto';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
export async function signedHeaders(config, method, pathname, body, nonce=randomUUID(), stamp=String(Date.now())) {
  const digest=createHash('sha256').update(body).digest('hex');
  let key;
  if(config.key_format==='dpapi'){
    const helper=path.join(path.dirname(fileURLToPath(import.meta.url)),'Read-SigningKey.ps1');
    key=Buffer.from((await promisify(execFile)(config.powershell,['-NoProfile','-File',helper,'-KeyPath',config.signing_key],{windowsHide:true,maxBuffer:16384})).stdout.trim());
  }else key=await readFile(config.signing_key);
  const signature=sign('RSA-SHA256',Buffer.from(`${stamp}\n${nonce}\n${method}\n${pathname}\n${digest}`),key).toString('base64');key.fill(0);
  return {'content-type':'application/json','x-relay-key':config.actor??'local','x-relay-time':stamp,'x-relay-nonce':nonce,'x-relay-signature':signature};
}
export async function request(config, action, payload) {
  if(!config.coordinator_url || !/^https:\/\//.test(config.coordinator_url) && !/^http:\/\/127\.0\.0\.1:\d+$/.test(config.coordinator_url))throw new Error('Authenticated HTTPS coordinator is not configured');
  const method=payload===undefined?'GET':'POST',body=payload===undefined?'':JSON.stringify(payload),pathname='/'+action;
  const r=await fetch(config.coordinator_url+pathname,{method,headers:await signedHeaders(config,method,pathname,body),...(method==='POST'?{body}:{}),signal:AbortSignal.timeout(20000)});
  const result=await r.json(); if(!r.ok)throw new Error(result.error??`Coordinator HTTP ${r.status}`);return result.state;
}
