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
    /// BF-13 investigation. Natively (run26, receipts S06/S07, frames 178-179) choosing an insert-menu
    /// destination did not lead to the block-type list: clicking "Replace Current Block" closed the menu with
    /// nothing inserted, and pressing Down then Return left the menu showing the same "ADD WHERE" rows with no
    /// visible selection and nothing inserted. This case asks the same question headlessly against the mounted
    /// composition: after the destination row is chosen, does the menu stay open and offer the filtered type?
    /// </summary>
    // Skipped rather than left red or quietly deleted: this records an *unresolved* native observation and
    // the headless attempt to pin it. Natively (run26, receipts S06/S07, frames 178-179) choosing an insert
    // destination either closed the menu with nothing inserted (click on "Replace Current Block") or did
    // nothing at all (Down then Return, menu unchanged). Headlessly the same route is inconclusive: after the
    // search is filtered to "table" the destination rows are not present in the mounted window tree, so the
    // case cannot reach the step it means to test. The next attempt should drive the destination surface
    // through its own CUI actions rather than by hunting visuals, and must re-check the native click
    // coordinates against the popup bounds before calling it a defect.
    [Fact(Skip = "BF-13 open question: native insert-destination step does not reach the type list; headless fixture cannot yet drive that CUI step")]
    public Task ChoosingAnInsertDestinationKeepsTheMenuOpenAndOffersTheFilteredType() => OnSurfaceAsync((fixture, window) =>
    {
        Click(window, CenterInWindow(fixture.ParagraphEditors[0], window));
        window.KeyTextInput("/table");
        fixture.Settle();

        // The destination step is a CUI-authored surface, so its rows are not Buttons: search every visual
        // that paints the row's label and click the nearest focusable ancestor of that text.
        var shown = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .Where(text => text.Length > 0).Distinct().ToList();
        var label = window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(candidate =>
            (candidate.Text ?? string.Empty).IndexOf("Add Block Above", StringComparison.Ordinal) >= 0);
        Assert.NotNull(label ?? throw new Xunit.Sdk.XunitException(
            "no insert destination rows after filtering by \"table\". Texts visible in the mounted surface: " +
            string.Join(" | ", shown)));
        Avalonia.Visual target = label!;
        while (target is not InputElement { Focusable: true } && target.GetVisualParent() is { } parent)
            target = parent;
        Assert.True(target is InputElement { Focusable: true },
            $"no focusable ancestor was found for the destination row (stopped at {Describe(target)})");
        var point = CenterInWindow(target, window);
        Click(window, point);

        var open = window.GetVisualDescendants().OfType<TextBox>()
            .SingleOrDefault(candidate => AutomationProperties.GetName(candidate) == "Search insert menu");
        Assert.True(open is not null && open.IsVisible,
            $"choosing the insert destination closed the menu (clicked {point}); nothing was inserted and no " +
            $"block type was offered. Visuals under {point} (topmost first): {Chain(window, point)}");

        var offered = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => (text.Text ?? string.Empty).IndexOf("Table", StringComparison.Ordinal) >= 0)
            .ToList();
        Assert.NotEmpty(offered);   // the filtered type list must be reachable from the destination step
    });

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
