using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private void RefreshHistoryAvailability()
    {
        Set("CanRenameBoard", CanRenameBoard);
        Set("CanUndo", HasOpenBoard && CanEdit && IsEditMode && _session.CanUndo);
        Set("CanRedo", HasOpenBoard && CanEdit && IsEditMode && _session.CanRedo);
    }

    private void FinishBoardRename()
    {
        // TextChanged is posted after rendering. Commit the current input before
        // hiding it triggers a binding refresh, including a quick type-and-Enter.
        if (_attachedRoot is { } root)
        {
            if (BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "BoardTitleBox") is { } title)
                EditText("BoardTitleBox", title.Text ?? string.Empty);
            Set("BoardRenameVisible", false);
            BoardsCuiControlFinder.FindByAutomationId<MenuItem>(root, "BoardSwitcher")?.Focus();
        }
        else Set("BoardRenameVisible", false);
    }

    private async ValueTask<bool> DispatchHeaderAsync(string command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "RenameBoard":
                if (!CanRenameBoard) return true;
                Set("BoardRenameVisible", true);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || Get("BoardRenameVisible") is not true || _attachedRoot is null) return;
                    if (BoardsCuiControlFinder.FindByAutomationId<TextBox>(_attachedRoot, "BoardTitleBox") is { } title)
                    {
                        title.Focus();
                        title.SelectAll();
                    }
                }, DispatcherPriority.Loaded);
                return true;
            case "CloseAdaptiveProperties":
                SetAdaptiveProperties(false);
                return true;
            case "FinishBoardRename":
                FinishBoardRename();
                return true;
            case "ToggleTabView":
                TrySetValue("TabViewEnabled", !TabViewEnabled);
                return true;
            case "ToggleLayoutMode":
                if (CanChangeLayoutMode)
                    await SetLayoutModeAsync(IsFreeLayout ? HavenRichPageLayoutModes.Locked : HavenRichPageLayoutModes.Unlocked, cancellationToken);
                return true;
            default:
                return false;
        }
    }
}
