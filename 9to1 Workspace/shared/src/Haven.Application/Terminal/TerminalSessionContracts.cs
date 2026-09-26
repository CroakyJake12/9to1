using System;
using System.Threading;
using System.Threading.Tasks;

namespace Haven.Application;

public enum TerminalSessionLifecycleState
{
    Starting,
    Ready,
    Running,
    Interrupting,
    Ended,
    Faulted,
    Disposed
}

public enum TerminalOutputStream
{
    StandardOutput,
    StandardError,
    System
}

/// <summary>The interpretation state of a session; switching this value never replaces its process.</summary>
public enum TerminalSessionMode
{
    Command,
    Ai
}

public enum TerminalEnvironmentKind
{
    LocalHost,
    Wsl,
    Container,
    Ssh,
    VirtualMachine,
    DevEnvironment,
    NineToOneOs,
    Extension
}

public enum TerminalEnvironmentConnectionState
{
    Ready,
    Starting,
    Disconnected,
    Degraded,
    Unavailable
}

[Flags]
public enum TerminalEnvironmentCapability
{
    None = 0,
    InteractivePty = 1 << 0,
    Resize = 1 << 1,
    Signals = 1 << 2,
    ShellIntegration = 1 << 3,
    PersistentProcesses = 1 << 4,
    FileLinks = 1 << 5,
    PortForwarding = 1 << 6
}

/// <summary>A stable provider-owned identity. Display names and paths are never identities.</summary>
public readonly record struct TerminalEnvironmentId
{
    public TerminalEnvironmentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("An environment ID must be at most 256 printable characters.", nameof(value));
        Value = value.Trim();
        if (Value.Length == 0) throw new ArgumentException("An environment ID cannot be blank.", nameof(value));
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
}

public sealed record TerminalEnvironmentDescriptor(
    TerminalEnvironmentId Id,
    string ProviderId,
    TerminalEnvironmentKind Kind,
    string DisplayName,
    TerminalEnvironmentConnectionState State,
    string Platform,
    string Architecture,
    TerminalEnvironmentCapability Capabilities,
    bool IsElevated = false,
    string? SecurityContext = null,
    string? UnavailableReason = null);

public sealed record TerminalShellProfile(
    string Id,
    string DisplayName,
    string Executable,
    IReadOnlyList<string> Arguments,
    string ProviderId,
    bool SupportsShellIntegration,
    TerminalEnvironmentConnectionState State,
    string? UnavailableReason = null);

public sealed record TerminalSessionStartRequest(
    TerminalEnvironmentId EnvironmentId,
    string ShellProfileId,
    string WorkingDirectory,
    ushort Columns = 80,
    ushort Rows = 25,
    string? Title = null);

/// <summary>Structured, typed failures surfaced by Terminal providers and brokers.</summary>
public sealed record TerminalFailure(
    string Code,
    string Message,
    string TargetId,
    bool Recoverable,
    bool Retryable = false,
    string? Remediation = null);

public enum TerminalActionRisk
{
    ReadOnly,
    Mutating,
    Elevated,
    Destructive,
    Ambiguous
}

public enum TerminalActionKind
{
    TypedApi,
    ShellCommand,
    ScheduledAction
}

public sealed record TerminalResolvedAction(
    Guid Id,
    Guid SessionId,
    TerminalEnvironmentId EnvironmentId,
    TerminalActionKind Kind,
    string TargetApp,
    string Operation,
    string Summary,
    IReadOnlyList<string> AffectedObjects,
    TerminalActionRisk Risk,
    bool Reversible,
    bool HasExternalEffects,
    string? CommandText = null,
    string? UnknownImpact = null);

public sealed record TerminalAdviceContext(
    Guid SessionId,
    TerminalEnvironmentId EnvironmentId,
    TerminalSessionMode Mode,
    string ShellProfileId,
    string WorkingDirectory,
    string? PreviousCommand,
    int? PreviousExitCode,
    string SelectedOutput,
    string? ProjectId = null);

public sealed record TerminalAdviceResult(
    string Explanation,
    IReadOnlyList<string> SuggestedCommands,
    TerminalFailure? Failure = null);

/// <summary>
/// The advice boundary deliberately has no command execution capability. Suggestions are inert text.
/// </summary>
public interface ITerminalAdviceService
{
    Task<TerminalAdviceResult> AskAsync(TerminalAdviceContext context, string question, CancellationToken cancellationToken = default);
}

/// <summary>Central-action adapters implement this contract; Terminal never privately approves mutations.</summary>
public interface ITerminalActionBroker
{
    Task<TerminalResolvedAction> ResolveAsync(Guid sessionId, TerminalEnvironmentId environmentId, string request, CancellationToken cancellationToken = default);
    Task<TerminalActionExecutionResult> ExecuteAsync(TerminalResolvedAction action, string? verificationToken = null, CancellationToken cancellationToken = default);
}

public sealed record TerminalActionExecutionResult(
    bool Executed,
    string State,
    string Message,
    string? ResultText = null,
    TerminalFailure? Failure = null);

public enum TerminalHistoryOrigin
{
    UserCommand,
    AiAction,
    AiGeneratedCommand,
    Ask,
    ScheduledAction,
    AgentAction
}

public enum TerminalHistoryState
{
    Suggested,
    Inserted,
    Requested,
    PermissionRequired,
    Denied,
    Cancelled,
    Failed,
    Executed,
    Scheduled
}

public sealed record TerminalHistoryEntry(
    Guid Id,
    Guid SessionId,
    TerminalEnvironmentId EnvironmentId,
    TerminalHistoryOrigin Origin,
    TerminalHistoryState State,
    string DisplayText,
    DateTimeOffset Timestamp,
    int? ExitCode = null,
    Guid? ResolvedActionId = null);

public sealed record TerminalSessionMetadata(
    Guid SessionId,
    string ShellRuntime,
    string DisplayName,
    string InitialWorkingDirectory,
    string? CurrentWorkingDirectory,
    TerminalSessionLifecycleState State,
    DateTimeOffset StartedAt,
    int Generation,
    bool StateWasReset)
{
    public TerminalEnvironmentId? EnvironmentId { get; init; }
    public string? ShellProfileId { get; init; }
    public TerminalSessionMode Mode { get; init; } = TerminalSessionMode.Command;
    public long Revision { get; init; }
    public bool IsElevated { get; init; }
}

public sealed record TerminalSessionOutput(
    Guid SessionId,
    Guid? CommandId,
    TerminalOutputStream Stream,
    string Text,
    DateTimeOffset Timestamp)
{
    /// <summary>Original PTY bytes, when the provider supplies them; never reconstructed from Text.</summary>
    public ReadOnlyMemory<byte>? RawBytes { get; init; }
}

public sealed record TerminalSessionCommandResult(
    Guid SessionId,
    Guid CommandId,
    int? ExitCode,
    bool Cancelled,
    TimeSpan Duration,
    string? CurrentWorkingDirectory,
    bool ShellAlive,
    bool StateWasReset);

public interface ITerminalSession : IDisposable, IAsyncDisposable
{
    TerminalSessionMetadata Metadata { get; }
    int? ProcessId { get; }
    event EventHandler<TerminalSessionOutput>? OutputReceived;
    event EventHandler<TerminalSessionMetadata>? MetadataChanged;
    Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken);
    Task InterruptAsync(CancellationToken cancellationToken);
    Task SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken);
}

public interface ITerminalSessionFactory
{
    ITerminalSession Create(string initialDirectory, string? displayName = null);
}

/// <summary>Additional capabilities implemented by real interactive environments.</summary>
public interface ITerminalInteractiveSession : ITerminalSession
{
    TerminalEnvironmentDescriptor Environment { get; }
    TerminalSessionMode Mode { get; }
    Task SetModeAsync(TerminalSessionMode mode, CancellationToken cancellationToken = default);
    ValueTask SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default);
    ValueTask ResizeAsync(ushort columns, ushort rows, CancellationToken cancellationToken = default);
    Task SignalAsync(TerminalProcessSignal signal, CancellationToken cancellationToken = default);
}

public enum TerminalProcessSignal
{
    Interrupt,
    Terminate,
    Kill
}

public interface ITerminalInteractiveSessionFactory : ITerminalSessionFactory
{
    IReadOnlyList<TerminalEnvironmentDescriptor> ListEnvironments();
    IReadOnlyList<TerminalShellProfile> ListShellProfiles(TerminalEnvironmentId? environmentId = null);
    ITerminalInteractiveSession Create(TerminalSessionStartRequest request);
    Task<ITerminalInteractiveSession> CreateAsync(TerminalSessionStartRequest request, CancellationToken cancellationToken = default);
}
