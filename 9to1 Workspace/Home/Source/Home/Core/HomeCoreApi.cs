namespace HavenOS.Home.Core;

/// <summary>Authenticated public Home service-discovery and startup-compatibility API.</summary>
public sealed class HomeCoreApi(HomeCoreRuntime runtime, IHomeCoreAuthorization authorization) : IHomeCoreApi
{
    private readonly HomeCoreRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly IHomeCoreAuthorization _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

    public Task<HomeCoreOperationResult<HomeCoreStateSnapshot>> GetStateAsync(HomeCallerIdentity caller,
        CancellationToken cancellationToken = default) =>
        AuthorizeAsync(caller, "9to1.Home.GetState", "home.services.read", _runtime.Current, cancellationToken);

    public async Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(
        HomeCallerIdentity caller, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeAsync(caller, "9to1.Home.GetServices", "home.services.read",
            _runtime.Current.Services, cancellationToken).ConfigureAwait(false);
        return authorized;
    }

    public async Task<HomeCoreOperationResult<HomeServiceDescriptor>> GetServiceAsync(HomeCallerIdentity caller,
        string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return new(false, "HomeServiceIncompatible", "A stable service ID is required.", default, false);
        var authorized = await AuthorizeAsync(caller, "9to1.Home.GetService", "home.services.read",
            _runtime.Current.Services.FirstOrDefault(service => service.ServiceId == serviceId), cancellationToken)
            .ConfigureAwait(false);
        if (!authorized.Succeeded)
            return new(false, authorized.Code, authorized.Message, default, authorized.Recoverable, authorized.Revision);
        if (authorized.Value is { } service)
            return new(true, authorized.Code, authorized.Message, service, authorized.Recoverable, authorized.Revision);
        return new(false, "HomeServiceUnavailable", $"Home service '{serviceId}' is not registered.", default, true);
    }

    public Task<HomeCompatibilityResult> GetCompatibilityAsync(HomeCallerIdentity caller,
        HomeCompatibilityRequest request, CancellationToken cancellationToken = default) =>
        _runtime.GetCompatibilityAsync(caller, request, cancellationToken);

    private async Task<HomeCoreOperationResult<T>> AuthorizeAsync<T>(HomeCallerIdentity caller, string target,
        string scope, T value, CancellationToken cancellationToken)
    {
        if (caller is null || string.IsNullOrWhiteSpace(caller.StableId) || string.IsNullOrWhiteSpace(caller.Origin) ||
            string.IsNullOrWhiteSpace(caller.VerificationMethod))
            return new(false, "CallerIdentityUnverified",
                "The caller identity was not established by a trusted Home platform authenticator.", default, false);

        bool allowed;
        try
        {
            allowed = await _authorization.IsAllowedAsync(caller, target,
                new HashSet<string>(StringComparer.Ordinal) { scope }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            allowed = false;
        }

        return allowed
            ? new HomeCoreOperationResult<T>(true, "Succeeded", "Home service state returned.", value,
                true, _runtime.Current.Revision)
            : new HomeCoreOperationResult<T>(false, "PermissionDenied",
                "Home did not authorize this caller for the requested service operation.", default, false);
    }
}
