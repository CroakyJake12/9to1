using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Canonical, actor/model/surface-bound selected reasoning-effort state. This is the
/// minimal shared selection truth consumed by the universal picker, the top-bar and actual request
/// construction. It is keyed by the real profile id, the resolved model's stable identity and the
/// real route surface scope, and stores the selected advertised reasoning ID verbatim — never a
/// percentage or an unrelated enum, which would lose the exact ID the request must carry.</summary>
/// <summary>Typed payload raised after a canonical selected-reasoning commit on
/// <see cref="SelectedReasoningState"/>. It carries the binding key and the exact committed ID so a
/// subscriber can re-resolve the same owner and republish to real consumers.</summary>
public readonly record struct SelectionChangedEventArgs(SelectionKey Key, string SelectedId);

public sealed class SelectedReasoningState
{
    private readonly object _gate = new();
    // SelectionKey is a record struct: its default equality compares the component strings
    // ordinally. A StringComparer is not typed to IEqualityComparer<SelectionKey>, so the
    // default comparer is the correct, valid choice.
    private readonly Dictionary<SelectionKey, string> _byKey = new();

    /// <summary>Raised after a real selected-reasoning commit (never before the value is
    /// committed). Subscribers re-resolve the shared owner and re-publish to actual consumers, so a
    /// picker change is observed live rather than only on panel close.</summary>
    public event EventHandler<SelectionChangedEventArgs>? Changed;

    /// <summary>Reads the canonical selected reasoning ID for a (profile, model, surface) binding.
    /// Returns null when none has been committed or the binding is unknown — the truthful absent
    /// state, never a default selection.</summary>
    public bool TryGet(SelectionKey key, out string? selectedId)
    {
        lock (_gate) return _byKey.TryGetValue(key, out selectedId);
    }

    /// <summary>Commits a selected reasoning ID for the binding. Rejects blank values; callers are
    /// responsible for validating exact membership against the model's advertised levels before
    /// commit (a non-advertised ID is not representable). Returns true when committed, and raises
    /// <see cref="Changed"/> only after a genuine change is committed.</summary>
    public bool TrySet(SelectionKey key, string selectedId)
    {
        if (string.IsNullOrWhiteSpace(selectedId)) return false;
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var current) && string.Equals(current, selectedId, StringComparison.Ordinal))
                return true; // no-op: nothing changed, do not notify
            _byKey[key] = selectedId;
        }
        Changed?.Invoke(this, new(key, selectedId));
        return true;
    }
}

/// <summary>Stable identity of one canonical selected-reasoning binding: the authenticated profile,
/// the resolved model's stable identity, and the real route surface scope (never a hardcoded
/// surface literal).</summary>
public readonly record struct SelectionKey(string ProfileId, string ModelStableKey, string SurfaceScope);

/// <summary>Single Home-owned resolved-selection source shared by the universal picker, the top-bar
/// and actual request construction. It resolves the ACTUAL effective selection through the real
/// personal routes (Chat then Active) and the real <see cref="ModelRouteRegistry"/>/<see cref="ModelRouteResolver"/>
/// eligibility path, derives locality from the real provider registry, and surfaces genuine
/// advertised reasoning IDs from the authorised single-Den model owner when one is supplied. It
/// never fabricates a model, a scope, an advertised scale, a selected ordinal or a percentage.</summary>
public sealed class HomeModelSelectionSource
{
    private readonly IModelProviderRegistry _providers;
    private readonly IVersionedModelRouteRepository _routes;
    private readonly Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>> _routeReader;
    private readonly Func<CancellationToken, Task<AuthenticatedResourceActor?>> _actorSource;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>>? _advertisedLevels;
    private readonly SelectedReasoningState _selected;
    private readonly IHomeModelPickerOriginalActorFeatureProvider? _routeProvider;
    private readonly object _pendingGate = new();
    private readonly Dictionary<SelectionKey, PendingApprovalPayload> _pendingByKey = new();
    private readonly object _auditGate = new();
    private readonly Dictionary<SelectionKey, PendingAuditPayload> _auditByKey = new();

    /// <summary>A retained, not-yet-resolved route audit handle. Its sealed outcome stays owned by the
    /// SAME real feature provider/broker; this source only surfaces the request id, blocks replay of the
    /// save until it is resolved, and forwards explicit finish/retry through that SAME owner (never CAS).</summary>
    private sealed class PendingAuditPayload
    {
        internal PendingAuditPayload(string requestId) => RequestId = requestId;
        internal string RequestId { get; }
    }

    /// <summary>A retained, not-yet-committed reasoning-level route approval. It pins the exact
    /// Draft route edit and its expected revision so a later commit resumes the SAME review (after a
    /// genuine Home decision) instead of issuing a fresh, duplicate request. Any change to the actor,
    /// route identity/revision, effective model identity or requested level invalidates the old review.</summary>
    private sealed class PendingApprovalPayload
    {
        internal PendingApprovalPayload(string approvalRequestId, long expectedRevision,
            AuthenticatedResourceActor actor, ConfiguredModelRoute route, ModelIdentity model, string? levelId)
        {
            ApprovalRequestId = approvalRequestId;
            ExpectedRevision = expectedRevision;
            Actor = actor;
            RouteId = route.RouteId;
            RouteRevision = route.Revision;
            ProviderId = model.ProviderId;
            ModelId = model.ModelId;
            ArtifactRevision = model.ArtifactRevision;
            ReasoningLevelId = levelId;
        }

        internal string ApprovalRequestId { get; }
        internal long ExpectedRevision { get; }
        internal AuthenticatedResourceActor Actor { get; }
        internal string RouteId { get; }
        internal long RouteRevision { get; }
        internal string ProviderId { get; }
        internal string ModelId { get; }
        internal string? ArtifactRevision { get; }
        internal string? ReasoningLevelId { get; }

        internal bool Matches(AuthenticatedResourceActor actor, ConfiguredModelRoute route, ModelIdentity model, string? levelId) =>
            Equals(Actor, actor) && RouteId == route.RouteId && RouteRevision == route.Revision
            && string.Equals(ReasoningLevelId, levelId, StringComparison.Ordinal)
            && StringComparer.OrdinalIgnoreCase.Equals(ProviderId, model.ProviderId)
            && StringComparer.OrdinalIgnoreCase.Equals(ModelId, model.ModelId)
            && (ArtifactRevision is null || StringComparer.Ordinal.Equals(ArtifactRevision, model.ArtifactRevision));

        internal static bool SameEffectiveIdentity(ModelIdentity a, ModelIdentity b) =>
            StringComparer.OrdinalIgnoreCase.Equals(a.ProviderId, b.ProviderId)
            && StringComparer.OrdinalIgnoreCase.Equals(a.ModelId, b.ModelId)
            && (a.ArtifactRevision is null || StringComparer.Ordinal.Equals(a.ArtifactRevision, b.ArtifactRevision));
    }

    /// <summary>Creates the source. <paramref name="routeReader"/> is wired to the real
    /// <see cref="HomePersonalModelRoutes.GetAsync"/> in production (authorised, positive-revision
    /// personal route reads); tests may seed canonical routes through it.
    /// <paramref name="advertisedLevels"/> reads the ordered advertised reasoning IDs for a resolved
    /// model id from the existing authorised single-Den model owner (never a second Den); pass null
    /// when no such accessor is available, in which case advertised reasoning stays a truthful unknown
    /// (no percentage). <paramref name="routeProvider"/>, when wired, is the canonical guarded route-CAS
    /// owner (the original-session Home feature provider); it makes <see cref="CommitSelectionAsync"/>
    /// a real mutation path that persists the candidate-bound saved default through the broker/approval/
    /// audit/guarded CAS, and makes the resolved route candidate authoritative (verbatim, including a
    /// cleared null) over any in-memory selection. When it is null the source retains the tested
    /// ordinary/memory API: commits write the shared <see cref="SelectedReasoningState"/> and a saved
    /// non-blank candidate default wins over memory.</summary>
    public HomeModelSelectionSource(
        IModelProviderRegistry providers,
        IVersionedModelRouteRepository routes,
        Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>> routeReader,
        Func<CancellationToken, Task<AuthenticatedResourceActor?>> actorSource,
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>>? advertisedLevels = null,
        SelectedReasoningState? selection = null,
        IHomeModelPickerOriginalActorFeatureProvider? routeProvider = null)
    {
        _providers = providers;
        _routes = routes;
        _routeReader = routeReader;
        _actorSource = actorSource;
        _advertisedLevels = advertisedLevels;
        _selected = selection ?? new SelectedReasoningState();
        _routeProvider = routeProvider;
    }

    /// <summary>Resolves the genuine effective selection to an immutable, versioned snapshot.
    /// Returns null when no personal route/selection exists (truthful unavailable). All fields are
    /// derived from real sources; advertised reasoning IDs are read from the authorised Den's model
    /// record, and the selected ID from the canonical state, only when both are available.</summary>
    public async Task<HavenModelProjectionSnapshot?> ResolveAsync(long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // First coherent read of the current actor/authentication and route.
        var actor0 = _actorSource is null ? null : await _actorSource(token);
        var route0 = await ResolveCurrentRouteAsync(token);
        if (route0 is null) return null;

        var registry = new ModelRouteRegistry(_providers, _routes, new ModelRouteResolver(_providers));
        var preview = await registry.PreviewAsync(route0, containsPrivateContext: false, cancellationToken: token);
        if (preview.Error is not null || preview.Selection is null) return null;
        var selected = preview.Selection;

        var descriptor = _providers.Find(selected.Model.ProviderId);
        var localityKnown = descriptor is not null;
        var isLocal = descriptor?.IsLocal ?? false;

        IReadOnlyList<string> advertised = [];
        string? selectedId = null;
        if (_advertisedLevels is not null && actor0 is not null)
        {
            // Metadata is keyed by the genuine provider+model pair so another provider's same-named
            // model can never authorise this model's advertised scale.
            advertised = (await _advertisedLevels(selected.Model.ProviderId, selected.Model.ModelId, token)) ?? [];
            if (_routeProvider is not null)
            {
                // Canonical production mode: the matched route candidate's saved default is the sole
                // truth, taken verbatim (including a cleared null). The in-memory selection is never
                // consulted here, so an explicit clear stays cleared and a just-committed route default
                // is read back from the guarded store after recreation.
                if (TryResolvePersistedDefault(route0, selected.Model, out var canonicalDefault))
                    selectedId = canonicalDefault;
            }
            else
            {
                // Legacy memory mode (tested ordinary/memory provenance): a saved non-blank candidate
                // default wins; otherwise the current in-memory selection applies.
                if (!TryResolvePersistedDefault(route0, selected.Model, out var persistedDefault)
                    || string.IsNullOrWhiteSpace(persistedDefault))
                {
                    var key = new SelectionKey(actor0.ProfileId, selected.Model.StableKey,
                        $"{ScopeName(route0.Scope)}:{route0.ScopeId}");
                    selectedId = _selected.TryGet(key, out var value) ? value : null;
                }
                else
                {
                    selectedId = persistedDefault;
                }
            }
        }

        // Recheck AFTER the last asynchronous read (the awaited preview/metadata): the actor/authentication
        // and route must still be coherent with the first reads we validated before taking the metadata. A
        // changed profile, authentication revision, route identity or route revision means the earlier model
        // selection is stale and must be rejected rather than published. Cancellation is honoured throughout.
        token.ThrowIfCancellationRequested();
        var actor1 = _actorSource is null ? null : await _actorSource(token);
        var route1 = await ResolveCurrentRouteAsync(token);
        if (route1 is null) return null;
        if (!CoherentActor(actor0, actor1)) return null;
        if (!CoherentRoute(route0, route1)) return null;
        var actor = actor1;
        var route = route1;

        return HavenModelProjectionSnapshot.CreateValidated(
            actor, actor?.AuthenticationRevision, ScopeName(route.Scope), route.ScopeId,
            route.RouteId, route.Revision, generation,
            selected.Model.ProviderId, selected.Model.ModelId,
            isLocal, localityKnown, advertised, selectedId);
    }

    /// <summary>Reads the saved candidate-bound reasoning-level default from the selected candidate of the
    /// resolved route, using the same real provider/model/artifact matcher as the resolver (exact Ordinal
    /// provider/model, and artifact revision when the candidate pins one — never candidate zero, never a
    /// ModelId-only match). Returns false when no candidate matches the effective selection or no default
    /// is stored, so stale/in-memory state is never mistaken for a persisted default.</summary>
    private static bool TryResolvePersistedDefault(ConfiguredModelRoute route, ModelIdentity selectedModel, out string? levelId)
    {
        var candidate = route.Candidates.FirstOrDefault(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.Model.ProviderId, selectedModel.ProviderId)
            && StringComparer.OrdinalIgnoreCase.Equals(item.Model.ModelId, selectedModel.ModelId)
            && (item.Model.ArtifactRevision is null || StringComparer.Ordinal.Equals(item.Model.ArtifactRevision, selectedModel.ArtifactRevision)));
        if (candidate is null) { levelId = null; return false; }
        levelId = candidate.ReasoningLevelId;
        return true;
    }

    private async Task<ConfiguredModelRoute?> ResolveCurrentRouteAsync(CancellationToken token)
    {
        var route = await _routeReader(ModelCapabilityCategory.Chat, token);
        if (route is null) route = await _routeReader(ModelCapabilityCategory.Active, token);
        return route;
    }

    private static bool CoherentActor(AuthenticatedResourceActor? first, AuthenticatedResourceActor? second)
    {
        if (first is null || second is null) return first is null && second is null;
        return first.ProfileId == second.ProfileId
            && first.AuthenticationRevision == second.AuthenticationRevision;
    }

    private static bool CoherentRoute(ConfiguredModelRoute? first, ConfiguredModelRoute? second)
    {
        if (first is null || second is null) return first is null && second is null;
        return first.RouteId == second.RouteId && first.Revision == second.Revision;
    }

    /// <summary>Maps the real <see cref="ModelRouteScope"/> to a dependency-neutral name so the
    /// Home-owned snapshot never carries a Dulche.Runtime type (Haven.Application must not depend on
    /// Dulche.Runtime). This is the only place the home scope type is converted to a string.</summary>
    private static string ScopeName(ModelRouteScope scope) => scope switch
    {
        ModelRouteScope.App => "App",
        ModelRouteScope.Agent => "Agent",
        ModelRouteScope.Task => "Task",
        _ => "User"
    };

    /// <summary>Raised after a real selected-reasoning ID is committed on this retained owner. It is
    /// the typed live-change seam consumers subscribe to so a picker change propagates immediately
    /// (rather than only on panel close). Subscribers must re-resolve this same source and re-publish
    /// through a generation + actor-revision fence.</summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged
    {
        add => _selected.Changed += value;
        remove => _selected.Changed -= value;
    }

    /// <summary>Raised after a real canonical model-route commit (a successful guarded CAS save) that
    /// this source's owner observes. The picker's commit path calls <see cref="NotifyRouteCommitted"/>
    /// so consumers republish the genuine projection live.</summary>
    public event EventHandler? RouteChanged;

    /// <summary>Notifies subscribers that a canonical route was committed. Called by the picker's
    /// commit seam only AFTER a successful guarded CAS save (never on draft/refusal), so consumers
    /// re-resolve the real route and republish under the generation/actor fence.</summary>
    public void NotifyRouteCommitted() => RouteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Returns the retained pending model-route approval request id for a canonical binding,
    /// or null when none is awaiting review. This is presentation-only; committing still routes through
    /// <see cref="CommitSelectionAsync"/>, which alone mutates the retained review.</summary>
    public string? GetPendingApprovalRequestId(string profileId, string modelStableKey, string surfaceScope)
    {
        lock (_pendingGate)
            return _pendingByKey.TryGetValue(new(profileId, modelStableKey, surfaceScope), out var pending)
                ? pending.ApprovalRequestId : null;
    }

    private void SetPending(SelectionKey key, PendingApprovalPayload payload)
    {
        lock (_pendingGate) _pendingByKey[key] = payload;
    }

    private PendingApprovalPayload? TryGetPending(SelectionKey key)
    {
        lock (_pendingGate) return _pendingByKey.TryGetValue(key, out var payload) ? payload : null;
    }

    private void ClearPending(SelectionKey key)
    {
        lock (_pendingGate) _pendingByKey.Remove(key);
    }

    /// <summary>Returns the retained pending model-route audit request id for a canonical binding, or
    /// null when no route audit is awaiting resolution. Presentation/panel use only; the audit is finished
    /// through <see cref="FinishAuditAsync"/>, which routes to the SAME owning provider/broker (never CAS).</summary>
    public string? GetPendingAuditRequestId(string profileId, string modelStableKey, string surfaceScope)
    {
        lock (_auditGate)
            return _auditByKey.TryGetValue(new(profileId, modelStableKey, surfaceScope), out var audit)
                ? audit.RequestId : null;
    }

    private void SetAudit(SelectionKey key, PendingAuditPayload payload)
    {
        lock (_auditGate) _auditByKey[key] = payload;
    }

    private PendingAuditPayload? TryGetAudit(SelectionKey key)
    {
        lock (_auditGate) return _auditByKey.TryGetValue(key, out var audit) ? audit : null;
    }

    private void ClearAuditByRequestId(string requestId)
    {
        lock (_auditGate)
            foreach (var key in _auditByKey.Where(pair => pair.Value.RequestId == requestId).Select(pair => pair.Key).ToList())
                _auditByKey.Remove(key);
    }

    /// <summary>Finishes a retained route audit through the SAME owning provider/broker. This only confirms
    /// the sealed outcome; it never repeats the canonical CAS save and never itself proves save success
    /// (an uncertain outcome still requires an authorized canonical reload). Returns the provider result.</summary>
    public async Task<HomeCoreOperationResult<object>> FinishAuditAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (_routeProvider is null || string.IsNullOrWhiteSpace(requestId))
            return new(false, "AuditNotOwned", "This host does not own the original route audit.");
        var result = await _routeProvider.RetryAuditAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) ClearAuditByRequestId(requestId);
        return result;
    }

    /// <summary>Accesses the canonical selected-reasoning state. The committed selected ID is stored
    /// verbatim for later request construction. Production mutations must route through
    /// <see cref="CommitSelectionAsync"/> (validated, actor/model/surface-bound, raising the typed live
    /// event); raw <see cref="SelectedReasoningState.TrySet"/> is retained for controlled commit and
    /// state isolation tests, never as the production sole mutation path.</summary>
    public SelectedReasoningState Selection => _selected;

    /// <summary>Resolves the exact selected reasoning ID for a real request, preserving the ID
    /// verbatim in <see cref="GenerationSettings.ReasoningLevel"/> when it is a genuine member of the
    /// model's advertised levels. A missing or non-advertised ID returns a structured failure (the
    /// backend is explicitly unsupported/unrepresentable) rather than silently mapping to an
    /// unrelated enum or percentage.</summary>
    public static OperationResult<string> ReasoningLevelForRequest(HavenModelProjectionSnapshot? snapshot)
    {
        var selectedId = snapshot?.SelectedReasoningId;
        var advertised = snapshot?.AdvertisedReasoningIds;
        if (selectedId is null || advertised is null || advertised.Count == 0)
            return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                "No selected reasoning level.", "reasoningLevel", false));
        // Exact ordinal membership, never a case-insensitive name match. The canonical selected ID is
        // returned verbatim so the request carries the exact advertised id — a case-only variant of an
        // advertised id is NOT a genuine member and must be rejected.
        if (!advertised.Contains(selectedId, StringComparer.Ordinal))
            return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                "Requested reasoning level is not advertised by this model.", selectedId, false));
        return OperationResult<string>.Success(selectedId);
    }

    /// <summary>Commits a canonical selected reasoning ID through the validated, actor/model/surface-
    /// bound Home path (the production picker mutation path). The supplied actor must equal the genuine
    /// current <see cref="_actorSource"/> authority on FULL <see cref="AuthenticatedResourceActor"/>
    /// record equality (actor/profile/account/organisation/authentication revision) — never reference
    /// equality or only profile/revision. The canonical binding is derived from the real resolved
    /// personal route's model (its stable identity) and its real scope; caller-supplied stable-key and
    /// scope are only compared, never trusted. After the LAST awaited metadata/eligibility read the
    /// current actor and route are revalidated, so a foreign/stale actor, a changed target/route, or
    /// cancellation yields a typed refusal with an unchanged selection and ZERO <see cref="Changed"/>
    /// notifications. Reads the model's genuine advertised levels through the existing authorised Den
    /// owner (never a second Den) and requires exact Ordinal membership before writing the exact ID
    /// verbatim to the shared selected-ID state. Missing metadata never yields a percentage.</summary>
    public async Task<OperationResult<string>> CommitSelectionAsync(
        AuthenticatedResourceActor actor,
        string modelId,
        string modelStableKey,
        string surfaceScope,
        string? reasoningId,
        CancellationToken cancellationToken)
    {
        if (actor is null)
            return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument,
                "A current authenticated Home actor is required to commit a selection.", "actor", false));
        if (_advertisedLevels is null)
            return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                "This composition resolves no advertised reasoning levels.", $"model:{modelId}", false));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // First coherent read of the genuine current authority. The supplied actor must be the
            // current authenticated actor on full record equality.
            var currentActor = await _actorSource(cancellationToken);
            if (currentActor is null)
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument,
                    "No current authenticated Home actor is available to commit a selection.", "actor", false));
            if (!Equals(actor, currentActor))
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument,
                    "The supplied actor does not match the current authenticated Home actor.", "actor", false));

            // Resolve the GENUINE current personal route and its eligible target model so the
            // advertised-level lookup is keyed by the real provider+model identity — never a string
            // split, never a guess from the stable key, never an empty provider. The requested model
            // must be exactly the resolved route's model.
            var route = await ResolveCurrentRouteAsync(cancellationToken);
            if (route is null)
                return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                    "No current personal route is available to resolve the selected model.", $"model:{modelId}", false));
            var registry = new ModelRouteRegistry(_providers, _routes, new ModelRouteResolver(_providers));
            var preview = await registry.PreviewAsync(route, containsPrivateContext: false, cancellationToken: cancellationToken);
            if (preview.Error is not null || preview.Selection is null)
                return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                    "The current personal route resolves no eligible model.", $"model:{modelId}", false));
            var selected = preview.Selection;
            if (!string.Equals(selected.Model.ModelId, modelId, StringComparison.Ordinal))
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument,
                    "The requested model is not the current route's resolved model.", $"model:{modelId}", false));

            // Derive the canonical binding from the real route/model; reject caller mismatches rather
            // than parse or trust the supplied stable-key/scope.
            var derivedStableKey = selected.Model.StableKey;
            var derivedSurfaceScope = $"{ScopeName(route.Scope)}:{route.ScopeId}";
            if (!string.Equals(modelStableKey, derivedStableKey, StringComparison.Ordinal))
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument,
                    "The supplied model stable key does not match the current route's resolved model.", $"model:{modelId}", false));
            if (!string.Equals(surfaceScope, derivedSurfaceScope, StringComparison.Ordinal))
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument,
                    "The supplied surface scope does not match the current route's scope.", surfaceScope, false));

            var providerId = selected.Model.ProviderId;
            var advertised = await _advertisedLevels(providerId, selected.Model.ModelId, cancellationToken);
            if (advertised is null || advertised.Count == 0)
                return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                    "This model advertises no reasoning levels.", $"model:{modelId}", false));
            if (!string.IsNullOrWhiteSpace(reasoningId) && !advertised.Contains(reasoningId, StringComparer.Ordinal))
                return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability,
                    "Requested reasoning level is not advertised by this model.", reasoningId, false));

            // AFTER the last awaited metadata/eligibility read, revalidate the current actor and route
            // (identity/revision/provider/model/artifact/scope) before committing. A foreign/stale actor,
            // a changed target/route, or cancellation must refuse without mutating the selection.
            cancellationToken.ThrowIfCancellationRequested();
            var currentActor2 = await _actorSource(cancellationToken);
            if (currentActor2 is null || !Equals(actor, currentActor2))
                return OperationResult<string>.Failure(new(DulcheErrorCode.Conflict,
                    "The authenticated actor changed while committing the selection.", "actor", false));
            var route2 = await ResolveCurrentRouteAsync(cancellationToken);
            if (route2 is null || !CoherentRoute(route, route2))
                return OperationResult<string>.Failure(new(DulcheErrorCode.Conflict,
                    "The current route changed while committing the selection.", $"model:{modelId}", false));
            var preview2 = await registry.PreviewAsync(route2, containsPrivateContext: false, cancellationToken: cancellationToken);
            if (preview2.Error is not null || preview2.Selection is null ||
                !PendingApprovalPayload.SameEffectiveIdentity(preview2.Selection.Model, selected.Model))
                return OperationResult<string>.Failure(new(DulcheErrorCode.Conflict,
                    "The current route's resolved model changed while committing the selection.", $"model:{modelId}", false));

            var key = new SelectionKey(currentActor2.ProfileId, derivedStableKey, derivedSurfaceScope);

            if (_routeProvider is null)
            {
                // Legacy ordinary/memory API (preserved): commit to the shared selection state.
                if (reasoningId is not null) _selected.TrySet(key, reasoningId);
                return new OperationResult<string>(reasoningId, null);
            }

            // Canonical production path: persist the saved default through the guarded route CAS. The
            // edit mutates only the matched candidate's reasoning-level default and expects the genuine
            // current route revision, so the broker/approval/audit/guarded CAS runs against the real
            // stored identity. Only a committed canonical success refreshes the same source/live label.
            //
            // Audit custody: if a route save outcome is still unresolved (SavedAuditPending / uncertain
            // outcome), its sealed audit handle is retained by the SAME owner; replay of the save is
            // blocked until it is finished through that SAME owner (never CAS).
            if (TryGetAudit(key) is not null)
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                    "Finish the original route audit before saving this reasoning level.", reasoningId ?? $"model:{modelId}", false));

            // Approval continuation: the first commit authorizes and retains a pending review. A later
            // commit (after a genuine Home decision) resumes that SAME payload via its request id instead
            // of minting a fresh duplicate review. Editing/changing the actor/route/candidate invalidates
            // the old review (no retained match -> a new authorization is requested). Before any Begin we
            // observe the GENUINE typed authorization state so a still-pending review is kept, a genuine
            // denial/block/missing invalidates it, and a repeat never mints a duplicate authorization.
            var retained = TryGetPending(key);
            string? approvalRequestId = null;
            if (retained is not null && retained.Matches(currentActor2, route2, selected.Model, reasoningId))
            {
                var observationTask = _routeProvider.GetApprovalAuthorizationAsync(retained.ApprovalRequestId, cancellationToken);
                var auth = observationTask is not null ? await observationTask.ConfigureAwait(false) : null;
                if (auth is not null && auth.State is HomePermissionRequestState.PendingApproval)
                {
                    // Still genuinely awaiting review: keep the SAME retained payload, publish no success,
                    // and do not reauthorize, Begin, or mutate the canonical route.
                    return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                        "The reasoning-level change awaits review in Home.", reasoningId ?? $"model:{modelId}", false));
                }
                if (auth is not null && auth.IsAllowed)
                {
                    // Approved (or already executing): resume the SAME review/payload/revision/actor/broker.
                    approvalRequestId = retained.ApprovalRequestId;
                }
                else
                {
                    // Genuine denial, blockage, cancellation, or missing request invalidates the retained review.
                    ClearPending(key);
                    return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                        "The reasoning-level review is no longer available; request approval again.", reasoningId ?? $"model:{modelId}", false));
                }
            }

            var edit = BuildRouteEdit(route2, selected.Model, reasoningId, currentActor2, approvalRequestId);
            var outcome = await _routeProvider.UpdateRouteForActorAsync(currentActor2, edit, cancellationToken);

            if (outcome.Succeeded)
            {
                ClearPending(key);
                if (!string.IsNullOrWhiteSpace(reasoningId)) _selected.TrySet(key, reasoningId);
                NotifyRouteCommitted();
                // A committed canonical save updates selection once; an unconfirmed audit trail is retained
                // through the SAME owner and is finished/retried separately (never CAS).
                if (outcome.Value is { PendingAuditRequestId: { } committedAuditPending })
                    SetAudit(key, new PendingAuditPayload(committedAuditPending));
                return new OperationResult<string>(reasoningId, null);
            }

            // An outcome that retained a sealed audit request (SavedAuditPending / OutcomeUnconfirmed /
            // conflict-after-claim) invalidates any pending approval and blocks replay until resolved. No
            // success is published: audit completion does not prove the canonical save succeeded.
            if (outcome.Value is { PendingAuditRequestId: { } auditPending })
            {
                ClearPending(key);
                SetAudit(key, new PendingAuditPayload(auditPending));
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                    outcome.Message, $"model:{modelId}", false));
            }

            // A fresh pending review was created: retain exactly the payload we just submitted so the
            // next commit (post-approval) resumes it. No memory write and no route-commit notification.
            if (string.Equals(outcome.Code, "ApprovalRequired", StringComparison.Ordinal)
                && outcome.Value is { PendingApprovalRequestId: { } newPending })
            {
                SetPending(key, new PendingApprovalPayload(newPending, edit.ExpectedRevision,
                    currentActor2, route2, selected.Model, reasoningId));
                return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                    "Review this reasoning-level change in Home before it can be saved.", reasoningId ?? $"model:{modelId}", false));
            }

            // Any other refusal invalidates the retained review: reload and request approval again.
            ClearPending(key);
            return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                outcome.Message, $"model:{modelId}", false));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidState,
                "The selection commit was cancelled.", $"model:{modelId}", false));
        }
    }

    /// <summary>Builds the guarded route edit that persists the matched candidate's reasoning-level default
    /// (set to <paramref name="reasoningId"/> or null to clear) for the genuine current route revision. It
    /// preserves order/enabled, carries the optional retained approval request id (so a later commit resumes
    /// the SAME review), and never invents a provider/model/artifact or a second store.</summary>
    private static HomeModelRouteEdit BuildRouteEdit(
        ConfiguredModelRoute route, ModelIdentity selectedModel, string? reasoningId, AuthenticatedResourceActor actor,
        string? approvalRequestId = null)
    {
        var candidates = route.Candidates.Select(item => new HomeModelRouteCandidate(
            item.Model.ProviderId, item.Model.ModelId, item.Model.ArtifactRevision,
            item.Enabled, item.Order, item.ReasoningLevelId)).ToArray();
        var index = Array.FindIndex(candidates, candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.ProviderId, selectedModel.ProviderId)
            && StringComparer.OrdinalIgnoreCase.Equals(candidate.ModelId, selectedModel.ModelId)
            && (candidate.ArtifactRevision is null
                || StringComparer.Ordinal.Equals(candidate.ArtifactRevision, selectedModel.ArtifactRevision)));
        if (index >= 0)
            candidates[index] = candidates[index] with { ReasoningLevelId = reasoningId };

        var contract = new HomeModelRouteContract(
            route.RouteId, route.Revision + 1, "User", route.Category.ToString(), null, null,
            Array.AsReadOnly(candidates), JsonSerializer.SerializeToElement(route.Policy), actor.ProfileId);
        return new HomeModelRouteEdit(contract, route.Revision) { ApprovalRequestId = approvalRequestId };
    }
}
