import {AUTH_ORIGIN,SCOPES,SESSION_COOKIE,FLOW_COOKIE,randomToken,challenge,cookie,setCookie,json,boundedJson,seal,unseal,allowedAccountRoute,validTokenSet} from './security.mjs';
function redirect(location,cookies=[]){const h=new Headers({'location':location,'cache-control':'no-store'});for(const c of cookies)h.append('set-cookie',c);return new Response(null,{status:303,headers:h});}
export async function authRoute(request,env,origin,brand){
  const url=new URL(request.url),path=url.pathname;
  const clientId=brand==='cake'?env.CAKE_CLIENT_ID:env.NINE_CLIENT_ID;
  // Private testing still relies on the issuer's unchanged owner-only Access
  // gate. It does not publish sign-in links or open invitation redemption.
  if(['/auth/sign-in','/auth/sign-up','/auth/recover'].includes(path)&&request.method==='GET'&&!['invite-only','private-testing'].includes(env.ACCOUNTS_AVAILABLE))return redirect('/account?notice=not-ready');
  if(path==='/auth/sign-in'&&request.method==='GET'){
    if(!clientId||!env.SESSION_KEY)return redirect('/account?notice=not-ready');
    const state=randomToken(),verifier=randomToken();
    const returnTo=brand==='nine'&&url.searchParams.get('return')==='/files/'?'/files/':'/account';
    const flow=await seal({state,verifier,returnTo,created:Date.now()},env.SESSION_KEY,origin);
    const auth=new URL(AUTH_ORIGIN+'/api/auth/oauth2/authorize');
    auth.search=new URLSearchParams({client_id:clientId,redirect_uri:origin+'/oauth/callback',response_type:'code',scope:SCOPES,state,code_challenge:await challenge(verifier),code_challenge_method:'S256',resource:AUTH_ORIGIN}).toString();
    return redirect(auth.href,[setCookie(FLOW_COOKIE,flow,600)]);
  }
  if(path==='/auth/sign-up'&&request.method==='GET')return redirect(AUTH_ORIGIN+'/sign-up?app='+brand);
  if(path==='/auth/recover'&&request.method==='GET')return redirect(AUTH_ORIGIN+'/forgot-password?app='+brand);
  if(path==='/oauth/callback'&&request.method==='GET'){
    const clear=setCookie(FLOW_COOKIE,'',0),flow=await unseal(cookie(request,FLOW_COOKIE),env.SESSION_KEY,origin);
    if(!flow||typeof flow.state!=='string'||flow.state!==url.searchParams.get('state')||!Number.isFinite(flow.created)||Date.now()-flow.created>600000||flow.created>Date.now())return redirect('/account?notice=invalid-login',[clear]);
    const code=url.searchParams.get('code');if(!code||code.length>4096||url.searchParams.has('error'))return redirect('/account?notice=cancelled',[clear]);
    const r=await env.IDENTITY.fetch(AUTH_ORIGIN+'/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:new URLSearchParams({grant_type:'authorization_code',code,redirect_uri:origin+'/oauth/callback',client_id:clientId,code_verifier:flow.verifier,resource:AUTH_ORIGIN}).toString(),redirect:'manual'});
    const tokens=await boundedJson(r,32768);
    if(!r.ok||!validTokenSet(tokens))return redirect('/account?notice=login-failed',[clear]);
    // Ask the maintained resource server to validate signature, audience and active session.
    const current=await env.IDENTITY.fetch(AUTH_ORIGIN+'/api/account/current',{headers:{authorization:'Bearer '+tokens.access_token},redirect:'manual'});
    if(!current.ok)return redirect('/account?notice=login-failed',[clear]);
    await current.body?.cancel();
    const sid=randomToken();if(!await env.WEB_SESSIONS.getByName(sid).create(tokens,clientId,origin))return redirect('/account?notice=login-failed',[clear]);
    return redirect(brand==='nine'&&flow.returnTo==='/files/'?'/files/':'/account',[clear,setCookie(SESSION_COOKIE,sid)]);
  }
  if(path.startsWith('/api/account/')){
    if(!allowedAccountRoute(path,request.method))return json({error:'not_found'},404);
    if(!['GET','HEAD'].includes(request.method)&&(request.headers.get('origin')!==origin||request.headers.get('content-type')!=='application/json'))return json({error:'origin_not_allowed'},403);
    const sid=cookie(request,SESSION_COOKIE);if(!sid||!/^[\w-]{43}$/.test(sid))return json({error:'sign_in_required'},401);
    let body=null;if(['PATCH','POST'].includes(request.method)){body=await boundedJson(request,65536);if(body===null)return json({error:'invalid_request'},400);}
    const r=await env.WEB_SESSIONS.getByName(sid).request(path,request.method,body,origin);
    if(r.status===401||(path==='/api/account/signout'&&r.ok)){const headers=new Headers(r.headers);headers.append('set-cookie',setCookie(SESSION_COOKIE,'',0));return new Response(r.body,{status:r.status,headers});}
    return r;
  }
  return null;
}
