using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Navigation admission for the installed-Chromium web backend. It composes the
/// canonical <see cref="BrowserNativeRequestPolicy"/> and additionally forbids any
/// internal/privileged route (file, 9to1, browse, cui) from ever reaching the web
/// renderer. about:blank remains allowed only as the internal bootstrap target.
/// </summary>
public static class ChromiumNavigationPolicy
{
    public static BrowserTopLevelAssessment Assess(Uri? address)
    {
        // A missing address is left to the canonical policy (which denies it).
        if (address is null) return BrowserNativeRequestPolicy.AssessTopLevel(null);

        // Relative routes must never reach the web renderer. Deny them up front
        // with the specific Chromium backend diagnostic BEFORE touching Scheme:
        // System.Uri represents digit-leading/relative raw internal-route inputs as
        // relative URIs, so reading .Scheme on them is unsafe and would throw.
        if (!address.IsAbsoluteUri)
            return BrowserTopLevelAssessment.Deny("Internal or privileged routes cannot be opened through the Chromium web backend.");

        // Internal/privileged absolute schemes are rejected with a specific
        // diagnostic before the canonical policy runs, so a file/9to1/browse/cui
        // route can never reach the web renderer (and about:blank bootstrap remains
        // allowed).
        if (address.Scheme is "file" or "9to1" or "browse" or "cui")
            return BrowserTopLevelAssessment.Deny("Internal or privileged routes cannot be opened through the Chromium web backend.");

        return BrowserNativeRequestPolicy.AssessTopLevel(address);
    }
}
