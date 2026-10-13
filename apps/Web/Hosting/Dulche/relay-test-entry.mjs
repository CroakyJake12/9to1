// Isolated test entry only. Never deploy this service-auth fixture entry.
export { RuntimeRelay } from './relay-worker.mjs';
import { dispatchAuthorized } from './relay-worker.mjs';
export default { async fetch(request,env,ctx) {
  const url=new URL(request.url);
  if(url.pathname==='/test/direct-chat'){const target=new URL(request.url);target.pathname='/api/chat';return env.RUNTIME_RELAY.getByName('ollama-qwen3-runtime-20261013').dispatch(new Request(target,request));}
  if(url.pathname==='/test/retire'){
    try { await env.RUNTIME_RELAY.getByName('ollama-qwen3-runtime-20261013').retireOriginal(url.searchParams.get('id')); return Response.json({joined:true}); }
    catch { return Response.json({error:'original_custody_unavailable'},{status:409}); }
  }
  return dispatchAuthorized(request,env,ctx);
} };
