import { DurableObject } from 'cloudflare:workers';
import { initialState, transition } from './policy.mjs';
import {verifyPublishedSource} from './git-remote.mjs';

const algorithm = { name:'RSASSA-PKCS1-v1_5',hash:'SHA-256' };
const decode = b => Uint8Array.from(atob(b), c => c.charCodeAt(0));
const hex = b => Array.from(new Uint8Array(b), n => n.toString(16).padStart(2,'0')).join('');

export class RelayCoordinator extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    ctx.storage.sql.exec('CREATE TABLE IF NOT EXISTS relay_state (id INTEGER PRIMARY KEY CHECK(id=1), value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS nonces (nonce TEXT PRIMARY KEY, expires INTEGER NOT NULL)');
  }
  handle(action, payload, actor, nonce, now) {
    return this.ctx.storage.transactionSync(() => {
      this.ctx.storage.sql.exec('DELETE FROM nonces WHERE expires < ?', now);
      if (this.ctx.storage.sql.exec('SELECT nonce FROM nonces WHERE nonce=?',nonce).toArray().length) return { status:409,error:'Signed request was replayed' };
      this.ctx.storage.sql.exec('INSERT INTO nonces VALUES (?,?)',nonce,now+120000);
      const row=this.ctx.storage.sql.exec('SELECT value FROM relay_state WHERE id=1').toArray()[0];
      const s=row?JSON.parse(row.value):initialState();
      if(action==='state') return {status:200,state:s};
      try {transition(s,action,payload,actor,now);} catch(e){return {status:e.status??400,error:e.message};}
      this.ctx.storage.sql.exec('INSERT INTO relay_state VALUES (1,?) ON CONFLICT(id) DO UPDATE SET value=excluded.value',JSON.stringify(s));
      return {status:200,state:s};
    });
  }
}

export default {
  async fetch(request,env) {
    const url=new URL(request.url);
    if(url.pathname==='/health' && request.method==='GET') return Response.json({status:'ok',service:'9to1-relay',version:1});
    if(!['GET','POST'].includes(request.method))return new Response('Method not allowed',{status:405});
    if(Number(request.headers.get('content-length')??0)>262144)return new Response('Request too large',{status:413});
    let raw=''; const decoder=new TextDecoder(); for await(const chunk of request.body??[]){raw+=decoder.decode(chunk,{stream:true});if(raw.length>262144)return new Response('Request too large',{status:413});}raw+=decoder.decode();
    const actor=request.headers.get('x-relay-key'), stamp=request.headers.get('x-relay-time'),nonce=request.headers.get('x-relay-nonce'), signature=request.headers.get('x-relay-signature');
    const now=Date.now();
    if(!actor || !['local','cloud'].includes(actor) || !nonce || !/^[a-zA-Z0-9-]{16,80}$/.test(nonce) || !stamp || !Number.isFinite(Number(stamp)) || Math.abs(now-Number(stamp))>60000 || !signature)return new Response('Unauthorized',{status:401});
    try {
      const keys=JSON.parse(env.PUBLIC_KEYS??'{}');
      if(!keys[actor])return new Response('Unconfigured signing identity',{status:401});
      const key=await crypto.subtle.importKey('spki',decode(keys[actor]),algorithm,false,['verify']);
      const digest=hex(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(raw)));
      const canonical=`${stamp}\n${nonce}\n${request.method}\n${url.pathname}\n${digest}`;
      if(!await crypto.subtle.verify(algorithm,key,decode(signature),new TextEncoder().encode(canonical)))return new Response('Unauthorized',{status:401});
      const payload=raw?JSON.parse(raw):{};
      const action=url.pathname.replace(/^\//,'');
      if(request.method==='GET' && action!=='state')return new Response('Unsupported route',{status:404});
      if(['handover','ack','ack-recovery'].includes(action)) {
        let source=payload.handover?.source;
        if(action==='ack'||action==='ack-recovery') { const current=await env.COORDINATOR.getByName('CroakyJake12/9to1').handle('state',{},actor,crypto.randomUUID(),now);source=current.state.handovers.find(h=>h.id===payload.handover_id)?.source; }
        const verification=await verifyPublishedSource(source,fetch,{allowAncestor:action!=='handover'});
        payload.remote_verified=verification.verified;
        if(!verification.verified)return Response.json({status:409,error:`Exact remote source verification failed (GitHub HTTP ${verification.http_status??'none'}, remaining ${verification.rate_limit_remaining??'unknown'}); ownership retained`,verification},{status:409,headers:{'Cache-Control':'no-store'}});
      }
      const result=await env.COORDINATOR.getByName('CroakyJake12/9to1').handle(action,payload,actor,nonce,now);
      return Response.json(result,{status:result.status,headers:{'Cache-Control':'no-store'}});
    }catch(e){return Response.json({error:e.name==='SyntaxError'?'Invalid JSON':'Request could not be validated'},{status:400});}
  }
};
