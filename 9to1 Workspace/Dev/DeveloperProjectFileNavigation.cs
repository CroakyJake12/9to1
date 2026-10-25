namespace HavenOS.Apps.Dev;

public sealed record DeveloperProjectFileMatch(string RelativePath, int Rank);
public sealed record DeveloperProjectFileNavigationResult(string Query, IReadOnlyList<DeveloperProjectFileMatch> Matches);

/// <summary>Literal filename/path navigation over an already authorised manifest. This helper
/// performs no filesystem enumeration and issues no Files identity, grant or source read.</summary>
public static class DeveloperProjectFileNavigation
{
    public const int MaximumQueryLength = 256;
    public const int MaximumFiles = 50_000;
    public static bool IsValidQuery(string query) => query.Length <= MaximumQueryLength && !string.IsNullOrWhiteSpace(query);

    public static DeveloperOperationResult<DeveloperProjectFileNavigationResult> Search(
        IEnumerable<string> capturedFilePaths, string query, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(capturedFilePaths); ArgumentNullException.ThrowIfNull(query);
        token.ThrowIfCancellationRequested();
        if (!IsValidQuery(query)) return Invalid("Enter a literal filename or path fragment up to 256 characters.");
        // Query convenience never changes the returned authored path or its case-sensitive identity.
        var needle = query.Trim().Replace('\\', '/');
        var matches = new List<DeveloperProjectFileMatch>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in capturedFilePaths)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains('\\') ||
                path.Split('/').Any(part => part is "" or "." or "..") || !seen.Add(path))
                return Invalid("Some file paths are invalid or duplicated. Refresh the file list.");
            if (seen.Count > MaximumFiles) return Invalid("File search supports up to 50,000 files. This project is too large for this search.");
            var name = path[(path.LastIndexOf('/') + 1)..];
            var rank = name.Equals(needle, StringComparison.OrdinalIgnoreCase) ? 0
                : name.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 1
                : name.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 2
                : path.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 3
                : path.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 4 : -1;
            if (rank >= 0) matches.Add(new(path, rank));
        }
        token.ThrowIfCancellationRequested();
        var ordered = matches.OrderBy(value => value.Rank).ThenBy(value => value.RelativePath, StringComparer.Ordinal).ToArray();
        token.ThrowIfCancellationRequested();
        return DeveloperOperationResult<DeveloperProjectFileNavigationResult>.Success(new(query, Array.AsReadOnly(ordered)));
    }

    private static DeveloperOperationResult<DeveloperProjectFileNavigationResult> Invalid(string message) =>
        DeveloperOperationResult<DeveloperProjectFileNavigationResult>.Failure(DeveloperOperationErrorCode.InvalidInput, message, "project-file-navigation");
}
