using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Individual READ of the SAME privately issued Files selection. This
/// grants neither attachment import nor mutation of the selected original file.</summary>
public sealed partial class HomeCanonicalAssistantAttachmentReadSource : ICanonicalAttachmentOriginalReadSource,
    IOriginalScopedCanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string ReadAction = "assistants.attachments.read";
    public string ResourceKind => "assistant.attachment.source";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly ICanonicalAttachmentOriginalSelectionSource _selections;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<ICanonicalAttachmentOriginalSelection, Read> _issued = new();
    private readonly ConditionalWeakTable<Task, Read> _acquisitions = new();
    private readonly List<Read> _active = [];
    private bool _retiring;
    private Task? _close, _withdrawal;
    public HomeCanonicalAssistantAttachmentReadSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ResourceAuthorizationService resources, HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        ICanonicalAttachmentOriginalSelectionSource selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) ||
            !resources.IsBoundToActorSource(profiles) || !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new UnauthorizedAccessException("The SAME Home store/profile/resource/broker/policy tuple is required.");
        _store = store; _profiles = profiles; _broker = broker; _permissions = permissions; _selections = selections;
    }
    public ICanonicalAttachmentOriginalSelectionSource OriginalSelections => _selections;
    public bool HasOriginalComposition(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ICanonicalAttachmentOriginalSelectionSource selections) =>
        ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles) && ReferenceEquals(_selections, selections);
    public Task<ICanonicalAttachmentOriginalReadAdmission> AcquireOriginalReadWithinSourceAsync(
        ICanonicalAttachmentOriginalSelection selection, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        DemandExternalOriginalJoin(); Read read; TaskCompletionSource start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_issued.TryGetValue(selection, out read!)) return read.Acquisition;
            _active.RemoveAll(value => value.ObserveHealthyClose());
            if (_active.Count >= 128) throw new InvalidOperationException("Settle original attachment reads before reviewing another file.");
            read = new(this, selection); _issued.Add(selection, read); _active.Add(read);
            start = read.Prepare(scope, retain, token); _acquisitions.Add(read.Acquisition, read);
        }
        try { read.Publish(retain); } finally { start.SetResult(); }
        return read.Acquisition;
    }
    public bool IsIssuedOriginalRead(ICanonicalAttachmentOriginalSelection selection, ICanonicalAttachmentOriginalReadAdmission admission)
    {
        lock (_gate) return admission is Read read && ReferenceEquals(read.Owner, this) &&
            _issued.TryGetValue(selection, out var issued) && ReferenceEquals(issued, read) && read.IsAdmitted;
    }
    public bool IsAcknowledgedOriginalReadRefusal(Task original) =>
        _acquisitions.TryGetValue(original, out var read) && read.IsKnownRefusal(original);
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope scope, CancellationToken token) => new(EvaluateWithinOriginalSourceAsync(actor, action, scope, body => body(), _ => { }, token));
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        Read? read;
        lock (_gate) read = action == ReadAction ? _active.SingleOrDefault(value => value.Matches(actor, resource)) : null;
        return read is null ? Task.FromResult(new ResourceAccessDecision(false, "ATTACHMENT_ORIGINAL_SELECTION_REQUIRED", actor.ActorId, resource.Revision, actor.OrganisationId))
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
        if (errors.Count != 0) throw new AggregateException("Original attachment READ withdrawals failed.", errors);
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalPendingReviewWithdrawals(); TaskCompletionSource? start = null; Task result;
        lock (_gate)
        {
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task, _active.ToArray(), _withdrawal!); }
            result = _close;
        }
        start?.SetResult(); return result;
    }
    private static async Task Drain(Task start, Read[] reads, Task withdrawal)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        try { await withdrawal.ConfigureAwait(false); } catch (Exception error) { errors.Add(withdrawal.Exception ?? error); }
        foreach (var read in reads)
        {
            Task? raw = null;
            try { raw = read.CloseAndDrainOriginalAsync(); await raw.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(raw?.Exception ?? error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original Home attachment READs did not close healthy.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}

public sealed class HomeAssistantAttachmentReadActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "assistants" && actionId == HomeCanonicalAssistantAttachmentReadSource.ReadAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
public sealed class HomeAssistantAttachmentReadResourceResolver(Func<HomeCanonicalAssistantAttachmentReadSource> source)
    : IOriginalScopedCanonicalResourceAccessResolver
{
    public string ResourceKind => "assistant.attachment.source";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
        source().EvaluateAsync(actor, action, scope, token);
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => source().EvaluateWithinOriginalSourceAsync(actor, action, resource, scope, retain, token);
    public bool IsBoundToOriginalOwner(HomeCanonicalAssistantAttachmentReadSource actual) => ReferenceEquals(source(), actual);
}
