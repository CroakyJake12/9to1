using System.Net;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Android;

/// <summary>Keyboard execution narrows the canonical Home route for each explicit action.
/// It never changes global privacy, stores field content, or uses an unbounded fallback client.</summary>
internal sealed class RoutedKeyboardAiExecutor(
    IModelProviderRegistry providers,
    IVersionedModelRouteRepository routes,
    IProviderConfigurationStore configurations,
    IPrivacyPreferenceStore privacy,
    Func<string?> currentModel,
    Func<bool> allowCloud) : IKeyboardAiExecutor
{
    internal const string ActiveRouteId = "home.active";

    public async Task<string?> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configured = await routes.GetAsync(ActiveRouteId, cancellationToken).ConfigureAwait(false);
        var cloudConsent = allowCloud() && !privacy.Current.LocalOnlyMode && !RuntimeSafetyState.IsSafeMode;
        var policy = configured?.Policy ?? new ProviderPolicy(AllowCloud: cloudConsent,
            AllowRemote: cloudConsent, AllowPrivateContextToCloud: cloudConsent, AllowFallback: false);
        policy = policy with
        {
            AllowCloud = policy.AllowCloud && cloudConsent,
            AllowRemote = policy.AllowRemote && cloudConsent,
            AllowPrivateContextToCloud = policy.AllowPrivateContextToCloud && cloudConsent,
            RequiredCapabilities = new HashSet<string>((policy.RequiredCapabilities ?? new HashSet<string>()).Append(nameof(ToolCapability.Text)), StringComparer.OrdinalIgnoreCase)
        };
        var candidates = configured?.Candidates.Where(item => item.Enabled).OrderBy(item => item.Order).Select(item => item.Model).ToArray()
            ?? LegacySelection(currentModel());
        var route = new ModelRoute(configured?.RouteId ?? "android.keyboard.current-selection",
            (int)Math.Clamp(configured?.Revision ?? 1, 1, int.MaxValue), candidates, policy);
        var resolver = new ModelRouteResolver(providers);
        while (route.Candidates.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = await resolver.ResolveAsync(route, null, containsPrivateContext: true, cancellationToken).ConfigureAwait(false);
            var selected = resolution.Value?.Model ?? throw new InvalidOperationException("No authorised keyboard model is available.");
            var provider = providers.GetRequired(selected.ProviderId);
            if ((await configurations.GetAsync(provider.Id, cancellationToken).ConfigureAwait(false))?.IsEnabled == false)
                throw new InvalidOperationException("The selected keyboard provider is disabled.");
            // Recheck live consent/privacy immediately before disclosing field content.
            var mustRemainLocal = !allowCloud() || privacy.Current.LocalOnlyMode || RuntimeSafetyState.IsSafeMode
                || !policy.AllowCloud || !policy.AllowRemote || !policy.AllowPrivateContextToCloud;
            var actualLocal = await IsDeviceLocalAsync(provider, cancellationToken).ConfigureAwait(false);
            if (mustRemainLocal && !actualLocal)
            {
                if (!policy.AllowFallback) throw new InvalidOperationException("This model is outside the device-local keyboard policy.");
                route = Without(route, selected);
                continue;
            }
            try
            {
                return await provider.CompleteAsync(new OllamaChatRequest(selected.ModelId,
                    [new OllamaMessage("user", prompt)], EffortLevel.Medium), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (policy.AllowFallback && IsRoutingFailure(exception, cancellationToken))
            {
                // Every next candidate passes the same canonical resolver and live locality checks.
                route = Without(route, selected);
            }
        }
        throw new InvalidOperationException("The authorised keyboard route is unavailable or exhausted.");
    }

    private ModelIdentity[] LegacySelection(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return [];
        var separator = model.IndexOf(':');
        return separator > 0 && providers.Find(model[..separator]) is not null
            ? [new(model[..separator], model[(separator + 1)..])]
            : [new("ollama", model)];
    }

    private async Task<bool> IsDeviceLocalAsync(IModelProvider provider, CancellationToken cancellationToken)
    {
        if (!provider.IsLocal) return false;
        var configuration = await configurations.GetAsync(provider.Id, cancellationToken).ConfigureAwait(false);
        if (configuration?.IsEnabled == false || configuration?.IsLocal == false) return false;
        var endpoint = configuration?.Endpoint;
        if (provider.Kind == ModelProviderKind.Ollama)
            endpoint ??= Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://127.0.0.1:11434/";
        // Native in-process providers may have no network endpoint. Any explicit
        // HTTP endpoint must be loopback before it is called device-local.
        if (string.IsNullOrWhiteSpace(endpoint)) return true;
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static ModelRoute Without(ModelRoute route, ModelIdentity selected) =>
        route with { Candidates = route.Candidates.Where(item => !item.StableKey.Equals(selected.StableKey, StringComparison.OrdinalIgnoreCase)).ToArray() };

    private static bool IsRoutingFailure(Exception exception, CancellationToken token) => !token.IsCancellationRequested && (exception switch
    {
        HttpRequestException http => http.StatusCode is null or HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        IOException => true,
        _ => false
    });
}
