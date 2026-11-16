import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';

async function load(source,plugins=[]){
 const result=await build({entryPoints:[source],bundle:true,write:false,platform:'node',format:'esm',plugins});
 return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64')+'#'+crypto.randomUUID());
}
const {readPublicApp,publicAppPath,publicAppDestination}=await load('src/public-app.ts');
const pages=await load('src/pages.ts');
const origin='https://cake-id-release-validation.jcbailey008.workers.dev';
test('only two fixed production destinations can be selected',()=>{
 assert.equal(publicAppDestination(readPublicApp(new URLSearchParams('app=cake'))),'https://cakemods.com/auth/sign-in');
 assert.equal(publicAppDestination(readPublicApp(new URLSearchParams('app=nine'))),'https://9to1.uk/auth/sign-in');
 for(const app of ['https://evil.example','//evil.example','javascript:alert(1)','CAKE','cake/../../evil','nine&returnTo=https://evil.example']){
  assert.equal(readPublicApp(new URLSearchParams({app})),null);assert.equal(publicAppDestination(null),'/account');
 }
});
test('OAuth navigation recognizes only exact callbacks and ignores arbitrary return URLs',()=>{
 for(const [callback,expected] of [['https://cakemods.com/oauth/callback','cake'],['https://9to1.uk/oauth/callback','nine'],
   ['https://cakemods.com.evil.example/oauth/callback',null],['https://cakemods.com/oauth/callback?next=evil',null],
   ['http://9to1.uk/oauth/callback',null],['https://www.9to1.uk/oauth/callback',null]]){
  assert.equal(readPublicApp(new URLSearchParams({redirect_uri:callback,returnTo:'https://evil.example'})),expected);
 }
 assert.equal(readPublicApp(new URLSearchParams({app:'evil',redirect_uri:'https://cakemods.com/oauth/callback'})),null);
});
test('signup, recovery and reset navigation retain only the approved app selector',async()=>{
 for(const app of ['cake','nine']){
  assert.equal(publicAppPath('/sign-in',app),'/sign-in?app='+app);
  const signin=await pages.signInPage(app).text();assert.ok(signin.includes('href="/sign-up?app='+app+'"'));assert.ok(signin.includes('href="/forgot-password?app='+app+'"'));
  for(const fn of ['signUpPage','forgotPasswordPage','resetPasswordPage'])assert.ok((await pages[fn](app).text()).includes('href="/sign-in?app='+app+'"'));
 }
});

async function runForm(id,query,auth,check){
 const keys=['document','location','FormData','__flowAuth'];const original=new Map(keys.map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
 let submit;const status={textContent:''},button={disabled:false};const form={dataset:{},querySelector:()=>button,addEventListener:(type,fn)=>{if(type==='submit')submit=fn;}};
 const assigned=[];
 Object.defineProperty(globalThis,'document',{configurable:true,value:{querySelector:()=>status,getElementById:target=>target===id?form:target.startsWith('turnstile-')?{dataset:{turnstileRequired:'false'}}:null}});
 Object.defineProperty(globalThis,'location',{configurable:true,value:{origin,search:query,assign:value=>assigned.push(value)}});
 Object.defineProperty(globalThis,'FormData',{configurable:true,value:class{get(field){return {name:'  Example  ',username:'  example  ',email:'  test@example.test  ',password:'  a-long-example-password  '}[field]??null;}}});
 globalThis.__flowAuth=auth;
 const stub={name:'isolated-auth-transport',setup(b){
  b.onResolve({filter:/^(better-auth\/client|@better-auth\/oauth-provider\/client)$/},args=>({path:args.path,namespace:'flow-stub'}));
  b.onLoad({filter:/.*/,namespace:'flow-stub'},args=>({contents:args.path==='better-auth/client'?'export function createAuthClient(){return globalThis.__flowAuth;}':'export function oauthProviderClient(){return {};}',loader:'js'}));
 }};
 try{await load('src/browser.ts',[stub]);assert.equal(typeof submit,'function');await submit({preventDefault(){}});await check({assigned,status,button});}
 finally{for(const [key,descriptor] of original){if(descriptor)Object.defineProperty(globalThis,key,descriptor);else delete globalThis[key];}}
}
test('standalone sign-in starts the chosen website PKCE flow while provider redirects remain authoritative',async()=>{
 for(const [app,destination] of [['cake','https://cakemods.com/auth/sign-in'],['nine','https://9to1.uk/auth/sign-in'],['evil','/account']]){
  await runForm('sign-in-form','?app='+app,{signIn:{email:async()=>({data:{},error:null})}},({assigned})=>assert.deepEqual(assigned,[destination]));
 }
 await runForm('sign-in-form','?app=cake',{signIn:{email:async()=>({data:{redirect:true,url:'https://cakemods.com/oauth/callback?code=opaque'},error:null})}},({assigned})=>assert.deepEqual(assigned,[]));
});
test('verification and password recovery callbacks stay on issuer with an allowlisted app',async()=>{
 for(const app of ['cake','nine','https://evil.example']){
  const suffix=app==='cake'||app==='nine'?'?app='+app:'';let signup,recovery;
  await runForm('sign-up-form','?'+new URLSearchParams({app}),{$fetch:async(path,request)=>{assert.equal(path,'/sign-up/email');signup=request.body;return {error:null};}},()=>{
   assert.equal(signup.callbackURL,origin+'/sign-in'+suffix);
  });
  await runForm('forgot-password-form','?'+new URLSearchParams({app}),{requestPasswordReset:async request=>{recovery=request;return {error:null};}},()=>{
   assert.equal(recovery.redirectTo,origin+'/reset-password'+suffix);
  });
 }
});
test('sign-in, signup and reset preserve the exact password including whitespace',async()=>{
 const expected='  a-long-example-password  ';
 await runForm('sign-in-form','?app=cake',{signIn:{email:async request=>{assert.equal(request.password,expected);assert.equal(request.email,'test@example.test');return {data:{},error:null};}}},()=>{});
 await runForm('sign-up-form','?app=cake',{$fetch:async(path,request)=>{assert.equal(request.body.password,expected);assert.equal(request.body.email,'test@example.test');assert.equal(request.body.name,'Example');assert.equal(request.body.username,'example');return {error:null};}},()=>{});
 await runForm('reset-password-form','?app=cake&token=synthetic-token',{resetPassword:async request=>{assert.equal(request.newPassword,expected);assert.equal(request.token,'synthetic-token');return {error:null};}},()=>{});
});
test('password recovery errors do not falsely claim that a reset link will be sent',async()=>{
 for(const status of [429,500])await runForm('forgot-password-form','?app=cake',{
  requestPasswordReset:async()=>({error:{status,message:'private-delivery-detail'}})
 },({status:message})=>{assert.match(message.textContent,/could not request a reset link/);assert.doesNotMatch(message.textContent,/will send|private-delivery-detail/);});
});
