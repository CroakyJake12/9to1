#if !ANDROID
using Avalonia.Threading;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Go;
using HavenOS.Apps.Assistants.Contracts;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private Task OpenOriginalRegisteredAssistantsAsync(AssistantIdentity? selectedIdentity = null) => _originalShellWork.RunAsync(original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        if (global::Avalonia.Application.Current is not global::Haven.Desktop.App owningApp)
            throw new InvalidOperationException("The actual Assistants App owner is unavailable.");
        return original.AwaitAsync(AcquireOriginalShellSynchronous(original,
            () => owningApp.OpenOriginalAssistantsForShellAsync(this, original.Token, selectedIdentity)));
    });

    private void OnNewChatAssistantCatalogueRequested(object? sender, EventArgs e)
    {
        void SetCatalogue(IReadOnlyList<AssistantDefinitionSnapshot>? assistants, string? message = null)
        {
            if (sender is NewChatPage chat) chat.SetAssistantCatalogue(assistants, message);
            else if (sender is GoPage go) go.SetAssistantCatalogue(assistants, message);
        }
        if (sender is not NewChatPage && sender is not GoPage) return;
        var selectedPage = SelectedTab?.Page;
        if (!ReferenceEquals(selectedPage, sender)) return;
        var conversationId = sender is NewChatPage currentChat ? currentChat.ConversationId : (Guid?)null;
        var requestGeneration = sender switch
        {
            NewChatPage chat => chat.AssistantCatalogueRequestGeneration,
            GoPage go => go.AssistantCatalogueRequestGeneration,
            _ => -1
        };
        bool CanPublish() => !IsDisposed && ReferenceEquals(SelectedTab?.Page, selectedPage) &&
            (sender switch
            {
                NewChatPage chat => chat.IsAssistantCatalogueOpen && chat.ConversationId == conversationId &&
                    chat.AssistantCatalogueRequestGeneration == requestGeneration,
                GoPage go => go.IsAssistantCatalogueOpen && go.AssistantCatalogueRequestGeneration == requestGeneration,
                _ => false
            });
        if (global::Avalonia.Application.Current is not global::Haven.Desktop.App owningApp)
        {
            if (CanPublish()) SetCatalogue(null, "Assistants is unavailable. Open Assistants to set up the workspace.");
            return;
        }
        StartOriginalShellEvent(async original =>
        {
            try
            {
                var assistants = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
                    () => owningApp.ReadOriginalAssistantCatalogueForShellAsync(this, original.Token)));
                if (CanPublish())
                    SetCatalogue(assistants, assistants is null
                        ? "Assistants is not ready. Open Assistants to set up the workspace."
                        : null);
            }
            catch
            {
                if (CanPublish()) SetCatalogue(null, "Could not load the configured Assistants. Open Assistants to retry.");
            }
        });
    }
}
#endif
