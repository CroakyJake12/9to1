import { readFileSync, statSync } from 'node:fs';
import { publicAccountConfiguration } from './theme-browser-proxy.mjs';

// Browser-controller helper, never shipped to the app. Call triggerSignIn using
// the real UI (semantic button or inspected coordinates), not owner/API dispatch.
// Disable Playwright API debug logging/traces while credentials are entered.
export async function signInFixtureAccount({ page, privateManifestPath, accountIndex = 0, triggerSignIn }) {
  if (process.env.DEBUG?.includes('pw:') || process.env.PWDEBUG) throw new Error('Credential-safe browser logging is required.');
  if (typeof triggerSignIn !== 'function' || ![0, 1].includes(accountIndex)) throw new Error('An actual UI sign-in action and fixture account index are required.');
  const mode = statSync(privateManifestPath).mode & 0o777;
  if (mode !== 0o600) throw new Error('Private fixture manifest permissions must be0600.');
  let popup;
  try {
    const manifest = JSON.parse(readFileSync(privateManifestPath, 'utf8'));
    const config = publicAccountConfiguration(manifest);
    if (new URL(page.url()).origin !== manifest.browserOrigin) throw new Error('Wrong application origin.');
    const account = manifest.accounts[accountIndex];
    if (!account?.email?.endsWith('@example.test') || typeof account.password !== 'string') throw new Error('Synthetic fixture credentials required.');
    [popup] = await Promise.all([page.waitForEvent('popup', { timeout: 15000 }), triggerSignIn()]);
    const closed = popup.waitForEvent('close', { timeout: 30000 });
    closed.catch(() => {}); // Retain the original rejection for the awaited race.
    const visible = (selector, state) => popup.locator(selector).waitFor({ state: 'visible', timeout: 30000 })
      .then(() => state).catch(error => { if (popup.isClosed()) return 'closed'; throw error; });
    let next = await Promise.race([closed.then(() => 'closed'), visible('#sign-in-form #email', 'signin'), visible('#allow', 'consent')]);
    let submittedRealSignIn = false;
    if (next === 'signin') {
      if (new URL(popup.url()).origin !== config.apiResource) throw new Error('Unexpected credential recipient.');
      await popup.locator('#email').fill(account.email);
      await popup.locator('#password').fill(account.password);
      await popup.getByRole('button', { name: 'Sign in', exact: true }).click();
      submittedRealSignIn = true;
      next = await Promise.race([closed.then(() => 'closed'), visible('#allow', 'consent')]);
    }
    // An existing actual CAKE session/grant may legitimately skip either form.
    if (next === 'consent') {
      if (new URL(popup.url()).origin !== config.apiResource) throw new Error('Unexpected consent origin.');
      await popup.locator('#allow').click();
      await closed;
    }
    // The caller must still inspect the real connected-account/appearance UI.
    return { fixtureOnly: true, submittedRealSignIn, popupClosed: true, accountIndex };
  } catch {
    // Playwright errors can include fill values or callback URLs. Keep them out of
    // tool output; the owner may inspect redacted UI state after this refusal.
    throw new Error('Actual local account sign-in failed; no credential or callback details emitted.');
  }
}
