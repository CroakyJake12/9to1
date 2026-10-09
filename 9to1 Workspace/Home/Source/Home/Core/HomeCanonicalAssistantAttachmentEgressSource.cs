using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Separate manual disclosure of the SAME accepted selected attachment
/// context to one actual Task request/model. Local READ/import does not grant disclosure.</summary>
public sealed partial class HomeCanonicalAssistantAttachmentEgressSource : ICanonicalAttachmentHomeEgressSource,
    IOriginalScopedCanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string DiscloseAction = "assistants.attachments.disclose";
    public string ResourceKind => "assistant.attachment.egress";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly ICanonicalAttachmentEgressProducer _producer;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<ICanonicalAttachmentEgressIntent, Read> _issued = new();
    private readonly ConditionalWeakTable<Task, Read> _acquisitions = new();
    private readonly List<Read> _active = [];
    private readonly object _unexpectedGate = new();
    private readonly List<Exception> _unexpectedCallbacks = [];
    private readonly List<Task> _unexpectedOriginalTasks = [];
    private bool HasHealthyOriginalCallbacks { get { lock (_unexpectedGate) return _unexpectedCallbacks.Count == 0; } }
    private void RememberUnexpectedOriginalCallback(Exception cause)
    { lock (_unexpectedGate) if (!_unexpectedCallbacks.Contains(cause, ReferenceEqualityComparer.Instance)) _unexpectedCallbacks.Add(cause); }
    private void RetainUnexpectedOriginalTask(Task actual)
    { lock (_unexpectedGate) if (!_unexpectedOriginalTasks.Contains(actual, ReferenceEqualityComparer.Instance)) _unexpectedOriginalTasks.Add(actual); }
    private void DemandHealthyOriginalCallbacks()
    {
        lock (_unexpectedGate) if (_unexpectedCallbacks.Count != 0)
            throw new AggregateException("Unexpected original attachment disclosure callbacks remain in Home custody.", _unexpectedCallbacks);
    }
    private bool _retiring;
    private Task? _close, _withdrawal;
    public HomeCanonicalAssistantAttachmentEgressSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ResourceAuthorizationService resources, HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        ICanonicalAttachmentEgressProducer producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) ||
            !resources.IsBoundToActorSource(profiles) || !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new UnauthorizedAccessException("The SAME Home store/profile/resource/broker/policy tuple is required.");
        _store = store; _profiles = profiles; _broker = broker; _permissions = permissions; _producer = producer;
    }
    public ICanonicalAttachmentEgressProducer OriginalProducer => _producer;
    public bool HasOriginalComposition(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ICanonicalAttachmentEgressProducer producer) =>
        ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles) && ReferenceEquals(_producer, producer);
    public Task<ICanonicalAttachmentHomeEgressLease> AcquireOriginalEgressWithinSourceAsync(
        ICanonicalAttachmentEgressIntent selection, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        DemandExternalOriginalJoin(); Read read; TaskCompletionSource start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            DemandHealthyOriginalCallbacks();
            if (_issued.TryGetValue(selection, out read!)) return read.Acquisition;
            _active.RemoveAll(value => value.ObserveHealthyClose());
            if (_active.Count >= 128) throw new InvalidOperationException("Settle original attachment disclosures before reviewing another file.");
            read = new(this, selection); _issued.Add(selection, read); _active.Add(read);
            start = read.Prepare(scope, retain, token); _acquisitions.Add(read.Acquisition, read);
        }
        try { read.Publish(retain); } finally { start.SetResult(); }
        return read.Acquisition;
    }
    public bool IsIssuedOriginalEgressLease(ICanonicalAttachmentEgressIntent selection, ICanonicalAttachmentHomeEgressLease admission)
    {
        lock (_gate) return HasHealthyOriginalCallbacks && admission is Read read && ReferenceEquals(read.Owner, this) &&
            _issued.TryGetValue(selection, out var issued) && ReferenceEquals(issued, read) && read.IsAdmitted;
    }
    public bool IsAcknowledgedOriginalEgressRefusal(Task original) =>
        HasHealthyOriginalCallbacks && _acquisitions.TryGetValue(original, out var read) && read.IsKnownRefusal(original);
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope scope, CancellationToken token) => new(EvaluateWithinOriginalSourceAsync(actor, action, scope, body => body(), _ => { }, token));
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        DemandHealthyOriginalCallbacks();
        Read? read;
        lock (_gate) read = action == DiscloseAction ? _active.SingleOrDefault(value => value.Matches(actor, resource)) : null;
        return read is null ? Task.FromResult(new ResourceAccessDecision(false, "ATTACHMENT_ORIGINAL_DISCLOSURE_REQUIRED", actor.ActorId, resource.Revision, actor.OrganisationId))
            : read.Evaluate(actor, resource, scope, retain, token);
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public Task? OriginalPendingReviewWithdrawalTask { get { lock (_gate) return _withdrawal; } }
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Read[] reads; lock (_gate) reads = _active.ToArray(); foreach (var read in reads) read.DemandExternalOriginalJoin();
    }
    public void RequestOriginalPendingReviewWithdrawals()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_withdrawal is not null) return;
            _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _withdrawal = Withdraw(start.Task, _active.ToArray());
        }
        start.SetResult();
    }
    public void RequestOriginalRetirement() => RequestOriginalPendingReviewWithdrawals();
    private static async Task Withdraw(Task start, Read[] reads)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var read in reads)
        {
            Task? raw = null;
            try { raw = read.Withdraw(); await raw.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(raw?.Exception ?? error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original attachment disclosure withdrawals failed.", errors);
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalPendingReviewWithdrawals(); TaskCompletionSource? start = null; Task result;
        lock (_gate)
        {
            // A late callback cannot turn a settled historical close receipt
            // into evidence that the current owner remains healthy.
            if (_close?.IsCompletedSuccessfully == true) DemandHealthyOriginalCallbacks();
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task, _active.ToArray(), _withdrawal!); }
            result = _close;
        }
        start?.SetResult(); return result;
    }
    private async Task Drain(Task start, Read[] reads, Task withdrawal)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        try { await withdrawal.ConfigureAwait(false); } catch (Exception error) { errors.Add(withdrawal.Exception ?? error); }
        foreach (var read in reads)
        {
            Task? raw = null;
            try { raw = read.CloseAndDrainOriginalAsync(); await raw.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(raw?.Exception ?? error); }
        }
        var observed = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] raw; lock (_unexpectedGate) raw = _unexpectedOriginalTasks.Where(observed.Add).ToArray();
            if (raw.Length == 0) break;
            foreach (var actual in raw)
                try { await actual.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
        }
        lock (_unexpectedGate) errors.AddRange(_unexpectedCallbacks);
        if (errors.Count != 0) throw new AggregateException("Original Home attachment disclosures did not close healthy.", errors.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}

public sealed class HomeAssistantAttachmentEgressActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "assistants" && actionId == HomeCanonicalAssistantAttachmentEgressSource.DiscloseAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
public sealed class HomeAssistantAttachmentEgressResourceResolver(Func<HomeCanonicalAssistantAttachmentEgressSource> source)
    : IOriginalScopedCanonicalResourceAccessResolver
{
    public string ResourceKind => "assistant.attachment.egress";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
        source().EvaluateAsync(actor, action, scope, token);
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => source().EvaluateWithinOriginalSourceAsync(actor, action, resource, scope, retain, token);
    public bool IsBoundToOriginalOwner(HomeCanonicalAssistantAttachmentEgressSource actual) => ReferenceEquals(source(), actual);
}
