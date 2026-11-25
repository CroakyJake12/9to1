namespace HavenOS.Apps.Dev;

/// <summary>One file's contribution to a refactor: its document identity, the buffer version it was computed against, and the exact original text
/// that must still be present at apply time. Unopened files are represented identically — presence in the preview is not tied to being open.</summary>
public sealed record DeveloperRefactorEdit(Guid DocumentId, string RelativePath, long ExpectedVersion, string OriginalText, string ReplacementText)
{
    public DeveloperRefactorEdit Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A refactor edit requires a document identity.");
        if (string.IsNullOrWhiteSpace(RelativePath)) throw new InvalidDataException("A refactor edit requires a relative path.");
        if (ExpectedVersion < 1) throw new InvalidDataException("A refactor edit requires a positive expected version.");
        ArgumentNullException.ThrowIfNull(OriginalText); ArgumentNullException.ThrowIfNull(ReplacementText);
        return this;
    }
}

/// <summary>The current state of one document used to revalidate a refactor edit.</summary>
public sealed record DeveloperRefactorBuffer(Guid DocumentId, long Version, string Text)
{
    public DeveloperRefactorBuffer Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A refactor buffer requires a document identity.");
        if (Version < 1) throw new InvalidDataException("A refactor buffer requires a positive version.");
        ArgumentNullException.ThrowIfNull(Text);
        return this;
    }
}

/// <summary>An immutable multi-file refactor preview: the full ordered set of affected files/ranges. Prepare validates non-empty and unique document identities.</summary>
public sealed record DeveloperRefactorPreview(Guid RefactorId, string Description, IReadOnlyList<DeveloperRefactorEdit> Edits)
{
    public const int MaximumEdits = 4096;

    public DeveloperRefactorPreview Capture()
    {
        if (RefactorId == Guid.Empty) throw new InvalidDataException("A refactor preview requires an identity.");
        if (string.IsNullOrWhiteSpace(Description)) throw new InvalidDataException("A refactor preview requires a description.");
        var edits = (Edits ?? []).Select(e => e?.Capture() ?? throw new InvalidDataException("A refactor edit is missing.")).ToArray();
        if (edits.Length == 0) throw new InvalidDataException("A refactor preview must affect at least one file.");
        if (edits.Length > MaximumEdits) throw new InvalidDataException("The refactor preview exceeds the configured file limit.");
        if (edits.Select(e => e.DocumentId).Distinct().Count() != edits.Length) throw new InvalidOperationException("A refactor preview lists a document more than once.");
        return this with { Edits = Array.AsReadOnly(edits) };
    }

    public static DeveloperRefactorPreview Prepare(Guid refactorId, string description, IEnumerable<DeveloperRefactorEdit> edits)
        => new(refactorId, description, (edits ?? []).ToArray()).Capture();

    /// <summary>The set of affected relative paths for the preview UI, including files that are not currently open.</summary>
    public IReadOnlyList<string> AffectedPaths() => Capture().Edits.Select(e => e.RelativePath).ToArray();
}

/// <summary>Outcome of applying a refactor. A refusal names the first mismatched file and applies nothing (all-or-nothing).</summary>
public sealed record DeveloperRefactorApplyResult(bool Applied, Guid? MismatchedDocumentId, string? Reason, IReadOnlyDictionary<Guid, string>? RevisedTexts)
{
    public static DeveloperRefactorApplyResult Success(IReadOnlyDictionary<Guid, string> revised) => new(true, null, null, revised);
    public static DeveloperRefactorApplyResult Refused(Guid documentId, string reason) => new(false, documentId, reason, null);
}

/// <summary>Pure multi-file refactor apply. Every edit is revalidated against the current buffers (version and exact original text) before any
/// change is produced; a single mismatch aborts the whole transaction so no partial write occurs.</summary>
public static class DeveloperRefactorApply
{
    public static DeveloperRefactorApplyResult Apply(DeveloperRefactorPreview preview, IReadOnlyDictionary<Guid, DeveloperRefactorBuffer> currentBuffers)
    {
        var captured = preview.Capture();
        ArgumentNullException.ThrowIfNull(currentBuffers);

        // First pass: revalidate every edit. Do not compute any output until all are confirmed.
        foreach (var edit in captured.Edits)
        {
            if (!currentBuffers.TryGetValue(edit.DocumentId, out var buffer))
                return DeveloperRefactorApplyResult.Refused(edit.DocumentId, "A file affected by the refactor is no longer available: " + edit.RelativePath);
            var current = buffer.Capture();
            if (current.Version != edit.ExpectedVersion)
                return DeveloperRefactorApplyResult.Refused(edit.DocumentId, "The version of " + edit.RelativePath + " changed after the preview; revalidate before applying.");
            if (!string.Equals(current.Text, edit.OriginalText, StringComparison.Ordinal))
                return DeveloperRefactorApplyResult.Refused(edit.DocumentId, "The content of " + edit.RelativePath + " changed after the preview; revalidate before applying.");
        }

        // Second pass: all confirmed, produce the full revised set atomically.
        var revised = new Dictionary<Guid, string>();
        foreach (var edit in captured.Edits) revised[edit.DocumentId] = edit.ReplacementText;
        return DeveloperRefactorApplyResult.Success(revised);
    }
}
