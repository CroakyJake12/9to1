using System.Globalization;
using Haven.Core;

namespace HavenOS.Apps.Dev;

/// <summary>Checks a reported build location before the normal authorized source Open.
/// A diagnostic is a location hint, never Files identity, read permission or source-version proof.</summary>
public static class DeveloperBuildProblemNavigation
{
    public static bool TryGetRelativePath(CodeDiagnostic diagnostic, out string relativePath)
    {
        relativePath = "";
        var path = diagnostic.RelativePath;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl) || Path.IsPathRooted(path) ||
            path.StartsWith('/') || path.StartsWith('\\') || path.Length > 1 && path[1] == ':' ||
            !OperatingSystem.IsWindows() && path.Contains('\\')) return false;
        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(component => component is "" or "." or "..")) return false;
        relativePath = normalized; return true;
    }

    public static bool TryResolveCurrentSavedText(CodeDiagnostic diagnostic, string text, out int offset,
        CancellationToken token = default)
    {
        offset = 0; var start = diagnostic.Range.Start;
        if (start.Line < 0 || start.Character < 0 || start.Line == int.MaxValue || start.Character == int.MaxValue) return false;
        return DeveloperTextNavigation.TryResolve(text,
            ((long)start.Line + 1).ToString(CultureInfo.InvariantCulture) + ":" + ((long)start.Character + 1).ToString(CultureInfo.InvariantCulture),
            out offset, token);
    }
}
