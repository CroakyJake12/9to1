import { bindLoginSubmit } from './login-submit.mjs';
export function createOidcBeginTransport(fetch, navigate, approved, csrf) {
  return { async submit(input, signal) {
    if (!approved || !csrf || typeof input.nonce !== 'string') return {kind:'unavailable'};
    const response = await fetch('/remote/oidc/begin', {
      method:'POST', credentials:'same-origin', redirect:'error', signal,
      headers:{'Content-Type':'application/json','X-CSRF-Token':csrf},
      body:JSON.stringify({requestNonce:input.nonce})
    });
    if (response.status !== 200) return {kind:response.status===503?'unavailable':'unknown'};
    if (response.headers.get('content-type')?.split(';')[0].trim() !== 'application/json') return {kind:'unknown'};
    const declared=response.headers.get('content-length');
    if (declared!==null && (!/^\d+$/.test(declared) || Number(declared)>16384)) return {kind:'unknown'};
    if (!response.body) return {kind:'unknown'};
    const reader=response.body.getReader();let length=0;const parts=[];
    try { for (;;) {
      const {value,done}=await reader.read();if(done)break;
      length+=value.byteLength;if(length>16384){await reader.cancel();return {kind:'unknown'};}parts.push(value);
    }} finally { reader.releaseLock(); }
    if (signal.aborted) return {kind:'unknown'};
    const body=new Uint8Array(length);let offset=0;for(const part of parts){body.set(part,offset);offset+=part.byteLength;}
    let data;try { data=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(body)); } catch {return {kind:'unknown'};}
    if (typeof data.authorizationUri!=='string' || data.authorizationUri.length>8192) return {kind:'unknown'};
    let url, expected;try {url=new URL(data.authorizationUri);expected=new URL(approved.authorizationEndpoint);} catch{return {kind:'unknown'};}
    if (url.protocol!=='https:' || url.username || url.password || url.hash || url.origin!==expected.origin || url.pathname!==expected.pathname
      || url.searchParams.get('client_id')!==approved.clientId || url.searchParams.get('response_type')!=='code'
      || url.searchParams.get('code_challenge_method')!=='S256' || !url.searchParams.get('state') || !url.searchParams.get('nonce') || !url.searchParams.get('code_challenge')) return {kind:'unknown'};
    if(signal.aborted)return {kind:'unknown'};
    navigate(url.href);return {kind:'browser-authorization-started'};
  }};
}
// Wire the real form event handler to an explicit approved server-host discovery configuration.
// No password, bearer, PKCE verifier, or account authority is exposed to this page.
export function mountOidcSignIn(form,button,report,fetch,navigate,approved,csrf) {
 return bindLoginSubmit(form,button,()=>({}),createOidcBeginTransport(fetch,navigate,approved,csrf),report);
}
