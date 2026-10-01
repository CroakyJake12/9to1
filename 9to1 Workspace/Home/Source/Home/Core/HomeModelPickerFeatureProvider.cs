using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Personal native model routes are owned by Home's real OS-bound profile. App/Agent/task ownership
/// requires its own canonical owner service and is never inferred from an arbitrary route scope string.</summary>
public sealed class HomeModelRouteOwner(HomeLocalProfileIdentity profiles, IVersionedModelRouteRepository routes)
    : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "home.model-route";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        var current = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current != actor || actor.OrganisationId is not null || actor.AccountId is not null ||
            actionId is not ("models.routes.read" or "models.routes.preview" or "models.routes.update"))
            return new(false, "HomeProfileRequired", actor.ActorId, string.Empty, null);
        var route = await routes.GetAsync(scope.Id, cancellationToken).ConfigureAwait(false);
        var allowed = route is not null && route.Scope == ModelRouteScope.User && route.ScopeId == actor.ProfileId &&
            route.RouteId == HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, route.Category) &&
            scope.Access == (actionId == "models.routes.update" ? ResourceAccess.Write : ResourceAccess.Read);
        return new(allowed, allowed ? "HomeProfileRoute" : "RouteOwnerDenied", actor.ActorId,
            route?.Revision.ToString(CultureInfo.InvariantCulture) ?? string.Empty, null);
    }
}

public sealed class HomeModelRouteProfileOwner(HomeLocalProfileIdentity profiles) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "home.profile-model-routes";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        var current = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var allowed = current == actor && actor.AccountId is null && actor.OrganisationId is null && scope.Id == actor.ProfileId &&
            actionId is "models.routes.read" or "models.routes.preview" or "models.routes.update" &&
            scope.Access == (actionId == "models.routes.update" ? ResourceAccess.Write : ResourceAccess.Read);
        return new(allowed, allowed ? "HomeProfileRoutes" : "HomeProfileRequired", actor.ActorId, current?.AuthenticationRevision ?? string.Empty, null);
    }
}

public sealed class HomeModelRouteActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == HomeModelPickerFeatureProvider.AppId && actionId == "models.routes.update"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Routine, true, false, true) : null;
}

/// <summary>Real canonical repository/provider adapter. Catalogue discovery is privacy filtered before network access;
/// mutation requires the same Home broker and the repository's actual expected revision.</summary>
public sealed class HomeModelPickerFeatureProvider(HomeLocalProfileIdentity profiles, IVersionedModelRouteRepository routes,
    IModelProviderRegistry providers, IPrivacyPreferenceStore privacy, ResourceAuthorizationService resources,
    HomeResourceOperationBroker operations) : IHomeModelPickerFeatureProvider
{
    private readonly ConcurrentDictionary<string, (HomeResourceExecutionCapability Capability, string RouteId)> _pendingAudits = new();

    public const string AppId = "9to1.home.models";
    public static string RouteId(string profileId, ModelCapabilityCategory category) => $"home.profile:{profileId}:{category.ToString().ToLowerInvariant()}";

    public async Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync("models.routes.read", ResourceAccess.Read, cancellationToken).ConfigureAwait(false);
        if (actor is null) return Denied<HomeModelCataloguePage>();
        var models = await providers.GetModelsAsync(new ModelCataloguePolicy(AllowRemote: !privacy.Current.LocalOnlyMode), cancellationToken).ConfigureAwait(false);
        if (await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Denied<HomeModelCataloguePage>();
        var entries = models.Where(model => string.IsNullOrWhiteSpace(query) || model.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            model.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).Select(model => new HomeModelPickerCatalogueEntry(
                model.ProviderId, model.Name, null, model.Label, providers.Find(model.ProviderId)?.DisplayName ?? model.ProviderId,
                model.IsLocal, model.Capabilities.Select(value => value.ToString()).ToHashSet(StringComparer.Ordinal),
                model.ContextWindow, null, null, null)).ToArray();
        return new(true, "Ready", "Current authorised provider catalogue.", new(entries, 0, entries.Length, false, 0));
    }

    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category,
        CancellationToken cancellationToken = default)
    {
        if (!TryCategory(scope, category, out var parsed)) return Unsupported<HomeModelPickerSnapshot>();
        var actor = await ActorAsync("models.routes.read", ResourceAccess.Read, cancellationToken).ConfigureAwait(false);
        if (actor is null) return Denied<HomeModelPickerSnapshot>();
        var route = await routes.GetAsync(RouteId(actor.ProfileId, parsed), cancellationToken).ConfigureAwait(false);
        if (route is not null && !Owns(route, actor)) return Denied<HomeModelPickerSnapshot>();
        if (await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Denied<HomeModelPickerSnapshot>();
        route ??= new(RouteId(actor.ProfileId, parsed), 0, ModelRouteScope.User, actor.ProfileId, parsed, [], new());
        var pendingAudit = _pendingAudits.FirstOrDefault(entry => entry.Value.RouteId == route.RouteId).Key;
        return new(true, "Ready", route.Revision == 0 ? "New personal route draft; no model is selected until saved." : "Current personal Home route.", Snapshot(route) with { PendingAuditRequestId = pendingAudit });
    }

    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit,
        CancellationToken cancellationToken = default)
    {
        if (edit?.Route is not { } input || !TryCategory(input.Scope, input.Category, out var category) ||
            input.Candidates is null || input.Candidates.Count > 256 || input.Policy.ValueKind != JsonValueKind.Object || edit.ExpectedRevision < 0 ||
            input.Version != edit.ExpectedRevision + 1) return Invalid<HomeModelPickerSnapshot>();
        // Freeze all caller-owned collections and JSON before the first asynchronous authority lookup.
        if (edit.ApprovalRequestId is { } pendingRequest && _pendingAudits.ContainsKey(pendingRequest))
            return new(false, "AuditPending", "The original route outcome is retained. Finish its audit; do not repeat the save.");
        input = input with { Candidates = Array.AsReadOnly(input.Candidates.ToArray()), Policy = input.Policy.Clone() };
        ProviderPolicy policy;
        try { policy = input.Policy.Deserialize<ProviderPolicy>() ?? throw new JsonException(); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return Invalid<HomeModelPickerSnapshot>(); }
        var candidates = input.Candidates.ToArray();
        if (candidates.Any(item => item is null || string.IsNullOrWhiteSpace(item.ProviderId) || string.IsNullOrWhiteSpace(item.ModelId) ||
            item.ArtifactRevision is not null || item.Order < 0) || candidates.Select(item => new ModelIdentity(item.ProviderId, item.ModelId).StableKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidates.Length ||
            candidates.Select(item => item.Order).Distinct().Count() != candidates.Length) return Invalid<HomeModelPickerSnapshot>();
        if (routes is not IHomeGuardedModelRouteRepository guardedRoutes) return Denied<HomeModelPickerSnapshot>();
        var actor = await ActorAsync("models.routes.update", ResourceAccess.Write, cancellationToken).ConfigureAwait(false);
        if (actor is null || input.ScopeId != actor.ProfileId || input.RouteId != RouteId(actor.ProfileId, category) ||
            input.AppId is not null || input.OverrideIdentity is not null) return Denied<HomeModelPickerSnapshot>();
        // Detach payloads before any approval or owner lookup. Provider descriptors currently do not attest artifact revisions.
        var route = new ConfiguredModelRoute(input.RouteId, input.Version, ModelRouteScope.User, actor.ProfileId, category,
            Array.AsReadOnly(candidates.Select(item => new ModelRouteCandidate(new(item.ProviderId, item.ModelId), item.Enabled, item.Order)).ToArray()),
            policy with { AllowedProviders = policy.AllowedProviders?.ToHashSet(StringComparer.Ordinal), RequiredCapabilities = policy.RequiredCapabilities?.ToHashSet(StringComparer.Ordinal) });
        var current = await routes.GetAsync(route.RouteId, cancellationToken).ConfigureAwait(false);
        if (current is not null && !Owns(current, actor)) return Denied<HomeModelPickerSnapshot>();
        if ((current?.Revision ?? 0) != edit.ExpectedRevision) return new(false, "Conflict", "Reload the route before saving.");
        var scopeList = new List<ResourceScope> { ProfileScope(actor, ResourceAccess.Write) };
        if (current is not null) scopeList.Add(new("home.model-route", current.RouteId, current.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write));
        var arguments = JsonSerializer.SerializeToElement(route);
        var changePreview = DescribeRouteChange(current, route);
        if (changePreview.Length > 65536)
            return new(false, "PreviewTooLarge", "This route change is too large to review safely. Reduce candidate or policy details before requesting approval.");
        if (string.IsNullOrWhiteSpace(edit.ApprovalRequestId))
        {
            var authorization = await operations.AuthorizeAsync(AppId, "models.routes.update", scopeList, arguments,
                changePreview, null,
                actor.AuthenticationRevision, cancellationToken).ConfigureAwait(false);
            return new(false, "ApprovalRequired", "Review this exact route change in Home before saving.",
                Snapshot(current ?? route with { Revision = 0 }) with { PendingApprovalRequestId = authorization.RequestId });
        }
        var capability = await operations.BeginExecutionCapabilityAsync(edit.ApprovalRequestId, arguments, cancellationToken).ConfigureAwait(false);
        if (capability is null || await operations.ClaimExecutionAsync(capability, AppId, "models.routes.update", scopeList, arguments, cancellationToken).ConfigureAwait(false) != actor)
            return Denied<HomeModelPickerSnapshot>();
        // CAS is the authoritative transaction; an approval cannot overwrite a concurrent or changed-owner record.
        bool saved;
        try { saved = await guardedRoutes.TrySaveGuardedAsync(route, edit.ExpectedRevision, actor, profiles, cancellationToken).ConfigureAwait(false); }
        catch
        {
            var outcomeAudited = await RecordOutcomeAsync(capability, new(HomePermissionRequestState.PartiallyCompleted,
                "HOME_MODEL_ROUTE_OUTCOME_UNCONFIRMED", "The model route save did not return a confirmed outcome. Reload its canonical state before any further edit.", []), route.RouteId).ConfigureAwait(false);
            return new(false, "OutcomeUnconfirmed", "The route save did not return a confirmed result. Reload canonical state before any further edit.",
                Snapshot(current ?? route with { Revision = 0 }) with { PendingAuditRequestId = outcomeAudited ? null : capability.RequestId });
        }
        if (!saved)
        {
            var failureAudited = await RecordOutcomeAsync(capability, new(HomePermissionRequestState.Failed,
                "HOME_MODEL_ROUTE_NOT_COMMITTED", "The canonical route revision or current authority rejected the save.", []), route.RouteId).ConfigureAwait(false);
            return new(false, "Conflict", "Another route edit won or authority changed; reload and request approval again.",
                Snapshot(current ?? route with { Revision = 0 }) with { PendingAuditRequestId = failureAudited ? null : capability.RequestId });
        }
        var audited = await RecordOutcomeAsync(capability, new(HomePermissionRequestState.Succeeded,
            "HOME_MODEL_ROUTE_COMMITTED", "The canonical model route was saved.", [new("home.model-route", route.RouteId)]), route.RouteId).ConfigureAwait(false);
        return new(true, audited ? "Saved" : "SavedAuditPending",
            audited ? "Model route saved." : "Model route saved; Home could not confirm its audit record. Do not repeat the save.", Snapshot(route) with { PendingAuditRequestId = audited ? null : capability.RequestId }, Revision: route.Revision);
    }

    private async Task<bool> RecordOutcomeAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome, string routeId)
    {
        _pendingAudits.TryAdd(capability.RequestId, (capability, routeId));
        try
        {
            var recorded = await operations.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false);
            if (recorded.Succeeded) _pendingAudits.TryRemove(capability.RequestId, out _);
            return recorded.Succeeded;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        { return false; } // The exact sealed outcome remains retained; never repeat the route CAS.
    }

    public async Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId) || !_pendingAudits.TryGetValue(requestId, out var pending))
            return new(false, "AuditNotOwned", "This host does not retain the original route audit.");
        try
        {
            var result = await operations.RetryCompletionAuditAsync(pending.Capability, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded) return new(false, "AuditPending", "The original outcome is retained; its audit is still unconfirmed.");
            _pendingAudits.TryRemove(requestId, out _);
            return new(true, "AuditRecorded", "The original route outcome audit is confirmed. No route save was repeated.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        { return new(false, "AuditPending", "The original outcome is retained; its audit is still unavailable."); }
    }

    private static string DescribeRouteChange(ConfiguredModelRoute? before, ConfiguredModelRoute after)
    {
        static string Describe(ConfiguredModelRoute route)
        {
            var lines = route.Candidates.OrderBy(candidate => candidate.Order).Select(candidate =>
                $"Priority {candidate.Order}: {(candidate.Enabled ? "enabled" : "disabled")} provider {JsonSerializer.Serialize(candidate.Model.ProviderId)}, model {JsonSerializer.Serialize(candidate.Model.ModelId)}");
            var candidates = route.Candidates.Count == 0 ? "No candidates." : string.Join("\n", lines);
            // Only typed routing fields are displayed. JSON string escaping prevents control characters or
            // provider/model labels from impersonating another line; no provider credentials/configuration are read.
            return $"Revision {route.Revision}\n{candidates}\nPolicy: {JsonSerializer.Serialize(route.Policy)}";
        }
        return $"Change {after.Category} model route for this Home profile.\nBefore:\n" +
            (before is null ? "No saved route." : Describe(before)) + "\nAfter:\n" + Describe(after);
    }

    public async Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RouteId) || string.IsNullOrWhiteSpace(request.Capability)) return Invalid<HomeModelRoutePreview>();
        var actor = await ActorAsync("models.routes.preview", ResourceAccess.Read, cancellationToken).ConfigureAwait(false);
        if (actor is null) return Denied<HomeModelRoutePreview>();
        var route = await routes.GetAsync(request.RouteId, cancellationToken).ConfigureAwait(false);
        if (route is null || !Owns(route, actor) || request.AppId is not null || request.AgentId is not null) return Denied<HomeModelRoutePreview>();
        var policy = route.Policy with { AllowRemote = route.Policy.AllowRemote && !privacy.Current.LocalOnlyMode,
            RequiredCapabilities = (route.Policy.RequiredCapabilities ?? new HashSet<string>()).Append(request.Capability).ToHashSet(StringComparer.Ordinal) };
        var registry = new ModelRouteRegistry(providers, routes, new ModelRouteResolver(providers));
        var preview = await registry.PreviewAsync(route with { Policy = policy }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Denied<HomeModelRoutePreview>();
        return new(true, "PreviewReady", "Eligibility preview only; no model request was dispatched.", new(route.RouteId, route.Revision,
            preview.Selection?.Model.StableKey, preview.Selection?.Reason ?? "No eligible configured candidate.", preview.Trace,
            preview.Error?.Code.ToString()), Revision: route.Revision);
    }

    private async Task<AuthenticatedResourceActor?> ActorAsync(string action, ResourceAccess access, CancellationToken cancellationToken)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        return actor is null ? null : await resources.AuthorizeAsync(action, [ProfileScope(actor, access)], cancellationToken).ConfigureAwait(false);
    }
    private static ResourceScope ProfileScope(AuthenticatedResourceActor actor, ResourceAccess access) => new("home.profile-model-routes", actor.ProfileId, actor.AuthenticationRevision, access);
    private static bool Owns(ConfiguredModelRoute route, AuthenticatedResourceActor actor) => route.Scope == ModelRouteScope.User &&
        route.ScopeId == actor.ProfileId && route.RouteId == RouteId(actor.ProfileId, route.Category);
    private static bool TryCategory(string scope, string category, out ModelCapabilityCategory value) =>
        Enum.TryParse(category, true, out value) && Enum.IsDefined(value) && string.Equals(scope, "User", StringComparison.OrdinalIgnoreCase);
    private static HomeModelPickerSnapshot Snapshot(ConfiguredModelRoute route) => new(route.Revision, "User", route.Category.ToString(),
        [new(route.RouteId, route.Revision, "User", route.Category.ToString(), null, null,
            Array.AsReadOnly(route.Candidates.Select(item => new HomeModelRouteCandidate(item.Model.ProviderId, item.Model.ModelId, item.Model.ArtifactRevision, item.Enabled, item.Order)).ToArray()),
            JsonSerializer.SerializeToElement(route.Policy), route.ScopeId)]);
    private static HomeCoreOperationResult<T> Denied<T>() => new(false, "PermissionDenied", "Current canonical Home profile and route ownership are required.");
    private static HomeCoreOperationResult<T> Unsupported<T>() => new(false, "OwnerUnavailable", "This route scope requires its actual app, Agent or task owner authority.");
    private static HomeCoreOperationResult<T> Invalid<T>() => new(false, "InvalidRoute", "The route edit contains invalid or unsupported identities, policy or revisions.");
}

public sealed class HomeModelPickerCoreService(IHomeModelPickerFeatureProvider provider) : IHomeCoreService
{
    public HomeServiceDescriptor Descriptor { get; } = new("models.routes", HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped, false, "Model route authority has not been verified.");
    public IReadOnlyList<string> Dependencies { get; } = ["home.core", "home.state", "permissions.trust"];
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await provider.GetSnapshotAsync("User", "Active", cancellationToken).ConfigureAwait(false);
        if (!snapshot.Succeeded) throw new InvalidOperationException("Home model route authority is unavailable.");
    }
    public Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}
