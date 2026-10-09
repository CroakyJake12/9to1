using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>Original protected SQLite handles/transaction custody. This is not a Home
/// content grant. Consumers must check their actual resource-kind receipt independently.</summary>
public sealed class CanonicalSqliteOriginalStoreLease
{
    private readonly CanonicalSqliteOriginalStoreOwner _owner;
    private readonly CanonicalSqliteOriginalSourceScope _source;
    private readonly NativePersonalTaskRecoveryStore _physical;
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private readonly AsyncLocal<int> _logical = new();
    private readonly bool _reserveWriter;
    private bool _retiring, _committed;
    private Task? _close;
    private ResourceStoreIdentity? _identity;
    private SqliteTransaction? _transaction;
    internal CanonicalSqliteOriginalStoreLease(CanonicalSqliteOriginalStoreOwner owner,
        CanonicalSqliteOriginalSourceScope source, AuthenticatedResourceActor actor,
        NativePersonalTaskRecoveryStore physical, SqliteConnection connection,
        SqliteTransaction? transaction, bool reserveWriter)
    {
        _owner = owner; _source = source; Actor = actor; _physical = physical;
        Connection = connection; _transaction = transaction; _reserveWriter = reserveWriter;
    }
    internal CanonicalSqliteOriginalStoreOwner OriginalOwner => _owner;
    public AuthenticatedResourceActor Actor { get; }
    public ResourceStoreIdentity OriginalIdentity => _identity ?? throw new InvalidOperationException("Explicitly prepare the actual SQLite store identity first.");
    public SqliteConnection Connection { get; }
    public SqliteTransaction Transaction => _transaction ?? throw new InvalidOperationException("The approved original writer reservation has not begun.");
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    internal void CaptureOriginalIdentity(ResourceStoreIdentity actual) => _identity = actual;

    public T InvokeOriginalSource<T>(Func<T> actual)
    {
        DemandProductive();
        return _source.Invoke(actual);
    }
    public void InvokeOriginalSource(Action actual) => InvokeOriginalSource(() => { actual(); return 0; });
    public SqliteCommand CreateOriginalCommand() => InvokeOriginalSource(() =>
    {
        var command = Connection.CreateCommand(); command.Transaction = _transaction; return command;
    });
    public Task<T> ReadOriginalSourceAsync<T>(Func<Task<T>> actual) => Admit(() => _source.Read(actual));
    public Task ReadOriginalSourceAsync(Func<Task> actual) => Admit(async () => { await _source.Read(actual).ConfigureAwait(false); return 0; });
    public Task CloseOriginalSourceAsync(IAsyncDisposable actual) => _source.CloseAsync(actual);
    public Task CloseOriginalResourceAsync(IAsyncDisposable actual) => _source.CloseAsync(actual);

    public Task RevalidateWithinSourceAsync(Action<Action> scope, Action<Task> retain,
        CancellationToken token = default) => Admit(async () =>
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var child = new CanonicalSqliteOriginalSourceScope(_owner, body => _source.Run(() => scope(body)),
            actual => { _source.Retain(actual); retain(actual); });
        await _owner.DemandActorAsync(Actor, child, token).ConfigureAwait(false);
        child.Run(() => { token.ThrowIfCancellationRequested(); _physical.Validate(); });
        var current = await ReadIdentityCoreAsync(true, child, token).ConfigureAwait(false);
        // A held read transaction sees its original SQLite snapshot. Re-observe the
        // immutable identity through a NEW protected reader to catch external rekeying.
        var fresh = await child.Read(() => _owner.GetStoreIdentityWithinOriginalSourceAsync(Actor,
            child.Run, child.Retain, token)).ConfigureAwait(false);
        child.Run(() =>
        {
            if (current != OriginalIdentity || fresh != OriginalIdentity) throw new UnauthorizedAccessException("The protected SQLite logical identity changed.");
            _physical.Validate();
        });
        await _owner.DemandActorAsync(Actor, child, token).ConfigureAwait(false);
        return 0;
    });
    internal Task<ResourceStoreIdentity?> ReadIdentityAsync(bool required, CancellationToken token) =>
        ReadIdentityCoreAsync(required, _source, token);
    private async Task<ResourceStoreIdentity?> ReadIdentityCoreAsync(bool required,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        SqliteCommand? command = null; ResourceStoreIdentity? result = null; var errors = new List<Exception>();
        try
        {
            command = source.Invoke(() => { var actual = Connection.CreateCommand(); actual.Transaction = _transaction; return actual; });
            source.Run(() =>
            {
                command.CommandText = "SELECT value FROM settings WHERE key=$key;";
                command.Parameters.AddWithValue("$key", CanonicalSqliteOriginalStoreOwner.IdentityKey);
            });
            var raw = await source.Read(() => command.ExecuteScalarAsync(token)).ConfigureAwait(false);
            source.Run(() =>
            {
                if (raw is null or DBNull)
                {
                    if (required) throw new UnauthorizedAccessException("Explicit SQLite identity setup and Home ownership import are required.");
                    return;
                }
                CanonicalSqliteOriginalStoreOwner.IdentityMetadata metadata;
                try { metadata = JsonSerializer.Deserialize<CanonicalSqliteOriginalStoreOwner.IdentityMetadata>((string)raw) ?? throw new JsonException(); }
                catch (Exception cause) when (cause is JsonException or InvalidCastException)
                { throw new InvalidDataException("Preserve the invalid SQLite store identity for recovery.", cause); }
                if (metadata.SchemaVersion != 1 || metadata.StoreId == Guid.Empty || metadata.CreatedAtUtc == default)
                    throw new InvalidDataException("Preserve the unsupported SQLite store identity for recovery.");
                result = new(1, metadata.StoreId, metadata.CreatedAtUtc, false);
            });
        }
        catch (Exception cause) { errors.Add(cause); }
        if (command is not null) try { await source.CloseAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return result;
    }
    public Task CommitOriginalAsync(CancellationToken token = default) => Admit(async () =>
    {
        if (!_reserveWriter) throw new InvalidOperationException("The actual lease has no writer reservation.");
        await _owner.DemandActorAsync(Actor, _source, token).ConfigureAwait(false);
        _source.Run(_physical.Validate);
        if (_committed) throw new InvalidOperationException("The actual SQLite reservation was already committed; never replay it.");
        await _source.Read(() => Transaction.CommitAsync(token)).ConfigureAwait(false);
        _committed = true;
        _source.Run(_physical.Validate);
        await _owner.DemandActorAsync(Actor, _source, token).ConfigureAwait(false);
        return 0;
    });
    // This held-entry path never reads Home/profile/ownership. The actual producer
    // supplies a pure private issuer/current native guard and owns every SQL original.
    internal void DemandPinnedOriginalPhysical()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_retiring, this);
        _source.InvokeOwningCleanup(() => { _physical.Validate(); return 0; });
    }
    internal void BeginPinnedOriginalWriter(Action demandOriginalWrite)
    {
        DemandProductive();
        if (!_reserveWriter || _transaction is not null)
            throw new InvalidOperationException("The SAME unbegun protected writer pin is required.");
        _source.Invoke(() =>
        {
            demandOriginalWrite(); _physical.Validate();
            _transaction = Connection.BeginTransaction(deferred: false);
            return _transaction;
        });
    }
    internal Task CommitPinnedOriginalAsync(Action demandOriginalWrite, CancellationToken token) => Admit(async () =>
    {
        if (!_reserveWriter || _transaction is null || _committed)
            throw new InvalidOperationException("The actual uncommitted writer reservation is required; no replay.");
        _source.Run(() => { demandOriginalWrite(); _physical.Validate(); });
        await _source.Read(() => _transaction.CommitAsync(token)).ConfigureAwait(false);
        _committed = true;
        _source.Run(() => { demandOriginalWrite(); _physical.Validate(); });
        return 0;
    });
    private void DemandProductive()
    {
        _owner.DemandProductiveAdmission();
        _source.DemandCapacity();
        lock (_gate) ObjectDisposedException.ThrowIf(_retiring || _committed, this);
    }
    private Task<T> Admit<T>(Func<Task<T>> body)
    {
        DemandProductive(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _committed, this);
            _operations.RemoveAll(value => value.IsCompletedSuccessfully);
            if (_operations.Count >= 512) throw new InvalidOperationException("Actual SQLite operation custody requires retirement.");
            actual = Run(); _operations.Add(actual);
        }
        start.SetResult(); return actual;
        async Task<T> Run()
        {
            await start.Task.ConfigureAwait(false); _logical.Value++;
            try { return await body().ConfigureAwait(false); }
            finally { _logical.Value--; }
        }
    }
    public void RequestRetirement() { lock (_gate) _retiring = true; }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_logical.Value != 0 || CanonicalSqliteOriginalSourceScope.IsPhysicalSource(_source))
            throw new InvalidOperationException("The actual SQLite lease/source cannot synchronously join its own original close.");
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin(); lock (_gate)
        {
            _retiring = true;
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = Drain(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>(); Task[] operations;
        lock (_gate) operations = _operations.ToArray();
        foreach (var actual in operations)
            try { await actual.ConfigureAwait(false); } catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, actual, cause); }
        // Independently settle every admitted source before releasing the transaction/native handles.
        try { await _source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        // Every privately acquired object is captured INSIDE its actual callback before
        // post-scope publication. This also closes orphaned command/readers in reverse order.
        try { await _source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        try { _source.InvokeOwningCleanup(() => { _physical.Dispose(); return 0; }); } catch (Exception cause) { errors.Add(cause); }
        try { await _source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
}
