namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    // Presentation eligibility only: a retained workspace must still have its original
    // live page owner. This neither reopens a sealed lifetime nor grants store access.
    internal bool CanRestoreWorkspace => !_disposed && !_originalWork.IsRetiring;

    // Preserve the exact current draft through its existing original write chain;
    // navigating to another shared workspace neither clears it nor retires Chat.
    internal Task PrepareWorkspaceNavigationAsync() => RunChatOriginalAsync(async () =>
    {
        var outgoing = CaptureOriginalDraftTransition();
        await FlushOriginalConversationDraftAsync(outgoing);
    });
}
