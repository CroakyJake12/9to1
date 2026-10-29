namespace HavenOS.Apps.Browse;

/// <summary>
/// Builds the installed-Chromium command line for one owned process. The process
/// binds a DevTools endpoint on a random loopback-only port (port 0) so nothing
/// is exposed beyond the machine. The Chromium sandbox is deliberately preserved:
/// no --no-sandbox, --disable-web-security or remote-port flags are ever added.
/// </summary>
public static class ChromiumArguments
{
    public const string LoopbackAddress = "127.0.0.1";
    private const string RemoteDebuggingPort = "0";
    internal const int InitialWidth = 1280;
    internal const int InitialHeight = 800;

    /// <summary>Builds the argument list for the given engine-isolated profile. The binary path is
    /// accepted so the engine can be described honestly rather than assumed; when it is absent the
    /// engine's own identity is left untouched.</summary>
    public static IReadOnlyList<string> Build(string profileDirectory, string? binaryPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (!Path.IsPathFullyQualified(profileDirectory)) throw new ArgumentException("The profile directory must be absolute.", nameof(profileDirectory));
        var arguments = new List<string>
        {
            // Headless stays: Haven composites the renderer into its own surface instead of letting
            // Chromium own a top-level window. Removing it would replace the embedded tab with a
            // separate browser window and break the shared Browse surface.
            "--headless=new",
            // Owner report 29 October: every site refused - Cloudflare's "You've been blocked by
            // network security", reCAPTCHA loops on google.com, x.com refusing the connection. That is
            // not the network and not a certificate fault: default headless Chromium announces itself,
            // both through Blink's automation-controlled state and through a user agent that literally
            // contains "HeadlessChrome", and public site protection keys bot traffic off exactly those
            // signals. Dropping the automation flag is the first honest correction; the second is below.
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-component-update",
            "--remote-debugging-address=" + LoopbackAddress,
            "--remote-debugging-port=" + RemoteDebuggingPort,
            // Give the engine the same starting geometry the app already treats as its logical default
            // (ChromiumEngineSurface.RenderWidth/RenderHeight, ChromiumViewport.Default). Without it
            // headless starts at Chromium's own default window, so the frames that arrive before the
            // real viewport override lands are a different shape from the tab and get letterboxed -
            // the black bars the owner saw around the page on 29 October. The override still decides
            // every real frame; this only removes the wrong-shaped first frames.
            "--window-size=" + InitialWidth + "," + InitialHeight,
            "--user-data-dir=" + profileDirectory,
        };
        if (UserAgentFor(binaryPath) is { } agent) arguments.Add("--user-agent=" + agent);
        arguments.Add("about:blank");
        return arguments;
    }

    /// <summary>Describes the engine this build actually ships, so the identity cannot drift away from
    /// the binary: the version comes from the located runtime, never a constant. Returns null when the
    /// version cannot be determined, which keeps Chromium's own agent rather than inventing one. Only
    /// the "Headless" marker is removed and the platform is stated truthfully; this makes an embedded
    /// renderer readable as ordinary Chrome, and it does not claim to defeat every bot signal - the
    /// client-hint and TLS fingerprints are unchanged and remain open work.</summary>
    public static string? UserAgentFor(string? binaryPath)
    {
        if (string.IsNullOrWhiteSpace(binaryPath)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(binaryPath, @"chromium-(\d+)\.\d+\.\d+\.\d+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" +
            match.Groups[1].Value + ".0.0.0 Safari/537.36";
    }
}
