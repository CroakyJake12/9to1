// Undo granularity, measured on the running app first. On 1 November 2026 the standalone editor typed
// "ZZTOP" into a block and then pressed Ctrl+Z: the physical file stepped the block back one character per
// press (SPLITMEZZTOP -> SPLITMEZZTO -> SPLITMEZZT -> SPLITMEZZ, richNotes.version 140 -> 138 -> 136 -> 134),
// because the editor merges every keystroke into the canonical document and each merge took its own history
// entry. One undo boundary has to be the edit the user actually made, not one character of it.
//
// The grouping rule lives in the session (RichBoardSession.HistoryCoalescingWindow + the historyGroup on
// MutateAsync); the caller decides what counts as one edit. These cases drive the session directly so the
// rule itself is pinned, and BoardsBlockClickFocusTests pins the user-visible half through the real editor.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class BoardsHistoryGranularityTests
{
    private const string TypingGroup = "text-edit:block-of-the-paragraph";

    [Fact]
    public async Task ConsecutiveKeystrokesOfTheSameEditUndoAsOneStep()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await RichBoardSession.CreateNewAsync(store, "Granularity");
            foreach (var letter in new[] { "H", "E", "L", "L", "O" })
                await session.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks[0].PlainText += letter,
                    CancellationToken.None, TypingGroup);

            Assert.Equal("HELLO", Text(session));
            Assert.True(await session.UndoAsync(), "one undo press must take back the whole typed run");
            Assert.Equal(string.Empty, Text(session));
            Assert.True(await session.RedoAsync(), "the run must come back as one step too");
            Assert.Equal("HELLO", Text(session));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task AGroupedEditAndAnUngroupedEditStaySeparateSteps()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await RichBoardSession.CreateNewAsync(store, "Granularity");
            var blockId = session.Rich.Sections[0].Pages[0].Blocks[0].Id;
            var pageId = session.Rich.Sections[0].Pages[0].Id;

            await session.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks[0].PlainText += "AB",
                CancellationToken.None, TypingGroup);
            // Structure is never grouped: this is a separate user action and must be separately undoable.
            await session.MutateAsync(rich => HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Divider));

            Assert.Equal("AB", Text(session, blockId));
            var inserted = Count(session);
            Assert.Equal(2, inserted);

            // The structural step undoes alone (it carries no group), and the typed run is still intact.
            Assert.True(await session.UndoAsync());
            Assert.Equal(1, Count(session));
            Assert.Equal("AB", Text(session, blockId));

            // Then the whole typed run goes as one boundary.
            Assert.True(await session.UndoAsync());
            Assert.Equal(string.Empty, Text(session, blockId));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task AWindowOfZeroRestoresOneStepPerMerge()
    {
        // The other half of the contract: hosts that disable grouping get the old per-merge history, so the
        // coalescing is provably an opt-in window rather than a lost capability.
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await RichBoardSession.CreateNewAsync(store, "Granularity");
            session.HistoryCoalescingWindow = TimeSpan.Zero;

            foreach (var letter in new[] { "A", "B" })
                await session.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks[0].PlainText += letter,
                    CancellationToken.None, TypingGroup);
            Assert.Equal("AB", Text(session));

            Assert.True(await session.UndoAsync());
            Assert.Equal("A", Text(session));
            Assert.True(await session.UndoAsync());
            Assert.Equal(string.Empty, Text(session));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task AnExpiredWindowStartsANewStep()
    {
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await RichBoardSession.CreateNewAsync(store, "Granularity");
            session.HistoryCoalescingWindow = TimeSpan.FromMilliseconds(1);

            await session.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks[0].PlainText += "A",
                CancellationToken.None, TypingGroup);
            await Task.Delay(40);
            await session.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks[0].PlainText += "B",
                CancellationToken.None, TypingGroup);

            Assert.Equal("AB", Text(session));
            Assert.True(await session.UndoAsync());
            // The second run began after the window expired, so it undoes alone.
            Assert.Equal("A", Text(session));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task ARefusedMutationStillTakesNoHistoryStep()
    {
        // Grouping must not turn a refusal into an invisible boundary: the rejected attempt leaves the head
        // exactly where it was (contract "a refused attempt must not be observable").
        var root = NewDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await RichBoardSession.CreateNewAsync(store, "Granularity");
            await session.MutateAsync(rich => rich.Sections[0].Pages[0].Blocks[0].PlainText += "kept",
                CancellationToken.None, TypingGroup);
            var before = Text(session);

            await Assert.ThrowsAnyAsync<Exception>(() => session.MutateAsync(
                rich => rich.Sections[0].Pages[0].Blocks[0].Kind = (HavenRichBlockKind)9_000,
                CancellationToken.None, TypingGroup));

            var undone = await session.UndoAsync();
            Assert.True(undone);
            Assert.Equal(string.Empty, Text(session));
            Assert.NotEqual(before, Text(session));
        }
        finally { DeleteDirectory(root); }
    }

    // Read through the session each time: an undo installs a freshly deserialized head, so a held
    // HavenRichNotes/block reference belongs to the superseded graph and cannot show the restore.
    private static string Text(RichBoardSession session) =>
        session.Rich.Sections[0].Pages[0].Blocks[0].PlainText;

    private static string Text(RichBoardSession session, string blockId) =>
        session.Rich.Sections[0].Pages[0].Blocks.Single(block => block.Id == blockId).PlainText;

    private static int Count(RichBoardSession session) => session.Rich.Sections[0].Pages[0].Blocks.Count;

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-history-granularity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}
