namespace HavenOS.Apps.Dev;

public sealed record DeveloperNewSourceFile(string RelativePath, string TemplateId, string Text)
{
    /// <summary>Pure generated preview only. This value creates no source file, Files identity or
    /// permission. The existing absent-target CREATE owner alone publishes reviewed bytes.</summary>
    public static DeveloperOperationResult<DeveloperNewSourceFile> Prepare(string name, bool cuiPage)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name != name.Trim() || name is "." or ".." ||
            name.EndsWith('.') || name.Any(character => char.IsControl(character) || character is '/' or '\\' or ':'))
            return DeveloperOperationResult<DeveloperNewSourceFile>.Failure(DeveloperOperationErrorCode.InvalidInput,
                "Enter one direct project-root file name (up to 120 characters). Nested paths and new directories are not supported by this CREATE owner.", "new-source-file");
        if (cuiPage && !Path.GetExtension(name).Equals(".cui", StringComparison.OrdinalIgnoreCase))
            return DeveloperOperationResult<DeveloperNewSourceFile>.Failure(DeveloperOperationErrorCode.InvalidInput,
                "The CUI page template requires a .cui file name. Choose an empty source file for other source types.", "new-source-file");
        return DeveloperOperationResult<DeveloperNewSourceFile>.Success(new(name, cuiPage ? "cui-page" : "empty-source", cuiPage
            ? "<Cui version=\"1\">\n  <Page id=\"page\" accessible-name=\"New CUI page\">\n    <Container id=\"content\" Type=\"Vertical Stack\" margin=\"16\" CellGap=\"12\">\n      <Text id=\"title\" text=\"New CUI page\" />\n    </Container>\n  </Page>\n</Cui>\n"
            : ""));
    }
}

/// <summary>Observed successful native publication. This contains no invented Files identity,
/// source grant or execution receipt; the returned hash comes from the actual CREATE owner.</summary>
public sealed record DeveloperSourceFilePublication(DeveloperProjectReference Project, string RelativePath,
    string AfterSha256, bool ColdReadVerified);
