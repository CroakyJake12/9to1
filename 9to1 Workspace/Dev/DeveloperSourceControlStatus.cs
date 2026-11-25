namespace HavenOS.Apps.Dev;

/// <summary>A file's source-control state within its bound repository. NotInRepository is explicit so a foreign path is never guessed as clean/modified.</summary>
public enum DeveloperFileSourceState { Clean, Modified, Staged, Untracked, Conflicted, NotInRepository }

/// <summary>A raw per-path status entry observed from the source-control provider for one canonical repository. The relative path is repository-relative.
/// A path outside the bound repository must not appear here; the projection reports such files as NotInRepository.</summary>
public sealed record DeveloperSourceControlEntry(string RelativePath, DeveloperFileSourceState State)
{
    public DeveloperSourceControlEntry Capture()
    {
        if (string.IsNullOrWhiteSpace(RelativePath)) throw new InvalidDataException("A source-control entry requires a repository-relative path.");
        if (RelativePath.Any(char.IsControl) || Path.IsPathRooted(RelativePath) || RelativePath.Contains('\\')) throw new InvalidDataException("A source-control entry path must be a normalized relative path.");
        if (RelativePath.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("A source-control entry path cannot contain traversal segments.");
        if (!Enum.IsDefined(State) || State == DeveloperFileSourceState.NotInRepository) throw new InvalidDataException("A source-control entry must carry an in-repository state.");
        return this;
    }
}

/// <summary>A resolved per-file status tied to the document identity and the canonical repository it belongs to.</summary>
public sealed record DeveloperFileSourceStatus(Guid DocumentId, string RelativePath, string CanonicalRepositoryId, DeveloperFileSourceState State)
{
    public bool IsConflicted => State == DeveloperFileSourceState.Conflicted;
}

/// <summary>The read-side source-control status for one bound repository. It reports per-file state only for files inside the bound repository and
/// surfaces conflicts explicitly; a file whose path is not covered by an observed entry is reported NotInRepository rather than assumed clean.</summary>
public sealed record DeveloperSourceControlStatus(string BindingId, string CanonicalRepositoryId, IReadOnlyList<DeveloperSourceControlEntry> Entries)
{
    public const int MaximumEntries = 100_000;

    public DeveloperSourceControlStatus Capture()
    {
        if (string.IsNullOrWhiteSpace(BindingId)) throw new InvalidDataException("A source-control status requires a binding identity.");
        if (string.IsNullOrWhiteSpace(CanonicalRepositoryId)) throw new InvalidDataException("A source-control status requires the canonical repository identity.");
        var entries = (Entries ?? []).Select(e => e?.Capture() ?? throw new InvalidDataException("A source-control entry is missing.")).ToArray();
        if (entries.Length > MaximumEntries) throw new InvalidDataException("The source-control status exceeds the configured entry limit.");
        if (entries.Select(e => e.RelativePath).Distinct(StringComparer.Ordinal).Count() != entries.Length) throw new InvalidOperationException("Duplicate path in a source-control status.");
        return this with { Entries = Array.AsReadOnly(entries) };
    }

    public static DeveloperSourceControlStatus From(string bindingId, string canonicalRepositoryId, IEnumerable<DeveloperSourceControlEntry> entries)
        => new(bindingId, canonicalRepositoryId, (entries ?? []).ToArray()).Capture();

    /// <summary>Resolve one document's status. A path with no observed entry is NotInRepository — never guessed.</summary>
    public DeveloperFileSourceStatus StatusFor(Guid documentId, string relativePath)
    {
        var captured = Capture();
        if (documentId == Guid.Empty) throw new InvalidDataException("A status query requires a document identity.");
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("A status query requires a path.");
        var normalized = relativePath.Replace('\\', '/');
        var entry = captured.Entries.FirstOrDefault(e => string.Equals(e.RelativePath, normalized, StringComparison.Ordinal));
        return new DeveloperFileSourceStatus(documentId, normalized, captured.CanonicalRepositoryId, entry?.State ?? DeveloperFileSourceState.NotInRepository);
    }

    /// <summary>Paths currently in conflict within the bound repository.</summary>
    public IReadOnlyList<string> ConflictedPaths() => Capture().Entries.Where(e => e.State == DeveloperFileSourceState.Conflicted).Select(e => e.RelativePath).ToArray();
}
