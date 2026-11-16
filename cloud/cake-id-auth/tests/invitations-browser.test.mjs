import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
const bundle=await build({entryPoints:['src/invitations-browser.ts'],bundle:true,write:false,platform:'node',format:'esm'});
const {installInvitationAction}=await import('data:text/javascript;base64,'+Buffer.from(bundle.outputFiles[0].text).toString('base64'));
async function fixture(reply,run){
 const keys=['document','FormData','fetch'];const old=new Map(keys.map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
 const button={disabled:false},status={textContent:''},output={textContent:''};let submit;const calls=[];
 const form={dataset:{},querySelector:()=>button,addEventListener:(_event,fn)=>{submit=fn;}};
 Object.defineProperty(globalThis,'document',{configurable:true,value:{querySelector:()=>status,getElementById:id=>id==='create-invitation-form'?form:id==='invitation-result'?output:null}});
 Object.defineProperty(globalThis,'FormData',{configurable:true,value:class{get(key){return key==='email'?'invitee@example.test':'72';}}});
 globalThis.fetch=async(url,options)=>{calls.push({url,options});if(reply instanceof Error)throw reply;return Response.json(reply.body,{status:reply.status??201});};
 try{installInvitationAction();await run({button,status,output,calls,submit:()=>submit({preventDefault(){}})});}
 finally{for(const[key,value]of old){if(value)Object.defineProperty(globalThis,key,value);else delete globalThis[key];}}
}
test('admin action shows a returned invitation once and never places the code in a URL',async()=>{
 const code='s'.repeat(43);await fixture({body:{code,invitation:{email:'invitee@example.test',expiresAt:Date.now()+3600000}}},async({submit,calls,button,output,status})=>{
  await submit();await submit();assert.equal(calls.length,1);assert.equal(calls[0].url,'/account/invitations');assert.equal(calls[0].options.credentials,'same-origin');
  assert.ok(!calls[0].url.includes(code));assert.ok(output.textContent.includes(code));assert.match(status.textContent,/No email has been sent/);assert.equal(button.disabled,true);
 });
});
test('lost or malformed invitation responses cannot be blindly retried or reported as success',async()=>{
 for(const reply of [new Error('lost response'),{body:{}},{status:409,body:{error:'private diagnostic'}}])await fixture(reply,async({submit,calls,button,output,status})=>{
  await submit();await submit();assert.equal(calls.length,1);assert.equal(button.disabled,true);assert.equal(output.textContent,'');assert.doesNotMatch(status.textContent,/Invitation created|private diagnostic/);
 });
});
