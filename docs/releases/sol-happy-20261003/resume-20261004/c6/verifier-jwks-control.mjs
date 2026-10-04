import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {oauthProviderResourceClient} from '/workspace/team-c-release/cloud/cake-id-auth/node_modules/@better-auth/oauth-provider/dist/client-resource.mjs';
const require=createRequire('/workspace/team-c-release/cloud/cake-id-auth/package.json');
const {generateKeyPair,exportJWK,SignJWT}=await import(require.resolve('jose'));
const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
const issuer=origin+'/api/auth';
const {privateKey,publicKey}=await generateKeyPair('EdDSA');
const jwk={...await exportJWK(publicKey),kid:'c6-fictional-key',alg:'EdDSA'};
const jwt=await new SignJWT({scope:'openid cake:account:read',sid:'0845433e-9d63-41b4-8469-65b253b14a30'}).setProtectedHeader({alg:'EdDSA',kid:jwk.kid}).setIssuer(issuer).setAudience(origin).setSubject('ee81bff0-1fdd-4f18-9738-b045d4a62b2a').setIssuedAt().setExpirationTime('5m').sign(privateKey);
const request=new Request(origin+'/api/account/current',{headers:{authorization:'Bearer '+jwt}});
const verifier=oauthProviderResourceClient().getActions();
const savedFetch=globalThis.fetch;
let requests=0;
const source=(status,body)=>async(url)=>{const actual=new URL(url);assert.equal(actual.origin,origin);assert.equal(actual.pathname,'/api/auth/jwks');requests++;return new Response(body,{status,headers:{'content-type':status===200?'application/json':'text/html'}})};
async function check(name,options,shouldPass){
 let accepted=false,errorName=null;
 try{const claims=await verifier.verifyAccessTokenRequest(request,options);assert.equal(claims.sub,'ee81bff0-1fdd-4f18-9738-b045d4a62b2a');accepted=true;}catch(e){errorName=e.name;}
 assert.equal(accepted,shouldPass,name);
 return {name,passed:true,accepted,errorName};
}
const base={verifyOptions:{audience:origin,issuer},requiredScopes:['cake:account:read']};
const observations=[];
try {
 globalThis.fetch=source(404,'Public endpoint not routed to worker');
 observations.push(await check('JWKS refusal denies an otherwise genuine local signed token',{...base,jwksUrl:issuer+'/jwks?c6=refusal'},false));
 globalThis.fetch=source(200,JSON.stringify({keys:[jwk]}));
 observations.push(await check('Same maintained verifier accepts with reachable public JWKS',{...base,jwksUrl:issuer+'/jwks?c6=success'},true));
 observations.push(await check('Same maintained verifier denies wrong API audience',{...base,jwksUrl:issuer+'/jwks?c6=audience',verifyOptions:{audience:'https://foreign.example.test',issuer}},false));
 observations.push(await check('Same maintained verifier denies missing required scope',{...base,jwksUrl:issuer+'/jwks?c6=scope',requiredScopes:['cake:profile:write']},false));
 console.log(JSON.stringify({result:'passed',requests,controls:observations,qualification:'C6 synthetic local signature/JWKS transport controls with actual pinned resource verifier; no real provider requests, D1, tokens, auth or hosted routing reproduction. Self-fetch remains runtime hypothesis.'}));
}
finally {globalThis.fetch=savedFetch;}
