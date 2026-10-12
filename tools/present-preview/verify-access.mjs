#!/usr/bin/env node
// Read-only, anonymous Cloudflare Access guard for the existing private 9to1 Present preview.
// Do not attach auth cookies: the whole point is to reject an accidentally public preview.
const fail = (message) => { console.error("PRIVATE_PREVIEW_GATE_FAILED: " + message); process.exitCode = 1; };
const urlText = process.env.NINETOONE_PRESENT_PREVIEW_URL;
if (!urlText) {
  fail("Set NINETOONE_PRESENT_PREVIEW_URL privately before running this gate.");
} else {
  let url;
  try { url = new URL(urlText); } catch { fail("Invalid preview URL."); }
  if (url && (url.protocol !== "https:" || !url.hostname.endsWith(".9to1.uk"))) {
    fail("Refusing a non-HTTPS or non-9to1.uk target.");
    url = undefined;
  }
  if (url) {
    try {
      const response = await fetch(url, {
        redirect: "manual",
        cache: "no-store",
        signal: AbortSignal.timeout(12000),
        headers: { "Accept": "text/html", "Cache-Control": "no-store" }
      });
      const location = response.headers.get("location") || "";
      const redirectedToAccess = [301, 302, 303, 307, 308].includes(response.status)
        && (/cloudflareaccess\.com/i.test(location) || /\/cdn-cgi\/access\//i.test(location));
      const explicitlyDenied = response.status === 401 || response.status === 403;
      if (redirectedToAccess || explicitlyDenied) {
        console.log("PRIVATE_PREVIEW_ACCESS_GATE: PASS (anonymous visitor blocked by Access)");
      } else if (response.status === 200) {
        fail("Preview returned 200 to an anonymous request: publication is unsafe.");
      } else {
        fail("Unexpected anonymous response HTTP " + response.status + ". Verify Access manually.");
      }
    } catch (error) {
      fail("Cannot prove the preview is access-protected (" + (error?.name || "network error") + ").");
    }
  }
}
