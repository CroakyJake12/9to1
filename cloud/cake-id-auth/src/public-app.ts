export type PublicApp = "cake" | "nine";

export function readPublicApp(params: URLSearchParams): PublicApp | null {
  const app = params.get("app");
  if (app !== null) return app === "cake" || app === "nine" ? app : null;
  // The provider's signed OAuth query can identify a website during first login.
  // This only selects a fixed navigation destination; it grants no authority.
  const redirect = params.get("redirect_uri");
  return redirect === "https://cakemods.com/oauth/callback" ? "cake"
    : redirect === "https://9to1.uk/oauth/callback" ? "nine" : null;
}

export function publicAppPath(path: "/sign-in" | "/sign-up" | "/forgot-password" | "/reset-password", app: PublicApp | null): string {
  return path + (app ? `?app=${app}` : "");
}

export function publicAppDestination(app: PublicApp | null): string {
  return app === "cake" ? "https://cakemods.com/auth/sign-in"
    : app === "nine" ? "https://9to1.uk/auth/sign-in" : "/account";
}

export function googleFlowCallbacks(origin: string, search: string): { callbackURL: string; errorCallbackURL: string } {
  const original = new URLSearchParams(search);
  const app = readPublicApp(original);
  const callback = new URL(publicAppPath("/sign-in", app), origin);
  callback.searchParams.set("social", "complete");
  const error = new URL("/sign-in", origin);
  // Preserve the signed provider query for a retry. The provider client extracts
  // only its signed parameters; the status flag conveys no authentication data.
  error.search = original.toString();
  error.searchParams.set("social", "error");
  return { callbackURL: callback.href, errorCallbackURL: error.href };
}
