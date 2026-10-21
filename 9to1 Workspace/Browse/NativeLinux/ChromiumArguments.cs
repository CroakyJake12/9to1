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

    /// <summary>Builds the argument list for the given engine-isolated profile.</summary>
    public static IReadOnlyList<string> Build(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (!Path.IsPathFullyQualified(profileDirectory)) throw new ArgumentException("The profile directory must be absolute.", nameof(profileDirectory));
        return
        [
            "--headless=new",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-component-update",
            "--remote-debugging-address=" + LoopbackAddress,
            "--remote-debugging-port=" + RemoteDebuggingPort,
            "--user-data-dir=" + profileDirectory,
            "about:blank"
        ];
    }
}
