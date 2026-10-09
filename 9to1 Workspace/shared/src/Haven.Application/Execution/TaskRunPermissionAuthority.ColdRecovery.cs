using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority : ITaskRunColdOwnerAuthority
{
    private ITaskRunColdRecoveryJournal? _coldJournal;
    private ITaskRunColdContextAuthority? _coldContext;
    private ITaskRunColdAuthoritySourceScope? _coldSource;
    private bool _coldActorSourceBound;
    private readonly HashSet<ColdOwnerAdmission> _coldAdmissions = [];
    private readonly Dictionary<RenewalWork, ColdSourceCallbacks> _coldWorkSources = [];

    /// <summary>Trusted composition only, before any ordinary/cold admission. The actual protected
    /// journal/context producer and SAME Task actor source must validate every private reference.</summary>
    public void ConfigureOriginalColdRecoverySources(ITaskRunColdRecoveryJournal originalJournal,
        ITaskRunColdContextAuthority originalContext)
    {
        ArgumentNullException.ThrowIfNull(originalJournal); ArgumentNullException.ThrowIfNull(originalContext);
        if (!ReferenceEquals(originalJournal, originalContext) || originalJournal is not ITaskRunColdAuthoritySourceScope scoped ||
            !scoped.HasOriginalTaskActorSource(_actors))
            throw new InvalidOperationException("The SAME protected journal/context and actual Task actor source with original callback custody are required.");
        lock (_sync)
        {
            DemandOriginalAdmissionOpen();
            if (_coldJournal is not null || _owners.Count != 0 || _coldAdmissions.Count != 0 || _renewalWork.Count != 0)
                throw new InvalidOperationException("Configure cold sources once before actual Task admission.");
            _coldJournal = originalJournal; _coldContext = originalContext; _coldSource = scoped; _coldActorSourceBound = true;
        }
    }
    // Captured trusted composition identity only. No actor read, capsule readiness or permission.
    public bool HasOriginalColdRecoveryComposition(ITaskRunColdRecoveryJournal sameJournal,
        ITaskRunColdContextAuthority sameContext)
    {
        lock (_sync) return _coldActorSourceBound && _coldSource is not null &&
            ReferenceEquals(_coldJournal, sameJournal) && ReferenceEquals(_coldContext, sameContext);
    }
    private void DemandColdConfigured()
    {
        if (_coldJournal is null || _coldContext is null || _coldSource is null ||
            !ReferenceEquals(_coldJournal, _coldContext) || !_coldActorSourceBound)
            throw new InvalidOperationException("Cold owner recovery requires the genuine protected same-store journal/context/actor producer; it is unconfigured.");
    }
    public ValueTask<ITaskRunColdOwnerAdmission> PrepareOriginalColdOwnerAsync(
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext,
        TaskExecutionSnapshot actualExpected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameClaim); ArgumentNullException.ThrowIfNull(sameContext); ArgumentNullException.ThrowIfNull(actualExpected);
        DemandColdConfigured();
        return new(StartColdWork(null, token, (work, sources) => PrepareColdBodyAsync(work, sources, sameClaim, sameContext, actualExpected, token)));
    }
    private async Task<ITaskRunColdOwnerAdmission> PrepareColdBodyAsync(RenewalWork work, CloudflareOriginalTaskLedger sources,
        ITaskRunColdJournalClaim claim, ITaskRunColdContextLease context, TaskExecutionSnapshot expected, CancellationToken token)
    {
        if (!_coldJournal!.IsIssuedOriginalEntry(claim.OriginalEntry) || !_coldContext!.IsIssuedOriginal(context, claim) ||
            !ReferenceEquals(context.OriginalClaim, claim)) throw new UnauthorizedAccessException("SAME privately issued journal claim and fresh context are required.");
        TaskRunColdRecoveryBoundary.DemandRestorableBoundary(claim.OriginalEntry.Capsule, expected);
        if (FingerprintCold(expected) != FingerprintCold(claim.OriginalExpected)) throw new UnauthorizedAccessException("The exact current expected snapshot is required.");
        var old = expected.OwnerBinding ?? throw new UnauthorizedAccessException("The original actor audit binding is unavailable.");
        if (old.TaskId != expected.TaskId || old.ContextId != expected.ContextId || old.ExecutionId != expected.ExecutionId)
            throw new UnauthorizedAccessException("The original actor binding belongs to another Task/context/run.");
        DemandStableRenewalIdentity(old, context.CurrentActor);
        var scope = new ColdOwnerAdmission(this, claim, context, expected, old, old with
        {
            AuthenticationRevision = context.CurrentActor.AuthenticationRevision,
            AuthorizationReceiptReference = "task-cold-activation:" + Guid.NewGuid().ToString("N")
        });
        await ValidateColdBeforeCasAsync(work, sources, scope, token).ConfigureAwait(false);
        lock (_sync)
        {
            DemandRenewalOpen();
            // Map absence is only a collision check AFTER genuine domain/journal/actor policy proof.
            // Any retained same-process owner, even sealed, requires its existing owning lifecycle.
            if (_owners.ContainsKey(expected.TaskId)) throw new UnauthorizedAccessException("A retained original Task owner cannot be replaced by cold recovery.");
            if (_owners.Count >= 1024 || _coldAdmissions.Count >= 128) throw new InvalidOperationException("Retained cold authority custody is full.");
            var staged = new Owner(scope.NextOwner); staged.Activation.Seal();
            scope.StagedOwner = staged; scope.StagedActivation = staged.Activation;
            _owners.Add(expected.TaskId, staged); // Sealed staging: RequireOwner/attempt/tool admission still refuses.
            scope.Work.Add(work); _coldAdmissions.Add(scope);
        }
        return scope;
    }
    private async Task ValidateColdBeforeCasAsync(RenewalWork work, CloudflareOriginalTaskLedger sources,
        ColdOwnerAdmission scope, CancellationToken token)
    {
        var callback = ColdCaller(sources); Action<Task> retain = raw => RetainColdRaw(work, sources, raw);
        await sources.AwaitAsync(sources.Invoke(() => _coldSource!.ValidateOriginalClaimWithinSourceAsync(scope.OriginalClaim,
            scope.Expected, callback, retain, token))).ConfigureAwait(false);
        await sources.AwaitAsync(sources.Invoke(() => _coldSource!.ValidateOriginalContextWithinSourceAsync(scope.OriginalContext,
            scope.Expected, callback, retain, token).AsTask())).ConfigureAwait(false);
        if (!_coldContext!.IsIssuedOriginal(scope.OriginalContext, scope.OriginalClaim)) throw new UnauthorizedAccessException("The genuine context scope retired.");
        await ValidateColdAcceptedBoundaryAsync(work, sources, scope, token).ConfigureAwait(false);
        await ValidateColdProjectBeforeCasAsync(work, sources, scope, token).ConfigureAwait(false);
        await ReadColdActivationPolicyForScopeAsync(sources, scope, token).ConfigureAwait(false);
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if(sources.OriginalErrors.Count!=0) throw new AggregateException("Actual cold source validation failed.",sources.OriginalErrors);
        // The actual configured actor is read LAST after journal/context/model/policy awaits.
        var actor = await ReadColdActivationActorAsync(work, sources, scope, token).ConfigureAwait(false);
        if (actor is null || actor != scope.OriginalContext.CurrentActor || !ValidActor(actor)) throw new UnauthorizedAccessException("The genuine fresh actor changed during cold owner validation.");
        DemandStableRenewalIdentity(scope.PreviousOwner, actor);
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            DemandRenewalOpen();
            if (scope.StagedOwner is not null) DemandColdStaging(scope, requireOpenScope: true);
            else if (_owners.ContainsKey(scope.Expected.TaskId)) throw new UnauthorizedAccessException("An existing Task owner blocks cold staging.");
        }
    }
    private async Task ReadColdPolicyAsync(CloudflareOriginalTaskLedger sources, TaskRunColdChatInput input, CancellationToken token)
    {
        var policy = sources.Invoke(() => new ModelCataloguePolicy(AllowLocal: true,
            AllowRemote: !RuntimeSafetyState.IsSafeMode && !_privacy.Current.LocalOnlyMode));
        // Strict cold admission observes each original provider acquisition itself. The ordinary
        // registry catalogue intentionally tolerates unavailable providers; that await proxy is
        // not original-task custody and cannot authorize this cold owner boundary.
        var providers = sources.Invoke(() => _providers.Providers.Where(provider =>
            provider.IsLocal ? policy.AllowLocal : policy.AllowRemote).ToArray());
        var models = new List<ProviderModelDescriptor>();
        foreach (var actualProvider in providers)
        {
            token.ThrowIfCancellationRequested();
            Task<IReadOnlyList<ProviderModelDescriptor>>? original = null;
            try
            {
                // Capture the exact raw Task inside the finite callback, before caller-scope exit.
                _ = sources.Invoke(() => { original = actualProvider.GetModelsAsync(token); return original; });
                var actualModels = await sources.AwaitAsync(original!).ConfigureAwait(false);
                models.AddRange(sources.Invoke(() => actualModels
                    .Where(model => model.ProviderId.Equals(actualProvider.Id, StringComparison.OrdinalIgnoreCase) &&
                        (model.IsLocal && actualProvider.IsLocal ? policy.AllowLocal : policy.AllowRemote))
                    .Select(model => actualProvider.IsLocal ? model : model with { IsLocal = false }).ToArray()));
            }
            catch (Exception cause) { sources.Capture(original, cause); }
        }
        if (sources.OriginalErrors.Count != 0)
            throw new AggregateException("An actual cold catalogue original failed; no provider failure is discarded.", sources.OriginalErrors);
        var distinctModels = models.GroupBy(model => model.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First());
        var selected = distinctModels.Where(model => model.Matches(input.Model.Name)).Take(2).ToArray();
        if (selected.Length != 1) throw new UnauthorizedAccessException("The original input model is missing or ambiguous in the actual current catalogue.");
        var model = selected[0]; var provider = sources.Invoke(() => _providers.Find(model.ProviderId))
            ?? throw new UnauthorizedAccessException("The actual current provider is unavailable.");
        var config = await sources.AwaitAsync(sources.Invoke(() => _configurations.GetAsync(model.ProviderId, token))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual current provider configuration is unavailable.");
        var required = (input.ExplicitCapabilities ?? []).Append(ToolCapability.Text).Distinct().ToArray();
        if (required.Length > 128 || required.Any(value => !Enum.IsDefined(value) || !model.Supports(value)) ||
            !config.IsEnabled || config.Id != provider.Id || config.IsLocal != provider.IsLocal || model.IsLocal != provider.IsLocal ||
            sources.Invoke(() => (RuntimeSafetyState.IsSafeMode || _privacy.Current.LocalOnlyMode) && !provider.IsLocal))
            throw new UnauthorizedAccessException("Current model/configuration/privacy policy denies cold input admission.");
        // Only declared typed browser/computer capabilities have a direct restriction mapping.
        // Future file/command/MCP operations still need fresh normal preparation/final effect policy.
        var restricted = new List<RestrictedModelCapability>();
        if (required.Contains(ToolCapability.Browser)) restricted.Add(RestrictedModelCapability.BrowserAutomation);
        if (required.Contains(ToolCapability.ComputerUse)) restricted.Add(RestrictedModelCapability.ComputerUse);
        if (restricted.Count != 0)
        {
            var actualPolicy = await sources.AwaitAsync(sources.Invoke(() =>
                _modelPermissions.GetOriginalPolicyAsync(token))).ConfigureAwait(false);
            foreach (var capability in restricted)
                if (!ModelPermissionEvaluator.Evaluate(actualPolicy, model, capability, acrossMesh: false).Allowed)
                    throw new UnauthorizedAccessException("Current central model permission denies the declared typed capability.");
        }
    }
    public bool IsIssuedOriginalColdOwner(ITaskRunColdOwnerAdmission sameAdmission,
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext)
    {
        lock (_sync) return sameAdmission is ColdOwnerAdmission scope && ReferenceEquals(scope.Issuer, this) &&
            _coldAdmissions.Contains(scope) && ReferenceEquals(scope.OriginalClaim, sameClaim) && ReferenceEquals(scope.OriginalContext, sameContext);
    }
    private ColdOwnerAdmission RequireCold(ITaskRunColdOwnerAdmission actual)
    {
        if (actual is not ColdOwnerAdmission scope || !ReferenceEquals(scope.Issuer, this) || !_coldAdmissions.Contains(scope))
            throw new UnauthorizedAccessException("SAME private cold owner admission required.");
        return scope;
    }
    private void DemandColdStaging(ColdOwnerAdmission scope, bool requireOpenScope)
    {
        if (requireOpenScope && scope.Closing || scope.Activated || scope.StagedOwner is null || scope.StagedActivation is null ||
            !ReferenceEquals(_owners.GetValueOrDefault(scope.Expected.TaskId), scope.StagedOwner) ||
            !ReferenceEquals(scope.StagedOwner.Activation, scope.StagedActivation) || !scope.StagedActivation.IsSealed)
            throw new UnauthorizedAccessException("The exact sealed cold staging owner is unavailable.");
    }
    private sealed class ColdOwnerAdmission(TaskRunPermissionAuthority issuer, ITaskRunColdJournalClaim claim,
        ITaskRunColdContextLease context, TaskExecutionSnapshot expected, TaskExecutionOwnerBinding previous,
        TaskExecutionOwnerBinding next) : ITaskRunColdOwnerAdmission
    {
        internal TaskRunPermissionAuthority Issuer { get; } = issuer;
        internal TaskExecutionSnapshot Expected { get; } = expected;
        public ITaskRunColdJournalClaim OriginalClaim { get; } = claim;
        public ITaskRunColdContextLease OriginalContext { get; } = context;
        public TaskExecutionOwnerBinding PreviousOwner { get; } = previous;
        public TaskExecutionOwnerBinding NextOwner { get; } = next;
        internal Owner? StagedOwner; internal OwnerActivation? StagedActivation;
        internal readonly List<RenewalWork> Work = [];
        internal readonly CloudflareOriginalTaskLedger ClosingSources = new();
        internal readonly SemaphoreSlim Commit = new(1, 1);
        internal readonly List<ColdPin> Pins = [];
        internal Task? Close, Activation; internal bool Closing, Activated;
        internal ITaskRunColdJournalAcknowledgment? Acknowledgment;
        internal ITaskRunColdAcceptedBoundarySource? AcceptedBoundaryIssuer;
        internal Task? AcceptedBoundaryValidation;
        internal ITaskRunColdOriginalProjectBoundarySource? ProjectBoundaryIssuer;
        internal Task? ProjectBoundaryValidation;
        public ValueTask RevalidateAsync(CancellationToken token) => new(Issuer.StartColdWork(this, token, async (work, sources) =>
        { await Issuer.ValidateColdBeforeCasAsync(work, sources, this, token).ConfigureAwait(false); return true; }));
        public ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken token) => new(Issuer.StartColdWork(this, token, async (_, sources) =>
        {
            await sources.AwaitAsync(Commit.WaitAsync(token)).ConfigureAwait(false); var transferred = false;
            try
            {
                lock (Issuer._sync) { Issuer.DemandRenewalOpen(); Issuer.DemandColdStaging(this, true); var pin = new ColdPin(Issuer, this); Pins.Add(pin); transferred = true; return (IAsyncDisposable)pin; }
            }
            finally { if (!transferred) Commit.Release(); }
        }));
        public ValueTask DisposeAsync() { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); Issuer.DemandExternalRenewalJoin(); return new(Issuer.CloseColdScope(this)); }
    }
    private sealed class ColdPin(TaskRunPermissionAuthority issuer, ColdOwnerAdmission scope) : IAsyncDisposable
    {
        internal Task? Close;
        public ValueTask DisposeAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(scope); issuer.DemandExternalRenewalJoin(); TaskCompletionSource? start = null; Task actual;
            lock (issuer._sync) { if (Close is null) { start = NewRenewalGate(); Close = ClosePublishedAsync(start.Task); } actual = Close; }
            start?.SetResult(); return new(actual);
        }
        private async Task ClosePublishedAsync(Task begin)
        { await begin.ConfigureAwait(false); scope.Commit.Release(); }
    }
    private Task CloseColdScope(ColdOwnerAdmission scope)
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_sync) { _ = RequireCold(scope); if (scope.Close is null) { scope.Closing = true; start = NewRenewalGate(); scope.Close = CloseColdPublishedAsync(scope, start.Task); } actual = scope.Close; }
        start?.SetResult(); return actual;
    }
    private async Task CloseColdPublishedAsync(ColdOwnerAdmission scope, Task begin)
    {
        await begin.ConfigureAwait(false); using var scopePhase = CloudflareOriginalExecutionGuard.EnterOriginal(scope);
        var previousPhase = _renewalExecuting.Value; var phase = new RenewalPhase(this, previousPhase); _renewalExecuting.Value = phase;
        try
        {
        var sources = scope.ClosingSources; sources.BindOriginalOwner(scope);
        sources.BindOriginalCallerCallback(body => InvokeRenewalPhysical(() => { body(); return true; })); RenewalWork[] work;
        lock (_sync) work = scope.Work.ToArray();
        foreach (var original in work)
            try { await sources.AwaitAsync(original.Driver).ConfigureAwait(false); } catch (Exception cause) { sources.Capture(original.Driver,cause); }
        await sources.AwaitAsync(sources.Invoke(() => scope.Commit.WaitAsync())).ConfigureAwait(false);
        try
        {
            ColdPin[] pins; lock (_sync) pins = scope.Pins.ToArray();
            foreach (var pin in pins)
                if (pin.Close is { } actual) try { await sources.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { sources.Capture(actual,cause); }
            foreach (var original in work)
            {
                Task[] raws; lock (_sync) raws = original.Raw.ToArray();
                foreach (var raw in raws) try { await sources.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { sources.Capture(raw,cause); }
                foreach (var cause in original.Errors) sources.Retain(cause);
            }
        }
        finally { scope.Commit.Release(); }
        // Context and Claim are borrowed: Data independently joins their actual closes before activation.
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (sources.OriginalErrors.Count != 0) throw new AggregateException("Cold authority original validation/pin cleanup failed.", sources.OriginalErrors);
        }
        finally { phase.Retire(); _renewalExecuting.Value = previousPhase; }
    }
    private async Task JoinOriginalColdScopesAsync(List<Exception> failures)
    {
        ColdOwnerAdmission[] scopes; lock (_sync) scopes = _coldAdmissions.ToArray();
        // All admitted validation drivers have already been joined by the owning global drain.
        // Data owns borrowed context/claim closes; an outstanding genuine commit pin remains held.
        var closes = new List<Task>();
        foreach (var scope in scopes)
            try { closes.Add(CloseColdScope(scope)); } catch (Exception cause) { AddRenewalCause(failures, cause); }
        foreach (var actual in closes) await JoinRenewalTaskAsync(actual, failures).ConfigureAwait(false);
        foreach (var scope in scopes)
            foreach (var actual in scope.ClosingSources.OriginalTasks) await JoinRenewalTaskAsync(actual, failures).ConfigureAwait(false);
    }
    public ValueTask ActivateAcknowledgedOriginalColdOwnerAsync(ITaskRunColdOwnerAdmission sameAdmission,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, CancellationToken token)
    {
        DemandExternalRenewalJoin(); DemandColdConfigured(); TaskCompletionSource? start = null; Task actual;
        lock (_sync)
        {
            var scope = RequireCold(sameAdmission); DemandRenewalOpen();
            if (scope.Activation is not null)
            { if (!ReferenceEquals(scope.Acknowledgment,sameAcknowledgment)) throw new UnauthorizedAccessException("The retained cold ACK cannot be replaced."); return new(scope.Activation); }
            scope.Acknowledgment = sameAcknowledgment; start = NewRenewalGate();
            var work = ReserveRenewalWork(null, addToScopeWork:false);
            scope.Activation = RunRenewalWorkAsync(work,start.Task,() => ActivateColdBodyAsync(work,scope,sameAcknowledgment,token)); work.Driver=scope.Activation; actual=scope.Activation;
        }
        start.SetResult(); return new(actual);
    }
    private async Task<bool> ActivateColdBodyAsync(RenewalWork work, ColdOwnerAdmission scope,
        ITaskRunColdJournalAcknowledgment acknowledgment, CancellationToken token)
    {
        var sources = NewColdSources(work);
        await RunColdBodyToSettlementAsync(work,sources,async () =>
        {
            lock (_sync)
            {
                DemandColdStaging(scope,false);
                if (scope.Close?.IsCompletedSuccessfully != true || scope.Pins.Any(pin => pin.Close?.IsCompletedSuccessfully != true) ||
                    scope.Work.Any(original => !original.Driver.IsCompletedSuccessfully || original.Errors.Count != 0))
                    throw new UnauthorizedAccessException("Actual successful admission/pin validation and cleanup is required before cold activation.");
            }
            if (!_coldJournal!.IsIssuedOriginalAcknowledgment(acknowledgment,scope.OriginalClaim)) throw new UnauthorizedAccessException("SAME private actual binding CAS ACK required.");
            var acknowledged=acknowledgment.AcknowledgedTask;
            var intended=scope.Expected with {OwnerBinding=scope.NextOwner, PersistenceRevision=checked(scope.Expected.PersistenceRevision+1), UpdatedAt=acknowledged.UpdatedAt};
            if (FingerprintCold(intended)!=FingerprintCold(acknowledged)) throw new UnauthorizedAccessException("Cold binding CAS changed the original Task/run/input/history.");
            var callback=ColdCaller(sources); Action<Task> retain=raw=>RetainColdRaw(work,sources,raw);
            await sources.AwaitAsync(sources.Invoke(()=>_coldSource!.ValidateOriginalAcknowledgmentWithinSourceAsync(acknowledgment,callback,retain,token))).ConfigureAwait(false);
            DemandColdAcceptedBoundaryProof(scope);
            DemandColdProjectBoundaryProof(scope);
            await ReadColdActivationPolicyForScopeAsync(sources,scope,token).ConfigureAwait(false);
            await sources.AwaitAsync(sources.Invoke(()=>_coldSource!.ValidateOriginalClosedContextWithinSourceAsync(scope.OriginalContext,acknowledgment,callback,retain,token).AsTask())).ConfigureAwait(false);
            await ValidateColdClosedProjectAsync(work, sources, scope, acknowledgment, token).ConfigureAwait(false);
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if(sources.OriginalErrors.Count!=0) throw new AggregateException("Actual cold activation sources failed.",sources.OriginalErrors);
            var actor=await ReadColdActivationActorAsync(work,sources,scope,token).ConfigureAwait(false);
            if(actor is null || actor!=scope.OriginalContext.CurrentActor || !ValidActor(actor)) throw new UnauthorizedAccessException("The actual fresh actor retired after final journal/input policy reads.");
            token.ThrowIfCancellationRequested();
            return true;
        }).ConfigureAwait(false);
        lock (_sync)
        {
            DemandRenewalOpen(); DemandColdStaging(scope,false);
            if(sources.OriginalErrors.Count!=0 || sources.OriginalTasks.Any(raw => !raw.IsCompletedSuccessfully))
                throw new UnauthorizedAccessException("Cold source faults or held originals remain retained.");
            scope.StagedOwner!.PublishActivation(scope.StagedActivation!,new(scope.NextOwner)); scope.Activated=true;
        }
        return true;
    }
    private Task<T> StartColdWork<T>(ColdOwnerAdmission? scope,CancellationToken token,
        Func<RenewalWork,CloudflareOriginalTaskLedger,Task<T>> body)
    {
        token.ThrowIfCancellationRequested(); TaskCompletionSource start; RenewalWork work; Task<T> actual;
        lock(_sync)
        {
            if(scope is not null) { _=RequireCold(scope); DemandColdStaging(scope,true); }
            work=ReserveRenewalWork(null,true); start=NewRenewalGate();
            actual=RunRenewalWorkAsync(work,start.Task,()=> { var sources=NewColdSources(work); return RunColdBodyToSettlementAsync(work,sources,()=>body(work,sources)); });
            work.Driver=actual; scope?.Work.Add(work);
        }
        start.SetResult(); return actual;
    }
    private sealed class ColdSourceCallbacks(TaskRunPermissionAuthority issuer, RenewalWork work,
        CloudflareOriginalTaskLedger sources, RenewalPhase phase)
    {
        private readonly object _gate = new(); private int _active; private bool _sealed;
        private readonly TaskCompletionSource _drained = NewRenewalGate();
        internal void Invoke(Action body)
        {
            lock (_gate)
            {
                if (_sealed || !phase.Live) throw new InvalidOperationException("The original cold source callback phase has retired.");
                _active++;
            }
            try
            {
                issuer.InvokeRenewalPhysical(() =>
                {
                    try { body(); }
                    finally { foreach (var raw in sources.OriginalTasks) issuer.RetainColdRaw(work, sources, raw); }
                    return true;
                });
            }
            finally
            {
                bool done; lock (_gate) { _active--; done = _sealed && _active == 0; }
                if (done) _drained.TrySetResult();
            }
        }
        internal Task SealAndJoinFiniteCallbacks()
        {
            bool done; lock (_gate) { _sealed = true; done = _active == 0; }
            if (done) _drained.TrySetResult();
            return _drained.Task; // Finite callback admission barrier; each returned raw Task is joined separately.
        }
    }
    private CloudflareOriginalTaskLedger NewColdSources(RenewalWork work)
    {
        var sources=new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        var phase=_renewalExecuting.Value ?? throw new InvalidOperationException("An actual admitted cold authority phase is required.");
        var callbacks = new ColdSourceCallbacks(this, work, sources, phase);
        sources.BindOriginalCallerCallback(callbacks.Invoke);
        lock(_sync) _coldWorkSources.Add(work,callbacks);
        return sources;
    }
    private async Task<T> RunColdBodyToSettlementAsync<T>(RenewalWork work, CloudflareOriginalTaskLedger sources, Func<Task<T>> body)
    {
        Task<T>? actual=null; T value=default!; Exception? observed=null;
        try { _ = InvokeRenewalPhysical(() => { actual=body(); lock(_sync) work.Raw.Add(actual); return true; }); }
        catch(Exception cause) { observed=cause; sources.Retain(cause); }
        if(actual is not null)
            try { value=await actual.ConfigureAwait(false); }
            catch(Exception cause) { observed??=cause; sources.Capture(actual,cause); }
        // Seal future factory admission, then join every already-admitted finite callback.
        // The encompassing body is retained by work, never in the child cohort it settles.
        ColdSourceCallbacks callbacks; lock (_sync) callbacks = _coldWorkSources[work];
        var callbackDrain = callbacks.SealAndJoinFiniteCallbacks(); lock (_sync) work.Raw.Add(callbackDrain);
        try { await callbackDrain.ConfigureAwait(false); } catch (Exception cause) { sources.Capture(callbackDrain, cause); }
        foreach (var raw in sources.OriginalTasks) RetainColdRaw(work, sources, raw);
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if(actual?.IsCanceled==true && observed is OperationCanceledException && sources.OriginalTasks.All(raw=>!raw.IsFaulted) && sources.OriginalErrors.All(cause=>cause is OperationCanceledException))
            ExceptionDispatchInfo.Capture(observed).Throw();
        if(sources.OriginalErrors.Count!=0) throw new AggregateException("Actual cold authority source/body failed.",sources.OriginalErrors);
        return value;
    }
    private static Action<Action> ColdCaller(CloudflareOriginalTaskLedger sources) => body=>sources.Invoke(()=> {body();return true;});
    private void RetainColdRaw(RenewalWork work,CloudflareOriginalTaskLedger sources,Task raw)
    { _=sources.Track(raw); lock(_sync) if(!work.Raw.Any(actual=>ReferenceEquals(actual,raw))) work.Raw.Add(raw); }
    private static string FingerprintCold(TaskExecutionSnapshot value)=>Digest(JsonSerializer.SerializeToElement(value));
}
