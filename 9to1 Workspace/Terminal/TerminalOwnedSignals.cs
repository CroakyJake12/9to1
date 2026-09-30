using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Terminal;

public sealed record TerminalOwnedSessionSnapshot(Guid SessionID, TerminalEnvironmentId EnvironmentID, long Revision,
    int ProcessID, string ShellProfileID, string WorkingDirectory);

/// <summary>Runtime-only ownership of actual host-created interactive sessions. This never restores or invents a process.
/// Registration is a trusted in-process host composition operation, not a client-supplied session descriptor endpoint.</summary>
public sealed class TerminalOwnedSessionRegistry(IAuthenticatedResourceActorSource actors) : ICanonicalResourceAccessResolver
{
    private sealed record Entry(AuthenticatedResourceActor Actor, ITerminalInteractiveSession Session);
    private readonly ConcurrentDictionary<Guid, Entry> _sessions = new();
    public string ResourceKind => "terminal.session";
    public async Task<TerminalOwnedSessionSnapshot> RegisterAsync(ITerminalInteractiveSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Current Home actor is required.");
        var snapshot = Snapshot(session);
        if (!_sessions.TryAdd(snapshot.SessionID, new(actor, session))) throw new InvalidOperationException("Terminal session is already registered.");
        return snapshot;
    }
    public async Task<TerminalOwnedSessionSnapshot> GetAsync(Guid sessionID, CancellationToken ct = default)
        => Snapshot((await CurrentAsync(sessionID, ct).ConfigureAwait(false)).Session);
    public void Unregister(ITerminalInteractiveSession session)
    {
        if (_sessions.TryGetValue(session.Metadata.SessionId, out var entry) && ReferenceEquals(entry.Session, session))
            _sessions.TryRemove(new KeyValuePair<Guid, Entry>(session.Metadata.SessionId, entry));
    }
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
    {
        var allowed = false;
        if (actionId == TerminalSignalIntent.ActionID && scope.Kind == ResourceKind && scope.Access == ResourceAccess.Write && Guid.TryParse(scope.Id, out var id))
        {
            try
            {
                var entry = await CurrentAsync(id, ct).ConfigureAwait(false);
                allowed = entry.Actor == actor && Snapshot(entry.Session).Revision.ToString(CultureInfo.InvariantCulture) == scope.Revision;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException) { }
        }
        return new(allowed, allowed ? "terminal_session_current" : "terminal_session_unavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
    }
    internal async Task SignalAsync(TerminalSignalIntent intent, AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        var entry = await CurrentAsync(intent.Session.SessionID, ct).ConfigureAwait(false);
        if (entry.Actor != expectedActor || Snapshot(entry.Session) != intent.Session)
            throw new UnauthorizedAccessException("Terminal process, context or ownership changed before the signal.");
        await entry.Session.SignalAsync(intent.Signal, ct).ConfigureAwait(false);
    }
    private async Task<Entry> CurrentAsync(Guid id, CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (actor is null || !_sessions.TryGetValue(id, out var entry) || entry.Actor != actor)
            throw new UnauthorizedAccessException("The current actor does not own this live Terminal session.");
        return entry;
    }
    private static TerminalOwnedSessionSnapshot Snapshot(ITerminalInteractiveSession session)
    {
        var state = session.Metadata;
        if (session.Environment.State != TerminalEnvironmentConnectionState.Ready || state.SessionId == Guid.Empty || state.EnvironmentId is null || state.EnvironmentId != session.Environment.Id || state.Revision < 1 ||
            state.State is not (TerminalSessionLifecycleState.Ready or TerminalSessionLifecycleState.Running or TerminalSessionLifecycleState.Interrupting) ||
            session.ProcessId is not { } pid || pid <= 0 || !session.Environment.Capabilities.HasFlag(TerminalEnvironmentCapability.Signals))
            throw new InvalidOperationException("A current live process with supported signal capability is required.");
        return new(state.SessionId, state.EnvironmentId.Value, state.Revision, pid, state.ShellProfileId ?? state.ShellRuntime, state.CurrentWorkingDirectory ?? state.InitialWorkingDirectory);
    }
}

public sealed class TerminalSignalIntent
{
    public const string AppID = "terminal";
    public const string ActionID = "terminal.process.signal";
    private readonly JsonElement _arguments;
    private TerminalSignalIntent(TerminalOwnedSessionSnapshot session, TerminalProcessSignal signal)
    {
        if (!Enum.IsDefined(signal)) throw new ArgumentOutOfRangeException(nameof(signal));
        Session = session; Signal = signal;
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("terminal.session", session.SessionID.ToString(), session.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write) });
        _arguments = JsonSerializer.SerializeToElement(new { session.SessionID, environmentID = session.EnvironmentID.Value, session.Revision, session.ProcessID, session.WorkingDirectory, signal });
    }
    public TerminalOwnedSessionSnapshot Session { get; }
    public TerminalProcessSignal Signal { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static async Task<TerminalSignalIntent> CaptureAsync(TerminalOwnedSessionRegistry registry, Guid sessionID, TerminalProcessSignal signal, CancellationToken ct = default)
        => new(await registry.GetAsync(sessionID, ct).ConfigureAwait(false), signal);
}

public sealed class TerminalSignalActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == TerminalSignalIntent.AppID && actionId == TerminalSignalIntent.ActionID
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null;
}

public sealed class TerminalOwnedSignalExecutor(TerminalOwnedSessionRegistry sessions, HomeResourceOperationBroker home)
{
    public async Task ExecuteAsync(TerminalSignalIntent intent, HomeResourceExecutionCapability capability, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        if (await sessions.GetAsync(intent.Session.SessionID, ct).ConfigureAwait(false) != intent.Session)
            throw new InvalidOperationException("Terminal session revision changed; request a new preview.");
        var actor = await home.ClaimExecutionAsync(capability, TerminalSignalIntent.AppID, TerminalSignalIntent.ActionID, intent.Scopes, intent.Arguments, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorise this exact Terminal signal.");
        await sessions.SignalAsync(intent, actor, ct).ConfigureAwait(false);
    }
}
