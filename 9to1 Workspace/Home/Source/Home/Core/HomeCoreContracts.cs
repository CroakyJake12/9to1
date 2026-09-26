using System.Text.Json;

namespace HavenOS.Home.Core;

/// <summary>Contract versions are independent from the Home product version.</summary>
public readonly record struct HomeContractVersion(int Major, int Minor, int Patch) : IComparable<HomeContractVersion>
{
    public HomeContractVersion
    {
        if (Major < 0 || Minor < 0 || Patch < 0)
            throw new ArgumentOutOfRangeException(nameof(Major), "Contract version components cannot be negative.");
    }

    public int CompareTo(HomeContractVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public enum HomeServiceLifecycleState
{
    Starting,
    Ready,
    Degraded,
    Restarting,
    Stopped,
    Unavailable,
}

public enum HomeCoreErrorCode
{
    PermissionDenied,
    CallerIdentityUnverified,
    HomeServiceUnavailable,
    HomeServiceIncompatible,
    HomeStateCorrupt,
    HomeStateIncompatible,
    HomeStateConflict,
    HomeStateScopeUnsupported,
    HomeDependencyUnavailable,
    HomeServiceStartFailed,
    HomeShutdownBlocked,
}

public sealed record HomeCoreFailure(
    HomeCoreErrorCode Code,
    string Message,
    string Target,
    bool Retryable,
    string? RecoveryAction = null);

public sealed record HomeServiceDescriptor(
    string ServiceId,
    HomeContractVersion ContractVersion,
    HomeServiceLifecycleState State,
    bool IsAvailable,
    string? Diagnostic = null,
    long Revision = 0);

public sealed record HomeServiceRequirement(
    string ServiceId,
    int MajorVersion,
    int MinimumMinorVersion = 0,
    int? MaximumMinorVersionExclusive = null,
    bool Required = true)
{
    public bool Accepts(HomeContractVersion version) =>
        version.Major == MajorVersion &&
        version.Minor >= MinimumMinorVersion &&
        (MaximumMinorVersionExclusive is null || version.Minor < MaximumMinorVersionExclusive);
}

/// <summary>
/// Stable caller identity as established by a trusted platform authenticator. Display names
/// intentionally do not participate in identity or authorization decisions.
/// </summary>
public sealed record HomeCallerIdentity(string StableId, string Origin, string VerificationMethod);

public sealed record HomeCompatibilityRequest(
    string AppId,
    string AppVersion,
    IReadOnlyList<HomeServiceRequirement> RequiredServices);

public enum HomeCompatibilityState
{
    Compatible,
    RequiresHomeRepair,
    PermissionDenied,
}

public sealed record HomeCompatibilityResult(
    HomeCompatibilityState State,
    string AppId,
    long RegistryRevision,
    IReadOnlyList<HomeServiceDescriptor> AcceptedServices,
    IReadOnlyList<HomeCoreFailure> Failures)
{
    public bool CanStartNormally => State == HomeCompatibilityState.Compatible;
}

public sealed record HomeCoreStateSnapshot(
    long Revision,
    IReadOnlyList<HomeServiceDescriptor> Services,
    DateTimeOffset CapturedAtUtc);

public enum HomeDataScope
{
    DeviceLocal,
    AccountSynchronized,
    WorkspaceSynchronized,
    OrganizationSynchronized,
}

public enum HomeRecordAuthority
{
    LocalCanonical,
    RemoteCanonicalReplica,
    RebuildableCache,
}

/// <summary>
/// Extensible versioned Home-owned state. Payloads contain domain data, never raw credentials;
/// secret material must use an OS credential reference owned by the corresponding service.
/// </summary>
public sealed record HomeCoreStateRecord(
    string RecordId,
    string RecordType,
    int SchemaVersion,
    HomeDataScope Scope,
    HomeRecordAuthority Authority,
    long Revision,
    JsonElement Payload);

public sealed record HomeCoreStoredState(int SchemaVersion, long Revision, IReadOnlyList<HomeCoreStateRecord> Records);

public sealed record HomeStateReadResult(HomeCoreStoredState? State, HomeCoreFailure? Failure)
{
    public bool IsSuccess => State is not null && Failure is null;

    public static HomeStateReadResult Success(HomeCoreStoredState state) => new(state, null);
    public static HomeStateReadResult Failed(HomeCoreFailure failure) => new(null, failure);
}

public sealed record HomeStateWriteResult(HomeCoreStoredState? State, HomeCoreFailure? Failure)
{
    public bool IsSuccess => State is not null && Failure is null;

    public static HomeStateWriteResult Success(HomeCoreStoredState state) => new(state, null);
    public static HomeStateWriteResult Failed(HomeCoreFailure failure) => new(null, failure);
}

public interface IHomeCoreStateStore
{
    Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically creates/updates one record if the expected record revision still matches.</summary>
    Task<HomeStateWriteResult> WriteAsync(
        HomeCoreStateRecord record,
        long expectedRecordRevision,
        CancellationToken cancellationToken = default);
}

public interface IHomeCoreService
{
    HomeServiceDescriptor Descriptor { get; }
    IReadOnlyList<string> Dependencies { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Permission policy is supplied by the Home trust broker; callers fail closed without it.</summary>
public interface IHomeCoreAuthorization
{
    ValueTask<bool> IsAllowedAsync(
        HomeCallerIdentity caller,
        string target,
        IReadOnlySet<string> scopes,
        CancellationToken cancellationToken = default);
}

public sealed record HomeCoreDependencySignal(
    long Revision,
    IReadOnlyList<HomeServiceDescriptor> Services,
    bool RequiresReconnect,
    DateTimeOffset OccurredAtUtc);

