using Haven.Application;

namespace Dulche.Runtime;

/// <summary>Versioned shared model catalogue and scoped route editor used by Home and app pickers.</summary>
public sealed class ModelRouteRegistry(IModelProviderRegistry providers, IVersionedModelRouteRepository routes, ModelRouteResolver resolver)
{
    public async Task<IReadOnlyList<ModelCatalogueEntry>> GetCatalogueAsync(CancellationToken cancellationToken = default)
    {
        var models = await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.Select(model => new ModelCatalogueEntry(
            new(model.ProviderId, model.Name), model.Label, model.ProviderId, model.IsLocal,
            model.Capabilities.Select(value => value.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            model.ContextWindow, ModelState.Installed, Metric<long>.Na)).ToArray();
    }

    public Task<ConfiguredModelRoute?> GetRouteAsync(string routeId, CancellationToken cancellationToken = default) =>
        routes.GetAsync(routeId, cancellationToken);

    public Task<IReadOnlyList<ConfiguredModelRoute>> ListRoutesAsync(CancellationToken cancellationToken = default) =>
        routes.ListAsync(cancellationToken);

    public async Task<RouteUpdateResult> SaveRouteAsync(ConfiguredModelRoute route, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var validation = Validate(route);
        if (validation is not null) return new(null, validation);
        var current = await routes.GetAsync(route.RouteId, cancellationToken).ConfigureAwait(false);
        var actual = current?.Revision ?? 0;
        if (expectedRevision != actual) return new(current, new(DulcheErrorCode.Conflict, "Route changed since it was read; reload before applying edits.", route.RouteId, true, Details: new Dictionary<string, string> { ["expectedRevision"] = expectedRevision.ToString(), ["actualRevision"] = actual.ToString() }));
        var updated = route with { Revision = actual + 1, Candidates = route.Candidates.OrderBy(candidate => candidate.Order).Select((candidate, index) => candidate with { Order = index }).ToArray() };
        if (!await routes.TrySaveAsync(updated, expectedRevision, cancellationToken).ConfigureAwait(false))
            return new(await routes.GetAsync(route.RouteId, cancellationToken).ConfigureAwait(false), new(DulcheErrorCode.Conflict, "Concurrent route edit won the revision check.", route.RouteId, true));
        return new(updated, null);
    }

    public async Task<ModelRouteResolutionPreview> PreviewAsync(ConfiguredModelRoute route, ModelIdentity? explicitModel = null, bool containsPrivateContext = false, CancellationToken cancellationToken = default)
    {
        var validation = Validate(route);
        if (validation is not null) return new(route, null, [], validation);
        var ordered = route.Candidates.Where(candidate => candidate.Enabled).OrderBy(candidate => candidate.Order).Select(candidate => candidate.Model).ToArray();
        var disabled = route.Candidates.Where(candidate => !candidate.Enabled).Select(candidate => $"{candidate.Model.StableKey}: disabled by user").ToArray();
        var runtimeRoute = new ModelRoute(route.RouteId, checked((int)Math.Min(int.MaxValue, route.Revision)), ordered, route.Policy);
        var resolution = await resolver.ResolveAsync(runtimeRoute, explicitModel, containsPrivateContext, cancellationToken).ConfigureAwait(false);
        var trace = disabled.Concat(resolution.Value?.Skipped ?? GetSkipped(resolution.Error)).ToArray();
        return new(route, resolution.Value, trace, resolution.Error);
    }

    private static IReadOnlyList<string> GetSkipped(DulcheError? error) =>
        error?.Details?.TryGetValue("skipped", out var values) == true && !string.IsNullOrEmpty(values) ? values.Split(" | ") : [];

    private static DulcheError? Validate(ConfiguredModelRoute route)
    {
        if (route is null || string.IsNullOrWhiteSpace(route.RouteId) || route.Revision < 0 || string.IsNullOrWhiteSpace(route.ScopeId))
            return new(DulcheErrorCode.InvalidArgument, "Route identity, non-negative revision and scope identity are required.", "route", false);
        if (route.Candidates is null) return new(DulcheErrorCode.InvalidArgument, "Route candidates are required.", route.RouteId, false);
        if (route.Candidates.Any(candidate => candidate is null || string.IsNullOrWhiteSpace(candidate.Model.ProviderId) || string.IsNullOrWhiteSpace(candidate.Model.ModelId) || candidate.Order < 0))
            return new(DulcheErrorCode.InvalidArgument, "Every route candidate needs a stable provider-scoped identity and non-negative order.", route.RouteId, false);
        if (route.Candidates.Select(candidate => candidate.Model.StableKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != route.Candidates.Count)
            return new(DulcheErrorCode.InvalidArgument, "A route cannot contain the same stable model identity more than once.", route.RouteId, false);
        if (route.Candidates.Select(candidate => candidate.Order).Distinct().Count() != route.Candidates.Count)
            return new(DulcheErrorCode.InvalidArgument, "Route candidate order values must be unique.", route.RouteId, false);
        return null;
    }
}

/// <summary>Simple in-process CAS repository for standalone/runtime-hosted route state.</summary>
public sealed class InMemoryModelRouteRepository : IVersionedModelRouteRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ConfiguredModelRoute> _routes = new(StringComparer.Ordinal);
    public Task<ConfiguredModelRoute?> GetAsync(string routeId, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(_routes.GetValueOrDefault(routeId)); }
    public Task<IReadOnlyList<ConfiguredModelRoute>> ListAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult<IReadOnlyList<ConfiguredModelRoute>>(_routes.Values.OrderBy(item => item.Scope).ThenBy(item => item.ScopeId, StringComparer.Ordinal).ThenBy(item => item.Category).ToArray()); }
    public Task<bool> TrySaveAsync(ConfiguredModelRoute route, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var actual = _routes.TryGetValue(route.RouteId, out var current) ? current.Revision : 0;
            if (actual != expectedRevision || route.Revision != expectedRevision + 1) return Task.FromResult(false);
            _routes[route.RouteId] = route;
            return Task.FromResult(true);
        }
    }
}
