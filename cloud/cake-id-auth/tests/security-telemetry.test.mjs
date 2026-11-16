import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
const compiled=await build({entryPoints:['src/security-telemetry.ts'],bundle:true,write:false,platform:'node',format:'esm'});
test('security logs contain only bounded static event counters, never caller-supplied values',async()=>{
 const original=console.warn,captured=[];console.warn=value=>captured.push(JSON.parse(value));
 try{
  const {recordSecurityEvent,flushSecurityEvents}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'));
  recordSecurityEvent('private@example.test');recordSecurityEvent('captcha_rejected');
  for(let i=0;i<50;i++)recordSecurityEvent('signup_rejected');
  assert.equal(captured.length,1);flushSecurityEvents();assert.equal(captured.length,2);
  assert.deepEqual(captured[0],{event:'account_security_events',counts:{captcha_rejected:1}});
  assert.deepEqual(captured[1],{event:'account_security_events',counts:{signup_rejected:50}});
  assert.ok(!JSON.stringify(captured).includes('private@example.test'));
 }finally{console.warn=original;}
});
test('CAPTCHA availability reports pair the total with one static stage, including the first emission',async()=>{
 const original=console.warn,captured=[];console.warn=value=>captured.push(JSON.parse(value));
 try{
  const {recordCaptchaUnavailable,flushSecurityEvents}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64')+'#'+crypto.randomUUID());
  for(const invalid of ['private@example.test','__proto__','constructor',undefined])recordCaptchaUnavailable(invalid);
  assert.equal(captured.length,0);
  recordCaptchaUnavailable('configuration');
  assert.deepEqual(captured,[{event:'account_security_events',counts:{captcha_unavailable:1,captcha_unavailable_configuration:1}}]);
  for(const stage of ['replay_read','siteverify_transport','siteverify_response','replay_commit'])recordCaptchaUnavailable(stage);
  flushSecurityEvents();
  assert.deepEqual(captured[1],{event:'account_security_events',counts:{captcha_unavailable:4,
   captcha_unavailable_replay_read:1,captcha_unavailable_siteverify_transport:1,
   captcha_unavailable_siteverify_response:1,captcha_unavailable_replay_commit:1}});
  assert.ok(!JSON.stringify(captured).includes('private@example.test'));
 }finally{console.warn=original;}
});
