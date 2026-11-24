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
        return TryResolveCurrentSavedRange(diagnostic, text, out offset, out _, token);
    }

    /// <summary>Resolve both reported UTF-16 endpoints; never clamp a stale range or select a partial one.</summary>
    public static bool TryResolveCurrentSavedRange(CodeDiagnostic diagnostic, string text, out int offset,
        out int length, CancellationToken token = default)
    {
        offset = length = 0;
        token.ThrowIfCancellationRequested();
        if (!Resolve(diagnostic.Range.Start, out var start) || !Resolve(diagnostic.Range.End, out var end) ||
            end < start) return false;
        offset = start;
        length = end - start;
        return true;

        bool Resolve(CodePosition position, out int coordinate)
        {
            coordinate = 0;
            if (position.Line is < 0 or int.MaxValue || position.Character is < 0 or int.MaxValue) return false;
            return DeveloperTextNavigation.TryResolve(text,
                (position.Line + 1).ToString(CultureInfo.InvariantCulture) + ":" +
                (position.Character + 1).ToString(CultureInfo.InvariantCulture), out coordinate, token);
        }
    }
}
