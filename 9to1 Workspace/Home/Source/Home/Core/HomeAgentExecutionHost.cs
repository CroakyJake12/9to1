using System.Runtime.ExceptionServices;
using Dulche.Runtime.Agents;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Designated Home owner only, over the SAME original Core Session. It creates no socket reader,
/// lease or durable Agent registry. Each private handle owns one admitted canonical Den context.</summary>
public sealed class HomeAgentExecutionHost(HomeNativeCoreApiSessions originalCoreSessions,
    IHomeAgentCurrentDenSource currentDens, IHomeAgentExecutionSessionComposer composer,
    IAuthenticatedResourceActorSource actors, HomePermissionTrustService permissions, IModelProviderRegistry models,
    ResourceAuthorizationService resources, Func<IHomeAgentOriginalToolPolicySource?> originalTools,
    HomePersonalModelRoutes? routes = null, IHomeApprovalPromptPresenter? presenter = null)
    : IHomeAgentExecutionHost, IChatExecutionAdmission, IAsyncDisposable
{
    private readonly IHomeAgentExecutionSessionComposer _composer = composer;
    private readonly object _admission = new();
    private readonly Dictionary<Handle, HomeAgentOriginalWork> _contexts = new(ReferenceEqualityComparer.Instance);
    private Task? _close;
    private Task? _retire;
    private readonly List<Task> _retirements = [];
    private bool _closing;
    private bool _retiring;

    public Task<Handle> PrepareAsync(HomeNativeCoreApiSessions.Session sameCoreSession,
        DenAgentReference reference, string objective, CancellationToken cancellationToken = default)
    {
        HomeAgentOriginalWork work;
        lock (_admission)
        {
            if (_closing || _retiring || _contexts.Count >= 128) throw new ObjectDisposedException(nameof(HomeAgentExecutionHost));
            work = new HomeAgentOriginalWork();
            // Reserve original work before binding awaits. Global retirement cannot miss this opening.
            var reserved = new Handle(this, work, reference, objective);
            _contexts.Add(reserved, work);
            return work.RunAsync(async token =>
            {
                var connection = await originalCoreSessions.BindOriginalAgentAsync(sameCoreSession, token).ConfigureAwait(false);
                work.BindConnection(connection);
                return await connection.RunAsync(async ct =>
                {
                    var factory = await currentDens.ResolveCurrentAsync(reference.DenId, connection.Actor, ct).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("The current canonical Home-owned Den is unavailable.");
                    await connection.DemandCurrentAsync(ct).ConfigureAwait(false);
                    var personal = await factory.OpenAsync(ct).ConfigureAwait(false);
                    await connection.DemandCurrentAsync(ct).ConfigureAwait(false);
                    if (personal.Actor != connection.Actor || personal.DenId != reference.DenId) throw Refused();
                    var issuer = new HomeAgentExecutionAdmissions(factory, actors, connection, permissions,
                        composer.CreateRunReader(personal.Den, reference.NamespaceId), models, resources,
                        work.Lifetime, composer.CreateRunReader, routes, presenter, originalTools());
                    work.Issuer = issuer;
                    reserved.Prepared = await issuer.PrepareAsync(reference, objective, ct).ConfigureAwait(false);
                    return reserved;
                }, token).ConfigureAwait(false);
            }, cancellationToken);
        }
    }

    public sealed class Handle
    {
        private readonly HomeAgentExecutionHost _owner;
        private readonly HomeAgentOriginalWork _work;
        private readonly string _objective;
        internal HomeAgentExecutionAdmissions.Preparation? Prepared;
        internal HomeAgentExecutionAdmissions.OriginalInvocation? _original;
        private IDenAgentExecutionSession? _session;
        private Task<AgentResult<AgentExecutionSnapshot>>? _start;
        private AgentBudgetLimits? _startBudget;
        private string? _startKey;
        internal string? _run;
        internal Handle(HomeAgentExecutionHost owner, object work, DenAgentReference reference, string objective)
        { _owner = owner; _work = work as HomeAgentOriginalWork ?? throw Refused(); Reference = reference; _objective = objective; }
        public DenAgentReference Reference { get; }
        public string PermissionRequestId => Prepared?.RequestId ?? throw Refused();
        public Task<AgentResult<AgentExecutionSnapshot>> StartAsync(AgentBudgetLimits budget, string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256) throw Refused();
            lock (_owner._admission)
            {
                if (_start is not null)
                    return _startBudget == budget && _startKey == idempotencyKey ? _start : throw Refused();
                _startBudget = budget; _startKey = idempotencyKey;
                return _start = _owner.RunAsync(this, async token =>
                {
                    var issuer = _work.Issuer ?? throw Refused();
                    _original = await issuer.AdmitAsync(Prepared ?? throw Refused(), token).ConfigureAwait(false);
                    var factory = _owner._composer.Compose(issuer, _original, Reference);
                    var opened = await factory.OpenCurrentAsync(Reference, _original.Context, _original.OriginalLifetime, token).ConfigureAwait(false);
                    if (opened.Error is { } error) return AgentResult<AgentExecutionSnapshot>.Failure(error);
                    _session = opened.Value ?? throw Refused();
                    var request = new AgentRunRequest(Reference.AgentId, Reference.DefinitionRevision, _objective,
                        AgentTriggerKind.User, Prepared.RequestId, _original.Context, budget, IdempotencyKey: idempotencyKey);
                    var result = await _session.StartAsync(request, token).ConfigureAwait(false);
                    if (result.Value is { } snapshot) _run = snapshot.Run.AgentRunId;
                    return result;
                }, cancellationToken);
            }
        }
        private Task<AgentResult<AgentExecutionSnapshot>> ActAsync(string run,
            Func<IDenAgentExecutionSession, string, CancellationToken, ValueTask<AgentResult<AgentExecutionSnapshot>>> action,
            CancellationToken cancellationToken)
        {
            if (_run is null || run != _run || _session is null) throw Refused();
            return _owner.RunAsync(this, token => action(_session, run, token).AsTask(), cancellationToken);
        }
        public Task<AgentResult<AgentExecutionSnapshot>> GetAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.GetAsync(id, ct), token);
        public Task<AgentResult<AgentExecutionSnapshot>> ExecuteNextStepAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.ExecuteNextStepAsync(id, ct), token);
        public Task<AgentResult<AgentExecutionSnapshot>> PauseAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.PauseAsync(id, ct), token);
        public Task<AgentResult<AgentExecutionSnapshot>> StopAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.StopAsync(id, ct), token);
        public Task<AgentResult<AgentExecutionSnapshot>> CancelAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.CancelAsync(id, ct), token);
        public Task<AgentResult<AgentExecutionSnapshot>> ResumeAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.ResumeAsync(id, ct), token);
        public Task<AgentResult<AgentExecutionSnapshot>> RecoverAsync(string run, CancellationToken token = default) =>
            ActAsync(run, (session, id, ct) => session.RecoverAsync(id, ct), token);
    }

    private Task<T> RunAsync<T>(Handle sameHandle, Func<CancellationToken, Task<T>> action, CancellationToken caller)
    {
        lock (_admission)
        {
            if (_closing || _retiring || !_contexts.TryGetValue(sameHandle, out var work)) throw Refused();
            return work.RunAsync(token => work.Connection.RunAsync(async ct =>
            {
                T? result = default;
                List<Exception> errors = [];
                try { result = await action(ct).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
                if (sameHandle._original is { } original && work.Issuer is { } issuer)
                    try { await issuer.AuditOriginalTerminalAsync(original.Context, sameHandle._run).ConfigureAwait(false); }
                    catch (Exception error) { Add(errors, error); }
                Throw(errors);
                return result!;
            }, token), caller);
        }
    }

    private HomeAgentExecutionAdmissions Issuer(object sameToken, bool commit = false)
    {
        lock (_admission)
        {
            var issuers = _contexts.Values.Select(work => work.Issuer).Where(issuer => issuer is not null &&
                (commit ? issuer.OwnsOriginalCommit(sameToken) : issuer.OwnsOriginalAuthority(sameToken))).Take(2).ToArray();
            return issuers.Length == 1 ? issuers[0]! : throw Refused();
        }
    }
    public ValueTask DemandCurrentAsync(object authority, Guid conversation, string model, OllamaToolCall? call, CancellationToken token = default) =>
        Issuer(authority).DemandCurrentAsync(authority, conversation, model, call, token);
    public ValueTask<CancellationToken> GetOriginalLifetimeAsync(object authority, CancellationToken token = default) =>
        Issuer(authority).GetOriginalLifetimeAsync(authority, token);
    public ValueTask<OllamaToolCall> GetOriginalDispatchCallAsync(object authority, Guid conversation, string model,
        OllamaToolCall call, CancellationToken token = default) => Issuer(authority).GetOriginalDispatchCallAsync(authority, conversation, model, call, token);
    public ValueTask<object> GetOriginalCommitAdmissionAsync(object authority, Guid conversation, string model,
        OllamaToolCall call, CancellationToken token = default) => Issuer(authority).GetOriginalCommitAdmissionAsync(authority, conversation, model, call, token);
    public ValueTask<AuthenticatedResourceActor> DemandOriginalCommitCurrentAsync(object authority, CancellationToken token = default) =>
        Issuer(authority, commit: true).DemandOriginalCommitCurrentAsync(authority, token);
    public ValueTask CompleteOriginalCallAsync(object authority, Guid conversation, string model, OllamaToolCall original,
        OllamaToolCall dispatch, WorkspaceToolResult? result, Exception? failure, CancellationToken token = default) =>
        Issuer(authority).CompleteOriginalCallAsync(authority, conversation, model, original, dispatch, result, failure, token);

    public Task RetireCurrentContextAsync()
    {
        lock (_admission)
        {
            if (_closing) return _close ?? Task.CompletedTask;
            if (_retire is { IsCompleted: false }) return _retire;
            _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _retire = RetireAsync(start.Task, _contexts.ToArray(), permanent: false, priorRetirements: []);
            _retirements.Add(_retire);
            start.SetResult();
            return _retire;
        }
    }
    public Task CloseAndDrainAsync()
    {
        lock (_admission)
        {
            if (_close is not null) return _close;
            _closing = true; _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = RetireAsync(start.Task, _contexts.ToArray(), permanent: true, priorRetirements: _retirements.ToArray());
            start.SetResult();
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task RetireAsync(Task start, KeyValuePair<Handle, HomeAgentOriginalWork>[] originals, bool permanent, Task[] priorRetirements)
    {
        await start.ConfigureAwait(false);
        var closes = originals.Select(item => item.Value.CloseAsync()).ToArray();
        List<Exception> errors = [];
        foreach (var original in closes)
            try { await original.ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
        foreach (var original in priorRetirements)
            try { await original.ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
        lock (_admission)
        {
            foreach (var original in originals) _contexts.Remove(original.Key);
            if (!permanent && !_closing) { _retiring = false; }
        }
        Throw(errors);
    }
    private static UnauthorizedAccessException Refused() => new("The same original Home Agent host context is unavailable.");
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); }
    private static void Throw(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original Agent work, settlement and cleanup failed.", errors);
    }

}
