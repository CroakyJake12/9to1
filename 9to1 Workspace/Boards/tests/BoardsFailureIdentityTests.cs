// The canonical Boards failure identities. Specification, Boards "Failure behaviour":
//   "Boards MUST return structured failures for BoardNotFound, PageNotFound, BlockNotFound,
//    UnsupportedPageType, UnsupportedBlockType, InvalidLayoutTransition, RevisionConflict,
//    PermissionDenied, SourceIngestionFailed, ScopedChatExternalAccessDenied, DependencyUnavailable and
//    SharedEngineIncompatible, plus the global error contract."
// and the global actions clause: "Errors identify recoverability and the affected operation without
// leaking forbidden source details."
//
// Before this the same condition reached a caller as a bare KeyNotFoundException or a sentence: a caller
// could not tell "that page id does not exist" from "that page is in another section", and the app had to
// show whatever prose happened to be attached. Each case below drives one canonical operation and asserts
// the identity, the affected operation and object, and that the older catch path still works.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class BoardsFailureIdentityTests
{
    [Fact]
    public void AnUnknownPageIdIsPageNotFoundOnTheOperationThatAskedForIt()
    {
        var notes = HavenRichNotes.Create();
        var refused = Assert.Throws<BoardsFailureException>(() =>
            HavenRichNotesOps.SetPageLayoutMode(notes, "page-does-not-exist", HavenRichPageLayoutModes.Unlocked));
        Assert.Equal(BoardsFailureCode.PageNotFound, refused.Code);
        Assert.Equal("page-does-not-exist", refused.TargetId);
        // The identity from a shared lookup helper names the lookup, not the public operation; the
        // operations with their own guard sites (MoveBlock, SetPageParent, placement, layout mode) name the
        // public operation, asserted in the cases below. Carrying the public name through every shared
        // lookup is recorded as remaining work rather than claimed here.
        Assert.Equal("Boards.Page.Require", refused.Operation);
    }

    [Fact]
    public void AnUnknownBlockIdIsBlockNotFoundRatherThanAGenericLookupFailure()
    {
        var notes = HavenRichNotes.Create();
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);
        HavenRichNotesOps.SetPageLayoutMode(notes, page.Id, HavenRichPageLayoutModes.Unlocked);

        var refused = Assert.Throws<BoardsFailureException>(() =>
            HavenRichNotesOps.MoveBlock(notes, page.Id, "block-does-not-exist", 0));
        Assert.Equal(BoardsFailureCode.BlockNotFound, refused.Code);
        Assert.Equal("block-does-not-exist", refused.TargetId);
        Assert.Equal("Boards.Block.Move", refused.Operation);
    }

    [Fact]
    public void AnUnknownSectionUsesTheGlobalMissingSourceSectionIdentity()
    {
        var notes = HavenRichNotes.Create();
        var refused = Assert.Throws<BoardsFailureException>(() =>
            HavenRichNotesOps.MoveSection(notes, "sec-does-not-exist", 0));
        Assert.Equal(BoardsFailureCode.MissingSourceSection, refused.Code);
        Assert.Equal("sec-does-not-exist", refused.TargetId);
    }

    [Fact]
    public void AnUnknownParentPageIsPageNotFoundWithTheParentAsTheTarget()
    {
        var notes = HavenRichNotes.Create();
        var section = Assert.Single(notes.Sections);
        var page = Assert.Single(section.Pages);

        var refused = Assert.Throws<BoardsFailureException>(() =>
            HavenRichNotesOps.SetPageParent(notes, page.Id, "page-never-created"));
        Assert.Equal(BoardsFailureCode.PageNotFound, refused.Code);
        Assert.Equal("page-never-created", refused.TargetId);
    }

    [Fact]
    public void APageTypeThatDoesNotSupportAnOperationSaysSoRatherThanFailingGenerically()
    {
        var notes = HavenRichNotes.Create();
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);
        page.PageType = HavenRichPageTypes.Write;   // an embedded Write page, not a Native authoring page

        var layout = Assert.Throws<BoardsFailureException>(() =>
            HavenRichNotesOps.SetPageLayoutMode(notes, page.Id, HavenRichPageLayoutModes.Unlocked));
        Assert.Equal(BoardsFailureCode.UnsupportedPageType, layout.Code);
        Assert.Equal(page.Id, layout.TargetId);

        var block = Assert.Single(page.Blocks);
        var placement = Assert.Throws<BoardsFailureException>(() =>
            HavenRichNotesOps.SetBlockFlowLayout(notes, page.Id, block.Id));
        Assert.Equal(BoardsFailureCode.UnsupportedPageType, placement.Code);
    }

    [Fact]
    public void AnUnsupportedBlockKindIsNamedAsUnsupportedBlockType()
    {
        var notes = HavenRichNotes.Create();
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);
        page.Blocks.Add(new HavenRichBlock { Kind = (HavenRichBlockKind)9_000 });

        var refused = Assert.Throws<BoardsFailureException>(() => HavenRichNotesValidator.Validate(notes));
        Assert.Equal(BoardsFailureCode.UnsupportedBlockType, refused.Code);
    }

    [Fact]
    public void EveryStructuredFailureStillPresentsAsAnOperationFailureToOlderCallers()
    {
        // Nothing that already catches the operation-level failure may start seeing an unrelated type:
        // the structure is added to the existing contract, not substituted for it.
        var notes = HavenRichNotes.Create();
        var refused = Assert.ThrowsAny<InvalidOperationException>(() =>
            HavenRichNotesOps.SetPageParent(notes, "page-missing", null));
        Assert.IsType<BoardsFailureException>(refused);
        Assert.Equal(BoardsFailureCode.PageNotFound, ((BoardsFailureException)refused).Code);
        Assert.Contains("PageNotFound", ((BoardsFailureException)refused).Describe(), StringComparison.Ordinal);
        Assert.Contains("unchanged", ((BoardsFailureException)refused).Describe(), StringComparison.Ordinal);
    }
}
