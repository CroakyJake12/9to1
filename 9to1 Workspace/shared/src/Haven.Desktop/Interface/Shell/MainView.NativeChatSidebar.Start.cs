using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task StartNativeConversationAsync(HavenMode mode, Guid? chatGroupId)
    {
        var selectedSpaceId = _nativeChatSidebar?.CurrentSpaceId;
        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            return;
        }

        Guid? lessonId = null;
        if (mode == HavenMode.Study && chatGroupId is Guid subjectId)
        {
            lessonId = (await _containers.GetLessonsAsync(subjectId, CancellationToken.None))
                .OrderBy(item => item.SortOrder)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefault();
        }

        await StartNewChatWithScopeAsync(_newChatPage, mode, chatGroupId, lessonId, selectedSpaceId);
        _nativeChatSidebar?.SetMode(mode);
        _nativeChatSidebar?.SetActiveConversation(_newChatPage.CurrentConversation.Id, chatGroupId);
        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }

    // Bind the Space to the newly issued live identity, never via a second repository write.
    // The page decides when the new ordinary conversation actually becomes durable.
    internal static Task StartNewChatWithScopeAsync(NewChatPage page, HavenMode mode, Guid? groupId,
        Guid? lessonId, Guid? spaceId) => page.StartFreshConversationAsync(mode, groupId, lessonId, spaceId);
}
