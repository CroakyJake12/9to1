using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Public result metadata is observational. Only the retained original session may dispatch.</summary>
public sealed record HomeNativeCoreApiResult<T>(HomeCoreOperationResult<T> Operation, string? PermissionRequestId);

/// <summary>
/// Canonical trusted-host admission and Home broker adapter for native service discovery.
/// No public caller record, installation receipt or remote actor grants access. The host must
/// supply its SAME actually accepted socket, held Home lease and original connection/session
/// lifetime. This component does not read that socket, acquire a lease or own the transport.
/// </summary>
public sealed partial class HomeNativeCoreApiSessions : IHomeCoreAuthorization, IAsyncDisposable
{
    private readonly HomePermissionTrustService _permissions;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IHomeNativeInstalledPeerVerifier _verifier;
    private readonly Func<IHomeCoreApi> _api;
    private readonly ConcurrentDictionary<Guid, Context> _sessions = new();
    private readonly ConcurrentDictionary<HomeCallerIdentity, Operation> _operations = new(ReferenceEqualityComparer.Instance);
    private readonly object _admissionGate = new();
    private Task? _closeTask;
    private bool _closing;

    public HomeNativeCoreApiSessions(HomePermissionTrustService permissions,
        IAuthenticatedResourceActorSource actors, IHomeNativeInstalledPeerVerifier verifier,
        Func<IHomeCoreApi> canonicalApi,
        Func<HomeNativeCoreApiSessions, IHomeNativeFilesDomainOwner?>? originalFilesOwnerFactory = null)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _api = canonicalApi ?? throw new ArgumentNullException(nameof(canonicalApi));
        _originalFilesOwner = new(() => originalFilesOwnerFactory?.Invoke(this), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Trusted accepting-server composition only. A copied observed-peer DTO is never input.</summary>
    public async ValueTask<Session?> AcceptUnixAsync(Socket originalAcceptedSocket,
        HomeNativeSessionLease originalHeldHomeLease, CancellationToken originalConnectionLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalAcceptedSocket);
        ArgumentNullException.ThrowIfNull(originalHeldHomeLease);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_admissionGate) if (_closing) return null;
        if (!OperatingSystem.IsLinux()) return null;
        return await AcceptOriginalAsync(new OriginalChannel(originalAcceptedSocket), originalHeldHomeLease,
            originalConnectionLifetime, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Trusted Windows accepting composition only. Credentials are observed from THIS connected server pipe.</summary>
    public async ValueTask<Session?> AcceptWindowsPipeAsync(NamedPipeServerStream originalAcceptedPipe,
        HomeNativeSessionLease originalHeldHomeLease, CancellationToken originalConnectionLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalAcceptedPipe);
        ArgumentNullException.ThrowIfNull(originalHeldHomeLease);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_admissionGate) if (_closing) return null;
        if (!OperatingSystem.IsWindows() || !originalAcceptedPipe.IsConnected) return null;
        return await AcceptOriginalAsync(new OriginalChannel(originalAcceptedPipe), originalHeldHomeLease,
            originalConnectionLifetime, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Session?> AcceptOriginalAsync(OriginalChannel originalChannel,
        HomeNativeSessionLease originalHeldHomeLease, CancellationToken originalConnectionLifetime,
        CancellationToken cancellationToken)
    {
        if (!originalConnectionLifetime.CanBeCanceled ||
            originalConnectionLifetime.IsCancellationRequested || !originalHeldHomeLease.IsHeld ||
            _verifier is not IHomeNativeInstalledPeerOriginalActorVerifier originalVerifier) return null;
        var actor = await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || !Valid(actor) || actor.OrganisationId is not null || actor.ProfileId != originalHeldHomeLease.ProfileId)
            return null;
        var observed = originalChannel.Observe();
        if (observed is null) return null;
        var peer = Copy(await originalVerifier.VerifyForActorAsync(observed, actor, cancellationToken).ConfigureAwait(false));
        if (peer is null || await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor)
            return null;
        var context = new Context(originalChannel, originalHeldHomeLease, actor, observed, peer,
            originalConnectionLifetime);
        var added = false;
        Exception? primary = null;
        try
        {
            if (!await CurrentAsync(context, cancellationToken).ConfigureAwait(false)) return null;
            lock (_admissionGate)
            {
                if (_closing || _sessions.Count >= 128 || !_sessions.TryAdd(context.Id, context)) return null;
                added = true;
            }
            return new Session(this, context.Id);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (!added)
                try { await context.CloseAsync().ConfigureAwait(false); }
                catch (Exception cleanup) when (primary is not null && !ReferenceEquals(primary, cleanup))
                { throw new AggregateException("Original native admission and cleanup both failed.", primary, cleanup); }
        }
    }

    /// <summary>Canonical DI shutdown retains the same original cancellation/drain task.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_admissionGate)
        {
            if (_closeTask is not null) return new(_closeTask);
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = CloseAllAsync(start.Task);
            start.SetResult();
            return new(_closeTask);
        }
    }
    private async Task CloseAllAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var original = _sessions.Values.ToArray();
        // Start every original retirement before awaiting any one in-flight action.
        var drains = original.Select(context => context.CloseAsync().AsTask()).ToArray();
        List<Exception> failures = [];
        for (var index = 0; index < drains.Length; index++)
        {
            try { await drains[index].ConfigureAwait(false); }
            catch (Exception error) { Add(failures, error, null); }
            finally { _sessions.TryRemove(original[index].Id, out _); }
        }
        Throw(null, failures);
    }

    /// <summary>Only one currently executing privately issued operation may authorize its exact target/scope.</summary>
    public async ValueTask<bool> IsAllowedAsync(HomeCallerIdentity caller, string target, IReadOnlySet<string> scopes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (caller is null || scopes is null || scopes.Count != 1 ||
            !scopes.Contains(HomeCoreServiceReadActionPolicies.Scope) ||
            !_operations.TryGetValue(caller, out var operation) || operation.Target != target) return false;
        if (!await CurrentAsync(operation.Context, cancellationToken).ConfigureAwait(false) ||
            !await _permissions.IsExecutionCurrentAsync(operation.RequestId, cancellationToken).ConfigureAwait(false))
            return false;
        return _operations.TryGetValue(caller, out var current) && ReferenceEquals(current, operation) &&
            await CurrentAsync(operation.Context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> CurrentAsync(Context context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _closing) || context.Closed || context.Lifetime.IsCancellationRequested || !context.Lease.IsHeld ||
            context.Lease.ProfileId != context.Actor.ProfileId ||
            _verifier is not IHomeNativeInstalledPeerOriginalActorVerifier originalVerifier) return false;
        if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != context.Actor) return false;
        HomeNativeObservedPeer? observed;
        try { observed = context.Channel.Observe(); }
        catch (Exception error) when (error is SocketException or ObjectDisposedException or InvalidOperationException ||
            context.Channel.IsWindows && error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return false; }
        if (observed is null || observed != context.Observed) return false;
        var peer = Copy(await originalVerifier.VerifyForActorAsync(observed, context.Actor, token).ConfigureAwait(false));
        return peer is not null && SameOwner(peer, context.Peer) && !context.Closed &&
            !context.Lifetime.IsCancellationRequested && context.Lease.IsHeld &&
            await _actors.GetCurrentAsync(token).ConfigureAwait(false) == context.Actor &&
            !context.Closed && !context.Lifetime.IsCancellationRequested && context.Lease.IsHeld;
    }

    private async Task<HomeNativeCoreApiResult<T>> ReadAsync<T>(Context context, string target, JsonElement arguments,
        IReadOnlyList<HomeObjectReference> objects,
        Func<IHomeCoreApi, HomeCallerIdentity, CancellationToken, Task<HomeCoreOperationResult<T>>> read,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(context.Id, out var issued) || !ReferenceEquals(issued, context))
            return Refused<T>("CallerIdentityUnverified", "The original native Home API session has retired.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.Lifetime);
        await context.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        var state = new ReadAttempt();
        HomeNativeCoreApiResult<T>? result = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try { result = await PerformAsync().ConfigureAwait(false); }
        catch (Exception error) { primary = error; }
        finally
        {
            if (state.Operation is { } operation) _operations.TryRemove(operation.Caller, out _);
            if (state.BeginAttempted && state.RequestId is { } requestId)
            {
                try
                {
                    var outcome = primary is OperationCanceledException
                        ? new HomeExecutionOutcome(HomePermissionRequestState.Cancelled, "HOME_CORE_READ_CANCELLED",
                            "The original native service read was cancelled.", objects)
                        : primary is not null
                            ? new HomeExecutionOutcome(HomePermissionRequestState.Failed, "HOME_CORE_READ_FAILED",
                                "The original native service read failed; no successful response was issued.", objects)
                            : new HomeExecutionOutcome(result!.Operation.Succeeded ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                                result.Operation.Code, result.Operation.Message, objects);
                    var audit = await _permissions.RecordExecutionAsync(requestId, outcome, CancellationToken.None).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("Home did not record this exact native read outcome: " + audit.Code);
                }
                catch (Exception error) { Add(cleanup, error, primary); }
            }
            try { context.Gate.Release(); } catch (Exception error) { Add(cleanup, error, primary); }
        }
        Throw(primary, cleanup);
        return result ?? throw new InvalidOperationException("The original native read produced no result.");

        async Task<HomeNativeCoreApiResult<T>> PerformAsync()
        {
            var token = linked.Token;
            if (!await CurrentAsync(context, token).ConfigureAwait(false))
                return Refused<T>("CallerIdentityUnverified", "The original peer, installation, profile or Home lease changed.");
            var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(arguments)));
            var key = target + ":" + digest;
            HomePermissionAuthorization approval;
            Intent intent;
            if (context.Pending.TryGetValue(key, out var retained))
            {
                intent = retained;
                var observed = await _permissions.ReadRequestObservationAsync(intent.Submission.RequestId!, token).ConfigureAwait(false);
                if (!Matches(intent, observed))
                    return Refused<T>("PermissionDenied", "The original reviewed Home request tuple changed.");
                approval = await _permissions.GetAuthorizationAsync(intent.Submission.RequestId!, token).ConfigureAwait(false);
            }
            else
            {
                if (context.Pending.Count >= 16)
                    return Refused<T>("PermissionDenied", "The original session already has its maximum outstanding approval requests.");
                var submission = new HomePermissionRequestSubmission(Guid.NewGuid().ToString("N"), context.PermissionCaller, context.SessionId,
                    new HomePermissionScope(HomeCoreServiceReadActionPolicies.TargetAppId, target, objects).Validate(),
                    new(objects.Select(item => item.ObjectType).Distinct(StringComparer.Ordinal).ToArray(), objects.Count,
                        objects, false, "Read Home service metadata; this request grants no app data or mutation access.", ArgumentsDigest: digest));
                var policy = _permissions.ResolveTrustedActionPolicy(HomeCoreServiceReadActionPolicies.TargetAppId, target);
                if (policy is null) return Refused<T>("PermissionDenied", "The trusted Home service read policy is unavailable.");
                intent = new(submission, policy);
                approval = await _permissions.AuthorizeAsync(submission, token).ConfigureAwait(false);
                if (approval.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
                    context.Pending[key] = intent;
            }
            if (!await CurrentAsync(context, token).ConfigureAwait(false))
                return Refused<T>("CallerIdentityUnverified", "The original native caller changed during Home review admission.");
            if (!approval.IsAllowed)
            {
                if (approval.State != HomePermissionRequestState.PendingApproval) context.Pending.Remove(key);
                return new(new(false, approval.State == HomePermissionRequestState.PendingApproval ? "PermissionRequired" : "PermissionDenied",
                    approval.Message, default, false), approval.RequestId);
            }
            context.Pending.Remove(key);
            state.RequestId = approval.RequestId;
            state.BeginAttempted = true;
            var begun = await _permissions.BeginExecutionAsync(approval.RequestId, token).ConfigureAwait(false);
            if (!begun.IsAllowed)
            {
                state.BeginAttempted = false;
                if (begun.State == HomePermissionRequestState.PendingApproval) context.Pending[key] = intent;
                return new(new(false, begun.State == HomePermissionRequestState.PendingApproval ? "PermissionRequired" : "PermissionDenied",
                    begun.Message, default, false), begun.RequestId);
            }
            if (!await CurrentAsync(context, token).ConfigureAwait(false))
                return new(new(false, "CallerIdentityUnverified", "The original native caller retired before service read dispatch.", default, false), begun.RequestId);
            var executing = await _permissions.ReadRequestObservationAsync(begun.RequestId, token).ConfigureAwait(false);
            if (!Matches(intent, executing) || executing!.State != HomePermissionRequestState.Executing ||
                !await CurrentAsync(context, token).ConfigureAwait(false))
                return new(new(false, "PermissionDenied", "The exact original reviewed tuple changed before dispatch.", default, false), begun.RequestId);
            // This record is never exposed. Equal record copies and replay after this operation remain denied.
            var caller = new HomeCallerIdentity(context.PermissionCaller.CallerId, "installed-native", "kernel-peer+controlled-installed-runtime");
            var operation = new Operation(context, target, begun.RequestId, caller);
            if (!_operations.TryAdd(caller, operation)) throw new InvalidOperationException("The original read admission was ambiguous.");
            state.Operation = operation;
            var value = await read(_api(), caller, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (value.Succeeded && (!await CurrentAsync(context, token).ConfigureAwait(false) ||
                !await _permissions.IsExecutionCurrentAsync(begun.RequestId, token).ConfigureAwait(false) ||
                !await CurrentAsync(context, token).ConfigureAwait(false)))
                value = new(false, "PermissionDenied", "Original native session or Home permission changed before response publication.", default, false);
            return new(value, begun.RequestId);
        }
    }

    private static HomeNativeCoreApiResult<T> Refused<T>(string code, string message) =>
        new(new(false, code, message, default, false), null);
    private static bool Valid(AuthenticatedResourceActor? actor) => actor is not null && Text(actor.ActorId) &&
        Text(actor.ProfileId) && Text(actor.AuthenticationRevision);
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096;
    private static HomeNativeInstalledPeer? Copy(HomeNativeInstalledPeer? peer) => peer is null ||
        !Text(peer.AppId) || peer.InstalledApplicationId == Guid.Empty || !Text(peer.InstallationRevision) ||
        !Text(peer.ExecutableIdentity) || peer.AllowedServiceIds is null ||
        !peer.AllowedServiceIds.Contains(HomeCoreServiceReadActionPolicies.RequiredInstalledServiceId)
        ? null : peer with { AllowedServiceIds = peer.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal),
            Roles = (peer.Roles ?? FrozenSet<string>.Empty).ToFrozenSet(StringComparer.Ordinal) };
    private static bool SameOwner(HomeNativeInstalledPeer left, HomeNativeInstalledPeer right) =>
        left.AppId == right.AppId && left.InstalledApplicationId == right.InstalledApplicationId &&
        left.InstallationRevision == right.InstallationRevision && left.ExecutableIdentity == right.ExecutableIdentity &&
        left.AllowedServiceIds.SetEquals(right.AllowedServiceIds) && left.Roles.SetEquals(right.Roles);
    private bool Matches(Intent intent, HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest? request) => request is not null &&
        request.RequestId == intent.Submission.RequestId && request.Caller == intent.Submission.Caller &&
        request.SessionId == intent.Submission.SessionId && request.Policy == intent.Policy &&
        _permissions.ResolveTrustedActionPolicy(request.Scope.TargetAppId, request.Scope.ActionName) == intent.Policy &&
        request.Scope.TargetAppId == intent.Submission.Scope.TargetAppId &&
        request.Scope.ActionName == intent.Submission.Scope.ActionName &&
        request.Scope.IncludesAllObjects == intent.Submission.Scope.IncludesAllObjects &&
        request.Scope.Objects.SequenceEqual(intent.Submission.Scope.Objects) &&
        request.Impact.ArgumentsDigest == intent.Submission.Impact.ArgumentsDigest &&
        request.Impact.ResourceBinding is null && !request.Impact.IsUnknown;
    private static void Add(List<Exception> failures, Exception error, Exception? primary)
    { if (!ReferenceEquals(error, primary) && !failures.Any(item => ReferenceEquals(item, error))) failures.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        if (cleanup.Count != 0) throw new AggregateException("Original native read and audit/drain failures are retained.",
            primary is null ? cleanup : new[] { primary }.Concat(cleanup));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    /// <summary>Private physical channel custody only. No observed-peer DTO or external observer callback is admitted.</summary>
    private sealed class OriginalChannel
    {
        private readonly Socket? _unix;
        private readonly NamedPipeServerStream? _windows;
        internal OriginalChannel(Socket original) { _unix = original; }
        internal OriginalChannel(NamedPipeServerStream original) { _windows = original; }
        internal bool IsWindows => _windows is not null;
        internal HomeNativeObservedPeer? Observe() => _windows is not null
            ? HomeNativePeerObservation.FromConnectedWindowsPipe(_windows)
            : _unix is not null ? HomeNativePeerObservation.FromAcceptedUnixSocket(_unix) : null;
    }

    private sealed record Operation(Context Context, string Target, string RequestId, HomeCallerIdentity Caller);
    private sealed record Intent(HomePermissionRequestSubmission Submission, HomePermissionActionPolicy Policy);
    private sealed class ReadAttempt
    {
        internal string? RequestId;
        internal bool BeginAttempted;
        internal Operation? Operation;
    }
    private sealed class Context(OriginalChannel channel, HomeNativeSessionLease lease, AuthenticatedResourceActor actor,
        HomeNativeObservedPeer observed, HomeNativeInstalledPeer peer, CancellationToken originalLifetime)
    {
        internal readonly Guid Id = Guid.NewGuid();
        internal readonly OriginalChannel Channel = channel;
        internal readonly HomeNativeSessionLease Lease = lease;
        internal readonly AuthenticatedResourceActor Actor = actor;
        internal readonly HomeNativeObservedPeer Observed = observed;
        internal readonly HomeNativeInstalledPeer Peer = peer;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal readonly Dictionary<string, Intent> Pending = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalLifetime);
        private readonly object _closeGate = new();
        private Task? _close;
        internal IHomeNativeFilesDomainOwner? OriginalFilesOwner;
        internal HomeNativeFilesOriginalConnection? OriginalFilesConnection;
        internal HomeNativeFilesReply? OriginalFilesReply;
        private int _closed;
        internal bool Closed => Volatile.Read(ref _closed) != 0;
        internal CancellationToken Lifetime => _lifetime.Token;
        internal readonly string SessionId = "home-native-core:" + Guid.NewGuid().ToString("N");
        internal HomePermissionCallerIdentity PermissionCaller { get; } = new HomePermissionCallerIdentity(
            "installed:" + peer.InstalledApplicationId.ToString("N"), peer.AppId, "installed-native",
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                peer.AppId, peer.InstalledApplicationId, peer.InstallationRevision, peer.ExecutableIdentity,
                actor.ActorId, actor.ProfileId, actor.AuthenticationRevision }))), true).Validate();
        internal ValueTask CloseAsync()
        {
            lock (_closeGate)
            {
                if (_close is not null) return new(_close);
                Volatile.Write(ref _closed, 1);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseCoreAsync(start.Task);
                start.SetResult();
                return new(_close);
            }
        }
        private async Task CloseCoreAsync(Task start)
        {
            await start.ConfigureAwait(false);
            List<Exception> failures = [];
            try { _lifetime.Cancel(); } catch (Exception error) { failures.Add(error); }
            try { await Gate.WaitAsync().ConfigureAwait(false); Gate.Release(); }
            catch (Exception error) { Add(failures, error, null); }
            try
            {
                if (OriginalFilesOwner is { } owner && OriginalFilesConnection is { } connection)
                    await owner.CloseOriginalConnectionAsync(connection).ConfigureAwait(false);
            }
            catch (Exception error) { Add(failures, error, null); }
            try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error, null); }
            Throw(null, failures);
        }
    }

    /// <summary>Opaque issuer-owned facade. No constructor, caller replacement or serializable grant exists.</summary>
    public sealed partial class Session : IAsyncDisposable
    {
        private readonly HomeNativeCoreApiSessions _issuer;
        private readonly Context _context;
        private readonly object _closeGate = new();
        private Task? _close;
        internal Session(HomeNativeCoreApiSessions issuer, Guid contextId)
        {
            _issuer = issuer;
            _context = issuer._sessions.TryGetValue(contextId, out var original)
                ? original : throw new UnauthorizedAccessException("Only an original admitted Home session can issue this facade.");
        }

        public Task<HomeNativeCoreApiResult<HomeCoreStateSnapshot>> GetStateAsync(CancellationToken token = default) =>
            _issuer.ReadAsync(_context, "9to1.Home.GetState", JsonSerializer.SerializeToElement(new { read = "state" }),
                RegistryObject(), async (api, caller, ct) =>
                {
                    var result = await api.GetStateAsync(caller, ct).ConfigureAwait(false);
                    return result.Value is { } state
                        ? result with { Value = state with { Services = DeclaredServices(state.Services) } } : result;
                }, token);
        public Task<HomeNativeCoreApiResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(CancellationToken token = default) =>
            _issuer.ReadAsync(_context, "9to1.Home.GetServices", JsonSerializer.SerializeToElement(new { read = "services" }),
                RegistryObject(), async (api, caller, ct) =>
                {
                    var result = await api.GetServicesAsync(caller, ct).ConfigureAwait(false);
                    return result.Value is { } services ? result with { Value = DeclaredServices(services) } : result;
                }, token);
        public Task<HomeNativeCoreApiResult<HomeServiceDescriptor>> GetServiceAsync(string serviceId, CancellationToken token = default)
        {
            if (!Text(serviceId) || serviceId != serviceId.Trim())
                return Task.FromResult(Refused<HomeServiceDescriptor>("HomeServiceIncompatible", "A canonical service ID is required."));
            if (!Declared(serviceId))
                return Task.FromResult(Refused<HomeServiceDescriptor>("PermissionDenied", "The installed package did not declare this service."));
            return _issuer.ReadAsync(_context, "9to1.Home.GetService", JsonSerializer.SerializeToElement(new { serviceId }),
                Array.AsReadOnly(new[] { new HomeObjectReference("home.service", serviceId) }),
                async (api, caller, ct) =>
                {
                    var result = await api.GetServiceAsync(caller, serviceId, ct).ConfigureAwait(false);
                    if (result.Value is { } service && (service.ServiceId != serviceId || !Declared(service.ServiceId)))
                        return new(false, "PermissionDenied", "The original service result is outside the installed declaration.", default, false);
                    return result;
                }, token);
        }
        public Task<HomeNativeCoreApiResult<HomeCompatibilityResult>> GetCompatibilityAsync(HomeCompatibilityRequest request,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.AppId != _context.Peer.AppId || !Text(request.AppVersion) || request.RequiredServices is null)
                return Task.FromResult(Refused<HomeCompatibilityResult>("CallerIdentityUnverified", "Compatibility must bind this original installed app."));
            var requirements = request.RequiredServices.Take(65).ToArray();
            if (requirements.Length > 64 || requirements.Any(item => item is null))
                return Task.FromResult(Refused<HomeCompatibilityResult>("HomeServiceIncompatible", "Service requirements must be explicit and bounded."));
            if (requirements.Any(item => !Text(item.ServiceId) || item.ServiceId != item.ServiceId.Trim() || !Declared(item.ServiceId)))
                return Task.FromResult(Refused<HomeCompatibilityResult>("PermissionDenied", "Compatibility may inspect only declared installed services."));
            var snapshot = request with { RequiredServices = Array.AsReadOnly(requirements) };
            return _issuer.ReadAsync(_context, "9to1.Home.GetCompatibility", JsonSerializer.SerializeToElement(snapshot),
                RegistryObject(), async (api, caller, ct) =>
                {
                    var value = await api.GetCompatibilityAsync(caller, snapshot, ct).ConfigureAwait(false);
                    return value.State == HomeCompatibilityState.PermissionDenied
                        ? new(false, "PermissionDenied", "Home denied the original compatibility read.", default, false)
                        : new HomeCoreOperationResult<HomeCompatibilityResult>(true, "Succeeded", "Home compatibility evaluated.",
                            value with { AcceptedServices = DeclaredServices(value.AcceptedServices) }, true, value.RegistryRevision);
                }, token);
        }
        private bool Declared(string serviceId) => _context.Peer.AllowedServiceIds.Contains(serviceId);
        private IReadOnlyList<HomeServiceDescriptor> DeclaredServices(IReadOnlyList<HomeServiceDescriptor> services) =>
            Array.AsReadOnly(services.Where(service => Declared(service.ServiceId)).ToArray());
        private IReadOnlyList<HomeObjectReference> RegistryObject() =>
            Array.AsReadOnly(new[] { new HomeObjectReference("home.service-registry", _context.Actor.ProfileId) });
        public ValueTask DisposeAsync()
        {
            lock (_closeGate)
            {
                if (_close is not null) return new(_close);
                var start = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseOriginalAsync(start.Task);
                // Publish this facade before sealing/cancelling its SAME context.
                try { start.SetResult(_context.CloseAsync().AsTask()); }
                catch (Exception error) { start.SetException(error); }
                return new(_close);
            }
        }
        private async Task CloseOriginalAsync(Task<Task> start)
        {
            var originalContextClose = await start.ConfigureAwait(false);
            // Keep issuer custody until the SAME original retirement task has settled.
            // Issuer shutdown racing this path must still await this context's read drain.
            try { await originalContextClose.ConfigureAwait(false); }
            finally { _issuer._sessions.TryRemove(_context.Id, out _); }
        }
    }
}
