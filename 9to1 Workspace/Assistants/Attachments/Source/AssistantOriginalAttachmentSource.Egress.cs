using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.Attachments;

public sealed partial class AssistantOriginalAttachmentSource : ITaskOriginalAttachmentEgressSource, ICanonicalAttachmentEgressProducer
{
    private TaskRunConfiguredCloudAdmissionSource? _egressCloud;
    private ICanonicalAttachmentHomeEgressSource? _egressHome;
    private readonly ConditionalWeakTable<TaskOriginalAttachmentEgressRequest, EgressLease> _egressLeases = new();
    private readonly ConditionalWeakTable<ICanonicalAttachmentEgressIntent, EgressLease> _egressIntents = new();
    private readonly ConditionalWeakTable<Task, EgressLease> _egressAcquisitions = new();
    public void BindOriginalEgressOwners(TaskRunConfiguredCloudAdmissionSource cloud, ICanonicalAttachmentHomeEgressSource home)
    {
        ArgumentNullException.ThrowIfNull(cloud); ArgumentNullException.ThrowIfNull(home);
        if (_inputChat is null || _inputTasks is null || !ReferenceEquals(home.OriginalProducer, this) ||
            !cloud.HasOriginalAttachmentEgressSource(this))
            throw new UnauthorizedAccessException("Retain this SAME Chat input, configured Task domain owner and Home disclosure supplier.");
        lock (_writeGate)
        {
            if (_egressCloud is not null && (!ReferenceEquals(_egressCloud, cloud) || !ReferenceEquals(_egressHome, home)))
                throw new InvalidOperationException("The original attachment disclosure composition cannot be replaced.");
            _egressCloud = cloud; _egressHome = home;
        }
    }
    public bool HasOriginalEgressComposition(TaskRunConfiguredCloudAdmissionSource source) =>
        ReferenceEquals(_egressCloud, source) && _egressHome is not null && ReferenceEquals(_egressHome.OriginalProducer, this) &&
        source.HasOriginalAttachmentEgressSource(this) && _inputChat is not null && _inputTasks is not null &&
        HasOriginalInputComposition(_inputChat, _inputTasks);
    public bool CanUseOriginalTaskAttachmentEgress(IChatOriginalAttachmentInput input) =>
        _egressCloud is not null && HasOriginalEgressComposition(_egressCloud) && IsIssuedOriginalAttachmentInput(input) &&
        DemandInput(input).Read.OriginalConversation.Mode == Haven.Core.HavenMode.Tasks &&
        DemandInput(input).Read.OriginalConversation.Kind == Haven.Core.ConversationKind.Task;
    private sealed class EgressIntent(AssistantOriginalAttachmentSource owner, AttachmentInput input,
        TaskOriginalAttachmentEgressRequest request) : ICanonicalAttachmentEgressIntent
    {
        internal AssistantOriginalAttachmentSource Owner => owner;
        internal AttachmentInput Input => input;
        internal TaskOriginalAttachmentEgressRequest Request => request;
        public Guid OriginalRequestId { get; } = Guid.NewGuid();
        public AuthenticatedResourceActor OriginalHomeActor => input.Read.Actor;
        public Haven.Core.TaskExecutionOwnerBinding OriginalTaskOwner => request.Snapshot.OwnerBinding!;
        public Guid AttemptId => request.Admission.AttemptId;
        public string ProviderId => request.Admission.Lease.Candidate.ProviderId;
        public string ModelId => request.Admission.Lease.Candidate.ModelId;
        public string PayloadSha256 => request.PayloadSha256;
        public ChatOriginalAttachmentLineage OriginalLineage => input.Lineage;
    }
    public bool IsIssuedOriginalEgressIntent(ICanonicalAttachmentEgressIntent intent) =>
        intent is EgressIntent actual && ReferenceEquals(actual.Owner, this) &&
        _egressIntents.TryGetValue(intent, out var lease) && ReferenceEquals(lease.Intent, intent) &&
        IsIssuedOriginalAttachmentInput(actual.Input) && _egressCloud is not null &&
        _egressCloud.IsIssuedOriginalAttachmentEgressRequest(actual.Request, this);
    public string GetOriginalEgressIntentDigest(ICanonicalAttachmentEgressIntent intent)
    {
        if (!IsIssuedOriginalEgressIntent(intent)) throw new UnauthorizedAccessException("The SAME actual input owner must issue this disclosure intent.");
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { intent.OriginalRequestId, intent.OriginalHomeActor, intent.OriginalTaskOwner, intent.AttemptId,
          intent.ProviderId, intent.ModelId, intent.PayloadSha256, intent.OriginalLineage })));
    }
    public Task ValidateOriginalEgressIntentWithinSourceAsync(ICanonicalAttachmentEgressIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Run(scope, retain, async source =>
    {
        if (!IsIssuedOriginalEgressIntent(intent) || intent is not EgressIntent actual)
            throw new UnauthorizedAccessException("The original selected attachment disclosure is unavailable.");
        await ValidateEgressRequest(actual.Request, actual.Input, source, token).ConfigureAwait(false); return true;
    });
    private async Task ValidateEgressRequest(TaskOriginalAttachmentEgressRequest request, AttachmentInput input,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        source.Run(() =>
        {
            if (_egressCloud is null || !HasOriginalEgressComposition(_egressCloud) ||
                !_egressCloud.IsIssuedOriginalAttachmentEgressRequest(request, this) ||
                _inputChat is null || !_inputChat.IsIssuedOriginalAttachmentInvocation(request.Invocation, this) ||
                !ReferenceEquals(request.Invocation.Input, input) ||
                !request.Admission.Lease.Candidate.UsesCloud || request.PayloadSha256.Length != 64 ||
                request.Snapshot.OwnerBinding is not { } owner || request.Admission.Lease.Owner != owner ||
                request.Context.TaskId != request.Snapshot.TaskId || request.Context.ExecutionId != request.Snapshot.ExecutionId ||
                request.Context.AttemptId != request.Admission.AttemptId || request.Context.ContextId != owner.ContextId ||
                _inputChat.ObserveOriginalAttachmentAcceptance(request.Invocation.Request, input, this) is null)
                throw new UnauthorizedAccessException("The live Chat input, issued Task request and independently selected model must match.");
        });
        _ = await ReadInputSnapshot(input, request.Invocation.Request, request.Context, source, token).ConfigureAwait(false);
        source.Run(() =>
        {
            if (!_egressCloud!.IsIssuedOriginalAttachmentEgressRequest(request, this))
                throw new UnauthorizedAccessException("The actual model request changed during protected attachment validation.");
        });
    }
    public Task<ITaskOriginalAttachmentEgressLease> AcquireOriginalEgressWithinSourceAsync(TaskOriginalAttachmentEgressRequest request,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        EgressLease lease; TaskCompletionSource start;
        lock (_writeGate)
        {
            if (_egressLeases.TryGetValue(request, out lease!)) return lease.Acquisition;
            var input = DemandInput(request.Invocation.Input);
            lease = new(this, new(this, input, request)); _egressLeases.Add(request, lease); _egressIntents.Add(lease.Intent, lease);
            start = lease.Prepare(scope, retain, token); _egressAcquisitions.Add(lease.Acquisition, lease);
        }
        start.SetResult(); return lease.Acquisition;
    }
    public bool IsIssuedOriginalEgressLease(TaskOriginalAttachmentEgressRequest request, ITaskOriginalAttachmentEgressLease lease) =>
        lease is EgressLease actual && ReferenceEquals(actual.Owner, this) && _egressLeases.TryGetValue(request, out var issued) &&
        ReferenceEquals(actual, issued) && actual.Acquisition.IsCompletedSuccessfully && actual.IsLive;
    public bool IsAcknowledgedOriginalEgressRefusal(Task actualAcquisition) =>
        _egressAcquisitions.TryGetValue(actualAcquisition, out var lease) && lease.IsKnownRefusal(actualAcquisition);
    private sealed class EgressLease(AssistantOriginalAttachmentSource owner, EgressIntent intent) : ITaskOriginalAttachmentEgressLease
    {
        internal AssistantOriginalAttachmentSource Owner => owner;
        internal EgressIntent Intent => intent;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<ITaskOriginalAttachmentEgressLease> _acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<AssistantMemoryOriginals.Scope> _scopes = [];
        private readonly List<Task> _commands = [];
        private ICanonicalAttachmentHomeEgressLease? _home;
        private Task<ICanonicalAttachmentHomeEgressLease>? _homeAcquisition;
        private Task? _driver, _publication, _homeClose;
        private bool _closed, _invoked, _refusalHealthy;
        private Exception? _knownRefusal;
        internal bool IsKnownRefusal(Task actual) => ReferenceEquals(actual, Acquisition) && _driver?.IsCompletedSuccessfully == true && _refusalHealthy &&
            _knownRefusal is not null && actual.Exception is { InnerExceptions.Count: 1 } errors &&
            ReferenceEquals(errors.InnerExceptions[0], _knownRefusal);
        internal Task<ITaskOriginalAttachmentEgressLease> Acquisition => _acquired.Task;
        internal bool IsLive { get { lock (_gate) return !_closed && !_invoked; } }
        internal TaskCompletionSource Prepare(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _driver = owner._originals.Admit(async () => { await start.Task.ConfigureAwait(false); await Drive(scope, retain, token).ConfigureAwait(false); return true; });
            _publication = owner._originals.AdmitOriginalCleanup(_driver, async () =>
            {
                try
                {
                    await _driver.ConfigureAwait(false);
                    if (_knownRefusal is not null && _refusalHealthy) _acquired.TrySetException(_knownRefusal);
                }
                catch (Exception cause)
                {
                    var actual = _driver.Exception ?? cause; _acquired.TrySetException(actual); throw;
                }
            });
            return start;
        }
        private async Task Drive(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var source = owner._originals.CreateScope(scope, retain); lock (_gate) _scopes.Add(source);
            var failures = new List<Exception>();
            try
            {
                source.Run(() => retain(Acquisition));
                await owner.ValidateEgressRequest(intent.Request, intent.Input, source, token).ConfigureAwait(false);
                var home = owner._egressHome ?? throw new NotSupportedException("The actual Home disclosure supplier is unavailable.");
                try
                {
                    await source.Read(() => _homeAcquisition = home.AcquireOriginalEgressWithinSourceAsync(intent, source.Run, source.Retain, token),
                        actual => _home = actual).ConfigureAwait(false);
                }
                catch (Exception cause)
                {
                    var known = false;
                    source.Run(() => known = _homeAcquisition is not null && home.IsAcknowledgedOriginalEgressRefusal(_homeAcquisition));
                    if (!known || _homeAcquisition is null || _homeAcquisition.Exception is not { InnerExceptions.Count: 1 } failure ||
                        !ReferenceEquals(cause, failure.InnerExceptions[0])) throw;
                    var acknowledged = false;
                    source.Run(() => acknowledged = source.AcknowledgeOriginalRefusal(_homeAcquisition, home.IsAcknowledgedOriginalEgressRefusal));
                    if (!acknowledged) throw;
                    _knownRefusal = new UnauthorizedAccessException("Home declined or withdrew this request's attachment disclosure.");
                }
                if (_knownRefusal is not null)
                {
                    await source.JoinAsync().ConfigureAwait(false);
                    _refusalHealthy = true; return;
                }
                source.Run(() =>
                {
                    if (!home.IsIssuedOriginalEgressLease(intent, _home!)) throw new UnauthorizedAccessException("The SAME Home supplier did not issue this disclosure.");
                });
                await source.JoinAsync().ConfigureAwait(false);
                _acquired.TrySetResult(this);
                await _release.Task.ConfigureAwait(false);
            }
            catch (Exception cause) { failures.Add(cause); }
            // Acquisition failure still captures and closes any actual late lease.
            if (_homeAcquisition?.IsCompletedSuccessfully == true) _home ??= _homeAcquisition.GetAwaiter().GetResult();
            Task[] commands; lock (_gate) { _closed = true; commands = _commands.ToArray(); }
            foreach (var raw in commands) try { await raw.ConfigureAwait(false); } catch (Exception cause) { failures.Add(raw.Exception ?? cause); }
            var cleanup = owner._originals.CreateCleanupScope(body => body(), _ => { });
            if (_home is not null)
                try { await cleanup.ReadCleanup(() => _homeClose = _home.CloseAndDrainOriginalAsync()).ConfigureAwait(false); }
                catch (Exception cause) { failures.Add(_homeClose?.Exception ?? cause); }
            try { await cleanup.JoinAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            AssistantMemoryOriginals.Scope[] sources; lock (_gate) sources = _scopes.ToArray();
            foreach (var actual in sources) try { await actual.JoinAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0)
            {
                var actual = new AggregateException("The original attachment disclosure or independent cleanup did not settle.", failures);
                _acquired.TrySetException(actual); throw actual;
            }
            if (!_acquired.Task.IsCompleted) _acquired.TrySetException(new InvalidOperationException("No actual disclosure lease was published."));
        }
        public Task ValidateOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            lock (_gate)
            {
                if (!Acquisition.IsCompletedSuccessfully || !IsLive || _home is null)
                    throw new UnauthorizedAccessException("The original accepted disclosure lease is not live.");
                var raw = owner.Run(scope, retain, async source =>
                {
                    // Release a previous finite entry before any Home/domain reread.
                    // Revalidation may run more than once; each entry remains independently owned.
                    await source.ReadCleanup(() => _home.ReleaseOriginalInvocationEntryWithinSourceAsync(source.Run, source.Retain)).ConfigureAwait(false);
                    await owner.ValidateEgressRequest(intent.Request, intent.Input, source, token).ConfigureAwait(false);
                    await source.Read(() => _home.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                    await source.Read(() => _home.AcquireOriginalInvocationEntryWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                    return true;
                });
                _commands.Add(raw); return raw;
            }
        }
        public T RunOriginalInvocation<T>(Func<T> originalRawStart)
        {
            var source = owner._originals.CreateScope(body => body(), _ => { }); T result = default!;
            lock (_gate) _scopes.Add(source);
            source.Run(() =>
            {
                lock (_gate)
                {
                    if (!IsLive || _home is null || _commands.Count == 0 || _commands.Any(raw => !raw.IsCompletedSuccessfully) ||
                        !owner.IsIssuedOriginalEgressIntent(intent))
                        throw new UnauthorizedAccessException("The exact current attachment disclosure entry is required for this raw start.");
                    result = _home.RunOriginalInvocation(() => { _invoked = true; return originalRawStart(); });
                }
            });
            return result;
        }
        public Task? OriginalClose { get { lock (_gate) return _closed ? _publication : null; } }
        public Task CloseAndDrainOriginalAsync()
        {
            owner._originals.DemandExternalOriginalRetirementJoin();
            lock (_gate) { _closed = true; _release.TrySetResult(); return _publication ?? throw new InvalidOperationException("The actual source driver/publication join was not retained."); }
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
}
