using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Home.Discover;

/// <summary>Projects the same configured provider instances used by the model registry.
/// Catalogue reads grant no model invocation, download, or configuration authority.</summary>
public sealed class HomeRegisteredModelCatalogueSource(
    IModelProviderRegistry registry, string providerId, Func<bool> allowRemote,
    IHomeDulcheModelEvidenceSource? dulcheModelEvidence = null)
    : IHomeDiscoverCatalogueSource
{
    private const string DulcheEvidenceProviderId = "llamacpp";
    private const string DulcheRegistryProviderId = "llama-cpp";

    public string ProviderId { get; } = providerId;

    public async Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = registry.Find(ProviderId)
            ?? throw new InvalidOperationException("The configured provider is no longer registered.");
        if (!provider.IsLocal && !allowRemote()) return Restricted(provider.DisplayName);

        // The packaged Dulche store proves installation, not that its engine is running or
        // capable. Read that evidence separately so an installed model remains discoverable
        // when the live worker catalogue is empty or unavailable.
        var useDulcheEvidence = dulcheModelEvidence is not null && IsDulcheProvider(provider);
        HomeDulcheModelEvidence? installed = null;
        Exception? evidenceFailure = null;
        if (useDulcheEvidence)
        {
            try
            {
                installed = await dulcheModelEvidence!.FindEligibleAsync(cancellationToken).ConfigureAwait(false);
                if (installed is not null && (!installed.IsVerifiedArtifact ||
                    !installed.ProviderId.Equals(DulcheEvidenceProviderId, StringComparison.OrdinalIgnoreCase)))
                    installed = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                evidenceFailure = exception;
            }
        }

        // Read this registered provider directly so a failed query cannot be mistaken for
        // the compatibility registry's empty/partial catalogue result. A verified packaged
        // model can still be returned below with its unavailable runtime state.
        IReadOnlyList<ProviderModelDescriptor> observed;
        Exception? providerFailure = null;
        try
        {
            observed = await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (useDulcheEvidence && installed is not null)
        {
            observed = [];
            providerFailure = exception;
        }
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
        var installedModelIsLive = false;
        if (installed is not null)
        {
            var installedModel = models.FirstOrDefault(model =>
                model.Identity.ModelId.Equals(installed.ModelId, StringComparison.Ordinal));
            if (installedModel is not null)
            {
                installedModelIsLive = true;
                models = models.Select(model => ReferenceEquals(model, installedModel)
                    ? model with
                    {
                        Identity = model.Identity with { ArtifactRevision = installed.ArtifactSha256 },
                        InstallState = HomeDiscoverInstallState.Installed
                    }
                    : model).ToArray();
            }
            else
            {
                models = models.Append(ProjectInstalledButUnavailable(installed)).ToArray();
            }
        }
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(models)));
        if (providerFailure is not null || evidenceFailure is not null)
        {
            // Keep the installed model visible, but do not conceal that its live engine
            // observation failed. Diagnostic text is bounded because provider exceptions may
            // contain arbitrary response details.
            var failure = providerFailure ?? evidenceFailure!;
            return new(ProviderId, provider.DisplayName, HomeDiscoverConnectionState.Unavailable,
                HomeDiscoverSourceState.Partial, revision, models, "Discover.ProviderUnavailable",
                Diagnostic(failure), Retryable: true);
        }
        if (installed is not null && !installedModelIsLive)
        {
            return new(ProviderId, provider.DisplayName,
                observed.Count > 0 ? HomeDiscoverConnectionState.Connected : HomeDiscoverConnectionState.Unavailable,
                HomeDiscoverSourceState.Partial, revision, models, "Discover.ModelNotObserved",
                "The verified Dulche model is installed, but no matching live engine model was observed.", Retryable: true);
        }
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

    private static bool IsDulcheProvider(IModelProvider provider) =>
        provider.Id.Equals(DulcheRegistryProviderId, StringComparison.Ordinal) && provider.IsLocal &&
        provider.Kind == ModelProviderKind.OpenAICompatible;

    private static HomeDiscoverModel ProjectInstalledButUnavailable(HomeDulcheModelEvidence evidence) =>
        new(new(DulcheRegistryProviderId, evidence.ModelId, evidence.ArtifactSha256), evidence.ModelId,
            null, new HashSet<HomeDiscoverCategory>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            null, null, null, null, HomeDiscoverLocality.Local, HomeDiscoverInstallState.Installed,
            HomeDiscoverConnectionState.Unavailable, null, null, null, null, null, null,
            new HashSet<HomeDiscoverAction>(), evidence.ModelId);

    private static string Diagnostic(Exception exception)
    {
        var value = $"{exception.GetType().Name}: {exception.Message}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static string? Known(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
