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
            value.Draft.CapabilityProfileId is (CanonicalAutomationCoreNodes.EmptyProfileId or CanonicalAutomationCoreNodes.ProfileId) &&
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
            if (!ValidOrigin(graph, identity.StoreId, actor))
            {
                // Later saved revisions are not inception recovery selections. Skip
                // only a freshly verified SAME protected SQL owner/binding, never
                // an unknown, unbound or merely well-shaped graph record.
                if (graph.CanonicalOwner is { } boundOwner && _creator is ICanonicalAutomationOriginalGraphEditSource savedSource &&
                    await savedSource.IsOriginalBoundSavedGraphWithinSourceAsync(actor, identity, boundOwner, graph.Draft,
                        graph.Active?.Revision, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(graph.CreationOrigin))),
                        body => body(), _ => { }, token).ConfigureAwait(false)) continue;
                throw new InvalidDataException("A stored creation publication is superseded or inconsistent. Preserve it for explicit recovery; no creation replay was admitted.");
            }
            if (!await _broker.IsOriginalAutomationCreationGraphPublicationAsync(graph, actor, token).ConfigureAwait(false))
                throw new InvalidDataException("The original inception publication could not be authenticated; no creation replay was admitted.");
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
        if (_creator.IsIssuedOriginalChangeIntent(intent) && _savedPublicationIntents.TryGetValue(intent, out var published))
            return IsPublishedSavedCurrentInState(published, intent.Actor, state);
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
    public sealed class PublishedSavedObservation
    {
        internal readonly HomeGraphCreationAssociationSource Issuer;
        internal readonly SavedObservation Before;
        internal readonly HomeGraphPublicationObservation? Publication;
        internal readonly HomeCoreStateRecord Record;
        internal readonly byte[] Fingerprint;
        internal readonly OwnedGraphDefinition Stored;
        internal readonly IReadOnlyList<ResourceScope> ReviewScopes;
        internal readonly string ArgumentsDigest;
        private readonly SemaphoreSlim _recoveryLifetime = new(1, 1);
        internal PublishedSavedObservation(HomeGraphCreationAssociationSource issuer, SavedObservation before,
            HomeGraphPublicationObservation? publication, HomeCoreStateRecord record, OwnedGraphDefinition stored,
            IReadOnlyList<ResourceScope> reviewScopes, string argumentsDigest)
        { Issuer = issuer; Before = before; Publication = publication; Record = record; Stored = stored;
          ReviewScopes = reviewScopes; ArgumentsDigest = argumentsDigest;
          Fingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record)); }
        public AuthenticatedResourceActor Actor => Before.Actor;
        public Haven.Core.AutomationDefinitionGraphBinding Binding => new(Stored.Draft.GraphId, Stored.Draft.Revision, Stored.Active?.Revision);
        public GraphDocument Graph => JsonSerializer.Deserialize<GraphDocument>(JsonSerializer.Serialize(Stored.Draft))!;
        public GraphPublicationReceipt Receipt => Publication?.Receipt ?? Stored.LastPublication!;
        public string FingerprintSha256 => Convert.ToHexString(Fingerprint);
        internal IAsyncDisposable? TryAcquireRecoveryLifetime()
        {
            if (!_recoveryLifetime.Wait(0)) return null;
            return new RecoveryLifetime(_recoveryLifetime);
        }
        private sealed class RecoveryLifetime(SemaphoreSlim gate) : IAsyncDisposable
        {
            private int _disposed;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
    private readonly ConditionalWeakTable<PublishedSavedObservation, PublishedSavedObservation> _savedPublications = new();
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalChangeIntent, PublishedSavedObservation> _savedPublicationIntents = new();
    public bool IsIssuedPublishedSavedObservation(PublishedSavedObservation value) => ReferenceEquals(value.Issuer, this) &&
        _savedPublications.TryGetValue(value, out var issued) && ReferenceEquals(issued, value);
    public async Task<PublishedSavedObservation> CapturePublishedSavedAsync(SavedObservation before,
        GraphPublicationIntent exactIntent, HomeGraphPublicationObservation samePublication, CancellationToken token)
    {
        if (!IsIssuedSavedObservation(before) || _creator is not ICanonicalAutomationOriginalGraphEditSource source ||
            !source.IsIssuedOriginalSavedGraphRead(before.OriginalRead))
            throw new UnauthorizedAccessException("The SAME original saved graph observation is required.");
        await source.RevalidateOriginalSavedGraphReadWithinSourceAsync(before.OriginalRead, body => body(), _ => { }, token).ConfigureAwait(false);
        var owner = new GraphOwnerDescriptor("automations", before.OriginalRead.OriginalStoreIdentity.StoreId,
            "automation.definition", before.OriginalRead.OriginalDefinition.Id);
        var receipt = samePublication.Receipt;
        if (exactIntent.Owner != owner || exactIntent.OriginalActor != before.Actor || exactIntent.CreationOrigin is not null ||
            exactIntent.Graph.GraphId != before.Graph.GraphId || exactIntent.ExpectedGraphRevision != before.Graph.Revision ||
            receipt.Owner != owner || receipt.OperationId != exactIntent.OperationId || receipt.Kind != exactIntent.Kind ||
            receipt.GraphId != exactIntent.Graph.GraphId || receipt.ExpectedGraphRevision != exactIntent.ExpectedGraphRevision ||
            receipt.CommittedGraphRevision != exactIntent.Graph.Revision ||
            await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != before.Actor)
            throw new UnauthorizedAccessException("The original saved graph and exact publication identity differ.");
        var read = await _store.ReadAsync(token).ConfigureAwait(false);
        if (!read.IsSuccess || !_publisher.IsObservationCurrentInState(samePublication, before.Actor, read.State!) ||
            !HomeLocalStoreOwnership.IsReceiptCurrentInState(read.State!, before.Ownership))
            throw new UnauthorizedAccessException("The SAME live publisher observation and actual Home ownership/state are required.");
        var record = read.State!.Records.Single(value => value.RecordId == before.Record.RecordId);
        var stored = HomeVersionedNodeGraphRepository.DecodeOriginalCreationRecord(record);
        if (stored.CanonicalOwner != owner || stored.OwnerAppId != before.Stored.OwnerAppId || stored.OwnerEntityId != before.Stored.OwnerEntityId ||
            JsonSerializer.Serialize(stored.CreationOrigin) != JsonSerializer.Serialize(before.Stored.CreationOrigin) ||
            stored.Draft.CapabilityProfileId != before.Stored.Draft.CapabilityProfileId || stored.LastPublication != receipt ||
            JsonSerializer.Serialize(stored.Draft) != JsonSerializer.Serialize(exactIntent.Graph) ||
            (exactIntent.Kind == GraphPublicationKind.SaveDraft ? stored.Active?.Revision != before.ActiveRevision :
                stored.Active is null || stored.Active.Revision != stored.Draft.Revision ||
                JsonSerializer.Serialize(stored.Active with { State = GraphRevisionState.Draft }) != JsonSerializer.Serialize(stored.Draft)))
            throw new UnauthorizedAccessException("The actual published graph changed owner, profile, origin, revisions or proposed contents.");
        var scopes = exactIntent.Scopes;
        var result = new PublishedSavedObservation(this, before, samePublication, record, stored, scopes,
            samePublication.Receipt.ArgumentsDigest);
        if (!await IsPublishedSavedRecordCurrentAsync(result, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual Home publication changed while it was observed.");
        _savedPublications.Add(result, result); return result;
    }

    public async Task<PublishedSavedObservation?> CapturePublishedSavedRecoveryAsync(
        ICanonicalAutomationOriginalSavedGraphRead sameRead, CancellationToken token)
    {
        if (_creator is not ICanonicalAutomationOriginalGraphEditSource source || !source.IsIssuedOriginalSavedGraphRead(sameRead))
            throw new UnauthorizedAccessException("The SAME current protected Automation definition read is required.");
        await source.RevalidateOriginalSavedGraphReadWithinSourceAsync(sameRead, body => body(), _ => { }, token).ConfigureAwait(false);
        var definition = sameRead.OriginalDefinition;
        if (definition.GraphBinding is not { } priorBinding ||
            !CanonicalAutomationGraphReferenceCodec.TryDeserialize(definition.Instruction, out var reference) ||
            reference.Binding != priorBinding)
            throw new InvalidDataException("The protected Automation row has no exact prior graph binding.");
        var owner = new GraphOwnerDescriptor("automations", sameRead.OriginalStoreIdentity.StoreId,
            "automation.definition", definition.Id);
        var expectedScopes = new ResourceScope[]
        {
            new("automation.definition", owner.StoreId.ToString("D") + "/" + owner.EntityId.ToString("D"),
                sameRead.OriginalSnapshotSha256, ResourceAccess.Write),
            new("nodegraph.definition", sameRead.Actor.ProfileId + "/" + priorBinding.GraphId.ToString("D"),
                priorBinding.DraftRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write)
        };
        var ownership = await _ownership.GetVerifiedAsync("canonical.sqlite", owner.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (ownership is null || !await _ownership.IsCurrentAsync(ownership, sameRead.Actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The current actual canonical SQLite owner receipt is required.");
        var records = await _graphs.ReadOriginalCreationRecordsAsync(token).ConfigureAwait(false);
        var matches = records.Where(value => value.RecordId == "home.node-graph:" + priorBinding.GraphId.ToString("D")).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The exact Home graph record is missing or ambiguous.");
        var record = matches[0]; var stored = HomeVersionedNodeGraphRepository.DecodeOriginalCreationRecord(record);
        if (stored.CanonicalOwner != owner || stored.OwnerAppId != owner.AppId || stored.OwnerEntityId != owner.CanonicalEntityId ||
            stored.CreationOrigin is not { } currentOrigin || currentOrigin.StoreId != owner.StoreId ||
            currentOrigin.AutomationId != owner.EntityId || currentOrigin.OriginalActor is null ||
            !SamePrincipal(currentOrigin.OriginalActor, sameRead.Actor) ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(currentOrigin))) !=
                sameRead.OriginalCreationOriginSha256)
            throw new InvalidDataException("The Home graph owner or creation origin does not match this Automation.");
        if (stored.Draft.Revision == priorBinding.DraftRevision && stored.Active?.Revision == priorBinding.ActiveRevision)
            return null;
        var currentReceipt = stored.LastPublication;
        if (currentReceipt is null || currentReceipt.OperationId == Guid.Empty || currentReceipt.GraphId != priorBinding.GraphId ||
            currentReceipt.Kind is not (GraphPublicationKind.SaveDraft or GraphPublicationKind.Activate) ||
            currentReceipt.ExpectedGraphRevision != priorBinding.DraftRevision ||
            currentReceipt.Kind == GraphPublicationKind.SaveDraft && currentReceipt.CommittedGraphRevision != priorBinding.DraftRevision + 1 ||
            currentReceipt.Kind == GraphPublicationKind.Activate && currentReceipt.CommittedGraphRevision != priorBinding.DraftRevision)
            throw new InvalidDataException("The durable Home receipt does not extend the exact prior SQL graph revision.");
        var exactIntent = _graphs.CaptureRecoveryIntent(owner, sameRead.Actor, currentReceipt.OperationId,
            currentReceipt.Kind, stored.Draft, currentReceipt.ExpectedGraphRevision, expectedScopes);
        var intentDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(exactIntent.Arguments.GetRawText())));
        var originDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(stored.CreationOrigin)));
        var publicationMatches = stored.CanonicalOwner == owner && stored.OwnerAppId == owner.AppId &&
            stored.OwnerEntityId == owner.CanonicalEntityId && stored.CreationOrigin is { } origin &&
            origin.StoreId == owner.StoreId && origin.AutomationId == owner.EntityId && origin.OriginalActor is not null &&
            SamePrincipal(origin.OriginalActor, sameRead.Actor) && originDigest == sameRead.OriginalCreationOriginSha256 &&
            JsonSerializer.Serialize(stored.Draft) == JsonSerializer.Serialize(exactIntent.Graph) &&
            currentReceipt.OperationId == exactIntent.OperationId &&
            currentReceipt.ArgumentsDigest == intentDigest && currentReceipt.Kind == exactIntent.Kind &&
            currentReceipt.Owner == owner && currentReceipt.GraphId == exactIntent.Graph.GraphId &&
            currentReceipt.ExpectedGraphRevision == exactIntent.ExpectedGraphRevision &&
            currentReceipt.CommittedGraphRevision == exactIntent.Graph.Revision &&
            (exactIntent.Kind == GraphPublicationKind.SaveDraft
                ? stored.Active?.Revision == priorBinding.ActiveRevision
                : stored.Active is { } active && active.Revision == exactIntent.Graph.Revision &&
                    active.State == GraphRevisionState.Active &&
                    JsonSerializer.Serialize(active) == JsonSerializer.Serialize(exactIntent.Graph with { State = GraphRevisionState.Active }) &&
                    stored.ActivatedRevisions.Count(value => value.Revision == active.Revision) == 1 &&
                    (priorBinding.ActiveRevision is null || stored.ActivatedRevisions.Any(value => value.Revision == priorBinding.ActiveRevision)));
        if (!publicationMatches)
            throw new InvalidDataException("The durable Home publication receipt does not match the prior protected SQL binding. No graph or SQL effect was repeated.");
        var originalRequest = await _broker.ReadOriginalAutomationGraphPublicationRequestAsync(stored, sameRead.Actor,
            expectedScopes, intentDigest, token).ConfigureAwait(false);
        if (originalRequest is null)
            throw new InvalidDataException("The original Home graph request does not match its durable receipt, actor, action, policy, scopes, or impact hash. No graph or SQL effect was repeated.");
        if (originalRequest.State == HomePermissionRequestState.Executing)
            throw new InvalidDataException("Home published the graph, but its original permission audit is still Executing. Binding recovery is blocked until that audit is reconciled.");
        if (originalRequest.State != HomePermissionRequestState.Succeeded || originalRequest.ResultCode != "HOME_GRAPH_PUBLISHED")
            throw new InvalidDataException($"The original Home graph request is {originalRequest.State} with result {originalRequest.ResultCode}; no binding recovery or graph replay was admitted.");
        if (await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != sameRead.Actor ||
            !await _ownership.IsCurrentAsync(ownership, sameRead.Actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual Home actor or canonical store ownership changed during recovery observation.");
        var before = new SavedObservation(this, sameRead, record, stored, ownership);
        _savedObservations.Add(before, before);
        var result = new PublishedSavedObservation(this, before, null, record, stored, expectedScopes, intentDigest);
        if (!await IsPublishedSavedRecordCurrentAsync(result, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The exact audited Home publication changed during recovery observation.");
        _savedPublications.Add(result, result); return result;
    }
    private bool IsPublishedSavedCurrentInState(PublishedSavedObservation value, AuthenticatedResourceActor actor, HomeCoreStoredState state)
    {
        if (!IsIssuedPublishedSavedObservation(value) || value.Actor != actor ||
            value.Publication is { } publication && !_publisher.IsObservationCurrentInState(publication, actor, state) ||
            value.Publication is null && !_broker.IsOriginalAutomationGraphPublicationInState(value.Stored, actor,
                value.ReviewScopes, value.ArgumentsDigest, state) ||
            !HomeLocalStoreOwnership.IsReceiptCurrentInState(state, value.Before.Ownership)) return false;
        var records = state.Records.Where(record => record.RecordId == value.Record.RecordId).ToArray();
        return records.Length == 1 && SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(records[0])).AsSpan().SequenceEqual(value.Fingerprint);
    }
    private async Task<bool> IsPublishedSavedRecordCurrentAsync(PublishedSavedObservation value, CancellationToken token)
    {
        var read = await _store.ReadAsync(token).ConfigureAwait(false);
        // During initial capture private issuance is completed only AFTER this
        // exact current record proof; no externally supplied object can reach it.
        if (!read.IsSuccess || value.Actor != await _profiles.GetCurrentAsync(token).ConfigureAwait(false) ||
            value.Publication is { } publication && !_publisher.IsObservationCurrentInState(publication, value.Actor, read.State!) ||
            value.Publication is null && !await _broker.IsOriginalAutomationGraphPublicationAsync(value.Stored, value.Actor,
                value.ReviewScopes, value.ArgumentsDigest, token).ConfigureAwait(false) ||
            !HomeLocalStoreOwnership.IsReceiptCurrentInState(read.State!, value.Before.Ownership)) return false;
        var matches = read.State!.Records.Where(record => record.RecordId == value.Record.RecordId).ToArray();
        return matches.Length == 1 && SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(matches[0])).AsSpan().SequenceEqual(value.Fingerprint);
    }
    public IAsyncDisposable? TryAcquirePublishedSavedIntentLifetime(ICanonicalAutomationDefinitionOriginalChangeIntent intent)
    {
        if (!_creator.IsIssuedOriginalChangeIntent(intent) || !_savedPublicationIntents.TryGetValue(intent, out var value) ||
            !IsIssuedPublishedSavedObservation(value) || intent.Actor != value.Actor) return null;
        if (value.Publication is null) return value.TryAcquireRecoveryLifetime();
        // Actual SQL transaction is already held. Never wait review -> original
        // SQL in reverse order; the existing publisher denies contention at once.
        return value.Publication.Review.TryAcquireAssociationLifetime(value.Publication);
    }
    public Task<bool> IsPublishedSavedCurrentAsync(PublishedSavedObservation value, CancellationToken token) =>
        IsIssuedPublishedSavedObservation(value) ? IsPublishedSavedRecordCurrentAsync(value, token) : Task.FromResult(false);
    public void BindPublishedSavedIntent(ICanonicalAutomationDefinitionOriginalChangeIntent intent, PublishedSavedObservation value)
    {
        if (!_creator.IsIssuedOriginalChangeIntent(intent) || !IsIssuedPublishedSavedObservation(value) ||
            intent.Actor != value.Actor || intent.OriginalStoreIdentity != value.Before.OriginalRead.OriginalStoreIdentity ||
            intent.OriginalDefinition.Id != value.Before.OriginalRead.OriginalDefinition.Id || intent.ProposedDefinition.GraphBinding != value.Binding ||
            (value.Receipt.Kind == GraphPublicationKind.SaveDraft ? intent.ChangeKind != CanonicalAutomationOriginalChangeKind.BindGraphDraftDisabled :
                intent.ChangeKind != CanonicalAutomationOriginalChangeKind.BindGraphActiveDisabled))
            throw new UnauthorizedAccessException("Only the SAME original SQL binding intent and current actual Home publication may be paired.");
        _savedPublicationIntents.Add(intent, value);
    }
    // Saved graph READ is a distinct issuer path. Inception recovery above remains
    // restricted to the original empty revision1 publication and its broker evidence.
    public sealed class SavedObservation
    {
        internal readonly HomeGraphCreationAssociationSource Issuer;
        internal readonly ICanonicalAutomationOriginalSavedGraphRead OriginalRead;
        internal readonly HomeCoreStateRecord Record;
        internal readonly OwnedGraphDefinition Stored;
        internal readonly VerifiedResourceStoreOwnership Ownership;
        internal readonly byte[] Fingerprint;
        internal SavedObservation(HomeGraphCreationAssociationSource issuer, ICanonicalAutomationOriginalSavedGraphRead read,
            HomeCoreStateRecord record, OwnedGraphDefinition stored, VerifiedResourceStoreOwnership ownership)
        {
            Issuer = issuer; OriginalRead = read; Record = record; Stored = stored; Ownership = ownership;
            Fingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record));
        }
        public AuthenticatedResourceActor Actor => OriginalRead.Actor;
        public GraphDocument Graph => JsonSerializer.Deserialize<GraphDocument>(JsonSerializer.Serialize(Stored.Draft))!;
        public long? ActiveRevision => Stored.Active?.Revision;
        public GraphOwnerCreationOrigin Origin => Stored.CreationOrigin!;
        public string FingerprintSha256 => Convert.ToHexString(Fingerprint);
    }
    private readonly ConditionalWeakTable<SavedObservation, SavedObservation> _savedObservations = new();
    public bool IsIssuedSavedObservation(SavedObservation value) => ReferenceEquals(value.Issuer, this) &&
        _savedObservations.TryGetValue(value, out var actual) && ReferenceEquals(value, actual);
    public async Task<SavedObservation> CaptureSavedAsync(ICanonicalAutomationOriginalSavedGraphRead sameRead,
        CancellationToken token)
    {
        if (_creator is not ICanonicalAutomationOriginalGraphEditSource actual || !actual.IsIssuedOriginalSavedGraphRead(sameRead))
            throw new UnauthorizedAccessException("The SAME protected saved-definition READ source is required.");
        await actual.RevalidateOriginalSavedGraphReadWithinSourceAsync(sameRead, body => body(), _ => { }, token).ConfigureAwait(false);
        var actor = sameRead.Actor; var identity = sameRead.OriginalStoreIdentity; var definition = sameRead.OriginalDefinition;
        if (!CanonicalAutomationGraphReferenceCodec.TryDeserialize(definition.Instruction, out var reference) ||
            reference.Binding != definition.GraphBinding)
            throw new InvalidDataException("The actual saved canonical graph reference and private owner binding differ.");
        if (await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The actual Home graph actor changed.");
        var ownership = await _ownership.GetVerifiedAsync("canonical.sqlite", identity.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (ownership is null || !await _ownership.IsCurrentAsync(ownership, actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The SAME actual canonical store ownership receipt is required.");
        var records = await _graphs.ReadOriginalCreationRecordsAsync(token).ConfigureAwait(false);
        var record = records.SingleOrDefault(value => value.RecordId == "home.node-graph:" + reference.GraphId.ToString("D"))
            ?? throw new InvalidDataException("The canonical saved graph record is missing; existing settings were preserved.");
        var stored = HomeVersionedNodeGraphRepository.DecodeOriginalCreationRecord(record);
        var owner = new GraphOwnerDescriptor("automations", identity.StoreId, "automation.definition", definition.Id);
        if (stored.CanonicalOwner != owner || stored.OwnerAppId != owner.AppId || stored.OwnerEntityId != owner.CanonicalEntityId ||
            stored.CreationOrigin is not { } origin || origin.StoreId != identity.StoreId || origin.AutomationId != definition.Id ||
            origin.OriginalActor is null || !SamePrincipal(origin.OriginalActor, actor) ||
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(origin))) != sameRead.OriginalCreationOriginSha256 ||
            stored.Draft.GraphId != reference.GraphId ||
            stored.Draft.Revision != reference.DraftRevision || stored.Active?.Revision != reference.ActiveRevision)
            throw new UnauthorizedAccessException("The saved Home graph and protected definition are not the SAME current owner/revision. Inspect pending original publications without replay.");
        await actual.RevalidateOriginalSavedGraphReadWithinSourceAsync(sameRead, body => body(), _ => { }, token).ConfigureAwait(false);
        if (await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor ||
            !await _ownership.IsCurrentAsync(ownership, actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual saved-graph actor/ownership changed during observation.");
        var observation = new SavedObservation(this, sameRead, record, stored, ownership);
        if (!await IsSavedRecordCurrentAsync(observation, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual graph record changed during its saved READ.");
        _savedObservations.Add(observation, observation); return observation;
    }
    private async Task<bool> IsSavedRecordCurrentAsync(SavedObservation value, CancellationToken token)
    {
        var read = await _store.ReadAsync(token).ConfigureAwait(false);
        if (!read.IsSuccess || !HomeLocalStoreOwnership.IsReceiptCurrentInState(read.State!, value.Ownership)) return false;
        var records = read.State!.Records.Where(record => record.RecordId == value.Record.RecordId).ToArray();
        return records.Length == 1 && SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(records[0])).AsSpan().SequenceEqual(value.Fingerprint);
    }
    public async Task<bool> IsSavedCurrentAsync(SavedObservation value, CancellationToken token)
    {
        if (!IsIssuedSavedObservation(value) || _creator is not ICanonicalAutomationOriginalGraphEditSource actual ||
            !actual.IsIssuedOriginalSavedGraphRead(value.OriginalRead) ||
            await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != value.Actor ||
            !await _ownership.IsCurrentAsync(value.Ownership, value.Actor, token).ConfigureAwait(false)) return false;
        await actual.RevalidateOriginalSavedGraphReadWithinSourceAsync(value.OriginalRead, body => body(), _ => { }, token).ConfigureAwait(false);
        return await IsSavedRecordCurrentAsync(value, token).ConfigureAwait(false);
    }
}
public sealed class HomeAutomationCreationGraphActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "automations" && actionId is "automations.graph.save-draft" or "automations.activate"
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
