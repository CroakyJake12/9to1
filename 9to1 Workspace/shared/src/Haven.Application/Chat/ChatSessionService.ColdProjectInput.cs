using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    /// <summary>Genuine new project Tasks input. The configured Home issuer supplies the
    /// private project input; public paths/IDs cannot substitute. Ordinary Send stays separate.</summary>
    public Task<TaskRunOriginalInitialChatObservationLease> StartObservedOriginalProjectTaskSendAsync(
        Conversation conversation, ITaskRunColdOriginalProjectInput sameProjectInput,
        string prompt, ModelDescriptor model, EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities, string agentName, string agentInstructions,
        DuoMode duoMode, CancellationToken observationCancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts = null, GenerationOptions? generationOptions = null,
        PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess,
        PermissionMode browserPermission = PermissionMode.FullAccess,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities = null,
        IReadOnlyCollection<ActiveCapability>? availableCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(sameProjectInput);
        return StartObservedOriginalTaskSendCore(conversation, prompt, model, effort, capabilities,
            agentName, agentInstructions, duoMode, null, null, null, null, observationCancellationToken,
            prompts, null, generationOptions, filePermission, commandPermission, browserPermission,
            explicitCapabilities, availableCapabilities, null, sameProjectInput);
    }

    private ITaskRunColdProjectResourceSource RequireOriginalColdProjectSource() =>
        (taskCoordinator?.OriginalColdRecoveryJournal as ITaskRunColdConfiguredProjectResourceSource)
            ?.RequireOriginalProjectResourceSource()
        ?? throw new InvalidOperationException("The SAME protected journal has no configured genuine Home project input source.");

    private InitialTaskInput BindOriginalInitialProjectInput(TaskRunProcessStageCustody stage,
        InitialTaskInput input, ITaskRunColdOriginalProjectInput? projectInput)
    {
        if (projectInput is null) return input;
        var source = RequireOriginalColdProjectSource();
        if (!source.IsIssuedOriginalProjectInput(projectInput))
            throw new UnauthorizedAccessException("The configured Home source did not issue this original project input.");
        if (!ReferenceEquals(projectInput.OriginalConversation, input.Conversation)
            || projectInput.OriginalContainer.Id != input.Conversation.ContainerId
            || projectInput.OriginalPreparation is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The exact source-selected original project Conversation/container/preparation changed.");
        stage.RetainSource(projectInput.OriginalPreparation);
        var identity = projectInput.OriginalIdentity;
        return input with { WorkspaceRoot = identity.CanonicalRoot,
            ProjectContext = identity.OriginalProjectContextJson,
            ProjectInstructions = identity.OriginalContainerInstructions, ProjectInput = projectInput,
            ProjectSource = source };
    }

    private static TaskRunColdChatInput DetachOriginalColdInput(InitialTaskInput input) => new(
        input.Conversation, input.Prompt, input.Model, input.Effort,
        Array.AsReadOnly(input.Capabilities.ToArray()), input.AgentName, input.AgentInstructions,
        input.DuoMode, input.WorkspaceRoot, input.ProjectContext, input.ProjectInstructions,
        input.Images, input.Prompts is null ? null : Array.AsReadOnly(input.Prompts.ToArray()),
        input.RegisteredContext, input.GenerationOptions, input.FilePermission, input.CommandPermission,
        input.BrowserPermission, input.ExplicitCapabilities is null ? null : Array.AsReadOnly(input.ExplicitCapabilities.ToArray()),
        input.AvailableCapabilities is null ? null : Array.AsReadOnly(input.AvailableCapabilities.ToArray()), input.ComputerUseRequest);

    private static async Task ValidateOriginalInitialProjectInputAsync(HostedInitialTaskSend original,
        InitialTaskInput input, CancellationToken token)
    {
        if (input.ProjectInput is not { } projectInput) return;
        var stage = original.Stage!;
        var source = input.ProjectSource ?? throw new InvalidOperationException("No actual initial project source is retained.");
        if (!stage.Invoke(() => source.IsIssuedOriginalProjectInput(projectInput)))
            throw new UnauthorizedAccessException("The original project input source has retired.");
        await stage.Await(() => source.ValidateOriginalProjectInputWithinSourceAsync(projectInput,
            DetachOriginalColdInput(input), callback => stage.Invoke(() => { callback(); return true; }),
            stage.RetainSource, token)).ConfigureAwait(false);
    }
}
