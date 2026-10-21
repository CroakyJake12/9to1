using System.Text;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Detects a Chromium candidate on Linux. Detection is truthfully limited to
/// presence/classification: a missing or non-executable path reports unsupported,
/// and a Snap transitional launcher (Ubuntu's /usr/bin/chromium-browser ->
/// chromium snap) is never promoted to a ready browser just because the wrapper
/// file exists. A detected non-Snap candidate is NOT proof of a launchable,
/// validated runtime (DevTools/profile/render readiness requires runtime
/// validation). Detection is overridable so policy tests can exercise refusal
/// without launching a browser.
/// </summary>
public class ChromiumLocator
{
    public static readonly ChromiumLocator Instance = new();

    // Only the authorized installed launch paths are considered. Anything else is
    // refused, so the engine never silently selects an unapproved binary.
    private static readonly string[] DefaultCandidates =
    [
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser"
    ];

    // On Ubuntu 24.04+/26 the "chromium-browser" package is a *transitional* Snap
    // launcher: a small shell script that requires the "chromium" snap (installed
    // as /snap/bin/chromium) and execs it. A launcher alone is not a ready browser.
    private const string SnapShimMarker = "/snap/bin/";
    private const string ChromiumSnapMount = "/snap/chromium/current";

    /// <summary>Candidate binary paths, in preference order.</summary>
    public virtual IReadOnlyList<string> Candidates => DefaultCandidates;

    /// <summary>True when the file is executable by at least one principal. The
    /// platform guard lives on the actual call so the analyzer (CA1416) sees the
    /// Windows-unreachable branch is guarded before File.GetUnixFileMode runs.</summary>
    public virtual bool IsExecutable(string path)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & exec) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>What a candidate path actually is, independent of any snap state.</summary>
    public enum BrowserSourceKind
    {
        /// <summary>Not present, not executable, or not a valid browser source.</summary>
        None,

        /// <summary>A non-Snap candidate path (a native binary or a distribution shell
        /// wrapper). Presence/detection only; readiness requires runtime validation.</summary>
        NativeExecutable,

        /// <summary>A Snap transitional launcher that delegates to a Snap rather than
        /// carrying a real browser binary.</summary>
        SnapLauncher
    }

    /// <summary>Classifies a candidate path without assuming it is ready.</summary>
    public virtual BrowserSourceKind DescribeKind(string path)
    {
        if (!OperatingSystem.IsLinux()) return BrowserSourceKind.None;
        if (string.IsNullOrEmpty(path)) return BrowserSourceKind.None;
        if (!File.Exists(path) || !IsExecutable(path)) return BrowserSourceKind.None;
        return LooksLikeSnapLauncher(path) ? BrowserSourceKind.SnapLauncher : BrowserSourceKind.NativeExecutable;
    }

    /// <summary>True when the installed "chromium" Snap mount is present. This is a
    /// diagnostic signal only; it does not by itself prove that headless DevTools
    /// with an isolated --user-data-dir profile works under Snap confinement (that
    /// requires a runtime validation on the actual owner machine).</summary>
    public virtual bool SnapPresent() => Directory.Exists(ChromiumSnapMount);

    /// <summary>Returns the first detected non-Snap Chromium candidate path, or null.
    /// This is a presence/detection result, not proof of a validated runtime. A Snap
    /// transitional launcher is deliberately never returned: it is not enabled/
    /// selected by this build, and confined headless/profile interoperability is not
    /// established source-only. Use <see cref="DescribeKind"/> and
    /// <see cref="SnapPresent"/> for truthful diagnostics.</summary>
    public virtual string? Find()
    {
        if (!OperatingSystem.IsLinux()) return null;
        foreach (var candidate in Candidates)
        {
            if (DescribeKind(candidate) == BrowserSourceKind.NativeExecutable) return candidate;
        }
        return null;
    }

    /// <summary>Detects the Ubuntu Snap transitional launcher pattern: a tiny shell
    /// script (or a symlink that resolves to one) which delegates to a
    /// /snap/bin/{browser} shim rather than carrying a real browser binary. Real
    /// browser binaries are large; only small text files are inspected, so a native
    /// binary is never misread.</summary>
    private static bool LooksLikeSnapLauncher(string path)
    {
        try
        {
            var link = new FileInfo(path).LinkTarget;
            if (link is not null && link.Contains(SnapShimMarker, StringComparison.Ordinal)) return true;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > 64 * 1024) return false; // a real browser binary, not a shim
            Span<byte> buffer = stackalloc byte[2048];
            int read = fs.Read(buffer);
            var text = Encoding.ASCII.GetString(buffer[..read]);
            return text.Contains(SnapShimMarker, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
