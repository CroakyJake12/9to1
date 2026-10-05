const sharedCss = `
  :root{color-scheme:light dark;font:16px/1.5 system-ui,sans-serif;background:#121416;color:#f4f5f6}
  *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px}
  main{width:min(100%,440px);padding:32px;border:1px solid #41464b;border-radius:18px;background:#1b1e21}
  h1{margin:0 0 8px;font-size:1.8rem}p{color:#c1c7cc}label{display:block;margin:16px 0 6px;font-weight:600}
  input{width:100%;padding:12px;border:1px solid #656c73;border-radius:8px;font:inherit;background:#101214;color:inherit}
  button{width:100%;margin-top:20px;padding:12px;border:0;border-radius:8px;background:#f2c14e;color:#111;font:inherit;font-weight:700;cursor:pointer}
  button:disabled{opacity:.65;cursor:wait}a{color:#f2c14e}nav{display:flex;justify-content:space-between;gap:12px;margin-top:18px}
  [role=status]{min-height:1.5em;color:#f2c14e}.scope{padding:8px 12px;border-radius:8px;background:#292e32;margin:8px 0}
`;

function frame(title: string, content: string): Response {
  return new Response(`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${title} · CAKE ID</title><style>${sharedCss}</style><body><main><h1>${title}</h1>${content}<script src="/assets/auth-ui.js" defer></script></main></body></html>`, {
    headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff", "referrer-policy": "no-referrer", "content-security-policy": "default-src 'self'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'" },
  });
}

export function signInPage(): Response {
  return frame("Sign in", `<p>Use your CAKE ID to continue.</p><form id="sign-in-form"><label for="email">Email</label><input id="email" name="email" type="email" autocomplete="username" required maxlength="320"><label for="password">Password</label><input id="password" name="password" type="password" autocomplete="current-password" required><button type="submit">Sign in</button></form><p role="status" aria-live="polite"></p><nav><a href="/sign-up">Create an account</a><a href="/forgot-password">Forgot password?</a></nav>`);
}

export function signUpPage(): Response {
  return frame("Create CAKE ID", `<p>Your account works across CAKE and 9to1.</p><form id="sign-up-form"><label for="name">Name</label><input id="name" name="name" autocomplete="name" required maxlength="100"><label for="username">Username</label><input id="username" name="username" autocomplete="username" required minlength="3" maxlength="30"><label for="email">Email</label><input id="email" name="email" type="email" autocomplete="email" required maxlength="320"><label for="password">Password</label><input id="password" name="password" type="password" autocomplete="new-password" required minlength="8" maxlength="128"><button type="submit">Create account</button></form><p role="status" aria-live="polite"></p><nav><a href="/sign-in">Sign in</a></nav>`);
}

export function forgotPasswordPage(): Response {
  return frame("Reset password", `<p>If an account matches the address, CAKE ID will send a reset link.</p><form id="forgot-password-form"><label for="email">Email</label><input id="email" name="email" type="email" autocomplete="email" required maxlength="320"><button type="submit">Send reset link</button></form><p role="status" aria-live="polite"></p><nav><a href="/sign-in">Back to sign in</a></nav>`);
}

export function resetPasswordPage(): Response {
  return frame("Choose a new password", `<form id="reset-password-form"><label for="password">New password</label><input id="password" name="password" type="password" autocomplete="new-password" required minlength="8" maxlength="128"><button type="submit">Save password</button></form><p role="status" aria-live="polite"></p>`);
}

export function consentPage(clientId: string, requestedScopes: string[]): Response {
  const safeClientId = JSON.stringify(clientId).replaceAll("&", "&amp;").replaceAll("<", "\\u003c").replaceAll("'", "&#39;");
  const safeScopes = JSON.stringify(requestedScopes).replaceAll("&", "&amp;").replaceAll("<", "\\u003c").replaceAll("'", "&#39;");
  const data = `data-client-id='${safeClientId}' data-scopes='${safeScopes}'`;
  return frame("Authorize application", `<section id="consent" ${data}><p id="client-name">A connected application</p><p>wants access to your CAKE ID.</p><div id="scope-list"></div><button id="allow">Allow access</button><button id="deny" type="button">Decline</button></section><p role="status" aria-live="polite"></p>`);
}

export function json(data: unknown, status = 200): Response {
  return Response.json(data, { status, headers: { "cache-control": "no-store", "x-content-type-options": "nosniff" } });
}
