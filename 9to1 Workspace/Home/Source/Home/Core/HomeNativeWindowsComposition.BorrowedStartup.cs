using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeNativeWindowsComposition
{
    private readonly bool _originalBorrowedHomeExpected;
    private IHomeCoreApi? _originalBorrowedApi;
    private HomeNativeStartupSession? _originalBorrowedSession;
    private AuthenticatedResourceActor? _originalBorrowedActor;
    private OriginalBorrowedHomeObservation? _originalBorrowedCurrent;
    private readonly ConditionalWeakTable<OriginalBorrowedHomeObservation, BorrowedRead> _originalBorrowedIssued = new();
    private sealed class BorrowedRead(HomeOwnershipOriginalSourceCallbacks sources)
    { internal readonly HomeOwnershipOriginalSourceCallbacks Sources = sources; internal Task<OriginalBorrowedHomeObservation> Driver = null!; }
    public bool IsOriginalBorrowedHomeExpected => _originalBorrowedHomeExpected;
    public IHomeCoreApi OriginalConsumerApi => _originalBorrowedHomeExpected ? _originalBorrowedApi! : Api;
    public sealed class OriginalBorrowedHomeObservation
    {
        internal readonly HomeNativeWindowsComposition Owner;
        internal OriginalBorrowedHomeObservation(HomeNativeWindowsComposition owner, AuthenticatedResourceActor actor,
            HomeCoreStateSnapshot? snapshot, string code, string message, string? review)
        { Owner = owner; Actor = actor; Snapshot = snapshot; Code = code; Message = message; PermissionRequestId = review; }
        public AuthenticatedResourceActor Actor { get; }
        public HomeCoreStateSnapshot? Snapshot { get; }
        public string Code { get; }
        public string Message { get; }
        public string? PermissionRequestId { get; }
        public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
        public bool RequiredServicesAvailable => Snapshot is { } state && RequiredBorrowedServices.All(id => state.Services.Any(service =>
            service.ServiceId == id && service.IsAvailable && service.State == HomeServiceLifecycleState.Ready &&
            service.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major));
    }
    private static readonly string[] RequiredBorrowedServices = ["home.core", "home.state", "permissions.trust"];
    public Task StartOriginalBorrowedHomeAsync(HomeNativeStartupSession originalSession, AuthenticatedResourceActor originalRootActor)
    {
        ArgumentNullException.ThrowIfNull(originalSession); ArgumentNullException.ThrowIfNull(originalRootActor);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _originalProcessRetiring, this);
            if (!_originalBorrowedHomeExpected || _bootstrap is not null || _linuxBootstrap is not null ||
                _originalBorrowedSession is not null && (!ReferenceEquals(_originalBorrowedSession, originalSession) || _originalBorrowedActor != originalRootActor))
                throw new UnauthorizedAccessException("Only the SAME preconfigured original connected Home borrower may start here.");
            if (_start is not null) return _start;
            _originalBorrowedSession = originalSession; _originalBorrowedActor = originalRootActor;
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _start = BorrowedStart(begin.Task); begin.SetResult(); return _start;
        }
        async Task BorrowedStart(Task begin)
        {
            await begin.ConfigureAwait(false);
            var observation = await ObserveOriginalBorrowedHomeWithinSourceAsync(RunOriginalHomeProcessSource,
                RetainOriginalHomeProcessSource, _process.Token).ConfigureAwait(false);
            if (!IsCurrentOriginalBorrowedHomeObservation(observation) || !observation.RequiredServicesAvailable)
                throw new UnauthorizedAccessException("The actual existing Home did not admit required services: " + observation.Code + ": " + observation.Message);
            // No local Runtime.Start, host, native lease or private session issuer
            // is started. This source borrows the actual remote Home only.
        }
    }
    public Task<OriginalBorrowedHomeObservation> ObserveOriginalBorrowedHomeWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new HomeOwnershipOriginalSourceCallbacks(scope, retain); var work = new BorrowedRead(sources);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _originalProcessRetiring, this);
            if (!_originalBorrowedHomeExpected || _originalBorrowedSession is null) throw new UnauthorizedAccessException("The original authenticated Home session is not bound.");
            work.Driver = Read(); RetainOriginalHomeProcessSource(work.Driver);
        }
        try { sources.Run(() => retain(work.Driver)); }
        catch (Exception cause) { begin.SetException(cause); throw; }
        begin.SetResult();
        return work.Driver;
        async Task<OriginalBorrowedHomeObservation> Read()
        {
            await begin.Task.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_process.Token, token);
            var actor = await sources.ReadAsync(() => Profiles.GetCurrentWithinOriginalSourceAsync(scope, retain, lifetime.Token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual borrower OS profile is unavailable.");
            if (actor != _originalBorrowedActor) throw new UnauthorizedAccessException("The actual borrower OS profile differs from the original Root-attested Home actor.");
            var session = _originalBorrowedSession!;
            var compatible = await sources.ReadAsync(() => session.CheckAsync(lifetime.Token)).ConfigureAwait(false);
            HomeCoreStateSnapshot? snapshot = null; string code = compatible.Code, message = compatible.Message; string? review = compatible.PermissionRequestId;
            if (compatible.CanStartNormally)
            {
                var actual = await sources.ReadAsync(() => session.ReadOriginalStateAsync(lifetime.Token)).ConfigureAwait(false);
                code = actual.Operation.Code; message = actual.Operation.Message; review = actual.PermissionRequestId;
                if (actual.Operation.Succeeded && actual.Operation.Value is { } current)
                {
                    if (current.Revision < 0 || current.Services is null || current.Services.Count > 128 || current.Services.Any(service =>
                        service is null || service.Revision < 0 || !Enum.IsDefined(service.State)) ||
                        current.Services.Select(service => service.ServiceId).Distinct(StringComparer.Ordinal).Count() != current.Services.Count)
                        throw new InvalidDataException("The actual connected Home service observation is invalid.");
                    snapshot = current with { Services = Array.AsReadOnly(current.Services.Select(service => service with { }).ToArray()) };
                }
            }
            if (actor != await sources.ReadAsync(() => Profiles.GetCurrentWithinOriginalSourceAsync(scope, retain, lifetime.Token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The borrower profile changed across its original Home reads.");
            var result = new OriginalBorrowedHomeObservation(this, actor, snapshot, code, message, review);
            sources.Run(() => {
                lifetime.Token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_closing || _originalProcessRetiring, this);
                    if (!ReferenceEquals(session, _originalBorrowedSession) || !session.IsOriginalConnectionRetained)
                        throw new UnauthorizedAccessException("The original connected Home retired during observation.");
                    _originalBorrowedIssued.Add(result, work); _originalBorrowedCurrent = result;
                }
            });
            return result;
        }
    }
    public bool IsCurrentOriginalBorrowedHomeObservation(OriginalBorrowedHomeObservation same)
    {
        lock (_sync) return !_closing && !_originalProcessRetiring && !_process.IsCancellationRequested &&
            same is not null && ReferenceEquals(same.Owner, this) && ReferenceEquals(_originalBorrowedCurrent, same) &&
            _originalBorrowedSession?.IsOriginalConnectionRetained == true && _originalBorrowedIssued.TryGetValue(same, out var work) &&
            work.Driver.IsCompletedSuccessfully && ReferenceEquals(work.Driver.Result, same) && work.Sources.HasIndependentlyJoinedHealthySources;
    }
    public bool IsCurrentOriginalBorrowedHomeCorrelation(string correlation)
    { lock (_sync) return _originalBorrowedCurrent is { } current && current.CorrelationId == correlation && IsCurrentOriginalBorrowedHomeObservation(current); }
    private HomeNativeStartupSession DemandOriginalBorrowedSession()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _originalProcessRetiring, this);
            return _originalBorrowedSession is { IsOriginalConnectionRetained: true } session ? session
                : throw new UnauthorizedAccessException("The actual authenticated existing Home session is unavailable.");
        }
    }
    private async Task<HomeNativeCoreApiResult<T>> ReadOriginalBorrowedCore<T>(
        Func<HomeNativeStartupSession, CancellationToken, Task<HomeNativeCoreApiResult<T>>> read, CancellationToken token)
    {
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_process.Token, token);
        var session = DemandOriginalBorrowedSession();
        Task<HomeNativeCoreApiResult<T>>? actual = null; var errors = new List<Exception>();
        try { actual = read(session, lifetime.Token); RetainOriginalHomeProcessSource(actual); } catch (Exception cause) { errors.Add(cause); }
        HomeNativeCoreApiResult<T>? result = null;
        if (actual is not null) try { result = await actual.ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("The original borrowed Core read retained actual failures.", errors);
        lifetime.Token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(session, DemandOriginalBorrowedSession())) throw new UnauthorizedAccessException("The original borrowed Core session changed.");
        return result ?? throw new InvalidOperationException("No original borrowed Core result was returned.");
    }

    // IHomeCoreApi's caller parameter belongs to its local call shape only. The
    // remote Home always authorizes THIS retained native session's kernel/Root
    // identity and actual service-read review; caller metadata cannot impersonate
    // Home, replace the session actor or supply resource/data consent.
    private sealed class BorrowedCoreApi(HomeNativeWindowsComposition owner) : IHomeCoreApi
    {
        public async Task<HomeCoreOperationResult<HomeCoreStateSnapshot>> GetStateAsync(HomeCallerIdentity caller, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return (await owner.ReadOriginalBorrowedCore((session, actual) => session.ReadOriginalStateAsync(actual), token).ConfigureAwait(false)).Operation;
        }
        public async Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(HomeCallerIdentity caller, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return (await owner.ReadOriginalBorrowedCore((session, actual) => session.ReadOriginalServicesAsync(actual), token).ConfigureAwait(false)).Operation;
        }
        public async Task<HomeCoreOperationResult<HomeServiceDescriptor>> GetServiceAsync(HomeCallerIdentity caller, string serviceId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return (await owner.ReadOriginalBorrowedCore((session, actual) => session.ReadOriginalServiceAsync(serviceId, actual), token).ConfigureAwait(false)).Operation;
        }
        public async Task<HomeCompatibilityResult> GetCompatibilityAsync(HomeCallerIdentity caller, HomeCompatibilityRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            var actual = await owner.ReadOriginalBorrowedCore((session, current) => session.ReadOriginalCompatibilityAsync(request, current), token).ConfigureAwait(false);
            return actual.Operation.Succeeded && actual.Operation.Value is { } value ? value : throw new UnauthorizedAccessException(actual.Operation.Message);
        }
    }
}
