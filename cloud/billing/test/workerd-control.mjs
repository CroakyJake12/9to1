// Local-only integration entry. Never included in the deployment bundle.
import {claimEvent,completeEvent,failEvent} from '../src/inbox-processing.mjs';
export default {async fetch(request,env) {
 const {operation,args}=await request.json();
 const functions={claim:claimEvent,complete:completeEvent,fail:failEvent};
 if (!Object.hasOwn(functions,operation)) return new Response(null,{status:404});
 return Response.json(await functions[operation](env.BILLING_DB,...args));
}};
