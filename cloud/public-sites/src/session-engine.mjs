import {AUTH_ORIGIN,allowedAccountRoute,boundedJson,json,validTokenSet} from './security.mjs';
const WEEK=7*24*60*60*1000;
// One engine per opaque browser session. The queue serializes refresh-token rotation.
export class SessionEngine {
  constructor(storage,env){this.storage=storage;this.env=env;this.queue=Promise.resolve();}
  exclusive(fn){const operation=this.queue.then(fn,fn);this.queue=operation.catch(()=>{});return operation;}
  async create(tokens,clientId,origin){return this.exclusive(async()=>{
    if(!validTokenSet(tokens)||!clientId||!['https://cakemods.com','https://9to1.uk'].includes(origin))return false;
    if(await this.storage.get('session'))return false;
    const now=Date.now();await this.storage.put('session',{access:tokens.access_token,refresh:tokens.refresh_token,clientId,origin,expires:now+tokens.expires_in*1000,deadline:now+WEEK});
    await this.storage.setAlarm(now+WEEK);return true;
  });}
  async request(path,method,body,origin){return this.exclusive(async()=>{
    if(!allowedAccountRoute(path,method))return json({error:'not_found'},404);
    let s=await this.storage.get('session');
    if(!s||s.origin!==origin||Date.now()>=s.deadline){if(s&&Date.now()>=s.deadline)await this.storage.deleteAll();return json({error:'sign_in_required'},401);}
    // A previous instance may have stopped after the provider rotated its token.
    // Never send the old token again when the outcome is unknown.
    if(s.refreshing){await this.storage.deleteAll();return json({error:'sign_in_required'},401);}
    if(Date.now()>s.expires-30000){
      const unavailable=()=>{const r=json({error:'service_unavailable'},503);r.headers.set('retry-after',String(Math.max(1,Math.ceil(((s.retryAfter??Date.now()+30000)-Date.now())/1000))));return r;};
      if(Number.isFinite(s.retryAfter)&&Date.now()<s.retryAfter)return unavailable();
      await this.storage.put('session',{...s,refreshing:true});
      let r,token;
      try{
        r=await this.env.IDENTITY.fetch(AUTH_ORIGIN+'/api/auth/oauth2/token',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:new URLSearchParams({grant_type:'refresh_token',refresh_token:s.refresh,client_id:s.clientId,resource:AUTH_ORIGIN}).toString(),redirect:'manual'});
        // Definitive rate-limit/server errors do not revoke credentials. Wait
        // before a later request retries instead of repeating in this request.
        if(r.status===429||(r.status>=500&&r.status<=599)){
          const seconds=Number(r.headers.get('retry-after'));
          s={...s,refreshing:false,retryAfter:Date.now()+Math.min(300,Math.max(5,Number.isFinite(seconds)&&seconds>0?seconds:30))*1000};
          await this.storage.put('session',s);
          await r.body?.cancel().catch(()=>{});
          return unavailable();
        }
        token=await boundedJson(r,32768);
      }catch{
        await this.storage.deleteAll();
        return json({error:'sign_in_required'},401);
      }
      if(!r.ok||!validTokenSet(token)){await this.storage.deleteAll();return json({error:'sign_in_required'},401);}
      s={...s,access:token.access_token,refresh:token.refresh_token,expires:Date.now()+token.expires_in*1000,refreshing:false,retryAfter:0};
      await this.storage.put('session',s);
    }
    const r=await this.env.IDENTITY.fetch(AUTH_ORIGIN+path,{method,headers:{authorization:'Bearer '+s.access,'content-type':'application/json'},...(body===null?{}:{body:JSON.stringify(body)}),redirect:'manual'});
    if(r.status===401)await this.storage.deleteAll();
    if(path==='/api/account/signout'&&r.ok)await this.storage.deleteAll();
    const headers={'content-type':'application/json; charset=utf-8','cache-control':'private, no-store','x-content-type-options':'nosniff'};
    if(path==='/api/account/export'&&r.ok)headers['content-disposition']='attachment; filename="9to1-account-export.json"';
    return new Response(r.body,{status:r.status,headers});
  });}
  async alarm(){await this.storage.deleteAll();}
}
