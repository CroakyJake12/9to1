namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    // Preserve the exact current draft through its existing original write chain;
    // navigating to another shared workspace neither clears it nor retires Chat.
    internal Task PrepareWorkspaceNavigationAsync() => RunChatOriginalAsync(async () =>
    {
        var outgoing = CaptureOriginalDraftTransition();
        await FlushOriginalConversationDraftAsync(outgoing);
    });
}
