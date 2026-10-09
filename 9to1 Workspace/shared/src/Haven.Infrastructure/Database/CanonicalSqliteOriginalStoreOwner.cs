using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>Physical custody of the SAME configured SQLite database and immutable logical
/// identity. This owner grants no Home/content access. Target owners must independently
/// verify their exact Home resource-kind receipt before querying product rows.</summary>
public sealed class CanonicalSqliteOriginalStoreOwner : IResourceStoreIdentitySource, IAsyncDisposable
{
    public const string IdentityKey = "canonical.sqlite.store-identity.v1";
    private readonly SqliteDatabase _database;
    private readonly IAppPaths _paths;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly object _gate = new();
    private readonly List<Invocation> _commands = [];
    private readonly ConditionalWeakTable<Task, OriginalRefusalReceipt> _refusals = new();
    private sealed record OriginalRefusalReceipt(CanonicalSqliteOriginalSourceScope Source, Exception Cause);
    public bool IsAcknowledgedOriginalCommandRefusal(Task sameOriginal)
    {
        if (!sameOriginal.IsFaulted || !_refusals.TryGetValue(sameOriginal, out var receipt) || !receipt.Source.IsHealthySettled)
            return false;
        var direct = sameOriginal.Exception!.InnerExceptions;
        return direct.Count == 1 && ReferenceEquals(direct[0], receipt.Cause);
    }
    private sealed class Invocation
    {
        internal Task Driver = null!;
        internal CanonicalSqliteOriginalSourceScope? Source;
        internal NativePersonalTaskRecoveryStore? Physical;
        internal CanonicalSqliteOriginalStoreLease? Lease;
        internal Task? PhysicalClose;
    }
    private readonly List<CanonicalSqliteOriginalStoreLease> _leases = [];
    private readonly AsyncLocal<int> _logical = new();
    private bool _retiring;
    private Task? _close;
    internal sealed record IdentityMetadata(int SchemaVersion, Guid StoreId, DateTimeOffset CreatedAtUtc);

    public CanonicalSqliteOriginalStoreOwner(SqliteDatabase actualDatabase, IAppPaths actualPaths,
        HomeLocalProfileIdentity actualProfiles)
    {
        ArgumentNullException.ThrowIfNull(actualDatabase); ArgumentNullException.ThrowIfNull(actualPaths);
        ArgumentNullException.ThrowIfNull(actualProfiles);
        if (!actualDatabase.HasOriginalPaths(actualPaths))
            throw new InvalidOperationException("The configured SQLite database must retain the SAME original app paths.");
        _database = actualDatabase; _paths = actualPaths; _profiles = actualProfiles;
    }
    public bool HasOriginalComposition(SqliteDatabase sameDatabase, IAppPaths samePaths,
        HomeLocalProfileIdentity sameProfiles) => ReferenceEquals(_database, sameDatabase) &&
        ReferenceEquals(_paths, samePaths) && ReferenceEquals(_profiles, sameProfiles) &&
        _database.HasOriginalPaths(samePaths);
    public bool HasOriginalProfiles(HomeLocalProfileIdentity sameProfiles) => ReferenceEquals(_profiles, sameProfiles);
    public bool HasOriginalDatabase(ISqliteConnectionFactory sameFactory) => ReferenceEquals(_database, sameFactory);
    public HomeLocalProfileIdentity OriginalProfiles => _profiles;
    internal object OriginalSourceOwner => this;
    internal Task<T> RetainOriginalReader<T>(CanonicalSqliteOriginalSourceScope source, Func<Task<T>> body) =>
        Admit(invocation => { invocation.Source = source; return body(); });

    // A missing existing identity is a negative setup observation, never permission
    // to seed or reset it. Corrupt/changed physical/schema data remains a real fault.
    internal Task<ResourceStoreIdentity?> ReadExistingStoreIdentityWithinOriginalSourceAsync(
        AuthenticatedResourceActor expectedActor, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        Admit(async invocation =>
        {
            var source = new CanonicalSqliteOriginalSourceScope(this, scope, retain);
            var lease = await AcquireCoreAsync(expectedActor, false, false, source, invocation, token).ConfigureAwait(false);
            ResourceStoreIdentity? identity = null; var errors = new List<Exception>();
            try { identity = await lease.ReadIdentityAsync(false, token).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
            try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            Throw(errors); return identity;
        });

    public Task<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(AuthenticatedResourceActor expectedActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default) => Admit(async invocation =>
    {
        var lease = await AcquireCoreAsync(expectedActor, false, true,
            new(this, scope, retain), invocation, token).ConfigureAwait(false);
        ResourceStoreIdentity? result = null; var errors = new List<Exception>();
        try { result = lease.OriginalIdentity; }
        catch (Exception cause) { errors.Add(cause); }
        try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        Throw(errors); return result!;
    });
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => new(Admit(async invocation =>
    {
        var source = new CanonicalSqliteOriginalSourceScope(this, body => body(), _ => { });
        invocation.Source = source;
        var actor = await source.Read(() => _profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException();
        var lease = await AcquireCoreAsync(actor, false, true, source, invocation, token).ConfigureAwait(false);
        var identity = lease.OriginalIdentity;
        await lease.CloseAndDrainAsync().ConfigureAwait(false); return identity;
    }));

    /// <summary>The returned transaction/native handles are physical read or reservation
    /// custody only. They confer no authority to disclose any table or alter product data.</summary>
    public Task<CanonicalSqliteOriginalStoreLease> AcquireOriginalProtectedReadWithinSourceAsync(
        AuthenticatedResourceActor expectedActor, bool reserveWriter, Action<Action> scope,
        Action<Task> retain, CancellationToken token = default) => Admit(invocation =>
        AcquireCoreAsync(expectedActor, reserveWriter, true, new(this, scope, retain), invocation, token));
    // No SQL transaction is begun here. This creates actual protected native/connection
    // custody before a separately approved Home WRITE entry can be held.
    internal Task<CanonicalSqliteOriginalStoreLease> AcquireOriginalProtectedWriterPinWithinSourceAsync(
        AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        Admit(invocation => AcquireCoreAsync(actor, true, true, new(this, scope, retain), invocation, token, true));
    public bool IsIssuedOriginalLease(CanonicalSqliteOriginalStoreLease lease)
    { lock (_gate) return !_retiring && lease.OriginalClose is null && ReferenceEquals(lease.OriginalOwner, this) && _leases.Any(value => ReferenceEquals(value, lease)); }

    private async Task<CanonicalSqliteOriginalStoreLease> AcquireCoreAsync(AuthenticatedResourceActor actor,
        bool reserveWriter, bool requireIdentity, CanonicalSqliteOriginalSourceScope source, Invocation invocation, CancellationToken token, bool deferWriterTransaction = false)
    {
        ArgumentNullException.ThrowIfNull(actor);
        invocation.Source = source;
        await DemandActorAsync(actor, source, token).ConfigureAwait(false);
        NativePersonalTaskRecoveryStore? physical = null; SqliteConnection? connection = null;
        SqliteTransaction? transaction = null; CanonicalSqliteOriginalStoreLease? lease = null;
        var errors = new List<Exception>();
        try
        {
            source.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    _leases.RemoveAll(value => value.OriginalClose?.IsCompletedSuccessfully == true);
                    if (_leases.Count >= 64) throw new InvalidOperationException("Protected SQLite lease custody requires external retirement.");
                }
                physical = NativePersonalTaskRecoveryStore.Acquire(_paths.DataDirectory, _paths.DatabasePath);
                invocation.Physical = physical;
            });
            connection = await source.Read(() => _database.OpenWithinOriginalSourceAsync(source, token)).ConfigureAwait(false);
            source.Run(() =>
            {
                physical!.Validate();
                if (Path.GetFullPath(connection.DataSource) != Path.GetFullPath(_paths.DatabasePath))
                    throw new UnauthorizedAccessException("The actual SQLite connection belongs to another configured database.");
            });
            if (!deferWriterTransaction)
                transaction = source.Invoke(() => connection.BeginTransaction(deferred: !reserveWriter));
            lease = new(this, source, actor, physical!, connection, transaction!, reserveWriter);
            invocation.Lease = lease;
            lock (_gate) _leases.Add(lease); // Capture before identity/actor callbacks can fail.
            var identity = await lease.ReadIdentityAsync(requireIdentity, token).ConfigureAwait(false);
            if (identity is not null) lease.CaptureOriginalIdentity(identity);
            await DemandActorAsync(actor, source, token).ConfigureAwait(false);
            source.Run(physical!.Validate); return lease;
        }
        catch (Exception cause) { errors.Add(cause); }
        if (lease is not null)
            try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        else
        {
            if (transaction is not null) try { await source.CloseAsync(transaction).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            if (connection is not null) try { await source.CloseAsync(connection).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            if (physical is not null) try { await ClosePhysical(invocation).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        }
        Throw(errors); throw new InvalidOperationException("No actual protected SQLite lease was acquired.");
    }
    internal async Task DemandActorAsync(AuthenticatedResourceActor expected,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        if (expected.AccountId is not null || expected.OrganisationId is not null ||
            await source.Read(() => _profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) != expected)
            throw new UnauthorizedAccessException("The SAME current operating-system Home profile is required.");
    }
    internal void DemandProductiveAdmission()
    { lock (_gate) ObjectDisposedException.ThrowIf(_retiring, this); }
    internal void DemandExternalJoin()
    {
        if (_logical.Value != 0 || CanonicalSqliteOriginalSourceScope.IsPhysicalSource(this))
            throw new InvalidOperationException("An actual SQLite source/command cannot synchronously join its own original owner.");
    }
    public void DemandExternalOriginalRetirementJoin() => DemandExternalJoin();
    private Task<T> Admit<T>(Func<Invocation, Task<T>> body)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> original; var invocation = new Invocation();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _commands.RemoveAll(value => (value.Driver.IsCompletedSuccessfully &&
                (value.Lease is not null || value.Source?.IsHealthySettled == true)) || IsAcknowledgedOriginalCommandRefusal(value.Driver));
            if (_commands.Count >= 128) throw new InvalidOperationException("SQLite command custody requires external retirement.");
            original = Run(); invocation.Driver = original; _commands.Add(invocation);
        }
        start.SetResult(); return original;
        async Task<T> Run()
        {
            await start.Task.ConfigureAwait(false); _logical.Value++;
            try { return await body(invocation).ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (invocation.Source is { IsHealthySettled: true, OriginalPreEffectRefusal: { } refusal } source &&
                    ReferenceEquals(cause, refusal))
                    _refusals.Add(invocation.Driver, new(source, refusal));
                throw;
            }
            finally { _logical.Value--; }
        }
    }
    public void RequestRetirement() { lock (_gate) _retiring = true; }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public Task CloseAndDrainAsync()
    {
        DemandExternalJoin(); lock (_gate)
        {
            _retiring = true;
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = Drain(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>(); Invocation[] commands;
        lock (_gate) commands = _commands.ToArray();
        foreach (var invocation in commands)
            try { await invocation.Driver.ConfigureAwait(false); }
            catch (Exception cause) { if (!IsAcknowledgedOriginalCommandRefusal(invocation.Driver)) Capture(errors, invocation.Driver, cause); }
        CanonicalSqliteOriginalStoreLease[] leases; lock (_gate) leases = _leases.ToArray();
        foreach (var lease in leases)
            try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { Capture(errors, lease.OriginalClose, cause); }
        foreach (var invocation in commands.Where(value => value.Lease is null))
        {
            if (invocation.Source is { } source)
            {
                try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            }
            try { await ClosePhysical(invocation).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        }
        Throw(errors);
    }
    private Task ClosePhysical(Invocation invocation)
    {
        TaskCompletionSource? receipt = null;
        lock (_gate)
        {
            if (invocation.PhysicalClose is not null) return invocation.PhysicalClose;
            receipt = new(TaskCreationOptions.RunContinuationsAsynchronously);
            invocation.PhysicalClose = receipt.Task;
        }
        try
        {
            if (invocation.Physical is { } physical)
                invocation.Source!.InvokeOwningCleanup(() => { physical.Dispose(); return 0; });
            receipt.SetResult();
        }
        catch (Exception cause) { receipt.SetException(cause); }
        return receipt.Task;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    internal static void Capture(List<Exception> errors, Task? actual, Exception observed)
    {
        var causes = actual?.Exception is { } clrContainer ? clrContainer.InnerExceptions : [observed];
        foreach (var direct in causes) if (!errors.Any(value => ReferenceEquals(value, direct))) errors.Add(direct);
    }
    internal static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("The actual SQLite owner/source/cleanup did not settle.", errors);
    }
}
