using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Same-composition observations of actual guarded Home graph records.
/// Neither serialized inception metadata nor a caller-supplied receipt issues an association.</summary>
public sealed class HomeGraphCreationAssociationSource
{
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeVersionedNodeGraphRepository _graphs;
    private readonly HomeGraphPublicationOwner _publisher;
    private readonly HomeResourceOperationBroker _broker;
    private readonly ICanonicalAutomationDefinitionOriginalWriteSource _creator;
    private readonly HomeCanonicalAutomationDefinitionWriteSource _writes;
    private readonly ConditionalWeakTable<Observation, Observation> _observations = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<HomeGraphPublicationOwner.Review,
        (GraphPublicationIntent Intent, IOriginalGraphOwnerSelection Selection)> _graphReviews = new();
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalChangeIntent, Observation> _intents = new();
    public HomeGraphCreationAssociationSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, HomeVersionedNodeGraphRepository graphs,
        HomeGraphPublicationOwner publisher, HomeResourceOperationBroker broker,
        HomeCanonicalAutomationDefinitionWriteSource writes, ICanonicalAutomationDefinitionOriginalWriteSource creator)
    {
        if (!profiles.IsBoundToStore(store) || !ownership.IsBoundTo(store, profiles) || !graphs.IsBoundToStore(store) ||
            !publisher.IsBoundToAssociationComposition(store, profiles, ownership, broker) ||
            !writes.HasOriginalComposition(store, profiles, creator))
            throw new UnauthorizedAccessException("The SAME actual Home graph/SQLite creator composition is required.");
        _store = store; _profiles = profiles; _ownership = ownership; _graphs = graphs;
        _publisher = publisher; _broker = broker; _creator = creator; _writes = writes;
    }
    public bool IsBoundToOriginalProducer(HomeLocalProfileIdentity profiles,
        ICanonicalAutomationDefinitionOriginalWriteSource creator, HomeCanonicalAutomationDefinitionWriteSource writes) =>
        ReferenceEquals(_profiles, profiles) && ReferenceEquals(_creator, creator) && ReferenceEquals(_writes, writes);
    public bool IsBoundToGraphRepository(IVersionedNodeGraphRepository graphs) => ReferenceEquals(_graphs, graphs);
    public HomeGraphPublicationOwner.Review Prepare(GraphPublicationIntent intent, IOriginalGraphOwnerSelection original)
    {
        if (_creator is not IGraphPublicationOriginAuthority || _graphReviews.Count >= 128)
            throw new InvalidOperationException("The actual creation graph origin or bounded review custody is unavailable.");
        var review = _publisher.Prepare(intent, original, "canonical.sqlite", "automation-create-graph:" + intent.OperationId.ToString("N"));
        if (!_graphReviews.TryAdd(review, (intent, original))) throw new InvalidOperationException("Actual graph inception review retention failed.");
        return review;
    }
    private readonly object _entityGate = new();
    private readonly List<Task> _entityOriginals = [];
    internal Task<ResourceAccessDecision> EvaluateOriginalGraphEntityWithinSourceAsync(AuthenticatedResourceActor actor,
        ResourceScope scope, Action<Action> callback, Action<Task> retain, CancellationToken token)
    {
        var sources = new HomeOwnershipOriginalSourceCallbacks(callback, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ResourceAccessDecision> original;
        lock (_entityGate)
        {
            foreach (var completed in _entityOriginals.Where(task => task.IsCompletedSuccessfully).ToArray())
            { completed.GetAwaiter().GetResult(); _entityOriginals.Remove(completed); }
            if (_entityOriginals.Count >= 1024) throw new InvalidOperationException("Original graph entity-source custody is full.");
            original = Drive(begin.Task); _entityOriginals.Add(original);
        }
        try { sources.Run(() => retain(original)); }
        finally { begin.TrySetResult(); }
        return original;
        async Task<ResourceAccessDecision> Drive(Task start)
        {
            await start.ConfigureAwait(false);
            var matches = sources.Invoke(() => _graphReviews.Values.Where(value => value.Intent.OriginalActor == actor && value.Intent.Scopes.Contains(scope) &&
                scope.Kind == "automation.definition" && scope.Access == ResourceAccess.Write).ToArray());
            ResourceAccessDecision result;
            if (matches.Length != 1 || _creator is not IGraphPublicationOriginAuthority actual)
                result = new(false, "HOME_AUTOMATION_CREATION_ORIGIN_REQUIRED", actor.ActorId, scope.Revision, actor.OrganisationId);
            else
            {
                await sources.ReadAsync<object?>(async () =>
                {
                    await actual.RequireCurrentAsync(matches[0].Selection, matches[0].Intent, token).ConfigureAwait(false);
                    return null;
                }).ConfigureAwait(false);
                result = new(await sources.ReadAsync(() => _profiles.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) == actor,
                    "HOME_AUTOMATION_CREATION_ORIGIN_CHECKED", actor.ActorId, scope.Revision, actor.OrganisationId);
            }
            if (!sources.HasIndependentlyJoinedHealthySources) throw new AggregateException("Original graph entity-source callbacks did not settle.", sources.Errors);
            return result;
        }
    }
    public async Task JoinOriginalEntitySourcesAsync()
    {
        Task[] originals; lock (_entityGate) originals = _entityOriginals.ToArray();
        var failures = new List<Exception>();
        foreach (var task in originals) try { await task.ConfigureAwait(false); } catch (Exception cause) { failures.Add(task.Exception ?? cause); }
        if (failures.Count != 0) throw new AggregateException("Original graph entity-source tasks did not settle successfully.", failures);
    }
    public async Task<bool> RetirePublishedReviewAsync(HomeGraphPublicationOwner.Review review)
    {
        if (!_graphReviews.ContainsKey(review)) return true;
        if (!await review.RetireAsync().ConfigureAwait(false)) return false;
        return _graphReviews.TryRemove(review, out _);
    }
    public sealed class Observation
    {
        internal readonly HomeGraphCreationAssociationSource Issuer;
        internal readonly HomeCoreStateRecord Record;
        internal readonly byte[] Fingerprint;
        internal readonly VerifiedResourceStoreOwnership Ownership;
        internal readonly OwnedGraphDefinition Stored;
        internal Observation(HomeGraphCreationAssociationSource issuer, AuthenticatedResourceActor actor,
            HomeCoreStateRecord record, OwnedGraphDefinition graph, VerifiedResourceStoreOwnership ownership, bool graphAuditRecorded)
        { Issuer = issuer; Actor = actor; Record = record; Stored = graph; Ownership = ownership;
          Fingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record)); GraphAuditRecorded = graphAuditRecorded; }
        public bool GraphAuditRecorded { get; }
        public AuthenticatedResourceActor Actor { get; }
        public GraphOwnerCreationOrigin Origin => Stored.CreationOrigin!;
        public GraphPublicationReceipt Publication => Stored.LastPublication!;
        public GraphDocument Graph => JsonSerializer.Deserialize<GraphDocument>(JsonSerializer.Serialize(Stored.Draft))!;
        public string FingerprintSha256 => Convert.ToHexString(Fingerprint);
    }
    public bool IsIssued(Observation value) => ReferenceEquals(value.Issuer, this) &&
        _observations.TryGetValue(value, out var actual) && ReferenceEquals(actual, value);
    private static bool SamePrincipal(AuthenticatedResourceActor left, AuthenticatedResourceActor right) =>
        left.ProfileId == right.ProfileId && left.ActorId == right.ActorId && left.AccountId == right.AccountId &&
        left.OrganisationId == right.OrganisationId;
    private static bool ValidOrigin(OwnedGraphDefinition value, Guid store, AuthenticatedResourceActor actor)
    {
        var origin = value.CreationOrigin; var receipt = value.LastPublication; var owner = value.CanonicalOwner;
        return origin is not null && origin.SchemaVersion == 1 && origin.StoreId == store &&
            origin.AutomationId != Guid.Empty && origin.DefinitionOperationId != Guid.Empty && origin.GraphOperationId != Guid.Empty &&
            new[] { origin.AutomationId, value.Draft.GraphId, origin.DefinitionOperationId, origin.GraphOperationId }.Distinct().Count() == 4 &&
            origin.OriginalActor is not null && SamePrincipal(origin.OriginalActor, actor) && origin.CapturedAt != default &&
            !string.IsNullOrWhiteSpace(origin.Name) && origin.Name.Length <= 256 && origin.OriginalAbsenceSha256 is { Length: 64 } &&
            origin.OriginalAbsenceSha256.All(Uri.IsHexDigit) && owner is not null &&
            owner.AppId == "automations" && owner.StoreId == store && owner.EntityKind == "automation.definition" && owner.EntityId == origin.AutomationId &&
            value.OwnerAppId == owner.AppId && value.OwnerEntityId == owner.CanonicalEntityId &&
            value.Draft.SchemaVersion == GraphDocument.CurrentSchemaVersion && value.Draft.Revision == 1 &&
            value.Draft.State == GraphRevisionState.Draft && value.Active is null && value.ActivatedRevisions.Count == 0 &&
            value.Draft.Nodes.Count == 0 && value.Draft.Connections.Count == 0 &&
            value.Draft.CapabilityProfileId == "automations.empty-draft.v1" &&
            receipt is not null && receipt.Owner == owner && receipt.Kind == GraphPublicationKind.SaveDraft &&
            receipt.GraphId == value.Draft.GraphId && receipt.OperationId == origin.GraphOperationId &&
            receipt.ExpectedGraphRevision == 0 && receipt.CommittedGraphRevision == 1 &&
            receipt.ArgumentsDigest is { Length: 64 } && receipt.ArgumentsDigest.All(Uri.IsHexDigit);
    }
    public async Task<IReadOnlyList<Observation>> ReadCurrentAsync(AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity, CancellationToken token)
    {
        if (await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The original current Home actor is required for graph recovery.");
        var ownership = await _ownership.GetVerifiedAsync("canonical.sqlite", identity.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (ownership is null || !await _ownership.IsCurrentAsync(ownership, actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual canonical SQLite ownership receipt is required.");
        var records = await _graphs.ReadOriginalCreationRecordsAsync(token).ConfigureAwait(false);
        var result = new List<Observation>();
        foreach (var record in records)
        {
            var graph = HomeVersionedNodeGraphRepository.DecodeOriginalCreationRecord(record);
            if (graph.CreationOrigin is null || graph.CreationOrigin.StoreId != identity.StoreId ||
                (graph.CreationOrigin.OriginalActor is null || !SamePrincipal(graph.CreationOrigin.OriginalActor, actor))) continue;
            if (!ValidOrigin(graph, identity.StoreId, actor) ||
                !await _broker.IsOriginalAutomationCreationGraphPublicationAsync(graph, actor, token).ConfigureAwait(false))
                throw new InvalidDataException("A stored creation publication is superseded or inconsistent. Preserve it for explicit recovery; no creation replay was admitted.");
            if (result.Count == 128) throw new InvalidDataException("The bounded creation recovery page is full; no records were adopted.");
            var observation = new Observation(this, actor, record, graph, ownership,
                await _broker.IsOriginalAutomationCreationGraphAuditRecordedAsync(graph, token).ConfigureAwait(false));
            _observations.Add(observation, observation); result.Add(observation);
        }
        if (result.Select(value => value.Origin.AutomationId).Distinct().Count() != result.Count ||
            result.Select(value => value.Origin.DefinitionOperationId).Distinct().Count() != result.Count ||
            result.Select(value => value.Origin.GraphOperationId).Distinct().Count() != result.Count)
            throw new InvalidDataException("Duplicate actual creation origins must remain unconfirmed.");
        if (await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor ||
            !await _ownership.IsCurrentAsync(ownership, actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual Home graph actor or ownership changed during observation.");
        return result.AsReadOnly();
    }
    public async Task<Observation?> CaptureAsync(AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        Guid graph, Guid automation, CancellationToken token) =>
        (await ReadCurrentAsync(actor, identity, token).ConfigureAwait(false)).SingleOrDefault(value =>
            value.Graph.GraphId == graph && value.Origin.AutomationId == automation);
    public void BindIntent(ICanonicalAutomationDefinitionOriginalChangeIntent intent, Observation observation)
    {
        if (!_creator.IsIssuedOriginalChangeIntent(intent) || !IsIssued(observation) || intent.Actor != observation.Actor ||
            intent.OriginalStoreIdentity.StoreId != observation.Origin.StoreId || intent.ProposedDefinition.Id != observation.Origin.AutomationId ||
            intent.ProposedDefinition.GraphBinding != new Haven.Core.AutomationDefinitionGraphBinding(observation.Graph.GraphId, 1, null) ||
            intent.ChangeKind is not (CanonicalAutomationOriginalChangeKind.CreateDisabled or CanonicalAutomationOriginalChangeKind.RecoverCreated))
            throw new UnauthorizedAccessException("The SAME actual SQL creation intent and observed Home Draft are required.");
        _intents.Add(intent, observation);
    }
    public bool IsIntentGraphCurrentInState(ICanonicalAutomationDefinitionOriginalChangeIntent intent, HomeCoreStoredState state)
    {
        if (!_creator.IsIssuedOriginalChangeIntent(intent) || !_intents.TryGetValue(intent, out var value) ||
            !IsIssued(value) || value.Actor != intent.Actor || !HomeLocalStoreOwnership.IsReceiptCurrentInState(state, value.Ownership)) return false;
        var records = state.Records.Where(record => record.RecordId == value.Record.RecordId).ToArray();
        return records.Length == 1 && SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(records[0])).AsSpan().SequenceEqual(value.Fingerprint);
    }
    public async Task<bool> IsCurrentAsync(Observation observation, CancellationToken token)
    {
        if (!IsIssued(observation) || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != observation.Actor ||
            !await _ownership.IsCurrentAsync(observation.Ownership, observation.Actor, token).ConfigureAwait(false)) return false;
        var records = await _graphs.ReadOriginalCreationRecordsAsync(token).ConfigureAwait(false);
        var current = records.Where(record => record.RecordId == observation.Record.RecordId).ToArray();
        return current.Length == 1 && SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current[0])).AsSpan().SequenceEqual(observation.Fingerprint) &&
            await _profiles.GetCurrentAsync(token).ConfigureAwait(false) == observation.Actor;
    }
}
public sealed class HomeAutomationCreationGraphActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "automations" && actionId == "automations.graph.save-draft"
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
public sealed partial class HomeVersionedNodeGraphRepository
{
    internal async Task<HomeCoreStateRecord[]> ReadOriginalCreationRecordsAsync(CancellationToken token)
    {
        var read = await store.ReadAsync(token).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidOperationException("Actual Home graph state is unavailable; recovery is unconfirmed.");
        var records = read.State!.Records.Where(record => record.RecordType == RecordType).ToArray();
        if (records.Length > 4096) throw new InvalidDataException("The original graph observation exceeds its bounded read.");
        return records;
    }
    internal static OwnedGraphDefinition DecodeOriginalCreationRecord(HomeCoreStateRecord record)
    {
        if (record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Noncanonical Home graph records cannot supply creation origin.");
        return Decode(record);
    }
}
