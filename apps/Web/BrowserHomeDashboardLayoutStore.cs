using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;
using HavenOS.Home;
using NineToOne.Web.Services;

namespace NineToOne.Web;

/// <summary>Home preferences partitioned by the SAME verified account profile. This owner
/// supplies no Task admission, permission, OS identity, model or account authority.</summary>
internal sealed class BrowserHomeDashboardLayoutStore : IBrowserPrivateContextParticipant
{
    private readonly object _gate = new();
    private readonly Action _privateDemand;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IHomeDashboardBrowserTransport _transport;
    private readonly Func<Task>? _publicationSource;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Original> _originals = [];
    private readonly HashSet<Binding> _bindings = [];
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static List<BrowserHomeDashboardLayoutStore>? _physical;
    private bool _revoked, _closeProbing;
    private Task? _close;
    private Exception? _capacityFailure;
    private static readonly ConditionalWeakTable<BrowserSurfaceRegistry, BrowserHomeDashboardLayoutStore> Owners = new();
    private static readonly Regex Profile = new("^cake-account-profile:[0-9a-f]{64}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);

    internal BrowserHomeDashboardLayoutStore(Action privateDemand, IAuthenticatedResourceActorSource actors,
        IHomeDashboardBrowserTransport transport, Func<Task>? publicationSource = null)
    { _privateDemand = privateDemand; _actors = actors; _transport = transport; _publicationSource = publicationSource; }

    [SupportedOSPlatform("browser")]
    internal static void Register(BrowserSurfaceRegistry registry, Action samePrivateDemand)
    {
        if (Owners.TryGetValue(registry, out var previous))
        {
            if (!previous.IsRevoked) throw new InvalidOperationException("The SAME registry still owns a live Home layout lifetime.");
            Owners.Remove(registry);
        }
        var actual = new BrowserHomeDashboardLayoutStore(samePrivateDemand, new BrowserTaskActorSource(), new HomeDashboardBrowserTransport());
        var result = registry.RegisterPrivateLifetime(actual); // No JS/API/actor callback preceded enrollment.
        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
        Owners.Add(registry, actual);
    }
    internal static BrowserHomeDashboardLayoutStore GetCurrent(BrowserSurfaceRegistry registry)
    {
        if (!Owners.TryGetValue(registry, out var actual)) throw new InvalidOperationException("The SAME registry has no Home layout owner.");
        actual.DemandCurrent(); return actual;
    }
    internal bool IsRevoked { get { lock (_gate) return _revoked; } }
    internal void DemandCurrent() { lock (_gate) DemandLocked(); }
    private void DemandLocked()
    {
        if (_revoked || _closeProbing) throw new ObjectDisposedException(nameof(BrowserHomeDashboardLayoutStore));
        _privateDemand(); // Deny-only fence from the SAME captured private Task owner, never a replacement lookup.
    }

    internal sealed class Binding(BrowserHomeDashboardLayoutStore owner) : IHomeDashboardLayoutStore, IHomeDashboardLayoutHistory, IDisposable
    {
        internal AuthenticatedResourceActor? Actor;
        internal bool Revoked;
        public void Dispose() { lock (owner._gate) { Revoked = true; Actor = null; owner._bindings.Remove(this); } }
        internal BrowserHomeDashboardLayoutStore Owner => owner;
        public Task<HomeDashboardLayout?> LoadAsync(CancellationToken token) => owner.ReadAsync(this, "Load", null, token);
        public Task<HomeDashboardLayout?> GetRevisionAsync(long revision, CancellationToken token = default)
        {
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            return owner.ReadAsync(this, "GetRevision", revision, token);
        }
        public Task<bool> TrySaveAsync(long expectedRevision, HomeDashboardLayout layout, CancellationToken token) =>
            owner.Start(async original => (await owner.StorageAsync(original, this, "Save", expectedRevision, layout, token)).Saved, token);
    }
    internal Binding CreateBinding() { lock (_gate) { DemandLocked(); var binding = new Binding(this); _bindings.Add(binding); return binding; } }
    internal Task RunViewAsync(Func<Task> actualSource, CancellationToken caller = default) => Start(async original =>
    {
        var actual = Source(original, actualSource);
        await Consume(actual); return true;
    }, caller);
    private Task<HomeDashboardLayout?> ReadAsync(Binding binding, string action, long? revision, CancellationToken token) =>
        Start(async original => (await StorageAsync(original, binding, action, revision, null, token)).Layout, token);

    private sealed class Original(Original? parent, CancellationToken caller)
    {
        internal readonly Original? Parent = parent;
        internal readonly CancellationToken Caller = caller;
        internal readonly List<Task> Sources = [];
        internal readonly List<Exception> Stops = [];
        internal Task Outer = null!, Driver = null!, Publisher = null!;
        internal bool Dispatched, ExpectedCancellation;
        internal Exception? ExpectedNoActor;
        internal string? RawReply, Proposal;
        internal object? Decoded;
        internal Binding? PublishBinding;
        internal AuthenticatedResourceActor? PublishActor;
    }
    internal sealed record OriginalObservation(Task Outer, Task Driver, Task Publisher, IReadOnlyList<Task> Sources,
        bool StorageDispatched, string? RawReply, string? Proposal, object? Decoded);
    internal IReadOnlyList<OriginalObservation> Originals
    {
        get { lock (_gate) return _originals.Select(row => new OriginalObservation(row.Outer, row.Driver, row.Publisher,
            row.Sources.ToArray(), row.Dispatched, row.RawReply, row.Proposal, row.Decoded)).ToArray(); }
    }
    private Task<T> Start<T>(Func<Original, Task<T>> source, CancellationToken caller)
    {
        var start = new TaskCompletionSource();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var row = new Original(_executing.Value, caller) { Outer = completion.Task };
        lock (_gate)
        {
            DemandLocked();
            _originals.RemoveAll(value => value.Outer.IsCompletedSuccessfully && value.Driver.IsCompletedSuccessfully &&
                value.Publisher.IsCompletedSuccessfully && value.Sources.All(task => task.IsCompletedSuccessfully) && value.Stops.Count == 0);
            if (_capacityFailure is not null) throw _capacityFailure;
            if (_originals.Count >= 256) throw _capacityFailure = new InvalidOperationException("Home source custody capacity requires external retirement.");
            var driver = DriveAsync(start.Task, row, source); row.Driver = driver;
            row.Publisher = PublishAsync(start.Task, row, driver, completion);
            _originals.Add(row); // All actual outer/driver/publisher Tasks exist BEFORE any source callback.
        }
        start.SetResult(); return completion.Task;
    }
    private async Task<T> DriveAsync<T>(Task start, Original row, Func<Original, Task<T>> source)
    {
        await start;
        var previous = _executing.Value; _executing.Value = row;
        try
        {
            if (row.Caller.IsCancellationRequested || _lifetime.IsCancellationRequested)
            { row.ExpectedCancellation = true; throw new OperationCanceledException(row.Caller); }
            DemandCurrent();
            var actual = Source(row, () => source(row));
            try { return await Consume(actual); }
            catch when (actual.IsCanceled && !row.Dispatched && (row.Caller.IsCancellationRequested || _lifetime.IsCancellationRequested))
            { row.ExpectedCancellation = true; throw; }
        }
        finally { _executing.Value = previous; }
    }
    private async Task PublishAsync<T>(Task start, Original row, Task<T> driver, TaskCompletionSource<T> completion)
    {
        await start;
        T? value = default; Exception? failure = null; var cancelled = false; var token = default(CancellationToken);
        try { value = await driver; }
        catch (Exception cause)
        {
            if (driver.Exception is { } group) failure = group;
            else if (driver.IsCanceled) { cancelled = true; token = (cause as OperationCanceledException)?.CancellationToken ?? default; }
            else failure = cause;
        }
        Exception? publicationFailure = null;
        // Internal controlled source only; production supplies none. The SAME exposed
        // Task remains pending until this independently retained original is terminal.
        if (failure is null && !cancelled && _publicationSource is { } source)
        {
            var previous = _executing.Value; _executing.Value = row;
            try
            {
                DemandCurrent();
                var actual = Source(row, source);
                try { await Consume(actual); }
                catch (Exception cause) { throw actual.Exception ?? new AggregateException("Post-driver publication source failed.", cause); }
            }
            catch (Exception cause) { publicationFailure = failure = cause; }
            finally { _executing.Value = previous; }
        }
        lock (_gate)
        {
            if (failure is null && !cancelled)
                try
                {
                    DemandLocked(); row.Caller.ThrowIfCancellationRequested();
                    if (row.PublishBinding is { } binding)
                    { if (binding.Revoked) throw new ObjectDisposedException("Original Home view"); binding.Actor = row.PublishActor; }
                }
                catch (Exception cause) { failure = publicationFailure = cause; }
            // Complete the SAME exposed Task only AFTER terminal driver cleanup and under the revocation gate.
            if (failure is not null) completion.SetException(failure);
            else if (cancelled) completion.SetCanceled(token);
            else completion.SetResult(value!);
        }
        if (publicationFailure is not null) throw new AggregateException("The original Home publication was refused.", publicationFailure);
    }
    private T Source<T>(Original row, Func<T> source)
    {
        var owners = _physical ??= []; owners.Add(this);
        try
        {
            T value;
            try { value = source(); }
            catch (OperationCanceledException fault) { throw new AggregateException("The original Home source faulted synchronously.", fault); }
            if (value is Task actual) Retain(row, actual);
            return value;
        }
        finally { owners.RemoveAt(owners.Count - 1); }
    }
    private void Retain(Original row, Task actual)
    {
        lock (_gate)
            for (Original? current = row; current is not null; current = current.Parent)
                if (!current.Sources.Any(prior => ReferenceEquals(prior, actual))) current.Sources.Add(actual);
    }
    private static async Task<T> Consume<T>(Task<T> actual)
    {
        try { return await actual; }
        catch (Exception cause)
        {
            if (actual.Exception is { } group) throw group; // Faulted OCE and siblings remain faults.
            ExceptionDispatchInfo.Capture(cause).Throw(); throw;
        }
    }
    private static async Task Consume(Task actual)
    {
        try { await actual; }
        catch (Exception cause)
        {
            if (actual.Exception is { } group) throw group;
            ExceptionDispatchInfo.Capture(cause).Throw(); throw;
        }
    }
    private async Task<AuthenticatedResourceActor?> ActorAsync(Original row, CancellationToken token)
    {
        var actual = Source(row, () => _actors.GetCurrentAsync(token).AsTask());
        try { return await Consume(actual); }
        catch when (actual.IsCanceled && !row.Dispatched && token.IsCancellationRequested)
        { row.ExpectedCancellation = true; throw; }
    }
    private sealed record StorageResult(HomeDashboardLayout? Layout, bool Saved);
    private async Task<StorageResult> StorageAsync(Original row, Binding binding, string action, long? revision,
        HomeDashboardLayout? proposed, CancellationToken caller)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        var failures = new List<Exception>(); StorageResult? value = null; Task<StorageResult>? actual = null;
        Exception? cancelled = null;
        try
        {
            actual = Source(row, () => StorageOriginalAsync(row, binding, action, revision, proposed, cancellation.Token));
            value = await Consume(actual);
        }
        catch (Exception cause)
        {
            if (actual is { IsCanceled: true }) cancelled = cause;
            else Add(failures, cause);
        }
        finally { try { cancellation.Dispose(); } catch (Exception cause) { Add(failures, cause); } }
        if (failures.Count != 0)
        {
            if (cancelled is not null) Add(failures, cancelled);
            var cause = failures.Count == 1 ? failures[0] : new AggregateException("Home original and linked lifetime cleanup failed.", failures);
            if (action == "Save" && row.Dispatched) throw new HomeDashboardCommitOutcomeUnknownException(row.RawReply, row.Proposal!, cause);
            ExceptionDispatchInfo.Capture(cause).Throw();
        }
        if (cancelled is not null) ExceptionDispatchInfo.Capture(cancelled).Throw();
        return value!;
    }
    private async Task<StorageResult> StorageOriginalAsync(Original row, Binding binding, string action, long? revision,
        HomeDashboardLayout? proposed, CancellationToken token)
    {
        lock (_gate) if (binding.Revoked) throw new ObjectDisposedException("Original Home view");
        var actor = await ActorAsync(row, token);
        if (actor is null)
        {
            var cause = new HomeDashboardNoCurrentActorException();
            lock (_gate) for (Original? current = row; current is not null; current = current.Parent) current.ExpectedNoActor = cause;
            throw cause; // Exact lawful null BEFORE any JS/import/database acquisition.
        }
        ValidateActor(actor);
        if (binding.Actor is { } expected && expected != actor)
            throw new InvalidOperationException("The original Home view's verified account/session changed; reopen it before saving.");
        DemandCurrent(); token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        var proposal = proposed is null ? null : JsonSerializer.Serialize(ValidateLayout(proposed));
        row.Proposal = proposal;
        if (action == "Save" && (revision is null || revision < 0 || proposed!.Revision != checked(revision.Value + 1)))
            throw new InvalidDataException("Home CAS requires the exact next revision.");
        var args = JsonSerializer.Serialize(new { profile = actor.ProfileId, expected = revision?.ToString(CultureInfo.InvariantCulture),
            revision = (proposed?.Revision ?? revision)?.ToString(CultureInfo.InvariantCulture), json = proposal });
        var initialize = Source(row, () => _transport.InitializeAsync(DemandCurrent));
        await Consume(initialize);
        foreach (var actual in _transport.OriginalInitializationTasks) Retain(row, actual);
        DemandCurrent(); token.ThrowIfCancellationRequested();
        lock (_gate) for (Original? current = row; current is not null; current = current.Parent) current.Dispatched = true;
        Task<string>? raw = null;
        CancellationTokenRegistration registration = default;
        var failures = new List<Exception>(); StorageResult? result = null;
        try
        {
            raw = Source(row, () => _transport.InvokeAsync(id, action, args, token.IsCancellationRequested));
            registration = token.Register(() =>
            {
                try { Source(row, () => { _transport.Cancel(id); return true; }); }
                catch (Exception stop) { lock (_gate) row.Stops.Add(stop); }
            });
            row.RawReply = await Consume(raw);
            result = Decode(row.RawReply, actor.ProfileId, action, revision, proposal);
            row.Decoded = result.Layout;
            var current = await ActorAsync(row, token);
            if (current != actor) throw new InvalidOperationException("The SAME verified account/session changed after the actual Home storage operation.");
            DemandCurrent(); token.ThrowIfCancellationRequested();
            row.PublishBinding = binding; row.PublishActor = actor;
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            try { registration.Dispose(); } catch (Exception cause) { Add(failures, cause); }
            lock (_gate) foreach (var stop in row.Stops) Add(failures, stop);
        }
        if (failures.Count != 0)
        {
            var original = failures.Count == 1 ? failures[0] : new AggregateException("Actual Home storage/stop/cleanup failures.", failures);
            // A dispatched write with a failed or malformed receipt is never reported as a proven no-effect save.
            if (action == "Save") throw new HomeDashboardCommitOutcomeUnknownException(row.RawReply, proposal!, original);
            ExceptionDispatchInfo.Capture(original).Throw();
        }
        return result!;
    }
    private static void ValidateActor(AuthenticatedResourceActor actor)
    {
        if (!Profile.IsMatch(actor.ProfileId) || actor.AccountId is not { } id || id == Guid.Empty ||
            !actor.ProfileId.EndsWith(":" + id.ToString("D"), StringComparison.Ordinal) || actor.OrganisationId is not null ||
            string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
            throw new InvalidDataException("Only the actual verified account-profile partition may hold browser Home preferences.");
    }
    internal static HomeDashboardLayout ValidateLayout(HomeDashboardLayout layout)
    {
        if (layout.SchemaVersion != 1 || layout.Revision < 0 || layout.Tiles is null || layout.Tiles.Count > 256 ||
            !Enum.IsDefined(layout.ChangeKind) || layout.Tiles.Any(tile => tile is null ||
                !Guid.TryParseExact(tile.TileInstanceId, "D", out var id) || id == Guid.Empty || tile.TileInstanceId != id.ToString("D") ||
                string.IsNullOrWhiteSpace(tile.ProviderId) || string.IsNullOrWhiteSpace(tile.TileType) || tile.Order < 0 ||
                !Enum.IsDefined(tile.Size) || !Enum.IsDefined(tile.Visibility) || !Enum.IsDefined(tile.Lifetime) ||
                tile.Configuration is null || tile.Configuration.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) ||
                tile.SourceEntityIds is null || tile.SourceEntityIds.Any(string.IsNullOrWhiteSpace)) ||
            layout.Tiles.Select(tile => tile.TileInstanceId).Distinct(StringComparer.Ordinal).Count() != layout.Tiles.Count ||
            layout.Tiles.Select(tile => tile.Order).Distinct().Count() != layout.Tiles.Count)
            throw new InvalidDataException("The complete Home layout contains invalid schema/revision/tile identities.");
        if (layout.ParentRevision is { } parent && (!long.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0 || parent != parsed.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidDataException("The Home parent revision is not exact Int64.");
        return layout;
    }
    private static StorageResult Decode(string raw, string profile, string action, long? expected, string? proposal)
    {
        using var document = JsonDocument.Parse(raw); RejectDuplicates(document.RootElement);
        var reply = document.RootElement;
        if (reply.GetProperty("ok").ValueKind != JsonValueKind.True || reply.GetProperty("profile").GetString() != profile)
            throw new InvalidDataException("The original Home storage reply did not prove this operation's outcome.");
        var committed = reply.GetProperty("committed").GetBoolean();
        var outcome = reply.GetProperty("outcome").GetString();
        if (action == "Save" ? (outcome is not ("Saved" or "Conflict") || committed != (outcome == "Saved"))
            : outcome != "Read" || committed)
            throw new InvalidDataException("The actual Home receipt has incompatible commit/outcome metadata.");
        var record = reply.GetProperty("record"); HomeDashboardLayout? layout = null;
        if (record.ValueKind != JsonValueKind.Null)
        {
            if (record.GetProperty("schema").GetInt32() != 1 || record.GetProperty("profile").GetString() != profile)
                throw new InvalidDataException("The stored Home row belongs to a different schema/profile.");
            var revision = record.GetProperty("revision").GetString();
            if (!long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0 || revision != parsed.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("The stored Home revision is not exact Int64.");
            var json = record.GetProperty("json").GetString() ?? throw new InvalidDataException("The complete stored Home JSON is missing.");
            if (Encoding.UTF8.GetByteCount(json) > 1_048_576 || record.GetProperty("hash").GetString() !=
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant())
                throw new InvalidDataException("The complete stored Home JSON/hash is corrupt; preserve the original row.");
            using var payload = JsonDocument.Parse(json); RejectDuplicates(payload.RootElement);
            layout = ValidateLayout(payload.RootElement.Deserialize<HomeDashboardLayout>() ?? throw new InvalidDataException("The stored Home layout is null."));
            if (layout.Revision != parsed || (action == "GetRevision" && layout.Revision != expected) ||
                (outcome == "Saved" && (json != proposal || layout.Revision != checked(expected!.Value + 1))))
                throw new InvalidDataException("The actual Home row lost its exact requested revision/proposal.");
        }
        if (outcome == "Saved" && layout is null) throw new InvalidDataException("The actual Home save did not return its committed record.");
        return new(layout, outcome == "Saved");
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate stored Home JSON properties are ambiguous."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    public void RevokePrivateContext()
    { lock (_gate) { _revoked = true; foreach (var binding in _bindings) { binding.Revoked = true; binding.Actor = null; } } }
    private void DemandExternalClose()
    {
        if (_physical?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("A physical Home source cannot join its encompassing close.");
        for (var row = _executing.Value; row is not null; row = row.Parent)
            if (!row.Outer.IsCompleted || !row.Driver.IsCompleted || !row.Publisher.IsCompleted || row.Sources.Any(actual => !actual.IsCompleted))
                throw new InvalidOperationException("A live Home original must return before its external close.");
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalClose();
        var start = new TaskCompletionSource(); Task actual;
        lock (_gate) { if (_close is not null) return new(_close); if (_closeProbing) throw new InvalidOperationException("Original Home close is already probing its physical owner."); _closeProbing = true; }
        try
        {
            // The qualified JS host refuses a returned/member own-join synchronously
            // BEFORE managed admission. Its SAME actual close Promise is retained by the bridge.
            if (_transport.HasOriginalOwner)
            {
                var owners = _physical ??= []; owners.Add(this);
                try { _transport.PrepareClose(); } finally { owners.RemoveAt(owners.Count - 1); }
            }
            lock (_gate)
            {
                _revoked = true; _closeProbing = false;
                foreach (var binding in _bindings) { binding.Revoked = true; binding.Actor = null; }
                actual = CloseAsync(start.Task, _originals.ToArray()); _close = actual;
            }
        }
        catch { lock (_gate) _closeProbing = false; throw; }
        start.SetResult(); return new(actual);
    }
    private bool Expected(Original row)
    {
        if (row.Dispatched || row.Stops.Count != 0 || !row.Publisher.IsCompletedSuccessfully) return false;
        if (row.ExpectedNoActor is { } cause)
            return (row.Outer.IsCompletedSuccessfully || Only(row.Outer, cause)) && (row.Driver.IsCompletedSuccessfully || Only(row.Driver, cause)) &&
                row.Sources.All(actual => actual.IsCompletedSuccessfully || Only(actual, cause));
        return row.ExpectedCancellation && row.Outer.IsCanceled && row.Driver.IsCanceled &&
            (row.Caller.IsCancellationRequested || _lifetime.IsCancellationRequested) && row.Sources.All(actual => actual.IsCompletedSuccessfully || actual.IsCanceled);
    }
    private static bool Only(Task actual, Exception cause) => actual.IsFaulted && actual.Exception!.Flatten().InnerExceptions is { Count: 1 } leaves && ReferenceEquals(leaves[0], cause);
    private async Task CloseAsync(Task start, Original[] rows)
    {
        await start; var failures = new List<Exception>();
        if (_capacityFailure is not null) Add(failures, _capacityFailure);
        try { _lifetime.Cancel(); } catch (Exception cause) { Add(failures, cause); }
        foreach (var row in rows)
        {
            var collected = new List<Exception>();
            async Task Join(Task task)
            {
                try { await task; } catch (Exception cause) { Add(collected, task.Exception ?? cause); }
            }
            await Join(row.Outer); await Join(row.Driver); await Join(row.Publisher);
            Task[] sources; lock (_gate) sources = row.Sources.ToArray();
            foreach (var source in sources) await Join(source);
            if (!Expected(row)) foreach (var cause in collected) Add(failures, cause);
            lock (_gate) foreach (var cause in row.Stops) Add(failures, cause);
        }
        foreach (var import in _transport.OriginalInitializationTasks)
            try { await import; } catch (Exception cause) { Add(failures, import.Exception ?? cause); }
        // Late acquired owner/imports are joined before closing that exact owner; no replacement module is closed.
        try
        {
            if (_transport.HasOriginalOwner)
            {
                _transport.PrepareClose(); var close = _transport.JoinCloseAsync();
                try { await close; } catch (Exception cause) { Add(failures, close.Exception ?? cause); }
            }
        }
        catch (Exception cause) { Add(failures, cause); }
        try { _lifetime.Dispose(); } catch (Exception cause) { Add(failures, cause); }
        if (failures.Count != 0) throw new AggregateException("Actual Home originals/storage/cleanup failed.", failures);
    }
    private static void Add(List<Exception> failures, Exception cause)
    { if (!failures.Any(previous => ReferenceEquals(previous, cause))) failures.Add(cause); }
}
internal sealed class HomeDashboardNoCurrentActorException() : InvalidOperationException("Sign in to save or reopen this account's Home dashboard.") {}
internal sealed class HomeDashboardCommitOutcomeUnknownException(string? reply, string proposal, Exception original)
    : Exception("The dispatched Home save has an unknown outcome. Its original proposal/reply were retained; reopen before retrying.", original)
{ internal string? OriginalReply { get; } = reply; internal string OriginalProposal { get; } = proposal; }

internal interface IHomeDashboardBrowserTransport
{
    Task InitializeAsync(Action demandCurrent);
    IReadOnlyList<Task> OriginalInitializationTasks { get; }
    bool HasOriginalOwner { get; }
    Task<string> InvokeAsync(string requestId, string action, string arguments, bool cancelled);
    void Cancel(string requestId);
    void PrepareClose();
    Task JoinCloseAsync();
}

[SupportedOSPlatform("browser")]
internal sealed partial class HomeDashboardBrowserTransport : IHomeDashboardBrowserTransport
{
    private Task? _initialization;
    private Task<JSObject>? _import;
    private string? _owner;
    private bool _prepared;
    public IReadOnlyList<Task> OriginalInitializationTasks => _import is null ? [] : [_import];
    public bool HasOriginalOwner => _owner is not null;
    public Task InitializeAsync(Action demandCurrent)
    {
        if (_initialization is not null) return _initialization;
        var start = new TaskCompletionSource();
        _initialization = InitializeOriginalAsync(start.Task, demandCurrent);
        start.SetResult(); return _initialization;
    }
    private async Task InitializeOriginalAsync(Task start, Action demandCurrent)
    {
        await start;
        demandCurrent();
        var url = new Uri(new Uri(ReadLocation()), "Home/Storage/home-dashboard-indexeddb.js").AbsoluteUri;
        _import = JSHost.ImportAsync("nineToOneHomeDashboard", url, CancellationToken.None);
        await _import;
        demandCurrent();
        _owner = OpenOwner(); // Borrow the shared namespace; custody belongs only to this issued owner key.
        demandCurrent();
    }
    public Task<string> InvokeAsync(string id, string action, string args, bool cancelled) => Invoke(_owner!, id, action, args, cancelled);
    public void Cancel(string id) => CancelOriginal(_owner!, id);
    public void PrepareClose() { if (!_prepared && _owner is { } owner) { PrepareOriginalClose(owner); _prepared = true; } }
    public Task JoinCloseAsync() => _owner is { } owner ? JoinOriginalClose(owner) : Task.CompletedTask;
    [JSImport("globalThis.location.toString")] private static partial string ReadLocation();
    [JSImport("openOwner", "nineToOneHomeDashboard")] private static partial string OpenOwner();
    [JSImport("invoke", "nineToOneHomeDashboard")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string owner, string id, string action, string args, bool cancelled);
    [JSImport("cancel", "nineToOneHomeDashboard")] private static partial void CancelOriginal(string owner, string id);
    [JSImport("prepareClose", "nineToOneHomeDashboard")] private static partial void PrepareOriginalClose(string owner);
    [JSImport("joinClose", "nineToOneHomeDashboard")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task JoinOriginalClose(string owner);
}
