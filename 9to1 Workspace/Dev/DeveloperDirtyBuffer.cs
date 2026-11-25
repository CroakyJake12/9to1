namespace HavenOS.Apps.Dev;

/// <summary>Line-ending convention retained by a dirty buffer independently of the saved disk bytes.</summary>
public enum DeveloperLineEnding { Lf, CrLf, Cr }

/// <summary>A selection range within the draft, retained across edits and saves without affecting the saved revision.</summary>
public sealed record DeveloperBufferSelection(int Anchor, int Caret)
{
    public DeveloperBufferSelection Capture()
    {
        if (Anchor < 0 || Caret < 0) throw new InvalidDataException("A buffer selection must be non-negative.");
        return this;
    }
}

/// <summary>A unified dirty-buffer identity: the document, the saved disk revision it diverged from, the current draft text, and the presentation
/// state (encoding, line endings, selection) that must survive edits and saves separately from the saved disk revision. Edits never mutate the
/// saved revision; MarkSaved advances it and clears dirtiness while preserving encoding/line-endings/selection.</summary>
public sealed record DeveloperDirtyBuffer(Guid DocumentId, long SavedDiskRevision, string DraftText, string SavedSnapshotText,
    string EncodingId, DeveloperLineEnding LineEnding, DeveloperBufferSelection Selection)
{
    public const int MaximumTextCharacters = 8_000_000;

    public DeveloperDirtyBuffer Capture()
    {
        if (DocumentId == Guid.Empty) throw new InvalidDataException("A dirty buffer requires a document identity.");
        if (SavedDiskRevision < 1) throw new InvalidDataException("A dirty buffer requires a positive saved disk revision.");
        ArgumentNullException.ThrowIfNull(DraftText); ArgumentNullException.ThrowIfNull(SavedSnapshotText);
        if (DraftText.Length > MaximumTextCharacters || SavedSnapshotText.Length > MaximumTextCharacters) throw new InvalidDataException("The buffer text exceeds the editor limit.");
        if (string.IsNullOrWhiteSpace(EncodingId)) throw new InvalidDataException("A dirty buffer requires an encoding identifier.");
        if (!Enum.IsDefined(LineEnding)) throw new InvalidDataException("The buffer line-ending kind is unsupported.");
        Selection.Capture();
        return this;
    }

    /// <summary>Open a buffer from saved disk content. It starts clean (draft equals the saved snapshot).</summary>
    public static DeveloperDirtyBuffer Open(Guid documentId, long savedDiskRevision, string text, string encodingId, DeveloperLineEnding lineEnding)
        => new(documentId, savedDiskRevision, text, text, encodingId, lineEnding, new DeveloperBufferSelection(0, 0)).Capture();

    /// <summary>Whether the draft differs from the last-saved snapshot. This is independent of the saved disk revision.</summary>
    public bool IsDirty => !StringComparer.Ordinal.Equals(DraftText, SavedSnapshotText);

    /// <summary>Edit the draft text. The saved disk revision and saved snapshot are untouched, so dirtiness is derived from content only.</summary>
    public DeveloperDirtyBuffer Edit(string revisedText)
    {
        var captured = Capture();
        ArgumentNullException.ThrowIfNull(revisedText);
        if (revisedText.Length > MaximumTextCharacters) throw new InvalidDataException("The edited buffer text exceeds the editor limit.");
        return captured with { DraftText = revisedText }.Capture();
    }

    /// <summary>Update the retained selection without affecting dirtiness or the saved revision.</summary>
    public DeveloperDirtyBuffer SetSelection(int anchor, int caret)
        => Capture() with { Selection = new DeveloperBufferSelection(anchor, caret) }.Capture();

    /// <summary>Change the presentation encoding/line-ending independently of text and the saved revision.</summary>
    public DeveloperDirtyBuffer SetPresentation(string encodingId, DeveloperLineEnding lineEnding)
        => Capture() with { EncodingId = encodingId, LineEnding = lineEnding }.Capture();

    /// <summary>Persist to disk: advance the saved revision to newSavedRevision, adopt the current draft as the clean snapshot, and preserve
/// encoding/line-endings/selection. The buffer becomes clean.</summary>
    public DeveloperDirtyBuffer MarkSaved(long newSavedRevision)
    {
        var captured = Capture();
        if (newSavedRevision < captured.SavedDiskRevision) throw new InvalidOperationException("The saved disk revision cannot move backwards.");
        return captured with { SavedDiskRevision = newSavedRevision, SavedSnapshotText = captured.DraftText }.Capture();
    }
}
