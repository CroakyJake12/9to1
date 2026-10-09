using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Migration;

/// <summary>Explicit, recoverable migration over an owned original SQLite source and the SAME
/// Home Den/bridge. The source remains the legacy API owner through April 2027; this service
/// never executes legacy permissions, detection rules, history or model fallbacks.</summary>
public sealed partial class LegacyAgentMigrationController : ILegacyAgentMigrationController
{
    private const string Namespace = "personal";
    private const string MigrationKey = "assistants.savedAgentMigration.v1";
    private const string DefinitionKey = "assistants.definition.v1";
    private const string OriginalDefinitionKey = "assistants.legacyDefinition.v1";
    private readonly LegacySavedAgentSqliteSource _source;
    private readonly HomePersonalDenFactory _home;
    private readonly IResourceStoreOwnershipReceiptAuthority _ownership;
    private readonly IAssistantCanonicalBridge _bridge;
    private readonly MigrationOriginals _originals = new();
    private readonly object _issuer = new();
    private AuthenticatedResourceActor? _actor;
    private string? _denId;
    private sealed record OriginalPreview(HomePersonalDenSession Home, LegacySavedAgentSqliteSource.Snapshot Source,
        DenRecord? Destination);
    private sealed record OriginalRecovery(HomePersonalDenSession Home, AgentDefinitionRecord Current,
        AgentDefinitionRecord Original, MigrationMetadata Migration);
    private sealed record MigrationMetadata(int Schema, Guid SourceStoreId, Guid LegacyAgentId,
        string SourceDefinitionSha256, string OriginalDefinitionJson, string ProfileId,
        Guid OperationId, ConfiguredIdentityKind Kind, AssistantConfiguration Configuration,
        long StagedRevision, LegacyAgentMigrationState State);

    public LegacyAgentMigrationController(LegacySavedAgentSqliteSource actualSource, HomePersonalDenFactory actualHome,
        IResourceStoreOwnershipReceiptAuthority actualOwnership, IAssistantCanonicalBridge actualBridge,
        HomeOriginalLocalStoreImportSession? originalImportSession = null)
    {
        _source = actualSource; _home = actualHome; _ownership = actualOwnership; _bridge = actualBridge;
        if (actualBridge is not HavenOS.Apps.Assistants.Canonical.DenAssistantCanonicalBridge canonical ||
            !ReferenceEquals(canonical.OriginalHomeDenFactory, actualHome))
            throw new ArgumentException("Migration must borrow the SAME actual canonical bridge and Home Den factory.", nameof(actualBridge));
        if (originalImportSession is not null && !actualSource.IsOriginalImportSession(originalImportSession, actualOwnership))
            throw new ArgumentException("Import must borrow the SAME configured source, Home profile and ownership authority.", nameof(originalImportSession));
        _imports = originalImportSession;
    }
    public bool IsOriginalComposition(LegacySavedAgentSqliteSource source, HomePersonalDenFactory home,
        IAssistantCanonicalBridge bridge) => ReferenceEquals(source, _source) && ReferenceEquals(home, _home) && ReferenceEquals(bridge, _bridge);

    public Task<LegacyAgentMigrationPage> ListPageAsync(string? cursor = null, int maximum = 30,
        CancellationToken token = default) => _originals.Admit(async () =>
    {
        ValidatePage(cursor, maximum);
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        return await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, false, async source =>
        {
            var definitions = await source.ListAsync(cursor, maximum + 1, token).ConfigureAwait(false);
            var rows = new List<LegacyAgentMigrationItem>();
            foreach (var definition in definitions.Take(maximum))
            {
                var destination = await _originals.Source(() => home.Den.GetAsync<DenRecord>(Namespace, definition.Id.ToString("D"), token)).ConfigureAwait(false);
                rows.Add(new(definition.Id, definition.Name, definition.Description, definition.IsEnabled, definition.IsBuiltIn,
                    State(destination, source.Identity.StoreId, definition.Id, home.Actor.ProfileId)));
            }
            return new LegacyAgentMigrationPage(rows.AsReadOnly(), definitions.Count > maximum ? definitions[maximum - 1].Id.ToString("D") : null);
        }, _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
    });

    public Task<LegacyAgentMigrationPreview> PreviewAsync(Guid legacyAgentId, CancellationToken token = default) =>
        _originals.Admit(async () =>
    {
        if (legacyAgentId == Guid.Empty) throw _originals.Refuse("InvalidIdentity", "Choose an existing saved Agent.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        return await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, false, async source =>
        {
            var snapshot = await source.ReadAsync(legacyAgentId, token).ConfigureAwait(false)
                ?? throw _originals.Refuse("LegacyAgentUnavailable", "This saved Agent is unavailable. Refresh the list.");
            var destination = await _originals.Source(() => home.Den.GetAsync<DenRecord>(Namespace, legacyAgentId.ToString("D"), token)).ConfigureAwait(false);
            var state = State(destination, source.Identity.StoreId, legacyAgentId, home.Actor.ProfileId);
            var prior = ReadMigration(destination);
            var canClassify = state is LegacyAgentMigrationState.Unselected or LegacyAgentMigrationState.Recovered ||
                state == LegacyAgentMigrationState.Staged && prior?.SourceDefinitionSha256 == snapshot.DefinitionSha256;
            var notes = new List<string>
            {
                "Choose Assistant for a personal assistant, or Specialist for repeatable configured work. No type has been chosen for you.",
                "The original saved Agent, its run history, scoped memories and references remain in their existing store. Linked conversations retain their own identity and permissions.",
                "Legacy permissions and automatic detection rules are preserved for review. They do not grant access or start work after migration.",
                "Legacy API compatibility is retained until April 2027. Review model routing and resource permissions in the new configuration before running work."
            };
            if (!string.IsNullOrWhiteSpace(snapshot.Definition.FallbackModel)) notes.Add("The original fallback model is retained in the source. Enable a compatible fallback only after reviewing the current model policy.");
            if (state == LegacyAgentMigrationState.Conflict) notes.Add("The destination identity belongs to another definition. Nothing will be overwritten.");
            if (state == LegacyAgentMigrationState.Staged && !canClassify) notes.Add("The saved Agent changed after staging. Its original staged version is retained; inspect recovery before proceeding.");
            if (state == LegacyAgentMigrationState.Classified) notes.Add("This saved Agent has already been classified. Open its configuration or review undo.");
            var suggested = prior is not null && state == LegacyAgentMigrationState.Staged ? prior.Configuration : new AssistantConfiguration
            {
                Name = snapshot.Definition.Name, Description = snapshot.Definition.Description,
                Instructions = snapshot.Definition.Instructions, IconResourceId = snapshot.Definition.IconKey,
                Model = new(ModelId: string.IsNullOrWhiteSpace(snapshot.Definition.PreferredModel) ? null : snapshot.Definition.PreferredModel),
                Enabled = snapshot.Definition.IsEnabled
            };
            AssistantDefinitionSnapshot? classified = null;
            if (state == LegacyAgentMigrationState.Classified)
            {
                // Ask the SAME canonical owner for the actual destination, including Specialists.
                // The UI must never reconstruct a Den identity from a legacy row identifier.
                classified = await _originals.Source(() => _bridge.GetAsync(
                    new(home.DenId, Namespace, destination!.Id), token)).ConfigureAwait(false);
                if (classified.Revision != destination!.Revision)
                    throw _originals.Refuse("RevisionConflict", "The classification changed during preview. Refresh and review it again.");
            }
            return new LegacyAgentMigrationPreview(_issuer, new OriginalPreview(home, snapshot, destination), legacyAgentId,
                snapshot.DefinitionSha256, snapshot.Definition, Freeze(suggested), state, destination?.Revision ?? 0,
                snapshot.Preserved, notes.AsReadOnly(), canClassify,
                state == LegacyAgentMigrationState.Staged && prior is not null ? new(prior.OperationId, prior.Kind) : null, classified);
        }, _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
    });

    public Task<LegacyAgentLinkPage> ReadPreservedLinksAsync(LegacyAgentMigrationPreview preview,
        string kind, string? cursor = null, int maximum = 30, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var original = DemandPreview(preview); ValidatePage(cursor, maximum);
        if (kind is not ("runs" or "memories" or "references" or "conversations"))
            throw _originals.Refuse("InvalidLinkKind", "Choose runs, memories, references or conversations.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false); DemandSameHome(original.Home, home);
        return await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, false, async source =>
        {
            if (source.Identity.StoreId != original.Source.Store.StoreId)
                throw _originals.Refuse("SourceChanged", "The original Saved Agent store changed. Reopen migration.");
            return await source.LinksAsync(preview.LegacyAgentId, kind, cursor, maximum, token).ConfigureAwait(false);
        }, _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
    });

    public Task<LegacyAgentMigrationResult> ClassifyAsync(LegacyAgentMigrationPreview preview,
        ConfiguredIdentityKind confirmedKind, AssistantConfiguration confirmedConfiguration,
        Guid operationId, CancellationToken token = default)
    {
        // Capture caller-owned collections before asynchronous execution can yield.
        var configuration = Freeze(confirmedConfiguration);
        return _originals.Admit(async () =>
        {
            var original = DemandPreview(preview);
            ValidateSelection(confirmedKind, configuration, operationId);
            if (!preview.CanClassify) throw _originals.Refuse("MigrationNeedsReview", "Review this saved Agent before classifying it.");
            var home = await OpenHomeAsync(token).ConfigureAwait(false); DemandSameHome(original.Home, home);
            return await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, true, async source =>
            {
                var currentSource = await source.ReadAsync(preview.LegacyAgentId, token).ConfigureAwait(false);
                if (currentSource is null || source.Identity.StoreId != original.Source.Store.StoreId ||
                    currentSource.DefinitionSha256 != original.Source.DefinitionSha256)
                    throw _originals.Refuse("SourceChanged", "The saved Agent changed since preview. Refresh and review it again.");
                var id = preview.LegacyAgentId.ToString("D");
                var current = await _originals.Source(() => home.Den.GetAsync<DenRecord>(Namespace, id, token)).ConfigureAwait(false);
                if ((current?.Revision ?? 0) != preview.DestinationRevision ||
                    Serialize(current) != Serialize(original.Destination))
                    throw _originals.Refuse("RevisionConflict", "The destination changed since preview. No definition was replaced.");
                var state = State(current, source.Identity.StoreId, preview.LegacyAgentId, home.Actor.ProfileId);
                if (state is not (LegacyAgentMigrationState.Unselected or LegacyAgentMigrationState.Recovered or LegacyAgentMigrationState.Staged))
                    throw _originals.Refuse("IdentityCollision", "This identity is already used. Nothing will be overwritten.");
                var migration = ReadMigration(current);
                AgentDefinitionRecord staged;
                if (state == LegacyAgentMigrationState.Staged)
                {
                    if (migration is null || migration.OperationId != operationId || migration.Kind != confirmedKind ||
                        migration.SourceDefinitionSha256 != currentSource.DefinitionSha256 || Serialize(migration.Configuration) != Serialize(configuration))
                        throw _originals.Refuse("StagedOperationMismatch", "This staged migration retains its original choice. Review and continue that exact operation.");
                    staged = (AgentDefinitionRecord)current!;
                }
                else
                {
                    var expected = current?.Revision ?? 0;
                    migration = new(1, source.Identity.StoreId, preview.LegacyAgentId, currentSource.DefinitionSha256,
                        currentSource.RawDefinitionJson, home.Actor.ProfileId, operationId, confirmedKind, configuration,
                        expected + 1, LegacyAgentMigrationState.Staged);
                    var row = current as AgentDefinitionRecord ?? new AgentDefinitionRecord
                    {
                        Id = id, NamespaceId = Namespace, DisplayName = currentSource.Definition.Name, Version = "1",
                        Instructions = currentSource.Definition.Instructions, Enabled = currentSource.Definition.IsEnabled
                    };
                    row = row with { ExtensionData = WriteMetadata(row.ExtensionData, MigrationKey, migration) };
                    await source.DemandCurrentAsync(token).ConfigureAwait(false);
                    await RecheckHomeAsync(home, token).ConfigureAwait(false);
                    _originals.EffectStarting();
                    var guarded = new DulcheDen(home.Den.Store,
                        new MigrationCommitPolicy(this, home, source, id, current, requireNoContinuingState: false), home.Den.PrincipalId);
                    staged = await _originals.Source(() => guarded.SaveAsync(row, expected, $"assistants.legacy.stage.{operationId:D}", token)).ConfigureAwait(false);
                }
                await source.DemandCurrentAsync(token).ConfigureAwait(false);
                await RecheckHomeAsync(home, token).ConfigureAwait(false);
                _originals.EffectStarting();
                var definition = await _originals.Source(() => _bridge.ResolveLegacyDefinitionAsync(
                    new(home.DenId, Namespace, id), staged.Revision, confirmedKind, configuration, operationId, token)).ConfigureAwait(false);
                if (definition.Identity.DefinitionId != id || definition.Revision != staged.Revision + 1 || definition.Kind != confirmedKind)
                    throw new InvalidDataException("The actual classification acknowledgement did not match the staged definition; inspect recovery.");
                await source.DemandCurrentAsync(token).ConfigureAwait(false);
                await RecheckHomeAsync(home, token).ConfigureAwait(false);
                return new LegacyAgentMigrationResult(definition, preview.LegacyAgentId, currentSource.DefinitionSha256, operationId, true);
            }, _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
        });
    }

    public Task<LegacyAgentRecoveryPreview> ReadRecoveryAsync(AssistantIdentity identity,
        CancellationToken token = default) => _originals.Admit(async () =>
    {
        var home = await OpenHomeAsync(token).ConfigureAwait(false); DemandIdentity(home, identity);
        var current = await _originals.Source(() => home.Den.GetAsync<AgentDefinitionRecord>(Namespace, identity.DefinitionId, token)).ConfigureAwait(false)
            ?? throw _originals.Refuse("DefinitionUnavailable", "This migrated definition is unavailable.");
        var migration = ReadMigration(current) ?? throw _originals.Refuse("RecoveryUnavailable", "This definition has no original Saved Agent migration record.");
        return await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, false, async source =>
        {
            if (source.Identity.StoreId != migration.SourceStoreId || migration.ProfileId != home.Actor.ProfileId)
                throw _originals.Refuse("SourceChanged", "This definition belongs to a different original store or profile.");
            var sourceRow = await source.ReadAsync(migration.LegacyAgentId, token).ConfigureAwait(false);
            var original = ReadMetadata<AgentDefinitionRecord>(current, OriginalDefinitionKey);
            var canUndo = sourceRow is not null && original is not null &&
                current.Revision == migration.StagedRevision + 1 && original.Revision == migration.StagedRevision &&
                original.Id == current.Id && original.NamespaceId == current.NamespaceId &&
                Serialize(ReadMigration(original)) == Serialize(migration) && IsExactClassifiedMetadata(current, migration) &&
                !await HasNewOwnedStateAsync(home, identity.DefinitionId, token).ConfigureAwait(false);
            var reason = canUndo
                ? "Undo removes this classification. The original saved Agent and all existing conversations, runs, memories and references remain available in their owning stores."
                : "This definition or its source changed after migration, or its original recovery state is unavailable. Later changes will be preserved; automatic undo is unavailable.";
            return new LegacyAgentRecoveryPreview(_issuer, new OriginalRecovery(home, current, original ?? current, migration),
                identity, current.Revision, current.DisplayName, migration.Kind, canUndo, reason);
        }, _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
    });

    public Task<LegacyAgentRecoveryResult> UndoAsync(LegacyAgentRecoveryPreview preview, Guid operationId,
        CancellationToken token = default) => _originals.Admit(async () =>
    {
        if (preview is null || !ReferenceEquals(preview.Owner, _issuer) || preview.Original is not OriginalRecovery original)
            throw _originals.Refuse("ForeignPreview", "Reopen recovery in this migration view.");
        if (!preview.CanUndo || operationId == Guid.Empty) throw _originals.Refuse("RecoveryNeedsReview", "Review an unchanged migrated definition before undoing its classification.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false); DemandSameHome(original.Home, home);
        return await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, true, async source =>
        {
            if (source.Identity.StoreId != original.Migration.SourceStoreId)
                throw _originals.Refuse("SourceChanged", "The original source store changed.");
            var legacy = await source.ReadAsync(original.Migration.LegacyAgentId, token).ConfigureAwait(false);
            if (legacy is null) throw _originals.Refuse("SourceUnavailable", "The original saved Agent is unavailable. Its captured definition remains retained for recovery.");
            var current = await _originals.Source(() => home.Den.GetAsync<AgentDefinitionRecord>(Namespace, preview.Identity.DefinitionId, token)).ConfigureAwait(false);
            if (current is null || current.Revision != preview.Revision || Serialize(current) != Serialize(original.Current))
                throw _originals.Refuse("RevisionConflict", "This definition changed since recovery preview. Later work was preserved.");
            if (await HasNewOwnedStateAsync(home, preview.Identity.DefinitionId, token).ConfigureAwait(false))
                throw _originals.Refuse("HistoryChanged", "This Assistant or Specialist has continuing conversations, runs or memory. They were preserved; undo is unavailable.");
            // Keep the original agent ID and every domain-owned history record. Remove only the
            // classified product metadata by restoring the exact preclassification Den record.
            var recovered = original.Original with
            {
                ExtensionData = WriteMetadata(original.Original.ExtensionData, MigrationKey,
                    original.Migration with { State = LegacyAgentMigrationState.Recovered })
            };
            await source.DemandCurrentAsync(token).ConfigureAwait(false); await RecheckHomeAsync(home, token).ConfigureAwait(false);
            _originals.EffectStarting();
            var guarded = new DulcheDen(home.Den.Store,
                new MigrationCommitPolicy(this, home, source, current.Id, current, requireNoContinuingState: true), home.Den.PrincipalId);
            var saved = await _originals.Source(() => guarded.SaveAsync(recovered, current.Revision,
                $"assistants.legacy.undo.{operationId:D}", token)).ConfigureAwait(false);
            await source.DemandCurrentAsync(token).ConfigureAwait(false); await RecheckHomeAsync(home, token).ConfigureAwait(false);
            return new LegacyAgentRecoveryResult(preview.Identity, saved.Revision, true, true);
        }, _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
    });

    private OriginalPreview DemandPreview(LegacyAgentMigrationPreview preview) =>
        preview is not null && ReferenceEquals(preview.Owner, _issuer) && preview.Original is OriginalPreview original
            ? original : throw _originals.Refuse("ForeignPreview", "Preview this saved Agent in the current migration view first.");
    private async Task<HomePersonalDenSession> OpenHomeAsync(CancellationToken token)
    {
        var home = await _originals.Source(() => _home.OpenWithinOriginalSourceAsync(_originals.Run, _originals.Retain, token)).ConfigureAwait(false);
        _actor ??= home.Actor; _denId ??= home.DenId;
        if (_actor != home.Actor || _denId != home.DenId)
            throw _originals.Refuse("ProfileChanged", "The current Home profile or Den changed. Reopen migration.");
        return home;
    }
    private async Task RecheckHomeAsync(HomePersonalDenSession expected, CancellationToken token) =>
        DemandSameHome(expected, await OpenHomeAsync(token).ConfigureAwait(false));
    private void DemandSameHome(HomePersonalDenSession expected, HomePersonalDenSession current)
    {
        if (expected.Actor != current.Actor || expected.DenId != current.DenId)
            throw _originals.Refuse("ProfileChanged", "The Home profile changed since preview. Reopen migration.");
    }
    private void DemandIdentity(HomePersonalDenSession home, AssistantIdentity identity)
    {
        if (identity is null || identity.DenId != home.DenId || identity.NamespaceId != Namespace ||
            !Guid.TryParseExact(identity.DefinitionId, "D", out var id) || id == Guid.Empty)
            throw _originals.Refuse("ForeignIdentity", "Choose a definition in this current Home Den.");
    }
    private async Task<bool> HasNewOwnedStateAsync(HomePersonalDenSession home, string id, CancellationToken token)
    {
        var sessions = await _originals.Source(() => home.Den.ListAsync<SessionRecord>(Namespace, token)).ConfigureAwait(false);
        foreach (var session in sessions)
            if (session.ExtensionData?.TryGetValue("assistants.membership.v1", out var metadata) == true &&
                metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("definitionId", out var definition) &&
                definition.ValueKind == JsonValueKind.String && definition.GetString() == id) return true;
        var runs = await _originals.Source(() => home.Den.ListAsync<AgentRunRecord>(Namespace, token)).ConfigureAwait(false);
        if (runs.Any(run => run.AgentDefinitionId == id)) return true;
        var memories = await _originals.Source(() => home.Den.ListAsync<MemoryEntry>(Namespace, token)).ConfigureAwait(false);
        return memories.Any(memory => memory.ScopeKind == MemoryScopeKind.Agent && memory.ScopeId == id);
    }
    /// <summary>Intersection with the ORIGINAL Home policy on the SAME Den writer. Den invokes
    /// this again under its canonical writer lease immediately before publication. Original
    /// Den record reads do not acquire that writer lease; Home.Open/ObserveOwnership must never
    /// be called here. No record or ownership DTO substitutes for either original policy.</summary>
    private sealed class MigrationCommitPolicy(LegacyAgentMigrationController owner, HomePersonalDenSession home,
        LegacySavedAgentSqliteSource.OwnedLease source, string definitionId, DenRecord? expectedDefinition,
        bool requireNoContinuingState) : IDenAccessPolicy
    {
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (principalId != home.Den.PrincipalId || namespaceId != Namespace || objectId != definitionId || permission != DenPermission.Write)
                return false;
            if (!await home.Den.AccessPolicy.IsAllowedAsync(principalId, namespaceId, objectId, permission, cancellationToken).ConfigureAwait(false)) return false;
            await source.DemandCurrentAsync(cancellationToken).ConfigureAwait(false);
            var actual = await owner._originals.Source(() => home.Den.GetAsync<DenRecord>(namespaceId, objectId, cancellationToken)).ConfigureAwait(false);
            if (Serialize(actual) != Serialize(expectedDefinition)) return false;
            if (requireNoContinuingState && await owner.HasNewOwnedStateAsync(home, definitionId, cancellationToken).ConfigureAwait(false)) return false;
            await source.DemandCurrentAsync(cancellationToken).ConfigureAwait(false);
            return await home.Den.AccessPolicy.IsAllowedAsync(principalId, namespaceId, objectId, permission, cancellationToken).ConfigureAwait(false);
        }
    }
    private void ValidatePage(string? cursor, int maximum)
    {
        if (maximum is < 1 or > 100 || cursor is not null && !Guid.TryParseExact(cursor, "D", out _))
            throw _originals.Refuse("InvalidPage", "Use a valid list cursor and a page size between 1 and 100.");
    }
    private void ValidateSelection(ConfiguredIdentityKind kind, AssistantConfiguration configuration, Guid operation)
    {
        if (!Enum.IsDefined(kind) || operation == Guid.Empty || string.IsNullOrWhiteSpace(configuration.Name) ||
            configuration.Model is null || configuration.Memory is null || configuration.Proactive is null || configuration.Limits is null)
            throw _originals.Refuse("ExplicitClassificationRequired", "Choose Assistant or Specialist and review its named configuration.");
        if (kind == ConfiguredIdentityKind.Specialist && (configuration.Proactive.AllowCheckIns || configuration.Proactive.AllowUnsolicitedConversations))
            throw _originals.Refuse("SpecialistProactivityConflict", "Personal check-ins and unsolicited conversations are Assistant features. Review this choice.");
        if (configuration.Limits.Tokens < 0 || configuration.Limits.Steps < 0 || configuration.Limits.ToolCalls < 0 ||
            configuration.Limits.Cost < 0 || configuration.Limits.Time < TimeSpan.Zero)
            throw _originals.Refuse("InvalidLimits", "Resource limits cannot be negative.");
    }
    private static LegacyAgentMigrationState State(DenRecord? row, Guid sourceId, Guid agentId, string profileId)
    {
        if (row is null) return LegacyAgentMigrationState.Unselected;
        if (row is not AgentDefinitionRecord) return LegacyAgentMigrationState.Conflict;
        var migration = ReadMigration(row);
        if (migration is null || migration.SourceStoreId != sourceId || migration.LegacyAgentId != agentId || migration.ProfileId != profileId)
            return LegacyAgentMigrationState.Conflict;
        if (row.ExtensionData?.ContainsKey(DefinitionKey) == true) return LegacyAgentMigrationState.Classified;
        return migration.State;
    }
    private static MigrationMetadata? ReadMigration(DenRecord? row)
    {
        var metadata = row is null ? null : ReadMetadata<MigrationMetadata>(row, MigrationKey);
        return metadata is { Schema: 1 } ? metadata : null;
    }
    private static bool IsExactClassifiedMetadata(AgentDefinitionRecord row, MigrationMetadata migration)
    {
        if (row.ExtensionData?.TryGetValue(DefinitionKey, out var json) != true || json.ValueKind != JsonValueKind.Object) return false;
        // Validate the existing bridge's metadata; do not create a competing definition serializer.
        return json.TryGetProperty("creationOperation", out var operation) && operation.TryGetGuid(out var id) && id == migration.OperationId &&
            json.TryGetProperty("configuration", out var configuration) && Serialize(configuration.Deserialize<AssistantConfiguration>(DenJson.Options)) == Serialize(migration.Configuration) &&
            json.TryGetProperty("kind", out var kind) && kind.Deserialize<ConfiguredIdentityKind>(DenJson.Options) == migration.Kind;
    }
    private static T? ReadMetadata<T>(DenRecord row, string key) where T : class =>
        row.ExtensionData?.TryGetValue(key, out var value) == true ? value.Deserialize<T>(DenJson.Options) : null;
    private static Dictionary<string, JsonElement> WriteMetadata<T>(Dictionary<string, JsonElement>? original, string key, T value)
    {
        var copy = original?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        copy[key] = JsonSerializer.SerializeToElement(value, DenJson.Options); return copy;
    }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, DenJson.Options);
    private static AssistantConfiguration Freeze(AssistantConfiguration value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Round-trip into owned arrays so UI collection edits cannot alter an in-flight command.
        var copy = JsonSerializer.Deserialize<AssistantConfiguration>(Serialize(value), DenJson.Options)
            ?? throw new ArgumentException("Configuration is unavailable.", nameof(value));
        return copy with
        {
            ToolIds = Array.AsReadOnly(copy.ToolIds.ToArray()), ConnectedAppIds = Array.AsReadOnly(copy.ConnectedAppIds.ToArray()),
            KnowledgeResourceIds = Array.AsReadOnly(copy.KnowledgeResourceIds.ToArray()), ProjectReferences = Array.AsReadOnly(copy.ProjectReferences.ToArray()),
            Modalities = Array.AsReadOnly(copy.Modalities.ToArray()), Proactive = copy.Proactive with
            {
                EventKinds = copy.Proactive.EventKinds is null ? null : Array.AsReadOnly(copy.Proactive.EventKinds.ToArray()),
                NotificationChannels = copy.Proactive.NotificationChannels is null ? null : Array.AsReadOnly(copy.Proactive.NotificationChannels.ToArray()),
                AutomationIds = copy.Proactive.AutomationIds is null ? null : Array.AsReadOnly(copy.Proactive.AutomationIds.ToArray())
            }
        };
    }
    public bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge actualBridge) => ReferenceEquals(_bridge, actualBridge);
    public bool IsAcknowledgedOriginalCommandRefusal(Task actualCommand) => _originals.IsAcknowledged(actualCommand);
    public void RequestRetirement() => _originals.RequestRetirement();
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalJoin();
    public Task? OriginalClose => _originals.OriginalClose;
    public Task CloseAndDrainAsync() => _originals.CloseAndDrainAsync();
}
