// Called once by the existing auth-ui bundle. No request runs until the real form submits.
export function installStagingAppsAction(): void {
  const form = document.getElementById("connect-staging-apps") as HTMLFormElement | null;
  const status = document.getElementById("staging-apps-status");
  if (!form || !status) return;
  const button = form.querySelector<HTMLButtonElement>("button[type=submit]");
  let busy = false;
  const message = (text: string) => { status.textContent = text; };
  const read = async () => {
    const response = await fetch("/account/staging-apps", {
      credentials: "same-origin", cache: "no-store", headers: { accept: "application/json" },
    });
    if (!response.ok) throw new Error("The current administrator session could not be verified.");
    return await response.json();
  };
  const showReady = (state: { kind?: string; clientId?: unknown }): boolean => {
    if (state.kind !== "ready" || typeof state.clientId !== "string" || !state.clientId) return false;
    message("Staging app OAuth client connected. Public client ID: " + state.clientId);
    if (button) button.disabled = true;
    return true;
  };
  const connect = async () => {
    const state = await read();
    if (showReady(state)) return;
    if (state.kind !== "missing" || !state.registration) {
      message("Existing staging client metadata needs review. No new client was created.");
      return;
    }
    let response: Response | undefined;
    let submitted = false;
    let primary: unknown;
    try {
      // This is the existing maintained session/CSRF/admin-privileged endpoint.
      submitted = true;
      response = await fetch("/api/auth/oauth2/create-client", {
        method: "POST", credentials: "same-origin",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify(state.registration),
      });
    } catch (error) { primary = error; }
    // A lost reply can follow a committed registration. Never automatically POST again.
    try {
      const current = await read();
      if (showReady(current)) return;
      if (current.kind === "conflict") {
        message("Staging client metadata needs review. No further registration was attempted.");
      } else if (response && !response.ok) {
        message("Client registration was refused (HTTP " + response.status + "). Refresh the account page before retrying.");
      } else {
        message("Registration outcome could not be confirmed. Refresh the account page before retrying.");
      }
    } catch {
      message(primary || submitted
        ? "Registration outcome could not be confirmed. Refresh the account page before retrying."
        : "The current administrator session could not be verified.");
    }
    // Keep the form disabled after a POST with an unconfirmed/refused outcome; a fresh GET reconciles.
    if (button) button.disabled = true;
  };
  form.addEventListener("submit", async event => {
    event.preventDefault();
    if (busy || button?.disabled) return;
    busy = true;
    if (button) button.disabled = true;
    message("Connecting staging apps…");
    try {
      // Same-origin tabs with Web Locks share one preflight+POST+reconciliation interval.
      if (navigator.locks) {
        await navigator.locks.request("cake-connect-staging-apps", { ifAvailable: true }, async lock => {
          if (!lock) { message("Another account tab is connecting staging apps. Refresh when it finishes."); return; }
          await connect();
        });
      } else await connect();
    } catch {
      message("The current administrator session could not be verified. Refresh the account page.");
    } finally { busy = false; }
  });
}
