using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// History for the in-memory session, which is what tests and the embedded Haven host drive. The session
/// records an undo checkpoint by comparing the current document with the last checkpoint, so a mutation has to
/// call <see cref="InMemoryRichBoardSession.MarkDirty"/> after applying its change: called first, nothing is
/// different yet, nothing is recorded, and the owner's first Ctrl+Z does nothing or steps past the wrong edit.
///
/// The store-backed Boards path pushes history inside its own mutation gate and was already correct. These
/// tests exist because the lag was real in this path, and because a pinned flow that promises "one action is
/// one undo transaction" is only worth anything if both session paths honour it.
/// </summary>
public sealed class InMemoryHistoryTests
{
    [Fact]
    public async Task One_page_icon_edit_is_undoable_on_its_own()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/history-{Guid.NewGuid():N}");
        var page = session.Document.Sections[0].Pages[0];
        var iconBefore = page.IconKey;

        await session.SetPageIconEmojiAsync(page.Id, "📚", null);

        Assert.Equal("emoji", session.Document.Sections[0].Pages[0].IconKey);
        Assert.True(session.CanUndo, "a single edit has to be undoable without another edit first");
        Assert.True(await session.UndoAsync());
        Assert.Equal(iconBefore, session.Document.Sections[0].Pages[0].IconKey);
    }

    [Fact]
    public async Task One_undo_steps_back_one_edit_at_a_time()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/history-{Guid.NewGuid():N}");
        var page = session.Document.Sections[0].Pages[0];

        await session.SetPageIconAsync(page.Id, "calendar", null);
        await session.SetPageIconAsync(page.Id, "folder", "#7026D9");

        Assert.True(await session.UndoAsync());
        Assert.Equal("calendar", session.Document.Sections[0].Pages[0].IconKey);
        Assert.True(await session.UndoAsync());
        Assert.NotEqual("calendar", session.Document.Sections[0].Pages[0].IconKey);
        Assert.True(await session.RedoAsync());
        Assert.Equal("calendar", session.Document.Sections[0].Pages[0].IconKey);
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_records_no_step()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync($"memory://boards/history-{Guid.NewGuid():N}");
        var page = session.Document.Sections[0].Pages[0];

        await session.SetPageIconAsync(page.Id, page.IconKey, page.IconColor);
        Assert.False(session.CanUndo, "a no-op must not fill the undo stack with identical states");
    }
}
