using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>Creates one distinct Tasks pair for a genuine existing Studio project.
/// The original Studio rows/history are never updated. Home store READ, live project
/// READ, and individual creation WRITE are independent admissions.</summary>
public sealed partial class CanonicalProjectTaskContextCreateOwner : ICanonicalProjectTaskContextCreateSource, ICanonicalProjectTaskContextResumeCreateSource,
    ICanonicalOriginalWriteSettlementPinOwner<ICanonicalProjectTaskContextCreationIntent, ICanonicalProjectTaskContextCreation>,
    ICanonicalProjectTaskContextOriginalDenRevisionOwner, IAsyncDisposable
{
    private readonly CanonicalProjectContextStoreReadOwner _contexts;
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly IDeveloperOriginalProjectCommandReadSource _projectReads;
    private ICanonicalProjectTaskContextResumeSelectionSource? _resumeSelections;
    private readonly object _gate = new();
    private readonly List<OriginalCommand> _originals = [];
    private readonly AsyncLocal<int> _logical = new();
    [ThreadStatic] private static Dictionary<CanonicalProjectTaskContextCreateOwner, int>? _originalPhysical;
    private bool _retiring;
    private Task? _close;
    private sealed class OriginalCommand
    {
        internal readonly TaskCompletionSource<Task> Publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Raw, Observation;
        internal bool IndependentlyJoined;
    }
    private ICanonicalProjectTaskContextHomeWriteSource? _writes;
    private readonly ConditionalWeakTable<ICanonicalProjectTaskContextCreationIntent, Intent> _intents = new();
    private readonly ConditionalWeakTable<ICanonicalProjectTaskContextCreation, Creation> _creations = new();
    private readonly ConditionalWeakTable<Task<ICanonicalProjectTaskContextCreation>, Commit> _atomic = new();
    private readonly ConditionalWeakTable<Intent, CommitAttempt> _commits = new();
    private readonly ConditionalWeakTable<Task, CommitAttempt> _publicCommits = new();
    private sealed class CommitAttempt
    {
        internal Task<ICanonicalProjectTaskContextCreation> Driver = null!;
        internal Task<ICanonicalProjectTaskContextCreation>? Inner;
        internal Commit Invocation = null!;
    }
    public bool IsAcknowledgedOriginalCreationRefusal(Task sameOriginalCommit)
    {
        if (!sameOriginalCommit.IsFaulted || !_publicCommits.TryGetValue(sameOriginalCommit, out var attempt) ||
            !ReferenceEquals(attempt.Driver, sameOriginalCommit) || attempt.Inner is not { } inner ||
            !_store.IsAcknowledgedOriginalCommandRefusal(inner)) return false;
        var outerCauses = sameOriginalCommit.Exception!.InnerExceptions;
        var innerCauses = inner.Exception!.InnerExceptions;
        return outerCauses.Count == 1 && innerCauses.Count == 1 && ReferenceEquals(outerCauses[0], innerCauses[0]);
    }
    private readonly Dictionary<Guid, WeakReference<Intent>> _operations = new();
    private readonly AsyncLocal<Commit?> _executing = new();
    [ThreadStatic] private static Dictionary<CanonicalProjectTaskContextCreateOwner, Commit>? _physical;
    private const string ReceiptPrefix = "canonical.sqlite.compatible-task-context.v1.";

    public CanonicalProjectTaskContextCreateOwner(CanonicalProjectContextStoreReadOwner actualContexts,
        CanonicalSqliteOriginalStoreOwner actualStore, IDeveloperOriginalProjectCommandReadSource actualProjectReads,
        ICanonicalProjectTaskContextResumeSelectionSource? actualResumeSelections = null)
    {
        ArgumentNullException.ThrowIfNull(actualContexts); ArgumentNullException.ThrowIfNull(actualStore);
        ArgumentNullException.ThrowIfNull(actualProjectReads);
        if (!ReferenceEquals(actualContexts.OriginalStoreOwner, actualStore))
            throw new InvalidOperationException("The SAME actual protected SQLite read owner is required.");
        _contexts = actualContexts; _store = actualStore; _projectReads = actualProjectReads;
        _resumeSelections = actualResumeSelections;
    }
    public CanonicalProjectContextStoreReadOwner OriginalContextReadOwner => _contexts;
    public CanonicalSqliteOriginalStoreOwner OriginalStoreOwner => _store;
    public IDeveloperOriginalProjectCommandReadSource OriginalProjectReadSource => _projectReads;
    public ICanonicalProjectTaskContextResumeSelectionSource? OriginalResumeSelectionSource { get { lock (_gate) return _resumeSelections; } }
    public ICanonicalProjectTaskContextHomeWriteSource? OriginalHomeWriteSource { get { lock (_gate) return _writes; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    // Resolves the constructor composition cycle only. It issues no resource authority.
    public void BindOriginalHomeWriteSource(ICanonicalProjectTaskContextHomeWriteSource actualSource)
    {
        ArgumentNullException.ThrowIfNull(actualSource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_writes is not null) throw new InvalidOperationException("Original Home WRITE composition is already bound.");
            _writes = actualSource;
        }
    }
    // One-time process composition only; never a presentation-source replacement
    // or a Home/resource grant. The root compares the actual same object afterward.
    public void BindOriginalResumeSelectionSource(ICanonicalProjectTaskContextResumeSelectionSource actualSource)
    {
        ArgumentNullException.ThrowIfNull(actualSource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_resumeSelections is not null) throw new InvalidOperationException("Original Den resume composition is already bound.");
            _resumeSelections = actualSource;
        }
    }
    private bool IsInsideOriginal => _logical.Value != 0 ||
        (_originalPhysical?.TryGetValue(this, out var depth) == true && depth != 0);
    private Action<Action> WrapOriginalScope(Action<Action> caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return body => WithinOriginalPhysical(() => caller(body));
    }
    private void WithinOriginalPhysical(Action body)
    {
        var map = _originalPhysical ??= new(ReferenceEqualityComparer.Instance);
        map.TryGetValue(this, out var depth); map[this] = depth + 1;
        try { body(); } finally { if (depth == 0) map.Remove(this); else map[this] = depth; }
    }
    private bool IsCurrentAdmittedCommitValidation(Intent sameIntent)
    {
        var current = _physical is not null && _physical.TryGetValue(this, out var physical) ? physical : _executing.Value;
        return current is not null && ReferenceEquals(current.Intent, sameIntent) &&
            _commits.TryGetValue(sameIntent, out var attempt) && !attempt.Driver.IsCompleted;
    }
    private OriginalCommand ReserveOriginal(Intent? admittedValidation = null)
    {
        lock (_gate)
        {
            // Home may validate the SAME admitted commit while that original drains.
            // This admits no new prepare/commit or caller work after retirement.
            ObjectDisposedException.ThrowIf(_retiring && !(admittedValidation is not null &&
                IsCurrentAdmittedCommitValidation(admittedValidation)), this);
            _originals.RemoveAll(value => value.IndependentlyJoined && value.Raw is { } raw &&
                (raw.IsCompletedSuccessfully || IsAcknowledgedOriginalCreationRefusal(raw)));
            if (_originals.Count >= 128) throw new InvalidOperationException("Canonical creation original custody is full; unresolved evidence remains retained.");
            var record = new OriginalCommand(); _originals.Add(record); return record;
        }
    }
    private void PublishOriginal(OriginalCommand record, Task actual)
    {
        record.Raw = actual; record.Publication.TrySetResult(actual);
        record.Observation = ObserveOriginal(record);
    }
    private async Task ObserveOriginal(OriginalCommand record)
    {
        var raw = await record.Publication.Task.ConfigureAwait(false);
        try { await raw.ConfigureAwait(false); } catch { /* Exact terminal raw is retained for the owning drain. */ }
        lock (_gate) record.IndependentlyJoined = true;
    }
    private Task<T> AdmitOriginal<T>(Func<Task<T>> factory, Intent? admittedValidation = null)
    {
        var record = ReserveOriginal(admittedValidation);
        var previous = _logical.Value; _logical.Value = previous + 1;
        Task<T>? actual = null;
        try
        {
            WithinOriginalPhysical(() => actual = factory());
            if (actual is null) throw new InvalidOperationException("No actual canonical creation command Task was returned.");
        }
        catch (Exception cause) { actual = Task.FromException<T>(cause); }
        finally { _logical.Value = previous; }
        PublishOriginal(record, actual!); return actual!;
    }
    public void RequestOriginalRetirement() { lock (_gate) _retiring = true; }
    public void DemandExternalOriginalJoin()
    {
        if (IsInsideOriginal || _executing.Value is not null || _physical?.ContainsKey(this) == true)
            throw new InvalidOperationException("An actual canonical creation source cannot join its own encompassing originals.");
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); Task original; TaskCompletionSource? begin = null;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = DrainOriginals(begin.Task);
            }
            original = _close;
        }
        begin?.SetResult(); return original;
    }
    private async Task DrainOriginals(Task begin)
    {
        await begin.ConfigureAwait(false); var errors = new List<Exception>();
        var joined = new HashSet<OriginalCommand>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            OriginalCommand[] pending;
            lock (_gate) pending = _originals.Where(value => !joined.Contains(value)).ToArray();
            if (pending.Length == 0) break;
            foreach (var record in pending)
            {
                var raw = await record.Publication.Task.ConfigureAwait(false);
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (!IsAcknowledgedOriginalCreationRefusal(raw)) CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause);
                }
                if (record.Observation is { } observation) await observation.ConfigureAwait(false);
                joined.Add(record);
            }
        }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    private sealed record Receipt(int SchemaVersion, Guid StoreId, string ActorId, string ProfileId,
        Guid OperationId, Conversation Studio, ContainerDefinition StudioContainer,
        Conversation Task, ContainerDefinition TaskContainer);
    private sealed class Intent(CanonicalProjectTaskContextCreateOwner owner,
        ICanonicalProjectContextStoreObservation observation, IDeveloperOriginalProjectCommandRead read,
        Receipt receipt, ICanonicalProjectTaskContextResumeSelection? resumeSelection = null,
        VerifiedResourceStoreOwnership? originalDenOwnership = null) : ICanonicalProjectTaskContextCreationIntent
    {
        internal readonly CanonicalProjectTaskContextCreateOwner Owner = owner;
        internal readonly Receipt Durable = receipt;
        internal readonly ICanonicalProjectTaskContextResumeSelection? ResumeSelection = resumeSelection;
        internal readonly VerifiedResourceStoreOwnership? OriginalDenOwnership = originalDenOwnership;
        public AuthenticatedResourceActor Actor => observation.Actor;
        public ICanonicalProjectContextStoreObservation OriginalStoreObservation => observation;
        public IDeveloperOriginalProjectCommandRead OriginalProjectRead => read;
        public Conversation OriginalStudioConversation => Durable.Studio;
        public ContainerDefinition OriginalStudioContainer => Durable.StudioContainer;
        public Conversation TaskConversation => Durable.Task;
        public ContainerDefinition TaskContainer => Durable.TaskContainer;
        public Guid OperationId => Durable.OperationId;
    }
    private sealed class Creation(Intent intent) : ICanonicalProjectTaskContextCreation
    {
        public ICanonicalProjectTaskContextCreationIntent OriginalIntent => intent;
        public AuthenticatedResourceActor Actor => intent.Actor;
        public Conversation TaskConversation => intent.TaskConversation;
        public ContainerDefinition TaskContainer => intent.TaskContainer;
        public Guid OperationId => intent.OperationId;
    }
    private sealed class Commit(Intent intent)
    {
        internal readonly Intent Intent = intent;
        internal CanonicalSqliteOriginalStoreLease? Pin;
        internal IDeveloperOriginalProjectCommandNativeRead? ProjectPin;
        internal ICanonicalProjectTaskContextHomeWriteClaim? Claim;
        internal ICanonicalProjectTaskContextResumeRevisionPin? ResumePin;
        internal readonly TaskCompletionSource DispatchSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DispatchWait? OriginalDispatchWait;
        internal Task? AcknowledgedWriteAcquisition;
        internal Task<ICanonicalProjectTaskContextCreation>? Atomic;
        internal Creation? Acknowledgment;
    }
    public bool IsIssuedOriginalCreationIntent(ICanonicalProjectTaskContextCreationIntent intent) =>
        intent is Intent actual && ReferenceEquals(actual.Owner, this) &&
        _intents.TryGetValue(intent, out var same) && ReferenceEquals(same, actual);
    public bool IsIssuedOriginalCreation(ICanonicalProjectTaskContextCreation creation) =>
        creation is Creation actual && _creations.TryGetValue(creation, out var same) && ReferenceEquals(actual, same);
    public bool IsOriginalCreationCommitTask(ICanonicalProjectTaskContextCreationIntent intent,
        Task<ICanonicalProjectTaskContextCreation> task) => IsIssuedOriginalCreationIntent(intent) &&
        _atomic.TryGetValue(task, out var invocation) && ReferenceEquals(invocation.Intent, intent) && ReferenceEquals(invocation.Atomic, task);
    public bool IsOwnedOriginalCreationAcknowledgment(ICanonicalProjectTaskContextCreationIntent intent,
        ICanonicalProjectTaskContextCreation creation, Task<ICanonicalProjectTaskContextCreation> task) =>
        IsOriginalCreationCommitTask(intent, task) && _atomic.TryGetValue(task, out var invocation) &&
        ReferenceEquals(invocation.Acknowledgment, creation) && ReferenceEquals(creation.OriginalIntent, intent);
    public string GetOriginalCreationIntentDigest(ICanonicalProjectTaskContextCreationIntent intent)
    {
        var actual = RequireIntent(intent);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(actual.Durable)));
    }
    private Intent RequireIntent(ICanonicalProjectTaskContextCreationIntent intent) =>
        IsIssuedOriginalCreationIntent(intent) ? (Intent)intent : throw new UnauthorizedAccessException("This actual SQL owner did not issue the SAME creation intent.");

    public Task<ICanonicalProjectTaskContextCreationIntent> PrepareOriginalCreationIntentWithinSourceAsync(
        ICanonicalProjectContextStoreObservation observation, IDeveloperOriginalProjectCommandRead read,
        Guid conversationId, string title, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, WrapOriginalScope(scope), retain);
        return AdmitOriginal(() => _store.RetainOriginalReader<ICanonicalProjectTaskContextCreationIntent>(source, async () =>
        {
            if (conversationId == Guid.Empty || operationId == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Length > 512)
                throw new ArgumentException("A new conversation/operation identity and bounded title are required.");
            await DemandSourceAsync(observation, read, source, token).ConfigureAwait(false);
            var descriptor = read.OriginalDescriptor;
            var studio = observation.Conversations.Single(value => value.Id == descriptor.OriginalConversation.Id);
            var container = observation.Containers.Single(value => value.Id == studio.ContainerId);
            DemandStudioTuple(observation, read, studio, container);
            if (conversationId == studio.Id) throw new ArgumentException("A distinct Tasks conversation is required.");
            var receipt = await ReadReceiptAsync(observation.Actor, observation.OriginalStoreIdentity, operationId, source, token).ConfigureAwait(false);
            if (receipt is not null)
            {
                if (receipt.SchemaVersion != 1 || receipt.StoreId != observation.OriginalStoreIdentity.StoreId ||
                    receipt.ActorId != observation.Actor.ActorId || receipt.ProfileId != observation.Actor.ProfileId ||
                    receipt.OperationId != operationId || receipt.Studio != studio || receipt.StudioContainer != container ||
                    receipt.Task.Id != conversationId || receipt.Task.Title != title)
                    throw new InvalidDataException("Preserve the existing incompatible operation receipt for explicit recovery.");
            }
            else
            {
                lock (_gate)
                {
                    if (_operations.TryGetValue(operationId, out var weak) && weak.TryGetTarget(out var prior))
                    {
                        receipt = prior.Durable;
                        if (receipt.StoreId != observation.OriginalStoreIdentity.StoreId || receipt.ActorId != observation.Actor.ActorId ||
                            receipt.ProfileId != observation.Actor.ProfileId || receipt.Studio != studio || receipt.StudioContainer != container ||
                            receipt.Task.Id != conversationId || receipt.Task.Title != title)
                            throw new InvalidOperationException("The SAME operation is already prepared with a different immutable intent.");
                    }
                }
                if (receipt is null)
                {
                    var now = DateTimeOffset.UtcNow;
                    var taskContainer = new ContainerDefinition(Guid.NewGuid(), HavenMode.Tasks, container.Name,
                        descriptor.RegisteredProjectRoot, descriptor.ExactProjectReferenceJson, container.Instructions, now, now);
                    var task = new Conversation(conversationId, HavenMode.Tasks, ConversationKind.Task, title,
                        taskContainer.Id, null, false, false, now, now);
                    receipt = new(1, observation.OriginalStoreIdentity.StoreId, observation.Actor.ActorId,
                        observation.Actor.ProfileId, operationId, studio, container, task, taskContainer);
                }
            }
            await DemandSourceAsync(observation, read, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var intent = new Intent(this, observation, read, receipt);
            source.Run(() =>
            {
                lock (_gate)
                {
                    // Weak operation metadata is not live resource custody.
                    foreach (var dead in _operations.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) _operations.Remove(dead);
                    if (_operations.Count >= 128 && !_operations.ContainsKey(operationId))
                        throw new InvalidOperationException("Settle prepared creation metadata before another operation.");
                    if (_operations.TryGetValue(operationId, out var existing) && existing.TryGetTarget(out var original) && original.Durable != receipt)
                        throw new InvalidOperationException("The SAME operation acquired a different immutable prepared tuple. Refresh without changing the existing intent.");
                    _intents.Add(intent, intent); _operations[operationId] = new(intent);
                }
            });
            return intent;
        }));
    }
    public Task ValidateOriginalCreationIntentWithinSourceAsync(ICanonicalProjectTaskContextCreationIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var sameIntent = RequireIntent(intent);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, WrapOriginalScope(scope), retain);
        return AdmitOriginal(() => _store.RetainOriginalReader(source, async () =>
        {
            var actual = RequireIntent(intent);
            var committing = _physical is not null && _physical.TryGetValue(this, out var physical) ? physical : _executing.Value;
            if (actual.ResumeSelection is not null && committing?.ResumePin is not null && ReferenceEquals(committing.Intent, actual))
            {
                // A Home commit descendant under the SAME Den/Home pins may only use
                // the pure actual guards. It must not reacquire Den or Home state.
                source.Run(() => DemandOriginalCreationCommit(actual));
                await source.JoinAllAsync().ConfigureAwait(false); return 0;
            }
            await DemandResumeSelectionAsync(actual, source, token).ConfigureAwait(false);
            await DemandSourceAsync(actual.OriginalStoreObservation, actual.OriginalProjectRead, source, token).ConfigureAwait(false);
            DemandStudioTuple(actual.OriginalStoreObservation, actual.OriginalProjectRead,
                actual.OriginalStudioConversation, actual.OriginalStudioContainer);
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        }), admittedValidation: sameIntent);
    }
    private async Task DemandSourceAsync(ICanonicalProjectContextStoreObservation observation,
        IDeveloperOriginalProjectCommandRead read, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        if (!_contexts.IsIssuedOriginalObservation(observation) || !_projectReads.IsIssuedOriginalCommandRead(read))
            throw new UnauthorizedAccessException("The SAME actual canonical store observation and live Home project READ are required.");
        await source.Read(() => _contexts.RevalidateOriginalObservationWithinSourceAsync(observation,
            observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
        await source.Read(() => _projectReads.ValidateOriginalCommandReadWithinSourceAsync(read,
            source.Run, source.Retain, token)).ConfigureAwait(false);
    }
    private static void DemandStudioTuple(ICanonicalProjectContextStoreObservation observation,
        IDeveloperOriginalProjectCommandRead read, Conversation studio, ContainerDefinition container)
    {
        var descriptor = read.OriginalDescriptor; var identity = read.OriginalIdentity;
        if (observation.Conversations.Count != 1 || observation.Containers.Count != 1 || observation.HasMore ||
            studio.Mode != HavenMode.Studio || studio.Kind != ConversationKind.StudioChat || studio.IsArchived || studio.IsTemporary ||
            studio.SpaceId is not null || studio.LessonId is not null || studio.ContainerId != container.Id ||
            container.Mode != HavenMode.Studio || container.IsArchived || descriptor.OriginalConversation != studio ||
            descriptor.OriginalContainer != container || descriptor.OriginalActor != observation.Actor ||
            identity.OriginalHomeResourceActor != observation.Actor || identity.ContainerId != container.Id ||
            identity.CanonicalRoot != descriptor.RegisteredProjectRoot || identity.OriginalProjectContextJson != descriptor.ExactProjectReferenceJson)
            throw new UnauthorizedAccessException("The actual Studio row/container and privately read project tuple disagree.");
    }

    public Task<ICanonicalProjectTaskContextCreation> CommitOriginalCreationWithinSourceAsync(
        ICanonicalProjectTaskContextCreationIntent intent, IDeveloperOriginalProjectCommandRead read,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireIntent(intent);
        if (!ReferenceEquals(actual.OriginalProjectRead, read)) throw new UnauthorizedAccessException("The SAME intent-bound live project READ is required.");
        TaskCompletionSource begin; CommitAttempt attempt;
        lock (_gate)
        {
            if (_commits.TryGetValue(actual, out var existing)) return existing.Driver;
            var original = ReserveOriginal();
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            attempt = new();
            var invocation = new Commit(actual); attempt.Invocation = invocation;
            var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner,
                WrapOriginalScope(body => WithinPhysical(invocation, () => scope(body))), retain);
            attempt.Driver = Start(begin.Task, invocation, source, attempt);
            _publicCommits.Add(attempt.Driver, attempt);
            _commits.Add(actual, attempt); // SAME original before a source callback can reenter.
            PublishOriginal(original, attempt.Driver);
        }
        begin.SetResult(); return attempt.Driver;
        async Task<ICanonicalProjectTaskContextCreation> Start(Task start, Commit invocation, CanonicalSqliteOriginalSourceScope source, CommitAttempt originalAttempt)
        {
            await start.ConfigureAwait(false);
            var previous = _logical.Value; _logical.Value = previous + 1;
            try
            {
                originalAttempt.Inner = _store.RetainOriginalReader<ICanonicalProjectTaskContextCreation>(source,
                    () => CommitDriverAsync(invocation, source, token));
                _innerCommitSources.Add(originalAttempt.Inner, originalAttempt);
                return await originalAttempt.Inner.ConfigureAwait(false);
            }
            finally
            {
                // Even source admission failure before CommitDriver begins owns no
                // future acquisition. The actual child (when admitted) was awaited.
                invocation.DispatchSettled.TrySetResult(); _logical.Value = previous;
            }
        }
    }
    private async Task<ICanonicalProjectTaskContextCreation> CommitDriverAsync(Commit invocation,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var previous = _executing.Value; _executing.Value = invocation;
        var errors = new List<Exception>(); ICanonicalProjectTaskContextCreation? result = null;
        var intent = invocation.Intent;
        ICanonicalProjectTaskContextHomeWriteSource writes;
        try
        {
            writes = OriginalHomeWriteSource ?? throw new InvalidOperationException("Actual separate Home WRITE composition is unavailable.");
            await DemandResumeSelectionAsync(intent, source, token).ConfigureAwait(false);
            await DemandSourceAsync(intent.OriginalStoreObservation, intent.OriginalProjectRead, source, token).ConfigureAwait(false);
            // Real individual approval precedes acquiring either a SQLite writer reservation
            // or held Home commit entry. Capture partial claim inside the issuing callback.
            Task<ICanonicalProjectTaskContextHomeWriteClaim>? originalWrite = null;
            try
            {
                await source.ReadCapture(() => originalWrite = writes.AcquireOriginalWriteWithinSourceAsync(intent,
                    intent.OriginalProjectRead, source.Run, source.Retain,
                    claim =>
                    {
                        invocation.Claim = claim; source.CaptureOriginalResource(claim);
                        _writeAcquisitions.Add(claim.OriginalAcquisition, invocation);
                    }, token),
                    value => invocation.Claim = value).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (originalWrite is null || !source.InvokeOwningCleanup(() => writes.IsAcknowledgedOriginalWriteRefusal(originalWrite))) throw;
                // The actual Home owner confirms only its original no-capability/no-entry/
                // no-SQL review refusal after independent healthy raw joins. Close its
                // partial product before acknowledging this exact source occurrence.
                if (invocation.Claim is null) throw;
                // No productive acquisition or atomic dispatch follows this branch.
                invocation.DispatchSettled.TrySetResult();
                await source.CloseAsync(invocation.Claim).ConfigureAwait(false);
                if (!source.AcknowledgeOriginalExternalPreEffectRefusal(originalWrite, writes.IsAcknowledgedOriginalWriteRefusal)) throw;
                await source.JoinAllAsync().ConfigureAwait(false);
                if (!source.IsHealthySettled) throw;
                invocation.AcknowledgedWriteAcquisition = originalWrite;
                var declined = new InvalidOperationException("The separate Home WRITE review was declined before any canonical SQL effect.");
                source.RegisterOriginalPreEffectRefusal(declined); throw declined;
            }
            var actualClaim = invocation.Claim ?? throw new UnauthorizedAccessException("The actual Home owner returned no WRITE claim.");
            if (!writes.IsIssuedOriginalWriteClaim(actualClaim, intent)) throw new UnauthorizedAccessException("The actual Home owner did not issue this WRITE claim.");
            await DemandSourceAsync(intent.OriginalStoreObservation, intent.OriginalProjectRead, source, token).ConfigureAwait(false);
            await source.ReadCapture(() => _store.AcquireOriginalProtectedWriterPinWithinSourceAsync(
                intent.Actor, source.Run, source.Retain, token), value => invocation.Pin = value).ConfigureAwait(false);
            if (invocation.Pin!.OriginalIdentity != intent.OriginalStoreObservation.OriginalStoreIdentity)
                throw new UnauthorizedAccessException("The pinned actual SQLite identity changed before WRITE.");
            Task<IDeveloperOriginalProjectCommandNativeRead>? rawNative = null;
            try
            {
                invocation.ProjectPin = await source.Read(() => rawNative = _projectReads.CaptureOriginalCommandNativeReadWithinSourceAsync(
                    intent.OriginalProjectRead, source.Run, source.Retain, token)).ConfigureAwait(false);
            }
            finally
            {
                // Capture a successful original even if a caller rejects post-publication.
                if (rawNative?.IsCompletedSuccessfully == true)
                {
                    invocation.ProjectPin = rawNative.GetAwaiter().GetResult();
                    source.CaptureOriginalResource(invocation.ProjectPin.OriginalRead);
                }
            }
            if (invocation.ProjectPin is null || !_projectReads.IsIssuedOriginalCommandNativeRead(intent.OriginalProjectRead, invocation.ProjectPin))
                throw new UnauthorizedAccessException("The SAME project READ did not issue its retained native pin.");
            await source.Read(() => _projectReads.ValidateOriginalCommandNativeReadWithinSourceAsync(intent.OriginalProjectRead,
                invocation.ProjectPin, source.Run, source.Retain, token)).ConfigureAwait(false);
            await DemandResumeSelectionAsync(intent, source, token).ConfigureAwait(false);
            await DemandSourceAsync(intent.OriginalStoreObservation, intent.OriginalProjectRead, source, token).ConfigureAwait(false);
            // Actual Den exclusion follows all ordinary Home/native reads and the fresh
            // individual WRITE approval. No Home/Den reacquisition under this pin.
            if (intent.ResumeSelection is { } resumeSelection)
            {
                ICanonicalProjectTaskContextResumeSelectionSource resumeSource = null!;
                source.Run(() => { resumeSource = RequireResumeSource(resumeSelection); });
                await source.ReadCapture(() => resumeSource.AcquireOriginalResumeRevisionPinWithinSourceAsync(
                    resumeSelection, intent.Actor, source.Run, source.Retain,
                    pin => { invocation.ResumePin = pin; source.CaptureOriginalResource(pin); }, token),
                    pin => { invocation.ResumePin = pin; source.CaptureOriginalResource(pin); }).ConfigureAwait(false);
                if (invocation.ResumePin is null || !source.Invoke(() => resumeSource.IsIssuedOriginalResumeRevisionPin(resumeSelection, invocation.ResumePin)))
                    throw new UnauthorizedAccessException("The SAME Den source did not issue this actual resume revision pin.");
            }
            source.Run(() => DemandOriginalCreationCommit(intent));
            await source.Read(() => writes.AcquireOriginalCommitEntryWithinSourceAsync(invocation.Claim,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.Read(() => writes.ValidateOriginalCommitWithinSourceAsync(invocation.Claim,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rawAtomic = AtomicAsync(start.Task, invocation, source, writes, token);
            invocation.Atomic = rawAtomic; _atomic.Add(rawAtomic, invocation);
            Exception? acquisition = null;
            try
            {
                source.Run(() => { writes.RetainOriginalSqlCommit(invocation.Claim, rawAtomic); source.Retain(rawAtomic); });
            }
            catch (Exception cause) { acquisition = cause; }
            // Publication rejection does not strand an accepted raw task. Admission is
            // checked before any effect; cancellation is an actual child terminal result.
            start.TrySetResult(acquisition is null);
            try { result = await rawAtomic.ConfigureAwait(false); } catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, rawAtomic, cause); }
            if (acquisition is not null) errors.Add(acquisition);
            // Every accepted productive acquisition and actual atomic child has joined.
            // Settlement must wait this child barrier, never the encompassing driver.
            invocation.DispatchSettled.TrySetResult();
            try { await source.Read(() => writes.CompleteOriginalWriteWithinSourceAsync(invocation.Claim,
                rawAtomic, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            // Every productive source acquisition has independently returned/failed;
            // no late actual pin or SQL start can appear after this barrier publication.
            invocation.DispatchSettled.TrySetResult();
            // SQL/native custody is separate from presentation and manual review. Every
            // actual close is independently joined; no database/file cleanup is performed.
            if (invocation.Claim is not null)
                try { await source.CloseAsync(invocation.Claim).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            // Home completion/held-entry release and independently terminal atomic
            // child precede releasing the actual Den revision exclusion.
            if (invocation.ResumePin is not null)
                try { await source.CloseAsync(invocation.ResumePin).ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(cause); }
            if (invocation.ProjectPin is not null)
                try { await source.CloseAsync(invocation.ProjectPin.OriginalRead).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            if (invocation.Pin is not null)
                try { await invocation.Pin.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            _executing.Value = previous;
        }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return result ?? throw new InvalidOperationException("No healthy original create-only pair acknowledgment was observed.");
    }
    private async Task<ICanonicalProjectTaskContextCreation> AtomicAsync(Task<bool> start, Commit invocation,
        CanonicalSqliteOriginalSourceScope source, ICanonicalProjectTaskContextHomeWriteSource writes, CancellationToken token)
    {
        if (!await start.ConfigureAwait(false))
            throw new InvalidOperationException("The actual atomic child publication was declined before effects; no SQL was dispatched.");
        var intent = invocation.Intent; var pin = invocation.Pin!;
        void Demand() { DemandOriginalCreationCommit(intent); writes.DemandOriginalCommit(invocation.Claim!, intent); }
        source.Run(() => { token.ThrowIfCancellationRequested(); Demand(); pin.BeginPinnedOriginalWriter(Demand); });
        var studio = await CanonicalProjectContextStoreReadOwner.ReadConversationsAsync(pin, 1,
            intent.OriginalStudioConversation.Id, true, token).ConfigureAwait(false);
        var containers = await CanonicalProjectContextStoreReadOwner.ReadContainersAsync(pin, [intent.OriginalStudioContainer.Id], token).ConfigureAwait(false);
        if (studio.Count != 1 || studio[0] != intent.OriginalStudioConversation || containers.Count != 1 || containers[0] != intent.OriginalStudioContainer)
            throw new UnauthorizedAccessException("The actual Studio snapshot changed before the atomic create-only effect.");
        var existing = await ReadReceiptWithinLeaseAsync(pin, intent.OperationId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing != intent.Durable) throw new InvalidDataException("Preserve a conflicting operation receipt; never replay or overwrite it.");
            await DemandCreatedPairAsync(pin, intent, token).ConfigureAwait(false);
            // Recovery observes the SAME committed pair. It performs no second mutation.
        }
        else
        {
            await InsertPairAsync(pin, intent, Demand, token).ConfigureAwait(false);
        }
        await pin.CommitPinnedOriginalAsync(Demand, token).ConfigureAwait(false);
        // This private acknowledgment is registered by the actual atomic child only.
        // Home audits it only together with that exact child task's healthy terminal state.
        var acknowledgment = new Creation(intent);
        source.Run(() => { Demand(); invocation.Acknowledgment = acknowledgment; _creations.Add(acknowledgment, acknowledgment); });
        return acknowledgment;
    }
    public void DemandOriginalCreationCommit(ICanonicalProjectTaskContextCreationIntent intent)
    {
        var actual = RequireIntent(intent);
        var invocation = _physical is not null && _physical.TryGetValue(this, out var physical) ? physical : _executing.Value;
        if (invocation is null || !ReferenceEquals(invocation.Intent, actual) || invocation.Pin is null || invocation.ProjectPin is null ||
            !_store.IsIssuedOriginalLease(invocation.Pin) || invocation.Pin.OriginalIdentity != actual.OriginalStoreObservation.OriginalStoreIdentity)
            throw new UnauthorizedAccessException("No SAME retained actual SQL/project commit driver is active.");
        invocation.Pin.DemandPinnedOriginalPhysical();
        invocation.ProjectPin.OriginalRead.DemandOriginalExecutionBinding();
        if (actual.ResumeSelection is { } resumeSelection)
        {
            var resumeSource = RequireResumeSource(resumeSelection);
            if (invocation.ResumePin is null || !resumeSource.IsIssuedOriginalResumeRevisionPin(resumeSelection, invocation.ResumePin))
                throw new UnauthorizedAccessException("No SAME retained actual Den resume exclusion is active.");
            resumeSource.DemandOriginalPinnedResumeSelection(resumeSelection, invocation.ResumePin);
        }
    }
    private void WithinPhysical(Commit invocation, Action body)
    {
        var map = _physical ??= new(ReferenceEqualityComparer.Instance);
        map.TryGetValue(this, out var old); map[this] = invocation;
        try { body(); } finally { if (old is null) map.Remove(this); else map[this] = old; }
    }
    public Task RevalidateOriginalCreationWithinSourceAsync(ICanonicalProjectTaskContextCreation creation,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, WrapOriginalScope(scope), retain);
        return AdmitOriginal(() => _store.RetainOriginalReader(source, async () =>
        {
            if (!IsIssuedOriginalCreation(creation)) throw new UnauthorizedAccessException("No SAME owning create-only receipt was issued.");
            var intent = RequireIntent(creation.OriginalIntent);
            await DemandSourceAsync(intent.OriginalStoreObservation, intent.OriginalProjectRead, source, token).ConfigureAwait(false);
            CanonicalSqliteOriginalStoreLease? lease = null; var errors = new List<Exception>();
            try
            {
                await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(intent.Actor, false,
                    source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
                lease = lease ?? throw new InvalidOperationException("No actual original SQL read lease was captured.");
                var receipt = await ReadReceiptWithinLeaseAsync(lease, intent.OperationId, token).ConfigureAwait(false);
                if (receipt != intent.Durable) throw new InvalidDataException("The actual operation acknowledgment changed; preserve it for recovery.");
                await DemandCreatedPairAsync(lease, intent, token).ConfigureAwait(false);
                await lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token).ConfigureAwait(false);
            }
            catch (Exception cause) { errors.Add(cause); }
            if (lease is not null) try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            CanonicalSqliteOriginalStoreOwner.Throw(errors); return 0;
        }));
    }
    private async Task<Receipt?> ReadReceiptAsync(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        Guid operationId, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null;
        Receipt? result = null; var errors = new List<Exception>();
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
            if (lease!.OriginalIdentity != identity) throw new UnauthorizedAccessException("The actual SQLite identity changed.");
            result = await ReadReceiptWithinLeaseAsync(lease, operationId, token).ConfigureAwait(false);
            await lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        if (lease is not null) try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return result;
    }
    private static async Task<Receipt?> ReadReceiptWithinLeaseAsync(CanonicalSqliteOriginalStoreLease lease, Guid operationId, CancellationToken token)
    {
        var raw = await ExecuteScalarAsync(lease, "SELECT value FROM settings WHERE key=$key;", command =>
            command.Parameters.AddWithValue("$key", ReceiptPrefix + operationId.ToString("N")), token).ConfigureAwait(false);
        if (raw is null or DBNull) return null;
        try { return JsonSerializer.Deserialize<Receipt>((string)raw) ?? throw new JsonException("Empty operation receipt."); }
        catch (Exception cause) when (cause is JsonException or InvalidCastException)
        { throw new InvalidDataException("Preserve invalid canonical creation receipt bytes for recovery.", cause); }
    }
    private static async Task DemandCreatedPairAsync(CanonicalSqliteOriginalStoreLease lease, Intent intent, CancellationToken token)
    {
        var contexts = await CanonicalProjectContextStoreReadOwner.ReadConversationsAsync(lease, 1, intent.TaskConversation.Id, false, token).ConfigureAwait(false);
        var containers = await CanonicalProjectContextStoreReadOwner.ReadContainersAsync(lease, [intent.TaskContainer.Id], token).ConfigureAwait(false);
        if (contexts.Count != 1 || contexts[0] != intent.TaskConversation || containers.Count != 1 || containers[0] != intent.TaskContainer)
            throw new InvalidDataException("The actual committed Tasks pair changed; preserve existing history for recovery.");
    }
    private static async Task InsertPairAsync(CanonicalSqliteOriginalStoreLease lease, Intent intent, Action demand, CancellationToken token)
    {
        var container = intent.TaskContainer; var conversation = intent.TaskConversation;
        await ExecuteNonQueryAsync(lease, """
            INSERT INTO containers(id,mode,name,root_path,context,instructions,created_at,updated_at,is_archived)
            VALUES($id,$mode,$name,$root,$context,$instructions,$created,$updated,0);
            """, command =>
        {
            demand(); command.Parameters.AddWithValue("$id", container.Id.ToString("D")); command.Parameters.AddWithValue("$mode", (int)HavenMode.Tasks);
            command.Parameters.AddWithValue("$name", container.Name); command.Parameters.AddWithValue("$root", container.RootPath!);
            command.Parameters.AddWithValue("$context", container.Context); command.Parameters.AddWithValue("$instructions", container.Instructions);
            command.Parameters.AddWithValue("$created", container.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", container.UpdatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
        await ExecuteNonQueryAsync(lease, """
            INSERT INTO conversations(id,mode,kind,title,container_id,lesson_id,is_pinned,is_temporary,created_at,updated_at,is_archived,parent_conversation_id,compacted_at,space_id)
            VALUES($id,$mode,$kind,$title,$container,NULL,0,0,$created,$updated,0,NULL,NULL,NULL);
            """, command =>
        {
            demand(); command.Parameters.AddWithValue("$id", conversation.Id.ToString("D")); command.Parameters.AddWithValue("$mode", (int)HavenMode.Tasks);
            command.Parameters.AddWithValue("$kind", (int)ConversationKind.Task); command.Parameters.AddWithValue("$title", conversation.Title);
            command.Parameters.AddWithValue("$container", container.Id.ToString("D")); command.Parameters.AddWithValue("$created", conversation.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updated", conversation.UpdatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
        await ExecuteNonQueryAsync(lease, "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$at);", command =>
        {
            demand(); command.Parameters.AddWithValue("$key", ReceiptPrefix + intent.OperationId.ToString("N"));
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(intent.Durable));
            command.Parameters.AddWithValue("$at", intent.TaskConversation.CreatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
    }
    private static async Task<object?> ExecuteScalarAsync(CanonicalSqliteOriginalStoreLease lease,
        string sql, Action<SqliteCommand> configure, CancellationToken token)
    {
        var command = lease.CreateOriginalCommand(); object? result = null; var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => { command.CommandText = sql; configure(command); });
            result = await lease.ReadOriginalSourceAsync(() => command.ExecuteScalarAsync(token)).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return result;
    }
    private static async Task ExecuteNonQueryAsync(CanonicalSqliteOriginalStoreLease lease,
        string sql, Action<SqliteCommand> configure, CancellationToken token)
    {
        var command = lease.CreateOriginalCommand(); var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() => { command.CommandText = sql; configure(command); });
            if (await lease.ReadOriginalSourceAsync(() => command.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new InvalidDataException("The actual create-only SQL did not affect exactly one new row.");
        }
        catch (Exception cause) { errors.Add(cause); }
        try { await lease.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
}
