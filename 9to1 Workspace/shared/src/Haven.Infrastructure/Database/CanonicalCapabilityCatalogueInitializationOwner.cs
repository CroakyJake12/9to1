using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Explicit first-use setup over the SAME maintained capability repository.
/// READ never seeds; separate individual Home WRITE inserts missing builtins only.
/// Existing disabled/custom rows and ordinary discovery behavior are preserved.</summary>
public sealed partial class CanonicalCapabilityCatalogueInitializationOwner : ICapabilityOriginalInitializationProcessSource, IAsyncDisposable
{
    private readonly CanonicalCapabilityCatalogueReadOwner _catalogue;
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly object _gate = new();
    private readonly List<Original> _originals = [];
    private readonly ConditionalWeakTable<ICapabilityOriginalInitializationIntent, Intent> _intents = new();
    private readonly ConditionalWeakTable<ICapabilityOriginalInitializationAcknowledgment, Acknowledgment> _acknowledgments = new();
    private readonly ConditionalWeakTable<Intent, Commit> _commits = new();
    private readonly ConditionalWeakTable<Task, Commit> _commitSources = new();
    private readonly ConditionalWeakTable<Task, CanonicalSqliteOriginalSourceScope> _commandSources = new();
    private readonly ConditionalWeakTable<Task<ICapabilityOriginalInitializationAcknowledgment>, Commit> _atomic = new();
    private readonly AsyncLocal<Commit?> _executing = new();
    private readonly AsyncLocal<int> _logical = new();
    [ThreadStatic] private static Dictionary<CanonicalCapabilityCatalogueInitializationOwner, Commit?>? _physical;
    private ICapabilityOriginalInitializationHomeWriteSource? _writes;
    private bool _retiring;
    private Task? _close;
    private const string ReceiptPrefix = "canonical.capability-initialization.v1.";

    public CanonicalCapabilityCatalogueInitializationOwner(CanonicalCapabilityCatalogueReadOwner sameCatalogue)
    { ArgumentNullException.ThrowIfNull(sameCatalogue); _catalogue = sameCatalogue; _store = sameCatalogue.OriginalStore; }
    public CanonicalCapabilityCatalogueReadOwner OriginalCatalogue => _catalogue;
    public CanonicalSqliteOriginalStoreOwner OriginalStore => _store;
    public CapabilityRepository OriginalRepository => _catalogue.OriginalRepository;
    public ICapabilityOriginalInitializationHomeWriteSource? OriginalHomeWriteSource { get { lock (_gate) return _writes; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void BindOriginalHomeWriteSource(ICapabilityOriginalInitializationHomeWriteSource actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_writes is not null) throw new InvalidOperationException("Original Home capability setup composition is already bound.");
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
    private sealed record Receipt(int SchemaVersion, Guid StoreId, string ActorId, string ProfileId,
        Guid OperationId, string BuiltInsSha256, string BeforeSha256, string AfterSha256,
        IReadOnlyList<Guid> InsertedIds, DateTimeOffset CompletedAtUtc);
    private sealed class Intent(CanonicalCapabilityCatalogueInitializationOwner owner,
        AuthenticatedResourceActor actor, ResourceStoreIdentity identity, VerifiedResourceStoreOwnership ownership,
        Guid operationId, IReadOnlyList<CapabilityDefinition> missing, string snapshotSha256,
        string builtInsSha256, Receipt? recovered) : ICapabilityOriginalInitializationIntent
    {
        internal readonly CanonicalCapabilityCatalogueInitializationOwner Owner = owner;
        internal readonly string SnapshotSha256 = snapshotSha256, BuiltInsSha256 = builtInsSha256;
        internal readonly Receipt? Recovered = recovered;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public ResourceStoreIdentity OriginalStoreIdentity { get; } = identity;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership { get; } = ownership;
        public Guid OperationId { get; } = operationId;
        public IReadOnlyList<CapabilityDefinition> MissingDefinitions { get; } = missing;
        public bool IsRecoveredOperation => Recovered is not null;
    }
    private sealed class Acknowledgment(Intent intent, Receipt receipt) : ICapabilityOriginalInitializationAcknowledgment
    {
        internal readonly Receipt Durable = receipt;
        public ICapabilityOriginalInitializationIntent OriginalIntent { get; } = intent;
        public Guid OperationId => Durable.OperationId;
        public IReadOnlyList<Guid> InsertedDefinitionIds { get; } = Array.AsReadOnly(receipt.InsertedIds.ToArray());
        public bool IsRecoveredOperation => OriginalIntent.IsRecoveredOperation;
    }
    private sealed class Commit(Intent intent, CanonicalSqliteOriginalSourceScope source)
    {
        internal readonly Intent Intent = intent;
        internal readonly CanonicalSqliteOriginalSourceScope Source = source;
        internal Task<ICapabilityOriginalInitializationAcknowledgment> Driver = null!;
        internal Task<ICapabilityOriginalInitializationAcknowledgment>? Inner, Atomic;
        internal Task? AcknowledgedAcquisition;
        internal ICapabilityOriginalInitializationHomeWriteClaim? Claim;
        internal CanonicalSqliteOriginalStoreLease? Pin;
        internal Acknowledgment? Acknowledgment;
    }
    private Action<Action> Scope(Action<Action> caller, Commit? commit = null) => body =>
    {
        var map = _physical ??= new(ReferenceEqualityComparer.Instance);
        var hadPrior = map.TryGetValue(this, out var prior); map[this] = commit ?? prior;
        try { caller(body); } finally { if (hadPrior) map[this] = prior; else map.Remove(this); }
    };
    private bool IsInside => _logical.Value != 0 || _executing.Value is not null || _physical?.ContainsKey(this) == true;
    private Original Reserve(CanonicalSqliteOriginalSourceScope source, Intent? sameValidation = null)
    {
        lock (_gate)
        {
            var current = _physical?.TryGetValue(this, out var physical) == true ? physical : _executing.Value;
            var admittedSameCommit = sameValidation is not null && current is not null && ReferenceEquals(current.Intent, sameValidation) && !current.Driver.IsCompleted;
            ObjectDisposedException.ThrowIf(_retiring && !admittedSameCommit, this);
            _originals.RemoveAll(item => item.Joined && item.Raw is { } raw && (raw.IsCompletedSuccessfully || IsAcknowledgedOriginalInitializationSourceRefusal(raw)));
            if (_originals.Count >= 128) throw new InvalidOperationException("Capability setup custody is full; unresolved originals remain retained.");
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
        try { actual = factory() ?? throw new InvalidOperationException("The actual capability setup command returned no Task."); }
        catch (Exception cause) { actual = Task.FromException<T>(cause); }
        finally { _logical.Value = previous; }
        Publish(record, actual); return actual;
    }
    public bool IsIssuedOriginalInitializationIntent(ICapabilityOriginalInitializationIntent value) =>
        value is Intent actual && ReferenceEquals(actual.Owner, this) && _intents.TryGetValue(value, out var issued) && ReferenceEquals(actual, issued);
    private Intent RequireIntent(ICapabilityOriginalInitializationIntent value) => IsIssuedOriginalInitializationIntent(value)
        ? (Intent)value : throw new UnauthorizedAccessException("The SAME configured capability setup owner did not issue this intent.");
    public string GetOriginalInitializationIntentDigest(ICapabilityOriginalInitializationIntent value)
    {
        var actual = RequireIntent(value);
        return Hash(new { actual.Actor, actual.OriginalStoreIdentity, actual.OperationId,
            actual.SnapshotSha256, actual.BuiltInsSha256, actual.MissingDefinitions, actual.Recovered,
            Ownership = new { actual.OriginalStoreOwnership.ResourceKind, actual.OriginalStoreOwnership.StoreId,
                actual.OriginalStoreOwnership.ProfileId, actual.OriginalStoreOwnership.ObservedStoreRevision } });
    }
    public Task<ICapabilityOriginalInitializationIntent> PrepareOriginalInitializationWithinSourceAsync(
        AuthenticatedResourceActor actor, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader<ICapabilityOriginalInitializationIntent>(source, async () =>
        {
            if (operationId == Guid.Empty) throw new ArgumentException("A stable capability setup operation ID is required.", nameof(operationId));
            var identity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            var ownership = await ReadOwnershipAsync(actor, identity, source, token).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(actor, identity, operationId, source, token).ConfigureAwait(false);
            var builtIns = Array.AsReadOnly(CapabilityRegistryCatalog.BuiltIns.ToArray());
            var builtInsSha256 = Hash(builtIns);
            var recovered = snapshot.Receipt;
            if (recovered is not null) DemandReceipt(recovered, actor, identity, operationId, builtInsSha256);
            var missing = recovered is null ? builtIns.Where(item => !snapshot.Rows.Any(row => row.Id == item.Id)).ToArray() : [];
            if (missing.Any(item => snapshot.Rows.Any(row => row.Id != item.Id && string.Equals(row.Key, item.Key, StringComparison.Ordinal))))
                throw new InvalidDataException("Preserve a conflicting capability key for explicit recovery; setup never replaces custom rows.");
            await DemandCurrentAsync(actor, identity, ownership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var intent = new Intent(this, actor, identity, ownership, operationId,
                Array.AsReadOnly(missing), snapshot.Sha256, builtInsSha256, recovered);
            source.Run(() => _intents.Add(intent, intent)); return intent;
        }));
    }
    public Task RevalidateOriginalInitializationIntentWithinSourceAsync(ICapabilityOriginalInitializationIntent value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireIntent(value);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, () => _store.RetainOriginalReader(source, async () =>
        {
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OperationId, source, token).ConfigureAwait(false);
            if (snapshot.Sha256 != actual.SnapshotSha256 || Hash(CapabilityRegistryCatalog.BuiltIns) != actual.BuiltInsSha256 ||
                !SameReceipt(snapshot.Receipt, actual.Recovered))
                throw new UnauthorizedAccessException("The reviewed actual capability catalogue changed. Refresh setup before approval.");
            await DemandCurrentAsync(actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        }), actual);
    }
    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("The actual capability setup snapshot exceeds its bound.");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static bool SameReceipt(Receipt? left, Receipt? right) =>
        left is null ? right is null : right is not null && Hash(left) == Hash(right);
    private static void DemandReceipt(Receipt actual, AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        Guid operationId, string builtInsSha256)
    {
        if (actual.SchemaVersion != 1 || actual.StoreId != identity.StoreId || actual.ActorId != actor.ActorId || actual.ProfileId != actor.ProfileId ||
            actual.OperationId != operationId || actual.BuiltInsSha256 != builtInsSha256 || actual.CompletedAtUtc == default ||
            actual.InsertedIds is null || actual.InsertedIds.Count > CapabilityRegistryCatalog.BuiltIns.Count ||
            actual.InsertedIds.Distinct().Count() != actual.InsertedIds.Count ||
            actual.InsertedIds.Any(id => !CapabilityRegistryCatalog.BuiltIns.Any(item => item.Id == id)) ||
            actual.BeforeSha256.Length != 64 || actual.AfterSha256.Length != 64)
            throw new InvalidDataException("Preserve the conflicting capability initialization receipt for recovery.");
    }
    private async Task<VerifiedResourceStoreOwnership> ReadOwnershipAsync(AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var receipt = await source.Read(() => _catalogue.OriginalOwnership.GetVerifiedWithinOriginalSourceAsync("canonical.sqlite",
            identity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (receipt is null)
        {
            await source.JoinAllAsync().ConfigureAwait(false);
            var refusal = new InvalidOperationException("Import this actual canonical SQLite store through Home before capability setup.");
            source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
        }
        await DemandCurrentAsync(actor, identity, receipt, source, token).ConfigureAwait(false); return receipt;
    }
    private async Task DemandCurrentAsync(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        VerifiedResourceStoreOwnership receipt, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        await _store.DemandActorAsync(actor, source, token).ConfigureAwait(false);
        if (receipt.ResourceKind != "canonical.sqlite" || receipt.Receipt is null || receipt.ProfileId != actor.ProfileId ||
            receipt.StoreId != identity.StoreId.ToString("D") ||
            !await source.Read(() => _catalogue.OriginalOwnership.IsCurrentWithinOriginalSourceAsync(receipt, actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual canonical.sqlite Home receipt is not current; it grants no capability setup WRITE.");
        if (await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor, source.Run, source.Retain, token)).ConfigureAwait(false) != identity)
            throw new UnauthorizedAccessException("The actual protected store identity changed before capability setup.");
    }
    public void RequestOriginalRetirement()
    {
        lock (_gate) _retiring = true;
        RequestOriginalPendingReviewWithdrawals();
        RetireOriginalDeliveries();
    }
    public void DemandExternalOriginalJoin()
    { if (IsInside) throw new InvalidOperationException("An actual capability setup source cannot join its own original close."); }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalRetirement(); Task result; TaskCompletionSource? begin = null;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(begin.Task); }
            result = _close;
        }
        begin?.SetResult(); return result;
    }
    private async Task Drain(Task begin)
    {
        await begin.ConfigureAwait(false); var errors = new List<Exception>(); var joined = new HashSet<Original>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Original[] pending; lock (_gate) pending = _originals.Where(item => joined.Add(item)).ToArray();
            if (pending.Length == 0) break;
            foreach (var item in pending)
            {
                var raw = await item.Publication.Task.ConfigureAwait(false);
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause) { if (!IsAcknowledgedOriginalInitializationSourceRefusal(raw)) CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); }
                try { await item.Observation.ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            }
        }
        try { await JoinOriginalProcessDeliveriesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
