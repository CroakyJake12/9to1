using CakeOS.Apps.Boards.App;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Pins the owner's corrected board-creation flow as an explicit rule, because no existing test
/// touched the create-operation routing at all: automatic provisioning of the 9to1 workspace removes the
/// Files *connect* requirement, it does not remove destination selection when creating a board, and the
/// draft path exists only so creation can never be blocked or lost.
///
/// Owner's words, 29 October: "you click create new board - it opens files (inside of the boards window)
/// and then you navigate through files, select a folder, and click 'create' - the board is saved to that
/// folder", corrected later the same day: "automatic provisioning of Documents/9to1 removes the Files
/// connection requirement, but does not cancel the requested destination-selection flow when creating a
/// board."</summary>
public sealed class BoardsCreateDestinationRoutingTests
{
    [Fact]
    public void Create_with_a_reachable_files_workspace_always_offers_destination_selection()
    {
        Assert.True(BoardsCreateRouting.OffersDestinationSelection(BoardsHomeBoardCreationOperation.Create, true));
    }

    [Fact]
    public void Create_without_a_reachable_files_service_uses_the_draft_path_instead_of_a_gate()
    {
        // The owner must never be blocked from creating a board: no selection surface means the board is
        // still created, as a recoverable draft that Boards also tries to place in the workspace folder.
        Assert.False(BoardsCreateRouting.OffersDestinationSelection(BoardsHomeBoardCreationOperation.Create, false));
    }

    [Fact]
    public void Provisioning_success_keeps_selection_offered_and_suppresses_the_draft_fallback()
    {
        Assert.False(BoardsCreateRouting.FallsBackToRecoverableDraft(
            BoardsHomeBoardCreationOperation.Create, "/home/user/Documents/9to1/Boards"));
    }

    [Fact]
    public void Only_a_failed_provision_falls_back_to_the_recoverable_draft()
    {
        Assert.True(BoardsCreateRouting.FallsBackToRecoverableDraft(
            BoardsHomeBoardCreationOperation.Create, null));
    }

    [Theory]
    [InlineData(BoardsHomeBoardCreationOperation.RetrySettlement)]
    [InlineData(BoardsHomeBoardCreationOperation.InspectOutcome)]
    [InlineData(BoardsHomeBoardCreationOperation.OpenRecordedExisting)]
    [InlineData(BoardsHomeBoardCreationOperation.RetryOpenExisting)]
    public void Resuming_a_retained_create_never_starts_a_second_board_or_re_asks_for_a_folder(
        BoardsHomeBoardCreationOperation operation)
    {
        Assert.False(BoardsCreateRouting.OffersDestinationSelection(operation, true));
        Assert.False(BoardsCreateRouting.FallsBackToRecoverableDraft(operation, null));
    }
}
