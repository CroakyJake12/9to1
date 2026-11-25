using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task SwitchNativeChatModeAsync(HavenMode mode)
    {
        if (mode == HavenMode.Study)
        {
            _nativeChatSidebar?.SetMode(mode);
            await OpenStudyHomeAsync();
            await RefreshNativeChatSidebarAsync();
            ApplyShellVisualState();
            return;
        }

        if (mode == HavenMode.Studio)
        {
            await NavigateModeAsync(HavenMode.Studio, true);
            return;
        }

        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            return;
        }

        var page = _newChatPage;
        var sidebar = _nativeChatSidebar;
        sidebar?.SetMode(mode);
        var selectedSpaceId = mode == HavenMode.Chat ? sidebar?.CurrentSpaceId : null;
        bool IsCurrent() => ReferenceEquals(page, _newChatPage) && ReferenceEquals(sidebar, _nativeChatSidebar) &&
            (sidebar is null || (sidebar.CurrentMode == mode &&
                (mode != HavenMode.Chat || sidebar.CurrentSpaceId == selectedSpaceId)));

        if (!await RestoreNativeModeConversationAsync(page, _conversations, SpacesRegistry, mode, selectedSpaceId, IsCurrent))
            return;
        if (!IsCurrent()) return;
        sidebar?.SetActiveConversation(page.ConversationId, page.CurrentConversation.ContainerId);
        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }

    internal static async Task<bool> RestoreNativeModeConversationAsync(NewChatPage page, IConversationRepository conversations,
        SpaceRegistry spaces, HavenMode mode, Guid? selectedSpaceId, Func<bool> isCurrent)
    {
        var token = CancellationToken.None;
        Conversation? recent;
        if (mode == HavenMode.Chat && selectedSpaceId is { } spaceId)
        {
            if (spaceId == Guid.Empty || await spaces.ReadExistingAsync(spaceId, token) is not { IsArchived: false } || !isCurrent())
                return false;
            // A Space query is the only history source for this selection. A miss must
            // never fall through to the global list, even with malformed repository rows.
            bool IsAllowed(Conversation item) => item.SpaceId == spaceId && item.Mode == HavenMode.Chat &&
                item.Kind == ConversationKind.Chat && !item.IsArchived && !item.IsTemporary;
            recent = (await conversations.GetBySpaceAsync(spaceId, 500, token))
                .Where(IsAllowed).OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
            if (!isCurrent() || await spaces.ReadExistingAsync(spaceId, token) is not { IsArchived: false } || !isCurrent())
                return false;
            if (recent is { } observed)
            {
                var current = await conversations.GetAsync(observed.Id, token);
                recent = current is not null && current.Id == observed.Id && IsAllowed(current) ? current : null;
            }
        }
        else
        {
            recent = (await conversations.GetRecentAsync(mode, 1, token))
                .FirstOrDefault(item => !item.IsArchived && !item.IsTemporary && item.Kind != ConversationKind.Call);
        }

        // Selection can move while querying. In that case don't touch the current page.
        if (!isCurrent()) return false;
        if (recent is not null)
            await page.LoadConversationAsync(recent);
        else
            await StartNewChatWithScopeAsync(page, mode, null, null, mode == HavenMode.Chat ? selectedSpaceId : null);
        return true;
    }
}
