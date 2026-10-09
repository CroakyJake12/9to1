using System.Text.Json;
using MembershipMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.MembershipMetadata;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    public Task<IReadOnlyList<AssistantConversationSummary>> ReadConversationsAsync(AssistantIdentity identity,
        int maximum = 100, CancellationToken token = default) => _originals.Admit<IReadOnlyList<AssistantConversationSummary>>(async () =>
    {
        if (maximum is < 1 or > 1000) throw new AssistantCommandRefusedException("Conversation count must be between 1 and 1000.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        await DefinitionAsync(home, identity, token).ConfigureAwait(false);
        var sessions = await _originals.Source(() => home.Den.ListAsync<SessionRecord>(identity.NamespaceId, token)).ConfigureAwait(false);
        var result = new List<AssistantConversationSummary>();
        foreach (var session in sessions)
        {
            var membership = ReadMetadata<MembershipMetadata>(session, SessionKey);
            if (membership is not { Schema: 1, Publication: "ready" } || membership.DefinitionId != identity.DefinitionId ||
                !Guid.TryParse(session.ConversationId, out var id)) continue;
            var conversation = await _originals.Source(() => _conversations.GetAsync(id, token)).ConfigureAwait(false);
            if (conversation is null || !MatchesMembership(conversation, membership)) continue;
            var task = await _originals.Source(() => _tasks.GetByContextAsync(id, token)).ConfigureAwait(false);
            result.Add(new(id, conversation.Title, conversation.UpdatedAt, conversation.IsArchived, task is null ? null : Context(task)));
        }
        await OpenHomeAsync(token).ConfigureAwait(false);
        return result.OrderByDescending(row => row.UpdatedAt).Take(maximum).ToArray();
    });

    public Task<AssistantConversationBinding> CreateConversationAsync(AssistantIdentity identity, long expectedDefinitionRevision,
        Guid conversationId, string title, Guid operationId, CancellationToken token = default,
        AssistantConversationKind kind = AssistantConversationKind.Chat) => _originals.Admit(() =>
            CreateConversationOriginalAsync(identity, expectedDefinitionRevision, conversationId, title, operationId, token, kind, null));

    public Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesAsync(int maximum, CancellationToken token = default) =>
        _originals.Admit(async () =>
        {
            if (_development is not DenAssistantOriginalDevelopmentOwner actualOwner)
                throw new AssistantCommandRefusedException("The actual canonical project catalogue owner is not composed.");
            await OpenHomeAsync(token).ConfigureAwait(false);
            return await _originals.Source(() => actualOwner.ReadOriginalProjectCandidatesAsync(maximum, token)).ConfigureAwait(false);
        });
    public Task<AssistantOriginalProjectChoice> AuthorizeOriginalProjectChoiceAsync(AssistantOriginalProjectCandidate candidate,
        CancellationToken token = default) => _originals.Admit(async () =>
        {
            if (_development is not DenAssistantOriginalDevelopmentOwner actualOwner)
                throw new AssistantCommandRefusedException("The actual Home project READ selection owner is not composed.");
            await OpenHomeAsync(token).ConfigureAwait(false);
            return await _originals.Source(() => actualOwner.AuthorizeOriginalProjectChoiceWithinSourceAsync(candidate,
                MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
        });

    public Task<AssistantConversationBinding> CreateProjectConversationOriginalAsync(AssistantIdentity identity, long expectedDefinitionRevision,
        Guid conversationId, string title, Guid operationId, AssistantOriginalProjectChoice actualChoice, CancellationToken token = default) =>
        _originals.Admit(async () =>
        {
            if (_development is not DenAssistantOriginalDevelopmentOwner actualOwner)
                throw new AssistantCommandRefusedException("The actual authorized project selection owner is not composed.");
            if (_originals.Invoke(() => actualOwner.ObserveOriginalProjectChoiceMode(actualChoice)) == HavenMode.Studio)
                return await CreateStudioProjectConversationOriginalAsync(actualOwner, identity, expectedDefinitionRevision,
                    conversationId, title, operationId, actualChoice, token).ConfigureAwait(false);
            var selected = await _originals.Source(() => actualOwner.ValidateOriginalProjectChoiceWithinSourceAsync(
                actualChoice, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            if (selected.Actor != home.Actor || selected.Container.Mode != HavenMode.Tasks)
                throw new AssistantCommandRefusedException("The actual Home actor and selected Tasks project container must remain the same.");
            return await CreateConversationOriginalAsync(identity, expectedDefinitionRevision, conversationId, title,
                operationId, token, AssistantConversationKind.Task, selected).ConfigureAwait(false);
        });

    private async Task<AssistantConversationBinding> CreateConversationOriginalAsync(AssistantIdentity identity,
        long expectedDefinitionRevision, Guid conversationId, string title, Guid operationId, CancellationToken token,
        AssistantConversationKind kind, DenAssistantOriginalDevelopmentOwner.ProjectChoiceObservation? selected)
    {
        if (conversationId == Guid.Empty || operationId == Guid.Empty || !Enum.IsDefined(kind))
            throw new AssistantCommandRefusedException("A conversation, operation identity and valid conversation kind are required.");
        if (_conversations is not IConversationCreateOnlyRepository creation)
            throw new AssistantCommandRefusedException("The actual canonical conversation store does not expose atomic create-only writes; no membership was created.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var definition = await DefinitionAsync(home, identity, token).ConfigureAwait(false);
        if (definition.Revision != expectedDefinitionRevision) throw new DenException(DenErrorCode.Conflict, "The definition changed before conversation creation.", recoverable: true);
        var sessionId = SessionId(conversationId);
        var prior = await _originals.Source(() => home.Den.GetAsync<SessionRecord>(identity.NamespaceId, sessionId, token)).ConfigureAwait(false);
        if (prior is not null)
        {
            var metadata = ReadMetadata<MembershipMetadata>(prior, SessionKey);
            if (metadata?.CreationOperation != operationId || metadata.DefinitionId != identity.DefinitionId || metadata.Kind != kind || metadata.OriginalProjectReference != selected?.Project.Reference)
                throw new AssistantCommandRefusedException("This conversation identity already has another membership or creation operation.");
            if (metadata.Publication != "ready")
                throw new AssistantCommandRefusedException("Conversation publication is pending or unknown; inspect the retained original before recovery. Creation will not replay its write.");
            return await BindAsync(home, identity, prior, token).ConfigureAwait(false);
        }
        if (await _originals.Source(() => _conversations.GetAsync(conversationId, token)).ConfigureAwait(false) is not null)
            throw new AssistantCommandRefusedException("An existing canonical conversation cannot be adopted by a creation request.");
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(conversationId, kind == AssistantConversationKind.Task ? HavenMode.Tasks : HavenMode.Chat,
            kind == AssistantConversationKind.Task ? ConversationKind.Task : ConversationKind.Chat,
            string.IsNullOrWhiteSpace(title) ? "New conversation" : title, selected?.Container.Id, null, false, false, now, now);
        var metadataPending = new MembershipMetadata(1, identity.DefinitionId, operationId, kind, "pending", conversation, selected?.Project.Reference);
        var pending = new SessionRecord { Id = sessionId, NamespaceId = identity.NamespaceId, ConversationId = conversationId.ToString("D"),
            ExtensionData = WriteMetadata(null, SessionKey, metadataPending) };
        pending = await _originals.Source(() => home.Den.SaveAsync(pending, 0, Operation(operationId, "conversation.reserve"), token)).ConfigureAwait(false);
        var currentDefinition = await DefinitionAsync(home, identity, token).ConfigureAwait(false);
        if (currentDefinition.Revision != expectedDefinitionRevision)
            throw new DenException(DenErrorCode.Conflict, "The definition changed after membership reservation; preserve the pending original for inspection.", recoverable: true);
        if (!await _originals.Source(() => creation.TryCreateConversationAsync(conversation, token)).ConfigureAwait(false))
            throw new AssistantCommandRefusedException("Another canonical row already has this ID. The pending membership is retained for inspection; no existing conversation was overwritten or adopted.");
        var actual = await _originals.Source(() => _conversations.GetAsync(conversationId, token)).ConfigureAwait(false);
        if (actual != conversation) throw new InvalidOperationException("The actual conversation write was not acknowledged with its exact original identity.");
        var ready = pending with { ExtensionData = WriteMetadata(pending.ExtensionData, SessionKey, metadataPending with { Publication = "ready" }) };
        ready = await _originals.Source(() => home.Den.SaveAsync(ready, pending.Revision, Operation(operationId, "conversation.publish"), token)).ConfigureAwait(false);
        return await BindAsync(home, identity, ready, token).ConfigureAwait(false);
    }

    public Task<AssistantConversationBinding> OpenConversationAsync(AssistantIdentity identity, Guid conversationId,
        CancellationToken token = default) => _originals.Admit(async () =>
    {
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var session = await _originals.Source(() => home.Den.GetAsync<SessionRecord>(identity.NamespaceId, SessionId(conversationId), token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The conversation has no persisted Assistant membership.");
        return await BindAsync(home, identity, session, token).ConfigureAwait(false);
    });

    private Task<AssistantConversationBinding> BindAsync(HomePersonalDenSession home, AssistantIdentity identity,
        SessionRecord session, CancellationToken token) => _originals.Source(() => _membership.BindWithinSourceAsync(
            home, identity, session, MembershipScope, _originals.Retain, token));

    public bool IsIssuedOriginalBinding(AssistantConversationBinding binding) => _membership.IsIssuedOriginalBinding(binding);
    private async Task<AssistantConversationBinding> ValidateBindingAsync(AssistantConversationBinding binding, CancellationToken token) =>
        (await _originals.Source(() => _membership.ValidateOriginalWithinSourceAsync(
            binding, MembershipScope, _originals.Retain, token)).ConfigureAwait(false)).Binding;

    public Task<AssistantConversationData> ReadConversationAsync(AssistantConversationBinding binding, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        var messages = await _originals.Source(() => _conversations.GetMessagesAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        var attachments = await _originals.Source(() => _production.GetAttachmentsAsync(current.Conversation.Id, null, token)).ConfigureAwait(false);
        var branches = await _originals.Source(() => _production.GetBranchesAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        var branch = await _originals.Source(() => _production.GetCurrentBranchAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        var draft = await _originals.Source(() => _production.GetDraftAsync(current.Conversation.Id, branch?.Id, token)).ConfigureAwait(false);
        var task = await _originals.Source(() => _tasks.GetByContextAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        return new AssistantConversationData(current.Conversation, messages, attachments, branches, draft, task);
    });

    public Task SaveConversationDraftAsync(AssistantConversationBinding binding, Guid? branchId,
        string content, IReadOnlyList<Guid> attachmentIds, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await ValidateBranchAsync(current.Conversation.Id, branchId, token).ConfigureAwait(false);
        await ValidateAttachmentsAsync(current.Conversation.Id, attachmentIds, token).ConfigureAwait(false);
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await _originals.Source(() => _production.SaveDraftAsync(new(current.Conversation.Id, branchId, content,
            JsonSerializer.Serialize(attachmentIds), DateTimeOffset.UtcNow), token)).ConfigureAwait(false);
        return true;
    });

    public Task<ConversationBranch> CreateBranchAsync(AssistantConversationBinding binding, Guid messageId,
        string? name, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        var messages = await _originals.Source(() => _conversations.GetMessagesAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        if (!messages.Any(message => message.Id == messageId)) throw new AssistantCommandRefusedException("The branch point is outside this conversation.");
        var branch = await _originals.Source(() => _production.GetCurrentBranchAsync(current.Conversation.Id, token)).ConfigureAwait(false)
            ?? await _originals.Source(() => _production.EnsureRootBranchAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        return await _originals.Source(() => _production.CreateBranchAsync(current.Conversation.Id, branch.Id, messageId,
            string.IsNullOrWhiteSpace(name) ? "Branch" : name, ConversationBranchReason.Manual, token)).ConfigureAwait(false);
    });

    public Task SwitchBranchAsync(AssistantConversationBinding binding, Guid branchId, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await ValidateBranchAsync(current.Conversation.Id, branchId, token).ConfigureAwait(false);
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await _originals.Source(() => _production.SetCurrentBranchAsync(current.Conversation.Id, branchId, token)).ConfigureAwait(false);
        return true;
    });

    private async Task ValidateBranchAsync(Guid conversationId, Guid? branchId, CancellationToken token)
    {
        if (branchId is null) return;
        var branches = await _originals.Source(() => _production.GetBranchesAsync(conversationId, token)).ConfigureAwait(false);
        if (!branches.Any(branch => branch.Id == branchId)) throw new AssistantCommandRefusedException("The selected branch belongs to another conversation or is unavailable.");
    }
    private async Task ValidateAttachmentsAsync(Guid conversationId, IReadOnlyList<Guid> ids, CancellationToken token)
    {
        var rows = await _originals.Source(() => _production.GetAttachmentsAsync(conversationId, null, token)).ConfigureAwait(false);
        if (ids.Any(id => !rows.Any(row => row.Id == id))) throw new AssistantCommandRefusedException("A selected attachment is outside this conversation.");
    }
    private static string SessionId(Guid conversationId) => AssistantCanonicalMembershipSource.SessionId(conversationId);
    private static bool MatchesMembership(Conversation conversation, MembershipMetadata metadata) =>
        AssistantCanonicalMembershipSource.MatchesMembership(conversation, metadata);
    private static ProviderExecutionContext Context(TaskExecutionSnapshot snapshot) => new(snapshot.TaskId, snapshot.ContextId,
        snapshot.ExecutionId, snapshot.Attempts.LastOrDefault()?.Id, snapshot.PersistenceRevision)
        { SelectedCandidate = snapshot.Attempts.LastOrDefault()?.Candidate };
}
