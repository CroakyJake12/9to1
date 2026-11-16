import { publicAppPath, type PublicApp } from "./public-app";
import { NINE_LOGO_URL, CAKEMODS_LOGO_URL, MONTSERRAT_CSS } from "./brand-assets";

const policyLinks = {
  terms: "https://9to1.uk/terms/",
  privacy: "https://9to1.uk/privacy/",
  cookies: "https://9to1.uk/cookies/",
} as const;

const escapeAttribute = (value: string) => value.replaceAll("&", "&amp;").replaceAll('"', "&quot;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll("'", "&#39;");

const sharedCss = MONTSERRAT_CSS + `
  :root{color-scheme:light;font:16px/1.5 "Montserrat",sans-serif;background:#f8f8f3;color:#172020;--ink:#172020;--muted:#59635f;--blue:#264df2;--line:#dce0d9}
  *{box-sizing:border-box}body{margin:0;min-height:100vh}a{color:inherit;text-underline-offset:4px}button,input{font:inherit}button,a,input{ -webkit-tap-highlight-color:transparent }
  [hidden]{display:none!important}a:focus-visible,button:focus-visible,input:focus-visible{outline:3px solid #264df2;outline-offset:4px}
  .skip-link{position:absolute;top:-80px;left:20px;z-index:10;padding:12px 18px;background:white}.skip-link:focus{top:15px}
  .page-header,.page-footer{width:min(1240px,calc(100% - 80px));margin:auto;display:flex;align-items:center;justify-content:space-between;gap:24px}
  .page-header{min-height:100px;border-bottom:1px solid var(--line)}.brand{display:inline-flex;align-items:center;gap:12px;color:var(--ink);font-size:1.05rem;font-weight:700;letter-spacing:-.035em;text-decoration:none;white-space:nowrap}
  .brand-mark{display:block;width:42px;height:auto;flex-shrink:0;object-fit:contain}
  .back-link{display:inline-flex;align-items:center;gap:9px;min-height:44px;font-size:.82rem;text-decoration:none}.back-link:hover{text-decoration:underline}
  .account-layout{width:min(1120px,calc(100% - 80px));margin:64px auto 70px;display:grid;grid-template-columns:minmax(0,1fr) minmax(0,480px);align-items:center;gap:85px}
  .intro{align-self:stretch;display:flex;flex-direction:column;justify-content:center;min-width:0;padding-block:25px}.eyebrow{margin:0 0 20px;font-size:.69rem;font-weight:650;letter-spacing:.14em;text-transform:uppercase;color:#4b5b51}
  .intro h2{margin:0;font-size:clamp(2.5rem,4.5vw,4.25rem);font-weight:580;letter-spacing:-.065em;line-height:1.06}.intro h2 em{color:var(--blue);font-style:normal}.intro>p:not(.eyebrow){max-width:355px;margin:24px 0 0;color:var(--muted);font-size:1rem;line-height:1.65}
  .account-art{position:relative;width:330px;max-width:100%;height:205px;margin-top:40px;isolation:isolate}.orbit{position:absolute;inset:10px 14px;border:1px solid #dce2d4;border-radius:50%;transform:rotate(-15deg)}
  .art-tile{position:absolute;display:grid;place-items:center;border-radius:17px;box-shadow:0 12px 24px #1720200c;font-weight:750;letter-spacing:-.06em}.tile-nine{left:22px;top:56px;width:95px;height:auto;border-radius:0;box-shadow:none}.tile-nine img{display:block;width:100%;height:auto;object-fit:contain}
  .tile-cake{right:9px;top:23px;width:114px;height:92px;background:transparent;box-shadow:none;transform:rotate(8deg)}.tile-account{left:122px;bottom:7px;width:98px;height:87px;background:#ede6f5;color:#615080;transform:rotate(3deg)}.tile-account svg{width:36px;height:36px}.tile-cake img{width:100%;height:100%;object-fit:contain}code,pre{font-family:inherit}
  .card{container-type:inline-size;min-width:0;background:#fffefb;border:1px solid #e0e3dc;border-radius:20px;padding:28px 32px 26px;box-shadow:0 18px 60px #17202007}.auth-tablist{position:relative;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));margin:0 -8px 30px;padding:4px;background:#f0f2ec;border-radius:10px;isolation:isolate;--active-tab:0}
  .auth-tablist:before{content:"";position:absolute;top:4px;bottom:4px;left:4px;width:calc((100% - 8px)/3);background:white;border:2px solid var(--blue);border-radius:7px;box-shadow:0 2px 6px #17202009;z-index:-1;transform:translateX(calc(var(--active-tab)*100%));transition:transform .42s cubic-bezier(.2,.85,.2,1)}
  .auth-tab{display:flex;align-items:center;justify-content:center;min-width:0;min-height:48px;padding:8px 7px;color:#657064;font-size:.77rem;font-weight:600;line-height:1.3;text-align:center;text-decoration:none;border-radius:7px}.auth-tab[aria-selected=true]{color:var(--blue)}.auth-tab:hover{color:var(--ink)}
  .card-heading{margin-bottom:25px}.card h1{margin:0;font-size:2rem;font-weight:620;letter-spacing:-.05em;line-height:1.15}.card-heading p{margin:12px 0 0;color:var(--muted);font-size:.89rem;line-height:1.6}.auth-panel.is-entering,.standalone-panel{animation:panel-in .38s cubic-bezier(.16,1,.3,1)}
  @keyframes panel-in{from{opacity:0;transform:translateY(15px) scale(.985)}to{opacity:1;transform:translateY(0) scale(1)}}
  form{margin:0}.field{margin:0 0 18px}.field-grid{display:grid;grid-template-columns:1fr 1fr;gap:15px}.field-grid .field{min-width:0}label{display:block;margin-bottom:7px;font-size:.81rem;font-weight:650;letter-spacing:-.015em}
  input{display:block;width:100%;min-width:0;min-height:49px;padding:12px 14px;border:1px solid #7f897b;border-radius:6px;background:#fffefa;color:var(--ink);font-size:.94rem;line-height:1.4;transition:border-color .15s,box-shadow .15s}input:hover{border-color:#62705e}input:focus{outline:0;border-color:var(--blue);box-shadow:0 0 0 3px #264df219}input::placeholder{color:#657064}
  .field-help{margin:7px 0 0;color:#6c746c;font-size:.73rem;line-height:1.5}.field-label-row{display:flex;justify-content:space-between;align-items:baseline;gap:15px}.field-label-row a{font-size:.75rem;color:#4c5d77}.primary,.secondary,.google-button{width:100%;display:flex;justify-content:center;align-items:center;gap:12px;min-height:50px;padding:13px 17px;border:1px solid transparent;border-radius:6px;font-size:.88rem;font-weight:650;cursor:pointer;transition:background .15s,transform .15s}
  .primary{margin-top:22px;background:var(--blue);color:white}.primary:hover{background:#1b3bd0;transform:translateY(-1px)}.primary:active{transform:translateY(0)}button:disabled{opacity:.6;cursor:not-allowed;transform:none}.secondary{margin-top:12px;color:var(--ink);background:transparent;border-color:#7f897b}.secondary:hover{background:#f1f3ed}
  .google-button{background:#fffefb;color:#26312c;border-color:#7f897b}.google-button:hover{background:#f1f3ed}.google-button svg{width:19px;height:19px;flex:none}.divider{display:flex;align-items:center;gap:12px;margin:22px 0;color:#7b8379;font-size:.69rem}.divider:before,.divider:after{content:"";height:1px;background:#e2e6dd;flex:1}
  [role=status]{margin:19px 0 0;min-height:1.5em;color:#334a80;font-size:.82rem;line-height:1.6;overflow-wrap:anywhere}.card-note{margin:21px 0 0;padding-top:21px;border-top:1px solid #e4e7e0;color:#697366;font-size:.74rem;line-height:1.6}.card-note strong{font-weight:600;color:#465443}.card-nav{display:flex;flex-wrap:wrap;gap:14px;justify-content:space-between;margin-top:22px;font-size:.81rem}.card-nav a{min-height:36px;display:flex;align-items:center}
  .permission-list{padding:0;list-style:none;margin:22px 0}.permission-list li{padding:13px 0;border-bottom:1px solid var(--line);font-size:.86rem;line-height:1.55}.permission-list strong,.permission-list span{display:block}.permission-list span{margin-top:4px;color:var(--muted);font-size:.81rem}.permission-details{margin-top:20px;font-size:.8rem;overflow-wrap:anywhere}.management-layout{display:block;max-width:920px}.management-layout>.intro{display:none}.management-layout .card{padding:32px}.management-actions{display:flex;flex-wrap:wrap;gap:12px;margin:24px 0}.management-actions a{display:inline-flex;align-items:center;min-height:48px;padding:12px 18px;border:1px solid var(--line);border-radius:10px;text-decoration:none;font-weight:600}.management-section{margin-top:28px;padding-top:24px;border-top:1px solid var(--line)}.management-section h2{font-size:1.3rem}.management-details{margin-top:24px;overflow-wrap:anywhere}.management-details summary{min-height:44px;cursor:pointer;font-weight:600}.management-details code{font-size:.8rem;overflow-wrap:anywhere}.client-name{font-size:1.1rem;font-weight:650;overflow-wrap:anywhere;margin:25px 0 6px}.consent-copy{margin:0 0 23px;color:var(--muted);font-size:.88rem}
  .page-footer{border-top:1px solid var(--line);padding-block:23px;color:#59635f;font-size:.78rem}.page-footer nav{display:flex;gap:22px;flex-wrap:wrap}.page-footer a{display:inline-flex;align-items:center;min-height:44px;text-decoration:none}.page-footer a:hover{text-decoration:underline}
  .terms-ack{display:flex;align-items:flex-start;gap:10px;margin:21px 0 0;font-size:.75rem;font-weight:400;line-height:1.6;letter-spacing:0;color:#505e51}.terms-ack input{flex:none;width:18px;min-width:18px;height:18px;min-height:18px;margin:3px 0 0;padding:0;border-radius:3px;accent-color:var(--blue)}.terms-ack a{color:#284bb4}
  .age-bands{margin:0;padding:0;border:0}.age-bands legend{padding:0;margin-bottom:12px;font-size:.83rem;font-weight:650}.age-choice{display:flex;gap:12px;align-items:center;min-height:48px;padding:12px 14px;margin-bottom:9px;border:1px solid #d4ddce;border-radius:6px;background:#fffefa;font-size:.83rem;font-weight:500;cursor:pointer}.age-choice:has(input:checked){border-color:var(--blue);background:#f0f3ff}.age-choice input{width:18px;min-width:18px;height:18px;min-height:18px;padding:0;margin:0;accent-color:var(--blue)}.safety-note{margin:18px 0 0;padding:13px 15px;border:1px solid #dae2d3;border-radius:6px;color:#4d6045;background:#f3f6ed;font-size:.77rem;line-height:1.65}.age-refusal{padding:12px 14px;background:#fff1ed;border:1px solid #e8ccc1;border-radius:6px;color:#823d2f;font-size:.8rem;line-height:1.6}
  .age-field{margin-top:22px}.invitation-result{display:block;max-width:100%;margin-top:22px;padding:16px;border:1px solid #cedac3;border-radius:6px;background:#f4f7ee;white-space:pre-wrap;overflow-wrap:anywhere;font: .8rem/1.65 "Montserrat",sans-serif;color:#344c2c}.invitation-result:empty{display:none}
  .turnstile-slot{margin-top:22px;max-width:100%;min-width:0}.turnstile-slot[data-turnstile-required="true"]{min-height:75px}.turnstile-slot[data-turnstile-required="true"]:empty::before{content:"Loading security check…";display:flex;align-items:center;min-height:inherit;color:var(--muted);font-size:.78rem;line-height:1.5}@container(max-width:299px){.turnstile-slot[data-turnstile-required="true"]{min-height:150px}}.verification-notice{margin:0;padding:12px;border:1px solid #dedacb;border-radius:6px;background:#f8f5e9;color:#605a3f;font-size:.78rem;line-height:1.6}.cookie-link{min-height:44px;margin:0;padding:0;border:0;background:transparent;color:inherit;font-size:inherit;cursor:pointer}.cookie-link:hover{text-decoration:underline;text-underline-offset:4px}
  body.has-cookie-notice{padding-bottom:0}.cookie-notice{position:static;width:min(1120px,calc(100% - 40px));margin:0 auto 24px;display:flex;align-items:center;gap:25px;padding:20px 24px;border:1px solid #d3dbcc;border-radius:10px;background:#f0f3eb}.cookie-notice h2{margin:0 0 7px;font-size:.88rem;font-weight:650;letter-spacing:-.02em}.cookie-notice p{margin:0;color:#56624e;font-size:.76rem;line-height:1.6}.cookie-notice .secondary{flex:none;width:auto;min-width:90px;min-height:44px;margin:0;padding:10px 17px;font-size:.8rem}
  @media(min-width:1500px){.account-layout{margin-block:85px 100px}}
  @media(max-width:1000px){.account-layout{gap:40px;grid-template-columns:minmax(0,1fr) minmax(0,450px)}.intro h2{font-size:3.1rem}.account-art{width:280px}.tile-nine{left:5px}.tile-cake{right:0}.tile-account{left:100px}}
  @media(max-width:780px){.page-header,.page-footer{width:calc(100% - 40px)}.page-header{min-height:82px}.account-layout{width:min(480px,calc(100% - 40px));margin:35px auto 45px;display:block}.intro{display:none}.card{padding:25px 26px 23px}.page-footer{align-items:start}.page-footer>span{max-width:150px}.page-footer nav{justify-content:flex-end;gap:12px 18px}.card h1{font-size:1.9rem}}
  @media(max-width:600px){.cookie-notice{width:calc(100% - 24px);display:block;padding:17px 18px}.cookie-notice .secondary{margin-top:14px;width:100%}}
  @media(max-width:420px){.page-header,.page-footer{width:calc(100% - 32px)}.page-header{gap:16px}.brand{font-size:.92rem;gap:9px}.brand-mark{width:35px}.back-link{font-size:.72rem;max-width:112px;line-height:1.35;text-align:right}.account-layout{width:calc(100% - 24px);margin-top:25px}.card{padding:23px 20px 21px;border-radius:14px}.auth-tablist{margin-inline:-8px;margin-bottom:26px}.auth-tab{font-size:.69rem;padding-inline:4px;min-height:48px}.card h1{font-size:1.75rem}.field-grid{grid-template-columns:1fr;gap:0}.page-footer{display:block}.page-footer>span{max-width:none}.page-footer nav{justify-content:flex-start;margin-top:14px}}
  @media(prefers-reduced-motion:reduce){*,*::before,*::after{animation:none!important;transition:none!important;scroll-behavior:auto!important}.primary:hover{transform:none}}
`;

const statusRegion = '<p role="status" aria-live="polite" aria-atomic="true"></p>';

function frame(title: string, content: string, app: PublicApp | null = null, siteKey: string | null = null, management = false): Response {
  const home = app === "cake" ? "https://cakemods.com" : "https://9to1.uk";
  const homeLabel = app === "cake" ? "Back to CakeMods" : "Back to 9to1";
  const challengeHost = siteKey?.trim() ? " https://challenges.cloudflare.com" : "";
  const challengeScript = challengeHost ? '<script src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit&amp;onload=onNineTurnstileLoad" defer></script>' : "";
  return new Response(`<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${title} · 9to1 Account</title><link rel="icon" href="${NINE_LOGO_URL}" type="image/png"><style>${sharedCss}</style></head><body class="has-cookie-notice">
    <a class="skip-link" href="#main">Skip to account form</a>
    <header class="page-header"><a class="brand" href="https://9to1.uk"><img class="brand-mark" src="${NINE_LOGO_URL}" alt="" width="460" height="435">9to1 Account</a><a class="back-link" href="${home}"><span aria-hidden="true">←</span>${homeLabel}</a></header>
    <main class="account-layout${management ? " management-layout" : ""}" id="main"><aside class="intro" aria-label="Your shared account"><p class="eyebrow">A LITTLE MORE CONNECTED</p><h2>Your ideas.<br>Your people.<br><em>One account.</em></h2><p>Sign in to CakeMods and 9to1 with one account. Keep your profile, preferences and signed-in devices together.</p><div class="account-art" aria-hidden="true"><div class="orbit"></div><span class="art-tile tile-nine"><img src="${NINE_LOGO_URL}" alt="" width="460" height="435"></span><span class="art-tile tile-cake"><img src="${CAKEMODS_LOGO_URL}" alt="" width="2626" height="2626" decoding="async"></span><span class="art-tile tile-account"><svg viewBox="0 0 40 40" fill="none"><circle cx="20" cy="13" r="6" stroke="currentColor" stroke-width="2.5"/><path d="M8 34v-3a12 12 0 0 1 24 0v3" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/></svg></span></div></aside><section class="card" aria-labelledby="account-title">${content}${statusRegion}<p class="card-note"><strong>Your 9to1 Account works across both sites.</strong><br>Manage your profile and sessions in your account.</p></section></main>
    <aside id="cookie-notice" class="cookie-notice" aria-labelledby="cookie-notice-title"><div><h2 id="cookie-notice-title">Essential cookies only</h2><p>This account service uses cookies for sign-in and security, with no analytics or advertising cookies. A session preference remembers when you dismiss this notice. <a href="${policyLinks.cookies}">Read our cookie information</a>.</p></div><button id="cookie-notice-dismiss" class="secondary" type="button">Got it</button></aside>
    <footer class="page-footer"><span>One account for CakeMods and 9to1.</span><nav aria-label="Account footer"><a href="${policyLinks.terms}">Terms</a><a href="${policyLinks.privacy}">Privacy</a><button class="cookie-link" id="cookie-notice-open" type="button">Cookie information</button><a href="https://cakemods.com">CakeMods</a><a href="https://9to1.uk">9to1</a></nav></footer>
    <script src="/assets/auth-ui.js?v=20261115-invite-tabs" defer></script>${challengeScript}</body></html>`, {
    headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff", "referrer-policy": "no-referrer", "content-security-policy": `default-src 'self'; script-src 'self'${challengeHost}; style-src 'unsafe-inline'; connect-src 'self'${challengeHost}; frame-src 'self'${challengeHost}; base-uri 'none'; frame-ancestors 'none'` },
  });
}

type AuthTab = "sign-in" | "sign-up" | "forgot-password";

const authCopy = {
  "sign-in": { label: "Sign In", title: "Welcome back.", description: "Sign in to your 9to1 Account to pick up where you left off." },
  "sign-up": { label: "Create Account", title: "Join with an invitation.", description: "Account creation is currently invite-only. Use the invitation sent to your email address." },
  "forgot-password": { label: "Account Recovery", title: "Let’s get you back in.", description: "Enter your email and we’ll help you reset your password." },
} as const;

function googleButton(enabled: boolean, active: AuthTab): string {
  if (!enabled) return "";
  return `<div id="google-auth"${active !== "sign-in" ? " hidden" : ""}><button id="google-sign-in" class="google-button" type="button"><svg aria-hidden="true" viewBox="0 0 24 24"><path fill="#4285F4" d="M21.6 12.2c0-.7-.1-1.4-.2-2.1H12v4h5.4c-.2 1.3-.9 2.3-2 3v2.5h3.3c1.9-1.8 2.9-4.3 2.9-7.4Z"/><path fill="#34A853" d="M12 22c2.7 0 5-1 6.7-2.5l-3.3-2.5c-.9.6-2.1 1-3.4 1-2.6 0-4.9-1.8-5.7-4.2H2.9v2.6A10 10 0 0 0 12 22Z"/><path fill="#FBBC05" d="M6.3 13.8a6 6 0 0 1 0-3.6V7.6H2.9a10 10 0 0 0 0 8.8l3.4-2.6Z"/><path fill="#EA4335" d="M12 6c1.5 0 2.8.5 3.8 1.5l2.9-2.9A9.6 9.6 0 0 0 12 2a10 10 0 0 0-9.1 5.6l3.4 2.6C7.1 7.8 9.4 6 12 6Z"/></svg>Continue with Google</button><p class="field-help">For existing 9to1 Accounts. New accounts need an invitation.</p><div class="divider">or use your email</div></div>`;
}

function verificationSlot(id: string, action: string, siteKey: string | null, required: boolean): string {
  const key = siteKey?.trim() ?? "";
  return `<div class="turnstile-slot" id="turnstile-${id}" data-turnstile-action="${action}" data-sitekey="${escapeAttribute(key)}" data-turnstile-required="${required}"${!required ? " hidden" : ""}>${required && !key ? '<p class="verification-notice">Account verification is temporarily unavailable. Please try again later.</p>' : ""}</div>`;
}

function signupAgeBands(): string {
  return `<div class="field age-field"><fieldset class="age-bands"><legend>Your age group</legend><label class="age-choice"><input name="ageBand" type="radio" value="under_13" required><span>Under 13</span></label><label class="age-choice"><input name="ageBand" type="radio" value="13_15" required><span>13–15</span></label><label class="age-choice"><input name="ageBand" type="radio" value="16_17" required><span>16–17</span></label><label class="age-choice"><input name="ageBand" type="radio" value="18_plus" required><span>18 or older</span></label></fieldset><p id="signup-age-ineligible" class="age-refusal" role="alert" hidden>9to1 Account is for people aged 13 and older. You cannot create an account with the under-13 age group.</p><p class="field-help">Your age group is recorded with your account. AI features are not available on these websites. AI use by under-16s will require parental controls, with mandatory safety protections for everyone under 18.</p></div>`;
}

function authPage(active: AuthTab, app: PublicApp | null, googleEnabled = false, siteKey: string | null = null, turnstileRequired = true, policy: AccountPolicyVersions | null = null): Response {
  const policiesReady = typeof policy?.termsVersion === "string" && !!policy.termsVersion.trim() &&
    typeof policy?.privacyVersion === "string" && !!policy.privacyVersion.trim();
  const termsVersion = policiesReady ? escapeAttribute(policy!.termsVersion) : "";
  const privacyVersion = policiesReady ? escapeAttribute(policy!.privacyVersion) : "";
  const policyNotice = policiesReady ? "" : '<p id="signup-policy-unavailable" class="verification-notice">Invitation redemption is temporarily unavailable while we prepare the account policies. Please try again later.</p>';
  const tabs = Object.entries(authCopy).map(([key, copy], index) => {
    const path = `/${key}` as "/sign-in" | "/sign-up" | "/forgot-password";
    return `<a id="tab-${key}" class="auth-tab" role="tab" aria-controls="panel-${key}" aria-selected="${key === active}" tabindex="${key === active ? 0 : -1}" href="${publicAppPath(path, app)}" data-auth-tab="${key}" data-tab-index="${index}" data-title="${copy.title}" data-description="${copy.description}">${copy.label}</a>`;
  }).join("");
  const state = (tab: AuthTab) => tab === active ? ' class="auth-panel is-entering"' : ' class="auth-panel" hidden';
  const content = `<nav id="auth-tabs" class="auth-tablist" role="tablist" aria-label="Account access" style="--active-tab:${Object.keys(authCopy).indexOf(active)}">${tabs}</nav>
    <div class="card-heading"><h1 id="account-title">${authCopy[active].title}</h1><p id="account-description">${authCopy[active].description}</p></div>
    ${googleButton(googleEnabled, active)}
    <section id="panel-sign-in" role="tabpanel" aria-labelledby="tab-sign-in"${state("sign-in")}><form id="sign-in-form" method="post"><div class="field"><label for="email">Email</label><input id="email" name="email" type="email" autocomplete="username" required maxlength="320" placeholder="you@example.com"></div><div class="field"><div class="field-label-row"><label for="password">Password</label><a href="${publicAppPath("/forgot-password", app)}" data-auth-link="forgot-password">Forgot password?</a></div><input id="password" name="password" type="password" autocomplete="current-password" required></div>${verificationSlot("sign-in", "login", siteKey, turnstileRequired)}<button class="primary" type="submit">Sign in <span aria-hidden="true">→</span></button></form></section>
    <section id="panel-sign-up" role="tabpanel" aria-labelledby="tab-sign-up"${state("sign-up")}><form id="sign-up-form" method="post" data-policy-unavailable="${!policiesReady}"><input type="hidden" name="termsVersion" value="${termsVersion}"><input type="hidden" name="privacyVersion" value="${privacyVersion}">${policyNotice}<div class="field"><label for="signup-invitation-code">Invitation code</label><input id="signup-invitation-code" name="invitationCode" type="text" autocomplete="off" autocapitalize="none" spellcheck="false" required maxlength="256" aria-describedby="invitation-help"><p class="field-help" id="invitation-help">Use the email address your invitation was sent to.</p></div><div class="field-grid"><div class="field"><label for="name">Name</label><input id="name" name="name" autocomplete="name" required maxlength="100"></div><div class="field"><label for="username">Username</label><input id="username" name="username" autocomplete="username" required minlength="3" maxlength="30" aria-describedby="username-help"></div></div><div class="field"><label for="signup-email">Email</label><input id="signup-email" name="email" type="email" autocomplete="email" required maxlength="320" placeholder="you@example.com"></div><div class="field"><label for="signup-password">Password</label><input id="signup-password" name="password" type="password" autocomplete="new-password" required minlength="8" maxlength="128" aria-describedby="password-help"><p class="field-help" id="password-help">Use at least 8 characters.</p></div><p class="field-help" id="username-help">Choose a username of 3–30 characters. We’ll email you a verification link.</p>${signupAgeBands()}<label class="terms-ack" for="signup-accept-terms"><input id="signup-accept-terms" name="acceptTerms" type="checkbox" value="true" required><span>I agree to the <a href="${policyLinks.terms}" target="_blank" rel="noopener">Terms of Service</a> and have read the <a href="${policyLinks.privacy}" target="_blank" rel="noopener">Privacy Notice</a>.</span></label>${verificationSlot("sign-up", "signup", siteKey, turnstileRequired)}<button class="primary" type="submit"${policiesReady ? "" : ' disabled aria-describedby="signup-policy-unavailable"'}>Create account <span aria-hidden="true">→</span></button></form></section>
    <section id="panel-forgot-password" role="tabpanel" aria-labelledby="tab-forgot-password"${state("forgot-password")}><form id="forgot-password-form" method="post"><div class="field"><label for="recovery-email">Email</label><input id="recovery-email" name="email" type="email" autocomplete="email" required maxlength="320" placeholder="you@example.com"></div><p class="field-help">If an account matches this address, we’ll send a link to choose a new password.</p>${verificationSlot("forgot-password", "recovery", siteKey, turnstileRequired)}<button class="primary" type="submit">Send reset link <span aria-hidden="true">→</span></button></form></section>`;
  return frame(authCopy[active].label, content, app, siteKey);
}

export function signInPage(app: PublicApp | null = null, googleEnabled = false, siteKey: string | null = null, turnstileRequired = true, policy: AccountPolicyVersions | null = null): Response {
  return authPage("sign-in", app, googleEnabled, siteKey, turnstileRequired, policy);
}

export function signUpPage(app: PublicApp | null = null, googleEnabled = false, siteKey: string | null = null, turnstileRequired = true, policy: AccountPolicyVersions | null = null): Response {
  return authPage("sign-up", app, googleEnabled, siteKey, turnstileRequired, policy);
}

export function forgotPasswordPage(app: PublicApp | null = null, googleEnabled = false, siteKey: string | null = null, turnstileRequired = true, policy: AccountPolicyVersions | null = null): Response {
  return authPage("forgot-password", app, googleEnabled, siteKey, turnstileRequired, policy);
}

export function resetPasswordPage(app: PublicApp | null = null): Response {
  return frame("Choose a new password", `<div class="standalone-panel"><div class="card-heading"><p class="eyebrow">ACCOUNT RECOVERY</p><h1 id="account-title">A fresh start.</h1><p>Choose a new password for your 9to1 Account.</p></div><form id="reset-password-form" method="post"><div class="field"><label for="password">New password</label><input id="password" name="password" type="password" autocomplete="new-password" required minlength="8" maxlength="128" aria-describedby="password-help"><p class="field-help" id="password-help">Use at least 8 characters.</p></div><button class="primary" type="submit">Save password <span aria-hidden="true">→</span></button></form><nav class="card-nav"><a href="${publicAppPath("/sign-in", app)}">← Back to sign in</a></nav></div>`, app);
}

/** The caller must verify the canonical administrator session before rendering. */
export function invitationsPage(): Response {
  return frame("Create an invitation", `<div class="standalone-panel"><div class="card-heading"><p class="eyebrow">ACCOUNT ADMINISTRATION</p><h1 id="account-title">Invite someone to 9to1.</h1><p>Create an invitation for one specific email address.</p></div><form id="create-invitation-form" method="post" action="/account/invitations"><div class="field"><label for="invitation-email">Recipient email</label><input id="invitation-email" name="email" type="email" autocomplete="off" required maxlength="320" aria-describedby="invitation-email-help"><p class="field-help" id="invitation-email-help">The recipient must use this exact email address when creating their account.</p></div><div class="field"><label for="invitation-expiry">Expires in</label><input id="invitation-expiry" name="expiryHours" type="number" value="72" min="1" max="168" step="1" required aria-describedby="invitation-expiry-help"><p class="field-help" id="invitation-expiry-help">Hours from now, between 1 and 168 (7 days).</p></div><button class="primary" type="submit">Create invitation <span aria-hidden="true">→</span></button></form><output id="invitation-result" class="invitation-result" for="invitation-email invitation-expiry" aria-label="Created invitation"></output><p class="safety-note">The invitation code is shown once. Copy it and share it privately with the exact recipient. Creating an invitation does not send an email. An address can have one active invitation at a time.</p><nav class="card-nav"><a href="/account">← Back to account administration</a></nav></div>`);
}

export interface AccountPolicyVersions {
  termsVersion: string;
  privacyVersion: string;
}

/** Only the server-selected, published policy versions may reach this screen. */
export function onboardingPage(app: PublicApp | null, policy: AccountPolicyVersions): Response {
  if (!policy || !policy.termsVersion?.trim() || !policy.privacyVersion?.trim()) {
    return json({ error: "account_policy_unavailable" }, 503);
  }
  const termsVersion = escapeAttribute(policy.termsVersion);
  const privacyVersion = escapeAttribute(policy.privacyVersion);
  return frame("Finish account setup", `<div class="standalone-panel"><div class="card-heading"><p class="eyebrow">BEFORE YOU CONTINUE</p><h1 id="account-title">A few account details.</h1><p>Confirm your age group and review the account terms before continuing to your account.</p></div><form id="public-onboarding-form" method="post"><input type="hidden" name="termsVersion" value="${termsVersion}"><input type="hidden" name="privacyVersion" value="${privacyVersion}"><fieldset class="age-bands"><legend>Your age group</legend><label class="age-choice"><input name="ageBand" type="radio" value="under_13" required><span>Under 13</span></label><label class="age-choice"><input name="ageBand" type="radio" value="13_15" required><span>13–15</span></label><label class="age-choice"><input name="ageBand" type="radio" value="16_17" required><span>16–17</span></label><label class="age-choice"><input name="ageBand" type="radio" value="18_plus" required><span>18 or older</span></label></fieldset><p id="age-ineligible" class="age-refusal" role="alert" hidden>9to1 Account is for people aged 13 and older. You cannot continue with the under-13 age group.</p><p class="safety-note">AI features are not currently available on these websites. When available, AI use by under-16s will require parental controls, and everyone under 18 will have mandatory safety protections.</p><label class="terms-ack" for="onboarding-accept-terms"><input id="onboarding-accept-terms" name="acceptTerms" type="checkbox" value="true" required><span>I agree to the <a href="${policyLinks.terms}" target="_blank" rel="noopener">Terms of Service</a> and have read the <a href="${policyLinks.privacy}" target="_blank" rel="noopener">Privacy Notice</a>.</span></label><button class="primary" type="submit">Continue <span aria-hidden="true">→</span></button></form></div>`, app);
}

export function accountManagementPage(title: string, content: string): Response {
  return frame(title, `<div class="management-panel">${content}</div>`, null, null, true);
}

export function consentPage(clientId: string, requestedScopes: string[]): Response {
  const safeClientId = JSON.stringify(clientId).replaceAll("&", "&amp;").replaceAll("<", "\\u003c").replaceAll("'", "&#39;");
  const safeScopes = JSON.stringify(requestedScopes).replaceAll("&", "&amp;").replaceAll("<", "\\u003c").replaceAll("'", "&#39;");
  const requested = new Set(requestedScopes);
  const known = new Set(["openid", "profile", "email", "offline_access", "cake:account:read", "cake:profile:read", "cake:profile:write", "cake:sessions:read", "cake:sessions:revoke"]);
  const unknown = requestedScopes.filter(scope => !known.has(scope));
  const permissions: [string, string][] = [];
  const identity = [requested.has("profile") ? "basic profile" : "", requested.has("email") ? "email address and verification status" : "", requested.has("cake:account:read") ? "account details" : ""].filter(Boolean);
  if (requested.has("openid") || identity.length) permissions.push(["Sign you in", identity.length ? `Read your ${identity.join(", ")}.` : "Confirm your account identity."]);
  if (requested.has("cake:profile:read")) permissions.push(["Read your shared profile", "View your profile, appearance preferences and the account details included in your account export."]);
  if (requested.has("cake:profile:write")) permissions.push(["Update your profile and preferences", "Save changes to your shared profile information and appearance preferences."]);
  if (requested.has("cake:sessions:read") || requested.has("cake:sessions:revoke")) permissions.push([
    requested.has("cake:sessions:read") && requested.has("cake:sessions:revoke") ? "View and sign out your devices" : requested.has("cake:sessions:read") ? "View your signed-in devices" : "Sign out your devices",
    "This applies to sessions across your 9to1 Account."]);
  if (requested.has("offline_access")) permissions.push(["Keep the connection signed in", "Continue the connection between visits until you sign out or the session expires."]);
  const details = permissions.map(([title, description]) => `<li><strong>${escapeAttribute(title)}</strong><span>${escapeAttribute(description)}</span></li>`).join("");
  const warning = unknown.length ? `<p class="age-refusal">This app requests access we cannot explain yet. Cancel this request and contact support.</p><details class="permission-details"><summary>Unrecognised permissions</summary><p>${unknown.map(escapeAttribute).join(", ")}</p></details>` : "";
  const data = `data-client-id='${safeClientId}' data-scopes='${safeScopes}' data-reviewable="${unknown.length === 0}"`;
  return frame("Connect an app", `<div class="standalone-panel"><div class="card-heading"><p class="eyebrow">ACCOUNT CONNECTION</p><h1 id="account-title">Connect an app.</h1><p>Choose whether this app can use the account information below.</p></div><section id="consent" ${data}><p class="client-name" id="client-name">Checking the app…</p><p class="consent-copy">is requesting this connection to your 9to1 Account.</p><ul id="scope-list" class="permission-list">${details}</ul>${warning}<p class="field-help">This connection does not share your password with the app.</p><button class="primary" id="allow" type="button" disabled>Connect account <span aria-hidden="true">→</span></button><button class="secondary" id="deny" type="button">Cancel</button></section></div>`);
}

export function json(data: unknown, status = 200): Response {
  return Response.json(data, { status, headers: { "cache-control": "no-store", "x-content-type-options": "nosniff" } });
}
