using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text.Json;

namespace CakeOS.ModelPicker;

/// <summary>
/// Universal model picker consuming ProviderRegistry and ModelGovernance.
/// Provides a platform-neutral model selection API.
/// </summary>
public sealed class ModelPickerService(
    IProviderRegistry providerRegistry,
    IModelGovernance modelGovernance,
    IPermissionService permissions,
    ILogger<ModelPickerService> logger)
{
    /// <summary>
    /// Gets all available providers for model selection.
    /// </summary>
    public IReadOnlyList<ProviderDescriptor> GetAvailableProviders()
    {
        return providerRegistry.GetAll();
    }

    /// <summary>
    /// Gets a provider by ID.
    /// </summary>
    public ProviderDescriptor? GetProvider(string providerId)
    {
        providerRegistry.TryGet(providerId, out var descriptor);
        return descriptor;
    }

    /// <summary>
    /// Gets the current model fallback order.
    /// </summary>
    public async ValueTask<IReadOnlyList<string>> GetFallbackOrderAsync(CancellationToken cancellationToken = default)
    {
        return await modelGovernance.GetFallbackOrderAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the model fallback order (used for unqualified model resolution).
    /// </summary>
    public async ValueTask SetFallbackOrderAsync(IReadOnlyCollection<string> providerIds, CancellationToken cancellationToken = default)
    {
        await modelGovernance.SetFallbackOrderAsync(providerIds, cancellationToken);
    }

    /// <summary>
    /// Resolves an unqualified model key to qualified model keys based on fallback order.
    /// </summary>
    public async ValueTask<IReadOnlyList<ResolvedModelKey>> ResolveModelKeyAsync(string unqualifiedModelKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unqualifiedModelKey))
            return ImmutableList<ResolvedModelKey>.Empty;

        var candidates = await modelGovernance.ResolveModelKeyAsync(unqualifiedModelKey, cancellationToken);
        
        var resolved = candidates
            .Select(key => new ResolvedModelKey(key, ParseProviderFromKey(key), unqualifiedModelKey))
            .ToImmutableList();

        logger.LogDebug("Resolved '{UnqualifiedKey}' to {Count} candidates", unqualifiedModelKey, resolved.Count);
        return resolved;
    }

    /// <summary>
    /// Checks if a model use request is permitted.
    /// </summary>
    public async ValueTask<ModelUseResult> CheckModelUseAsync(
        string providerId,
        string modelKey,
        string capability,
        PermissionRisk risk = PermissionRisk.Consequential,
        CancellationToken cancellationToken = default)
    {
        var request = new ModelUseRequest(providerId, modelKey, capability, risk);
        var decision = await modelGovernance.EvaluateModelUseAsync(request, cancellationToken);

        return new ModelUseResult(
            decision.Kind == PermissionDecisionKind.Allowed,
            providerId,
            modelKey,
            capability,
            decision.Reason);
    }

    /// <summary>
    /// Grants permission for a model use.
    /// </summary>
    public async ValueTask GrantModelUseAsync(
        string providerId,
        string modelKey,
        string capability,
        GrantSource source = GrantSource.User,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        await permissions.GrantAsync($"provider.{providerId}", $"model.{modelKey}", capability, source, expiresAtUtc, cancellationToken);
    }

    /// <summary>
    /// Revokes permission for a model use.
    /// </summary>
    public async ValueTask RevokeModelUseAsync(
        string providerId,
        string modelKey,
        string capability,
        CancellationToken cancellationToken = default)
    {
        await permissions.RevokeAsync($"provider.{providerId}", $"model.{modelKey}", capability, cancellationToken);
    }

    /// <summary>
    /// Gets all available model keys from registered providers.
    /// </summary>
    public IReadOnlyList<ModelKeyInfo> GetAvailableModelKeys()
    {
        var allKeys = new List<ModelKeyInfo>();

        foreach (var provider in providerRegistry.GetAll())
        {
            foreach (var kvp in provider.ModelKeys)
            {
                if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    var format = kvp.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(format))
                    {
                        allKeys.Add(new ModelKeyInfo(
                            provider.ProviderId,
                            provider.DisplayName,
                            kvp.Key,
                            format,
                            provider.Kind));
                    }
                }
            }
        }

        return allKeys.ToImmutableList();
    }

    /// <summary>
    /// Gets available models from a specific provider's status endpoint format.
    /// Note: This only returns the model key format; actual model listing requires provider communication.
    /// </summary>
    public ModelKeyInfo? GetProviderModelFormat(string providerId, string formatKey = "qualified")
    {
        if (!providerRegistry.TryGet(providerId, out var provider))
            return null;

        if (provider.ModelKeys.TryGetValue(formatKey, out var formatElement) && formatElement.ValueKind == JsonValueKind.String)
        {
            var format = formatElement.GetString();
            if (format is null)
                return null;
            
            return new ModelKeyInfo(
                provider.ProviderId,
                provider.DisplayName,
                formatKey,
                format,
                provider.Kind);
        }

        return null;
    }

    private static string? ParseProviderFromKey(string key)
    {
        var colonIndex = key.IndexOf(':');
        if (colonIndex > 0)
            return key[..colonIndex];
        return null;
    }
}

/// <summary>
/// Extension methods for dependency injection registration.
/// </summary>
public static class ModelPickerServiceExtensions
{
    public static IServiceCollection AddModelPicker(this IServiceCollection services)
    {
        return services.AddScoped<ModelPickerService>();
    }
}

/// <summary>
/// Result of resolving an unqualified model key.
/// </summary>
public sealed record ResolvedModelKey(
    string QualifiedKey,
    string? ProviderId,
    string UnqualifiedKey);

/// <summary>
/// Result of a model use permission check.
/// </summary>
public sealed record ModelUseResult(
    bool Allowed,
    string ProviderId,
    string ModelKey,
    string Capability,
    string Reason);

/// <summary>
/// Information about a model key format from a provider.
/// </summary>
public sealed record ModelKeyInfo(
    string ProviderId,
    string ProviderDisplayName,
    string FormatKey,
    string Format,
    ProviderKind ProviderKind);