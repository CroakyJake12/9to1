using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>A live scoped input adapter for the SAME canonical Chat and Knowledge Library.
/// It owns no database, index, memory writer, model router or permission broker.</summary>
public sealed partial class AssistantOriginalMemorySource : IAssistantOriginalPersistentMemoryInputOwner, IAsyncDisposable
{
    public const string ResourceKind = "assistant.memory";
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly TaskExecutionCoordinator _tasks;
    private readonly KnowledgeLibraryService _knowledge;
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly IAssistantOriginalLegacyMemorySource? _legacyMemory;
    private readonly AssistantMemoryOriginals _originals = new();
    private sealed class Input(AssistantOriginalMemorySource owner, AssistantCanonicalMembershipSource membership,
        AssistantConversationBinding binding, AuthenticatedResourceActor actor,
        ResourceStoreIdentity store, VerifiedResourceStoreOwnership permission,
        KnowledgeLibraryService.AssistantOriginalReadScope readScope, AssistantOriginalLegacyMemoryReceipt? legacy) : IChatOriginalPersistentMemoryInput
    {
        internal AssistantOriginalMemorySource Owner => owner;
        internal AssistantCanonicalMembershipSource Membership => membership;
        internal AssistantConversationBinding Binding => binding;
        internal AuthenticatedResourceActor Actor => actor;
        internal ResourceStoreIdentity Store => store;
        internal VerifiedResourceStoreOwnership Permission => permission;
        internal KnowledgeLibraryService.AssistantOriginalReadScope ReadScope => readScope;
        internal AssistantOriginalLegacyMemoryReceipt? Legacy => legacy;
    }

    public AssistantOriginalMemorySource(HomePersonalDenFactory actualHome,
        IConversationRepository actualConversations, TaskExecutionCoordinator actualTasks,
        KnowledgeLibraryService actualKnowledge, CanonicalSqliteOriginalStoreOwner actualStore,
        HomeLocalProfileIdentity actualProfiles, HomeResourceStoreOwnershipAuthority actualOwnership,
        IAssistantOriginalLegacyMemorySource? actualLegacyMemorySource = null)
    {
        ArgumentNullException.ThrowIfNull(actualHome); ArgumentNullException.ThrowIfNull(actualConversations);
        ArgumentNullException.ThrowIfNull(actualTasks); ArgumentNullException.ThrowIfNull(actualKnowledge);
        ArgumentNullException.ThrowIfNull(actualStore); ArgumentNullException.ThrowIfNull(actualOwnership);
        if (!actualStore.HasOriginalProfiles(actualProfiles) || !actualKnowledge.HasOriginalAssistantMemoryStoreOwner(actualStore) ||
            actualLegacyMemorySource is not null && !actualLegacyMemorySource.HasOriginalLegacyMemoryStore(actualStore, actualProfiles))
            throw new ArgumentException("Assistant memory requires the SAME canonical Knowledge Library, protected SQLite store and Home profile.");
        _home = actualHome; _conversations = actualConversations; _tasks = actualTasks;
        _knowledge = actualKnowledge; _store = actualStore; _ownership = actualOwnership; _legacyMemory = actualLegacyMemorySource; _profiles = actualProfiles;
    }
    public bool HasOriginalComposition(HomePersonalDenFactory home, IConversationRepository conversations) =>
        ReferenceEquals(home, _home) && ReferenceEquals(conversations, _conversations);
    public bool HasOriginalHomeDenFactory(HomePersonalDenFactory home) => ReferenceEquals(home, _home);
    public bool IsIssuedOriginalInput(IChatOriginalPersistentMemoryInput input) =>
        input is Input original && ReferenceEquals(original.Owner, this) &&
        original.Membership.IsIssuedOriginalBinding(original.Binding) &&
        ReferenceEquals(original.Membership.OriginalHomeDenFactory, _home) &&
        ReferenceEquals(original.Membership.OriginalConversations, _conversations);

    public Task<AssistantOriginalMemoryPreparation> PrepareOriginalAssistantMemoryInputWithinSourceAsync(
        AssistantConversationBinding sameBinding, AssistantDefinitionSnapshot actualDefinition,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        RunAsync<AssistantOriginalMemoryPreparation>(originalSynchronousScope, retainOriginalTask, async source =>
        {
            // Decline before opening/querying any memory source. This is an immutable
            // pre-effect outcome, never a waiver for a faulted/canceled raw source.
            var reason = ConfigurationRefusal(actualDefinition);
            if (reason is not null) return new(null, reason);
            var membership = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(sameBinding);
            if (membership is null || !ReferenceEquals(membership.OriginalHomeDenFactory, _home) ||
                !ReferenceEquals(membership.OriginalConversations, _conversations) ||
                sameBinding.Definition.Identity != actualDefinition.Identity || sameBinding.Definition.Revision != actualDefinition.Revision)
                return new(null, "Reopen the Assistant through its actual current conversation owner before enabling memory.");
            var current = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(
                sameBinding, source.Run, source.Retain, token)).ConfigureAwait(false);
            reason = ConfigurationRefusal(current.Definition);
            if (reason is not null) return new(null, reason);
            return await WithLeaseAsync(current.Actor, source, async lease =>
            {
                var permission = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(
                    ResourceKind, lease.OriginalIdentity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
                if (!MatchesPermission(permission, current.Actor, lease.OriginalIdentity) ||
                    !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(permission!, current.Actor,
                        source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
                    return new AssistantOriginalMemoryPreparation(null,
                        "Review and import this existing Assistant memory store through Home before using its records.");
                var final = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(
                    sameBinding, source.Run, source.Retain, token)).ConfigureAwait(false);
                if (final.Actor != current.Actor || ConfigurationRefusal(final.Definition) is not null)
                    return new AssistantOriginalMemoryPreparation(null, "The Assistant or current Home profile changed. Reopen its memory settings.");
                var identity = current.Definition.Identity;
                var storageScope = JsonSerializer.Serialize(new[] { "assistant-memory.v1", lease.OriginalIdentity.StoreId.ToString("D"),
                    identity.DenId, identity.NamespaceId, identity.DefinitionId });
                var legacy = await PrepareLegacyMemoryAsync(sameBinding, lease.OriginalIdentity, source, token).ConfigureAwait(false);
                var selection = _knowledge.CreateOriginalAssistantMemoryReadScope(_store, identity.DefinitionId, storageScope, legacy is not null);
                return new AssistantOriginalMemoryPreparation(new Input(this, membership, sameBinding,
                    current.Actor, lease.OriginalIdentity, permission!, selection, legacy),
                    "Only this Assistant's permitted local Learn Me records are prepared.", storageScope);
            }, token).ConfigureAwait(false);
        });

    public Task<IReadOnlyList<KnowledgeRecord>> ReadOriginalWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
        Conversation sameActualConversation, ProviderExecutionContext? actualCanonicalContext,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        RunAsync<IReadOnlyList<KnowledgeRecord>>(originalSynchronousScope, retainOriginalTask, async source =>
        {
            var original = DemandInput(input);
            await ValidateMembershipAsync(original, sameActualConversation, actualCanonicalContext, source, token).ConfigureAwait(false);
            return await WithLeaseAsync(original.Actor, source, async lease =>
            {
                await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
                var records = await source.Read(() => _knowledge.GetActiveAssistantLearnMeWithinSourceAsync(
                    lease, original.ReadScope, MemoryInjection.MaximumRecords, token)).ConfigureAwait(false);
                await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
                await ValidateMembershipAsync(original, sameActualConversation, actualCanonicalContext, source, token).ConfigureAwait(false);
                return (IReadOnlyList<KnowledgeRecord>)Array.AsReadOnly(records.ToArray());
            }, token).ConfigureAwait(false);
        });

    public Task ValidateOriginalWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
        Conversation sameActualConversation, ProviderExecutionContext? actualCanonicalContext,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        RunAsync(originalSynchronousScope, retainOriginalTask, async source =>
        {
            var original = DemandInput(input);
            await ValidateMembershipAsync(original, sameActualConversation, actualCanonicalContext, source, token).ConfigureAwait(false);
            return await WithLeaseAsync(original.Actor, source, async lease =>
            {
                await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
                await ValidateMembershipAsync(original, sameActualConversation, actualCanonicalContext, source, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        });

    public Task<AssistantMemoryManagementRecords> ReadOriginalManagementRecordsWithinSourceAsync(
        IChatOriginalPersistentMemoryInput input, Conversation conversation, Action<Action> scope,
        Action<Task> retain, CancellationToken token) => RunAsync(scope, retain, async source =>
    {
        var original = DemandInput(input);
        await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
        return await WithLeaseAsync(original.Actor, source, async lease =>
        {
            await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
            var ready = await source.Read(() => KnowledgeLibraryService.IsOriginalAssistantMemorySchemaReadyAsync(lease, token)).ConfigureAwait(false);
            IReadOnlyList<KnowledgeRecord> records = ready
                ? await source.Read(() => _knowledge.GetActiveAssistantLearnMeWithinSourceAsync(lease, original.ReadScope, MemoryInjection.MaximumRecords, token)).ConfigureAwait(false)
                : Array.Empty<KnowledgeRecord>();
            await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
            await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
            return new AssistantMemoryManagementRecords(records, !ready);
        }, token).ConfigureAwait(false);
    });

    private Input DemandInput(IChatOriginalPersistentMemoryInput input) => IsIssuedOriginalInput(input)
        ? (Input)input : throw new UnauthorizedAccessException("Only the SAME configured Assistant memory source's live input is accepted.");
    private async Task ValidateMembershipAsync(Input input, Conversation conversation, ProviderExecutionContext? context,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        var current = await source.Read(() => input.Membership.ValidateOriginalWithinSourceAsync(
            input.Binding, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (current.Actor != input.Actor || ConfigurationRefusal(current.Definition) is not null ||
            !SameConversationScope(current.Conversation, conversation))
            throw new UnauthorizedAccessException("The actual Assistant, conversation, configuration or current Home actor changed.");
        if (conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat)
        {
            if (context is not null) throw new UnauthorizedAccessException("An ordinary Assistant conversation cannot borrow a Task's memory context.");
            return;
        }
        if (conversation.Mode != HavenMode.Tasks || conversation.Kind != ConversationKind.Task || context is null || context.ContextId != conversation.Id)
            throw new UnauthorizedAccessException("Assistant Task memory requires its actual canonical execution context.");
        var task = await source.Read(() => _tasks.GetByContextAsync(conversation.Id, token)).ConfigureAwait(false);
        if (task is null || task.TaskId != context.TaskId || task.ExecutionId != context.ExecutionId ||
            task.ContextId != context.ContextId || task.PersistenceRevision != context.PersistenceRevision ||
            task.Attempts.LastOrDefault()?.Id != context.AttemptId || task.OwnerBinding is null ||
            !await IsOriginalTaskOwnerCurrentAsync(task, source, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The canonical Task or its actual current owner changed before memory access.");
    }
    private static bool SameConversationScope(Conversation first, Conversation second) =>
        first.Id == second.Id && first.Mode == second.Mode && first.Kind == second.Kind &&
        first.ContainerId == second.ContainerId && first.SpaceId == second.SpaceId && first.LessonId == second.LessonId &&
        first.IsTemporary == second.IsTemporary && !second.IsTemporary && !second.IsArchived;
    private static string? ConfigurationRefusal(AssistantDefinitionSnapshot definition)
    {
        if (definition.Kind != ConfiguredIdentityKind.Assistant) return "This memory input belongs to the Assistant product.";
        var configuration = definition.Configuration;
        if (!configuration.Enabled || configuration.Archived) return "Enable this Assistant before preparing memory.";
        if (!configuration.Memory.Enabled) return "Persistent memory is disabled for this Assistant.";
        if (configuration.Memory.IncludeProjectContext) return "Project memory requires its separately authorized canonical project source.";
        if (configuration.Model.AllowCloud) return "Cloud memory disclosure requires its actual disclosure permission owner. Use local-only routing for this scoped memory source.";
        return null;
    }
    private static bool MatchesPermission(VerifiedResourceStoreOwnership? permission, AuthenticatedResourceActor actor,
        ResourceStoreIdentity store) => permission?.Receipt is not null && permission.ResourceKind == ResourceKind &&
        permission.StoreId == store.StoreId.ToString("D") && permission.ProfileId == actor.ProfileId;
    private async Task DemandPermissionAsync(Input input, CanonicalSqliteOriginalStoreLease lease,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        if (lease.OriginalIdentity != input.Store || !MatchesPermission(input.Permission, input.Actor, input.Store) ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(input.Permission, input.Actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual Assistant memory store or Home import receipt changed.");
        await source.Read(() => lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        await DemandLegacyMemoryAsync(input, source, token).ConfigureAwait(false);
    }
    private async Task<T> WithLeaseAsync<T>(AuthenticatedResourceActor actor, AssistantMemoryOriginals.Scope source,
        Func<CanonicalSqliteOriginalStoreLease, Task<T>> operation, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null; T result = default!; var failures = new List<Exception>();
        try
        {
            await source.Read(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actor, false,
                source.Run, source.Retain, token), original => lease = original).ConfigureAwait(false);
            await source.Read(() => lease!.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            result = await operation(lease!).ConfigureAwait(false);
            await source.Read(() => lease!.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        catch (Exception failure) { failures.Add(failure); }
        if (lease is not null)
            try { await source.ReadCleanup(lease.CloseAndDrainAsync).ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        AssistantMemoryOriginals.Throw(failures); return result;
    }
    private Task<T> RunAsync<T>(Action<Action> caller, Action<Task> retain,
        Func<AssistantMemoryOriginals.Scope, Task<T>> operation) => _originals.Admit(async () =>
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(retain);
        var source = _originals.CreateScope(caller, retain); T result = default!; var failures = new List<Exception>();
        try { result = await operation(source).ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        AssistantMemoryOriginals.Throw(failures); return result;
    });
    public void RequestRetirement() => _originals.RequestRetirement();
    public Task? OriginalClose => _originals.OriginalClose;
    public Task CloseAndDrainAsync() => _originals.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
