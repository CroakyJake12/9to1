using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    private sealed partial class Host
    {
        private async Task ConfigureOriginalStrataProviderAsync(DesktopOriginalWorkLifetime.Original original,
            string explicitTarget, CancellationToken token)
        {
            if (!StrataLoopbackModelProvider.IsAllowedEndpoint(explicitTarget))
                throw new ArgumentException("Choose an explicit literal-loopback /v1 Strata endpoint without credentials, query or fragment.");
            var registry = Resolve<IModelProviderRegistry>(original);
            StrataLoopbackModelProvider? selected = null;
            Scope(original, () => selected = registry.GetRequired(StrataLoopbackModelProvider.ProviderId) as StrataLoopbackModelProvider
                ?? throw new InvalidOperationException("The maintained local Strata provider is unavailable."));
            var source = selected ?? throw new InvalidOperationException("No original Strata provider was captured.");
            var configurations = Resolve<IProviderConfigurationStore>(original);
            var endpoint = new Uri(explicitTarget).GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
            var requested = new ProviderConfiguration(source.Id, source.Kind, source.DisplayName, endpoint,
                IsEnabled: true, IsLocal: true, AllowCloudFallback: false,
                Metadata: new Dictionary<string, string>(), UpdatedAt: DateTimeOffset.UtcNow);
            await Acquire(original, () => configurations.UpsertAsync(requested, token)).ConfigureAwait(false);
            var actual = await Acquire(original, () => configurations.GetAsync(source.Id, token)).ConfigureAwait(false);
            Scope(original, () =>
            {
                if (!ReferenceEquals(registry.GetRequired(source.Id), source) || actual is null || actual.Id != source.Id ||
                    actual.Kind != source.Kind || actual.Endpoint != endpoint || !actual.IsEnabled || !actual.IsLocal || actual.AllowCloudFallback)
                    throw new InvalidOperationException("The actual loopback configuration readback changed.");
                Write(new { providerMetadataConfigured = true, configuration = actual,
                    note = "Existing server connection configured only; no model loaded. Use strata-probe explicitly, then models strata-local and select its actual observed key. File/tool work still requires the canonical Task permissions." });
            });
        }

        private async Task ConfigureOriginalLocalProviderAsync(DesktopOriginalWorkLifetime.Original original,
            string explicitLogicalTarget, CancellationToken token)
        {
            if (!Uri.TryCreate(explicitLogicalTarget, UriKind.Absolute, out var target)
                || target.Scheme is not ("http" or "https") || !target.IsLoopback
                || !string.IsNullOrEmpty(target.UserInfo) || !string.IsNullOrEmpty(target.Query)
                || !string.IsNullOrEmpty(target.Fragment))
                throw new ArgumentException("Choose an explicit loopback logical HTTP/HTTPS target without credentials, query or fragment. The actual model transport remains the verified Unix socket.");
            var source = Resolve<LlamaCppModelProvider>(original);
            var registry = Resolve<IModelProviderRegistry>(original);
            var configurations = Resolve<IProviderConfigurationStore>(original);
            Scope(original, () =>
            {
                if (!ReferenceEquals(registry.GetRequired(source.Id), source)
                    || source.Id != "llama-cpp" || source.Kind != ModelProviderKind.OpenAICompatible || !source.IsLocal)
                    throw new InvalidOperationException("The SAME maintained local llama.cpp provider is required for this explicit metadata configuration.");
            });
            var endpoint = target.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
            var requested = new ProviderConfiguration(source.Id, source.Kind, "Local llama.cpp", endpoint,
                IsEnabled: true, IsLocal: true, AllowCloudFallback: false,
                Metadata: new Dictionary<string, string>(), UpdatedAt: DateTimeOffset.UtcNow);
            await Acquire(original, () => configurations.UpsertAsync(requested, token)).ConfigureAwait(false);
            var acknowledged = await Acquire(original, () => configurations.GetAsync(source.Id, token)).ConfigureAwait(false);
            Scope(original, () =>
            {
                if (!ReferenceEquals(registry.GetRequired(source.Id), source)
                    || acknowledged is null || acknowledged.Id != requested.Id || acknowledged.Kind != requested.Kind
                    || acknowledged.DisplayName != requested.DisplayName || acknowledged.Endpoint != endpoint
                    || !acknowledged.IsEnabled || !acknowledged.IsLocal || acknowledged.AllowCloudFallback
                    || acknowledged.Metadata.Count != 0)
                    throw new InvalidOperationException("The actual enabled/local provider metadata readback changed.");
                Write(new { providerMetadataConfigured = true, configuration = acknowledged,
                    note = "Configuration readback only. The logical URI is not contacted by the AF_UNIX provider; actual peer, executable/model checksum, capabilities and request authority remain required." });
            });
        }
    }
}
