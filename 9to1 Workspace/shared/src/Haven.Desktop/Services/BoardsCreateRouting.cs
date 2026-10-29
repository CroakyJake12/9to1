using CakeOS.Apps.Boards.App;

namespace Haven.Desktop.Services;

/// <summary>The owner's board-creation flow, stated as one small rule so it can be tested and cannot drift.
///
/// Automatic provisioning of the 9to1 workspace removes the Files <em>connect</em> requirement. It does not
/// remove destination selection: creating a board still lets the owner navigate Files inside the Boards
/// window, choose a folder and click Create, and the board is saved into that folder. The recoverable-draft
/// path exists only so creation is never gated and a board is never lost, and it still places a visible
/// <c>.9to1board</c> in the workspace boards folder when it can.</summary>
public static class BoardsCreateRouting
{
    /// <summary>Creating a board offers destination selection whenever a Files workspace can be reached.</summary>
    public static bool OffersDestinationSelection(BoardsHomeBoardCreationOperation operation,
        bool selectionServiceReachable) =>
        operation == BoardsHomeBoardCreationOperation.Create && selectionServiceReachable;

    /// <summary>Fall back to the recoverable draft only when provisioning was attempted and produced no
    /// boards folder. Any other operation resumes its retained attempt instead.</summary>
    public static bool FallsBackToRecoverableDraft(BoardsHomeBoardCreationOperation operation,
        string? provisionedBoardsFolder) =>
        operation == BoardsHomeBoardCreationOperation.Create && provisionedBoardsFolder is null;
}
