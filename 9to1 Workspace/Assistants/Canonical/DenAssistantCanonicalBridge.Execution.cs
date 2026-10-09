using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    public Task<IReadOnlyList<AssistantModelChoice>> ListAvailableModelsAsync(AssistantConversationBinding binding,
        CancellationToken token = default) => _originals.Admit<IReadOnlyList<AssistantModelChoice>>(async () =>
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        if (_models is null) return [];
        var available = await _originals.Source(() => _models.ReadAvailableOriginalAsync(current.Definition, current.Conversation, token)).ConfigureAwait(false);
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        return available.ToArray();
    });

    private async Task<ModelDescriptor> SelectModelAsync(AssistantConversationBinding binding, AssistantTaskInput input,
        OriginalAssistantMemoryRequest memory, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt) || string.IsNullOrWhiteSpace(input.ProviderId))
            throw new AssistantCommandRefusedException("A prompt and an actual owner-issued provider/model choice are required.");
        if (_models is null) throw new AssistantCommandRefusedException("The canonical model selection owner is not composed.");
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await DemandCurrentOriginalAssistantMemoryRequestAsync(binding, memory, token).ConfigureAwait(false);
        var limits = current.Definition.Configuration.Limits;
        if (limits.Tokens is not null || limits.Time is not null || limits.Steps is not null || limits.ToolCalls is not null || limits.Cost is not null)
            throw new AssistantCommandRefusedException("Configured usage limits require actual canonical enforcement before dispatch; the saved input remains available.");
        if (!current.Definition.Configuration.Enabled || current.Definition.Configuration.Archived || current.Conversation.IsArchived)
            throw new AssistantCommandRefusedException("This configured identity or conversation is inactive.");
        var available = await _originals.Source(() => _models.ReadAvailableOriginalAsync(current.Definition, current.Conversation, token)).ConfigureAwait(false);
        var selected = available.Where(choice => choice.ProviderId == input.ProviderId && SameModel(choice.Model, input.Model)).ToArray();
        if (selected.Length != 1) throw new AssistantCommandRefusedException("The selected provider/model is no longer uniquely available under the effective canonical policy.");
        var model = selected[0].Model;
        // The SAME Chat owner recognizes provider-qualified keys and applies shared routing/permissions.
        var name = input.ProviderId == "ollama" ? model.Name : model.Name.StartsWith(input.ProviderId + ":", StringComparison.Ordinal)
            ? model.Name : input.ProviderId + ":" + model.Name;
        return model with { Name = name };
    }
    private static bool SameModel(ModelDescriptor first, ModelDescriptor second) =>
        first.Name == second.Name && first.Capabilities.SetEquals(second.Capabilities);

    public Task<MessageAttachment> ImportAttachmentOriginalAsync(AssistantConversationBinding binding, string selectedPath,
        Guid? branchId, CancellationToken token = default) => _originals.Admit(async () =>
    {
        if (_attachments is null) throw new AssistantCommandRefusedException("The actual selected-file owner is not composed.");
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await ValidateBranchAsync(current.Conversation.Id, branchId, token).ConfigureAwait(false);
        Task<MessageAttachment>? original = null;
        MessageAttachment result;
        try { result = await _originals.Source(() => original = _attachments.ImportOriginalAsync(binding, selectedPath, branchId, token)).ConfigureAwait(false); }
        catch
        {
            if (original is not null && _attachments is IAssistantOriginalAttachmentCommandSource issuer)
                _originals.AcknowledgeOriginalExternalPreEffectRefusal(original, issuer.IsAcknowledgedOriginalCommandRefusal);
            throw;
        }
        if (result.ConversationId != current.Conversation.Id || result.BranchId != branchId)
            throw new InvalidOperationException("The actual attachment owner returned a different conversation or branch.");
        return result;
    });

    public Task RemoveAttachmentOriginalAsync(AssistantConversationBinding binding, Guid attachmentId, CancellationToken token = default) => _originals.Admit(async () =>
    {
        if (_attachments is null) throw new AssistantCommandRefusedException("The actual attachment owner is not composed.");
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await ValidateAttachmentsAsync(current.Conversation.Id, [attachmentId], token).ConfigureAwait(false);
        Task? original = null;
        try { await _originals.Source(() => original = _attachments.RemoveOriginalAsync(binding, attachmentId, token)).ConfigureAwait(false); }
        catch
        {
            if (original is not null && _attachments is IAssistantOriginalAttachmentCommandSource issuer)
                _originals.AcknowledgeOriginalExternalPreEffectRefusal(original, issuer.IsAcknowledgedOriginalCommandRefusal);
            throw;
        }
        return true;
    });

    public Task<AssistantOriginalConversationObservation> SendConversationOriginalAsync(AssistantConversationBinding binding,
        AssistantTaskInput input, CancellationToken token = default) => _originals.Admit(async () =>
    {
        _originals.DemandObservationCapacity();
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        if (current.Conversation.Mode != HavenMode.Chat || current.Conversation.Kind != ConversationKind.Chat)
            throw new AssistantCommandRefusedException("Ordinary conversation Send requires its actual Chat context; existing Task input uses steering or original recovery.");
        var memory = await PrepareOriginalAssistantMemoryRequestAsync(binding, current.Definition, token).ConfigureAwait(false);
        var model = await SelectModelAsync(binding, input, memory, token).ConfigureAwait(false);
        var attachments = await PrepareOriginalAttachmentInputAsync(binding, input, token).ConfigureAwait(false);
        current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        await DemandCurrentOriginalAssistantMemoryRequestAsync(binding, memory, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var observation = _originals.Invoke(() => _ordinary.StartOriginal(binding, _chat, current.Conversation,
            input.Prompt, model, input.Effort, current.Definition.Configuration.Name, current.Definition.Configuration.Instructions,
            OriginalAttachmentRoutingOptions(OriginalMemoryRoutingOptions(memory), attachments)));
        _originals.Observe(observation.CloseAndDrainAsync, () => observation.OriginalClose);
        return observation;
    });

    public Task<AssistantOriginalSendObservation> StartOriginalTaskAsync(AssistantConversationBinding binding,
        AssistantTaskInput input, CancellationToken token = default) => _originals.Admit(async () =>
    {
        _originals.DemandObservationCapacity();
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        if (current.Conversation.Mode != HavenMode.Tasks || current.Conversation.Kind != ConversationKind.Task)
            throw new AssistantCommandRefusedException("Explicit task input requires a separately created genuine durable Tasks conversation.");
        var attachments = await PrepareOriginalAttachmentInputAsync(binding, input, token).ConfigureAwait(false);
        var memory = await PrepareOriginalAssistantMemoryRequestAsync(binding, current.Definition, token).ConfigureAwait(false);
        var model = await SelectModelAsync(binding, input, memory, token).ConfigureAwait(false);
        current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        TaskRunOriginalInitialChatObservationLease lease;
        if (current.Conversation.ContainerId is not null)
        {
            if (_development is not DenAssistantOriginalDevelopmentOwner actualOwner)
                throw new AssistantCommandRefusedException("The actual Home-backed project Task input owner is not composed.");
            var prepared = await _originals.Source(() => actualOwner.PrepareOriginalProjectTaskInputWithinSourceAsync(
                binding, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
            try
            {
                current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
                if (prepared.OriginalConversation != current.Conversation)
                    throw new AssistantCommandRefusedException("The source-selected project conversation changed before dispatch.");
                var selected = await ReadOriginalTaskCapabilitiesAsync(binding, current.Definition, model,
                    prepared, token).ConfigureAwait(false);
                await DemandCurrentOriginalTaskCapabilitiesAsync(binding, selected, token).ConfigureAwait(false);
                await DemandCurrentOriginalAssistantMemoryRequestAsync(binding, memory, token).ConfigureAwait(false);
                // This SAME process owner captures actual Chat factory entry and raw acquisition.
                // A failed/unknown acquisition cannot retire the initial input borrowed by business.
                lease = await _originals.Source(() => actualOwner.AcquireOriginalProjectTaskDispatchWithinSourceAsync(
                    prepared, () => _chat.StartObservedOriginalProjectTaskSendAsync(prepared.OriginalConversation,
                        prepared, input.Prompt, model, input.Effort, selected.Active,
                        current.Definition.Configuration.Name, current.Definition.Configuration.Instructions,
                        DuoMode.Solo, token, generationOptions: OriginalAttachmentRoutingOptions(OriginalMemoryRoutingOptions(memory), attachments) with { RequestedToolSelectionConstraints = selected.ToolSelection },
                        filePermission: selected.FilePermission, commandPermission: selected.CommandPermission,
                        browserPermission: selected.BrowserPermission, explicitCapabilities: selected.RequiredModel,
                        availableCapabilities: selected.Active), CapabilityScope, _originals.Retain)).ConfigureAwait(false);
            }
            catch (Exception primary)
            {
                try
                {
                    await _originals.Source(() => actualOwner.CloseUndispatchedOriginalProjectInputWithinSourceAsync(
                        prepared, CapabilityScope, _originals.Retain)).ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Original project Task admission and independent undispatched input cleanup failed.", primary, cleanup);
                }
                throw;
            }
        }
        else
        {
            var selected = await ReadOriginalTaskCapabilitiesAsync(binding, current.Definition, model, null, token).ConfigureAwait(false);
            await DemandCurrentOriginalTaskCapabilitiesAsync(binding, selected, token).ConfigureAwait(false);
            await DemandCurrentOriginalAssistantMemoryRequestAsync(binding, memory, token).ConfigureAwait(false);
            lease = await _originals.Source(() => _chat.StartObservedOriginalTaskSendAsync(current.Conversation,
                input.Prompt, model, input.Effort, selected.Active, current.Definition.Configuration.Name,
                current.Definition.Configuration.Instructions, DuoMode.Solo, null, null, null, null, token,
                filePermission: selected.FilePermission, commandPermission: selected.CommandPermission,
                browserPermission: selected.BrowserPermission, explicitCapabilities: selected.RequiredModel,
                availableCapabilities: selected.Active,
                generationOptions: OriginalAttachmentRoutingOptions(OriginalMemoryRoutingOptions(memory), attachments) with { RequestedToolSelectionConstraints = selected.ToolSelection })).ConfigureAwait(false);
        }

        if (!_chat.IsIssuedOriginalTaskObservation(lease)) throw new InvalidOperationException("The actual Chat owner did not issue this observation.");
        var observation = new AssistantOriginalSendObservation(binding, lease);
        _originals.Observe(observation.CloseAndDrainAsync, () => observation.OriginalClose);
        return observation;
    });

    public Task<AssistantWorkObservation> ReadWorkAsync(AssistantConversationBinding binding, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        var task = await _originals.Source(() => _tasks.GetByContextAsync(current.Conversation.Id, token)).ConfigureAwait(false);
        // Current source-issued membership/read remains mandatory. Stable owner
        // equality only classifies renewal; it never grants access or live control.
        var actor = task is null ? null : await ReadOriginalTaskActorAsync(token).ConfigureAwait(false);
        var renewal = task is not null && RequiresOriginalTaskOwnerRenewal(current, task, actor!);
        TaskRunOriginalRunControlAvailability? controls = null;
        if (task is not null && !renewal) controls = await _originals.Source(() => _tasks.GetOriginalRunControlAvailabilityAsync(task.TaskId, task.ExecutionId, token)).ConfigureAwait(false);
        if (task is not null)
        {
            var finalActor = await ReadOriginalTaskActorAsync(token).ConfigureAwait(false);
            if (finalActor != actor || renewal != RequiresOriginalTaskOwnerRenewal(current, task, finalActor))
                throw new AssistantCommandRefusedException("The current Task owner changed; refresh this conversation.");
        }
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        return new AssistantWorkObservation(task, controls, Capabilities()) { RequiresOwnerRenewal = renewal };
    });

    private async Task<TaskExecutionSnapshot> RequireTaskAsync(AssistantConversationBinding binding, ProviderExecutionContext expected, CancellationToken token)
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        var task = await _originals.Source(() => _tasks.GetByContextAsync(current.Conversation.Id, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("No actual canonical Task is acknowledged for this conversation.");
        if (task.TaskId != expected.TaskId || task.ContextId != expected.ContextId || task.ExecutionId != expected.ExecutionId ||
            task.ContextId != current.Conversation.Id || task.PersistenceRevision != expected.PersistenceRevision ||
            task.Attempts.LastOrDefault()?.Id != expected.AttemptId)
            throw new AssistantCommandRefusedException("The canonical Task, run, attempt or revision changed; refresh its actual observation.");
        var actor = await ReadOriginalTaskActorAsync(token).ConfigureAwait(false);
        if (RequiresOriginalTaskOwnerRenewal(current, task, actor))
            throw new AssistantCommandRefusedException("The canonical Task belongs to an earlier authenticated session. Review recovery before continuing.");
        // The Task actor observation never substitutes for current Home/Den READ.
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        return task;
    }

    public Task<FollowUpDecision> SubmitFollowUpAsync(AssistantConversationBinding binding, ProviderExecutionContext expectedTask,
        string instruction, TaskFollowUpMode mode, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var task = await RequireTaskAsync(binding, expectedTask, token).ConfigureAwait(false);
        return await _originals.Source(() => _tasks.SubmitFollowUpAsync(task.TaskId, instruction, mode, null, null, token)).ConfigureAwait(false);
    });
    public Task<TaskRunOriginalRunControlResult> ControlOriginalRunAsync(AssistantConversationBinding binding,
        ProviderExecutionContext expectedTask, TaskRunOriginalRunControlKind kind, CancellationToken token = default) => _originals.Admit(async () =>
    {
        var task = await RequireTaskAsync(binding, expectedTask, token).ConfigureAwait(false);
        return kind switch
        {
            TaskRunOriginalRunControlKind.Pause => await _originals.Source(() => _tasks.PauseOriginalRunAsync(task.TaskId, task.ExecutionId, token)).ConfigureAwait(false),
            TaskRunOriginalRunControlKind.Stop => await _originals.Source(() => _tasks.StopOriginalRunAsync(task.TaskId, task.ExecutionId, token)).ConfigureAwait(false),
            _ => throw new AssistantCommandRefusedException("The requested original run control is unavailable.")
        };
    });
    public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalResumeAsync(AssistantConversationBinding binding,
        ProviderExecutionContext expectedTask, CancellationToken token = default) => _originals.Admit(async () =>
    {
        _originals.DemandObservationCapacity();
        var task = await RequireTaskAsync(binding, expectedTask, token).ConfigureAwait(false);
        var lease = await _originals.Source(() => _tasks.StartObservedOriginalRunResumeAsync(task.TaskId, task.ExecutionId, token)).ConfigureAwait(false);
        if (!_tasks.IsIssuedOriginalRunResumeObservation(lease)) throw new InvalidOperationException("The actual Task owner did not issue the original continuation observation.");
        _originals.Observe(lease.DetachAndDrainAsync);
        return lease;
    });
    public Task<AssistantDevelopmentBinding> OpenDevelopmentOriginalAsync(AssistantConversationBinding binding,
        DeveloperProjectReference reference, ProviderExecutionContext expectedTask, CancellationToken token = default) => _originals.Admit(async () =>
    {
        if (_development is null) throw new AssistantCommandRefusedException("The actual canonical Dev owner is not composed.");
        await RequireTaskAsync(binding, expectedTask, token).ConfigureAwait(false);
        var actual = await _originals.Source(() => _development.OpenOriginalAsync(binding, reference, expectedTask, token)).ConfigureAwait(false);
        if (!_development.IsIssuedOriginalBinding(actual) || !ReferenceEquals(actual.Conversation, binding) || actual.CanonicalTask != expectedTask)
            throw new InvalidOperationException("The actual Dev owner returned a different source binding or canonical Task.");
        return actual;
    });
}
