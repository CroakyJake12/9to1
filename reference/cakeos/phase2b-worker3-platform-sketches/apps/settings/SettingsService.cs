using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text.Json;

namespace CakeOS.Settings;

/// <summary>
/// Settings service consuming VersionedSettingsStore, Permissions, and ProviderRegistry/ModelGovernance.
/// Provides a unified settings API for platform and application settings.
/// </summary>
public sealed class SettingsService(
    VersionedSettingsStore settingsStore,
    IPermissionService permissions,
    IProviderRegistry providerRegistry,
    IModelGovernance modelGovernance,
    ILogger<SettingsService> logger)
{
    public const string PlatformSettingsPrefix = "platform.";
    public const string UserSettingsPrefix = "user.";

    /// <summary>
    /// Gets a platform setting.
    /// </summary>
    public async ValueTask<T?> GetPlatformSettingAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        return await settingsStore.GetAsync<T>($"{PlatformSettingsPrefix}{key}", cancellationToken);
    }

    /// <summary>
    /// Sets a platform setting.
    /// </summary>
    public async ValueTask SetPlatformSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        await settingsStore.SetAsync($"{PlatformSettingsPrefix}{key}", value, cancellationToken);
    }

    /// <summary>
    /// Gets a user setting.
    /// </summary>
    public async ValueTask<T?> GetUserSettingAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        return await settingsStore.GetAsync<T>($"{UserSettingsPrefix}{key}", cancellationToken);
    }

    /// <summary>
    /// Sets a user setting.
    /// </summary>
    public async ValueTask SetUserSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        await settingsStore.SetAsync($"{UserSettingsPrefix}{key}", value, cancellationToken);
    }

    /// <summary>
    /// Removes a setting.
    /// </summary>
    public async ValueTask RemoveSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await settingsStore.RemoveAsync(key, cancellationToken);
    }

    /// <summary>
    /// Gets all platform settings.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, object>> GetAllPlatformSettingsAsync(CancellationToken cancellationToken = default)
    {
        var allSettings = await settingsStore.GetAsync<Dictionary<string, JsonElement>>(string.Empty, cancellationToken) ?? new();
        return allSettings
            .Where(kvp => kvp.Key.StartsWith(PlatformSettingsPrefix, StringComparison.Ordinal))
            .ToImmutableDictionary(
                kvp => kvp.Key[PlatformSettingsPrefix.Length..],
                kvp => (object)kvp.Value);
    }

    /// <summary>
    /// Gets all user settings.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, object>> GetAllUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        var allSettings = await settingsStore.GetAsync<Dictionary<string, JsonElement>>(string.Empty, cancellationToken) ?? new();
        return allSettings
            .Where(kvp => kvp.Key.StartsWith(UserSettingsPrefix, StringComparison.Ordinal))
            .ToImmutableDictionary(
                kvp => kvp.Key[UserSettingsPrefix.Length..],
                kvp => (object)kvp.Value);
    }

    /// <summary>
    /// Gets the current permission policy.
    /// </summary>
    public async ValueTask<PermissionPolicy> GetPermissionPolicyAsync(CancellationToken cancellationToken = default)
    {
        return await permissions.GetPolicyAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the permission policy.
    /// </summary>
    public async ValueTask SetPermissionPolicyAsync(PermissionPolicy policy, CancellationToken cancellationToken = default)
    {
        await permissions.SetPolicyAsync(policy, cancellationToken);
    }

    /// <summary>
    /// Gets all permission grants.
    /// </summary>
    public async ValueTask<IReadOnlyList<PermissionGrant>> GetPermissionGrantsAsync(CancellationToken cancellationToken = default)
    {
        return await permissions.GetGrantsAsync(cancellationToken);
    }

    /// <summary>
    /// Grants a permission.
    /// </summary>
    public async ValueTask GrantPermissionAsync(
        string subjectId,
        string resource,
        string action,
        GrantSource source = GrantSource.User,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        await permissions.GrantAsync(subjectId, resource, action, source, expiresAtUtc, cancellationToken);
    }

    /// <summary>
    /// Revokes a permission.
    /// </summary>
    public async ValueTask RevokePermissionAsync(
        string subjectId,
        string resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        await permissions.RevokeAsync(subjectId, resource, action, cancellationToken);
    }

    /// <summary>
    /// Gets all registered providers.
    /// </summary>
    public IReadOnlyList<ProviderDescriptor> GetProviders()
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
    /// Registers a provider descriptor from file.
    /// </summary>
    public async ValueTask<ProviderDescriptor> RegisterProviderAsync(string descriptorPath, CancellationToken cancellationToken = default)
    {
        return await providerRegistry.RegisterDescriptorAsync(descriptorPath, cancellationToken);
    }

    /// <summary>
    /// Unregisters a provider.
    /// </summary>
    public async ValueTask<bool> UnregisterProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        return await providerRegistry.UnregisterAsync(providerId, cancellationToken);
    }

    /// <summary>
    /// Gets the model fallback order.
    /// </summary>
    public async ValueTask<IReadOnlyList<string>> GetModelFallbackOrderAsync(CancellationToken cancellationToken = default)
    {
        return await modelGovernance.GetFallbackOrderAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the model fallback order.
    /// </summary>
    public async ValueTask SetModelFallbackOrderAsync(IReadOnlyCollection<string> providerIds, CancellationToken cancellationToken = default)
    {
        await modelGovernance.SetFallbackOrderAsync(providerIds, cancellationToken);
    }

    /// <summary>
    /// Resolves an unqualified model key to qualified candidates.
    /// </summary>
    public async ValueTask<IReadOnlyList<string>> ResolveModelKeyAsync(string unqualifiedModelKey, CancellationToken cancellationToken = default)
    {
        return await modelGovernance.ResolveModelKeyAsync(unqualifiedModelKey, cancellationToken);
    }

    /// <summary>
    /// Evaluates a model use request.
    /// </summary>
    public async ValueTask<PermissionDecision> EvaluateModelUseAsync(ModelUseRequest request, CancellationToken cancellationToken = default)
    {
        return await modelGovernance.EvaluateModelUseAsync(request, cancellationToken);
    }

    /// <summary>
    /// Creates a backup of all settings.
    /// </summary>
    public async ValueTask<BackupResult> CreateBackupAsync(string backupName, CancellationToken cancellationToken = default)
    {
        return await settingsStore.CreateBackupAsync(backupName, cancellationToken);
    }

    /// <summary>
    /// Restores settings from a backup.
    /// </summary>
    public async ValueTask<RestoreResult> RestoreBackupAsync(string backupFile, CancellationToken cancellationToken = default)
    {
        return await settingsStore.RestoreBackupAsync(backupFile, cancellationToken);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160 ||
            key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
        {
            throw new ArgumentException("Settings keys may contain ASCII letters, digits, dot, dash, and underscore.", nameof(key));
        }
    }
}

/// <summary>
/// Extension methods for dependency injection registration.
/// </summary>
public static class SettingsServiceExtensions
{
    public static IServiceCollection AddSettingsService(this IServiceCollection services)
    {
        return services.AddScoped<SettingsService>();
    }
}