type TurnstileApi = {
  render(container: HTMLElement, options: Record<string, unknown>): string;
  reset(widget: string): void;
  remove(widget: string): void;
};
declare global { interface Window { turnstile?: TurnstileApi; onNineTurnstileLoad?: () => void } }
const forms = ["sign-in", "sign-up", "forgot-password"] as const;
type FormKey = typeof forms[number];
type Widget = { id?: string; token: string; generation: number; size?: "compact" | "flexible" };
export class VerificationError extends Error {}

export function installTurnstile(): { take(formId: string): string; reset(formId: string): void } {
  const widgets = new Map<FormKey, Widget>();
  const keyForForm = (id: string) => forms.find(key => id === `${key}-form`);
  const containerFor = (key: FormKey) => document.getElementById(`turnstile-${key}`) as HTMLElement | null;
  const active = (key: FormKey) => location.pathname === `/${key}` && !document.getElementById(`panel-${key}`)?.hidden;
  const remove = (key: FormKey) => {
    const widget = widgets.get(key); if (!widget) return;
    widget.token = ""; widget.generation++;
    if (widget.id !== undefined) {
      try { window.turnstile?.remove(widget.id); } catch { /* The server still rejects missing verification. */ }
      widget.id = undefined;
    }
  };
  const renderActive = () => {
    for (const key of forms) {
      const container = containerFor(key);
      if (!active(key)) { remove(key); continue; }
      if (!container || container.dataset.turnstileRequired === "false" || !container.dataset.sitekey || !window.turnstile) continue;
      const widget = widgets.get(key) ?? { token: "", generation: 0 };
      widgets.set(key, widget);
      // Flexible widgets have a 300px minimum. Measure layout width (not an
      // animated transform) so narrow forms use the 150px compact variant.
      const size = container.clientWidth < 300 ? "compact" : "flexible";
      if (widget.id !== undefined && widget.size === size) continue;
      if (widget.id !== undefined) remove(key);
      const generation = ++widget.generation;
      const clear = () => { if (widget.generation === generation) widget.token = ""; };
      try {
        widget.size = size;
        widget.id = window.turnstile.render(container, {
          sitekey: container.dataset.sitekey, action: container.dataset.turnstileAction,
          theme: "light", size, "response-field": false,
          callback: (token: unknown) => { if (widget.generation === generation && typeof token === "string") widget.token = token; },
          "expired-callback": clear, "timeout-callback": clear, "error-callback": () => { clear(); return true; },
        });
      } catch { clear(); }
    }
  };
  if (typeof window !== "undefined") {
    window.onNineTurnstileLoad = renderActive;
    window.addEventListener("nine-auth-tab-change", renderActive);
    window.addEventListener("resize", renderActive);
    if (typeof window.ResizeObserver === "function") {
      const observer = new window.ResizeObserver(renderActive);
      for (const key of forms) {
        const container = containerFor(key); if (container) observer.observe(container);
      }
    }
    renderActive();
  }
  return {
    take(formId) {
      const key = keyForForm(formId); if (!key) return "";
      const container = containerFor(key);
      if (container?.dataset.turnstileRequired === "false") return "";
      if (!container?.dataset.sitekey) throw new VerificationError("Verification is temporarily unavailable. Please try again later.");
      const widget = widgets.get(key);
      if (!widget?.token) throw new VerificationError("Complete the verification before continuing.");
      const token = widget.token; widget.token = ""; return token;
    },
    reset(formId) {
      const key = keyForForm(formId); if (!key) return;
      const widget = widgets.get(key); if (!widget) return;
      widget.token = "";
      if (widget.id !== undefined) {
        try { window.turnstile?.reset(widget.id); } catch { remove(key); }
      }
    },
  };
}
