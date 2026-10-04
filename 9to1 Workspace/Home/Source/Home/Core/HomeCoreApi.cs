using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Authenticated public Home service-discovery and startup-compatibility API.</summary>
public sealed class HomeCoreApi : IHomeCoreApi
{
    private readonly HomeCoreRuntime _runtime;
    private readonly IHomeCoreAuthorization _authorization;
    private readonly IAuthenticatedResourceActorSource? _actors;

    public HomeCoreApi(HomeCoreRuntime runtime, IHomeCoreAuthorization authorization)
        : this(runtime, authorization, null) { }

    /// <summary>The trusted composition may additionally fence reads to its original current profile.</summary>
    public HomeCoreApi(HomeCoreRuntime runtime, IHomeCoreAuthorization authorization,
        IAuthenticatedResourceActorSource? actors)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _actors = actors;
    }

    public Task<HomeCoreOperationResult<HomeCoreStateSnapshot>> GetStateAsync(HomeCallerIdentity caller,
        CancellationToken cancellationToken = default) =>
        AuthorizeAsync(caller, "9to1.Home.GetState", "home.services.read", snapshot => snapshot, cancellationToken);

    public Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(
        HomeCallerIdentity caller, CancellationToken cancellationToken = default) =>
        AuthorizeAsync(caller, "9to1.Home.GetServices", "home.services.read", snapshot => snapshot.Services,
            cancellationToken);

    public async Task<HomeCoreOperationResult<HomeServiceDescriptor>> GetServiceAsync(HomeCallerIdentity caller,
        string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return new(false, "HomeServiceIncompatible", "A stable service ID is required.", default, false);
        var authorized = await AuthorizeAsync(caller, "9to1.Home.GetService", "home.services.read",
            snapshot => snapshot.Services.FirstOrDefault(service => service.ServiceId == serviceId), cancellationToken)
            .ConfigureAwait(false);
        if (!authorized.Succeeded)
            return new(false, authorized.Code, authorized.Message, default, authorized.Recoverable, authorized.Revision);
        if (authorized.Value is { } service)
            return new(true, authorized.Code, authorized.Message, service, authorized.Recoverable, authorized.Revision);
        return new(false, "HomeServiceUnavailable", $"Home service '{serviceId}' is not registered.", default, true,
            authorized.Revision);
    }

    public Task<HomeCompatibilityResult> GetCompatibilityAsync(HomeCallerIdentity caller,
        HomeCompatibilityRequest request, CancellationToken cancellationToken = default) =>
        _runtime.GetCompatibilityAsync(caller, request, cancellationToken);

    private async Task<HomeCoreOperationResult<T>> AuthorizeAsync<T>(HomeCallerIdentity caller, string target,
        string scope, Func<HomeCoreStateSnapshot, T> select, CancellationToken cancellationToken)
    {
        if (caller is null || string.IsNullOrWhiteSpace(caller.StableId) || string.IsNullOrWhiteSpace(caller.Origin) ||
            string.IsNullOrWhiteSpace(caller.VerificationMethod))
            return new(false, "CallerIdentityUnverified",
                "The caller identity was not established by a trusted Home platform authenticator.", default, false);

        var snapshot = _runtime.Current;
        AuthenticatedResourceActor? originalActor = null;
        bool allowed;
        try
        {
            if (_actors is not null)
            {
                originalActor = await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                if (originalActor is null)
                    return new(false, "CallerIdentityUnverified", "Home could not verify its current profile.", default, false);
            }
            allowed = await _authorization.IsAllowedAsync(caller, target,
                new HashSet<string>(StringComparer.Ordinal) { scope }, cancellationToken).ConfigureAwait(false);
            if (allowed && _actors is not null)
                allowed = originalActor == await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            allowed = false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!allowed)
            return new(false, "PermissionDenied",
                "Home did not authorize this caller for the requested service operation.", default, false);

        var current = _runtime.Current;
        if (current.Revision != snapshot.Revision)
            return new(false, "HomeStateConflict", "Home service state changed during authorization. Retry the read.",
                default, true, current.Revision);

        return new(true, "Succeeded", "Home service state returned.", select(snapshot), true, snapshot.Revision);
    }
}
