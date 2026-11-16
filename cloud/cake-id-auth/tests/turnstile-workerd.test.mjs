import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
import {Miniflare,convertV4MiniflareOptions} from 'miniflare';

// This isolates native fetch. SQLite replay and concurrency are covered in turnstile.test.mjs.
test('native Workerd accepts verifier transport and rejects redirects without following them',async()=>{
  const compiled=await build({stdin:{contents:`
    import {prepareTurnstile,verifyTurnstile} from './src/turnstile.ts';
    const DB={prepare(){return {bind(){return {first:async()=>null};}};},
      async batch(){return [{success:true,results:[]},{success:true,results:[{tokenHash:'synthetic-digest'}]}];}};
    export default {async fetch(request,bindings){
      if(new URL(request.url).pathname==='/__test/unsupported-redirect'){
        try{await fetch('https://challenges.cloudflare.com/turnstile/v0/siteverify',{redirect:'error'});return Response.json({rejected:false});}
        catch(error){return Response.json({rejected:error instanceof TypeError});}
      }
      const env={...bindings,DB};
      const prepared=await prepareTurnstile(request,env);
      if(prepared instanceof Response)return prepared;
      if(!prepared)return Response.json({error:'challenge unexpectedly omitted'},{status:500});
      const denied=await verifyTurnstile(prepared,request,env);
      return denied??Response.json({verified:true});
    }};
  `,resolveDir:process.cwd()},bundle:true,write:false,platform:'browser',format:'esm'});
  const calls=[];
  const mf=new Miniflare(convertV4MiniflareOptions({telemetry:{enabled:false},modules:true,
    compatibilityDate:'2026-10-01',compatibilityFlags:['nodejs_compat','global_fetch_strictly_public'],
    script:compiled.outputFiles[0].text,
    bindings:{APP_MODE:'production',AUTH_BASE_URL:'https://issuer.example.test',TURNSTILE_SITE_KEY:'synthetic-site-key',TURNSTILE_SECRET_KEY:'synthetic-local-secret'},
    outboundService:async request=>{
      calls.push(request.url);
      assert.equal(request.url,'https://challenges.cloudflare.com/turnstile/v0/siteverify');
      const form=await request.formData();
      const token=form.get('response');
      if(token==='redirect-token')return Response.redirect('https://must-not-follow.invalid/',302);
      return Response.json({success:true,action:token==='wrong-action-token'?'login':'recovery',
        hostname:'issuer.example.test',challenge_ts:new Date().toISOString()});
    },
  }));
  try{
    // Preserve the pre-fix failure as a control: rejected before provider I/O.
    const unsupported=await mf.dispatchFetch('https://issuer.example.test/__test/unsupported-redirect');
    assert.deepEqual(await unsupported.json(),{rejected:true});
    assert.equal(calls.length,0);
    const request=token=>mf.dispatchFetch('https://issuer.example.test/api/auth/request-password-reset',{
      method:'POST',headers:{origin:'https://issuer.example.test','content-type':'application/json'},
      body:JSON.stringify({email:'synthetic@example.test','cf-turnstile-response':token}),
    });
    const accepted=await request('accepted-token');
    assert.equal(accepted.status,200);
    assert.deepEqual(await accepted.json(),{verified:true});
    assert.equal(calls.length,1);
    assert.equal((await request('wrong-action-token')).status,403,'provider success cannot authorize another action');
    assert.equal(calls.length,2);
    assert.equal((await request('redirect-token')).status,503,'redirect response fails closed');
    assert.equal(calls.length,3,'redirect must not be followed');
  }finally{await mf.dispose();}
});

