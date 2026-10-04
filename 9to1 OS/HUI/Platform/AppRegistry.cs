namespace CakeOS.Platform;

/// <summary>Stable identity for the one shared application registry product.</summary>
public static class AppRegistryContract
{
    public const string RegistryId = "cakeos.shared-app-registry";
    public const int RegistrySchemaVersion = 1;

    public static RegistryIdentity Identity { get; } = new(RegistryId, RegistrySchemaVersion);
}

public sealed record RegistryIdentity(string RegistryId, int RegistrySchemaVersion)
{
    public void Validate()
    {
        PlatformContractValidation.RequireIdentifier(RegistryId, nameof(RegistryId));
        if (RegistrySchemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(RegistrySchemaVersion), "Registry schema version must be positive.");
    }
}

public enum ProductType
{
    App = 0,
    SystemSurface = 1,
    BackgroundComponent = 2,
    Provider = 3,
    Service = 4
}

public enum LaunchAvailability
{
    Always = 0,
    OnDemand = 1,
    ManualOnly = 2,
    Disabled = 3
}

public sealed record ProductCapabilities(
    bool HasWindow,
    bool HasBackgroundExecution,
    bool HasSystemIntegration,
    bool RequiresElevatedPrivileges,
    IReadOnlyCollection<string> DeclaredCapabilities);

public sealed record ProductPersistence(
    bool RequiresSettings,
    bool RequiresCache,
    bool RequiresState,
    bool RequiresData);

public sealed record ProductPermissions(
    IReadOnlyCollection<string> RequiredGrants,
    IReadOnlyCollection<string> OptionalGrants);

public sealed record ProductDependencies(
    IReadOnlyCollection<string> RequiredProducts,
    IReadOnlyCollection<string> OptionalProducts,
    IReadOnlyCollection<string> RequiredProviders);

/// <summary>Metadata registered by a product; the platform does not hardcode product identities.</summary>
public sealed record ProductRegistration(
    string ProductId,
    string DisplayName,
    ProductType ProductType,
    string Route,
    Func<IServiceProvider, IRootElement?> RootFactory,
    string Entrypoint,
    ProductCapabilities Capabilities,
    ProductDependencies Dependencies,
    ProductPersistence Persistence,
    ProductPermissions Permissions,
    IReadOnlyCollection<string> OptionalProviders,
    IReadOnlyCollection<AppLifecycleOperation> LifecycleOperations,
    LaunchAvailability LaunchAvailability)
{
    public void Validate()
    {
        PlatformContractValidation.RequireIdentifier(ProductId, nameof(ProductId));
        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("Display name is required.", nameof(DisplayName));
        if (string.IsNullOrWhiteSpace(Route) || !Route.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Product routes must be non-empty absolute routes.", nameof(Route));
        if (RootFactory is null)
            throw new ArgumentNullException(nameof(RootFactory));
        if (string.IsNullOrWhiteSpace(Entrypoint))
            throw new ArgumentException("Entrypoint is required.", nameof(Entrypoint));
        if (Capabilities is null)
            throw new ArgumentNullException(nameof(Capabilities));
        if (Dependencies is null)
            throw new ArgumentNullException(nameof(Dependencies));
        if (Persistence is null)
            throw new ArgumentNullException(nameof(Persistence));
        if (Permissions is null)
            throw new ArgumentNullException(nameof(Permissions));
        if (OptionalProviders is null)
            throw new ArgumentNullException(nameof(OptionalProviders));
        if (LifecycleOperations is null || LifecycleOperations.Count == 0)
            throw new ArgumentException("Product registrations must declare at least one lifecycle operation.", nameof(LifecycleOperations));
        if (LifecycleOperations.Distinct().Count() != LifecycleOperations.Count)
            throw new ArgumentException("Product lifecycle operations must be distinct.", nameof(LifecycleOperations));
    }
}

public sealed record ProductRegistryChangedEventArgs(ProductRegistration Registration, bool Registered);

public interface IProductRegistry
{
    RegistryIdentity Identity { get; }
    event EventHandler<ProductRegistryChangedEventArgs>? Changed;
    IReadOnlyList<ProductRegistration> GetAll();
    bool TryGet(string productId, out ProductRegistration? registration);
    void Register(ProductRegistration registration);
    bool Unregister(string productId);
}

/// <summary>In-memory runtime registry. Product/release metadata references its identity but never owns enumeration.</summary>
public sealed class ProductRegistry : IProductRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProductRegistration> _registrations = new(StringComparer.Ordinal);

    public RegistryIdentity Identity => AppRegistryContract.Identity;

    public event EventHandler<ProductRegistryChangedEventArgs>? Changed;

    public IReadOnlyList<ProductRegistration> GetAll()
    {
        lock (_gate)
        {
            return _registrations.Values.OrderBy(registration => registration.ProductId, StringComparer.Ordinal).ToArray();
        }
    }

    public bool TryGet(string productId, out ProductRegistration? registration)
    {
        lock (_gate)
        {
            return _registrations.TryGetValue(productId, out registration);
        }
    }

    public void Register(ProductRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();
        lock (_gate)
        {
            if (!_registrations.TryAdd(registration.ProductId, registration))
                throw new InvalidOperationException($"Product '{registration.ProductId}' is already registered.");
        }
        Changed?.Invoke(this, new ProductRegistryChangedEventArgs(registration, Registered: true));
    }

    public bool Unregister(string productId)
    {
        PlatformContractValidation.RequireIdentifier(productId, nameof(productId));
        ProductRegistration? registration;
        lock (_gate)
        {
            if (!_registrations.Remove(productId, out registration))
                return false;
        }
        Changed?.Invoke(this, new ProductRegistryChangedEventArgs(registration, Registered: false));
        return true;
    }
}

public enum RouteResolutionKind
{
    Resolved = 0,
    UnknownProduct = 1,
    IncompatibleRegistry = 2,
    CapabilityDenied = 3,
    RootCreationFailed = 4
}

public sealed record ProductRouteRequest(RegistryIdentity Registry, string ProductId, IServiceProvider Services);

public sealed record RouteResolution(RouteResolutionKind Kind, ProductRegistration? Registration, IRootElement? Root, string? Reason)
{
    public static RouteResolution UnknownProduct { get; } = new(RouteResolutionKind.UnknownProduct, null, null, "Product not found in registry.");
    public static RouteResolution IncompatibleRegistry { get; } = new(RouteResolutionKind.IncompatibleRegistry, null, null, "Registry identity mismatch.");
    
    public static RouteResolution CapabilityDenied(string reason) => new(RouteResolutionKind.CapabilityDenied, null, null, reason);
    public static RouteResolution RootCreationFailed(string reason) => new(RouteResolutionKind.RootCreationFailed, null, null, reason);
}

public interface IProductRouter
{
    Task<RouteResolution> ResolveAsync(ProductRouteRequest request, IPermissionService permissions, CancellationToken cancellationToken = default);
}

/// <summary>Routes registered product identities, checks capabilities via permission service, creates root via factory.</summary>
public sealed class RegistryBackedRouter(IProductRegistry registry) : IProductRouter
{
    private readonly IProductRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public async Task<RouteResolution> ResolveAsync(ProductRouteRequest request, IPermissionService permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Registry.Validate();
        PlatformContractValidation.RequireIdentifier(request.ProductId, nameof(request.ProductId));
        ArgumentNullException.ThrowIfNull(request.Services);
        
        if (request.Registry != _registry.Identity)
            return RouteResolution.IncompatibleRegistry;

        if (!_registry.TryGet(request.ProductId, out var registration) || registration is null)
            return RouteResolution.UnknownProduct;

        var capabilityRequest = new PermissionRequest(
            $"product.{request.ProductId}",
            "launch",
            "execute",
            PermissionRisk.Consequential);

        var decision = await permissions.EvaluateAsync(capabilityRequest, cancellationToken).ConfigureAwait(false);
        if (decision.Kind == PermissionDecisionKind.Ask)
            return RouteResolution.CapabilityDenied(decision.Reason);

        IRootElement? root;
        try
        {
            root = registration.RootFactory(request.Services);
        }
        catch (Exception ex)
        {
            return RouteResolution.RootCreationFailed(ex.Message);
        }

        if (root is null)
            return RouteResolution.RootCreationFailed("Root factory returned null.");

        return new RouteResolution(RouteResolutionKind.Resolved, registration, root, "Resolved successfully.");
    }
}

public enum AppLifecycleOperation
{
    Create = 0,
    Activate = 1,
    Open = 2,
    Suspend = 3,
    Resume = 4,
    RequestClose = 5,
    Recover = 6
}

internal static class PlatformContractValidation
{
    public static void RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':')))
        {
            throw new ArgumentException("Identifiers may contain ASCII letters, digits, dot, dash, underscore, and colon.", parameterName);
        }
    }
}