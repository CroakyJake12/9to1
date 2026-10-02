namespace Haven.Application;

/// <summary>The original local editor revision no longer matches the actual canonical Notes file.</summary>
public sealed class NotesRevisionConflictException(Guid documentId, long expectedVersion, long actualVersion)
    : InvalidOperationException($"This document changed in another editor (expected v{expectedVersion}, current v{actualVersion}). Reopen it before saving.")
{
    public Guid DocumentId { get; } = documentId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}
