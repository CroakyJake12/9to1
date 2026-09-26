using Haven.Application;
using Haven.Core;

namespace Dulche.Runtime;

/// <summary>Eligibility-first routing over the canonical shared provider catalogue.</summary>
public sealed class ModelRouteResolver(IModelProviderRegistry providers)
{
    public async Task<OperationResult<RouteSelection>> ResolveAsync(ModelRoute route, ModelIdentity? explicitModel, bool containsPrivateContext = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Version <= 0 || string.IsNullOrWhiteSpace(route.RouteId))
            return Fail<RouteSelection>(DulcheErrorCode.InvalidArgument, "A route must have a stable identity and positive version.", "route");
        var policy = route.Policy ?? new ProviderPolicy();
        var catalogue = await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var all = catalogue.Select(model => (Descriptor: model, Identity: new ModelIdentity(model.ProviderId, model.Name))).ToArray();
        var skipped = new List<string>();
        var configured = route.Candidates;
        if (explicitModel is not null)
            configured = new[] { explicitModel }.Concat(policy.AllowFallback ? configured.Where(candidate => candidate.StableKey != explicitModel.StableKey) : []).ToArray();

        foreach (var candidate in configured)
        {
            var actual = all.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Identity.ProviderId, candidate.ProviderId)
                && StringComparer.OrdinalIgnoreCase.Equals(item.Identity.ModelId, candidate.ModelId)
                && (candidate.ArtifactRevision is null || StringComparer.Ordinal.Equals(item.Identity.ArtifactRevision, candidate.ArtifactRevision)));
            if (actual.Descriptor is null) { skipped.Add($"{candidate.StableKey}: model unavailable"); continue; }
            if (policy.AllowedProviders is { } allowed && !allowed.Contains(candidate.ProviderId)) { skipped.Add($"{candidate.StableKey}: provider blocked by policy"); continue; }
            if (actual.Descriptor.IsLocal && !policy.AllowLocal) { skipped.Add($"{candidate.StableKey}: local providers disabled"); continue; }
            if (!actual.Descriptor.IsLocal && (!policy.AllowRemote || !policy.AllowCloud)) { skipped.Add($"{candidate.StableKey}: remote/cloud use not authorised"); continue; }
            var required = policy.RequiredCapabilities ?? new HashSet<string>();
            var unsupported = required.Where(capability => !actual.Descriptor.Capabilities.Any(value => StringComparer.OrdinalIgnoreCase.Equals(value.ToString(), capability))).ToArray();
            if (unsupported.Length > 0) { skipped.Add($"{candidate.StableKey}: unsupported capabilities {string.Join(", ", unsupported)}"); continue; }
            if (!actual.Descriptor.IsLocal && containsPrivateContext && !policy.AllowPrivateContextToCloud)
                return Fail<RouteSelection>(DulcheErrorCode.PermissionDenied, "Cloud routing requires explicit permission for private context.", candidate.StableKey);
            return OperationResult<RouteSelection>.Success(new(actual.Identity, skipped.Count == 0 ? "Selected first eligible configured model." : "Selected next eligible model after policy/capability filtering.", skipped));
        }
        return Fail<RouteSelection>(explicitModel is null ? DulcheErrorCode.ProviderUnavailable : DulcheErrorCode.ModelNotFound,
            explicitModel is null ? "No configured route candidate is available." : "Model not found or ineligible; fallback is unavailable or exhausted.", explicitModel?.StableKey ?? route.RouteId, skipped);
    }

    private static OperationResult<T> Fail<T>(DulcheErrorCode code, string message, string target, IReadOnlyList<string>? skipped = null) =>
        OperationResult<T>.Failure(new(code, message, target, Retryable: code == DulcheErrorCode.ProviderUnavailable,
            Details: skipped is null ? null : new Dictionary<string, string> { ["skipped"] = string.Join(" | ", skipped) }));
}
