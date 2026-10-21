namespace HavenOS.Apps.Browse;

/// <summary>
/// Locates a genuinely installed, executable Chromium on Linux. Availability is
/// truthful: a missing or non-executable binary reports unsupported. Detection is
/// overridable so policy tests can exercise refusal without launching a browser.
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

    /// <summary>Returns the first installed executable Chromium path, or null.</summary>
    public virtual string? Find()
    {
        if (!OperatingSystem.IsLinux()) return null;
        foreach (var candidate in Candidates)
        {
            if (File.Exists(candidate) && IsExecutable(candidate)) return candidate;
        }
        return null;
    }
}
