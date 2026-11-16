import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';

const compiled = await build({entryPoints:['src/pages.ts'],bundle:true,write:false,platform:'node',format:'esm'});
const pages = await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'));

test('all account screens use final branding and preserve hardened response headers',async()=>{
  for(const response of [pages.signInPage(),pages.signUpPage(),pages.forgotPasswordPage(),pages.resetPasswordPage(),pages.consentPage('example',['openid'])]){
    const html=await response.text();
    assert.match(html,/<title>[^<]+ · 9to1 Account<\/title>/);
    assert.doesNotMatch(html,/CAKE ID|CakeID|9to1ID/);
    assert.equal(response.headers.get('cache-control'),'no-store');
    assert.equal(response.headers.get('referrer-policy'),'no-referrer');
    assert.match(response.headers.get('content-security-policy'),/frame-ancestors 'none'/);
    assert.equal((html.match(/role="status"/g)||[]).length,1);
    const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(match=>match[1]);
    assert.equal(ids.length,new Set(ids).size);
  }
});

test('direct auth routes select one panel and retain all existing form contracts',async()=>{
  const cases=[['signInPage','sign-in'],['signUpPage','sign-up'],['forgotPasswordPage','forgot-password']];
  for(const [fn,active] of cases){
    const html=await pages[fn]('nine').text();
    assert.equal((html.match(/role="tab"/g)||[]).length,3);
    assert.equal((html.match(/role="tabpanel"/g)||[]).length,3);
    assert.equal((html.match(/aria-selected="true"/g)||[]).length,1);
    assert.match(html,new RegExp('id="tab-'+active+'"[^>]+aria-selected="true"'));
    assert.match(html,new RegExp('id="panel-'+active+'"[^>]+class="auth-panel is-entering">'));
    for(const name of ['sign-in','sign-up','forgot-password'])assert.match(html,new RegExp('id="'+name+'-form" method="post"'));
    for(const field of ['name','username','email','password'])assert.ok(html.includes('name="'+field+'"'));
    assert.ok(html.includes('href="/sign-in?app=nine"'));
    assert.ok(html.includes('href="/sign-up?app=nine"'));
    assert.ok(html.includes('href="/forgot-password?app=nine"'));
  }
});

test('Google is absent when unavailable and appears once when configured',async()=>{
  for(const fn of ['signInPage','signUpPage','forgotPasswordPage']){
    assert.doesNotMatch(await pages[fn]('cake',false).text(),/id="google-sign-in"|Continue with Google/);
    const html=await pages[fn]('cake',true).text();
    assert.equal((html.match(/id="google-sign-in"/g)||[]).length,1);
    assert.match(html,/id="google-sign-in"[^>]+type="button"/);
    assert.ok(html.includes('Continue with Google'));
    assert.equal(html.includes('id="google-auth" hidden'),fn!=='signInPage');
  }
});

test('consent keeps escaped JSON data and current action IDs',async()=>{
  const html=await pages.consentPage("app'&<script>alert(1)</script>",["openid","profile'&<img>"]).text();
  assert.doesNotMatch(html,/<script>alert|<img>/);
  assert.match(html,/data-client-id='[^']*&#39;[^']*&amp;[^']*\\u003cscript/);
  for(const id of ['consent','client-name','scope-list','allow','deny'])assert.ok(html.includes('id="'+id+'"'));
});

test('app permission summaries explain only the requested capabilities',async()=>{
  const readOnly=await pages.consentPage('app',['openid','cake:profile:read']).text();
  const list=readOnly.match(/<ul id="scope-list"[^>]*>([\s\S]*?)<\/ul>/)?.[1] ?? '';
  assert.match(list,/Sign you in/);
  assert.match(list,/Read your shared profile/);
  assert.doesNotMatch(list,/Update your profile|Sign out your devices|Keep the connection signed in|cake:|offline_access/);
  assert.match(readOnly,/data-reviewable="true"/);
  const deviceOnly=await pages.consentPage('app',['cake:sessions:read']).text();
  assert.match(deviceOnly,/View your signed-in devices/);
  assert.doesNotMatch(deviceOnly,/View and sign out your devices/);
});

test('unrecognised app permissions cannot silently become an approvable summary',async()=>{
  const html=await pages.consentPage('app',['openid','future:unknown']).text();
  assert.match(html,/data-reviewable="false"/);
  assert.match(html,/requests access we cannot explain yet/);
  assert.match(html,/<button[^>]*id="allow"[^>]*disabled/);
  assert.match(html,/id="deny"[^>]*>Cancel<\/button>/);
});

test('signup agreement is explicit, unchecked and separate from cookie information',async()=>{
  const html=await pages.signUpPage('nine').text();
  const agreement=html.match(/<input[^>]+id="signup-accept-terms"[^>]*>/)?.[0];
  assert.ok(agreement);
  assert.match(agreement,/name="acceptTerms"/);
  assert.match(agreement,/value="true"/);
  assert.match(agreement,/required/);
  assert.doesNotMatch(agreement,/checked/);
  for(const path of ['terms','privacy','cookies'])assert.ok(html.includes('https://9to1.uk/'+path+'/'));
  assert.match(html,/Essential cookies only/);
  assert.match(html,/id="cookie-notice-dismiss"[^>]*>Got it<\/button>/);
  assert.doesNotMatch(html,/Accept all cookies|Consent to all/);
  assert.match(html,/Account creation is currently invite-only/);
  const invitation=html.match(/<input[^>]+id="signup-invitation-code"[^>]*>/)?.[0];
  assert.ok(invitation);
  assert.match(invitation,/name="invitationCode"/);
  assert.match(invitation,/required/);
  assert.match(invitation,/autocomplete="off"/);
  for(const value of ['under_13','13_15','16_17','18_plus'])assert.ok(html.includes('name="ageBand" type="radio" value="'+value+'" required'));
  assert.doesNotMatch(html,/<input[^>]+checked/);
  assert.ok(html.includes('id="signup-age-ineligible"'));
});

test('protected forms expose exact action slots and missing configuration never implies success',async()=>{
  const html=await pages.signInPage('nine',false).text();
  for(const [id,action] of [['sign-in','login'],['sign-up','signup'],['forgot-password','recovery']]){
    assert.match(html,new RegExp('id="turnstile-'+id+'" data-turnstile-action="'+action+'" data-sitekey="" data-turnstile-required="true"'));
  }
  assert.match(html,/Account verification is temporarily unavailable/);
  assert.doesNotMatch(html,/<script src="https:\/\/challenges.cloudflare.com/);
  const local=await pages.signInPage(null,false,null,false).text();
  assert.equal((local.match(/data-turnstile-required="false" hidden/g)||[]).length,3);
  assert.doesNotMatch(local,/Account verification is temporarily unavailable/);
});

test('Turnstile grants only its exact host and loads after the callback installer',async()=>{
  const response=pages.signInPage('nine',false,'key"<>&\'');
  const html=await response.text();
  assert.match(html,/data-sitekey="key&quot;&lt;&gt;&amp;&#39;"/);
  assert.ok(html.indexOf('<script src="/assets/auth-ui.js" defer>')<html.indexOf('<script src="https://challenges.cloudflare.com/turnstile/'));
  assert.doesNotMatch(html,/<script[^>]+async/);
  const csp=response.headers.get('content-security-policy');
  assert.match(csp,/script-src 'self' https:\/\/challenges.cloudflare.com;/);
  assert.match(csp,/frame-src 'self' https:\/\/challenges.cloudflare.com;/);
  assert.doesNotMatch(csp,/unsafe-eval|\*/);
});

test('onboarding requires server policy versions and offers no preselected age or agreement',async()=>{
  assert.equal(pages.onboardingPage('nine',{termsVersion:'',privacyVersion:'v1'}).status,503);
  assert.equal(pages.onboardingPage('nine',{termsVersion:'v1',privacyVersion:'   '}).status,503);
  const html=await pages.onboardingPage('nine',{termsVersion:'terms-v1',privacyVersion:'privacy-v1'}).text();
  assert.ok(html.includes('id="public-onboarding-form" method="post"'));
  for(const value of ['under_13','13_15','16_17','18_plus'])assert.ok(html.includes('name="ageBand" type="radio" value="'+value+'" required'));
  assert.equal((html.match(/type="radio"/g)||[]).length,4);
  assert.doesNotMatch(html,/<input[^>]+checked/);
  assert.match(html,/name="termsVersion" value="terms-v1"/);
  assert.match(html,/name="privacyVersion" value="privacy-v1"/);
  assert.match(html,/id="onboarding-accept-terms"[^>]+required/);
  assert.match(html,/AI features are not currently available/);
});

test('administrator invitations page has exact bounded input and empty private result contract',async()=>{
  const response=pages.invitationsPage();
  const html=await response.text();
  assert.equal(response.headers.get('cache-control'),'no-store');
  assert.match(html,/id="create-invitation-form" method="post" action="\/account\/invitations"/);
  assert.match(html,/id="invitation-email" name="email" type="email" autocomplete="off" required maxlength="320"/);
  assert.match(html,/name="expiryHours" type="number" value="72" min="1" max="168" step="1" required/);
  assert.match(html,/<output id="invitation-result"[^>]*><\/output>/);
  assert.equal((html.match(/role="status"/g)||[]).length,1);
  assert.match(html,/invitation code is shown once/);
  assert.match(html,/does not send an email/);
  assert.match(html,/href="\/account"/);
});

test('unpublished policy versions disable only signup and never invent an accepted version',async()=>{
  for(const policy of [null,{termsVersion:'',privacyVersion:'v1'},{termsVersion:'v1',privacyVersion:' '}]){
    const html=await pages.signUpPage('nine',false,null,false,policy).text();
    const signup=html.match(/<form id="sign-up-form"[\s\S]*?<\/form>/)?.[0];
    assert.match(signup,/data-policy-unavailable="true"/);
    assert.match(signup,/name="termsVersion" value=""/);
    assert.match(signup,/name="privacyVersion" value=""/);
    assert.match(signup,/Invitation redemption is temporarily unavailable/);
    assert.match(signup,/<button[^>]+type="submit"[^>]+disabled/);
    for(const id of ['sign-in-form','forgot-password-form']){
      const form=html.match(new RegExp('<form id="'+id+'"[\\s\\S]*?<\\/form>'))?.[0];
      assert.ok(form);
      assert.doesNotMatch(form,/<button[^>]+disabled/);
    }
  }
  const html=await pages.signUpPage('nine',false,null,false,{termsVersion:'terms-"<&',privacyVersion:'privacy-1'}).text();
  const signup=html.match(/<form id="sign-up-form"[\s\S]*?<\/form>/)?.[0];
  assert.match(signup,/data-policy-unavailable="false"/);
  assert.match(signup,/name="termsVersion" value="terms-&quot;&lt;&amp;"/);
  assert.match(signup,/name="privacyVersion" value="privacy-1"/);
  assert.doesNotMatch(signup,/<button[^>]+disabled/);
  assert.doesNotMatch(signup,/Invitation redemption is temporarily unavailable/);
});

test('age selection cannot reopen a signup form blocked by policy availability',async()=>{
  const compiledTabs=await build({entryPoints:['src/auth-tabs.ts'],bundle:true,write:false,platform:'node',format:'esm'});
  const {installOnboardingFormState}=await import('data:text/javascript;base64,'+Buffer.from(compiledTabs.outputFiles[0].text).toString('base64'));
  const original=Object.getOwnPropertyDescriptor(globalThis,'document');
  const submit={disabled:false};
  const age={value:'18_plus'};
  const refusal={hidden:true};
  let onChange;
  const form={dataset:{policyUnavailable:'true'},querySelector:selector=>selector.startsWith('button')?submit:age,addEventListener:(event,handler)=>{if(event==='change')onChange=handler;}};
  Object.defineProperty(globalThis,'document',{configurable:true,value:{getElementById:id=>id==='sign-up-form'?form:id==='signup-age-ineligible'?refusal:null}});
  try{
    installOnboardingFormState();
    assert.equal(submit.disabled,true);
    age.value='13_15';onChange();assert.equal(submit.disabled,true);
    form.dataset.policyUnavailable='false';onChange();assert.equal(submit.disabled,false);
    age.value='under_13';onChange();assert.equal(submit.disabled,true);assert.equal(refusal.hidden,false);
  }finally{
    if(original)Object.defineProperty(globalThis,'document',original);else delete globalThis.document;
  }
});
