using Haven.Core;
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

/// <summary>Original immutable metadata-only request; descriptive copies cannot authorise IO.</summary>
public interface INotesOriginalLibraryReadRequest
{
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
public sealed record NotesOriginalLibrarySnapshot(ResourceStoreIdentity StoreIdentity,
    IReadOnlyList<NotesDocumentSummary> Documents, NotesDocument? SelectedDocument)
{
    public string? AuditWarning { get; init; }
}

/// <summary>The original read found state requiring a separately authorised recovery.
/// Read capability never repairs, quarantines, promotes or silently omits that state.</summary>
public sealed class NotesOriginalReadRecoveryRequiredException(Guid documentId, string reason, Exception? inner = null)
    : IOException(reason, inner)
{
    public Guid DocumentId { get; } = documentId;
}
