using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Physical custody of the SAME configured canonical JSON catalogue. It
/// reuses the maintained native private-file checks, never task keys or SQLite grants.
/// Missing identity is unavailable setup; reads neither create nor adopt a catalogue.</summary>
public sealed partial class CanonicalMiniComputerCatalogOriginalOwner : IHomeOriginalScopedResourceStoreIdentitySource,
    IHomeOriginalScopedLocalStoreEvidenceProvider, IAsyncDisposable
{
    public const string CatalogResourceKind = "mini-computer.catalog";
    public string ResourceKind => CatalogResourceKind;
    private readonly ICanonicalMiniComputerCatalogLocation _catalog;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<Task> _commands = [];
    private readonly List<Lease> _leases = [];
    private bool _retiring; private Task? _close;
    private const int MaximumCatalogBytes = 16 * 1024 * 1024;

    public CanonicalMiniComputerCatalogOriginalOwner(ICanonicalMiniComputerCatalogLocation actualCatalog,
        HomeLocalProfileIdentity actualProfiles)
    {
        _catalog = actualCatalog ?? throw new ArgumentNullException(nameof(actualCatalog));
        _profiles = actualProfiles ?? throw new ArgumentNullException(nameof(actualProfiles));
        // Configuration normalization is not file IO, native identity or a grant.
        _path = Path.GetFullPath(actualCatalog.OriginalCatalogPath);
    }
    public bool HasOriginalComposition(ICanonicalMiniComputerCatalogLocation catalog, HomeLocalProfileIdentity profiles) =>
        ReferenceEquals(catalog, _catalog) && ReferenceEquals(profiles, _profiles) &&
        Path.GetFullPath(catalog.OriginalCatalogPath) == _path;
    public bool HasOriginalProfiles(HomeLocalProfileIdentity profiles) => ReferenceEquals(_profiles, profiles);

    public Task<Lease?> AcquireOriginalProtectedReadWithinSourceAsync(AuthenticatedResourceActor actor,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => AdmitScoped<Lease?>(scope, retain, async source =>
    {
        Lease? lease = null;
        try
        {
            await DemandActor(actor, source, token).ConfigureAwait(false);
            if (!source.Invoke(() => File.Exists(_path))) { await source.Join().ConfigureAwait(false); return null; }
            lease = new(this, actor, source);
            lock (_gate) _leases.Add(lease); // Before any acquisition callback.
            await lease.Acquire(token).ConfigureAwait(false);
            await DemandActor(actor, source, token).ConfigureAwait(false);
            await source.Join().ConfigureAwait(false);
            if (lease.IdentityOrNull is not null) return lease;
        }
        catch (Exception cause) { source.Remember(cause); }
        if (lease is not null)
            try { await lease.CloseAndDrainOriginalAsync().ConfigureAwait(false); }
            catch (Exception cause) { source.Remember(cause); }
        await source.Join().ConfigureAwait(false); return null;
    });

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) =>
        GetStoreIdentityWithinOriginalSourceAsync(body => body(), _ => { }, token);
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) => new(AdmitScoped<ResourceStoreIdentity>(scope, retain, async source =>
    {
        var actor = await source.Read(() => _profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The current actual Home profile is unavailable.");
        Lease? lease = null; ResourceStoreIdentity? identity = null;
        try
        {
            lease = await source.Read(() => AcquireOriginalProtectedReadWithinSourceAsync(actor,
                source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
            identity = lease?.IdentityOrNull;
        }
        catch (Exception cause) { source.Remember(cause); }
        if (lease is not null)
            try { await lease.CloseAndDrainOriginalAsync().ConfigureAwait(false); }
            catch (Exception cause) { source.Remember(cause); }
        await source.Join().ConfigureAwait(false);
        return identity ?? throw new InvalidOperationException("The canonical Mini Computer catalogue needs explicit owning setup; no identity was created or inferred.");
    }));
    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) =>
        ReadWithinOriginalSourceAsync(storeId, body => body(), _ => { }, token);
    public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => new(AdmitScoped<HomeLocalStoreEvidence?>(scope, retain, async source =>
    {
        var identity = await source.Read(() => GetStoreIdentityWithinOriginalSourceAsync(
            source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        await source.Join().ConfigureAwait(false);
        return storeId != identity.StoreId.ToString("D") ? null : new(CatalogResourceKind, storeId,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity))), false, false, true);
    }));

    private async Task DemandActor(AuthenticatedResourceActor actor, OriginalScope source, CancellationToken token)
    {
        if (actor.AccountId is not null || actor.OrganisationId is not null ||
            await source.Read(() => _profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The SAME current operating-system Home actor is required for this protected catalogue.");
    }
    private Task<T> AdmitScoped<T>(Action<Action> scope, Action<Task> retain, Func<OriginalScope, Task<T>> body) => Admit(async () =>
    {
        var source = new OriginalScope(this, scope, retain); T value = default!;
        try { value = await body(source).ConfigureAwait(false); }
        catch (Exception cause) { source.Remember(cause); }
        await source.Join().ConfigureAwait(false); return value;
    });
    private Task<T> Admit<T>(Func<Task<T>> body)
    {
        TaskCompletionSource start; Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _commands.RemoveAll(task => task.IsCompletedSuccessfully);
            _leases.RemoveAll(lease => lease.OriginalClose?.IsCompletedSuccessfully == true);
            if (_commands.Count >= 128 || _leases.Count >= 64)
                throw new InvalidOperationException("Mini Computer original catalogue custody requires inspection.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drive(start.Task, body); _commands.Add(actual);
        }
        start.SetResult(); return actual;
    }
    private async Task<T> Drive<T>(Task start, Func<Task<T>> body)
    { await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this); return await body().ConfigureAwait(false); }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public void RequestOriginalRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalRetirementJoin(); Task actual; TaskCompletionSource? start = null;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] commands; lock (_gate) commands = _commands.Where(joined.Add).ToArray();
            if (commands.Length == 0) break;
            foreach (var command in commands) try { await command.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(command.Exception ?? cause); }
        }
        Lease[] leases; lock (_gate) leases = _leases.ToArray();
        foreach (var lease in leases)
        {
            if (!CanRetireOriginalIdentityLease(lease))
            {
                errors.Add(new InvalidOperationException("The original catalogue setup Home release is unconfirmed; its actual file reservation remains retained."));
                continue;
            }
            try { await lease.CloseAndDrainOriginalAsync().ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual Mini Computer catalogue originals did not close healthy.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());

    public sealed partial class Lease : ICanonicalMiniComputerProtectedCatalogRead
    {
        private readonly CanonicalMiniComputerCatalogOriginalOwner _owner;
        private readonly OriginalScope _acquisition;
        private readonly List<Task> _operations = [];
        private readonly List<OriginalScope> _sources = [];
        private ICanonicalMiniComputerCatalogReservation? _reservation;
        private NativePersonalTaskRecoveryStore? _physical;
        private FileStream? _stream;
        private byte[]? _bytes;
        private string? _sha256;
        private bool _retired; private Task? _close;
        private Task? _streamClose, _reservationClose;
        internal ResourceStoreIdentity? IdentityOrNull { get; private set; }
        public ResourceStoreIdentity OriginalIdentity => IdentityOrNull ?? throw new InvalidOperationException("No durable catalogue identity is present.");
        public AuthenticatedResourceActor Actor { get; }
        internal Lease(CanonicalMiniComputerCatalogOriginalOwner owner, AuthenticatedResourceActor actor, OriginalScope acquisition)
        { _owner = owner; Actor = actor; _acquisition = acquisition; _sources.Add(acquisition); }
        public bool IsOriginalCatalog(ICanonicalMiniComputerCatalogLocation sameCatalog) => ReferenceEquals(_owner._catalog, sameCatalog);
        internal async Task Acquire(CancellationToken token)
        {
            _reservation = await _acquisition.Read(() => _owner._catalog.ReserveOriginalCatalogWithinSourceAsync(
                _acquisition.Run, _acquisition.Retain, token), value => _reservation = value).ConfigureAwait(false);
            _acquisition.Run(() =>
            {
                _reservation.DemandOriginalReservation();
                _physical = NativePersonalTaskRecoveryStore.Acquire(Path.GetDirectoryName(_owner._path)!, _owner._path);
                _stream = new FileStream(_owner._path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    16_384, FileOptions.Asynchronous | FileOptions.RandomAccess);
                _physical.ValidateOriginalMiniComputerReadHandle(_stream.SafeFileHandle);
            });
            _bytes = await ReadBytes(_acquisition, token).ConfigureAwait(false);
            _sha256 = Convert.ToHexString(SHA256.HashData(_bytes));
            IdentityOrNull = _acquisition.Invoke(() => ReadIdentity(_bytes));
        }
        internal static ResourceStoreIdentity? ReadIdentity(byte[] bytes)
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
                throw new InvalidDataException("The existing canonical Mini Computer catalogue schema is unsupported.");
            if (!json.RootElement.TryGetProperty("originalStoreIdentity", out var value) || value.ValueKind == JsonValueKind.Null) return null;
            var identity = value.Deserialize<ResourceStoreIdentity>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("The durable Mini Computer catalogue identity is invalid.");
            if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty || identity.NewlyCreated || identity.CreatedAtUtc == default)
                throw new InvalidDataException("The durable Mini Computer catalogue identity is invalid.");
            return identity;
        }
        private async Task<byte[]> ReadBytes(OriginalScope source, CancellationToken token)
        {
            var bytes = source.Invoke(() =>
            {
                _physical!.ValidateOriginalMiniComputerReadHandle(_stream!.SafeFileHandle); _reservation!.DemandOriginalReservation();
                var length = _stream!.Length;
                if (length is < 1 or > MaximumCatalogBytes) throw new InvalidDataException("The canonical catalogue exceeds its supported read boundary.");
                return new byte[checked((int)length)];
            });
            var offset = 0;
            while (offset < bytes.Length)
            {
                var actualOffset = offset;
                var count = await source.Read(() => RandomAccess.ReadAsync(_stream!.SafeFileHandle,
                    bytes.AsMemory(actualOffset), actualOffset, token).AsTask()).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The original catalogue changed during its read.");
                offset += count;
            }
            source.Run(() =>
            {
                _physical!.ValidateOriginalMiniComputerReadHandle(_stream!.SafeFileHandle);
                if (_stream!.Length != bytes.Length) throw new IOException("The original catalogue changed during its read.");
            });
            return bytes;
        }
        public Task<ReadOnlyMemory<byte>> ReadOriginalBytesWithinSourceAsync(Action<Action> scope,
            Action<Task> retain, CancellationToken token) => Admit<ReadOnlyMemory<byte>>(scope, retain, async source =>
        {
            await Validate(source, token).ConfigureAwait(false);
            return _bytes!.ToArray(); // No writable alias of the retained original bytes escapes.
        });
        public Task RevalidateWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            Admit(scope, retain, async source => { await Validate(source, token).ConfigureAwait(false); return true; });
        private async Task Validate(OriginalScope source, CancellationToken token)
        {
            await _owner.DemandActor(Actor, source, token).ConfigureAwait(false);
            var current = await ReadBytes(source, token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(current), Convert.FromHexString(_sha256!)))
                throw new UnauthorizedAccessException("The exact original Mini Computer catalogue revision changed.");
            await _owner.DemandActor(Actor, source, token).ConfigureAwait(false);
        }
        public void DemandOriginalPinnedCatalog()
        {
            lock (_owner._gate) ObjectDisposedException.ThrowIf(_retired || _owner._retiring, this);
            _reservation!.DemandOriginalReservation(); _physical!.ValidateOriginalMiniComputerReadHandle(_stream!.SafeFileHandle);
            if (_stream!.Length != _bytes!.Length) throw new UnauthorizedAccessException("The pinned catalogue changed.");
            var current = new byte[_bytes.Length]; var offset = 0;
            while (offset < current.Length)
            {
                var count = RandomAccess.Read(_stream.SafeFileHandle, current.AsSpan(offset), offset);
                if (count == 0) throw new EndOfStreamException("The pinned catalogue changed.");
                offset += count;
            }
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(current), Convert.FromHexString(_sha256!)))
                throw new UnauthorizedAccessException("The pinned catalogue revision changed.");
            _physical.ValidateOriginalMiniComputerReadHandle(_stream.SafeFileHandle);
        }
        private Task<T> Admit<T>(Action<Action> scope, Action<Task> retain, Func<OriginalScope, Task<T>> body)
        {
            lock (_owner._gate)
            {
                ObjectDisposedException.ThrowIf(_retired, this);
                if (_operations.Count >= 1024 || _sources.Count >= 1025)
                    throw new InvalidOperationException("The original catalogue read lease has reached its finite observation boundary.");
                var source = new OriginalScope(this, scope, retain); _sources.Add(source);
                var result = _owner.Admit(async () =>
                {
                    using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                    T value = default!;
                    try { value = await body(source).ConfigureAwait(false); }
                    catch (Exception cause) { source.Remember(cause); }
                    await source.Join().ConfigureAwait(false); return value;
                });
                _operations.Add(result); return result;
            }
        }
        public Task? OriginalClose { get { lock (_owner._gate) return _close; } }
        public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); Task actual; TaskCompletionSource? start = null;
            lock (_owner._gate)
            {
                _retired = true;
                if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task); }
                actual = _close;
            }
            start?.SetResult(); return actual;
        }
        private async Task Close(Task start)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>(); Task[] operations;
            lock (_owner._gate) operations = _operations.ToArray();
            foreach (var operation in operations) try { await operation.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(operation.Exception ?? cause); }
            foreach (var source in _sources) try { await source.Join().ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(cause); }
            // Independent, once-only closes: no re-entry into retired caller callbacks.
            if (_stream is not null) try { _streamClose = _stream.DisposeAsync().AsTask(); await _streamClose.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(cause); }
            if (_physical is not null) try { _physical.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            if (_reservation is not null) try { _reservationClose = _reservation.DisposeAsync().AsTask(); await _reservationClose.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Actual protected Mini Computer catalogue cleanup failed.", errors);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }

    internal sealed partial class OriginalScope(object owner, Action<Action> caller, Action<Task> retain)
    {
        private readonly CloudflareOriginalTaskLedger _raw = new();
        private readonly object _originalOwner = owner;
        internal void Remember(Exception cause) => _raw.Retain(cause);
        internal void Retain(Task actual) { _raw.Track(actual); try { retain(actual); } catch (Exception cause) { Remember(cause); throw; } }
        internal void Run(Action body) => Invoke(() => { body(); return true; });
        internal T Invoke<T>(Func<T> body)
        {
            var live = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; T result = default!;
            var errors = new List<Exception>();
            void Add(Exception cause) { Remember(cause); if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause); }
            try
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(_originalOwner, () =>
                {
                    try
                    {
                        caller(() =>
                        {
                            if (Volatile.Read(ref live) == 0 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                            { var cause = new InvalidOperationException("The original catalogue source callback expired, repeated or moved threads."); Add(cause); throw cause; }
                            try { result = body(); } catch (Exception cause) { Add(cause); throw; }
                        });
                    }
                    catch (Exception cause) { Add(cause); }
                    if (used == 0) Add(new InvalidOperationException("The original catalogue callback was not entered."));
                    return true;
                });
            }
            finally { Volatile.Write(ref live, 0); }
            if (errors.Count != 0) throw new AggregateException("Original catalogue callback failed.", errors);
            return result;
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
        {
            Task<T>? actual = null; T result = default!; var errors = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); } catch (Exception cause) { errors.Add(cause); }
            if (actual is not null) try { result = await _raw.AwaitAsync(actual).ConfigureAwait(false); capture?.Invoke(result); }
                catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Original catalogue read failed.", errors);
            return actual is null ? throw new InvalidOperationException("No original catalogue task was captured.") : result;
        }
        internal async Task Join()
        {
            if (_setupAcknowledgments.Count != 0) { await JoinIdentitySetupSources().ConfigureAwait(false); return; }
            await _raw.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_raw.OriginalErrors.Count != 0) throw new AggregateException("Original catalogue sources failed.", _raw.OriginalErrors);
        }
    }
}
