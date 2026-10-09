using Haven.Core;

namespace Haven.Application;

// These live observations are not persisted grants. Browser approval permits the
// native transfer only; Files READ/WRITE and canonical destination selection remain separate.
public interface IBrowserOriginalNativeDownloadApproval
{
    Guid ActionId { get; }
    bool IsPrivate { get; }
}

public interface IBrowserOriginalApprovedNativeDownloadExecution : IBrowserNativeDownloadExecution
{
    void BindOriginalDownloadApproval(IBrowserOriginalNativeDownloadApprovalSource source,
        IBrowserOriginalNativeDownloadApproval originalApproval);
}

public interface IBrowserOriginalNativeDownloadApprovalSource
{
    bool IsIssuedOriginalApproval(IBrowserOriginalNativeDownloadApproval originalApproval,
        IBrowserNativeDownloadExecution sameExecution);
    bool IsOriginalExecutionAdmission(IBrowserOriginalNativeDownloadApproval originalApproval,
        IBrowserNativeDownloadExecution sameExecution);
    Task<BrowserDownloadRecord>? GetOriginalExecutionTask(IBrowserOriginalNativeDownloadApproval originalApproval);
    Task WaitOriginalApprovalSettlementWithinSourceAsync(IBrowserOriginalNativeDownloadApproval originalApproval,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    bool IsOriginalApprovedCompletion(IBrowserOriginalNativeDownloadApproval originalApproval,
        IBrowserNativeDownloadExecution sameExecution, Task<BrowserDownloadRecord> sameExecutionTask,
        BrowserDownloadRecord sameRecord);
}

public interface IBrowserOriginalNativeDownloadTransportPlan
{
    Guid ActionId { get; }
}

// Path values describe the actual configured transport plan. Only its SAME issuer
// and the retained physical parent/leaf can establish current source identity.
public sealed record BrowserOriginalNativeDownloadPlanDescription(Guid ActionId,
    string DownloadDirectory, string PartialPath, string FinalPath, string FileName,
    string RecordAddress, DateTimeOffset PreparedAt);

public interface IBrowserOriginalNativeDownloadTransportSource
{
    bool IsIssuedOriginalTransportPlan(IBrowserOriginalNativeDownloadTransportPlan originalPlan);
    BrowserOriginalNativeDownloadPlanDescription GetOriginalTransportPlanDescription(
        IBrowserOriginalNativeDownloadTransportPlan originalPlan);
}

public interface IBrowserOriginalNativeDownloadCompletion
{
    IBrowserOriginalNativeDownloadTransportPlan OriginalPlan { get; }
    IBrowserOriginalNativeDownloadApprovalSource OriginalApprovalSource { get; }
    IBrowserOriginalNativeDownloadApproval OriginalApproval { get; }
    IBrowserNativeDownloadExecution OriginalExecution { get; }
}

public interface IBrowserOriginalNativeDownloadCompletionSource
{
    bool IsIssuedOriginalCompletion(IBrowserOriginalNativeDownloadCompletion originalCompletion,
        IBrowserOriginalNativeDownloadTransportPlan samePlan);
    Task RevalidateOriginalCompletionWithinSourceAsync(IBrowserOriginalNativeDownloadCompletion originalCompletion,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    void DemandOriginalCompletedDownload(IBrowserOriginalNativeDownloadCompletion originalCompletion);
}

public interface IBrowserOriginalDownloadContent
{
    BrowserDownloadRecord OriginalRecord { get; }
    IBrowserOriginalNativeDownloadTransportPlan OriginalPlan { get; }
    Task? OriginalClose { get; }
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}

public interface IBrowserOriginalNativeDownloadPhysicalOwner
{
    IBrowserOriginalNativeDownloadTransportSource? OriginalTransportSource { get; }
    void BindOriginalTransportSource(IBrowserOriginalNativeDownloadTransportSource sameSource);
    Task PrepareOriginalTransportPlanWithinSourceAsync(IBrowserOriginalNativeDownloadTransportPlan originalPlan,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    Task<IBrowserOriginalDownloadContent> FinalizeOriginalDownloadWithinSourceAsync(
        IBrowserOriginalNativeDownloadTransportPlan originalPlan,
        IBrowserOriginalNativeDownloadCompletionSource sameCompletionSource,
        IBrowserOriginalNativeDownloadCompletion originalCompletion, string? contentType,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    bool IsIssuedOriginalContent(IBrowserOriginalDownloadContent originalContent);
    Task WaitOriginalApprovedContentWithinSourceAsync(IBrowserOriginalDownloadContent originalContent,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException("The actual original approval settlement owner is unavailable."));
    Task RevalidateOriginalContentWithinSourceAsync(IBrowserOriginalDownloadContent originalContent,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    Task CopyOriginalContentWithinSourceAsync(IBrowserOriginalDownloadContent originalContent,
        Stream actualDestination, Action<Action> scope, Action<Task> retain,
        CancellationToken cancellationToken);
    Task? OriginalClose { get; }
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}

// The Browser view issues a selection of a SAME actual canonical download-ledger
// row. This is selection provenance only; Files independently authorizes its own
// current workspace and metadata before looking up or revealing a registration.
public interface IBrowserOriginalDownloadRecordObservation
{
    BrowserDownloadRecord OriginalRecord { get; }
}
public interface IBrowserOriginalDownloadRecordSource
{
    bool IsIssuedOriginalDownloadRecord(IBrowserOriginalDownloadRecordObservation originalRecord);
    Task RevalidateOriginalDownloadRecordWithinSourceAsync(IBrowserOriginalDownloadRecordObservation originalRecord,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
}

public interface IBrowserOriginalDownloadFilesDestination
{
    string DisplayName { get; }
}
public interface IBrowserOriginalDownloadFilesDestinationCatalogue
{
    IReadOnlyList<IBrowserOriginalDownloadFilesDestination> Destinations { get; }
    string Detail { get; }
}
public enum BrowserOriginalDownloadFilesRegistrationState { AwaitingApproval, Registered }
public interface IBrowserOriginalDownloadFilesRegistration
{
    Guid OriginalOperationId { get; }
    BrowserDownloadRecord OriginalRecord { get; }
    BrowserOriginalDownloadFilesRegistrationState State { get; }
    Guid? OriginalFilesItemId { get; }
    Guid? OriginalFilesRevisionId { get; }
    string Detail { get; }
}

// Implemented by the actual Files/Home owning composition. Destinations and
// registrations are privately issued; their IDs, titles and public state never
// grant Files READ/WRITE. Register is finite: a real pending individual Home
// review returns AwaitingApproval. Continue with the SAME operation/destination
// after actual approval. A registered mapping reopens through canonical Files.
public interface IBrowserOriginalDownloadFilesService
{
    IBrowserOriginalNativeDownloadPhysicalOwner OriginalContentOwner { get; }
    Task<IBrowserOriginalDownloadFilesDestinationCatalogue> ReadOriginalDestinationsWithinSourceAsync(
        IBrowserOriginalDownloadContent originalContent, Action<Action> scope, Action<Task> retain,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalDestination(IBrowserOriginalDownloadFilesDestination originalDestination);
    Task RevalidateOriginalDestinationWithinSourceAsync(IBrowserOriginalDownloadFilesDestination originalDestination,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    Task<IBrowserOriginalDownloadFilesRegistration> RegisterOriginalDownloadWithinSourceAsync(
        IBrowserOriginalDownloadContent originalContent, IBrowserOriginalDownloadFilesDestination originalDestination,
        Guid originalOperationId, Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    bool IsIssuedOriginalRegistration(IBrowserOriginalDownloadFilesRegistration originalRegistration);
    Task<IBrowserOriginalDownloadFilesRegistration?> ReadOriginalRegistrationWithinSourceAsync(
        IBrowserOriginalDownloadRecordSource sameRecordSource, IBrowserOriginalDownloadRecordObservation originalRecord,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    Task RevealOriginalRegistrationWithinSourceAsync(IBrowserOriginalDownloadFilesRegistration originalRegistration,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    Task? OriginalClose { get; }
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}

// Physical destination observations remain subordinate to the actual Files owner.
// The configured source issues the marker from a real registered folder, retains
// its mapping and separate claimed Home WRITE, and demands them before each effect.
public interface IBrowserOriginalFilesPhysicalDestination { }
public sealed record BrowserOriginalFilesPhysicalDestinationDescription(string DirectoryPath,
    Guid FilesStoreId, Guid ParentFolderId, Guid NewFileId, Guid NewRevisionId,
    string RelativeContentReference, AuthenticatedResourceActor OriginalActor);
public interface IBrowserOriginalFilesPhysicalDestinationSource
{
    bool IsIssuedOriginalPhysicalDestination(IBrowserOriginalFilesPhysicalDestination originalDestination);
    BrowserOriginalFilesPhysicalDestinationDescription GetOriginalPhysicalDestinationDescription(
        IBrowserOriginalFilesPhysicalDestination originalDestination);
    void DemandOriginalFilesCommit(IBrowserOriginalFilesPhysicalDestination originalDestination);
}
public interface IBrowserOriginalFilesPhysicalPin : IAsyncDisposable
{
    IBrowserOriginalFilesPhysicalDestination OriginalDestination { get; }
    Task? OriginalClose { get; }
}
public interface IBrowserOriginalDownloadFilesPhysicalOwner
{
    IBrowserOriginalFilesPhysicalDestinationSource? OriginalFilesDestinationSource { get; }
    void BindOriginalFilesDestinationSource(IBrowserOriginalFilesPhysicalDestinationSource sameSource);
    Task<IBrowserOriginalFilesPhysicalPin> PinOriginalFilesDestinationWithinSourceAsync(
        IBrowserOriginalFilesPhysicalDestination originalDestination, Action<Action> scope,
        Action<Task> retain, CancellationToken cancellationToken);
    bool IsIssuedOriginalFilesPin(IBrowserOriginalFilesPhysicalPin originalPin);
    Task CopyOriginalDownloadToFilesWithinSourceAsync(IBrowserOriginalFilesPhysicalPin originalPin,
        IBrowserOriginalDownloadContent originalContent, Action<Action> scope, Action<Task> retain,
        CancellationToken cancellationToken);
}
