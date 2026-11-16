export const encoder = new TextEncoder();
export const AUTH_ORIGIN = 'https://cake-id-release-validation.jcbailey008.workers.dev';
export const SCOPES = 'openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke';
export const SESSION_COOKIE = '__Host-cake_session';
export const FLOW_COOKIE = '__Host-cake_oauth';
export function escapeHtml(value = '') { return String(value).replace(/[&<>"']/g, x => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[x])); }
export function base64(bytes) { return btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,''); }
export function unbase64(value) { return Uint8Array.from(atob(value.replace(/-/g,'+').replace(/_/g,'/')), c=>c.charCodeAt(0)); }
export function randomToken() { return base64(crypto.getRandomValues(new Uint8Array(32))); }
export async function challenge(verifier) { return base64(await crypto.subtle.digest('SHA-256', encoder.encode(verifier))); }
export function cookie(request, name) { return request.headers.get('cookie')?.split(';').map(x=>x.trim()).find(x=>x.startsWith(name+'='))?.slice(name.length+1) ?? null; }
export function setCookie(name,value,maxAge=604800) { return `${name}=${value}; Path=/; Secure; HttpOnly; SameSite=Lax; Max-Age=${maxAge}`; }
export function json(value,status=200) { return Response.json(value,{status,headers:{'cache-control':'no-store'}}); }
export async function boundedJson(request, limit=16384) {
  if(!request.body) return null;
  const reader=request.body.getReader(); let size=0; const chunks=[];
  try { while(true) { const {done,value}=await reader.read(); if(done)break; size+=value.length; if(size>limit){await reader.cancel();return null;} chunks.push(value); } }
  finally {reader.releaseLock();}
  try { const data=new Uint8Array(size);let i=0;for(const c of chunks){data.set(c,i);i+=c.length;}return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(data)); }catch{return null;}
}
async function key(secret) {
  if(typeof secret!=='string'||secret.length<32)throw new Error('Session key unavailable');
  return crypto.subtle.importKey('raw',await crypto.subtle.digest('SHA-256',encoder.encode(secret)),{name:'AES-GCM'},false,['encrypt','decrypt']);
}
export async function seal(data,secret,origin) {
  const iv=crypto.getRandomValues(new Uint8Array(12));
  const encrypted=await crypto.subtle.encrypt({name:'AES-GCM',iv,additionalData:encoder.encode(origin)},await key(secret),encoder.encode(JSON.stringify(data)));
  return base64(iv)+'.'+base64(encrypted);
}
export async function unseal(value,secret,origin) {
  try { if(!value||value.length>3000)return null;const [iv,data]=value.split('.');return JSON.parse(new TextDecoder().decode(await crypto.subtle.decrypt({name:'AES-GCM',iv:unbase64(iv),additionalData:encoder.encode(origin)},await key(secret),unbase64(data)))); }catch{return null;}
}
export function allowedAccountRoute(path,method) {
  return (method==='GET'&&['/api/account/current','/api/account/profile','/api/account/sessions','/api/account/theme','/api/account/export'].includes(path)) ||
    (method==='PATCH'&&['/api/account/profile','/api/account/theme'].includes(path)) ||
    (method==='POST'&&['/api/account/signout','/api/account/revoke-other-sessions'].includes(path)) ||
    (method==='DELETE'&&/^\/api\/account\/sessions\/[a-f0-9-]{36}$/.test(path));
}
export function validTokenSet(data) {return data&&typeof data.access_token==='string'&&data.access_token.length<8192&&typeof data.refresh_token==='string'&&data.refresh_token.length<8192&&Number.isFinite(data.expires_in)&&data.expires_in>0;}
