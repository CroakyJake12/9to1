import {DurableObject} from 'cloudflare:workers';
import {SessionEngine} from './session-engine.mjs';
export class WebSession extends DurableObject {
  constructor(ctx,env){super(ctx,env);this.engine=new SessionEngine(ctx.storage,env);}
  create(tokens,clientId,origin){return this.engine.create(tokens,clientId,origin);}
  request(path,method,body,origin){return this.engine.request(path,method,body,origin);}
  alarm(){return this.engine.alarm();}
}
