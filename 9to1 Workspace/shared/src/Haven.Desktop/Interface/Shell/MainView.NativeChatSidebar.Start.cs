using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private Task StartNativeConversationAsync(HavenMode mode, Guid? chatGroupId) =>
        StartNativeConversationInSpaceAsync(mode, chatGroupId, _nativeChatSidebar?.CurrentSpaceId);

    private async Task StartNativeConversationInSpaceAsync(HavenMode mode, Guid? chatGroupId, Guid? spaceId)
    {
        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            throw new InvalidOperationException("The Chat workspace could not be opened.");
        }

        Guid? lessonId = null;
        if (mode == HavenMode.Study && chatGroupId is Guid subjectId)
        {
            lessonId = (await _containers.GetLessonsAsync(subjectId, CancellationToken.None))
                .OrderBy(item => item.SortOrder)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefault();
        }

        await _newChatPage.StartFreshConversationAsync(mode, chatGroupId, lessonId, spaceId);
        _nativeChatSidebar?.SetMode(mode);
        _nativeChatSidebar?.SetActiveConversation(_newChatPage.CurrentConversation.Id, chatGroupId);
        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }
}
