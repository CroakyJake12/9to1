import assert from 'node:assert/strict';
import fs from 'node:fs';
import crypto from 'node:crypto';

// Independent JS unit fixture with a deliberately minimal fake DOM/provider.
// This is not actual browser accessibility, CUI behavior or parity acceptance.
const sourcePath = process.argv[2];
const outputPath = process.argv[3];
const source = fs.readFileSync(sourcePath, 'utf8');
const checks = [];
function environment() {
  let document;
  class Element {
    constructor(tag) { this.tag=tag; this.style={}; this.dataset={}; this.children=[]; this.handlers={}; this.attributes={}; this.tabIndex=['button','input'].includes(tag)?0:-1; }
    append(node) { node.parent=this; this.children.push(node); }
    insertBefore(node,reference) {
      if (document.activeElement===node) document.activeElement=null;
      if(node.parent) node.parent.children=node.parent.children.filter(n=>n!==node);
      node.parent=this;
      const index=reference===null?this.children.length:this.children.indexOf(reference);
      assert(index>=0); this.children.splice(index,0,node);
    }
    replaceChildren() { for (const node of this.children) node.parent=null; this.children=[]; }
    remove() { if(this.parent) this.parent.children=this.parent.children.filter(n=>n!==this); this.parent=null; }
    setAttribute(k,v) { this.attributes[k]=v; }
    removeAttribute(k) { delete this.attributes[k]; }
    addEventListener(k,f) { this.handlers[k]=f; }
    focus() { document.activeElement=this; }
  }
  const timers = new Map(); let serial=0;
  document={ body:new Element('body'), createElement:tag=>new Element(tag), activeElement:null };
  return { document, window: {
    setInterval(f) { const id=++serial; timers.set(id,f); return id; },
    clearInterval(id) { timers.delete(id); }
  }, timers };
}
async function load(text) { return (await import('data:text/javascript;base64,'+Buffer.from(text).toString('base64'))).createBrowserAccessibility; }
async function test(text, check) { const create=await load(text); check(create, environment()); }
const nonfocusable = (create,e) => {
  const bridge=create(e.window,e.document,{ReadAccessibility:()=>JSON.stringify({generation:1,unsupported:[],elements:[{id:'1:1',role:'button',name:'Actual provider label',enabled:true,focusable:false}]}),PerformAccessibility:()=>false});
  try { assert.equal(e.document.body.children[0].children[0].tabIndex,-1); }
  finally { bridge.dispose(); }
  assert.equal(e.timers.size,0); assert.equal(e.document.body.children.length,0);
};
const startupRollback = (create,e) => {
  assert.throws(()=>create(e.window,e.document,{ReadAccessibility:()=>{throw Error('Owner snapshot unavailable');}}),/Owner snapshot unavailable/);
  assert.equal(e.document.body.children.length,0);
  assert.equal(e.timers.size,0);
};
const reorder = (create,e) => {
  let ids=['1:a','1:b','1:c'];
  const owner={ReadAccessibility:()=>JSON.stringify({generation:1,unsupported:[],elements:ids.map(id=>({id,role:'button',name:id,enabled:true,focusable:true}))}),PerformAccessibility:()=>true};
  const bridge=create(e.window,e.document,owner);
  try {
    const root=e.document.body.children[0];
    const retained=root.children[1]; retained.focus();
    ids=['1:c','1:b','1:a']; bridge.refresh();
    assert.deepEqual(root.children.map(node=>node.dataset.nativePeerId),ids);
    assert.equal(root.children[1],retained); assert.equal(e.document.activeElement,retained);
    ids=['1:b','1:a','1:c']; bridge.refresh();
    assert.deepEqual(root.children.map(node=>node.dataset.nativePeerId),ids);
    assert.equal(e.document.activeElement,retained);
  } finally { bridge.dispose(); }
};
const disposedEvents = (create,e) => {
  let operations=0;
  const owner={ReadAccessibility:()=>JSON.stringify({generation:1,unsupported:[],elements:[{id:'1:a',role:'button',name:'Native action',enabled:true,focusable:true},{id:'1:b',role:'textbox',name:'Native value',enabled:true,focusable:true,value:'original',readOnly:false}]}),PerformAccessibility:()=>{++operations;return true;}};
  const bridge=create(e.window,e.document,owner);
  const [button,input]=e.document.body.children[0].children;
  bridge.dispose(); button.handlers.click(); input.value='late'; input.handlers.input();
  assert.equal(operations,0); assert.equal(e.timers.size,0); assert.equal(e.document.body.children.length,0);
};
function nativeEnvironment(e) {
  const make=e.document.createElement;
  e.document.createElement=tag=>{
    const node=make(tag),focus=node.focus.bind(node);
    node.focus=()=>{if(e.document.activeElement===node)return;focus();node.handlers.focus?.();};
    return node;
  };
  const host=e.document.createElement('main');host.tabIndex=0;host.attributes.tabindex='0';
  host.getAttribute=name=>host.attributes[name]??null;
  const set=host.setAttribute.bind(host),remove=host.removeAttribute.bind(host);
  host.setAttribute=(name,value)=>{set(name,value);if(name==='tabindex')host.tabIndex=Number(value);};
  host.removeAttribute=name=>{remove(name);if(name==='tabindex')host.tabIndex=-1;};
  host.contains=node=>node===host||node?.parent===host;
  e.document.getElementById=id=>id==='nine-to-one-root'?host:null;
  const listeners=new Map();
  e.document.addEventListener=(name,handler,capture)=>{assert.equal(capture,true);listeners.set(name,handler);};
  e.document.removeEventListener=(name,handler,capture)=>{assert.equal(capture,true);if(listeners.get(name)===handler)listeners.delete(name);};
  const event=options=>({key:'Tab',target:host,shiftKey:false,ctrlKey:false,altKey:false,metaKey:false,prevented:false,stopped:false,
    preventDefault(){this.prevented=true;},stopImmediatePropagation(){this.stopped=true;},...options});
  return {host,listeners,event};
}
const nativeTab = (create,e) => {
  const native=nativeEnvironment(e);let focused='b';const calls=[];
  const owner={ReadAccessibility:()=>JSON.stringify({generation:1,unsupported:[],elements:['a','b','c','d'].map(id=>({id,role:'button',name:id,enabled:id!=='c',focusable:true,focused:id===focused}))}),
    PerformAccessibility:(id,operation)=>{calls.push({id,operation});native.host.focus();return true;}};
  const bridge=create(e.window,e.document,owner);
  try {
    assert.equal(native.host.tabIndex,-1);
    const forward=native.event();native.listeners.get('keydown')(forward);
    assert(forward.prevented&&forward.stopped);assert.equal(e.document.activeElement.dataset.nativePeerId,'d');
    assert.deepEqual(calls,[{id:'d',operation:'focus'}]);
    focused='a';const backward=native.event({shiftKey:true});native.listeners.get('keydown')(backward);
    assert(!backward.prevented&&backward.stopped);assert.equal(e.document.activeElement.dataset.nativePeerId,'a');
  } finally {bridge.dispose();}
  assert.equal(native.listeners.size,0);assert.equal(native.host.tabIndex,0);
};
const unrelatedKeys = (create,e) => {
  const native=nativeEnvironment(e);
  const bridge=create(e.window,e.document,{ReadAccessibility:()=>JSON.stringify({generation:1,unsupported:[],elements:[]}),PerformAccessibility:()=>{throw Error('Unexpected native operation');}});
  try {
    for(const options of [{key:'Enter'},{ctrlKey:true},{altKey:true},{metaKey:true},{target:e.document.body}]) {
      const event=native.event(options);native.listeners.get('keydown')(event);assert(!event.prevented&&!event.stopped);
    }
    const empty=native.event();native.listeners.get('keydown')(empty);assert(empty.stopped&&!empty.prevented);
  } finally {bridge.dispose();}
};
const nativeDisposed = (create,e) => {
  const native=nativeEnvironment(e);
  const bridge=create(e.window,e.document,{ReadAccessibility:()=>JSON.stringify({generation:1,unsupported:[],elements:[]}),PerformAccessibility:()=>false});
  const retained=native.listeners.get('keydown');bridge.dispose();
  const event=native.event();retained(event);assert(!event.stopped&&!event.prevented);
  assert.equal(native.listeners.size,0);assert.equal(native.host.tabIndex,0);
};
for(const [name,check] of [['native-nonfocusable-remains-out-of-tab-sequence',nonfocusable],['initial-snapshot-failure-removes-root-and-timer',startupRollback],['stable-peers-follow-current-native-order-and-retain-focus',reorder],['disposed-retained-dom-controls-stop-forwarding-operations',disposedEvents]]) {
  await test(source,check); checks.push({name,result:'PASS'});
}
for(const [name,check] of [['native-host-tab-follows-current-peer-focus-and-skips-disabled',nativeTab],['native-host-keeps-other-keys-modifiers-and-empty-view-egress',unrelatedKeys],['disposed-native-tab-listener-stops-routing-and-restores-host',nativeDisposed]]) {
  await test(source,check);checks.push({name,result:'PASS'});
}
const focusMutant=source.replace('node.tabIndex = peer.enabled && peer.focusable ? 0 : -1;','');
assert.notEqual(focusMutant,source);
await assert.rejects(test(focusMutant,nonfocusable),assert.AssertionError);
checks.push({name:'removed-native-focusability-guard',result:'EXPECTED_FAILURE_DETECTED'});
const rollbackStart=source.indexOf('    let timer;');
const rollbackEnd=source.indexOf('    return {',rollbackStart);
assert(rollbackStart>=0 && rollbackEnd>rollbackStart);
const rollbackMutant=source.slice(0,rollbackStart)+'    const timer = browserWindow.setInterval(refresh, 250);\n    refresh();\n'+source.slice(rollbackEnd);
await assert.rejects(test(rollbackMutant,startupRollback),assert.AssertionError);
checks.push({name:'restored-timer-before-failed-snapshot',result:'EXPECTED_FAILURE_DETECTED'});
const orderStart=source.indexOf('        snapshot.elements.forEach((peer, index) => {');
const orderEnd=source.indexOf('        if (focused &&',orderStart);
assert(orderStart>=0 && orderEnd>orderStart);
const orderMutant=source.slice(0,orderStart)+source.slice(orderEnd);
await assert.rejects(test(orderMutant,reorder),assert.AssertionError);
checks.push({name:'removed-native-order-reconciliation',result:'EXPECTED_FAILURE_DETECTED'});
const disposedMutant=source.replaceAll('() => { if (disposed) return; owner.PerformAccessibility','() => { owner.PerformAccessibility');
assert.notEqual(disposedMutant,source);
await assert.rejects(test(disposedMutant,disposedEvents),assert.AssertionError);
checks.push({name:'removed-disposed-event-forwarding-guards',result:'EXPECTED_FAILURE_DETECTED'});
const tabMutant=source.replace("if (nativeHost) browserDocument.addEventListener('keydown', nativeTab, true);",'');
assert.notEqual(tabMutant,source);await assert.rejects(test(tabMutant,nativeTab));
checks.push({name:'removed-native-tab-arbitration-subscription',result:'EXPECTED_FAILURE_DETECTED'});
const focusOrderMutant=source.replace('nativeFocusedId = snapshot.elements.find(peer => peer.focused)?.id;','');
assert.notEqual(focusOrderMutant,source);await assert.rejects(test(focusOrderMutant,nativeTab),assert.AssertionError);
checks.push({name:'removed-actual-native-focus-order',result:'EXPECTED_FAILURE_DETECTED'});
const report={scope:'Mock DOM/provider source-level negative controls only; NOT actual browser/CUI/provider acceptance',sourcePath,sourceSha256:crypto.createHash('sha256').update(source).digest('hex'),runnerSha256:crypto.createHash('sha256').update(fs.readFileSync(import.meta.filename)).digest('hex'),checks,productParityVerified:false};
fs.writeFileSync(outputPath,JSON.stringify(report,null,2));
console.log(JSON.stringify(checks));
