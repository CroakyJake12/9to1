using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Page-owned task source/metadata borrower. Uses only original Home/Files/Task,
/// canonical workspace and platform key owners; never creates a Task, store, model or grant.</summary>
internal sealed class DeveloperTaskDraftRecoveryOwner(
    HomeColdProjectReadReconciliation source, FilesDeveloperOriginalCurrentProjectExecutionBridge execution,
    FileDeveloperWorkspaceStore store, IProviderSecretStore keys, IAppPaths paths,
    TaskExecutionCoordinator canonical, IConversationRepository conversations, IContainerRepository containers,
    DeveloperManualSourceReadOwner reader, Guid taskId, Guid runId, Guid contextId)
{
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private readonly HashSet<IDeveloperOriginalProjectCommandRead> _ownedReads = new(ReferenceEqualityComparer.Instance);
    private IDeveloperOriginalProjectCommandRead? _read;
    private bool _busy, _retiring, _failed;
    private Task? _close;

    private Task<T> Start<T>(Action<Action> scope, Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        TaskCompletionSource start; Task<T> actual;
        lock (_gate)
        {
            if (_retiring || _busy || _failed) throw new InvalidOperationException("The actual task source owner is busy, retiring or needs outcome inspection.");
            _operations.RemoveAll(value => value.IsCompletedSuccessfully);
            if (_operations.Count >= 128) throw new InvalidOperationException("Task source outcome custody is full; preserve and inspect the originals.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously); _busy = true;
            actual = DriveAsync(start.Task, scope, body); _operations.Add(actual);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<T> DriveAsync<T>(Task start, Action<Action> scope, Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        await start.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this); sources.BindOriginalCallerCallback(scope);
        try { return await sources.RunToOriginalSettlementAsync(() => body(sources)).ConfigureAwait(false); }
        catch { lock (_gate) _failed = true; throw; }
        finally { lock (_gate) _busy = false; }
    }
    private static Task<T> Observe<T>(CloudflareOriginalTaskLedger sources, Func<Task<T>> factory) =>
        sources.AwaitAsync(sources.Invoke(() => sources.Track(factory())));
    private static Task Observe(CloudflareOriginalTaskLedger sources, Func<Task> factory) =>
        sources.AwaitAsync(sources.Invoke(() => sources.Track(factory())));
    private static void Scope(CloudflareOriginalTaskLedger sources, Action body) =>
        sources.Invoke(() => { body(); return true; });
    private static void Retain(CloudflareOriginalTaskLedger sources, Task actual) => sources.Track(actual);

    private async Task<(DeveloperCanonicalActionContext Context, TaskRunAttemptAdmission Attempt)> CurrentAsync(
        CloudflareOriginalTaskLedger sources, DeveloperCanonicalActionContext? exact, CancellationToken token)
    {
        var before = await Observe(sources, () => canonical.GetAsync(taskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original task is missing.");
        DemandTask(before);
        var attemptId = before.Attempts.LastOrDefault()?.Id ?? throw new UnauthorizedAccessException("No actual task attempt supports this source operation.");
        var attempt = await Observe(sources, () => canonical.GetIssuedAttemptAsync(taskId, runId, attemptId, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The genuine current attempt issuer is unavailable.");
        await Observe(sources, () => attempt.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        var current = await Observe(sources, () => canonical.GetAsync(taskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original task retired.");
        DemandTask(current);
        if (current.PersistenceRevision != before.PersistenceRevision || current.Attempts.LastOrDefault()?.Id != attemptId ||
            exact is not null && (exact.TaskId != taskId || exact.ExecutionId != runId || exact.ContextId != contextId ||
                exact.AttemptId != attemptId || exact.PersistenceRevision != current.PersistenceRevision))
            throw new UnauthorizedAccessException("The exact current Task/Run/attempt changed during source admission.");
        return (exact ?? new(taskId, runId, contextId, attemptId, current.PersistenceRevision, Guid.NewGuid()), attempt);
    }
    private void DemandTask(TaskExecutionSnapshot current)
    {
        if (taskId == Guid.Empty || runId == Guid.Empty || contextId == Guid.Empty || current.TaskId != taskId ||
            current.ExecutionId != runId || current.ContextId != contextId || current.OwnerBinding is null)
            throw new UnauthorizedAccessException("The actual owned canonical Task/Run/context is required.");
    }
    private void RetainRead(IDeveloperOriginalProjectCommandRead actual)
    {
        // Cleanup custody before any live-use/post-publication check.
        if (!source.IsOwnedOriginalCommandRead(actual)) throw new UnauthorizedAccessException("No SAME original Home READ cleanup identity exists.");
        lock (_gate)
        {
            if (_ownedReads.Count >= 128 && !_ownedReads.Contains(actual)) throw new InvalidOperationException("Owned task READ custody is full.");
            _ownedReads.Add(actual);
        }
    }
    private async Task<IDeveloperOriginalProjectCommandRead> ReadAsync(CloudflareOriginalTaskLedger sources,
        DeveloperResolvedProject project, CancellationToken token)
    {
        IDeveloperOriginalProjectCommandRead? read; lock (_gate) read = _read;
        if (read is not null)
        {
            await Observe(sources, () => source.ValidateOriginalCommandReadWithinSourceAsync(read,
                body => Scope(sources, body), actual => Retain(sources, actual), token)).ConfigureAwait(false);
            return read;
        }
        var conversation = await Observe(sources, () => conversations.GetAsync(contextId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual Task conversation is missing.");
        var available = await Observe(sources, () => containers.GetByModeAsync(HavenMode.Tasks, token)).ConfigureAwait(false);
        var container = available.Single(value => value.Id == conversation.ContainerId && !value.IsArchived);
        read = await sources.CaptureOriginalAcquisitionAsync(() => source.AcquireOriginalCommandReadWithinSourceAsync(
            conversation, container, JsonSerializer.Serialize(project.Reference), body => Scope(sources, body),
            actual => Retain(sources, actual), token), RetainRead).ConfigureAwait(false);
        Scope(sources, () =>
        {
            if (!source.IsIssuedOriginalCommandRead(read) || !ReferenceEquals(read.OriginalDescriptor.OriginalStore, store) ||
                read.OriginalIdentity.ProjectId != project.Reference.ProjectId || read.OriginalIdentity.RootId != project.Reference.RootId ||
                read.OriginalIdentity.WorkspaceId != project.Reference.WorkspaceId)
                throw new UnauthorizedAccessException("The genuine task READ belongs to another canonical source/store/project.");
            lock (_gate) _read = read;
        });
        return read;
    }
    private async Task<T> WithinReadAsync<T>(CloudflareOriginalTaskLedger sources,
        DeveloperResolvedProject project, DeveloperCanonicalActionContext action, Func<Task<T>> originalSource,
        CancellationToken token)
    {
        var read = await ReadAsync(sources, project, token).ConfigureAwait(false);
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalSourceInvocation<T>? invocation = null;
        Scope(sources, () => invocation = execution.CaptureOriginalSourceWithinSource(read, project.Reference, action,
            originalSource, body => Scope(sources, body), actual => Retain(sources, actual), token));
        var actual = invocation ?? throw new InvalidOperationException("The actual source invocation was not captured.");
        // Join after returning from the finite physical command factory; never self-join.
        try { await sources.AwaitAsync(actual.OriginalSourceTask).ConfigureAwait(false); }
        catch { /* Exact child failure remains in the ledger; independent join retains publication failures. */ }
        return await Observe(sources, actual.JoinOriginalAsync).ConfigureAwait(false);
    }
    public Task<T> RunOriginalSourceAsync<T>(DeveloperResolvedProject project, DeveloperCanonicalActionContext exact,
        Func<Task<T>> originalSource, Action<Action> scope, CancellationToken token) => Start(scope, async sources =>
    {
        var current = await CurrentAsync(sources, exact, token).ConfigureAwait(false);
        var read = await ReadAsync(sources, project, token).ConfigureAwait(false);
        DemandSameActor(read, current.Attempt);
        return await WithinReadAsync(sources, project, current.Context, originalSource, token).ConfigureAwait(false);
    });
    private static void DemandSameActor(IDeveloperOriginalProjectCommandRead read, TaskRunAttemptAdmission attempt)
    {
        var actor = read.OriginalIdentity.OriginalHomeResourceActor; var owner = attempt.Lease.Owner;
        if (actor.ActorId != owner.ActorId || actor.ProfileId != owner.ProfileId || actor.AccountId != owner.AccountId ||
            actor.OrganisationId != owner.OrganisationId || actor.AuthenticationRevision != owner.AuthenticationRevision)
            throw new UnauthorizedAccessException("The actual Home source actor differs from the genuine current Task owner.");
    }
    private DeveloperDraftRecoveryScope RecoveryScope(IDeveloperOriginalProjectCommandRead read)
    {
        var actor = read.OriginalIdentity.OriginalHomeResourceActor;
        if (actor.AccountId is not null || actor.OrganisationId is not null || !Guid.TryParse(actor.ProfileId, out var profile) || profile == Guid.Empty)
            throw new UnauthorizedAccessException("The actual personal Home profile is required for this protected draft partition.");
        return new(profile, taskId, runId, contextId);
    }
    public Task<DeveloperManualRecoveryState> RestoreAsync(DeveloperResolvedProject project, Action<Action> scope,
        CancellationToken token) => Start<DeveloperManualRecoveryState>(scope, async sources =>
    {
        DeveloperManualSourceReadOwner.DemandOriginalRecoveryKeys(keys, paths);
        var current = await CurrentAsync(sources, null, token).ConfigureAwait(false);
        var read = await ReadAsync(sources, project, token).ConfigureAwait(false); DemandSameActor(read, current.Attempt);
        var partition = RecoveryScope(read);
        var loaded = await Observe(sources, () => store.GetDraftRecoveryAsync(project, partition, token)).ConfigureAwait(false);
        if (!loaded.Succeeded) throw new InvalidOperationException(loaded.Error?.Message ?? "Protected task recovery could not be read.");
        if (loaded.Value is not { } snapshot) return new(partition, 0, [], null, false);
        byte[] key = []; DeveloperDraftRecoveryPayload payload;
        try
        {
            key = await Observe(sources, () => DeveloperManualSourceReadOwner.ReadRecoveryKeyAsync(keys, partition.ProfileId,
                project.Reference.WorkspaceId, required: true, token)).ConfigureAwait(false);
            await Observe(sources, () => source.ValidateOriginalCommandReadWithinSourceAsync(read,
                body => Scope(sources, body), actual => Retain(sources, actual), token)).ConfigureAwait(false);
            payload = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, partition, snapshot, key);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var restored = new List<DeveloperRestoredDirtyEditorTab>();
        foreach (var tab in payload.Tabs)
        {
            var text = await Observe(sources, () => reader.ReadFileAsync(project, tab.RelativePath, token)).ConfigureAwait(false);
            restored.Add(new(tab, text));
        }
        await Observe(sources, () => source.ValidateOriginalCommandReadWithinSourceAsync(read,
            body => Scope(sources, body), actual => Retain(sources, actual), token)).ConfigureAwait(false);
        return new(partition, snapshot.Generation, Array.AsReadOnly(restored.ToArray()), payload.SelectedRelativePath, true);
    });
    internal static void DemandTaskRecoveryPartition(DeveloperDraftRecoveryScope expected, DeveloperDraftRecoveryScope actual)
    {
        if (expected.IsManual || actual.IsManual || expected.ProfileId == Guid.Empty || expected.TaskId == Guid.Empty ||
            expected.ExecutionId == Guid.Empty || expected.ContextId == Guid.Empty || expected != actual)
            throw new UnauthorizedAccessException("The actual original task/profile recovery partition is required.");
    }
    public Task<DeveloperDraftRecoverySnapshot> SaveAsync(DeveloperResolvedProject project,
        DeveloperDraftRecoveryScope exactPartition, long expected, DeveloperDraftRecoveryPayload payload,
        Action<Action> scope, CancellationToken token) => Start(scope, async sources =>
    {
        DeveloperManualSourceReadOwner.DemandOriginalRecoveryKeys(keys, paths);
        var current = await CurrentAsync(sources, null, token).ConfigureAwait(false);
        var old = await ReadAsync(sources, project, token).ConfigureAwait(false);
        DemandSameActor(old, current.Attempt);
        var partition = RecoveryScope(old);
        DemandTaskRecoveryPartition(exactPartition, partition);
        if (await Observe(sources, () => canonical.ObserveOriginalColdInputAsync(taskId, runId, token)).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("This task already has a protected terminal capsule. Keep the draft open; its original capsule was preserved.");
        // Establish an actual idle own-cohort binding through its real private READ frame.
        // This observes current native identity; it does not approve execution or an attempt.
        _ = await WithinReadAsync(sources, project, current.Context,
            () => execution.ResolveOriginalProjectBindingAsync(project, body => Scope(sources, body),
                actual => Retain(sources, actual), token), token).ConfigureAwait(false);
        foreach (var tab in payload.Tabs)
        {
            var text = await Observe(sources, () => reader.ReadFileAsync(project, tab.RelativePath, token)).ConfigureAwait(false);
            if (!StringComparer.OrdinalIgnoreCase.Equals(DeveloperManualSourceEditOwner.Sha256(text), tab.OriginalSha256))
                throw new InvalidOperationException("Source changed before task recovery save. The live draft and previous ciphertext were preserved.");
        }
        var before = await Observe(sources, () => source.PrepareOriginalMetadataRebindWithinSourceAsync(old,
            body => Scope(sources, body), actual => Retain(sources, actual), token)).ConfigureAwait(false);
        var initial = source.GetOriginalInitialInputForMetadataRebindPreparation(before);
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalMetadataRebindReservation? reservation = null;
        ITaskRunOriginalProjectMetadataInputMutation? mutation = null;
        Exception? primary = null;
        try
        {
            Scope(sources, () => reservation = execution.AcquireOriginalMetadataRebindUpdate(before));
            Scope(sources, () => mutation = source.BeginOriginalInitialInputMetadataMutation(initial, before));
            Retain(sources, mutation!.OriginalSettlement);
            var saved = await Observe(sources, () => store.PrepareAndSaveDraftRecoveryAsync(project, partition, expected,
                async (recordsExist, ct) =>
                {
                    await Observe(sources, () => current.Attempt.Lease.RevalidateAsync(ct).AsTask()).ConfigureAwait(false);
                    byte[] key = [];
                    try
                    {
                        key = await Observe(sources, () => DeveloperManualSourceReadOwner.ReadRecoveryKeyAsync(keys,
                            partition.ProfileId, project.Reference.WorkspaceId, recordsExist, ct)).ConfigureAwait(false);
                        return FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, partition, checked(expected + 1), payload, key);
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                }, token)).ConfigureAwait(false);
            if (!saved.Succeeded || saved.Value is not { } snapshot)
                throw new InvalidOperationException(saved.Error?.Message ?? "Protected task recovery was not committed.");
            var transition = store.GetOriginalMetadataTransition(snapshot)
                ?? throw new UnauthorizedAccessException("No actual canonical atomic metadata write issued this snapshot.");
            var next = await Observe(sources, () => execution.CompleteOriginalMetadataRebindWithinSourceAsync(reservation!,
                transition, snapshot, body => Scope(sources, body), actual => Retain(sources, actual), token)).ConfigureAwait(false);
            RetainRead(next);
            var result = reservation!.OriginalAcquiredResult
                ?? throw new UnauthorizedAccessException("The actual Home after rebind result was not retained.");
            var lineage = await Observe(sources, () => source.PrepareOriginalInitialInputMetadataSuccessorWithinSourceAsync(
                initial, result, body => Scope(sources, body), actual => Retain(sources, actual), token)).ConfigureAwait(false);
            Scope(sources, () => source.CompleteOriginalInitialInputMetadataMutation(mutation!, lineage));
            lock (_gate) _read = next;
            old.RequestOriginalRetirement();
            await Observe(sources, old.CloseAndDrainOriginalAsync).ConfigureAwait(false);
            lock (_gate) _ownedReads.Remove(old); // Only after exact successful own close.
            return snapshot;
        }
        catch (Exception cause)
        {
            primary = cause;
            if (mutation is not null && !mutation.OriginalSettlement.IsCompleted)
                source.FailOriginalInitialInputMetadataMutation(mutation, cause);
            throw;
        }
        finally
        {
            // A new read acquired before a post-publication refusal still has a cleanup owner.
            if (reservation?.OriginalAcquiredResult is { } acquired)
                try { RetainRead(acquired.NewOriginalCommandRead); }
                catch (Exception cause) { sources.Retain(cause); }
            if (reservation is not null)
                try { reservation.Dispose(); }
                catch (Exception cause) { sources.Retain(cause); if (primary is null) throw; }
        }
    });
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        execution.DemandExternalOriginalExecutionTrustJoin();
        IDeveloperOriginalProjectCommandRead[] reads; lock (_gate) reads = _ownedReads.ToArray();
        foreach (var read in reads) read.DemandExternalOriginalJoin();
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin();
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = CloseAsync(start.Task, _operations.ToArray()); _close = actual;
        }
        start.TrySetResult(); return actual;
    }
    private async Task CloseAsync(Task start, Task[] originals)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        foreach (var actual in originals)
            try { await actual.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
        IDeveloperOriginalProjectCommandRead[] reads; lock (_gate) reads = _ownedReads.ToArray();
        foreach (var read in reads)
        {
            Task? close = null;
            try { read.RequestOriginalRetirement(); close = read.CloseAndDrainOriginalAsync(); }
            catch (Exception cause) { errors.Add(cause); }
            if (close is not null)
                try { await close.ConfigureAwait(false); } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual task source/read originals did not close cleanly.", errors);
    }
}
