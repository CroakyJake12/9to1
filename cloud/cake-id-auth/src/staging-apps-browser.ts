// Called once by the existing auth-ui bundle. No request runs until the real form submits.
export function installStagingAppsAction(): void {
  const form = document.getElementById("connect-staging-apps") as HTMLFormElement | null;
  const status = document.getElementById("staging-apps-status");
  if (!form || !status) return;
  const button = form.querySelector<HTMLButtonElement>("button[type=submit]");
  let busy = false;
  const message = (text: string) => { status.textContent = text; };
  type ClientState = { kind?: string; clientId?: unknown; registration?: unknown };
  type State = ClientState & { native?: ClientState };
  const read = async (): Promise<State> => {
    const response = await fetch("/account/staging-apps", {
      credentials: "same-origin", cache: "no-store", headers: { accept: "application/json" },
    });
    if (!response.ok) throw new Error("The current administrator session could not be verified.");
    return await response.json();
  };
  const ready = (client: ClientState | undefined): client is ClientState & { clientId: string } =>
    client?.kind === "ready" && typeof client.clientId === "string" && !!client.clientId;
  const showReady = (state: State): boolean => {
    if (!ready(state) || !ready(state.native)) return false;
    message("Staging clients connected. Web public client ID: " + state.clientId +
      "; Windows native public client ID: " + state.native.clientId);
    return true;
  };
  const select = (state: State, target: "web" | "native"): ClientState | undefined =>
    target === "web" ? state : state.native;
  const connectOne = async (target: "web" | "native"): Promise<boolean> => {
    // Fresh canonical admin/owner/metadata preflight before EACH possible POST.
    const state = await read();
    const current = select(state, target);
    if (ready(current)) return true;
    if (state.kind === "not-admin" || state.kind === "conflict" || state.native?.kind === "conflict" ||
        current?.kind !== "missing" || !current.registration) {
      message("Existing staging client metadata needs review. No new client was created.");
      return false;
    }
    let response: Response | undefined;
    try {
      // Same real browser session, CSRF/admin enforcement, maintained endpoint and native metadata validator.
      response = await fetch("/api/auth/oauth2/create-client", {
        method: "POST", credentials: "same-origin",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify(current.registration),
      });
    } catch { /* A lost reply may follow a commit: reconcile without another POST. */ }
    try {
      const reconciled = await read();
      if (ready(select(reconciled, target))) return true;
      if (reconciled.kind === "conflict" || reconciled.native?.kind === "conflict")
        message("Staging client metadata needs review. No further registration was attempted.");
      else if (response && !response.ok)
        message("Client registration was refused (HTTP " + response.status + "). Refresh the account page before retrying.");
      else message("Registration outcome could not be confirmed. Refresh the account page before retrying.");
    } catch { message("Registration outcome could not be confirmed. Refresh the account page before retrying."); }
    return false;
  };
  const connect = async () => {
    if (!await connectOne("web")) return;
    if (!await connectOne("native")) return;
    if (!showReady(await read())) message("Client metadata changed. Refresh the account page before retrying.");
  };
  form.addEventListener("submit", async event => {
    event.preventDefault();
    if (busy || button?.disabled) return;
    busy = true;
    if (button) button.disabled = true;
    message("Connecting staging apps…");
    try {
      // Preserve same-origin cross-tab suppression for the entire pair of originals.
      if (navigator.locks) {
        await navigator.locks.request("cake-connect-staging-apps", { ifAvailable: true }, async lock => {
          if (!lock) { message("Another account tab is connecting staging apps. Refresh when it finishes."); return; }
          await connect();
        });
      } else await connect();
    } catch { message("The current administrator session could not be verified. Refresh the account page."); }
    // Stay disabled after success/refusal/unknown outcome. A new page GET reconciles, never provisions.
    finally { busy = false; }
  });
}
