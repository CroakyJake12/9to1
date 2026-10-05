import { createAuthClient } from "better-auth/client";
import { oauthProviderClient } from "@better-auth/oauth-provider/client";

const auth = createAuthClient({
  baseURL: `${location.origin}/api/auth`,
  plugins: [oauthProviderClient()],
});

const status = document.querySelector<HTMLElement>("[role=status]");
const setStatus = (message: string) => { if (status) status.textContent = message; };

function guardedSubmit<T extends HTMLFormElement>(id: string, handler: (form: T) => Promise<void>): void {
  const form = document.getElementById(id) as T | null;
  if (!form) return;
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (form.dataset.busy === "true") return;
    form.dataset.busy = "true";
    const button = form.querySelector<HTMLButtonElement>("button[type=submit]");
    if (button) button.disabled = true;
    setStatus("");
    try { await handler(form); }
    catch { setStatus("That request could not be completed. Check the details and try again."); }
    finally {
      form.dataset.busy = "false";
      if (button) button.disabled = false;
    }
  });
}

const value = (form: HTMLFormElement, field: string) => new FormData(form).get(field)?.toString().trim() ?? "";

guardedSubmit("sign-in-form", async (form) => {
  const { data, error } = await auth.signIn.email({ email: value(form, "email"), password: value(form, "password") });
  if (error) return setStatus("Email or password was not accepted, or the account is not verified.");
  // The standard client already redirects after checking the URL scheme.
  if (!(data?.redirect && data.url)) location.assign("/account");
});

guardedSubmit("sign-up-form", async (form) => {
  const { error } = await auth.$fetch("/sign-up/email", {
    method: "POST",
    body: {
      name: value(form, "name"),
      username: value(form, "username"),
      email: value(form, "email"),
      password: value(form, "password"),
    },
  });
  if (error) return setStatus("The account could not be created. Check the fields or try signing in.");
  setStatus("Check your email for a verification link before signing in.");
});

guardedSubmit("forgot-password-form", async (form) => {
  await auth.requestPasswordReset({ email: value(form, "email"), redirectTo: `${location.origin}/reset-password` });
  setStatus("If an account matches the address, CAKE ID will send a reset link.");
});

guardedSubmit("reset-password-form", async (form) => {
  const token = new URLSearchParams(location.search).get("token");
  if (!token) return setStatus("This reset link is invalid or has expired.");
  const { error } = await auth.resetPassword({ token, newPassword: value(form, "password") });
  if (error) return setStatus("This reset link is invalid or has expired.");
  setStatus("Password updated. You can now sign in.");
});

const consent = document.getElementById("consent");
if (consent) {
  const clientId = JSON.parse(consent.dataset.clientId ?? '""') as string;
  const scopes = JSON.parse(consent.dataset.scopes ?? "[]") as string[];
  const scopeList = document.getElementById("scope-list");
  if (scopeList) {
    for (const scope of scopes) {
      const item = document.createElement("p");
      item.className = "scope";
      item.textContent = scope;
      scopeList.appendChild(item);
    }
  }
  void auth.oauth2.publicClient({ query: { client_id: clientId } }).then(({ data }) => {
    const name = data?.client_name;
    const label = document.getElementById("client-name");
    if (name && label) label.textContent = name;
  }).catch(() => setStatus("Could not verify the application details."));

  const submitConsent = async (accept: boolean) => {
    const buttons = ["allow", "deny"].map((id) => document.getElementById(id) as HTMLButtonElement | null);
    if (buttons.some((button) => button?.disabled)) return;
    buttons.forEach((button) => { if (button) button.disabled = true; });
    try {
      const { data, error } = await auth.oauth2.consent({ accept });
      if (error) setStatus("The authorization request could not be completed.");
      // The standard client owns the redirect; assigning again schedules it twice.
      else if (!(data?.redirect && data.url)) location.assign("/account");
    } catch {
      setStatus("The authorization request could not be completed.");
    } finally {
      buttons.forEach((button) => { if (button) button.disabled = false; });
    }
  };
  document.getElementById("allow")?.addEventListener("click", () => { void submitConsent(true); });
  document.getElementById("deny")?.addEventListener("click", () => { void submitConsent(false); });
}
