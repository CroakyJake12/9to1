using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Adapts the existing Chat session pipeline to the Chat Space's turn executor contract.
///
/// This is a deliberately thin bridge: it resolves pending attachment IDs to prompt context,
/// forwards the turn to <see cref="ChatSessionService"/>, and drains the returned event stream
/// so the service completes persistence. The Chat Space controller refreshes from repositories
/// afterwards, so streaming deltas are not pushed live here.
/// </summary>
public sealed class HavenChatSpaceTurnExecutor(
    ChatSessionService sessions,
    UserPreferencesService preferences,
    IMessageAttachmentService? attachments = null) : IChatSpaceTurnExecutor
{
    private readonly ChatSessionService _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly UserPreferencesService _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    private readonly IMessageAttachmentService? _attachments = attachments;

    public async Task ExecuteAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (prompt, images) = await ResolveAttachmentsAsync(request, cancellationToken).ConfigureAwait(false);

        var stream = _sessions.SendAsync(
            request.Conversation,
            prompt,
            request.Model,
            _preferences.DefaultEffort,
            [],
            agentName: "Haven",
            agentInstructions: string.Empty,
            duoMode: DuoMode.Solo,
            workspaceRoot: null,
            projectContext: null,
            projectInstructions: null,
            images: images,
            cancellationToken: cancellationToken,
            generationOptions: _preferences.GenerationOptions,
            filePermission: _preferences.FilePermission,
            commandPermission: _preferences.CommandPermission,
            browserPermission: _preferences.BrowserPermission);

        await foreach (var _ in stream.WithCancellation(cancellationToken).ConfigureAwait(false)) { }
    }

    private async Task<(string Prompt, IReadOnlyList<string>? Images)> ResolveAttachmentsAsync(
        ChatSpaceTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AttachmentIds.Count == 0 || _attachments is null)
            return (request.Prompt, null);

        var context = await _attachments.BuildPromptContextAsync(
            request.Conversation.Id,
            request.AttachmentIds,
            options: null,
            cancellationToken).ConfigureAwait(false);

        var prompt = request.Prompt;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.ExtractedText)) parts.Add(context.ExtractedText);
        if (context.Notices.Count > 0) parts.Add("Attachment processing:\n" + string.Join("\n", context.Notices));
        if (parts.Count > 0) prompt += "\n\n" + string.Join("\n\n", parts);

        return (prompt, context.ImageBase64.Count == 0 ? null : context.ImageBase64);
    }
}
