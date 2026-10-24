using Haven.Core;
using System.Globalization;
using System.Text.Json;

namespace Haven.Application;

/// <summary>Metadata observation from the actual Notes repository. It grants no document read,
/// Home ownership, account scope, mutation, approval or Ready state.</summary>
public interface INotesOriginalStoreObservation
{
    ResourceStoreIdentity StoreIdentity { get; }
    string Revision { get; }
    bool IsEmpty { get; }
}

/// <summary>The canonical repository owns its UUID and original physical observation.
/// Initialisation writes identity metadata only; existing content requires the existing
/// explicit Home ownership import. Never derive this identity from a mutable pathname.</summary>
public interface INotesOriginalStoreSource : IResourceStoreIdentitySource
{
    ValueTask<INotesOriginalLibraryReadRequest> PrepareOriginalLibraryReadAsync(Guid? documentId,
        CancellationToken cancellationToken = default);
    bool IsIssuedOriginalLibraryReadRequest(INotesOriginalLibraryReadRequest request);
    bool IsIssuedOriginalLibraryReadOutcome(INotesOriginalLibraryReadOutcome outcome, INotesOriginalLibraryReadRequest request);
    bool IsIssuedOriginalSaveOutcome(INotesOriginalSaveOutcome outcome, INotesOriginalSaveIntent intent);
    ValueTask<INotesOriginalSaveIntent> PrepareOriginalSaveAsync(NotesDocument document, string reason,
        CancellationToken cancellationToken = default);
    bool IsIssuedOriginalSaveIntent(INotesOriginalSaveIntent intent);
    ValueTask<ResourceStoreIdentity> InitialiseOriginalStoreIdentityAsync(CancellationToken cancellationToken = default);
    ValueTask<INotesOriginalStoreObservation> ObserveOriginalStoreAsync(CancellationToken cancellationToken = default);
    bool IsIssuedOriginalStoreObservation(INotesOriginalStoreObservation observation);
    ValueTask<bool> IsOriginalStoreObservationCurrentAsync(INotesOriginalStoreObservation observation,
        CancellationToken cancellationToken = default);
}

public enum NotesOriginalStoreSetupState { MissingIdentity, Available, Unsupported, Inaccessible, RecoveryRequired }

/// <summary>Owner-issued metadata only. Missing and unavailable never grant initialisation,
/// ownership, import, content access or an application-ready observation.</summary>
public interface INotesOriginalStoreSetupInspection
{
    NotesOriginalStoreSetupState State { get; }
    INotesOriginalStoreObservation? Observation { get; }
    string Code { get; }
    string Message { get; }
}

/// <summary>The SAME Notes metadata producer with nested original callback/task custody.
/// These contracts expose no alternative persistence engine or application workflow.</summary>
public interface INotesOriginalScopedStoreSource : INotesOriginalStoreSource
{
    bool IsIssuedOriginalSetupInspection(INotesOriginalStoreSetupInspection inspection);
    ValueTask<INotesOriginalStoreSetupInspection> InspectOriginalStoreSetupWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default);
    ValueTask<ResourceStoreIdentity> InitialiseOriginalStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default);
    ValueTask<INotesOriginalStoreObservation> ObserveOriginalStoreWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default);
    ValueTask<bool> IsOriginalStoreObservationCurrentWithinOriginalSourceAsync(INotesOriginalStoreObservation observation,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default);
}

/// <summary>Descriptive view of an immutable candidate issued by the original repository.
/// Copying these fields never creates an owner intent. Preparation grants no Home consent,
/// store ownership, content read, write execution or native Ready state.</summary>
public interface INotesOriginalSaveIntent
{
    Guid OperationId { get; }
    Guid DocumentId { get; }
    long ExpectedVersion { get; }
    string ContentSha256 { get; }
    string Reason { get; }
    INotesOriginalStoreObservation StoreObservation { get; }
    JsonElement Arguments { get; }
}

public enum NotesOriginalSaveDisposition { Committed, Refused, Uncertain }

/// <summary>Observation issued only by the actual repository after its owning save settles.
/// It authorises an exact terminal Home audit, never another mutation.</summary>
public interface INotesOriginalSaveOutcome
{
    INotesOriginalSaveIntent Intent { get; }
    NotesOriginalSaveDisposition Disposition { get; }
    long? CommittedVersion { get; }
    string? CommittedSha256 { get; }
    string DiagnosticCode { get; }
}

public enum NotesOriginalReadKind { LibrarySnapshot, VersionHistory, Version }

/// <summary>The existing Notes version-store leaf identity, never a path or a grant.</summary>
public static class NotesOriginalVersionIdentity
{
    public static bool IsVersionId(string? value)
    {
        if (value is null || value.Length is < 30 or > 39 || value[0] != 'v') return false;
        var dash = value.IndexOf('-', 1);
        if (dash is < 11 or > 20 || !long.TryParse(value.AsSpan(1, dash - 1), NumberStyles.None,
            CultureInfo.InvariantCulture, out var revision) || revision <= 0 ||
            !DateTime.TryParseExact(value[(dash + 1)..], "yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp)) return false;
        return value == "v" + revision.ToString("D10", CultureInfo.InvariantCulture) + "-" +
            timestamp.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
    }
}

/// <summary>Original immutable read request; descriptive copies cannot authorise IO.
/// History and saved-version reads are explicitly narrower tuples, never current-library grants.</summary>
public interface INotesOriginalLibraryReadRequest
{
    NotesOriginalReadKind Kind => NotesOriginalReadKind.LibrarySnapshot;
    string? VersionId => null;
    Guid OperationId { get; }
    Guid? DocumentId { get; }
    INotesOriginalStoreObservation StoreObservation { get; }
    JsonElement Arguments { get; }
}

/// <summary>Actual owner read observation, usable only for its original Home terminal audit.</summary>
public interface INotesOriginalLibraryReadOutcome
{
    INotesOriginalLibraryReadRequest Request { get; }
    bool Succeeded { get; }
    int DocumentCount { get; }
    bool DocumentWasRead { get; }
    string? SnapshotSha256 { get; }
    string DiagnosticCode { get; }
}

/// <summary>Authorised data, not an owner admission. Only summaries and the explicitly requested
/// document are retained, never every library document body.</summary>
public sealed record NotesOriginalDocumentMetadata(NotesDocumentSummary Summary, string? Product, bool IsDeleted);

public sealed record NotesOriginalLibrarySnapshot(ResourceStoreIdentity StoreIdentity,
    IReadOnlyList<NotesDocumentSummary> Documents, NotesDocument? SelectedDocument)
{
    public IReadOnlyList<NotesOriginalDocumentMetadata> Metadata { get; init; } = [];
    public IReadOnlyList<NotesVersionInfo> Versions { get; init; } = [];
    public string? AuditWarning { get; init; }
}

/// <summary>The original read found state requiring a separately authorised recovery.
/// Read capability never repairs, quarantines, promotes or silently omits that state.</summary>
public sealed class NotesOriginalReadRecoveryRequiredException(Guid documentId, string reason, Exception? inner = null)
    : IOException(reason, inner)
{
    public Guid DocumentId { get; } = documentId;
}
