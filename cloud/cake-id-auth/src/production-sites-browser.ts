// Each action synchronises only the fixed approved details of one pinned website.
export function installProductionSitesActions(): void {
  for (const site of ["cake", "nine"] as const) {
    const form = document.getElementById(`connect-production-${site}`) as HTMLFormElement | null;
    const status = document.getElementById(`production-${site}-status`);
    if (!form || !status) continue;
    const button = form.querySelector<HTMLButtonElement>("button[type=submit]");
    const message = (text: string) => { status.textContent = text; };
    let busy = false;
    type Client = { kind?: string; clientId?: unknown; directSignIn?: unknown; displayNameCurrent?: unknown };
    type State = { kind?: string; cake?: Client; nine?: Client };
    const read = async (): Promise<State> => {
      const response = await fetch("/account/production-sites/state", {
        credentials: "same-origin", cache: "no-store", headers: { accept: "application/json" },
      });
      if (!response.ok) throw new Error("Administrator session unavailable");
      return await response.json();
    };
    const showReady = (state: State): boolean => {
      const client = state[site];
      if (state.kind !== "admin" || client?.kind !== "ready" || client.directSignIn !== true || client.displayNameCurrent !== true || typeof client.clientId !== "string" || !client.clientId) return false;
      message("Website details are synchronised with your 9to1 Account.");
      return true;
    };
    const connect = async () => {
      const state = await read();
      if (showReady(state)) return;
      const client = state[site];
      if (state.kind !== "admin" || client?.kind !== "ready") {
        message("Existing client metadata needs review. No settings were changed.");
        return;
      }
      let response: Response | undefined;
      try {
        response = await fetch("/account/production-sites", {
          method: "POST", credentials: "same-origin",
          headers: { "content-type": "application/json", accept: "application/json" },
          body: JSON.stringify({ site }),
        });
      } catch { /* A lost response can follow a successful write. Read, never replay. */ }
      try {
        const observed = await read();
        if (showReady(observed)) return;
        if (observed[site]?.kind === "conflict") message("Client metadata needs review. No further change was attempted.");
        else if (response && !response.ok) message("The change was refused (HTTP " + response.status + "). Reload before retrying.");
        else message("The change could not be confirmed. Reload before retrying.");
      } catch { message("The change could not be confirmed. Reload before retrying."); }
    };
    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (busy || button?.disabled) return;
      busy = true;
      if (button) button.disabled = true;
      message("Synchronising website details…");
      try {
        // Refuse browsers without cross-tab coordination for this privileged action.
        if (!navigator.locks) { message("Use a browser with Web Locks support to connect this website."); return; }
        await navigator.locks.request(`cake-connect-production-${site}`, { ifAvailable: true }, async lock => {
          if (!lock) { message("Another account tab is connecting this website. Reload when it finishes."); return; }
          await connect();
        });
      } catch { message("The current administrator session could not be verified. Reload the account page."); }
      // Reload observes the canonical state after success, refusal or an unknown result.
      finally { busy = false; }
    });
  }
}
