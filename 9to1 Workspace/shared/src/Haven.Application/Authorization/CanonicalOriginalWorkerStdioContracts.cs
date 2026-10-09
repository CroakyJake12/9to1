using System.Text.Json;

namespace Haven.Application;

public enum CanonicalOriginalWorkerKind { Calc, DuckDb }
public enum CanonicalOriginalWorkerAction
{
    OpenReadOnly, Read, Edit, QueryReadOnly, Materialize, SaveAs, CreateDatabase, StartRuntime
}

/// <summary>Observations from the actual enrolled runtime source. Implementing this
/// interface or copying a digest does not authenticate a runtime or issue a launch.</summary>
public interface ICanonicalOriginalWorkerRuntimeObservation
{
    CanonicalOriginalWorkerKind Kind { get; }
    string PackageId { get; }
    string Version { get; }
    string CatalogueRevision { get; }
    string ActivationSha256 { get; }
    string OriginalRuntimeSha256 { get; }
    bool RequiresOriginalCompanion { get; }
}

/// <summary>A private Files-issued current selection/destination, retained by the
/// configured producer. IDs, paths, receipt fields and this interface are not access.</summary>
public interface ICanonicalOriginalWorkerResource
{
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    ResourceScope OriginalScope { get; }
    string OriginalContentSha256 { get; }
}

/// <summary>The configured product source's actual pre-existing runtime profile
/// reservation. It is a separate WRITE resource, never a workbook READ grant or
/// a directory to create, select by path, or delete during worker cleanup.</summary>
public interface ICanonicalOriginalWorkerProfileReservation : ICanonicalOriginalWorkerResource
{
    ICanonicalOriginalWorkerRuntimeObservation OriginalRuntime { get; }
    string OriginalNativeEvidenceSha256 { get; }
}

/// <summary>One immutable command issued by the actual product source after observing
/// its real Files resources and actor. Parameters contain observations; they cannot
/// select a runtime, executable, script, working directory or ambient environment.</summary>
public interface ICanonicalOriginalWorkerOperationIntent
{
    AuthenticatedResourceActor Actor { get; }
    Guid OperationId { get; }
    CanonicalOriginalWorkerKind WorkerKind { get; }
    CanonicalOriginalWorkerAction Action { get; }
    ICanonicalOriginalWorkerRuntimeObservation OriginalRuntime { get; }
    IReadOnlyList<ICanonicalOriginalWorkerResource> OriginalResources { get; }
    string OriginalMethod { get; }
    JsonElement OriginalParameters { get; }
    ICanonicalOriginalWorkerProfileReservation? OriginalRuntimeProfile { get; }
}

public interface ICanonicalOriginalWorkerOperationAcknowledgment
{
    ICanonicalOriginalWorkerOperationIntent OriginalIntent { get; }
    bool Applied { get; }
    JsonElement OriginalResult { get; }
    string Reason { get; }
}

/// <summary>The concrete configured product owner proves private intent/resource/
/// runtime issuance before metadata is read. It owns the actual Files pins and exact
/// command child. Its generic settlement port releases those pins before Home audit.</summary>
public interface ICanonicalOriginalWorkerOperationProducer
{
    bool IsIssuedOriginalOperationIntent(ICanonicalOriginalWorkerOperationIntent intent);
    bool IsIssuedOriginalRuntimeProfile(ICanonicalOriginalWorkerProfileReservation sameProfile,
        ICanonicalOriginalWorkerOperationIntent sameIntent);
    string GetOriginalOperationIntentDigest(ICanonicalOriginalWorkerOperationIntent intent);
    Task ValidateOriginalOperationIntentWithinSourceAsync(ICanonicalOriginalWorkerOperationIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalPinnedOperation(ICanonicalOriginalWorkerOperationIntent intent);
    Task<ICanonicalOriginalWorkerOperationAcknowledgment> InvokeOriginalOperationWithinSourceAsync(
        ICanonicalOriginalWorkerOperationIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsOriginalOperationTask(ICanonicalOriginalWorkerOperationIntent intent,
        Task<ICanonicalOriginalWorkerOperationAcknowledgment> actual);
    bool IsOwnedOriginalOperationAcknowledgment(ICanonicalOriginalWorkerOperationIntent intent,
        ICanonicalOriginalWorkerOperationAcknowledgment acknowledgment, Task<ICanonicalOriginalWorkerOperationAcknowledgment> actual);
}

public interface ICanonicalOriginalWorkerHomeOperationClaim : IAsyncDisposable
{
    ICanonicalOriginalWorkerOperationIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalOriginalWorkerHomeOperationSource
{
    Task<ICanonicalOriginalWorkerHomeOperationClaim> AcquireOriginalOperationWithinSourceAsync(
        ICanonicalOriginalWorkerOperationIntent intent, Action<Action> scope, Action<Task> retain,
        Action<ICanonicalOriginalWorkerHomeOperationClaim>? capture, CancellationToken token);
    bool IsIssuedOriginalOperationClaim(ICanonicalOriginalWorkerHomeOperationClaim claim,
        ICanonicalOriginalWorkerOperationIntent intent);
    Task AcquireOriginalOperationEntryWithinSourceAsync(ICanonicalOriginalWorkerHomeOperationClaim claim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ValidateOriginalOperationWithinSourceAsync(ICanonicalOriginalWorkerHomeOperationClaim claim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalOperation(ICanonicalOriginalWorkerHomeOperationClaim claim,
        ICanonicalOriginalWorkerOperationIntent intent);
    void RetainOriginalOperation(ICanonicalOriginalWorkerHomeOperationClaim claim,
        Task<ICanonicalOriginalWorkerOperationAcknowledgment> actual);
    Task CompleteOriginalOperationWithinSourceAsync(ICanonicalOriginalWorkerHomeOperationClaim claim,
        Task<ICanonicalOriginalWorkerOperationAcknowledgment> actual,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsAcknowledgedOriginalOperationRefusal(Task sameAcquisition) => false;
    bool IsIssuedOriginalSettlementReleasePhase(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalOriginalWorkerOperationIntent,
            ICanonicalOriginalWorkerOperationAcknowledgment> samePhase) => false;
}

/// <summary>Actual controlled child custody. These are original pending/terminal
/// receipts, never completed-task substitutes for a native handle or remote process.</summary>
public interface ICanonicalOriginalWorkerChildCustody
{
    Task OriginalAdmission { get; }
    Task OriginalStandardInput { get; }
    Task OriginalStandardOutput { get; }
    Task OriginalStandardError { get; }
    Task OriginalExit { get; }
    Task? OriginalClose { get; }
}

/// <summary>Opaque stdio issued by the authenticated enrolled Root runtime. The
/// command port accepts only the SAME privately issued intent admitted by the Home
/// operation source. Calc includes its SAME controlled LibreOffice/UNO companion.</summary>
public interface ICanonicalOriginalWorkerStdio : ICanonicalOriginalWorkerChildCustody
{
    ICanonicalOriginalWorkerRuntimeObservation OriginalRuntime { get; }
    IReadOnlyList<ICanonicalOriginalWorkerChildCustody> OriginalChildren { get; }
    Task<ICanonicalOriginalWorkerOperationAcknowledgment> InvokeOriginalCommandWithinSourceAsync(
        ICanonicalOriginalWorkerOperationIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsOriginalCommandTask(ICanonicalOriginalWorkerOperationIntent intent,
        Task<ICanonicalOriginalWorkerOperationAcknowledgment> actual);
    bool IsOwnedOriginalCommandAcknowledgment(ICanonicalOriginalWorkerOperationIntent intent,
        ICanonicalOriginalWorkerOperationAcknowledgment acknowledgment, Task<ICanonicalOriginalWorkerOperationAcknowledgment> actual);
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalOriginalWorkerStdioSource
{
    Task<ICanonicalOriginalWorkerRuntimeObservation?> ObserveOriginalRuntimeWithinSourceAsync(
        CanonicalOriginalWorkerKind kind, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalRuntime(ICanonicalOriginalWorkerRuntimeObservation same);
    Task<ICanonicalOriginalWorkerStdio> AcquireOriginalStdioWithinSourceAsync(
        ICanonicalOriginalWorkerOperationIntent sameIntent, ICanonicalOriginalWorkerHomeOperationClaim sameClaim,
        Action<Action> scope, Action<Task> retain, Action<ICanonicalOriginalWorkerStdio>? capture, CancellationToken token);
    bool IsIssuedOriginalStdio(ICanonicalOriginalWorkerStdio same);
    /// <summary>Prepare the exact accepted command's native transfers BEFORE Home
    /// entry/product pins. Invoke subsequently uses only this cached original
    /// preparation and pure held checks; it never performs Home/Files reads.</summary>
    Task PrepareOriginalCommandWithinSourceAsync(ICanonicalOriginalWorkerStdio same,
        ICanonicalOriginalWorkerOperationIntent sameIntent, ICanonicalOriginalWorkerHomeOperationClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task RevalidateOriginalStdioWithinSourceAsync(ICanonicalOriginalWorkerStdio same,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
