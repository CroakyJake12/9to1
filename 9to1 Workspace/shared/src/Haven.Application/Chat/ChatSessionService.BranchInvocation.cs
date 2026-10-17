using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    /// <summary>
    /// Runs the existing governed turn pipeline with an explicit original branch. The original
    /// repository atomically admits the first user write, then pins history and response writes
    /// to that branch even when another host changes its current selection during generation.
    /// Legacy SendAsync callers retain their existing current-branch behavior.
    /// </summary>
    public IAsyncEnumerable<ChatStreamEvent> SendOnBranchAsync(
        Guid branchId,
        Conversation conversation,
        string prompt,
        ModelDescriptor model,
        EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        IReadOnlyList<string>? images,
        CancellationToken cancellationToken,
        GenerationOptions? generationOptions = null,
        PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess,
        PermissionMode browserPermission = PermissionMode.FullAccess,
        ChatMessage? existingUserInput = null,
        IReadOnlyList<Guid>? attachmentIds = null,
        Func<ChatMessage, CancellationToken, Task>? inputAccepted = null)
    {
        if (branchId == Guid.Empty) throw new ArgumentException("An original branch identity is required.", nameof(branchId));
        return CreateOriginalSend(conversation, prompt, model, effort, capabilities, agentName,
            agentInstructions, duoMode, workspaceRoot, projectContext, projectInstructions, images,
            cancellationToken, prompts: null, registeredContext: null, generationOptions,
            filePermission, commandPermission, browserPermission, explicitCapabilities: null,
            availableCapabilities: null, computerUseRequest: null, executionContext: null,
            TaskRunExecutionIntent.OrdinaryConversation, originalCustody: null, expectedBranchId: branchId, existingUserInput: existingUserInput,
            branchAttachmentIds: attachmentIds is null ? [] : Array.AsReadOnly(attachmentIds.ToArray()), branchInputAccepted: inputAccepted);
    }
}
