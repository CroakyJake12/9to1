using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private void RefreshSnapState()
    {
        Set("IsSnapEnabled", IsSnapEnabled);
        Set("SnapLabel", SnapLabel);
        Set("SnapAccessibleName", SnapAccessibleName);
        Set("CanChangeSnap", CanChangeSnap);
    }

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
                        // This is the first moment the board-name editor is live, and the CUI loader may
                        // materialise it after Attach walked the tree, so the single-undo-owner rule is
                        // applied here too (see BoardsTextUndoOwner).
                        BoardsTextUndoOwner.SingleUndoOwner(title);
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
            case "SetEditModeOn" or "SetEditModeOff":
                // The persistent Edit/View switch in the top-right chrome. Clicking the mode that is
                // already active must do nothing: an editor whose own label flips the page state when
                // the user is only trying to confirm which mode they are in is a data risk in View mode.
                var wantEdit = command == "SetEditModeOn";
                if (CanToggleEditMode && wantEdit != IsEditMode)
                    SetEditMode(wantEdit);
                return true;
            case "ToggleLayoutMode":
                if (CanChangeLayoutMode)
                    await SetLayoutModeAsync(IsFreeLayout ? HavenRichPageLayoutModes.Locked : HavenRichPageLayoutModes.Unlocked, cancellationToken);
                return true;
            case "ToggleSnap":
                // The third independent dimension in the reference: it changes where a spatial edit
                // commits. Turning it is a document change, so it needs the edit permission, but it is
                // not gated on the layout mode or on View mode's own lock.
                if (_session is ContractSessionAdapter snapAdapter && CanChangeSnap)
                {
                    await snapAdapter.SetSnapAsync(SelectedPageId, !IsSnapEnabled, cancellationToken);
                    RefreshSnapState();
                }
                return true;
            default:
                return false;
        }
    }
}
