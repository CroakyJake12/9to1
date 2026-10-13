using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Original local Space graph ownership. Every selection is privately
/// issued after the same registry has acknowledged its pending intent. Store
/// ownership, publication and association remain three separate admissions.</summary>
public sealed partial class HomeSpaceGraphOwner : IGraphPublicationOriginAuthority
{
    public const string AssociateAction = "spaces.graph.associate";
    public static string CapabilityProfileForPage(SpaceGraphPage page) => page switch
    {
        SpaceGraphPage.Harness => "spaces.harness.v1",
        SpaceGraphPage.Model => "spaces.model.v1",
        SpaceGraphPage.UserMessage => "spaces.user-message.v1",
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };
    private readonly FileHomeCoreStateStore _home;
    private readonly HomeLocalProfileIdentity _actors;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeResourceOperationBroker _broker;
    private readonly VersionedAtomicSettingsStore _settings;
    private readonly SpaceRegistry _spaces;
    private readonly SpaceLocalStoreAuthority _spaceAuthority;
    private readonly HomeVersionedNodeGraphRepository _graphs;
    private readonly NodeGraphSchemaRegistry _schemas;
    private readonly HomeGraphPublicationResourceRegistry _graphResources;
    private readonly HomeGraphPublicationOwner _publisher;
    private readonly Func<bool> _hostAllows;
    private readonly ConcurrentDictionary<Operation, byte> _operations = new();
    private readonly object _capacity = new();
    private int _reserved;
    private readonly HashSet<Guid> _reservedSpaces = [];

    public HomeSpaceGraphOwner(FileHomeCoreStateStore home, HomeLocalProfileIdentity actors,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker broker,
        ResourceAuthorizationService canonicalResources, HomePermissionTrustService permissions, VersionedAtomicSettingsStore settings, SpaceRegistry spaces,
        SpaceLocalStoreAuthority spaceAuthority, HomeVersionedNodeGraphRepository graphs,
        NodeGraphSchemaRegistry schemas, HomeSpaceGraphResourceRegistry resources,
        HomeGraphPublicationResourceRegistry graphResources, Func<bool> hostAllows)
    {
        ArgumentNullException.ThrowIfNull(hostAllows);
        if (!actors.IsBoundToStore(home) || !ownership.IsBoundTo(home, actors) || !graphs.IsBoundToStore(home) ||
            !permissions.IsBoundToStore(home) || !broker.IsBoundToOriginalComposition(canonicalResources, permissions) ||
            !canonicalResources.IsBoundToActorSource(actors) || !canonicalResources.HasSingleOriginalGraphResolver(resources) ||
            !canonicalResources.HasSingleOriginalGraphResolver(graphResources) ||
            settings is not IVersionedSettingsGuardedCompareExchange || !spaces.IsBoundToOriginalSettings(settings) ||
            !spaceAuthority.IsBoundToOriginalComposition(settings, actors, ownership))
            throw new UnauthorizedAccessException("The original Home, Space registry and guarded settings owner must be identical.");
        _home = home; _actors = actors; _ownership = ownership; _broker = broker;
        _settings = settings; _spaces = spaces; _spaceAuthority = spaceAuthority;
        _graphs = graphs; _schemas = schemas; _graphResources = graphResources; _hostAllows = hostAllows;
        _publisher = new(home, actors, ownership, graphs, broker, permissions, [this], graphResources);
        spaces.BindOriginalGraphAuthority(spaceAuthority);
        resources.BindOriginal(this);
    }

    public string OwnerAppId => "spaces";
    public bool IsBoundToGraphRepository(IVersionedNodeGraphRepository actualRepository) => ReferenceEquals(actualRepository, _graphs);

    public async Task<Operation> PrepareAsync(Guid spaceId, long expectedSpaceRevision, SpaceGraphPage page,
        GraphPublicationKind kind, GraphDocument proposedGraph, string originalSessionId,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(proposedGraph);
        if (!Enum.IsDefined(page) || expectedSpaceRevision < 1 || expectedSpaceRevision == long.MaxValue ||
            string.IsNullOrWhiteSpace(originalSessionId)) throw new ArgumentException("An exact Space/page/session is required.");
        if (proposedGraph.CapabilityProfileId != CapabilityProfileForPage(page))
            throw new ArgumentException("The canonical graph capability profile must match the selected Space page.");
        DemandHost();
        lock (_capacity)
        {
            if (_reserved == 128 || !_reservedSpaces.Add(spaceId))
                throw new InvalidOperationException("Retire the original Space graph operation before opening another for that Space.");
            _reserved++;
        }
        var transferred = false;
        try
        {
            var actor = await RequireActorAsync(token).ConfigureAwait(false);
            _ = await _spaceAuthority.CaptureWriteAdmissionForActorAsync(actor, token).ConfigureAwait(false);
            var identity = await _settings.GetStoreIdentityAsync(token).ConfigureAwait(false);
            var current = await _spaces.ReadExistingAsync(spaceId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original Space no longer exists.");
            if (current.Revision != expectedSpaceRevision || current.IsBuiltIn || current.IsArchived || current.GraphConfiguration?.Pending is not null)
                throw new InvalidOperationException("The original editable Space revision is required.");
            var prior = current.GraphConfiguration?.GetReference(page);
            var owner = new GraphOwnerDescriptor("spaces", identity.StoreId, "space.definition", spaceId);
            ResourceScope[] scopes = [new("space.definition", EntityScope(owner), Revision(expectedSpaceRevision + 1), ResourceAccess.Write),
                new("nodegraph.definition", actor.ProfileId + "/" + proposedGraph.GraphId.ToString("D"), Revision(prior?.DraftRevision ?? 0), ResourceAccess.Write)];
            var intent = GraphPublicationIntent.Capture(owner, actor, Guid.NewGuid(), kind, proposedGraph,
                prior?.DraftRevision ?? 0, scopes, _schemas, new(4 * 1024 * 1024, 32));
            DemandHost();
            var pending = await _spaces.ReserveGraphPublicationAsync(spaceId, expectedSpaceRevision, page, intent, token).ConfigureAwait(false);
            // A lost acknowledgement above retains the durable pending intent and
            // returns no selection. Never invent a successful reserve or retry it.
            var operation = new Operation(this, intent, pending.GraphConfiguration!.Pending!, originalSessionId, actor, recovery: false);
            if (!_operations.TryAdd(operation, 0)) throw new InvalidOperationException("Original Space graph issuance failed.");
            transferred = true;
            try { operation.AttachPublication(_publisher.Prepare(intent, operation, "spaces", originalSessionId)); }
            catch
            {
                _operations.TryRemove(operation, out _);
                transferred = false;
                throw; // Keep durable pending evidence; no rollback guess.
            }
            return operation;
        }
        finally { if (!transferred) lock (_capacity) { _reserved--; _reservedSpaces.Remove(spaceId); } }
    }

    public ValueTask RequireCurrentAsync(IOriginalGraphOwnerSelection originalSelection, GraphPublicationIntent intent,
        CancellationToken token) => RequireOriginalAsync(originalSelection, intent, null, token, publicationOrigin: true);
    public ValueTask RequirePublishedAsync(IOriginalGraphOwnerSelection originalSelection, GraphPublicationIntent intent,
        GraphPublicationReceipt receipt, CancellationToken token) => RequireOriginalAsync(originalSelection, intent, receipt, token, publicationOrigin: true);

    private async ValueTask RequireOriginalAsync(IOriginalGraphOwnerSelection selection, GraphPublicationIntent intent,
        GraphPublicationReceipt? receipt, CancellationToken token, bool publicationOrigin = false)
    {
        DemandHost();
        if (selection is not Operation operation || !ReferenceEquals(operation.Owner, this) ||
            !_operations.ContainsKey(operation) || operation.IsRetired || !ReferenceEquals(operation.Intent, intent) || publicationOrigin && operation.IsRecovery)
            throw new UnauthorizedAccessException("The original privately issued Space graph selection is unavailable.");
        if (await RequireActorAsync(token).ConfigureAwait(false) != operation.CurrentActor)
            throw new UnauthorizedAccessException("The original Space graph actor changed.");
        _ = await _spaceAuthority.CaptureWriteAdmissionForActorAsync(operation.CurrentActor, token).ConfigureAwait(false);
        var current = await _spaces.ReadExistingAsync(intent.Owner.EntityId, token).ConfigureAwait(false);
        if (current is null || current.IsBuiltIn || current.IsArchived || current.Revision != operation.Pending.PendingSpaceRevision ||
            current.GraphConfiguration?.Pending is not { } pending || !SamePending(pending, operation.Pending) ||
            receipt is not null && !ReceiptMatches(pending, receipt))
            throw new UnauthorizedAccessException("The exact acknowledged Space/page/pending intent changed.");
        if (await RequireActorAsync(token).ConfigureAwait(false) != operation.CurrentActor)
            throw new UnauthorizedAccessException("The original Space graph actor changed.");
        DemandHost();
    }

    internal async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope scope, CancellationToken token)
    {
        var matches = _operations.Keys.Where(operation => operation.MatchesSpaceResource(actor, action, scope)).ToArray();
        if (matches.Length != 1) return new(false, "SPACE_GRAPH_ORIGINAL_SELECTION_UNAVAILABLE", actor.ActorId, scope.Revision, actor.OrganisationId);
        try
        {
            await RequireOriginalAsync(matches[0], matches[0].Intent, null, token).ConfigureAwait(false);
            return new(true, "SPACE_GRAPH_ORIGINAL_SELECTION_CURRENT", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidDataException)
        { return new(false, "SPACE_GRAPH_ORIGINAL_SELECTION_REJECTED", actor.ActorId, scope.Revision, actor.OrganisationId); }
    }

    private void DemandHost()
    {
        if (!HostAllows()) throw new UnauthorizedAccessException("The original Space graph host is retiring or unavailable.");
    }
    private bool HostAllows() => _hostAllows() && _spaceAuthority.IsOriginalHostAcceptingWrites();
    private async ValueTask<AuthenticatedResourceActor> RequireActorAsync(CancellationToken token)
    {
        var actor = await _actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("The actual OS-local Home profile is required.");
        return actor;
    }
    private static string Revision(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string EntityScope(GraphOwnerDescriptor owner) => owner.StoreId.ToString("D") + "/" + owner.EntityId.ToString("D");
    private static bool SamePending(SpaceGraphPendingPublication left, SpaceGraphPendingPublication right) =>
        left.OperationId == right.OperationId && left.Page == right.Page && left.PendingSpaceRevision == right.PendingSpaceRevision &&
        left.Owner == right.Owner && left.OriginalActor == right.OriginalActor && left.ArgumentsDigest == right.ArgumentsDigest &&
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static bool ReceiptMatches(SpaceGraphPendingPublication pending, GraphPublicationReceipt receipt) =>
        receipt.OperationId == pending.OperationId && receipt.ArgumentsDigest == pending.ArgumentsDigest && receipt.Kind == pending.Kind &&
        receipt.Owner == pending.Owner && receipt.GraphId == pending.GraphId && receipt.ExpectedGraphRevision == pending.ExpectedGraphRevision &&
        receipt.CommittedGraphRevision == (pending.Kind == GraphPublicationKind.SaveDraft ? checked(pending.ExpectedGraphRevision + 1) : pending.ExpectedGraphRevision);

    public sealed partial class Operation : IOriginalGraphOwnerSelection
    {
        internal readonly HomeSpaceGraphOwner Owner;
        internal readonly GraphPublicationIntent Intent;
        internal readonly SpaceGraphPendingPublication Pending;
        internal readonly AuthenticatedResourceActor CurrentActor;
        internal readonly bool IsRecovery;
        internal PublicationEvidence? CapturedEvidence => _observation;
        internal HomeGraphPublicationOwner.Review PublicationForEvidence => Publication;
        internal void SetRecoveredEvidence(PublicationEvidence evidence) => _observation = evidence;
        private readonly string _session;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private HomeGraphPublicationOwner.Review? _publication;
        private PublicationEvidence? _observation;
        private HomeResourcePreparedReview? _association;
        private JsonElement _associationArguments;
        private ResourceScope[] _associationScopes = [];
        private HomeResourceExecutionCapability? _associationCapability;
        private bool _associationReserved, _associationAttempted, _associationRefused, _associated, _auditRecorded;
        private int _retired;
        internal Operation(HomeSpaceGraphOwner owner, GraphPublicationIntent intent, SpaceGraphPendingPublication pending,
            string session, AuthenticatedResourceActor currentActor, bool recovery)
        { Owner = owner; Intent = intent; Pending = pending; _session = session; CurrentActor = currentActor; IsRecovery = recovery; }
        public Guid OperationId => Intent.OperationId;
        public string? PublicationRequestId => _publication?.RequestId;
        public string? AssociationRequestId => _association?.RequestId;
        internal bool IsRetired => Volatile.Read(ref _retired) != 0;
        internal void AttachPublication(HomeGraphPublicationOwner.Review review) => _publication = review;
        private HomeGraphPublicationOwner.Review Publication => _publication ?? throw new InvalidOperationException("Original graph review was not prepared; preserve pending evidence for reconciliation.");
        public Task<HomePreparedReviewObservation> SubmitPublicationAsync(CancellationToken token = default) => Publication.SubmitAsync(token);
        public Task<GraphPublicationReceipt?> PublishAsync(CancellationToken token = default) => Publication.PublishAsync(token);
        public Task<GraphPublicationOutcome> ObservePublicationAsync(CancellationToken token = default) => Publication.GetRetainedOutcomeAsync(token);
        public Task<HomePermissionOperationResult> FinishPublicationAuditAsync(CancellationToken token = default) => Publication.FinishAuditAsync(token);

        public async Task<HomePreparedReviewObservation> SubmitAssociationAsync(CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Owner.RequireOriginalAsync(this, Intent, null, token).ConfigureAwait(false);
                if (_association is null)
                {
                    var observed = await Owner.CaptureEvidenceAsync(this, token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The exact graph publication is not yet observed; do not replay it.");
                    if (!ReceiptMatches(Pending, observed.Receipt)) throw new InvalidDataException("The canonical graph receipt does not match the pending Space intent.");
                    _observation = observed;
                    _associationScopes = [new("space.definition", EntityScope(Intent.Owner), Revision(Pending.PendingSpaceRevision), ResourceAccess.Write),
                        new("nodegraph.definition", CurrentActor.ProfileId + "/" + Pending.GraphId.ToString("D"), Revision(observed.Receipt.CommittedGraphRevision), ResourceAccess.Read)];
                    _associationArguments = JsonSerializer.SerializeToElement(new { SchemaVersion = 1, Pending.OperationId, Pending.Owner, Pending.Page,
                        Pending.PendingSpaceRevision, Pending.PriorReference, GraphPublication = observed.Receipt });
                    _association = Owner._broker.PrepareReviewForActor(CurrentActor, "spaces", AssociateAction,
                        _associationScopes, _associationArguments, "Associate this exact published graph with the selected Space page", null, _session);
                    Owner._graphResources.RegisterSpaceAssociation(this);
                }
                return await Owner._broker.AuthorizePreparedReviewAsync(_association, token).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        internal bool MatchesSpaceResource(AuthenticatedResourceActor actor, string action, ResourceScope scope) =>
            !IsRetired && actor == CurrentActor && scope.Kind == "space.definition" &&
            (!IsRecovery && action == (Intent.Kind == GraphPublicationKind.SaveDraft ? "spaces.graph.save-draft" : "spaces.graph.activate") && Intent.Scopes.Contains(scope) ||
             action == AssociateAction && _association is not null && _associationScopes.Contains(scope));
        internal bool MatchesGraphAssociation(AuthenticatedResourceActor actor, string action, ResourceScope scope) =>
            !IsRetired && action == AssociateAction && actor == CurrentActor && _association is not null &&
            scope.Kind == "nodegraph.definition" && scope.Access == ResourceAccess.Read && _associationScopes.Contains(scope);
        internal async ValueTask<bool> VerifyGraphAssociationAsync(CancellationToken token)
        {
            await Owner.RequireOriginalAsync(this, Intent, _observation?.Receipt, token).ConfigureAwait(false);
            var read = await Owner._home.ReadAsync(token).ConfigureAwait(false);
            return read.IsSuccess && _observation is not null && Owner.IsEvidenceCurrentInState(_observation, CurrentActor, read.State!);
        }
    }
}

/// <summary>Private deferred owner binding avoids constructing Home's broker recursively.
/// Missing, substituted or duplicate registration denies. It is not an approval.</summary>
public sealed class HomeSpaceGraphResourceRegistry : ICanonicalResourceAccessResolver
{
    private HomeSpaceGraphOwner? _owner;
    public string ResourceKind => "space.definition";
    internal void BindOriginal(HomeSpaceGraphOwner owner)
    {
        if (Interlocked.CompareExchange(ref _owner, owner, null) is not null)
            throw new InvalidOperationException("One original Space graph owner is required.");
    }
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken) => _owner?.EvaluateAsync(actor, actionId, scope, cancellationToken)
        ?? ValueTask.FromResult(new ResourceAccessDecision(false, "SPACE_GRAPH_OWNER_UNAVAILABLE", actor.ActorId, scope.Revision, actor.OrganisationId));
}
public sealed class HomeSpaceGraphActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "spaces" &&
        actionId is "spaces.graph.save-draft" or "spaces.graph.activate" or HomeSpaceGraphOwner.AssociateAction
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
