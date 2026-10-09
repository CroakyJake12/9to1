using System.Text.Json;
using DefinitionMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.DefinitionMetadata;
using MembershipMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.MembershipMetadata;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>One presentation over the SAME Home Den, canonical conversations and Task owner.
/// Den and conversation writes are not atomic; pending membership is retained for inspection.</summary>
public sealed partial class DenAssistantCanonicalBridge : IAssistantCanonicalBridge
{
    private const string DefinitionKey = "assistants.definition.v1";
    private const string SessionKey = "assistants.membership.v1";
    private const string PersonalNamespace = "personal";
    private readonly AssistantCanonicalMembershipSource _membership;
    private readonly AssistantPresentationOriginals _originals = new();
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly IConversationProductionRepository _production;
    private readonly ChatSessionService _chat;
    private readonly TaskExecutionCoordinator _tasks;
    private readonly AssistantOriginalConversationHost _ordinary;
    private readonly IAssistantOriginalModelSelectionOwner? _models;
    private readonly IAssistantOriginalAttachmentOwner? _attachments;
    private readonly IAssistantOriginalDevelopmentOwner? _development;
    private readonly IAssistantOriginalCapabilityOwner? _capabilities;
    private readonly IAssistantOriginalPersistentMemoryInputOwner? _memory;
    public DenAssistantCanonicalBridge(HomePersonalDenFactory home, IConversationRepository conversations,
        IConversationProductionRepository production, ChatSessionService chat, TaskExecutionCoordinator tasks,
        AssistantOriginalConversationHost ordinary, IAssistantOriginalModelSelectionOwner? models = null,
        IAssistantOriginalAttachmentOwner? attachments = null, IAssistantOriginalDevelopmentOwner? development = null)
        : this(home, conversations, production, chat, tasks, ordinary, models, attachments, development, null) { }

    public DenAssistantCanonicalBridge(HomePersonalDenFactory home, IConversationRepository conversations,
        IConversationProductionRepository production, ChatSessionService chat, TaskExecutionCoordinator tasks,
        AssistantOriginalConversationHost ordinary, IAssistantOriginalModelSelectionOwner? models,
        IAssistantOriginalAttachmentOwner? attachments, IAssistantOriginalDevelopmentOwner? development,
        IAssistantOriginalCapabilityOwner? capabilities)
        : this(home, conversations, production, chat, tasks, ordinary, models, attachments, development, capabilities, null) { }

    public DenAssistantCanonicalBridge(HomePersonalDenFactory home, IConversationRepository conversations,
        IConversationProductionRepository production, ChatSessionService chat, TaskExecutionCoordinator tasks,
        AssistantOriginalConversationHost ordinary, IAssistantOriginalModelSelectionOwner? models,
        IAssistantOriginalAttachmentOwner? attachments, IAssistantOriginalDevelopmentOwner? development,
        IAssistantOriginalCapabilityOwner? capabilities, IAssistantOriginalPersistentMemoryInputOwner? memory)
    { _home = home; _conversations = conversations; _production = production; _chat = chat;
        _tasks = tasks; _ordinary = ordinary; _models = models; _attachments = attachments; _development = development; _capabilities = capabilities; _memory = memory;
        // This is a SAME configured tuple observation. Live source membership/permission
        // validation still occurs during preparation and each actual Chat dispatch.
        if (memory is not null && (!chat.HasOriginalPersistentMemorySource(memory) ||
            !memory.HasOriginalComposition(home, conversations)))
            throw new ArgumentException("The Assistant memory owner must be the SAME source configured in this Chat/Home/repository composition.", nameof(memory));
        // Membership keeps configured-presence metadata only. Source callbacks belong
        // to admitted runtime reads; construction creates no presentation admission.
        _membership = new(home, conversations, Capabilities(observeMemory: false));
        if (development is DenAssistantOriginalDevelopmentOwner actual) actual.BindOriginalMembershipSource(_membership); }

    // Composition compares the SAME configured owner objects; these references convey no grant.
    public TaskExecutionCoordinator OriginalTaskOwner => _tasks;
    public IAssistantOriginalDevelopmentOwner? OriginalDevelopmentOwner => _development;
    public IAssistantOriginalCapabilityOwner? OriginalCapabilityOwner => _capabilities;
    public IAssistantOriginalPersistentMemoryInputOwner? OriginalMemoryOwner => _memory;
    public HomePersonalDenFactory OriginalHomeDenFactory => _home;

    public Task<AssistantCatalogueObservation> ListAsync(CancellationToken token = default) => _originals.Admit(async () =>
    {
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var rows = await _originals.Source(() => home.Den.ListAsync<AgentDefinitionRecord>(PersonalNamespace, token)).ConfigureAwait(false);
        return new AssistantCatalogueObservation(rows.Where(IsConfigured).Select(row => Snapshot(home, row)).ToArray(), Capabilities());
    });

    public Task<AssistantDefinitionSnapshot> GetAsync(AssistantIdentity identity, CancellationToken token = default) =>
        _originals.Admit(async () => { var home = await OpenHomeAsync(token).ConfigureAwait(false);
            return Snapshot(home, await DefinitionAsync(home, identity, token).ConfigureAwait(false)); });

    public Task<AssistantDefinitionSnapshot> CreateAsync(ConfiguredIdentityKind kind, AssistantConfiguration configuration,
        Guid operationId, CancellationToken token = default) => _originals.Admit(async () =>
    {
        ValidateConfiguration(kind, configuration, operationId);
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var id = operationId.ToString("D");
        var existing = await _originals.Source(() => home.Den.GetAsync<AgentDefinitionRecord>(PersonalNamespace, id, token)).ConfigureAwait(false);
        if (existing is not null)
        {
            var original = ReadMetadata<DefinitionMetadata>(existing, DefinitionKey);
            if (original is null || original.CreationOperation != operationId || original.Kind != kind ||
                JsonSerializer.Serialize(original.Configuration, DenJson.Options) != JsonSerializer.Serialize(configuration, DenJson.Options))
                throw new AssistantCommandRefusedException("This creation operation already names a different definition.");
            return Snapshot(home, existing);
        }
        var row = new AgentDefinitionRecord { Id = id, NamespaceId = PersonalNamespace, DisplayName = configuration.Name,
            Version = "1", Instructions = configuration.Instructions, Enabled = configuration.Enabled,
            ToolIds = configuration.ToolIds.ToArray(), PluginIds = configuration.ConnectedAppIds.ToArray(),
            ModelPolicyJson = JsonSerializer.Serialize(configuration.Model, DenJson.Options),
            BudgetJson = JsonSerializer.Serialize(configuration.Limits, DenJson.Options),
            ExtensionData = WriteMetadata(null, DefinitionKey, new DefinitionMetadata(1, kind, configuration, operationId)) };
        var saved = await _originals.Source(() => home.Den.SaveAsync(row, 0, Operation(operationId, "definition.create"), token)).ConfigureAwait(false);
        return Snapshot(home, saved);
    });

    public Task<AssistantDefinitionSnapshot> UpdateAsync(AssistantIdentity identity, long expectedRevision,
        AssistantConfiguration configuration, Guid operationId, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var current = await DefinitionAsync(home, identity, token).ConfigureAwait(false);
        var metadata = ReadMetadata<DefinitionMetadata>(current, DefinitionKey)!;
        ValidateConfiguration(metadata.Kind, configuration, operationId);
        if (current.Revision != expectedRevision) throw new DenException(DenErrorCode.Conflict, "The configured definition changed.", recoverable: true);
        var changed = current with { DisplayName = configuration.Name, Instructions = configuration.Instructions,
            Enabled = configuration.Enabled, ToolIds = configuration.ToolIds.ToArray(), PluginIds = configuration.ConnectedAppIds.ToArray(),
            ModelPolicyJson = JsonSerializer.Serialize(configuration.Model, DenJson.Options), BudgetJson = JsonSerializer.Serialize(configuration.Limits, DenJson.Options),
            ExtensionData = WriteMetadata(current.ExtensionData, DefinitionKey, metadata with { Configuration = configuration }) };
        var saved = await _originals.Source(() => home.Den.SaveAsync(changed, expectedRevision, Operation(operationId, "definition.update"), token)).ConfigureAwait(false);
        return Snapshot(home, saved);
    });

    public Task<IReadOnlyList<AssistantLegacyMigrationCandidate>> ReadLegacyMigrationCandidatesAsync(CancellationToken token = default) =>
        _originals.Admit<IReadOnlyList<AssistantLegacyMigrationCandidate>>(async () =>
        {
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            var rows = await _originals.Source(() => home.Den.ListAsync<AgentDefinitionRecord>(PersonalNamespace, token)).ConfigureAwait(false);
            return rows.Where(row => !IsConfigured(row)).Select(row => new AssistantLegacyMigrationCandidate(
                Identity(home, row), row.Revision, row.DisplayName, "Legacy semantics require an explicit Assistant or Specialist decision; history and references are retained.",
                [ConfiguredIdentityKind.Assistant, ConfiguredIdentityKind.Specialist])).ToArray();
        });

    public Task<AssistantDefinitionSnapshot> ResolveLegacyDefinitionAsync(AssistantIdentity legacyIdentity,
        long expectedRevision, ConfiguredIdentityKind confirmedKind, AssistantConfiguration configuration,
        Guid operationId, CancellationToken token = default) => _originals.Admit(async () =>
    {
        ValidateConfiguration(confirmedKind, configuration, operationId);
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        DemandIdentity(home, legacyIdentity);
        var current = await _originals.Source(() => home.Den.GetAsync<AgentDefinitionRecord>(legacyIdentity.NamespaceId, legacyIdentity.DefinitionId, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The legacy definition is unavailable.");
        if (IsConfigured(current)) throw new AssistantCommandRefusedException("This definition already has a configured product kind.");
        if (current.Revision != expectedRevision) throw new DenException(DenErrorCode.Conflict, "The legacy definition changed.", recoverable: true);
        var extensions = WriteMetadata(current.ExtensionData, "assistants.legacyDefinition.v1", current);
        var changed = current with { DisplayName = configuration.Name, Instructions = configuration.Instructions,
            Enabled = configuration.Enabled, ExtensionData = WriteMetadata(extensions, DefinitionKey,
                new DefinitionMetadata(1, confirmedKind, configuration, operationId)) };
        var saved = await _originals.Source(() => home.Den.SaveAsync(changed, expectedRevision, Operation(operationId, "definition.classify"), token)).ConfigureAwait(false);
        return Snapshot(home, saved);
    });

    internal AssistantCanonicalMembershipSource OriginalMembershipSource => _membership;
    private void MembershipScope(Action body) => _originals.Invoke(() => { body(); return true; });
    private Task<HomePersonalDenSession> OpenHomeAsync(CancellationToken token) =>
        _originals.Source(() => _membership.OpenHomeWithinSourceAsync(MembershipScope, _originals.Retain, token));
    private static void DemandIdentity(HomePersonalDenSession home, AssistantIdentity identity) =>
        AssistantCanonicalMembershipSource.DemandIdentity(home, identity);
    private Task<AgentDefinitionRecord> DefinitionAsync(HomePersonalDenSession home, AssistantIdentity identity, CancellationToken token) =>
        _originals.Source(() => _membership.DefinitionWithinSourceAsync(home, identity, MembershipScope, _originals.Retain, token));
    private static bool IsConfigured(AgentDefinitionRecord row) => AssistantCanonicalMembershipSource.IsConfigured(row);
    private AssistantDefinitionSnapshot Snapshot(HomePersonalDenSession home, AgentDefinitionRecord row) => _membership.Snapshot(home, row);
    private static AssistantIdentity Identity(HomePersonalDenSession home, DenRecord row) => AssistantCanonicalMembershipSource.Identity(home, row);
    private static T? ReadMetadata<T>(DenRecord row, string key) where T : class => AssistantCanonicalMembershipSource.ReadMetadata<T>(row, key);
    private static Dictionary<string, JsonElement> WriteMetadata<T>(Dictionary<string, JsonElement>? original, string key, T value)
    {
        var captured = original is null ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : original.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        captured[key] = JsonSerializer.SerializeToElement(value, DenJson.Options);
        return captured;
    }
    private static string Operation(Guid id, string verb) => $"assistants.{verb}.{id:D}";
    private static void ValidateConfiguration(ConfiguredIdentityKind kind, AssistantConfiguration config, Guid operation)
    {
        if (!Enum.IsDefined(kind) || operation == Guid.Empty || string.IsNullOrWhiteSpace(config.Name))
            throw new AssistantCommandRefusedException("A configured type, nonempty name and operation identity are required.");
        if (kind == ConfiguredIdentityKind.Specialist && (config.Proactive.AllowCheckIns || config.Proactive.AllowUnsolicitedConversations))
            throw new AssistantCommandRefusedException("Personal check-ins and unsolicited conversations belong to Assistants.");
        if (config.Limits.Tokens < 0 || config.Limits.Steps < 0 || config.Limits.ToolCalls < 0 || config.Limits.Cost < 0 || config.Limits.Time < TimeSpan.Zero)
            throw new AssistantCommandRefusedException("Usage limits cannot be negative.");
    }
    private IReadOnlyList<AssistantCapabilityObservation> Capabilities(bool observeMemory = true) =>
    [
        new("Definitions and revisioned configuration", AssistantSupportState.Available, "Current verified personal Home Den; resources remain preferences until authorized."),
        new("Conversations, drafts and branches", AssistantSupportState.Available, "Canonical conversation and production repositories with persisted Den membership."),
        new("Models", _models is null ? AssistantSupportState.Unsupported : AssistantSupportState.RequiresAuthorization, "Requires the host's current canonical model selection owner."),
        new("Attachments", _attachments is null ? AssistantSupportState.Unsupported : AssistantSupportState.RequiresAuthorization, "Import requires the actual host selected-file owner."),
        new("Dev", _development is null ? AssistantSupportState.Unsupported : AssistantSupportState.RequiresAuthorization, "Requires the actual Tasks/Studio project, task and resource owner; saved IDs grant no access."),
        observeMemory ? ObserveOriginalMemoryCapability() : ObserveConfiguredMemoryPresence(),
        new("Knowledge and connected apps", AssistantSupportState.Unsupported, "Configuration persists; effective canonical resource bindings are not yet composed for this product."),
        new("Proactivity and schedules", AssistantSupportState.Unsupported, "No canonical Assistant trigger producer is composed; saved opt-ins do not dispatch work."),
        new("Computer, Mini Computer and voice", AssistantSupportState.Unsupported, "Requires actual product capability and resource bindings."),
        new("Cloud and fallback restrictions", AssistantSupportState.Available, "Restrictive original request options intersect the SAME canonical provider router policy; settings add no authorization."),
        new("Usage budgets", AssistantSupportState.Unsupported, "Preferences persist; configured limits refuse dispatch until canonical enforcement is composed.")
    ];
    private AssistantCapabilityObservation ObserveOriginalMemoryCapability()
    {
        // The constructor already pairs this owner with SAME Chat/Home/repository.
        // This optional source reports composition only; it performs no content query
        // and cannot replace Prepare/Read/WRITE authorization or grant a resource.
        if (_memory is IAssistantOriginalMemoryCapabilitySource source)
            return _originals.Invoke(source.ObserveOriginalMemoryCapability);
        return ObserveConfiguredMemoryPresence();
    }
    private AssistantCapabilityObservation ObserveConfiguredMemoryPresence() =>
        new("Own Assistant memory", _memory is null ? AssistantSupportState.Unsupported : AssistantSupportState.RequiresAuthorization,
            "Requires the SAME live source, current definition/membership and separately owned memory-store Home READ; cloud/project expansion remains unavailable.");

    public void RequestRetirement()
    {
        _originals.RequestRetirement();
        RequestOriginalConfigurationInitializationDeliveryRetirement();
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        DemandExternalOriginalConfigurationInitializationJoin();
        _originals.DemandExternalJoin();
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        // Retirement callback failures are retained as their SAME source occurrences
        // by the per-view delivery owner before this independently joined close.
        RequestRetirement();
        return _originals.CloseAndDrainAsync();
    }
}
