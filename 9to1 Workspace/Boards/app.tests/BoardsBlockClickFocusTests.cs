// Regression coverage for BF-07 ("typing and block boundaries") and BF-12 ("popover/focus lifecycle"),
// written from what was measured on the running standalone Boards app on 1 November 2026.
//
// On screen, against the physical .9to1board file, in a fresh instance of the current build
// (receipts L08-L11, frames 125-probe-block-row.png and 126-crop-title-and-block.png):
//   * clicking the paragraph text of a page did not place a caret in that block - the block's stored text
//     stayed byte-identical;
//   * keyboard focus stayed on the *page title* input, so the characters typed afterwards edited the
//     page's identity instead of its content (the title went from "Start here" to
//     "Stqzart here BF07k");
//   * the page-title input also answered Ctrl+Z with its own character-level undo stack
//     ("Start here BF07OK" -> "Start here BF07"), which is the second undo system the block editors
//     were fixed to remove (BlockRenderer sets UndoLimit = 0 because the canonical session history is
//     the only undo owner).
// The cases below drive the real mounted surface with real pointer and text input, and say what they
// find rather than what the code appears to intend.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using System.IO;
using System.Threading;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsBlockClickFocusTests
{
    [Fact]
    public Task ClickingParagraphTextPutsTheCaretInTheClickedBlock() => OnSurfaceAsync((fixture, window) =>
    {
        var editor = fixture.ParagraphEditors[0];
        Assert.False(editor.IsFocused, "the mounted surface starts with no focused text control");

        var point = CenterInWindow(editor, window);
        Click(window, point);

        Assert.True(editor.IsFocused,
            $"clicking the paragraph text must focus that block. Visuals under {point} (topmost first): " +
            $"{Chain(window, point)}; focus was on {Describe(Focused(window))}");
    });

    [Fact]
    public Task ClickingParagraphTextTakesFocusFromThePageTitleSoTypedTextReachesTheBlock() =>
        OnSurfaceAsync((fixture, window) =>
        {
            // The measured native sequence: the page title already had focus (the user had renamed the
            // page), the user then clicked the paragraph below it and typed.
            var title = fixture.PageTitleBox;
            Assert.True(title.Focus(), "the page title is focusable");
            title.CaretIndex = title.Text!.Length;
            fixture.Settle();

            var editor = fixture.ParagraphEditors[0];
            var pageId = fixture.Session.Document.Sections[0].Pages[0].Id;
            var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
            var titleBefore = (string)fixture.ViewModel.Get("PageTitle")!;
            var blockBefore = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Text;

            var point = CenterInWindow(editor, window);
            Click(window, point);

            Assert.False(title.IsFocused,
                $"the page title kept keyboard focus after a click inside the document. Visuals under " +
                $"{point} (topmost first): {Chain(window, point)}; focus was on {Describe(Focused(window))}");
            Assert.True(editor.IsFocused, $"the clicked block did not take focus; focus was {Describe(Focused(window))}");

            window.KeyTextInput("X");
            fixture.Settle();

            var page = fixture.Session.Document.Sections[0].Pages[0];
            Assert.Equal(blockBefore + "X", page.Blocks.Single(block => block.Id == blockId).Text);
            Assert.Equal(titleBefore, page.Title);
            Assert.Equal(titleBefore, (string)fixture.ViewModel.Get("PageTitle")!);
            Assert.Equal(pageId, fixture.ViewModel.SelectedPageId);
        });

    [Fact]
    public Task ThePageTitleInputIsNotASecondUndoSystemBesideTheCanonicalBoardHistory() =>
        OnSurfaceAsync((fixture, window) =>
        {
            // "The canonical board history is the only undo owner here (RichBoardSession snapshot stack)"
            // is the rule the block editors were fixed to honour on 1 November. The page title and board
            // title inputs are authored in CUI, so nothing carried that rule over: measured natively,
            // Ctrl+Z with the title focused removed typed characters and never consulted the board
            // history. The shared control default is what has to change.
            foreach (var (name, box) in new (string, TextBox)[]
            {
                ("PageTitleBox", fixture.PageTitleBox),
                ("BoardTitleBox", fixture.BoardTitleBox),
            })
                Assert.Equal(0, box.UndoLimit);   // Avalonia's default is 10, i.e. a second history

            var title = fixture.PageTitleBox;
            Assert.True(title.Focus());
            var documentBefore = fixture.Snapshot();
            window.KeyTextInput("Z");
            fixture.Settle();

            Assert.True(documentBefore != fixture.Snapshot(),
                "typing in the page title changed no canonical document state, so the board history cannot undo it");
            Assert.True(fixture.Session.CanUndo, "a page-title edit must be recorded in the canonical history");
        });

    /// <summary>
    /// BF-19's text half, measured natively first: in instance run20 (receipts M11-M36, frames 137-150,
    /// physical file /tmp/opencode/qwen-native-20261101/home7/Documents/9to1/Boards/Untitled board.9to1board)
    /// text typed into a block reached the physical file ("AAASOFT1\nSOFT2M9TAIL", block-47b0537df78b), and
    /// then **four Ctrl+Z presses and six toolbar Undo clicks changed nothing at all** - the live page still
    /// showed every block and the file stayed at richNotes.version 130 with the same four blocks, while the
    /// undo control stayed enabled and redo stayed disabled. The cases below separate where that breaks.
    /// </summary>
    [Fact]
    public Task UndoThroughTheAppCommandRevertsTypedBlockText() => OnSurfaceAsync((fixture, window) =>
    {
        var pageId = fixture.Session.Document.Sections[0].Pages[0].Id;
        var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string Text() => fixture.Session.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Text;

        var editor = fixture.ParagraphEditors[0];
        var before = Text();
        Click(window, CenterInWindow(editor, window));
        window.KeyTextInput("Q");
        fixture.Settle();

        var typed = Text();
        Assert.Equal(before + "Q", typed);      // the typing half is already proven by case 2
        Assert.True(fixture.Session.CanUndo, "typing into a block must be recorded in the canonical history");

        Pump(fixture.ViewModel.DispatchAsync("Undo", null).AsTask(),
            "the Undo command the toolbar button and Ctrl+Z both dispatch");
        fixture.Settle();

        var afterUndo = Text();
        var live = fixture.ParagraphEditors.Length > 0 ? fixture.ParagraphEditors[0].Text : "(no editor)";
        Assert.True(afterUndo == before,
            $"Undo left the canonical text unchanged. block {blockId}: before={Quote(before)} " +
            $"afterTyping={Quote(typed)} afterUndo={Quote(afterUndo)}; CanUndo={fixture.Session.CanUndo} " +
            $"CanRedo={fixture.Session.CanRedo}; live editor text={Quote(live)}; page {pageId}");
        Assert.Equal(before, live);             // the user must see the reverted content, not just the file
    });

    [Fact]
    public Task TheCanonicalHistoryRevertsTextTypedThroughTheEditor() => OnSurfaceAsync((fixture, window) =>
    {
        // The same edit as the case above, with the session's own history called directly instead of the
        // app's Undo command: if this half works while the case above fails, the loss is in the application
        // layer (command routing, or the live editor writing the undone text straight back into the view).
        var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string Text() => fixture.Session.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Text;

        var editor = fixture.ParagraphEditors[0];
        var before = Text();
        Click(window, CenterInWindow(editor, window));
        window.KeyTextInput("Q");
        fixture.Settle();

        var typed = Text();
        Assert.Equal(before + "Q", typed);

        Pump(fixture.Session.UndoAsync().AsTask(), "the canonical history undo");
        fixture.Settle();

        var afterUndo = Text();
        var live = fixture.ParagraphEditors.Length > 0 ? fixture.ParagraphEditors[0].Text : "(no editor)";
        Assert.True(afterUndo == before,
            $"the session's own undo left the canonical text unchanged. block {blockId}: before={Quote(before)} " +
            $"afterTyping={Quote(typed)} afterUndo={Quote(afterUndo)}; live editor text={Quote(live)}");
    });

    /// <summary>
    /// Drives a session or view-model task to completion while pumping the UI thread: the test thread *is*
    /// the UI thread here and both the block renderer and the interaction owner resume on it, exactly like
    /// the app host, so awaiting without pumping deadlocks and blocking starves the continuation.
    /// </summary>
    private static void Pump(Task task, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompletedSuccessfully, $"{what} did not finish: {task.Exception?.GetBaseException().Message}");
    }

    /// <summary>
    /// BF-19 keyboard half. Measured natively (instance run20, receipts M15/M32/M33): four Ctrl+Z presses
    /// inside a focused block changed nothing - not the page, not the physical file (it stayed at
    /// richNotes.version 130 with the same four blocks) - while the canonical history itself is proven
    /// working by the case below. The text control consumes Ctrl+Z for its own undo stack, which the
    /// single-undo-owner rule had just emptied, so the key reached neither a character undo nor the board
    /// history: the keyboard simply could not undo while authoring.
    /// </summary>
    [Fact]
    public Task CtrlZInsideAFocusedBlockDrivesTheCanonicalHistory() => OnSurfaceAsync((fixture, window) =>
    {
        var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string Text() => fixture.Session.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Text;

        var editor = fixture.ParagraphEditors[0];
        var before = Text();
        Click(window, CenterInWindow(editor, window));
        window.KeyTextInput("Q");
        fixture.Settle();

        Assert.Equal(before + "Q", Text());
        Assert.True(fixture.ParagraphEditors[0].IsFocused, "the block editor holds focus while typing");

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        fixture.Settle();

        var afterKey = Text();
        var live = fixture.ParagraphEditors[0].Text;
        Assert.True(afterKey == before,
            $"Ctrl+Z in the focused block reached no undo at all. block {blockId}: before={Quote(before)} " +
            $"afterTyping={Quote(before + "Q")} afterCtrlZ={Quote(afterKey)}; CanUndo={fixture.Session.CanUndo} " +
            $"CanRedo={fixture.Session.CanRedo}; live editor={Quote(live)}");
        Assert.Equal(before, live);
        Assert.True(fixture.Session.CanRedo, "an edit undone with Ctrl+Z must be redoable");
    });

    /// <summary>
    /// The deliberate exception to the single-undo-owner rule (see BoardsTextUndoOwner): the sidebar filter
    /// and the insert-menu search hold a query, not board content, so their Ctrl+Z must stay local and must
    /// not consume a board revision. Pinned here because the tunneling handler claims Ctrl+Z for every other
    /// text surface, and a silently widened exception would turn a query undo into a document undo.
    /// </summary>
    [Fact]
    public Task CtrlZInTheSidebarFilterLeavesTheBoardHistoryAlone() => OnSurfaceAsync((fixture, window) =>
    {
        var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string Text() => fixture.Session.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Text;

        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("Q");
        fixture.Settle();
        Assert.Equal("Q", Text());

        var filter = Assert.IsType<TextBox>(
            BoardsCuiControlFinder.FindByAutomationId<Control>(fixture.Surface, "NavSearchBox"));
        Assert.Equal(10, filter.UndoLimit);      // the local stack the rule deliberately keeps
        Assert.True(filter.Focus(), "the sidebar filter is focusable");
        window.KeyTextInput("query");
        fixture.Settle();

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        fixture.Settle();

        Assert.Equal("Q", Text());
        Assert.False(fixture.Session.CanRedo, "the filter's Ctrl+Z must not have taken a board undo step");
    });

    /// <summary>
    /// BF-05 / BF-13, measured natively: in instance run26 (receipts S03-S04, frames 175-176) typing "/" in an
    /// empty block opened the searchable insertion menu and removed the trigger character, but the next
    /// characters the user typed went into the **block** instead of the menu's search field - the frame shows
    /// the block reading "TABLE" with the menu gone. The placeholder promises "Use / to select the block type",
    /// so the keyboard route into the menu is part of the requirement, and typing that escapes into the
    /// document also makes an "open and cancel" gesture mutate the board.
    /// </summary>
    [Fact]
    public Task TypingSlashInABlockHandsTheKeyboardToTheInsertMenuSearch() => OnSurfaceAsync((fixture, window) =>
    {
        var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string Text() => fixture.Session.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Text;

        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();
        Assert.Equal(string.Empty, Text());     // the trigger character must never become content

        var search = window.GetVisualDescendants().OfType<TextBox>()
            .SingleOrDefault(candidate => AutomationProperties.GetName(candidate) == "Search insert menu");
        Assert.NotNull(search);
        Assert.True(search!.IsKeyboardFocusWithin,
            $"the insert menu opened but kept no keyboard focus, so the next characters fall into the " +
            $"document. Focus was on {Describe(Focused(window))}");

        window.KeyTextInput("para");
        fixture.Settle();
        Assert.Equal("para", search.Text);
        Assert.Equal(string.Empty, Text());     // searching is not typing into the board
    });

    /// <summary>
    /// BF-19 granularity, measured natively: Ctrl+Z stepped the physical file back one character per press
    /// (receipts N04/N05, frames 154-155: "SPLITMEZZTOP" -> "SPLITMEZZTO" -> "SPLITMEZZT" -> "SPLITMEZZ",
    /// richNotes.version 140 -> 138 -> 136 -> 134), because every keystroke is its own canonical merge and
    /// therefore its own history entry. One press must undo the edit the user made.
    /// </summary>
    [Fact]
    public Task CtrlZUndoesTheTypedRunRatherThanOneCharacterOfIt() => OnSurfaceAsync((fixture, window) =>
    {
        var adapter = Assert.IsType<ContractSessionAdapter>(fixture.Session);
        adapter.HistoryCoalescingWindow = TimeSpan.FromSeconds(30);   // no timer-sensitive assertion
        var blockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string Text() => fixture.Session.Document.Sections[0].Pages[0].Blocks.Single(b => b.Id == blockId).Text;

        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        foreach (var character in "HELLO") window.KeyTextInput(character.ToString());
        fixture.Settle();
        Assert.Equal("HELLO", Text());

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        fixture.Settle();

        var live = fixture.ParagraphEditors[0].Text;
        Assert.True(Text() == string.Empty,
            $"one Ctrl+Z left {Quote(Text())} of the typed run in place (live editor {Quote(live)}); " +
            "the run must undo as one boundary");
        Assert.Equal(string.Empty, live);
        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.None, null);
        fixture.Settle();
        Assert.Equal("HELLO", Text());
    });

    /// <summary>
    /// BF-13 / BF-05: the slash route all the way to a real table, including the destination step.
    ///
    /// How this case earned its current shape, recorded because the earlier versions were wrong in ways that
    /// looked like product defects:
    ///   * typing "/table" in one go never reaches the trigger at all - the inline rule fires only when the
    ///     block's whole text is exactly "/" - so the menu was never open and the "missing rows" were real
    ///     only in the test;
    ///   * a structure dump of the mounted window showed the destination rows exist and are actionable:
    ///     CuiMenuItem#InsertAbove["Add Block Above"], InsertBelow, InsertReplace, each visible,
    ///     hit-test-visible, focusable and enabled;
    ///   * natively the same route misbehaved (run26, receipts S06/S07, frames 178-179): clicking the measured
    ///     "Replace Current Block" row closed the menu and inserted nothing, and Down then Return left the menu
    ///     unchanged with no visible selection. This case is the discriminator: pass, and the native readings
    ///     are probe errors to re-test with per-keystroke frames; fail at a named step, and that step is real.
    /// </summary>
    [Fact]
    public Task TheSlashRouteReachesARealTableThroughTheDestinationStep() => OnSurfaceAsync((fixture, window) =>
    {
        var anchorBlockId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;
        string AnchorText() => fixture.Session.Document.Sections[0].Pages[0].Blocks
            .Single(block => block.Id == anchorBlockId).Text ?? string.Empty;

        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();
        Assert.Equal(string.Empty, AnchorText());   // the trigger character must never become content

        Assert.True(InsertSearch(window).IsKeyboardFocusWithin,
            "the insert menu must own the keyboard once it is open, or its search can never be typed into");

        window.KeyTextInput("table");
        fixture.Settle();

        var destination = InsertRow(window, "Add Block Above");
        Click(window, CenterInWindow(destination, window));
        fixture.Settle();

        Assert.True(InsertSearch(window).IsEffectivelyVisible,
            "choosing the insert destination closed the menu instead of offering the filtered block types - " +
            "the same dead end measured natively in run26 (frame 178)");

        // The rows are named "Insert <label>" by ToolbarBuilder.AddGroup, not by their bare label - the
        // first version of this case looked for "Table" and concluded, wrongly, that the product offered
        // nothing. The row is there: Button#Insert Table.
        var tableRow = InsertRow(window, "Insert Table");
        Click(window, CenterInWindow(tableRow, window));
        fixture.Settle();

        PumpUntil(() => fixture.Session.Document.Sections[0].Pages[0].Blocks
            .Any(block => string.Equals(block.Kind, "table", StringComparison.Ordinal)),
            "the chosen Table row never produced a table block in the canonical document");

        var blocks = fixture.Session.Document.Sections[0].Pages[0].Blocks;
        var table = blocks.Single(block => string.Equals(block.Kind, "table", StringComparison.Ordinal));
        Assert.True(table.TableRows >= 1 && table.TableCols >= 1,
            $"a table block with no grid is not a table ({table.TableRows}x{table.TableCols})");
        // The chosen destination was "Add Block Above", so the table must come first.
        Assert.NotEqual(anchorBlockId, blocks[0].Id);
    });

    /// <summary>
    /// BF-13 second half: a table is only real if its cells can be edited. The route is the same slash →
    /// destination → Insert Table path as the case above, then the first cell editor is clicked and typed into,
    /// and the value is read back out of the canonical document rather than off the screen.
    /// </summary>
    [Fact]
    public Task ATypedTableCellReachesTheCanonicalTable() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();
        window.KeyTextInput("table");
        fixture.Settle();
        Click(window, CenterInWindow(InsertRow(window, "Add Block Below"), window));
        fixture.Settle();
        Click(window, CenterInWindow(InsertRow(window, "Insert Table"), window));
        fixture.Settle();
        PumpUntil(() => fixture.Session.Document.Sections[0].Pages[0].Blocks
            .Any(block => string.Equals(block.Kind, "table", StringComparison.Ordinal)),
            "the Table row never produced a table block");

        var tableId = fixture.Session.Document.Sections[0].Pages[0].Blocks
            .Single(block => string.Equals(block.Kind, "table", StringComparison.Ordinal)).Id;
        var cell = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(candidate =>
            (candidate.Name ?? string.Empty).StartsWith("cell_" + tableId + "_", StringComparison.Ordinal) ||
            (candidate.Name ?? string.Empty).StartsWith("cell_", StringComparison.Ordinal));
        Assert.NotNull(cell);

        Click(window, CenterInWindow(cell!, window));
        window.KeyTextInput("CE11");
        fixture.Settle();

        PumpUntil(() => string.Equals(
            fixture.Session.Document.Sections[0].Pages[0].Blocks
                .Single(block => block.Id == tableId).TableCells.GetValueOrDefault("0,0"),
            "CE11", StringComparison.Ordinal),
            "typing into the cell never reached the canonical table cells");
        var cells = fixture.Session.Document.Sections[0].Pages[0].Blocks
            .Single(block => block.Id == tableId).TableCells;
        Assert.Equal("CE11", cells["0,0"]);
        // A fresh 2x2 table has every cell present (empty strings), so the meaningful check is that the
        // neighbour is still empty - typing in one cell must not write into the next one.
        Assert.Equal(string.Empty, cells.GetValueOrDefault("0,1"));
    });

    /// <summary>
    /// Outside-click closing for the floating insertion surface. Boards owns this behaviour now: measured on
    /// the running Linux build (ledger rows 86-91), the framework's light-dismiss overlay stops a drawn
    /// popup row from receiving a pointer press at all, so the overlay is switched off for the Boards
    /// popups and the closing rule is implemented against the bounds of the surface that is actually drawn.
    /// This case is what keeps that rule honest: without it, switching the overlay off would silently make
    /// the menu impossible to dismiss.
    /// </summary>
    [Fact]
    public Task TheInsertMenuClosesWhenAPressLandsOutsideItsSurface() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();

        var search = InsertSearch(window);
        Assert.True(search.IsEffectivelyVisible, "the insertion surface must be open before it can be dismissed");

        var pageTitle = window.GetVisualDescendants().OfType<TextBox>().Single(candidate =>
            AutomationProperties.GetName(candidate) == "Page title");
        Click(window, CenterInWindow(pageTitle, window));
        fixture.Settle();

        var stillThere = window.GetVisualDescendants().OfType<TextBox>()
            .SingleOrDefault(candidate => AutomationProperties.GetName(candidate) == "Search insert menu");
        Assert.True(stillThere is not { IsEffectivelyVisible: true },
            "a press outside the insertion surface left it open: Boards-owned outside-click closing is not working");
    });

    /// <summary>
    /// BF-17's keyboard row, measured through the destination stage.
    ///
    /// Why this exists: on the running build, pressing Down with the destination stage open changed the
    /// surrounding scroll area and left no row visibly chosen (run52, receipts AQ01-AQ02, frame 282). The
    /// insert surface builds its navigation list from rows that are focusable, and the CUI loader sets
    /// Focusable from the authored Interactive attribute, so three destination rows that never author it
    /// cannot be reached from the keyboard at all - the route was pointer-only. This case is the durable
    /// guard for that: it fails if the rows stop being focusable, if Down stops landing on one of them, or
    /// if Enter no longer applies the row that looks chosen.
    /// </summary>
    [Fact]
    public Task TheDestinationRowsAreKeyboardReachableAndEnterChoosesTheHighlightedOne() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();

        var rows = new[] { "Add Block Above", "Add Block Below", "Replace Current Block" }
            .Select(name => InsertRow(window, name))
            .OfType<MenuItem>()
            .ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.True(row.Focusable,
            $"destination row \"{AutomationProperties.GetName(row)}\" is not focusable, so the menu's own " +
            "keyboard navigation cannot reach it and the insertion route is pointer-only"));

        window.KeyPress(Key.Down, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowDown, "Down");
        fixture.Settle();
        var landed = rows.SingleOrDefault(row => row.IsKeyboardFocusWithin);
        Assert.True(landed is not null,
            "Down did not move the keyboard onto any destination row: " +
            string.Join(", ", rows.Select(row => $"{AutomationProperties.GetName(row)} focusable={row.Focusable} focused={row.IsKeyboardFocusWithin}")));

        window.KeyPress(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Enter, "Enter");
        fixture.Settle();
        var advanced = window.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => (text.Text ?? string.Empty).EndsWith("THIS BLOCK", StringComparison.Ordinal));
        Assert.True(advanced,
            "Enter on the highlighted destination row did not advance the surface to the block-type stage");
    });

    /// <summary>
    /// BF-12's keyboard lifecycle in the destination stage: the row that answers the keyboard (or the
    /// pointer) must be the row that looks chosen.
    ///
    /// Measured on the running build first (2 November 2026, run58/run59, `home33`/`profile33` and
    /// `home34`/`profile34`, receipts BC02-BC04 and BD01-BD02, frames 307-309 and 311). Pressing Down in
    /// this stage moved the keyboard - the page-side insertion preview changed, visible as the accent bar
    /// beside the plus and along the top of the card - but no row in the menu showed any state: the row
    /// backgrounds sampled byte-identical values before and after the key press at device y 602, 674 and
    /// 746. In the same build the C#-built type rows do show a chosen state, because ToolbarBuilder
    /// reconciles hover and keyboard focus into CuiPropertyPresentation.SetSelected for them. A keyboard
    /// user therefore could not see which destination Enter would apply, and the destination stage is the
    /// one choice that decides where a block lands.
    /// </summary>
    [Fact]
    public Task TheDestinationRowThatAnswersTheKeyboardOrPointerIsTheRowThatLooksChosen() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();

        var rows = new[] { "Add Block Above", "Add Block Below", "Replace Current Block" }
            .Select(name => InsertRow(window, name))
            .OfType<Control>()
            .ToArray();
        Assert.Equal(3, rows.Length);

        static CuiEffectivePropertyStore StoreOf(Control row)
        {
            Assert.True(CuiPropertyPresentation.TryGetExisting(row, out var store) && store is not null,
                $"destination row \"{AutomationProperties.GetName(row)}\" has no CUI property projection, so no " +
                "chosen state can be resolved for it");
            return store!;
        }
        // The chosen cue for a destination row is CUI's own accent BorderColor (written by the framework's
        // interaction state while the row holds focus) plus the border width the destination stage supplies
        // on top of it, because the menu-row theme declares BorderWidth 0 and would otherwise paint
        // nothing. Both halves are required: colour without width is invisible (the defect measured natively
        // on 2 November), and width without colour would invent a Boards-private indicator.
        static bool LooksChosen(Control row)
        {
            var store = StoreOf(row);
            var width = store.Resolve("BorderWidth").Value as Avalonia.Thickness?;
            var colour = store.Resolve("BorderColor").Value as Avalonia.Media.IBrush;
            return width is { } w && (w.Left > 0 || w.Top > 0 || w.Right > 0 || w.Bottom > 0) &&
                colour is not null && !ReferenceEquals(colour, Avalonia.Media.Brushes.Transparent);
        }
        static double RowHeight(Control row) =>
            (StoreOf(row).Resolve("Height").Value as double?) ?? double.NaN;
        static double RowBottomInWindow(Control row, Window window) =>
            row.TranslatePoint(new Point(0, row.Bounds.Height), window) is { } p ? p.Y : double.NaN;
        static string CueState(Control row)
        {
            var store = StoreOf(row);
            return "borderWidth=" + store.Resolve("BorderWidth").Value + " [" + store.Resolve("BorderWidth").Level +
                "/'" + store.Resolve("BorderWidth").Source + "'] borderColor=" + store.Resolve("BorderColor").Value +
                " [" + store.Resolve("BorderColor").Level + "/'" + store.Resolve("BorderColor").Source + "']";
        }
        string Inventory() => string.Join(" | ", rows.Select(row =>
            $"{AutomationProperties.GetName(row)}: chosen={LooksChosen(row)} {CueState(row)} " +
            $"focused={row.IsKeyboardFocusWithin} pointerOver={row.IsPointerOver}"));

        Assert.All(rows, row => Assert.False(LooksChosen(row),
            $"destination row \"{AutomationProperties.GetName(row)}\" claims to be chosen before any pointer or " +
            "keyboard has selected it"));

        var target = rows.Single(row => AutomationProperties.GetName(row) == "Add Block Below");
        Assert.True(target.Focus(), "the destination row must be keyboard focusable");
        fixture.Settle();
        // The chosen cue must not walk over the label it is highlighting: the row keeps its authored
        // 34-unit height, so without compensating the content padding the border crosses the top of the
        // row's own text (measured on the running build, run60 receipt BE04, frame 315).
        var restHeight = RowHeight(rows[0]);
        Assert.True(LooksChosen(target),
            "the keyboard-selected destination row does not look chosen: this is the exact native gap measured on " +
            "2 November (run59 frames 311 and 307-309) - Down moved the insertion preview on the page while every " +
            "row in the menu stayed blank, so a keyboard user cannot see which destination Enter will apply. " +
            "State right now: " + Inventory());
        Assert.False(LooksChosen(rows.Single(row => AutomationProperties.GetName(row) == "Add Block Above")),
            "another destination row took on a chosen look while the keyboard was elsewhere");
        Assert.True(RowHeight(target) >= restHeight + 1.0,
            $"the chosen cue did not grow the row, so the border is drawn across the row's own label: height " +
            $"went {restHeight} -> {RowHeight(target)} while the border is 1.5 units wide (measured natively on " +
            "2 November: run60 frame 315 shows the accent outline crossing the letters, run61 frames 316-319 " +
            "show a padding-based compensation deleting the labels of every row at or below the highlight)");
        Assert.True(RowBottomInWindow(target, window) > restHeight,
            "the grown row must still be positioned where its label is drawn; the highlight landed on empty space");

        // And the pointer route: hovering a row must choose it, and un-hovering must clear it without
        // disturbing a row that the keyboard still holds.
        var other = rows.Single(row => AutomationProperties.GetName(row) == "Add Block Above");
        window.MouseMove(CenterInWindow(other, window));
        fixture.Settle();
        Assert.True(LooksChosen(other),
            "the hovered destination row does not look chosen. Pointer state reached the row: " +
            $"{other.IsPointerOver}; {CueState(other)}; press point {CenterInWindow(other, window)}; " +
            $"window pointer state {window.IsPointerOver}");
        Assert.True(LooksChosen(target), "hovering a different row cleared the row the keyboard still holds");

        window.MouseMove(CenterInWindow(fixture.PageTitleBox, window));
        fixture.Settle();
        Assert.False(LooksChosen(other), "the un-hovered destination row still looks chosen");
        Assert.True(LooksChosen(target), "the keyboard-held row lost its chosen look when an unrelated pointer moved");
    });

    /// <summary>
    /// A keyboard-selected row in the block-type stage must show the same chosen cue as a row in the
    /// destination stage.
    ///
    /// Why: the insert surface is one menu but its two stages are built from different control kinds - the
    /// destination rows load from CUI as CuiMenuItem, the type rows are constructed in code as Buttons - and
    /// measured on the running build they answered the same interaction differently. A hovered type row was
    /// painted with a soft wash, while a type row moved onto by the keyboard kept CUI's accent BorderColor
    /// against a theme-declared zero border width, i.e. no visible cue at all (2 November 2026, run65 receipt
    /// BL04, frame 338: after Down in the filtered type stage the row's drawn state was unchanged from the
    /// previous frame). The destination stage already drew an accent outline (run62, frames 321-323). Two
    /// stages of one menu should not speak different languages to the same person.
    /// </summary>
    [Fact]
    public Task AKeyboardSelectedTypeRowShowsTheSameChosenCueAsADestinationRow() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();
        // Choose a destination first, which replaces the destination stage with the type list.
        var below = InsertRow(window, "Add Block Below");
        Click(window, CenterInWindow(below, window));
        fixture.Settle();

        var paragraph = InsertRow(window, "Insert Paragraph");
        Assert.False(LooksChosenForProbe(paragraph), "the type row looks chosen before anything selects it");

        Assert.True(paragraph.Focus(), "the type row must be keyboard focusable");
        fixture.Settle();
        Assert.True(LooksChosenForProbe(paragraph),
            "the keyboard-selected type row shows no chosen cue, while a keyboard-selected destination row " +
            "does (run62 frames 321-323): the insert menu's two stages disagree about how to show the same " +
            "state, so a keyboard user cannot tell which item Enter will apply");
    });

    private static bool LooksChosenForProbe(Control row)
    {
        if (!CuiPropertyPresentation.TryGetExisting(row, out var store) || store is null) return false;
        var width = store.Resolve("BorderWidth").Value as Avalonia.Thickness?;
        var colour = store.Resolve("BorderColor").Value as Avalonia.Media.IBrush;
        return width is { } w && (w.Left > 0 || w.Top > 0 || w.Right > 0 || w.Bottom > 0) &&
            colour is not null && !ReferenceEquals(colour, Avalonia.Media.Brushes.Transparent);
    }

    /// <summary>
    /// The row the insert menu highlights must be the row its Enter applies.
    ///
    /// Why this exists, and why it is written as an assertion rather than a workaround: on the running build
    /// the newly visible cue and the applied action disagree. run68 (`home42`/`profile42`, window
    /// `0x01a0000f`, receipts BO02-BO04, frames 346-348): open the menu, press Down once, and the accent
    /// outline sits on "Add Block Above" (band y 566-639 with the label at 596-615); pressing Enter then
    /// closed the surface and left the physical file at richNotes.version 1 with the same single empty
    /// paragraph - nothing was inserted above or below. A different run of the same two keys replaced the
    /// block instead (run67, `home41`/`profile41`: the paragraph became a Tabber carrying its own page
    /// "Tab 1", file version 1 -> 3), which is the destination "Replace Current Block". So the same key pair
    /// produced no action in one run and the third choice in another, while the screen showed the first
    /// choice. A person cannot be asked to guess which row a visible highlight stands for.
    ///
    /// This case is the durable, honest form of that finding: it asserts the one property the specification
    /// requires (BF-17: Escape/Close/scrim restore correct focus; the named control does the named thing) and
    /// it will fail somewhere until the two keyboard routes in the surface are reconciled. Measured on
    /// 2 November 2026 as NOT PASSING headlessly - see the ledger row that cites this case.
    /// </summary>
    [Fact]
    public Task PressingDownOnceThenEnterAppliesTheRowTheMenuIsShowingAsChosen() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/");
        fixture.Settle();

        var before = fixture.Session.Document.Sections[0].Pages[0].Blocks.Count;
        var anchorId = fixture.Session.Document.Sections[0].Pages[0].Blocks[0].Id;

        window.KeyPress(Key.Down, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowDown, "Down");
        fixture.Settle();

        var rows = new[] { "Add Block Above", "Add Block Below", "Replace Current Block" }
            .Select(name => InsertRow(window, name))
            .ToArray();
        var shown = rows.Single(row => LooksChosenForProbe(row) && row.IsKeyboardFocusWithin);
        var shownName = AutomationProperties.GetName(shown);
        Assert.Equal("Add Block Above", shownName);

        window.KeyPress(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Enter, "Enter");
        fixture.Settle();

        var after = fixture.Session.Document.Sections[0].Pages[0].Blocks;
        Assert.True(after.Count == before + 1,
            $"Enter on the shown destination row \"{shownName}\" did not add a block: blocks went " +
            $"{before} -> {after.Count}; the surface showed {shownName} chosen");
        Assert.True(after[0].Id != anchorId,
            $"Enter applied the shown row \"{shownName}\" but the new block is not in front of the anchor: " +
            $"first block is still {anchorId}");
        Assert.True(after[1].Id == anchorId,
            $"Add Block Above did not put the anchor second: order is " +
            $"{string.Join(",", after.Select(block => block.Id))}");
    });

    private static TextBox InsertSearch(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(
        candidate => AutomationProperties.GetName(candidate) == "Search insert menu");

    internal static Control? InsertRowForProbe(Window window, string accessibleName) => InsertRow(window, accessibleName);

    private static Control InsertRow(Window window, string accessibleName)
    {
        var row = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(candidate =>
            AutomationProperties.GetName(candidate) == accessibleName);
        Assert.True(row is not null,
            $"no insert row named '{accessibleName}'. Actionable controls in the surface right now: " +
            string.Join(" | ", window.GetVisualDescendants().OfType<Control>()
                .Where(candidate => (AutomationProperties.GetName(candidate) ?? string.Empty).Length > 0)
                .Select(candidate => candidate.GetType().Name + "#" +
                    (AutomationProperties.GetName(candidate) ?? ""))
                .Distinct()));
        Assert.True(row!.IsEffectivelyVisible && row.IsHitTestVisible && row.IsEnabled,
            $"insert row '{accessibleName}' is drawn but not actionable");
        return row;
    }

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(condition(), what);
    }

    private static string Quote(string? text) => text is null ? "(null)" : "\"" + text.Replace("\n", "\\n") + "\"";

    private static Point CenterInWindow(Avalonia.Visual visual, Window window) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static IInputElement? Focused(Window window) => window.FocusManager.GetFocusedElement();

    private static string Describe(object? any) => any switch
    {
        null => "null",
        Control control => $"{control.GetType().Name}#{control.Name ?? ""}[" +
            $"{AutomationProperties.GetName(control) ?? ""}] bounds={control.Bounds}",
        Visual visual => visual.GetType().Name + " bounds=" + visual.Bounds,
        _ => any.GetType().Name,
    };

    private static string Chain(Window window, Point point) =>
        string.Join(" < ", window.GetVisualsAt(point).Take(8).Select(Describe));

    private static async Task OnSurfaceAsync(Action<Fixture, Window> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-click-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        BoardsViewModel? viewModel = null;
        Window? window = null;
        CuiControlLoader? loader = null;
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, Path.Combine(root, "click.9to1board"));
            var fixture = TestUiThread.Run(() =>
            {
                viewModel = new BoardsViewModel(adapter);
                loader = BoardsCuiDocuments.CreateLoader();
                loader.SetBindingContext(viewModel);
                loader.SetActionDispatcher(viewModel);
                var loaded = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
                Assert.DoesNotContain(loaded.Diagnostics, item =>
                    item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var surface = Assert.IsAssignableFrom<Control>(loaded.Root);
                loader.WireBindings(surface);
                viewModel.Attach(surface);
                window = new Window { Content = surface, Width = 1440, Height = 900 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var created = new Fixture(adapter, viewModel, surface) { Root = window };
                created.ComposeLikeTheRunningApp();
                created.Settle();
                Assert.NotEmpty(created.ParagraphEditors);
                return created;
            });
            TestUiThread.Run(() =>
            {
                work(fixture, window!);
                return true;
            });
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                window?.Close();
                viewModel?.Dispose();
                loader?.Dispose();
                Dispatcher.UIThread.RunJobs();
            });
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class Fixture
    {
        internal IRichBoardSession Session { get; }
        internal BoardsViewModel ViewModel { get; }
        internal Control Surface { get; }

        internal Fixture(IRichBoardSession session, BoardsViewModel viewModel, Control surface)
        {
            Session = session;
            ViewModel = viewModel;
            Surface = surface;
        }

        internal string Snapshot() => Session.Document.Sections[0].Pages[0].Title + "|" +
            string.Join(",", Session.Document.Sections[0].Pages[0].Blocks
                .Select(block => block.Kind + ":" + block.Text));

        internal TextBox PageTitleBox =>
            Assert.IsType<TextBox>(BoardsCuiControlFinder.FindByAutomationId<Control>(Surface, "PageTitleBox"));

        internal TextBox BoardTitleBox
        {
            get
            {
                // The board-name editor only exists while a rename is open, which is exactly the state
                // whose undo behaviour was measured, so open it through the real command first.
                if (BoardsCuiControlFinder.FindByAutomationId<Control>(Surface, "BoardTitleBox") is TextBox existing)
                    return existing;
                Pump(ViewModel.DispatchAsync("RenameBoard", null).AsTask(), "the board rename request");
                Settle();
                return Assert.IsType<TextBox>(
                    BoardsCuiControlFinder.FindByAutomationId<Control>(Surface, "BoardTitleBox"));
            }
        }

        internal TextBox[] ParagraphEditors => Surface.GetVisualDescendants().OfType<TextBox>()
            .Where(control => AutomationProperties.GetName(control) == "Paragraph editor" &&
                control.IsEffectivelyVisible)
            .ToArray();

        internal Window? Root { get; set; }
        internal BoardInkInteractionController? Ink { get; set; }
        internal BoardsTextAndKeyboardOwner? TextOwner { get; set; }

        /// <summary>
        /// Builds the page the way the standalone host does (Program.cs:405-432): the CUI surface provides
        /// the named hosts, the native renderer fills BlocksHost, and the ink layer is owned by the
        /// interaction controller. Without this the mounted surface has no block rows to click, and the
        /// test would prove nothing about the running app.
        /// </summary>
        internal void ComposeLikeTheRunningApp()
        {
            // Same resolution the standalone host uses (Program.cs:409-410): the finder returns the
            // internal layout adapter for the named CUI container, not the authored wrapper.
            var blocks = BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Surface, "BlocksHost");
            var overlay = BoardsCuiControlFinder.FindByAutomationId<Panel>(Surface, "InkOverlayLayer");
            Assert.NotNull(blocks);
            Assert.NotNull(overlay);
            // The landing overlay is what the running app shows first, and it stays in the tree until a
            // board is opened. Without this the surface would still be covered by LandingHost and a click
            // would test the landing, not the page (measured: the topmost visual at the block's own point
            // was LandingHost's ScrollContentPresenter).
            ViewModel.SetLanding(false);
            TextOwner = new BoardsTextAndKeyboardOwner(Surface, ViewModel);
            TextOwner.AttachWindow(Root!);
            Ink = new BoardInkInteractionController(overlay!, ViewModel);
            // Pumped rather than blocked: the UI thread here is the test thread, and both calls resume on
            // it (BoardsPresentationInteractions uses ConfigureAwait(true)), exactly like the app host.
            Pump(Ink.RenderAsync(), "the ink layer render");
            Pump(BlockRenderer.RebuildAsync(blocks!, ViewModel), "the block host rebuild");
            // The running host also keeps the page rebuilt after every edit (Program.cs:436 wires
            // RebuildRequested to a posted rebuild). A mount that never wires it would keep stale editors
            // on screen after an undo and make an application-layer fault look like a product fault.
            ViewModel.RebuildRequested += () => Dispatcher.UIThread.Post(
                () => _ = BlockRenderer.RebuildAsync(blocks!, ViewModel), DispatcherPriority.Normal);
        }

        internal void Settle()
        {
            for (var pass = 0; pass < 3; pass++)
            {
                Root?.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
