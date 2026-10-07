using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class ResilientProviderRoutingModelClient
{
    private readonly object _coldSelectionSync = new();
    private readonly List<ColdCheckpointSelectionBody> _coldSelectionBodies = [];
    private readonly ConditionalWeakTable<ITaskRunColdToolCheckpointSelection, ColdCheckpointSelectionBody> _coldSelections = new();

    public Task<ITaskRunColdToolCheckpointSelection?> SelectLocalForColdOriginalToolCheckpointAsync(
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, TaskExecutionSnapshot actualCurrent,
        TaskRunColdOriginalSourceScope currentSources, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameAcknowledgment); ArgumentNullException.ThrowIfNull(actualCurrent);
        ArgumentNullException.ThrowIfNull(currentSources); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask); token.ThrowIfCancellationRequested();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var body = new ColdCheckpointSelectionBody(sameAcknowledgment, actualCurrent, token,
            currentSources.WithinOriginalCaller(originalSynchronousScope, retainOriginalTask)) { Router = this };
        lock (_coldSelectionSync)
        {
            if (_coldSelectionBodies.Count >= 64)
                throw new InvalidOperationException("Original cold selection custody requires external retirement.");
            // Own the actual encompassing driver before invoking any supplied/source callback.
            body.Whole = SelectColdCheckpointBodyAsync(body, start.Task);
            _coldSelectionBodies.Add(body);
        }
        try { body.PublishWhole(); }
        catch (Exception cause) { body.RecordSynchronous(cause); throw; }
        finally { start.SetResult(); }
        return body.Whole!;
    }

    private async Task<ITaskRunColdToolCheckpointSelection?> SelectColdCheckpointBodyAsync(
        ColdCheckpointSelectionBody body, Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            body.ThrowFailures();
            var journal = coldRecoveryJournal;
            if (taskCoordinator is null || journal is not SqliteTaskRunColdRecoveryJournal contextOwner
                || routeCapture is not ITaskRunOriginalSelectedRouteCaptureSource scopedCapture
                || catalogueEligibility is null) return null;
            body.InvokeOriginalFactory(() =>
            {
                if (!taskCoordinator.HasOriginalColdRecoveryComposition(journal, contextOwner)
                    || !journal.IsIssuedOriginalEntry(body.Acknowledgment.OriginalClaim.OriginalEntry)
                    || !journal.IsIssuedOriginalAcknowledgment(body.Acknowledgment, body.Acknowledgment.OriginalClaim))
                    throw new UnauthorizedAccessException("The SAME configured journal's private acknowledged claim is required.");
                return 0;
            });
            await body.Read(() => journal.ValidateOriginalRestoredInputAsync(body.Acknowledgment,
                body.Expected, body.Parent, body.Token)).ConfigureAwait(false);
            var checkpoint = body.InvokeOriginalFactory(() =>
            {
                var capsule = body.Acknowledgment.OriginalClaim.OriginalEntry.Capsule;
                if (capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse
                    || capsule.OriginalToolCheckpoint is not { } original
                    || body.Expected.State != TaskExecutionLifecycle.Suspended
                    || body.Expected.TaskId != body.Acknowledgment.AcknowledgedTask.TaskId
                    || body.Expected.ContextId != body.Acknowledgment.AcknowledgedTask.ContextId
                    || body.Expected.ExecutionId != body.Acknowledgment.AcknowledgedTask.ExecutionId
                    || body.Expected.OwnerBinding != body.Acknowledgment.AcknowledgedTask.OwnerBinding)
                    throw new UnauthorizedAccessException("No authentic activated SAME unfinished response boundary exists.");
                return original;
            });
            var required = RequiredCapabilities(checkpoint.OriginalNextRequest);
            var restrictions = checkpoint.OriginalNextRequest.Tools.Select(tool => ModelToolPermissionMap.Map(tool.Name))
                .Where(capability => capability.HasValue).Select(capability => capability!.Value).Distinct().ToArray();
            var digest = SnapshotFingerprint(body.Expected);
            var current = await body.Read(() => taskCoordinator.GetAsync(body.Expected.TaskId, body.Token)).ConfigureAwait(false);
            if (current is null || SnapshotFingerprint(current) != digest)
                throw new UnauthorizedAccessException("The actual cold activation row changed before local selection.");
            body.Current = current; body.Digest = digest;
            var localOwners = body.InvokeOriginalFactory(() => providers.Providers.OfType<LlamaCppModelProvider>().ToArray());
            foreach (var local in localOwners)
            {
                if (!body.InvokeOriginalFactory(() => ReferenceEquals(providers.Find(local.Id), local))) continue;
                var catalogue = await body.Read(() => local.GetModelsWithinOriginalSourceAsync(body, body.Token)).ConfigureAwait(false);
                var compatible = catalogue.Where(model => model.ProviderId == local.Id && model.IsLocal
                    && required.All(model.Supports)).ToArray();
                var eligible = body.InvokeOriginalFactory(() => catalogueEligibility.ObserveOriginalCatalogueEligibility(
                    compatible, required.ToHashSet(), new ModelRoutingPolicy(ModelRoutingMode.ManualFallback,
                        PreferLocal: true, AllowCloud: false, PreferredModelKeys: compatible.Select(model => model.Key).ToArray(), AllowFallback: false)));
                foreach (var model in compatible.Where(model => eligible.Any(issued => ReferenceEquals(issued, model))))
                {
                    // This fresh private candidate producer owns actual actor/config/catalogue/
                    // model policy callbacks after awaits. No plain async wrapper substitutes it.
                    var candidate = await body.Read(() => scopedCapture.CaptureSelectedRouteWithinOriginalSourceAsync(
                        current, model, required.ToArray(), restrictions, body.Callback, body.RetainOriginalTask, body.Token)).ConfigureAwait(false);
                    var observation = await body.Read(() => local.ObserveOriginalRuntimeWithinSourceAsync(
                        new ModelIdentity(model.ProviderId, model.Name, candidate.ArtifactIdentity), body, body.Token)).ConfigureAwait(false);
                    body.InvokeOriginalFactory(() =>
                    {
                        if (candidate.UsesCloud || candidate.ProviderId != model.ProviderId || candidate.ModelId != model.Name
                            || !required.Select(value => value.ToString()).Order(StringComparer.Ordinal)
                                .SequenceEqual(candidate.RequiredCapabilities.Order(StringComparer.Ordinal))
                            || !local.IsIssuedOriginalRuntimeObservation(observation)
                            || !required.All(observation.Endpoint.ObservedCapabilities.Contains))
                            throw new UnauthorizedAccessException("The fresh candidate and privately observed local endpoint differ.");
                        return 0;
                    });
                    await body.Read(() => journal.ValidateOriginalRestoredInputAsync(body.Acknowledgment,
                        current, body.Parent, body.Token)).ConfigureAwait(false);
                    var latest = await body.Read(() => taskCoordinator.GetAsync(current.TaskId, body.Token)).ConfigureAwait(false);
                    if (latest is null || SnapshotFingerprint(latest) != digest)
                        throw new UnauthorizedAccessException("The actual activated row changed during local selection.");
                    body.Local = local; body.Observation = observation; body.Model = model; body.Candidate = candidate;
                    return new ColdToolCheckpointSelection(body);
                }
            }
            return null;
        }
        catch (Exception cause) { body.RecordDriver(cause); return null; }
        finally
        {
            await body.JoinOriginals().ConfigureAwait(false);
            body.ThrowFailures();
            if (body.Local is { } local && body.Observation is { } observation)
                body.InvokeOriginalFactory(() =>
                {
                    if (!ReferenceEquals(providers.Find(local.Id), local) || !local.IsIssuedOriginalRuntimeObservation(observation))
                        throw new UnauthorizedAccessException("The actual local observation retired before disclosure.");
                    return 0;
                });
        }
    }

    public bool IsIssuedOriginalColdToolCheckpointSelection(ITaskRunColdToolCheckpointSelection selection,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, TaskExecutionSnapshot sameCurrent)
    {
        if (selection is not ColdToolCheckpointSelection issued || sameAcknowledgment is null || sameCurrent is null) return false;
        var body = issued.Body;
        lock (_coldSelectionSync)
            if (!_coldSelections.TryGetValue(selection, out var retained) || !ReferenceEquals(body, retained)
                || !ReferenceEquals(body.Acknowledgment, sameAcknowledgment) || body.Whole is not { IsCompletedSuccessfully: true }
                || !body.AllOriginalsSucceeded) return false;
        return body.Current is not null && ReferenceEquals(selection.OriginalSelectionTask, body.Whole)
            && ReferenceEquals(selection.ActualCurrentSnapshot, body.Current)
            && ReferenceEquals(selection.ActualSelectedModel, body.Model) && ReferenceEquals(selection.ActualSelectedCandidate, body.Candidate)
            && body.Digest == SnapshotFingerprint(sameCurrent) && body.Digest == SnapshotFingerprint(body.Current)
            && coldRecoveryJournal is SqliteTaskRunColdRecoveryJournal contextOwner && taskCoordinator is not null
            && taskCoordinator.HasOriginalColdRecoveryComposition(coldRecoveryJournal, contextOwner)
            && coldRecoveryJournal.IsIssuedOriginalAcknowledgment(sameAcknowledgment, sameAcknowledgment.OriginalClaim)
            && body.Local is { } local && body.Observation is { } observation
            && ReferenceEquals(providers.Find(local.Id), local) && local.IsIssuedOriginalRuntimeObservation(observation);
    }

    private sealed class ColdToolCheckpointSelection : ITaskRunColdToolCheckpointSelection
    {
        internal readonly ColdCheckpointSelectionBody Body;
        internal ColdToolCheckpointSelection(ColdCheckpointSelectionBody body)
        {
            Body = body;
            body.Router!._coldSelections.Add(this, body);
            OriginalSelectionSources = Array.AsReadOnly(body.SnapshotOriginals());
        }
        public ITaskRunColdJournalAcknowledgment OriginalAcknowledgment => Body.Acknowledgment;
        public TaskExecutionSnapshot ActualCurrentSnapshot => Body.Current!;
        public ProviderModelDescriptor ActualSelectedModel => Body.Model!;
        public TaskRunRouteCandidate ActualSelectedCandidate => Body.Candidate!;
        public Task OriginalSelectionTask => Body.Whole!;
        public IReadOnlyList<Task> OriginalSelectionSources { get; }
    }

    private sealed class ColdCheckpointSelectionBody(ITaskRunColdJournalAcknowledgment acknowledgment,
        TaskExecutionSnapshot expected, CancellationToken token, TaskRunColdOriginalSourceScope parent) : IInferenceEngineOriginalSourceScope
    {
        private readonly object _sync = new();
        private readonly List<Task> _sources = [];
        private readonly HashSet<Task> _joined = new(ReferenceEqualityComparer.Instance);
        private readonly List<Exception> _errors = [];
        private bool _faulted, _canceled;
        internal ResilientProviderRoutingModelClient? Router;
        internal readonly ITaskRunColdJournalAcknowledgment Acknowledgment = acknowledgment;
        internal readonly TaskExecutionSnapshot Expected = expected;
        internal readonly CancellationToken Token = token;
        internal readonly TaskRunColdOriginalSourceScope Parent = parent;
        internal Task<ITaskRunColdToolCheckpointSelection?>? Whole;
        internal TaskExecutionSnapshot? Current;
        internal string? Digest;
        internal LlamaCppModelProvider? Local;
        internal LlamaCppOriginalRuntimeObservation? Observation;
        internal ProviderModelDescriptor? Model;
        internal TaskRunRouteCandidate? Candidate;
        internal void PublishWhole() => Parent.Invoke(() => Whole!);
        public void Callback(Action action) => _ = InvokeOriginalFactory(() => { action(); return 0; });
        public T InvokeOriginalFactory<T>(Func<T> factory) => Invoke(factory, false);
        public T InvokeOriginalCleanup<T>(Func<T> factory) => Invoke(factory, true);
        private T Invoke<T>(Func<T> factory, bool cleanup)
        {
            var live = 1; var once = 0; var thread = Environment.CurrentManagedThreadId; T actual = default!;
            Exception? callbackFailure = null;
            try
            {
                _ = Parent.Invoke(() =>
                {
                    if (Volatile.Read(ref live) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref once, 1) != 0)
                    {
                        var refusal = new InvalidOperationException("The actual cold selector callback requires its issuing thread and one live invocation.");
                        RecordSynchronous(refusal); callbackFailure ??= refusal; throw refusal;
                    }
                    try
                    {
                        if (!cleanup) { Token.ThrowIfCancellationRequested(); lock (_sync) if (_faulted || _canceled || _sources.Count >= 4096) throw new InvalidOperationException("Cold selection admission failed or original custody requires retirement."); }
                        actual = factory();
                        if (actual is Task task) KeepOriginal(task);
                        return actual;
                    }
                    catch (Exception cause) { callbackFailure = cause; throw; }
                });
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                if (Volatile.Read(ref once) != 1) throw new InvalidOperationException("The original cold selector callback was omitted.");
                return actual;
            }
            catch (Exception cause)
            {
                RecordSynchronous(cause); if (callbackFailure is not null) RecordSynchronous(callbackFailure);
                throw new AggregateException("The actual cold selector callback failed.", callbackFailure is null || ReferenceEquals(callbackFailure, cause)
                    ? [cause] : new[] { callbackFailure, cause });
            }
            finally { Volatile.Write(ref live, 0); }
        }
        private void KeepOriginal(Task actual)
        { lock (_sync) { if (ReferenceEquals(actual, Whole)) return; if (!_sources.Contains(actual)) { _sources.Add(actual); if (_sources.Count > 4096) throw new InvalidOperationException("Cold original custody requires retirement."); } } }
        public void RetainOriginalTask(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual); KeepOriginal(actual);
            try { _ = Parent.Invoke(() => actual); } catch (Exception cause) { RecordSynchronous(cause); throw; }
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; Exception? scopeFailure = null;
            try { _ = InvokeOriginalFactory(() => { actual = factory() ?? throw new InvalidOperationException("No actual cold source Task exists."); KeepOriginal(actual); return actual; }); }
            catch (Exception cause) { scopeFailure = cause; }
            T value = default!;
            if (actual is not null) try { value = await actual.ConfigureAwait(false); } catch (Exception cause) { RecordTask(actual, cause); }
            finally { lock (_sync) _joined.Add(actual); }
            if (scopeFailure is not null) ExceptionDispatchInfo.Capture(scopeFailure).Throw();
            ThrowFailures(); return actual is null ? throw new InvalidOperationException("No source Task was acquired.") : value;
        }
        internal async Task Read(Func<Task> factory)
        {
            Task? actual = null; Exception? scopeFailure = null;
            try { _ = InvokeOriginalFactory(() => { actual = factory() ?? throw new InvalidOperationException("No actual cold source Task exists."); KeepOriginal(actual); return actual; }); }
            catch (Exception cause) { scopeFailure = cause; }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception cause) { RecordTask(actual, cause); }
            finally { lock (_sync) _joined.Add(actual); }
            if (scopeFailure is not null) ExceptionDispatchInfo.Capture(scopeFailure).Throw();
            ThrowFailures(); if (actual is null) throw new InvalidOperationException("No source Task was acquired.");
        }
        internal async Task JoinOriginals()
        {
            while (true)
            {
                Task? next; lock (_sync) { next = _sources.FirstOrDefault(task => !_joined.Contains(task)); if (next is not null) _joined.Add(next); }
                if (next is null) return;
                try { await next.ConfigureAwait(false); } catch (Exception cause) { RecordTask(next, cause); }
            }
        }
        internal Task[] SnapshotOriginals() { lock (_sync) return _sources.ToArray(); }
        internal bool AllOriginalsSucceeded { get { lock (_sync) return !_faulted && !_canceled && _sources.All(task => task.IsCompletedSuccessfully); } }
        internal void RecordSynchronous(Exception cause) { lock (_sync) { _faulted = true; AddCause(cause); } }
        internal void RecordDriver(Exception cause) { lock (_sync) { if (!_canceled || cause is not OperationCanceledException) _faulted = true; AddCause(cause); } }
        private void RecordTask(Task task, Exception cause)
        { lock (_sync) { if (task.IsCanceled) _canceled = true; else _faulted = true; AddCause(cause); if (task.Exception is { } group) AddCause(group); } }
        private void AddCause(Exception cause) { if (!_errors.Any(known => ReferenceEquals(known, cause))) _errors.Add(cause); }
        internal void ThrowFailures()
        {
            Exception[] causes; bool canceled; lock (_sync) { causes = _errors.ToArray(); canceled = _canceled && !_faulted; }
            if (causes.Length == 0) return;
            var group = new AggregateException("All original cold selection sources and callback causes are retained.", causes);
            if (canceled) throw new OperationCanceledException("Actual cold selection sources were canceled.", group);
            throw group;
        }
    }
}
