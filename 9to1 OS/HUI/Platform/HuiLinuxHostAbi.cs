namespace CakeOS.Platform;

/// <summary>Versioned, platform-neutral ABI advertised by a CakeUI Linux root provider.</summary>
public sealed record HuiRootProviderAbi(string ContractId, int Version);

public static class HuiLinuxHostAbi
{
    public const string ContractId = "cakeos.hui.linux-root-provider";
    public const int CurrentVersion = 1;

    public static HuiRootProviderAbi Current { get; } = new(ContractId, CurrentVersion);

    public static void RequireCompatible(HuiRootProviderAbi abi)
    {
        ArgumentNullException.ThrowIfNull(abi);
        if (!string.Equals(abi.ContractId, ContractId, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported HUI root-provider ABI '{abi.ContractId}'.");
        if (abi.Version != CurrentVersion)
            throw new NotSupportedException($"HUI root-provider ABI version {abi.Version} is unsupported; host requires version {CurrentVersion}.");
    }
}

public enum HuiRootLifecycleState
{
    Uninitialized = 0,
    Loading = 1,
    Active = 2,
    Suspended = 3,
    Deactivating = 4,
    Unavailable = 5
}

public sealed record HuiThemeTokens(
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyDictionary<string, string> Fonts);

public sealed record HuiAccessibilityState(
    bool HighContrast,
    bool ReducedMotion,
    bool ScreenReaderActive,
    double ScaleFactor);

public interface IHuiRootProvider
{
    HuiRootProviderAbi Abi { get; }
    IRootElement CreateRoot(IServiceProvider services);
    Task<HuiRootLifecycleState> InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);
    Task ActivateAsync(CancellationToken cancellationToken = default);
    Task DeactivateAsync(CancellationToken cancellationToken = default);
    Task<HuiRootLifecycleState> GetStateAsync(CancellationToken cancellationToken = default);
    Task ApplyThemeTokensAsync(HuiThemeTokens tokens, CancellationToken cancellationToken = default);
    Task ApplyAccessibilityStateAsync(HuiAccessibilityState state, CancellationToken cancellationToken = default);
    Task<ProviderInjectionResult> InjectProvidersAsync(IReadOnlyCollection<ProviderDescriptor> providers, IReadOnlyCollection<ServiceDescriptor> services, CancellationToken cancellationToken = default);
}

public sealed record ProviderInjectionResult(
    bool Success,
    IReadOnlyCollection<string> InjectedProviders,
    IReadOnlyCollection<string> InjectedServices,
    IReadOnlyCollection<string> UnavailableProviders,
    IReadOnlyCollection<string> UnavailableServices);

public sealed record ServiceDescriptor(
    string ServiceId,
    string DisplayName,
    Type ServiceType,
    object? ImplementationInstance);

public sealed record HuiUnavailableResult(
    string Reason,
    bool Retryable,
    TimeSpan? SuggestedRetryDelay);

public static class HuiRootProviderResult
{
    public static HuiUnavailableResult Unavailable(string reason, bool retryable = true, TimeSpan? suggestedRetryDelay = null) =>
        new HuiUnavailableResult(reason, retryable, suggestedRetryDelay);
}