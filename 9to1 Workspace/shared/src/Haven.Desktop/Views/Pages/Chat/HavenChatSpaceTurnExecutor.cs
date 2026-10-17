using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Adapts the existing Chat session pipeline to the Chat Space's turn executor contract.
///
/// This bridge resolves pending attachment IDs to prompt context, forwards the turn to
/// <see cref="ChatSessionService"/>, and forwards streaming deltas so the CUI surface can render
/// incremental responses and reasoning. Thread settings are enforced by canonical capability and
/// GenUI directive contracts rather than prompt hints alone.
/// </summary>
public sealed class HavenChatSpaceTurnExecutor(
    ChatSessionService sessions,
    UserPreferencesService preferences,
    ICapabilityRepository? capabilities = null,
    IMessageAttachmentService? attachments = null) : IChatSpaceTurnExecutor
{
    private readonly ChatSessionService _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly UserPreferencesService _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    private readonly ICapabilityRepository? _capabilities = capabilities;
    private readonly IMessageAttachmentService? _attachments = attachments;

    public async Task ExecuteAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (prompt, images) = await ResolveAttachmentsAsync(request, cancellationToken).ConfigureAwait(false);
        var activeCapabilities = await ResolveActiveCapabilitiesAsync(request, cancellationToken).ConfigureAwait(false);

        var stream = _sessions.SendOnBranchAsync(
            request.BranchId ?? throw new InvalidOperationException("The original conversation branch is required before sending."),
            request.Conversation,
            prompt,
            request.Model,
            _preferences.DefaultEffort,
            activeCapabilities,
            agentName: "Haven",
            agentInstructions: BuildAgentInstructions(request),
            duoMode: request.DuoMode,
            workspaceRoot: null,
            projectContext: null,
            projectInstructions: null,
            images: images,
            cancellationToken: cancellationToken,
            generationOptions: _preferences.GenerationOptions,
            filePermission: _preferences.FilePermission,
            commandPermission: _preferences.CommandPermission,
            browserPermission: _preferences.BrowserPermission,
            existingUserInput: request.ExistingUserInput,
            attachmentIds: request.ExistingUserInput is null ? request.AttachmentIds : [],
            inputAccepted: request.InputAccepted);

        await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(item);
        }
    }

    private async Task<(string Prompt, IReadOnlyList<string>? Images)> ResolveAttachmentsAsync(
        ChatSpaceTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AttachmentIds.Count == 0)
            return (request.Prompt, null);

        if (_attachments is null)
            throw new InvalidOperationException("The original attachment service is unavailable for this request.");

        var context = await _attachments.BuildPromptContextAsync(
            request.Conversation.Id,
            request.AttachmentIds,
            options: null,
            cancellationToken).ConfigureAwait(false);

        var prompt = request.Prompt;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.ExtractedText)) parts.Add(context.ExtractedText);
        if (context.Notices.Count > 0) parts.Add("Attachment processing:\n" + string.Join("\n", context.Notices));
        if (request.ExistingUserInput is null && parts.Count > 0) prompt += "\n\n" + string.Join("\n\n", parts);

        return (prompt, context.ImageBase64.Count == 0 ? null : context.ImageBase64);
    }

    private async Task<IReadOnlyCollection<ActiveCapability>> ResolveActiveCapabilitiesAsync(
        ChatSpaceTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (_capabilities is null || request.ActionMode == ChatActionMode.JustChat)
            return Array.Empty<ActiveCapability>();

        var definitions = await _capabilities.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var platform = OperatingSystem.IsWindows() ? CapabilityPlatform.Windows
            : OperatingSystem.IsLinux() ? CapabilityPlatform.Linux
            : OperatingSystem.IsAndroid() ? CapabilityPlatform.Android
            : CapabilityPlatform.None;

        var allowedRisk = request.ActionMode switch
        {
            ChatActionMode.AllowBasicActions => (CapabilityRiskClass?)CapabilityRiskClass.Low,
            _ => null
        };

        var candidates = definitions
            .Where(definition => definition.IsEnabled && definition.IsAgentUsable)
            .Where(definition => (definition.Platforms & platform) != 0 || platform == CapabilityPlatform.None)
            .Where(definition => allowedRisk is null || definition.RiskClass <= allowedRisk.Value)
            .Select(ActiveCapability.FromDefinition)
            .ToArray();

        var plan = _sessions.GetToolAvailability(
            request.Conversation.Mode,
            workspaceRoot: null,
            candidates,
            _preferences.FilePermission,
            _preferences.CommandPermission,
            _preferences.BrowserPermission);

        return plan.FilterCapabilities(candidates);
    }

    private static string BuildAgentInstructions(ChatSpaceTurnRequest request) =>
        GenUiChatDirectiveParser.ModelInstructionFor(request.ResponseMode);
}
