import { createAuthClient } from "better-auth/client";
import { oauthProviderClient } from "@better-auth/oauth-provider/client";
import { installPresentAppAction, installStagingAppsAction } from "./staging-apps-browser";
import { installProductionSitesActions } from "./production-sites-browser";
import { googleFlowCallbacks, publicAppDestination, publicAppPath, readPublicApp } from "./public-app";
import { installAuthTabs } from "./auth-tabs";
import { installTurnstile, VerificationError } from "./turnstile-browser";
import { installInvitationAction } from "./invitations-browser";

const auth = createAuthClient({
  baseURL: `${location.origin}/api/auth`,
  plugins: [oauthProviderClient()],
});

const status = document.querySelector<HTMLElement>("[role=status]");
const setStatus = (message: string) => { if (status) status.textContent = message; };
const publicApp = readPublicApp(new URLSearchParams(location.search));
const verification = installTurnstile();
const challengeBody = (token: string): Record<string, string> => token ? { "cf-turnstile-response": token } : {};

function guardedSubmit<T extends HTMLFormElement>(id: string, handler: (form: T, challenge: string) => Promise<void>): void {
  const form = document.getElementById(id) as T | null;
  if (!form) return;
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (form.dataset.policyUnavailable === "true") {
      setStatus("Invitation redemption is temporarily unavailable while account policies are reviewed."); return;
    }
    if (form.dataset.busy === "true") return;
    form.dataset.busy = "true";
    const button = form.querySelector<HTMLButtonElement>("button[type=submit]");
    if (button) button.disabled = true;
    setStatus("");
    let challenge = "";
    try { challenge = verification.take(id); await handler(form, challenge); }
    catch (error) { setStatus(error instanceof VerificationError ? error.message : "That request could not be completed. Check the details and try again."); }
    finally {
      if (challenge) verification.reset(id);
      form.dataset.busy = "false";
      if (button) button.disabled = form.dataset.policyUnavailable === "true" || new FormData(form).get("ageBand") === "under_13";
    }
  });
}

const value = (form: HTMLFormElement, field: string) => {
  const raw = new FormData(form).get(field)?.toString() ?? "";
  return field === "password" ? raw : raw.trim();
};

installStagingAppsAction();
installPresentAppAction();
installProductionSitesActions();
installInvitationAction();
installAuthTabs();

const googleButton = document.getElementById("google-sign-in") as HTMLButtonElement | null;
if (["error", "complete"].includes(new URLSearchParams(location.search).get("social") ?? ""))
  setStatus("Google sign-in was not completed. Try again or sign in with email.");
googleButton?.addEventListener("click", async () => {
  if (googleButton.disabled) return;
  googleButton.disabled = true;
  setStatus("Opening Google sign-in…");
  try {
    const { data, error } = await auth.signIn.social({ provider: "google", ...googleFlowCallbacks(location.origin, location.search) });
    // Better Auth owns redirecting to the configured provider. A failed or lost
    // request never creates a local success state or changes account authority.
    if (error || data?.redirect !== true || typeof data.url !== "string") { setStatus("Google sign-in could not be started. Try again or sign in with email."); googleButton.disabled = false; }
  } catch { setStatus("Google sign-in could not be started. Try again or sign in with email."); googleButton.disabled = false; }
});

guardedSubmit("sign-in-form", async (form, challenge) => {
  const { data, error } = await auth.signIn.email({ email: value(form, "email"), password: value(form, "password"), ...challengeBody(challenge) });
  if (error) return setStatus("Email or password was not accepted, or the account is not verified.");
  // The standard client already redirects after checking the URL scheme.
  if (!(data?.redirect && data.url)) location.assign(publicAppDestination(publicApp));
});

guardedSubmit("sign-up-form", async (form, challenge) => {
  const { error } = await auth.$fetch("/sign-up/email", {
    method: "POST",
    body: {
      name: value(form, "name"),
      username: value(form, "username"),
      email: value(form, "email"),
      password: value(form, "password"),
      callbackURL: `${location.origin}${publicAppPath("/sign-in", publicApp)}`,
      invitationCode: value(form, "invitationCode"),
      ageBand: value(form, "ageBand"),
      acceptTerms: new FormData(form).get("acceptTerms") === "true",
      termsVersion: value(form, "termsVersion"),
      privacyVersion: value(form, "privacyVersion"),
      ...challengeBody(challenge),
    },
  });
  if (error) return setStatus("code" in error && error.code === "account_policy_unavailable"
    ? "Invitation redemption is temporarily unavailable while account policies are reviewed."
    : "The account could not be created. Check your invitation, age group and account details, or try signing in.");
  setStatus("Check your email for a verification link before signing in.");
});

guardedSubmit("forgot-password-form", async (form, challenge) => {
  const { error } = await auth.requestPasswordReset({ email: value(form, "email"), redirectTo: `${location.origin}${publicAppPath("/reset-password", publicApp)}`, ...challengeBody(challenge) });
  if (error) return setStatus("We could not request a reset link. Please try again later.");
  setStatus("If an account matches the address, 9to1 Account will send a reset link.");
});

guardedSubmit("reset-password-form", async (form) => {
  const token = new URLSearchParams(location.search).get("token");
  if (!token) return setStatus("This reset link is invalid or has expired.");
  const { error } = await auth.resetPassword({ token, newPassword: value(form, "password") });
  if (error) return setStatus("This reset link is invalid or has expired.");
  setStatus("Password updated. You can now sign in.");
});

guardedSubmit("public-onboarding-form", async form => {
  const response = await fetch("/account/onboarding", { method: "POST", credentials: "same-origin", cache: "no-store",
    headers: { "content-type": "application/json" }, body: JSON.stringify({ ageBand: value(form, "ageBand"),
      acceptTerms: new FormData(form).get("acceptTerms") === "true", termsVersion: value(form, "termsVersion"), privacyVersion: value(form, "privacyVersion") }) });
  if (!response.ok) return setStatus(response.status === 503 ? "Account setup is temporarily unavailable while account policies are reviewed."
    : "Account setup could not be saved. Confirm your age group and the current policies, or request a correction if your age record is wrong.");
  const result = await response.json() as { ready?: unknown };
  if (result.ready !== true) return setStatus("Account setup could not be confirmed. Please reload before trying again.");
  const params = new URLSearchParams(location.search);
  if (params.has("sig") && params.has("client_id")) {
    const { data, error } = await auth.oauth2.continue({ postLogin: true });
    if (error) return setStatus("Your details were saved, but the sign-in request could not continue. Start sign-in again from the website.");
    if (data?.redirect && data.url) return;
  }
  location.assign(publicAppDestination(publicApp));
});

const consent = document.getElementById("consent");
if (consent) {
  const clientId = JSON.parse(consent.dataset.clientId ?? '""') as string;
  const canReview = consent.dataset.reviewable === "true";
  const allow = document.getElementById("allow") as HTMLButtonElement | null;
  let verifiedApp = false;
  let consentBusy = false;
  void auth.oauth2.publicClient({ query: { client_id: clientId } }).then(({ data, error }) => {
    const name = data?.client_name;
    const label = document.getElementById("client-name");
    if (error || typeof name !== "string" || !name.trim()) {
      setStatus("The app could not be verified. Cancel this request and try again from the app.");
      return;
    }
    if (label) label.textContent = name;
    verifiedApp = true;
    if (allow) allow.disabled = !canReview;
  }).catch(() => setStatus("The app could not be verified. Cancel this request and try again from the app."));

  const submitConsent = async (accept: boolean) => {
    const buttons = ["allow", "deny"].map((id) => document.getElementById(id) as HTMLButtonElement | null);
    if (consentBusy || (accept && (!canReview || !verifiedApp))) return;
    consentBusy = true;
    buttons.forEach((button) => { if (button) button.disabled = true; });
    try {
      const { data, error } = await auth.oauth2.consent({ accept });
      if (error) setStatus("The authorization request could not be completed.");
      // The standard client owns the redirect; assigning again schedules it twice.
      else if (!(data?.redirect && data.url)) location.assign("/account");
    } catch {
      setStatus("The authorization request could not be completed.");
    } finally {
      consentBusy = false;
      buttons.forEach((button) => { if (button) button.disabled = button.id === "allow" && (!canReview || !verifiedApp); });
    }
  };
  document.getElementById("allow")?.addEventListener("click", () => { void submitConsent(true); });
  document.getElementById("deny")?.addEventListener("click", () => { void submitConsent(false); });
}
