using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    /// <summary>
    /// The thread settings surface is authored in the shared CUI composer (the settings panel).
    /// This hook is invoked when the panel opens; it no longer depends on the retired Chatbox
    /// prefab or an obsolete HUI button anchor.
    /// </summary>
    private void OpenComposerSettings()
    {
    }

    private void SelectComposerActionMode(string mode)
    {
        if (Enum.TryParse<ChatActionMode>(mode, true, out var actionMode))
            CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, actionMode));
    }

    private void SelectComposerResponseMode(string mode)
    {
        if (Enum.TryParse<GenerativeUiResponseMode>(mode, true, out var responseMode))
            CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, responseMode));
    }
}
