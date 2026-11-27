using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    internal bool CanRestoreExploreBrief(Guid conversationId) => IsAlive && _explore?.OwnsBrief(conversationId) == true;

    internal Task<bool> RestoreExploreBriefAsync(Guid conversationId, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanRestoreExploreBrief(conversationId)) return Task.FromResult(false);
        return _work.RunAsync(async original =>
        {
            if (!CanRestoreExploreBrief(conversationId)) return false;
            await SelectAsync("explore", original, token, "home-explore");
            return original.IsAcceptedPublicationCurrent && CanRestoreExploreBrief(conversationId) && _section == "explore";
        });
    }
}
