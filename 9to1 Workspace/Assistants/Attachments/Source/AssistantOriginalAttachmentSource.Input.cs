using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.Attachments;

public sealed partial class AssistantOriginalAttachmentSource : IAssistantOriginalAttachmentInputOwner
{
    private ChatSessionService? _inputChat;
    private TaskExecutionCoordinator? _inputTasks;
    private readonly ConditionalWeakTable<IChatOriginalAttachmentInput, AttachmentInput> _inputs = new();
    private sealed class AttachmentInput(AssistantOriginalAttachmentSource owner, ReadSelection read,
        ConversationProductionRepository.OriginalAttachmentInputSnapshot snapshot, string prompt, Guid[] ids)
        : IChatOriginalAttachmentInput
    {
        internal AssistantOriginalAttachmentSource Owner => owner;
        internal ReadSelection Read => read;
        internal ConversationProductionRepository.OriginalAttachmentInputSnapshot Snapshot => snapshot;
        internal string Prompt => prompt;
        internal ChatOriginalAttachmentRequest? Request;
        internal IReadOnlyList<Guid> Ids { get; } = Array.AsReadOnly(ids);
        internal ChatOriginalAttachmentLineage Lineage { get; } = new(1, read.OriginalConversation.Id,
            read.BranchId, snapshot.SnapshotSha256, Array.AsReadOnly(ids));
    }
    public void BindOriginalInputOwners(ChatSessionService chat, TaskExecutionCoordinator tasks)
    {
        ArgumentNullException.ThrowIfNull(chat); ArgumentNullException.ThrowIfNull(tasks);
        if (!chat.HasOriginalAttachmentInputComposition(this, tasks))
            throw new ArgumentException("Retain the SAME configured Chat attachment source and Task coordinator.");
        lock (_writeGate)
        {
            if (_inputChat is not null && (!ReferenceEquals(_inputChat, chat) || !ReferenceEquals(_inputTasks, tasks)))
                throw new InvalidOperationException("The original attachment input composition cannot be replaced.");
            _inputChat = chat; _inputTasks = tasks;
        }
    }
    public bool HasOriginalInputComposition(ChatSessionService chat, TaskExecutionCoordinator tasks) =>
        ReferenceEquals(_inputChat, chat) && ReferenceEquals(_inputTasks, tasks) && chat.HasOriginalAttachmentInputComposition(this, tasks);
    public bool IsIssuedOriginalAttachmentInput(IChatOriginalAttachmentInput input) => input is AttachmentInput actual &&
        ReferenceEquals(actual.Owner, this) && _inputs.TryGetValue(input, out var issued) && ReferenceEquals(issued, actual) &&
        IsIssuedOriginalConversationRead(actual.Read);
    private AttachmentInput DemandInput(IChatOriginalAttachmentInput input) => IsIssuedOriginalAttachmentInput(input)
        ? (AttachmentInput)input : throw new UnauthorizedAccessException("Use this actual attachment source's saved input.");
    public ChatOriginalAttachmentLineage ObserveOriginalAttachmentLineage(IChatOriginalAttachmentInput input) => DemandInput(input).Lineage;

    public Task<IChatOriginalAttachmentInput> PrepareOriginalAttachmentInputWithinSourceAsync(
        AssistantConversationBinding binding, string prompt, IReadOnlyList<Guid> attachmentIds,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Run<IChatOriginalAttachmentInput>(scope, retain, async source =>
    {
        if (_inputChat is null || _inputTasks is null) throw new InvalidOperationException("Attachment input is not configured in this Chat owner.");
        Guid[] ids = []; source.Run(() => ids = attachmentIds.ToArray());
        var read = await PrepareRead(binding, null, source, token).ConfigureAwait(false);
        return await WithInputLease(read, source, async lease =>
        {
            var branch = await source.Read(() => _production.ReadOriginalAttachmentCurrentBranchAsync(lease, read, token)).ConfigureAwait(false);
            read = new ReadSelection(this, read.Membership, read.Binding, read.Actor, read.OriginalStoreIdentity,
                read.Permission, read.DenPermission, branch);
            _readSelections.Add(read, read);
            var snapshot = await source.Read(() => _production.ReadOriginalAttachmentInputAsync(lease, read, prompt, ids, token)).ConfigureAwait(false);
            DemandInputSnapshot(read, snapshot, source);
            await ValidateInputMembership(read, binding.Conversation, null, source, token, preparing: true).ConfigureAwait(false);
            var input = new AttachmentInput(this, read, snapshot, prompt, ids); _inputs.Add(input, input); return input;
        }, token).ConfigureAwait(false);
    });

    public Task<AttachmentPromptContext> ReadOriginalAttachmentInputWithinSourceAsync(IChatOriginalAttachmentInput input,
        ChatOriginalAttachmentRequest request, ProviderExecutionContext? context,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Run(scope, retain, async source =>
    {
        var actual = DemandInput(input);
        var snapshot = await ReadInputSnapshot(actual, request, context, source, token).ConfigureAwait(false);
        // Pure maintained formatting only. No legacy all-attachments query, stored
        // filename, image file or temporary-path fallback is called here.
        AttachmentPromptContext? result = null;
        source.Run(() => result = MessageAttachmentService.BuildOriginalApprovedTextContext(snapshot.Attachments));
        return result ?? throw new InvalidOperationException("No actual approved text context was produced.");
    });
    public Task ValidateOriginalAttachmentInputWithinSourceAsync(IChatOriginalAttachmentInput input,
        ChatOriginalAttachmentRequest request, ProviderExecutionContext? context,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Run(scope, retain, async source =>
    {
        _ = await ReadInputSnapshot(DemandInput(input), request, context, source, token).ConfigureAwait(false); return true;
    });

    private async Task<ConversationProductionRepository.OriginalAttachmentInputSnapshot> ReadInputSnapshot(AttachmentInput input,
        ChatOriginalAttachmentRequest request, ProviderExecutionContext? context, AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        ChatOriginalAttachmentAcceptance? accepted = null;
        source.Run(() =>
        {
            if (_inputChat is null || !_inputChat.IsIssuedOriginalAttachmentRequest(request, input, this) || request.Prompt != input.Prompt)
                throw new UnauthorizedAccessException("The SAME actual Chat request and saved input are required.");
            lock (_writeGate)
            {
                if (input.Request is not null && !_inputChat.IsSameOriginalAttachmentRequest(input.Request, request, input, this))
                    throw new UnauthorizedAccessException("A prepared attachment input cannot be replayed into another invocation.");
                input.Request ??= request;
            }
            accepted = _inputChat.ObserveOriginalAttachmentAcceptance(request, input, this);
        });
        await ValidateInputMembership(input.Read, request.Conversation, context, source, token).ConfigureAwait(false);
        var result = await WithInputLease(input.Read, source, async lease =>
        {
            var snapshot = accepted is null
                ? await source.Read(() => _production.ReadOriginalAttachmentInputAsync(lease, input.Read, input.Prompt, input.Ids, token)).ConfigureAwait(false)
                : await source.Read(() => _production.ReadOriginalAcceptedAttachmentInputAsync(lease, input.Read,
                    input.Snapshot, accepted.OriginalMessage, token)).ConfigureAwait(false);
            DemandInputSnapshot(input.Read, snapshot, source);
            if (snapshot.SnapshotSha256 != input.Snapshot.SnapshotSha256)
                throw new UnauthorizedAccessException("The actual saved draft, imported attachment or original import receipt changed.");
            return snapshot;
        }, token).ConfigureAwait(false);
        await ValidateInputMembership(input.Read, request.Conversation, context, source, token).ConfigureAwait(false); return result;
    }
    private void DemandInputSnapshot(ReadSelection read, ConversationProductionRepository.OriginalAttachmentInputSnapshot snapshot,
        AssistantMemoryOriginals.Scope source)
    {
        source.Run(() =>
        {
            if (!_production.IsIssuedOriginalAttachmentInputSnapshot(snapshot, read))
                throw new UnauthorizedAccessException("The SAME protected conversation owner did not issue this input snapshot.");
            foreach (var receipt in snapshot.OriginalImportReceipts)
            {
                using var document = JsonDocument.Parse(receipt); var root = document.RootElement;
                var actor = root.GetProperty("Actor").Deserialize<AuthenticatedResourceActor>();
                var definition = read.Binding.Definition;
                if (actor is null || actor.ActorId != read.Actor.ActorId || actor.ProfileId != read.Actor.ProfileId ||
                    actor.AccountId != read.Actor.AccountId || actor.OrganisationId != read.Actor.OrganisationId ||
                    root.GetProperty("DenId").GetString() != definition.Identity.DenId ||
                    root.GetProperty("NamespaceId").GetString() != definition.Identity.NamespaceId ||
                    root.GetProperty("DefinitionId").GetString() != definition.Identity.DefinitionId ||
                    root.GetProperty("DefinitionRevision").GetInt64() <= 0 ||
                    root.GetProperty("DefinitionRevision").GetInt64() > definition.Revision ||
                    root.GetProperty("SessionId").GetString() != read.Binding.DenSessionId ||
                    root.GetProperty("SessionRevision").GetInt64() <= 0 ||
                    root.GetProperty("SessionRevision").GetInt64() > read.Binding.DenSessionRevision)
                    throw new UnauthorizedAccessException("The original import belongs to another Assistant, conversation membership or owner.");
            }
        });
    }
    private async Task ValidateInputMembership(ReadSelection read, Conversation conversation, ProviderExecutionContext? context,
        AssistantMemoryOriginals.Scope source, CancellationToken token, bool preparing = false)
    {
        var current = await source.Read(() => read.Membership.ValidateOriginalDenWithinSourceAsync(read.Binding, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (current.Actor != read.Actor || current.Definition.Revision != read.Binding.Definition.Revision ||
            !current.Definition.Configuration.Enabled || current.Definition.Configuration.Archived ||
            !SameInputConversation(current.Conversation, conversation) || !SameInputConversation(read.OriginalConversation, conversation) ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(read.Permission, read.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(read.DenPermission, read.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual Assistant, conversation READ or current Home owner changed.");
        if (preparing) return; // No Task exists yet. This creates input, never execution authority.
        if (conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat && context is null) return;
        var tasks = _inputTasks;
        if (conversation.Mode != HavenMode.Tasks || conversation.Kind != ConversationKind.Task || context is null || tasks is null)
            throw new UnauthorizedAccessException("Attachment Task input requires its actual current Task context.");
        var task = await source.Read(() => tasks.GetByContextAsync(conversation.Id, token)).ConfigureAwait(false);
        var observation = await source.Read(() => tasks.ObserveOriginalTaskActorWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        var issued = false; source.Run(() => issued = tasks.IsIssuedOriginalTaskActorObservation(observation));
        if (!issued || !observation.IsConfigured || observation.Actor is not { } actor || task?.OwnerBinding is not { } owner ||
            task.TaskId != context.TaskId || task.ContextId != conversation.Id || context.ContextId != conversation.Id ||
            task.ExecutionId != context.ExecutionId || task.PersistenceRevision != context.PersistenceRevision || task.Attempts.LastOrDefault()?.Id != context.AttemptId ||
            owner.TaskId != task.TaskId || owner.ContextId != task.ContextId || owner.ExecutionId != task.ExecutionId ||
            owner.ActorId != actor.ActorId || owner.ProfileId != actor.ProfileId || owner.AccountId != actor.AccountId ||
            owner.OrganisationId != actor.OrganisationId || owner.AuthenticationRevision != actor.AuthenticationRevision)
            throw new UnauthorizedAccessException("The original canonical Task or its independently authenticated current owner changed.");
    }
    private static bool SameInputConversation(Conversation left, Conversation right) => left.Id == right.Id &&
        left.Mode == right.Mode && left.Kind == right.Kind && left.ContainerId == right.ContainerId &&
        left.SpaceId == right.SpaceId && left.LessonId == right.LessonId && !left.IsTemporary && !right.IsTemporary && !left.IsArchived && !right.IsArchived;
    private async Task<T> WithInputLease<T>(ReadSelection read, AssistantMemoryOriginals.Scope source,
        Func<CanonicalSqliteOriginalStoreLease, Task<T>> body, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? lease = null; T result = default!; var errors = new List<Exception>();
        try
        {
            await source.Read(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(read.Actor, false, source.Run, source.Retain, token),
                actual => lease = actual).ConfigureAwait(false);
            await source.Read(() => lease!.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            if (lease!.OriginalIdentity != read.OriginalStoreIdentity) throw new UnauthorizedAccessException("The original conversation store changed.");
            result = await body(lease).ConfigureAwait(false);
            await source.Read(() => lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        if (lease is not null) try { await source.ReadCleanup(lease.CloseAndDrainAsync).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        AssistantMemoryOriginals.Throw(errors); return result;
    }
}
