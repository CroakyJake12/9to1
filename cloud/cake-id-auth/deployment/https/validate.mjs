import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';

export const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
const issuer=`${origin}/api/auth`;
export async function validate({request=fetch,output}={}) {
  const observations=[];
  let assertions=0;
  const equal=(actual,expected,message)=>{assert.equal(actual,expected,message);assertions++;};
  const ok=(value,message)=>{assert.ok(value,message);assertions++;};
  async function get(path,options={}) {
    const url=new URL(path,origin);
    equal(url.origin,origin,'No endpoint may redirect requests to another authority');
    const response=await request(url,{...options,redirect:'manual',credentials:'omit',signal:AbortSignal.timeout(15000)});
    // Public diagnostics exclude bodies, cookies, credentials and URL queries.
    observations.push({path:url.pathname,method:options.method??'GET',status:response.status,acao:response.headers.get('access-control-allow-origin'),acac:response.headers.get('access-control-allow-credentials')});
    ok(response.status<300||response.status>=400,'Unexpected redirect is not followed');
    return response;
  }
  function publicCors(response,allowed=true) {
    equal(response.headers.get('access-control-allow-origin'),allowed?origin:null);
    equal(response.headers.get('access-control-allow-credentials'),null);
    ok(response.headers.get('vary')?.toLowerCase().split(',').map(x=>x.trim()).includes('origin'));
  }
  let primary;
  try {
    const metadataResponse=await get('/api/auth/.well-known/openid-configuration',{headers:{Origin:origin}});
    equal(metadataResponse.status,200);publicCors(metadataResponse);
    const metadata=await metadataResponse.json();equal(metadata.issuer,issuer);
    for(const [field,path] of [['authorization_endpoint','/api/auth/oauth2/authorize'],['token_endpoint','/api/auth/oauth2/token'],['jwks_uri','/api/auth/jwks']]) equal(metadata[field],`${origin}${path}`);
    ok(metadata.code_challenge_methods_supported?.includes('S256'));
    ok(metadata.response_types_supported?.includes('code'));
    const jwksResponse=await get('/api/auth/jwks',{headers:{Origin:origin}});equal(jwksResponse.status,200);publicCors(jwksResponse);
    const jwks=await jwksResponse.json();ok(Array.isArray(jwks.keys)&&jwks.keys.length>0);
    for(const key of jwks.keys) {ok(typeof key.kid==='string'&&key.kid.length>0);ok(!['d','p','q','dp','dq','qi','oth','k'].some(field=>Object.hasOwn(key,field)),'JWKS contains public material only');}
    for(const path of ['/api/auth/.well-known/openid-configuration','/api/auth/jwks']) {
      const preflight=await get(path,{method:'OPTIONS',headers:{Origin:origin,'Access-Control-Request-Method':'GET'}});equal(preflight.status,204);publicCors(preflight);equal(preflight.headers.get('access-control-allow-methods'),'GET, OPTIONS');
      for(const foreign of ['https://foreign.example.test','null','*']) {const response=await get(path,{headers:{Origin:foreign}});equal(response.status,200);publicCors(response,false);}
    }
    for(const path of ['/sign-in','/sign-up','/forgot-password','/reset-password']) {
      const page=await get(path);equal(page.status,200);equal(page.headers.get('cache-control'),'no-store');equal(page.headers.get('referrer-policy'),'no-referrer');equal(page.headers.get('x-content-type-options'),'nosniff');
      ok(page.headers.get('content-security-policy')?.includes("frame-ancestors 'none'"));
    }
    const noSession=await get('/api/account/current',{headers:{Origin:origin}});equal(noSession.status,401);equal((await noSession.json()).error,'unauthorized');equal(noSession.headers.get('cache-control'),'no-store');
    for(const path of ['/api/auth/jwks/extra','/api/auth/oauth2/token/extra','/api/auth/get-session']) {const response=await get(path,{headers:{Origin:origin}});equal(response.headers.get('access-control-allow-origin'),null);}
    const preflight=await get('/api/auth/oauth2/token',{method:'OPTIONS',headers:{Origin:origin,'Access-Control-Request-Method':'POST','Access-Control-Request-Headers':'content-type'}});equal(preflight.status,204);publicCors(preflight);equal(preflight.headers.get('access-control-allow-methods'),'POST, OPTIONS');
    const foreign=await get('/api/auth/oauth2/token',{method:'POST',headers:{Origin:'https://foreign.example.test','Content-Type':'application/x-www-form-urlencoded'},body:'grant_type=authorization_code'});equal(foreign.status,403);publicCors(foreign,false);equal((await foreign.json()).error,'origin_not_allowed');
    const invalid=await get('/api/auth/oauth2/token',{method:'POST',headers:{Origin:origin,'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({grant_type:'authorization_code',client_id:'unregistered-isolated-negative-client',code:'invalid-isolated-negative-code',code_verifier:'a'.repeat(43)})});equal(invalid.status,400);publicCors(invalid);
    const rejection=await invalid.json();ok(['invalid_client','invalid_grant','invalid_request'].includes(rejection.error),'Maintained OAuth error denies an unregistered client/invalid code');ok(!rejection.access_token&&!rejection.refresh_token&&!rejection.id_token);
    // Schema validation rejects before reset-token consumption, password changes or delivery.
    const reset=await get('/api/auth/reset-password',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json'},body:'{}'});equal(reset.status,400);
    const result={result:'passed',assertions,observations,issuer,operatorPinnedDeploymentVersion:'713c1695-7e21-489b-bc97-906450972cfa',qualification:'Anonymous deployed HTTPS phase only; deployment version is operator-pinned, not observed by HTTP. Root must bind same-time provider readback. No users/client provisioning, email delivery, valid OAuth tokens, session journeys or native interoperability accepted.'};
    if(output) await writeFile(output,JSON.stringify(result,null,2)+'\n',{flag:'wx'});
    return result;
  } catch(error) {
    primary=error;
    if(output) try {await writeFile(output,JSON.stringify({result:'failed',assertions,observations,error:{name:error.name,message:error.message}},null,2)+'\n',{flag:'wx'});}catch(cleanup){throw new AggregateError([primary,cleanup],'Validation and evidence write failed');}
    throw primary;
  }
}
if(process.argv[1]&&pathToFileURL(process.argv[1]).href===import.meta.url) {
  if(process.argv.length!==3) throw new Error('Usage: node deployment/https/validate.mjs NEW_PUBLIC_RESULT.json');
  console.log(JSON.stringify(await validate({output:process.argv[2]})));
}
