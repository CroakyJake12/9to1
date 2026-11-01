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

    private static Point CenterInWindow(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

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
        }

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
