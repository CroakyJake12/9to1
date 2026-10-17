using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Discover;

/// <summary>Projects the same configured provider instances used by the model registry.
/// Catalogue reads grant no model invocation, download, or configuration authority.</summary>
public sealed class HomeRegisteredModelCatalogueSource(
    IModelProviderRegistry registry, string providerId, Func<bool> allowRemote)
    : IHomeDiscoverCatalogueSource
{
    public string ProviderId { get; } = providerId;

    public async Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = registry.Find(ProviderId)
            ?? throw new InvalidOperationException("The configured provider is no longer registered.");
        if (!provider.IsLocal && !allowRemote()) return Restricted(provider.DisplayName);

        // Read this registered provider directly so a failed query cannot be mistaken for
        // the compatibility registry's empty/partial catalogue result.
        var observed = await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(registry.Find(ProviderId), provider))
            throw new InvalidOperationException("The configured provider changed during discovery.");
        var remoteAllowed = allowRemote();
        if (!provider.IsLocal && !remoteAllowed) return Restricted(provider.DisplayName);

        var models = observed.Where(model => model.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase))
            .Where(model => provider.IsLocal && model.IsLocal || remoteAllowed)
            .Select(model => Project(model, provider.IsLocal))
            .GroupBy(model => model.Identity.StableKey, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(model => model.Identity.StableKey, StringComparer.Ordinal).ToArray();
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(models)));
        return new(ProviderId, provider.DisplayName, HomeDiscoverConnectionState.Connected,
            models.Length == 0 ? HomeDiscoverSourceState.Empty : HomeDiscoverSourceState.Available, revision, models);
    }

    public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Installation and route mutation remain with the owning model manager/permission
        // broker. CanManageModels alone is not an approval to download or delete anything.
        return Task.FromResult(new HomeDiscoverActionResult(request.IdempotencyKey, request.Identity, request.Action,
            false, "Discover.ActionUnsupported", "Manage this model through the shared model settings.", false, true));
    }

    private HomeDiscoverProviderSnapshot Restricted(string displayName) =>
        new(ProviderId, displayName, HomeDiscoverConnectionState.Unavailable, HomeDiscoverSourceState.Unavailable,
            "privacy-restricted", [], "Discover.LocalOnly", "Cloud catalogue access is disabled by the current local-only preference.");

    private HomeDiscoverModel Project(ProviderModelDescriptor model, bool providerLocal)
    {
        var categories = new HashSet<HomeDiscoverCategory>();
        if (model.Supports(ToolCapability.Text)) categories.Add(HomeDiscoverCategory.Chat);
        if (model.Supports(ToolCapability.AudioInput) || model.Supports(ToolCapability.AudioOutput))
            categories.Add(HomeDiscoverCategory.Audio);
        // Vision means image understanding, not generation. Audio I/O alone does not
        // establish conversational/live Voice compatibility or measured voice quality.
        return new(new(ProviderId, model.Name), model.Name, null, categories,
            model.Capabilities.Select(value => value.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Known(model.Model.Family), Known(model.Model.ParameterSize), model.ContextWindow,
            Known(model.Model.Quantization), providerLocal && model.IsLocal ? HomeDiscoverLocality.Local : HomeDiscoverLocality.Cloud,
            HomeDiscoverInstallState.Unknown, HomeDiscoverConnectionState.Connected,
            null, null, null, null, null, null, new HashSet<HomeDiscoverAction>(), model.Label);
    }

    private static string? Known(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
