using Haven.Core;

namespace Haven.Application;

/// <summary>Canonical Files observations only. The issuing selection, current Home
/// READ, and original content lease independently authorize use of these fields.</summary>
public sealed record CanonicalAttachmentFileIdentity(Guid StoreId, Guid FileId, Guid RevisionId,
    long SizeBytes, string Sha256, string OriginalName);

/// <summary>Implemented privately by the configured Files selection owner. A copied
/// identity, selected path or another implementation is never an issued selection.</summary>
public interface ICanonicalAttachmentOriginalSelection
{
    AuthenticatedResourceActor OriginalActor { get; }
    CanonicalAttachmentFileIdentity OriginalFile { get; }
    string OriginalMaterializationPath { get; }
}

/// <summary>A selection locates an already registered canonical Files revision.
/// Resolving a platform-picked path observes metadata only; it neither registers an
/// arbitrary external file nor grants content READ or conversation import WRITE.</summary>
public interface ICanonicalAttachmentOriginalSelectionSource
{
    Task<ICanonicalAttachmentOriginalSelection?> ResolveOriginalPickedPathWithinSourceAsync(
        AuthenticatedResourceActor expectedHomeActor, string observedPickedPath,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalSelection(ICanonicalAttachmentOriginalSelection sameSelection);
    Task RevalidateOriginalSelectionWithinSourceAsync(ICanonicalAttachmentOriginalSelection sameSelection,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>One real Home READ over an issued file selection. Implementations retain
/// their own request/currentness/close custody; none of the observation fields grants
/// authority to another file, revision, profile or conversation.</summary>
public interface ICanonicalAttachmentOriginalReadAdmission : IAsyncDisposable
{
    ICanonicalAttachmentOriginalSelection OriginalSelection { get; }
    string OriginalApprovalRequestId { get; }
    Task ValidateOriginalWithinSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalAttachmentOriginalReadSource
{
    bool IsAcknowledgedOriginalReadRefusal(Task sameAcquisition) => false;
    Task<ICanonicalAttachmentOriginalReadAdmission> AcquireOriginalReadWithinSourceAsync(
        ICanonicalAttachmentOriginalSelection sameSelection,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalRead(ICanonicalAttachmentOriginalSelection sameSelection,
        ICanonicalAttachmentOriginalReadAdmission sameAdmission);
}

/// <summary>Actual held content from the SAME private Files selection and individually
/// approved Home READ. Close joins the original reader and native custody. A URI/path
/// is not supplied as a replacement for the held stream.</summary>
public interface ICanonicalAttachmentOriginalContentLease : IAsyncDisposable
{
    ICanonicalAttachmentOriginalSelection OriginalSelection { get; }
    ICanonicalAttachmentOriginalReadAdmission OriginalRead { get; }
    Stream OriginalContent { get; }
    Task ValidateOriginalWithinSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalAttachmentOriginalContentSource
{
    Task<ICanonicalAttachmentOriginalContentLease> OpenOriginalContentWithinSourceAsync(
        ICanonicalAttachmentOriginalSelection sameSelection, ICanonicalAttachmentOriginalReadAdmission sameRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalContent(ICanonicalAttachmentOriginalSelection sameSelection,
        ICanonicalAttachmentOriginalReadAdmission sameRead, ICanonicalAttachmentOriginalContentLease sameLease);
}

/// <summary>The SAME maintained attachment service chain can extract a verified
/// original stream. This is a local read, never an import, index/embedding write,
/// model permission or accepted Chat input. The destination owner persists only
/// after its separate current conversation/import WRITE admission.</summary>
public interface IOriginalMessageAttachmentProcessingSource
{
    Task<OriginalMessageAttachmentProcessingResult> ProcessOriginalWithinSourceAsync(
        ICanonicalAttachmentOriginalContentLease sameContent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token,
        AttachmentProcessingOptions? options = null);
    bool IsIssuedOriginalProcessing(ICanonicalAttachmentOriginalContentLease sameContent,
        OriginalMessageAttachmentProcessingResult sameResult);
}

/// <summary>Immutable local processing observation. Constructing one never issues a
/// service result; callers must verify the actual configured service's issuer proof.</summary>
public sealed record OriginalMessageAttachmentProcessingResult(CanonicalAttachmentFileIdentity OriginalFile,
    MessageAttachmentKind Kind, AttachmentProcessingState State, AttachmentAnalysisMethod Method,
    string MediaType, string ExtractedText, string Notice);
