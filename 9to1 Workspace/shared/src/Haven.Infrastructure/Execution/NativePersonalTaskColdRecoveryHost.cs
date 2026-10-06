using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure;

public enum NativePersonalTaskColdRecoveryConfigurationKind
{
    Disabled, MalformedConfiguration, UnsupportedPlatform, RequestedUnverified, ConfiguredUnverified
}
public sealed record NativePersonalTaskColdRecoveryConfigurationStatus(
    NativePersonalTaskColdRecoveryConfigurationKind Kind, string Detail);

/// <summary>Explicit host configuration only. It never certifies a protected database,
/// a fresh process activation, a Conversation, Home/account ownership or permission.</summary>
public sealed class NativePersonalTaskColdRecoveryConfiguration
{
    public const string OriginalEnvironmentSelector = "HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY";
    private NativePersonalTaskColdRecoveryConfiguration(NativePersonalTaskColdRecoveryConfigurationStatus status) => OriginalStatus = status;
    public NativePersonalTaskColdRecoveryConfigurationStatus OriginalStatus { get; }
    public static NativePersonalTaskColdRecoveryConfiguration ReadExplicitEnvironment() =>
        ParseOriginalOptIn(Environment.GetEnvironmentVariable(OriginalEnvironmentSelector));
    public static NativePersonalTaskColdRecoveryConfiguration ParseOriginalOptIn(string? value) => new(
        value is null or "0" ? new(NativePersonalTaskColdRecoveryConfigurationKind.Disabled,
            "Personal-store cold recovery is disabled; ordinary Task execution is unchanged.") :
        value != "1" ? new(NativePersonalTaskColdRecoveryConfigurationKind.MalformedConfiguration,
            "The explicit personal-store recovery selector must be 0 or 1.") :
        !OperatingSystem.IsLinux() ? new(NativePersonalTaskColdRecoveryConfigurationKind.UnsupportedPlatform,
            "The actual protected personal-store producer currently supports Linux only.") :
        new(NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified,
            "Requested; the SAME canonical sources must be configured before use. Store protection and fresh recovery authority remain unverified."));
}
public sealed class NativePersonalTaskColdRecoverySetupRequiredException(NativePersonalTaskColdRecoveryConfigurationStatus status)
    : InvalidOperationException(status.Detail)
{
    public NativePersonalTaskColdRecoveryConfigurationStatus OriginalStatus { get; } = status;
}

/// <summary>Normal native host composition and invocation of the SAME configured canonical
/// producer. It returns actual coordinator Tasks, performs no claim/permission reconstruction,
/// and creates no independent recovery executor or process-retirement owner.</summary>
public sealed class NativePersonalTaskColdRecoveryHost(NativePersonalTaskColdRecoveryConfiguration configuration)
{
    private readonly object _gate = new();
    private SqliteTaskRunColdRecoveryJournal? _journal;
    private TaskRunPermissionAuthority? _authority;
    private TaskExecutionCoordinator? _coordinator;
    private Func<ChatSessionService>? _actualChat;
    public NativePersonalTaskColdRecoveryConfigurationStatus ConfigurationStatus
    {
        get
        {
            lock (_gate) return _journal is not null && _authority is not null && _coordinator is not null
                ? new(NativePersonalTaskColdRecoveryConfigurationKind.ConfiguredUnverified,
                    "SAME native journal/context/actor/authority/coordinator configured. Actual store protection, safe capsule, fresh process activation and policy must still pass their owning sources.")
                : configuration.OriginalStatus;
        }
    }
    internal bool OriginalConfigurationRequested => configuration.OriginalStatus.Kind == NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified;
    internal void ConfigureOriginalAuthority(IServiceProvider actualProvider, TaskRunPermissionAuthority actualAuthority)
    {
        if (!OriginalConfigurationRequested) return;
        var actualJournal = actualProvider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>();
        if (!actualJournal.HasOriginalComposition(actualProvider.GetRequiredService<SqliteDatabase>(),
                actualProvider.GetRequiredService<IAppPaths>(), actualProvider.GetRequiredService<HostLocalTaskActorSource>()))
            throw new InvalidOperationException("The actual native journal must retain the SAME database, paths and native actor source.");
        // Source configuration happens before the actual authority factory returns. No lazy Chat
        // capture is evaluated here and no actor, filesystem, journal or policy read occurs.
        actualAuthority.ConfigureOriginalColdRecoverySources(actualJournal, actualJournal);
        if (!actualAuthority.HasOriginalColdRecoveryComposition(actualJournal, actualJournal))
            throw new InvalidOperationException("The SAME actual cold authority configuration was not retained.");
        lock (_gate)
        {
            if (_authority is not null || _journal is not null) throw new InvalidOperationException("The original cold host authority was already configured.");
            _journal = actualJournal; _authority = actualAuthority;
        }
    }
    internal void ConfigureOriginalCoordinator(IServiceProvider actualProvider, TaskExecutionCoordinator actualCoordinator)
    {
        if (!OriginalConfigurationRequested) return;
        var actualAuthority = actualProvider.GetRequiredService<TaskRunPermissionAuthority>();
        var actualJournal = actualProvider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>();
        lock (_gate)
            if (!ReferenceEquals(_authority, actualAuthority) || !ReferenceEquals(_journal, actualJournal) || _coordinator is not null)
                throw new InvalidOperationException("Configure the SAME original authority before its actual coordinator is exposed.");
        if (!actualAuthority.HasOriginalColdRecoveryComposition(actualJournal, actualJournal))
            throw new InvalidOperationException("Actual private cold authority/source configuration is absent.");
        actualCoordinator.ConfigureOriginalColdRecovery(actualJournal, actualJournal);
        if (!actualCoordinator.HasOriginalColdRecoveryComposition(actualJournal, actualJournal))
            throw new InvalidOperationException("The SAME actual coordinator sources were not retained.");
        lock (_gate)
        {
            _coordinator = actualCoordinator;
            _actualChat = () => actualProvider.GetRequiredService<ChatSessionService>();
        }
    }
    public bool HasOriginalComposition(SqliteTaskRunColdRecoveryJournal sameJournal,
        TaskRunPermissionAuthority sameAuthority, TaskExecutionCoordinator sameCoordinator)
    {
        lock (_gate) return _journal is not null && _authority is not null && _coordinator is not null &&
            ReferenceEquals(_journal, sameJournal) && ReferenceEquals(_authority, sameAuthority) && ReferenceEquals(_coordinator, sameCoordinator);
    }
    private (TaskExecutionCoordinator Coordinator, Func<ChatSessionService> Chat) RequireOriginalComposition()
    {
        lock (_gate)
        {
            if (_coordinator is null || _actualChat is null || _authority is null || _journal is null)
                throw new NativePersonalTaskColdRecoverySetupRequiredException(ConfigurationStatus);
            return (_coordinator, _actualChat);
        }
    }
    /// <summary>IDs select an observation. Only the genuine journal issues its capsule; this
    /// returns the SAME coordinator source Task without a status-changing await proxy.</summary>
    public Task<TaskRunColdCapsule?> ObserveOriginalInputAsync(Guid taskId, Guid expectedRunId, CancellationToken token) =>
        RequireOriginalComposition().Coordinator.ObserveOriginalColdInputAsync(taskId, expectedRunId, token);
    /// <summary>Command acquisition is distinct from process-owned business lifetime. The
    /// original private claim/context/new activation and current policy are reauthorized by Data/AUTH.</summary>
    public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalResumeAsync(
        Guid taskId, Guid expectedRunId, CancellationToken commandToken)
    {
        var actual = RequireOriginalComposition();
        return actual.Coordinator.StartObservedColdOriginalRunResumeAsync(actual.Chat(), taskId, expectedRunId, commandToken);
    }
}
