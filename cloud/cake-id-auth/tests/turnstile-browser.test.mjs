import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
const bundle=await build({entryPoints:['src/turnstile-browser.ts'],bundle:true,write:false,platform:'node',format:'esm'});
const {installTurnstile}=await import('data:text/javascript;base64,'+Buffer.from(bundle.outputFiles[0].text).toString('base64'));
async function fixture(run,{key='public-site-key',required='true',width=360,observeResize=true}={}){
 const names=['document','window','location'];const original=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
 const keys=['sign-in','sign-up','forgot-password'],actions=['login','signup','recovery'],nodes=new Map(),renders=[],removes=[],resets=[],events=new Map(),observers=[];
 keys.forEach((k,i)=>{nodes.set('turnstile-'+k,{clientWidth:width,dataset:{sitekey:key,turnstileRequired:required,turnstileAction:actions[i]}});nodes.set('panel-'+k,{hidden:i!==0});});
 const location={pathname:'/sign-in'};
 const window={addEventListener:(name,fn)=>events.set(name,fn),turnstile:{render(container,options){const id='widget-'+renders.length;renders.push({id,container,options});return id;},reset:id=>resets.push(id),remove:id=>removes.push(id)}};
 if(observeResize)window.ResizeObserver=class{constructor(callback){this.callback=callback;this.nodes=new Set();observers.push(this);}observe(node){this.nodes.add(node);}};
 Object.defineProperty(globalThis,'document',{configurable:true,value:{getElementById:id=>nodes.get(id)??null}});
 Object.defineProperty(globalThis,'window',{configurable:true,value:window});Object.defineProperty(globalThis,'location',{configurable:true,value:location});
 try{const manager=installTurnstile();await run({manager,renders,removes,resets,window,nodes,
  resize(nextWidth,{viewport=false,key=location.pathname.slice(1)}={}){
   const node=nodes.get('turnstile-'+key);node.clientWidth=nextWidth;
   if(viewport){assert.ok(events.has('resize'),'viewport resize listener installed');events.get('resize')();}
   else{assert.ok(observers.some(observer=>observer.nodes.has(node)),'container is observed');for(const observer of observers)if(observer.nodes.has(node))observer.callback([{target:node}]);}
  },
  change(k){location.pathname='/'+k;for(const item of keys)nodes.get('panel-'+item).hidden=item!==k;events.get('nine-auth-tab-change')();}});}
 finally{for(const [name,descriptor] of original){if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];}}
}
test('only the active form renders and challenge tokens are consumed once then reset',async()=>{
 await fixture(({manager,renders,resets})=>{
  assert.equal(renders.length,1);assert.equal(renders[0].options.action,'login');assert.equal(renders[0].options['response-field'],false);
  assert.throws(()=>manager.take('sign-in-form'),/Complete the verification/);
  renders[0].options.callback('single-use');assert.equal(manager.take('sign-in-form'),'single-use');assert.throws(()=>manager.take('sign-in-form'));
  manager.reset('sign-in-form');assert.deepEqual(resets,['widget-0']);assert.equal(manager.take('reset-password-form'),'');
 });
});
test('tab changes remove old widgets and ignore stale callbacks before rendering the correct new action',async()=>{
 await fixture(({manager,renders,removes,change})=>{
  const old=renders[0];old.options.callback('old-token');change('sign-up');assert.deepEqual(removes,['widget-0']);assert.equal(renders[1].options.action,'signup');
  old.options.callback('late-old-token');assert.throws(()=>manager.take('sign-in-form'));
  renders[1].options.callback('signup-token');assert.equal(manager.take('sign-up-form'),'signup-token');
  change('forgot-password');assert.equal(renders[2].options.action,'recovery');
 });
});
test('expired or failed challenges are cleared and cannot be submitted',async()=>{
 await fixture(({manager,renders})=>{
  for(const event of ['expired-callback','timeout-callback','error-callback']){
   renders[0].options.callback('stale-token');renders[0].options[event]();assert.throws(()=>manager.take('sign-in-form'));
  }
 });
});
test('missing production site key fails closed; explicit local-test markup remains usable',async()=>{
 await fixture(({manager,renders})=>{assert.equal(renders.length,0);assert.throws(()=>manager.take('sign-in-form'),/temporarily unavailable/);},{key:''});
 await fixture(({manager,renders})=>{assert.equal(renders.length,0);assert.equal(manager.take('sign-in-form'),'');},{key:'',required:'false'});
});

test('narrow containers use compact verification and the 300px boundary retains flexible sizing',async()=>{
 for(const [width,size] of [[239,'compact'],[299,'compact'],[300,'flexible'],[420,'flexible']]){
  await fixture(({renders})=>{assert.equal(renders.length,1);assert.equal(renders[0].options.size,size);},{width});
 }
});

test('container resizing invalidates the old challenge and ignores late callbacks from the removed widget',async()=>{
 await fixture(({manager,renders,removes,resize})=>{
  const old=renders[0];old.options.callback('old-completed-token');resize(239);
  assert.equal(renders.length,2);assert.deepEqual(removes,[old.id]);assert.equal(renders[1].options.size,'compact');
  assert.throws(()=>manager.take('sign-in-form'),/Complete the verification/);
  old.options.callback('late-old-token');assert.throws(()=>manager.take('sign-in-form'),/Complete the verification/);
  const current=renders[1];current.options.callback('current-token');
  for(const event of ['expired-callback','timeout-callback','error-callback'])old.options[event]();
  assert.equal(manager.take('sign-in-form'),'current-token');
  current.options.callback('another-current-token');resize(300);
  assert.equal(renders.length,3);assert.equal(renders[2].options.size,'flexible');
  assert.throws(()=>manager.take('sign-in-form'));
  current.options.callback('late-compact-token');assert.throws(()=>manager.take('sign-in-form'));
  renders[2].options.callback('new-wide-token');assert.equal(manager.take('sign-in-form'),'new-wide-token');
 });
});

test('resizes within one size class and repeated notifications preserve the current challenge',async()=>{
 await fixture(({manager,renders,removes,resize,window})=>{
  renders[0].options.callback('wide-token');resize(320);resize(300,{viewport:true});resize(300);window.onNineTurnstileLoad();
  assert.equal(renders.length,1);assert.deepEqual(removes,[]);assert.equal(manager.take('sign-in-form'),'wide-token');
  resize(299);renders[1].options.callback('compact-token');resize(239);resize(239,{viewport:true});resize(280);
  assert.equal(renders.length,2);assert.equal(removes.length,1);assert.equal(manager.take('sign-in-form'),'compact-token');
 });
});

test('viewport resizing works without ResizeObserver and retains each form action and reset behavior',async()=>{
 await fixture(({manager,renders,resets,resize,change})=>{
  for(const [key,action] of [['sign-in','login'],['sign-up','signup'],['forgot-password','recovery']]){
   change(key);resize(239,{viewport:true});const narrow=renders.at(-1);
   assert.equal(narrow.options.action,action);assert.equal(narrow.options.size,'compact');
   narrow.options.callback(key+'-token');assert.equal(manager.take(key+'-form'),key+'-token');
   manager.reset(key+'-form');assert.equal(resets.at(-1),narrow.id);assert.throws(()=>manager.take(key+'-form'));
   resize(350,{viewport:true});const wide=renders.at(-1);assert.equal(wide.options.action,action);assert.equal(wide.options.size,'flexible');
   narrow.options.callback('stale-token');assert.throws(()=>manager.take(key+'-form'));
   wide.options.callback(key+'-wide-token');assert.equal(manager.take(key+'-form'),key+'-wide-token');
  }
 },{observeResize:false});
});
