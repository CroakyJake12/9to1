type TabKey = "sign-in" | "sign-up" | "forgot-password";

const tabKeys: TabKey[] = ["sign-in", "sign-up", "forgot-password"];

function keyForPath(pathname: string): TabKey | null {
  return tabKeys.find(key => pathname === `/${key}`) ?? null;
}

/** This remembers notice dismissal for this tab only, not consent to tracking. */
export function installCookieNotice(): void {
  const notice = document.getElementById("cookie-notice");
  const dismiss = document.getElementById("cookie-notice-dismiss");
  const reopen = document.getElementById("cookie-notice-open");
  if (!notice || !dismiss || !reopen) return;
  const storageKey = "nine-account-cookie-notice-v1";
  let returnFocus: HTMLElement | null = null;
  const show = (visible: boolean) => {
    notice.hidden = !visible;
    document.body.classList.toggle("has-cookie-notice", visible);
  };
  try { show(sessionStorage.getItem(storageKey) !== "dismissed"); }
  catch { show(true); /* Blocked browser storage keeps the informational notice visible. */ }
  dismiss.addEventListener("click", () => {
    show(false);
    try { sessionStorage.setItem(storageKey, "dismissed"); }
    catch { /* Dismissal still works for this document when session storage is blocked. */ }
    const focus = returnFocus ?? document.getElementById("account-title");
    if (focus) { if (!returnFocus) focus.tabIndex = -1; focus.focus(); }
  });
  reopen.addEventListener("click", () => {
    returnFocus = reopen;
    show(true);
    dismiss.focus();
  });
}

export function installOnboardingFormState(): void {
  for (const [formId, refusalId] of [["public-onboarding-form", "age-ineligible"], ["sign-up-form", "signup-age-ineligible"]]) {
    const form = document.getElementById(formId) as HTMLFormElement | null;
    const refusal = document.getElementById(refusalId);
    if (!form || !refusal) continue;
    const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]');
    const update = () => {
      const ageBand = form.querySelector<HTMLInputElement>('input[name="ageBand"]:checked')?.value;
      const ineligible = ageBand === "under_13";
      refusal.hidden = !ineligible;
      if (submit) submit.disabled = ineligible || form.dataset.busy === "true" || form.dataset.policyUnavailable === "true";
    };
    form.addEventListener("change", update);
    update();
  }
}

/** Local navigation only. Keep the provider's signed OAuth query byte-for-byte. */
export function installAuthTabs(): void {
  installCookieNotice();
  installOnboardingFormState();
  const tablist = document.getElementById("auth-tabs");
  if (!tablist) return;
  const tabs = tabKeys.map(key => document.getElementById(`tab-${key}`) as HTMLAnchorElement | null);
  const panels = tabKeys.map(key => document.getElementById(`panel-${key}`));
  if (tabs.some(tab => !tab) || panels.some(panel => !panel)) return;
  const heading = document.getElementById("account-title");
  const description = document.getElementById("account-description");
  const google = document.getElementById("google-auth");
  const status = document.querySelector<HTMLElement>("[role=status]");

  const syncLinks = () => {
    for (const tab of tabs) {
      const key = tab!.dataset.authTab as TabKey;
      tab!.href = `/${key}${location.search}`;
    }
    document.querySelectorAll<HTMLAnchorElement>("[data-auth-link]").forEach(link => {
      const key = link.dataset.authLink as TabKey;
      if (tabKeys.includes(key)) link.href = `/${key}${location.search}`;
    });
  };

  const activate = (key: TabKey, updateHistory: boolean, focusTab: boolean) => {
    const selected = tabKeys.indexOf(key);
    if (selected < 0) return;
    // A response should stay attached to the form the visitor submitted.
    if (updateHistory && (document.querySelector('form[data-busy="true"]') ||
        (document.getElementById("google-sign-in") as HTMLButtonElement | null)?.disabled)) return;
    if (updateHistory && location.pathname !== `/${key}`) {
      // Do not rebuild URLSearchParams: re-encoding invalidates signed OAuth data.
      history.pushState(null, "", `/${key}${location.search}`);
    }
    tabs.forEach((tab, index) => {
      tab!.setAttribute("aria-selected", String(index === selected));
      tab!.tabIndex = index === selected ? 0 : -1;
      panels[index]!.hidden = index !== selected;
      panels[index]!.classList.remove("is-entering");
    });
    const panel = panels[selected]!;
    // Restart the entrance after an actual tab change, including browser Back.
    void panel.offsetWidth;
    panel.classList.add("is-entering");
    tablist.style.setProperty("--active-tab", String(selected));
    if (heading) heading.textContent = tabs[selected]!.dataset.title ?? "9to1 Account";
    if (description) description.textContent = tabs[selected]!.dataset.description ?? "";
    document.title = `${tabs[selected]!.textContent?.trim() ?? "Account"} · 9to1 Account`;
    if (google) google.hidden = key !== "sign-in";
    if (status) status.textContent = "";
    syncLinks();
    if (focusTab) tabs[selected]!.focus();
    window.dispatchEvent(new CustomEvent("nine-auth-tab-change", { detail: key }));
  };

  tabs.forEach((tab, index) => {
    tab!.addEventListener("click", event => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
      event.preventDefault();
      activate(tabKeys[index], true, false);
    });
    tab!.addEventListener("keydown", event => {
      let next = index;
      if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
      else if (event.key === "ArrowLeft") next = (index + tabs.length - 1) % tabs.length;
      else if (event.key === "Home") next = 0;
      else if (event.key === "End") next = tabs.length - 1;
      else if (event.key === " ") {
        event.preventDefault();
        activate(tabKeys[index], true, false);
        return;
      } else return;
      event.preventDefault();
      activate(tabKeys[next], true, true);
    });
  });
  document.querySelectorAll<HTMLAnchorElement>("[data-auth-link]").forEach(link => {
    link.addEventListener("click", event => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
      const key = link.dataset.authLink as TabKey;
      if (!tabKeys.includes(key)) return;
      event.preventDefault();
      activate(key, true, true);
    });
  });
  window.addEventListener("popstate", () => {
    const key = keyForPath(location.pathname);
    if (key) activate(key, false, tablist.contains(document.activeElement));
  });
  const initial = keyForPath(location.pathname);
  if (initial) activate(initial, false, false);
}
