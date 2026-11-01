using Avalonia.Controls;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Keeps the canonical session history as the single undo owner for every Boards text surface.
///
/// Avalonia's TextBox keeps its own character-level undo stack (UndoLimit defaults to 10). Inside a board
/// that creates a second, invisible history beside the canonical snapshot stack, so Ctrl+Z removes typed
/// characters while the board revision stays where it was - the user believes they undid an edit and did
/// not, and a later canonical undo then removes something else. The block editors already close this door
/// (BlockRenderer sets UndoLimit = 0, measured on 1 November 2026 in the running app).
///
/// Measured on the running app the same day, the page-title input still behaved the old way: two Ctrl+Z
/// presses turned the stored title "Start here BF07OK" into "Start here BF07" with no canonical board
/// revision in between, and headless the property itself reads 10 (see BoardsBlockClickFocusTests).
///
/// Filter fields are deliberately not part of this rule: an insert-menu search box holds a query, not
/// document content, so its own undo is harmless and useful.
/// </summary>
internal static class BoardsTextUndoOwner
{
    /// <summary>Disables the control-local undo stack and returns the same control for chaining.</summary>
    public static T SingleUndoOwner<T>(T box) where T : TextBox
    {
        box.UndoLimit = 0;
        return box;
    }
}
