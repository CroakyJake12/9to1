export function installInvitationAction(): void {
  const form = document.getElementById("create-invitation-form") as HTMLFormElement | null;
  const output = document.getElementById("invitation-result");
  const status = document.querySelector<HTMLElement>("[role=status]");
  if (!form || !output || !status) return;
  const button = form.querySelector<HTMLButtonElement>("button[type=submit]");
  form.addEventListener("submit", async event => {
    event.preventDefault();
    if (form.dataset.busy === "true" || button?.disabled) return;
    form.dataset.busy = "true";
    if (button) button.disabled = true;
    output.textContent = ""; status.textContent = "Creating invitation…";
    const fields = new FormData(form);
    try {
      const response = await fetch("/account/invitations", { method: "POST", credentials: "same-origin", cache: "no-store",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ email: fields.get("email")?.toString().trim(), expiryHours: Number(fields.get("expiryHours")) }) });
      if (!response.ok) {
        status.textContent = response.status === 409 ? "An invitation was not created. An active invitation or account may already exist. Review it before retrying."
          : "The invitation could not be created. Reload and verify your administrator session before retrying.";
        return;
      }
      const data = await response.json() as { code?: unknown; invitation?: { email?: unknown; expiresAt?: unknown } };
      if (typeof data.code !== "string" || !/^[A-Za-z0-9_-]{43}$/.test(data.code) || typeof data.invitation?.email !== "string" ||
          typeof data.invitation.expiresAt !== "number" || !Number.isFinite(data.invitation.expiresAt)) throw new Error("Unknown outcome");
      output.textContent = `Invitation for ${data.invitation.email}\nCode: ${data.code}\nExpires: ${new Date(data.invitation.expiresAt).toLocaleString()}\nCopy this code now and share it privately with that recipient. It will not be shown again.`;
      status.textContent = "Invitation created. No email has been sent automatically.";
    } catch { status.textContent = "The invitation outcome could not be confirmed. Do not repeat the request until its status has been reviewed."; }
    // Keep disabled after all outcomes. A lost reply must never cause a replay.
    finally { form.dataset.busy = "false"; }
  });
}
