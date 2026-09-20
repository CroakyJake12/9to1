using System.Runtime.Loader;
using CakeOS.Platform;
using Haven.UI.Components;

namespace CakeOS.HuiWindowsHost;

/// <summary>
/// Supplies an application-owned, platform-neutral HUI root to the Windows host.
/// </summary>
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

public static class HuiRootProviderResolver
{
    public const string AssemblyOption = "--hui-root-provider-assembly";
    public const string TypeOption = "--hui-root-provider-type";

    public static IHuiRootProvider? Resolve(string[] args, out string[] hostArgs)
    {
        string? assemblyPath = null;
        string? typeName = null;
        var remaining = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is AssemblyOption or TypeOption)
            {
                if (++index == args.Length)
                    throw new ArgumentException($"{arg} requires a value.");

                if (arg == AssemblyOption)
                {
                    if (assemblyPath is not null)
                        throw new ArgumentException($"{AssemblyOption} may only be specified once.");
                    assemblyPath = args[index];
                }
                else
                {
                    if (typeName is not null)
                        throw new ArgumentException($"{TypeOption} may only be specified once.");
                    typeName = args[index];
                }
            }
            else
            {
                remaining.Add(arg);
            }
        }

        hostArgs = remaining.ToArray();
        if (assemblyPath is null && typeName is null)
            return null;
        if (assemblyPath is null || typeName is null)
            throw new ArgumentException($"{AssemblyOption} and {TypeOption} must be specified together.");
        if (!Path.IsPathFullyQualified(assemblyPath))
            throw new ArgumentException($"{AssemblyOption} must be an absolute path.");

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var providerType = assembly.GetType(typeName, throwOnError: true, ignoreCase: false)
            ?? throw new InvalidOperationException($"Provider type '{typeName}' was not found.");
        if (!typeof(IHuiRootProvider).IsAssignableFrom(providerType))
            throw new ArgumentException($"Provider type '{typeName}' must implement {nameof(IHuiRootProvider)}.");

        var provider = Activator.CreateInstance(providerType) as IHuiRootProvider
            ?? throw new InvalidOperationException($"Provider type '{typeName}' must have a public parameterless constructor.");
        HuiWindowsHostAbi.RequireCompatible(provider.Abi);
        return provider;
    }
}