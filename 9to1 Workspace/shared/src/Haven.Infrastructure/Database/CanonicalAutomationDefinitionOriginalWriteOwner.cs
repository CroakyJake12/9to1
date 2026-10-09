using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Recover/disable ONLY a SAME protected library row. Its original raw
/// row, native store and individually accepted Home WRITE are rechecked before
/// one atomic transaction. Scheduling and Task/provider admission remain absent.</summary>
public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner : ICanonicalAutomationDefinitionOriginalProcessSource, IAsyncDisposable
{
    private readonly CanonicalAutomationLibraryOriginalReadOwner _library;
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly object _gate = new();
    private readonly List<Original> _originals = [];
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalChangeIntent, Intent> _intents = new();
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment, Acknowledgment> _acknowledgments = new();
    private readonly ConditionalWeakTable<Intent, Commit> _commits = new();
    private readonly ConditionalWeakTable<Task, Commit> _commitSources = new();
    private readonly ConditionalWeakTable<Task, CanonicalSqliteOriginalSourceScope> _commandSources = new();
    private readonly ConditionalWeakTable<Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>, Commit> _atomic = new();
    private readonly AsyncLocal<Commit?> _executing = new();
    private readonly AsyncLocal<int> _logical = new();
    [ThreadStatic] private static Dictionary<CanonicalAutomationDefinitionOriginalWriteOwner, Commit?>? _physical;
    private ICanonicalAutomationDefinitionOriginalHomeWriteSource? _writes;
    private bool _retiring;
    private Task? _close;
    private const string DescriptorPrefix = "canonical.automation-owner.v1.";
    private const string ReceiptPrefix = "canonical.automation-operation.v1.";
    public CanonicalAutomationDefinitionOriginalWriteOwner(CanonicalAutomationLibraryOriginalReadOwner sameLibrary)
    { ArgumentNullException.ThrowIfNull(sameLibrary); _library = sameLibrary; _store = sameLibrary.OriginalStore; }
    public CanonicalAutomationLibraryOriginalReadOwner OriginalLibrary => _library;
    public CanonicalSqliteOriginalStoreOwner OriginalStore => _store;
    public AutomationRepository OriginalRepository => _library.OriginalRepository;
    public ICanonicalAutomationDefinitionOriginalHomeWriteSource? OriginalHomeWriteSource { get { lock (_gate) return _writes; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void BindOriginalHomeWriteSource(ICanonicalAutomationDefinitionOriginalHomeWriteSource actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (actual is not HomeCanonicalAutomationDefinitionWriteSource sameHome ||
            !sameHome.HasOriginalProducerComposition(_store.OriginalProfiles, this))
            throw new UnauthorizedAccessException("Bind the SAME sealed Home source over the actual protected store profile and this producer.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_writes is not null) throw new InvalidOperationException("The original Automation Home WRITE is already bound.");
            _writes = actual;
        }
    }
    private sealed class Original
    {
        internal readonly TaskCompletionSource<Task> Publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Raw;
        internal readonly CanonicalSqliteOriginalSourceScope Source;
        internal Task Observation = null!;
        internal bool Joined;
        internal Original(CanonicalSqliteOriginalSourceScope source) => Source = source;
    }
    private sealed record Descriptor(int SchemaVersion, Guid StoreId, Guid EntityId, long Revision,
        AutomationOwnerBinding Owner, AutomationOperationalState State, Guid OperationId,
        string BeforeRowSha256, string AfterRowSha256, string? PreviousDescriptorSha256,
        AutomationOwnerCommitReceipt Receipt);
    private sealed record DurableOperation(int SchemaVersion, Guid StoreId, Guid EntityId, Guid OperationId,
        CanonicalAutomationOriginalChangeKind Kind, string IntentSha256, string BeforeRowSha256,
        string AfterRowSha256, Descriptor Descriptor);
    private sealed class Intent(CanonicalAutomationDefinitionOriginalWriteOwner owner,
        CanonicalAutomationLibraryOriginalReadOwner.OriginalWriteSelection selection,
        CanonicalAutomationOriginalChangeKind kind, Guid operationId, Snapshot snapshot) : ICanonicalAutomationDefinitionOriginalChangeIntent
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly CanonicalAutomationLibraryOriginalReadOwner.OriginalWriteSelection Selection = selection;
        internal readonly Snapshot Snapshot = snapshot;
        public AuthenticatedResourceActor Actor => Selection.Observation.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => Selection.Observation.OriginalStoreIdentity!;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => Selection.Ownership;
        public AutomationDefinition OriginalDefinition => Selection.Row.Value;
        public CanonicalAutomationOriginalChangeKind ChangeKind { get; } = kind;
        public string ActionId => ChangeKind == CanonicalAutomationOriginalChangeKind.RecoverLegacy ? "automations.recover" : "automations.disable";
        public Guid OperationId { get; } = operationId;
        public long ExpectedRevision => Snapshot.Descriptor?.Revision ?? 0;
        public string OriginalRowSha256 => Snapshot.RowSha256;
        public bool IsRecoveredOperation => Snapshot.Operation is not null;
    }
    private sealed class Acknowledgment(Intent intent, DurableOperation operation) : ICanonicalAutomationDefinitionOriginalChangeAcknowledgment
    {
        internal readonly DurableOperation Durable = operation;
        private readonly Intent _intent = intent;
        public ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent => _intent;
        public Guid OperationId => Durable.OperationId;
        public AutomationOwnerCommitReceipt OriginalReceipt => Durable.Descriptor.Receipt;
        public AutomationDefinition Definition => _intent.OriginalDefinition with
        { IsEnabled = false, Revision = Durable.Descriptor.Revision, OwnerBinding = Durable.Descriptor.Owner,
            OperationalState = Durable.Descriptor.State, LastOwnerCommit = Durable.Descriptor.Receipt };
        public bool IsRecoveredOperation => _intent.IsRecoveredOperation;
    }
    private sealed class Commit(Intent intent, CanonicalSqliteOriginalSourceScope source)
    {
        internal readonly Intent Intent = intent;
        internal readonly CanonicalSqliteOriginalSourceScope Source = source;
        internal Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> Driver = null!;
        internal Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? Inner, Atomic;
        internal Task? AcknowledgedAcquisition;
        internal ICanonicalAutomationDefinitionOriginalHomeWriteClaim? Claim;
        internal CanonicalSqliteOriginalStoreLease? Pin;
        internal Acknowledgment? Acknowledgment;
        internal Task? Withdrawal, NativeClose;
        internal bool NativeCloseJoined, DispatchSettled;
    }
    private Action<Action> Scope(Action<Action> caller, Commit? commit = null) => body =>
    {
        var map = _physical ??= new(ReferenceEqualityComparer.Instance);
        var hadPrior = map.TryGetValue(this, out var prior); map[this] = commit ?? prior;
        var active = 1; var invoked = 0; var thread = Environment.CurrentManagedThreadId;
        var failures = new List<Exception>(); var invocationGate = new object();
        void Remember(Exception cause) { lock (invocationGate) failures.Add(cause); }
        void Actual()
        {
            if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref invoked, 1) != 0)
            {
                var protocol = new InvalidOperationException("The Automation original callback is inactive, foreign-thread or already consumed.");
                Remember(protocol); throw protocol;
            }
            try { body(); } catch (Exception cause) { Remember(cause); throw; }
        }
        try
        {
            try { caller(Actual); } catch (Exception cause) { Remember(cause); }
            if (Volatile.Read(ref invoked) == 0) Remember(new InvalidOperationException("The actual Automation callback was not invoked."));
            Exception[] observed; lock (invocationGate) observed = failures.ToArray();
            CanonicalSqliteOriginalStoreOwner.Throw(observed.ToList());
        }
        finally
        {
            Volatile.Write(ref active, 0);
            if (hadPrior) map[this] = prior; else map.Remove(this);
        }
    };
    private bool IsInside => _logical.Value != 0 || _executing.Value is not null || _physical?.ContainsKey(this) == true;
    private Original Reserve(CanonicalSqliteOriginalSourceScope source, Intent? sameValidation = null)
    {
        lock (_gate)
        {
            var current = _physical?.TryGetValue(this, out var physical) == true ? physical : _executing.Value;
            var admittedSameCommit = sameValidation is not null && current is not null && ReferenceEquals(current.Intent, sameValidation) && !current.Driver.IsCompleted;
            ObjectDisposedException.ThrowIf(_retiring && !admittedSameCommit, this);
            _originals.RemoveAll(item => item.Joined && item.Raw is { } raw && (raw.IsCompletedSuccessfully || IsAcknowledgedOriginalChangeSourceRefusal(raw)));
            if (_originals.Count >= 128) throw new InvalidOperationException("Automation change custody is full; unresolved originals remain retained.");
            var record = new Original(source); _originals.Add(record); return record;
        }
    }
    private void Publish(Original record, Task actual)
    {
        record.Observation = Observe();
        _commandSources.Add(actual, record.Source);
        record.Raw = actual; record.Publication.TrySetResult(actual);
        async Task Observe()
        { var raw = await record.Publication.Task.ConfigureAwait(false); try { await raw.ConfigureAwait(false); } catch { } lock (_gate) record.Joined = true; }
    }
    private Task<T> Admit<T>(CanonicalSqliteOriginalSourceScope source, Func<Task<T>> factory, Intent? sameValidation = null)
    {
        var record = Reserve(source, sameValidation); Task<T> actual; var previous = _logical.Value;
        _logical.Value = previous + 1;
        try { actual = factory() ?? throw new InvalidOperationException("The actual automation definition WRITE command returned no Task."); }
        catch (Exception cause) { actual = Task.FromException<T>(cause); }
        finally { _logical.Value = previous; }
        Publish(record, actual); return actual;
    }
    public bool IsIssuedOriginalChangeIntent(ICanonicalAutomationDefinitionOriginalChangeIntent value) =>
        value is Intent actual && ReferenceEquals(actual.Owner, this) && _intents.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
    private Intent RequireIntent(ICanonicalAutomationDefinitionOriginalChangeIntent value) => IsIssuedOriginalChangeIntent(value)
        ? (Intent)value : throw new UnauthorizedAccessException("The SAME configured Automation writer must issue this intent.");
    public string GetOriginalChangeIntentDigest(ICanonicalAutomationDefinitionOriginalChangeIntent value)
    {
        var actual = RequireIntent(value);
        return ReviewDigest(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalDefinition.Id,
            actual.OperationId, actual.ChangeKind, actual.Snapshot);
    }
    private static string ReviewDigest(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        Guid entity, Guid operation, CanonicalAutomationOriginalChangeKind kind, Snapshot snapshot) =>
        Hash(new { actor, identity, entity, operation, kind, OriginalRow = snapshot.Operation?.BeforeRowSha256 ?? snapshot.RowSha256,
            ExpectedRevision = snapshot.Operation?.Descriptor.Receipt.ExpectedRevision ?? snapshot.Descriptor?.Revision ?? 0,
            PreviousDescriptorSha256 = snapshot.Operation is { } operationReceipt ? operationReceipt.Descriptor.PreviousDescriptorSha256 : (snapshot.DescriptorJson is null ? null : RawJsonDigest(snapshot.DescriptorJson)) });
    public Task<ICanonicalAutomationDefinitionOriginalChangeIntent> PrepareOriginalChangeWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, AutomationOwnerRead<AutomationDefinition> sameRow,
        CanonicalAutomationOriginalChangeKind kind, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<ICanonicalAutomationDefinitionOriginalChangeIntent>(source, async () =>
        {
            if (operationId == Guid.Empty || kind is not (CanonicalAutomationOriginalChangeKind.RecoverLegacy or CanonicalAutomationOriginalChangeKind.Disable))
                throw new ArgumentException("A supported exact change and stable original operation ID are required.");
            var selected = source.Invoke(() => _library.ObserveOriginalWriteSelection(sameObservation, sameRow));
            await source.Read(() => _library.RevalidateOriginalObservationWithinSourceAsync(selected.Observation,
                selected.Observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = selected.Observation.OriginalStoreIdentity!;
            await DemandCurrentAsync(selected.Observation.Actor, identity, selected.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(selected.Observation.Actor, identity, selected.Row.Value.Id,
                RawText(selected.Row.RetainedProtectedDescriptors, "id"), operationId, source, token).ConfigureAwait(false);
            if (snapshot.RowSha256 != AutomationDefinitionChange.ComputeRawRowSHA256(selected.Row.RetainedProtectedDescriptors))
                throw new UnauthorizedAccessException("The full actual selected row changed before review; refresh its protected page.");
            if (HasLegacyLease(snapshot.Row))
            {
                await source.JoinAllAsync().ConfigureAwait(false);
                var refusal = new InvalidOperationException("The actual selected legacy run lease remains stored; recovery/disable cannot interrupt or adopt it.");
                source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
            }
            DemandNoLegacyLease(snapshot.Row);
            DemandDescriptor(snapshot, selected.Observation.Actor, identity, selected.Row.Value.Id);
            if (snapshot.Operation is { } old)
                DemandDurableOperation(old, snapshot, selected.Observation.Actor, identity, selected.Row.Value.Id,
                    operationId, kind, ReviewDigest(selected.Observation.Actor, identity, selected.Row.Value.Id, operationId, kind, snapshot));
            await DemandCurrentAsync(selected.Observation.Actor, identity, selected.Ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var intent = new Intent(this, selected, kind, operationId, snapshot);
            source.Run(() => _intents.Add(intent, intent)); return intent;
        }));
    }
    public Task RevalidateOriginalChangeIntentWithinSourceAsync(ICanonicalAutomationDefinitionOriginalChangeIntent value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireIntent(value); var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader(source, async () =>
        {
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalDefinition.Id,
                RawText(actual.Selection.Row.RetainedProtectedDescriptors, "id"), actual.OperationId, source, token).ConfigureAwait(false);
            if (Hash(snapshot) != Hash(actual.Snapshot))
                throw new UnauthorizedAccessException("The full reviewed row/owner descriptor/operation changed; obtain a fresh observation and approval.");
            DemandNoLegacyLease(snapshot.Row);
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        }), actual);
    }
    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("The Automation review snapshot exceeds its returned metadata bound.");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private async Task DemandCurrentAsync(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        VerifiedResourceStoreOwnership receipt, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        await _store.DemandActorAsync(actor, source, token).ConfigureAwait(false);
        if (receipt.ResourceKind != "canonical.sqlite" || receipt.Receipt is null || receipt.ProfileId != actor.ProfileId ||
            receipt.StoreId != identity.StoreId.ToString("D") ||
            !await source.Read(() => _library.OriginalOwnership.IsCurrentWithinOriginalSourceAsync(receipt, actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("A current SAME canonical.sqlite Home receipt is required; READ grants no recovery/disable WRITE.");
        if (await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor,
            source.Run, source.Retain, token)).ConfigureAwait(false) != identity)
            throw new UnauthorizedAccessException("The actual canonical SQLite identity changed.");
    }
}
