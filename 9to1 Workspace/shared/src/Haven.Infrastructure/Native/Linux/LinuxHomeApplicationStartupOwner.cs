using System.Net.Sockets;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Actual selected-process startup borrows the authenticated existing Home.
/// It creates no Home listener, lease, installed identity, profile or data grant.</summary>
public sealed class LinuxHomeApplicationStartupOwner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime;
    private readonly HomeCompatibilityRequest _requirements;
    private readonly LinuxHomeRootHostVerifier _verifier;
    private readonly List<Task> _originals = [];
    private readonly List<Exception> _callbackFailures = [];
    private Socket? _socket;
    private HomeNativeStartupSession? _session;
    private LinuxHomeRootHostVerifier.Observation? _host;
    private Task<HomeNativeStartupObservation>? _connect;
    private Task? _initialize, _close;
    private bool _retiring;
    public LinuxHomeApplicationStartupOwner(string actualRootEndpoint, HomeCompatibilityRequest compiledRequirements, CancellationToken actualAppLifetime)
    {
        ArgumentNullException.ThrowIfNull(compiledRequirements);
        var rows = compiledRequirements.RequiredServices?.Take(65).ToArray();
        if (!actualAppLifetime.CanBeCanceled || rows is null || rows.Length is 0 or > 63 || rows.Any(value => value is null) ||
            rows.Select(value => value.ServiceId).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new ArgumentException("The actual app lifetime and bounded compiled Home service requirements are required.");
        if (!rows.Any(value => value.ServiceId == "home.core" && value.Required))
            rows = rows.Concat([new HomeServiceRequirement("home.core", HomeCoreServiceCatalog.CurrentContractVersion.Major,
                HomeCoreServiceCatalog.CurrentContractVersion.Minor)]).ToArray();
        _requirements = compiledRequirements with { RequiredServices = Array.AsReadOnly(rows.Select(value => value with { }).ToArray()) };
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(actualAppLifetime);
        _verifier = new(actualRootEndpoint, _requirements.AppId, _lifetime.Token);
    }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task<HomeNativeStartupObservation> ConnectWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        lock (_gate) return _connect ??= Admit(scope, retain, async (source, caller) => {
            _host = await source.Read(() => _verifier.ObserveOriginalHomeAsync(caller)).ConfigureAwait(false);
            source.Run(() => _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified));
            await source.Read(() => _socket!.ConnectAsync(new UnixDomainSocketEndPoint(_host.Listening.PipeName), caller).AsTask()).ConfigureAwait(false);
            async Task<bool> AttachOriginal()
            {
                // This owner captures the disposable before any caller postguard;
                // the finite source task returns only detached completion metadata.
                _session = await HomeNativeStartupSession.AttachAsync(_socket!, _verifier,
                    new("home", "9to1.package:" + _host.HomeActivation.PackageId), _requirements, _lifetime.Token, caller).ConfigureAwait(false);
                return _session is not null;
            }
            if (!await source.Read(AttachOriginal).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual selected app could not authenticate the original connected Home.");
            var connectedSession = _session ?? throw new InvalidOperationException("The original attached Home session was not retained.");
            return await source.Read(() => connectedSession.CheckAsync(caller)).ConfigureAwait(false);
        }, token);
    }
    public Task InitializeWithinOriginalSourceAsync(Func<HomeNativeStartupSession, AuthenticatedResourceActor, Task> initialize,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        lock (_gate) return _initialize ??= Admit(scope, retain, async (source, caller) => {
            if (_connect is not { IsCompletedSuccessfully: true } || !_connect.Result.CanStartNormally || _session is null || _host is null)
                throw new UnauthorizedAccessException("The SAME actual compatible Home connection is required before app initialization.");
            var before = await source.Read(() => _verifier.ObserveOriginalHomeAsync(caller)).ConfigureAwait(false);
            if (!SameHost(before, _host)) throw new UnauthorizedAccessException("The actual controlled selected/Home lease tuple changed before initialization.");
            await source.Read(() => initialize(_session, _host.Listening.Actor)).ConfigureAwait(false);
            var after = await source.Read(() => _verifier.ObserveOriginalHomeAsync(caller)).ConfigureAwait(false);
            if (!SameHost(after, _host) || !_session.IsOriginalConnectionRetained) throw new UnauthorizedAccessException("The actual selected/Home session retired during initialization.");
            return true;
        }, token);
    }
    private static bool SameHost(LinuxHomeRootHostVerifier.Observation left, LinuxHomeRootHostVerifier.Observation right)
        => left.Listening == right.Listening && left.HomeProcess == right.HomeProcess && left.SelectedProcess == right.SelectedProcess &&
            System.Text.Json.JsonSerializer.Serialize(left.HomeActivation) == System.Text.Json.JsonSerializer.Serialize(right.HomeActivation);
    private Task<T> Admit<T>(Action<Action> scope, Action<Task> retain, Func<CanonicalSqliteOriginalSourceScope, CancellationToken, Task<T>> body, CancellationToken token)
    {
        void DemandAdmission()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_retiring, this);
                if (_callbackFailures.Count != 0) throw new InvalidOperationException("The original selected startup callback admission failed.",
                    new AggregateException(_callbackFailures));
            }
        }
        var source = new CanonicalSqliteOriginalSourceScope(this, scope, retain,
            cause => { lock (_gate) _callbackFailures.Add(cause); }, DemandAdmission);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> actual;
        lock (_gate) { DemandAdmission(); actual = Drive(); _originals.Add(actual); }
        try { source.Run(() => retain(actual)); }
        catch (Exception cause)
        {
            lock (_gate) _callbackFailures.Add(cause);
            begin.SetException(cause); // the privately retained original faults before productive work
            throw;
        }
        begin.SetResult();
        return actual;
        async Task<T> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token);
            T result = default!; var failures = new List<Exception>();
            try { result = await body(source, lifetime.Token).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("The original selected Linux startup source did not settle.", failures);
            return result;
        }
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); lock (_gate) { _retiring = true; return _close ??= Close(_originals.ToArray()); }
        async Task Close(Task[] originals)
        {
            var failures = new List<Exception>();
            lock (_gate) failures.AddRange(_callbackFailures);
            try { _lifetime.Cancel(); } catch (Exception cause) { failures.Add(cause); }
            foreach (var task in originals) try { await task.ConfigureAwait(false); } catch (Exception cause) { failures.Add(task.Exception ?? cause); }
            if (_session is not null) try { await _session.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { _socket?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            try { await _verifier.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { _lifetime.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("Original selected Linux Home startup/cleanup retained actual failures.", failures);
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
