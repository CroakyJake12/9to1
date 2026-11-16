import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
import {oauthProviderClient} from '@better-auth/oauth-provider/client';

async function load(source,plugins=[]){
 const result=await build({entryPoints:[source],bundle:true,write:false,platform:'node',format:'esm',plugins,
  banner:{js:"import {createRequire as __testCreateRequire} from 'node:module';const require=__testCreateRequire(process.cwd()+'/package.json');"}});
 return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64')+'#'+crypto.randomUUID());
}
const {createAuthOptions,googleCredentials}=await load('src/auth.ts');
const {googleFlowCallbacks}=await load('src/public-app.ts');
const {signInPage,signUpPage,forgotPasswordPage}=await load('src/pages.ts');
const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
const configured={GOOGLE_CLIENT_ID:'synthetic.apps.googleusercontent.com',GOOGLE_CLIENT_SECRET:'synthetic-provider-config-only'};
function env(extra={}){return {AUTH_BASE_URL:origin,API_RESOURCE:origin,AUTH_SECRET:'synthetic-config-only-secret-'.repeat(3),
 APP_MODE:'production',DB:{prepare(sql){assert.match(sql,/^SELECT 1 FROM user WHERE id = \? AND role = 'admin'/);return {bind(id){return {first:async()=>id==='existing-admin'?{one:1}:null};}};}},...extra};}

test('missing or partial Google settings never enable a provider or a Google button',async()=>{
 for(const settings of [{},{GOOGLE_CLIENT_ID:configured.GOOGLE_CLIENT_ID},{GOOGLE_CLIENT_SECRET:configured.GOOGLE_CLIENT_SECRET},
  {...configured,GOOGLE_CLIENT_ID:' '},{...configured,GOOGLE_CLIENT_SECRET:' '}]){
  assert.equal(googleCredentials(settings),null);const options=createAuthOptions(env(settings));
  assert.deepEqual(options.socialProviders,{});assert.equal(options.emailAndPassword.enabled,true);
  for(const page of [signInPage,signUpPage,forgotPasswordPage])assert.doesNotMatch(await page('cake',googleCredentials(settings)!==null).text(),/id="google-sign-in"/);
 }
});
test('configured Google uses the maintained provider and exact issuer callback without changing issuer/session authority',()=>{
 const options=createAuthOptions(env(configured));
 assert.equal(options.appName,'9to1 Account');assert.equal(options.baseURL,origin);assert.equal(options.basePath,'/api/auth');
 assert.equal(options.socialProviders.google.redirectURI,origin+'/api/auth/callback/google');
 assert.equal(options.socialProviders.google.clientId,configured.GOOGLE_CLIENT_ID);
 assert.equal(options.socialProviders.google.clientSecret,configured.GOOGLE_CLIENT_SECRET);
 assert.deepEqual(options.trustedOrigins,[origin]);assert.equal(options.account.encryptOAuthTokens,true);
 assert.deepEqual(options.account.accountLinking,{enabled:true,trustedProviders:[],requireLocalEmailVerified:true,allowDifferentEmails:false,updateUserInfoOnLink:false});
 assert.deepEqual(options.user.additionalFields.role,{type:'string',required:false,input:false,returned:false,defaultValue:'user'});
});
test('Google requires verified email and cannot manufacture canonical administrator authority',async()=>{
 const options=createAuthOptions(env(configured));
 for(const emailVerified of [false,undefined,'true'])assert.equal((await options.user.validateUserInfo({user:{emailVerified},source:{action:'create-user',method:'oauth',oauth:{providerId:'google',profile:{}}}})).error,'google_email_not_verified');
 assert.equal((await options.user.validateUserInfo({user:{emailVerified:true},source:{action:'create-user',method:'oauth',oauth:{providerId:'google',profile:{}}}})).error,'invitation_required');
 assert.equal(options.socialProviders.google.disableSignUp,true);
 assert.equal(await options.user.validateUserInfo({user:{emailVerified:true},source:{action:'link-account',method:'oauth',oauth:{providerId:'google',profile:{}}}}),undefined);
 const provider=options.plugins.find(p=>p.id==='oauth-provider');
 assert.equal(await provider.options.clientPrivileges({user:{id:'ordinary',role:'admin',email:'admin@example.test'}}),false);
 assert.equal(await provider.options.resourcePrivileges({user:{id:'existing-admin'}}),true);
});
test('Google continuation stays same-origin and error retry retains the signed provider query',()=>{
 const search='?client_id=public&redirect_uri=https%3A%2F%2F9to1.uk%2Foauth%2Fcallback&state=opaque&code_challenge=proof&code_challenge_method=S256&sig=signature&ba_param=client_id&ba_param=state&ba_param=code_challenge';
 const callbacks=googleFlowCallbacks(origin,search);const success=new URL(callbacks.callbackURL),error=new URL(callbacks.errorCallbackURL);
 assert.equal(success.origin,origin);assert.equal(success.pathname,'/sign-in');assert.equal(success.searchParams.get('app'),'nine');assert.equal(success.searchParams.get('social'),'complete');
 for(const key of ['client_id','redirect_uri','state','code_challenge','code_challenge_method','sig'])assert.equal(error.searchParams.get(key),new URLSearchParams(search).get(key));
 assert.deepEqual(error.searchParams.getAll('ba_param'),new URLSearchParams(search).getAll('ba_param'));assert.equal(error.searchParams.get('social'),'error');
 for(const malicious of ['https://evil.example','//evil.example','javascript:alert(1)']){
  const callback=new URL(googleFlowCallbacks(origin,'?'+new URLSearchParams({app:malicious,returnTo:malicious})).callbackURL);
  assert.equal(callback.origin,origin);assert.equal(callback.pathname,'/sign-in');assert.equal(callback.searchParams.get('app'),null);
 }
});
test('installed OAuth client carries original PKCE state through the social sign-in request',async()=>{
 const previous=Object.getOwnPropertyDescriptor(globalThis,'window');
 const search='?client_id=public&state=opaque&code_challenge=proof&code_challenge_method=S256&sig=signature&ba_param=client_id&ba_param=state&ba_param=code_challenge&ba_param=code_challenge_method&social=error';
 Object.defineProperty(globalThis,'window',{configurable:true,value:{location:{search}}});
 try{
  const ctx={headers:new Headers({'content-type':'application/json'}),method:'POST',body:{provider:'google',callbackURL:origin+'/sign-in?social=complete'}};
  await oauthProviderClient().fetchPlugins[0].hooks.onRequest(ctx);
  const body=JSON.parse(ctx.body),query=new URLSearchParams(body.oauth_query);
  assert.equal(body.provider,'google');assert.equal(query.get('state'),'opaque');assert.equal(query.get('code_challenge'),'proof');assert.equal(query.get('code_challenge_method'),'S256');assert.equal(query.get('sig'),'signature');assert.equal(query.has('social'),false);
 }finally{if(previous)Object.defineProperty(globalThis,'window',previous);else delete globalThis.window;}
});

async function buttonAction(result,run){
 const keys=['document','location','__googleAuth'];const original=new Map(keys.map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
 let click;const button={disabled:false,addEventListener:(type,fn)=>{assert.equal(type,'click');click=fn;}},status={textContent:''},calls=[];
 const location={origin,search:'?app=cake',assign(){throw Error('provider owns navigation');}};
 Object.defineProperty(globalThis,'document',{configurable:true,value:{querySelector:()=>status,getElementById:id=>id==='google-sign-in'?button:null}});
 Object.defineProperty(globalThis,'location',{configurable:true,value:location});
 globalThis.__googleAuth={signIn:{social:async request=>{calls.push(request);if(result instanceof Error)throw result;return result;}}};
 const stub={name:'isolated-social-transport',setup(b){b.onResolve({filter:/^(better-auth\/client|@better-auth\/oauth-provider\/client)$/},args=>({path:args.path,namespace:'google-test'}));b.onLoad({filter:/.*/,namespace:'google-test'},args=>({contents:args.path==='better-auth/client'?'export function createAuthClient(){return globalThis.__googleAuth;}':'export function oauthProviderClient(){return {};}',loader:'js'}));}};
 try{await load('src/browser.ts',[stub]);await run({button,status,calls,location,click});}
 finally{for(const [key,descriptor] of original){if(descriptor)Object.defineProperty(globalThis,key,descriptor);else delete globalThis[key];}}
}
test('Google button uses the current tab query and suppresses duplicate clicks while redirecting',async()=>{
 await buttonAction({data:{redirect:true,url:'https://accounts.google.com/o/oauth2/v2/auth'},error:null},async({calls,location,click,button})=>{
  location.search='?app=nine';await click();await click();assert.equal(calls.length,1);assert.equal(calls[0].provider,'google');assert.equal(new URL(calls[0].callbackURL).searchParams.get('app'),'nine');assert.equal(button.disabled,true);
 });
});
test('Google provider errors, lost responses and malformed responses never report success',async()=>{
 for(const reply of [{error:{message:'private-provider-detail'}},new Error('network failure'),{data:{},error:null}])await buttonAction(reply,async({click,button,status})=>{
  await click();assert.equal(button.disabled,false);assert.match(status.textContent,/could not be started/);assert.doesNotMatch(status.textContent,/private-provider-detail|network failure/);
 });
});
