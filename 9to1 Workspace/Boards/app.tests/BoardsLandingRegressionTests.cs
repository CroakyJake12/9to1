// BF-21 / BF-22 (owner reports, 28 October):
//   BF-21 "Reloading the page on the generic native boards link opens a random boards document
//         instead of the splash screen."
//   BF-22 "When accessing the splash screen, the board sidebar still displays with pages and the
//         name of the last board you were accessing, which is confusing."
// Root cause measured here: the generic link always resolved BoardsLocalDocuments.LatestOrCreateAsync
// before the splash was shown, so a board - sometimes a freshly created empty one - sat behind the
// splash, and Boards.cui gated the editor and landing panes but NOT the navigation pane, so that
// board's title and page list stayed on screen.
//
// BLOCK MODEL: the splash surface runs on a store-less placeholder document (no file, no store, no
// recent entry). These checks assert nothing is written, nothing is recorded as recent, the board
// chrome is not visible while the splash shows, save/undo refuse honestly, and choosing a board from
// the splash is still the only thing that makes a document current.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsLandingRegressionTests
{
    // Mirrors the existing capture-suite helper: the pump itself runs on the owned UI thread so the
    // task's own continuations can complete there.
    private static T Pump<T>(Task<T> task, string operation) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException("Boards splash check timed out during " + operation + ".");
        return task.GetAwaiter().GetResult();
    });

    private static void Pump(Task task, string operation) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException("Boards splash check timed out during " + operation + ".");
        task.GetAwaiter().GetResult();
    });

    private static void SetTheme(bool dark)
    {
        BoardsTheme.SetMode(dark ? BoardsThemeMode.Dark : BoardsThemeMode.Light);
        if (Avalonia.Application.Current is not null)
            Avalonia.Application.Current.RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
    }

    private static Window MountSplash(BoardsWritablePresentation presentation)
    {
        // Same mount discipline as the capture suite: the surface must be measured and arranged,
        // otherwise no frame is ever rendered and readiness never arrives.
        const int width = 1180, height = 760;
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = presentation.Content,
            Background = BoardsTheme.AppBackgroundBrush,
        };
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        presentation.Content.Measure(new Size(width, height));
        presentation.Content.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static (BoardsWritablePresentation Presentation, Window Window) OpenSplash(
        BoardsLocalDocuments documents, List<Guid> opened)
    {
        var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLandingAsync(documents,
            (id, _) => { opened.Add(id); return Task.CompletedTask; }));
        var presentation = Pump(opening, "splash presentation");
        var window = TestUiThread.Run(() => MountSplash(presentation));
        // Surface readiness (the editor's input/ink owners) is deliberately not waited on here: it
        // requires the editor subtree, which the splash keeps inactive. Settle on layout instead.
        TestUiThread.Run(() =>
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                window.Measure(new Size(1180, 760));
                window.Arrange(new Rect(0, 0, 1180, 760));
                if (window.GetVisualDescendants().OfType<Control>().Any(control => control.Name == "LandingHost"))
                    break;
                Thread.Sleep(2);
            }
        });
        return (presentation, window);
    }

    private static void CloseSplash((BoardsWritablePresentation Presentation, Window Window) fixture)
    {
        TestUiThread.Run(() => { fixture.Window.Close(); });
        Pump(TestUiThread.Run(() => fixture.Presentation.DisposeAsync().AsTask()), "splash disposal");
    }

    // BoardsLocalDocuments keeps its drafts under <dataRoot>/profiles/<profile key>/drafts, so the
    // checks below count recursively rather than assuming a flat folder.
    private static int BoardFileCount(string directory) =>
        Directory.GetFiles(directory, "*.9to1board", SearchOption.AllDirectories).Length;

    // The notice must be rendered on the splash, not merely held in the view model.
    private static bool WindowShowsStorageNotice(Window window) => TestUiThread.Run(() =>
        window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
            .Any(text => (text.Text ?? string.Empty).Contains("Boards are kept in", StringComparison.Ordinal)));

    [Fact]
    public async Task Splash_opens_no_board_writes_nothing_and_refuses_save_and_undo()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-splash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        TestUiThread.Run(() => { CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow; SetTheme(false); return 0; });
        try
        {
            using var documents = new BoardsLocalDocuments("splash-profile", root);
            var before = BoardFileCount(root);
            var opened = new List<Guid>();
            var fixture = OpenSplash(documents, opened);
            try
            {
                var state = TestUiThread.Run(() =>
                {
                    var vm = fixture.Presentation.ViewModel;
                    return (HasOpenBoard: vm.HasOpenBoard, IsLanding: vm.IsLanding,
                        Title: vm.Get("BoardTitle") as string, OpenedCount: opened.Count,
                        Recents: documents.Recent.References.Count);
                });
                Assert.False(state.HasOpenBoard);
                Assert.True(state.IsLanding);
                Assert.Equal(0, state.OpenedCount);
                Assert.Equal(0, state.Recents);
                Assert.Equal(string.Empty, state.Title);
                Assert.Equal(before, BoardFileCount(root));
                // The owner's storage complaint: the surface must name the folder the boards are
                // actually in, not just say "Saved locally".
                var notice = TestUiThread.Run(() => fixture.Presentation.ViewModel.Get("BoardStorageNotice") as string);
                Assert.False(string.IsNullOrWhiteSpace(notice));
                Assert.Contains(documents.DraftDirectory, notice!);
                Assert.True(WindowShowsStorageNotice(fixture.Window),
                    "The splash must render the real board location, not just hold it in the view model.");

                var refused = Pump(TestUiThread.Run(async () =>
                {
                    var vm = fixture.Presentation.ViewModel;
                    await vm.DispatchAsync("SaveBoard", null);
                    var afterSave = vm.Get("StatusText")?.ToString() ?? string.Empty;
                    await vm.DispatchAsync("Undo", null);
                    var afterUndo = vm.Get("StatusText")?.ToString() ?? string.Empty;
                    return (afterSave, afterUndo);
                }), "save/undo refusal");
                Assert.Contains("No board is open", refused.afterSave);
                Assert.Contains("No board is open", refused.afterUndo);
                Assert.Equal(before, BoardFileCount(root));
            }
            finally { CloseSplash(fixture); }
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = priorCuiTheme;
                SetTheme(priorMode == BoardsThemeMode.Dark);
                return 0;
            });
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Board_chrome_is_present_for_an_open_board_and_absent_on_the_splash()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-splash-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        TestUiThread.Run(() => { CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow; SetTheme(false); return 0; });
        try
        {
            using var documents = new BoardsLocalDocuments("splash-chrome-profile", root);
            var boardId = await documents.CreateAsync();
            await using (var adapter = await documents.OpenAsync(boardId))
            {
                // A real board open: the navigation pane, its board title and the page list are on
                // screen. This is the half that proves the splash result below is the gate working
                // rather than the pane simply never existing in this surface.
                var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(
                    documents, boardId, (_, _) => Task.CompletedTask, landing: false));
                var board = Pump(opening, "board presentation");
                var boardWindow = TestUiThread.Run(() =>
                {
                    var window = MountSplash(board);
                    for (var attempt = 0; attempt < 400; attempt++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        window.Measure(new Size(1180, 760));
                        window.Arrange(new Rect(0, 0, 1180, 760));
                        if (window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "NavPane")) break;
                        Thread.Sleep(2);
                    }
                    return window;
                });
                var withBoard = TestUiThread.Run(() =>
                {
                    var found = boardWindow.GetVisualDescendants().OfType<Control>().ToList();
                    var nav = found.SingleOrDefault(control => control.Name == "NavPane");
                    var title = found.SingleOrDefault(control => control.Name == "BoardTitleBox");
                    return (NavFound: nav is not null, NavVisible: nav?.IsEffectivelyVisible ?? false,
                        TitleFound: title is not null, TitleVisible: title?.IsEffectivelyVisible ?? false,
                        BoardTitle: board.ViewModel.Get("BoardTitle") as string);
                });
                Assert.True(withBoard.NavFound, "An open board must show its navigation pane.");
                Assert.True(withBoard.NavVisible);
                Assert.True(withBoard.TitleFound && withBoard.TitleVisible, "An open board must show its title.");
                TestUiThread.Run(() => { boardWindow.Close(); });
                Pump(TestUiThread.Run(() => board.DisposeAsync().AsTask()), "board disposal");
            }

            // The splash must not keep that chrome on screen: no board title box, no page list.
            var opened = new List<Guid>();
            var fixture = OpenSplash(documents, opened);
            try
            {
                var onSplash = TestUiThread.Run(() =>
                {
                    var found = fixture.Window.GetVisualDescendants().OfType<Control>().ToList();
                    var landing = found.SingleOrDefault(control => control.Name == "LandingHost");
                    var titleBox = found.SingleOrDefault(control => control.Name == "BoardTitleBox");
                    var nav = found.SingleOrDefault(control => control.Name == "NavPane");
                    return (LandingVisible: landing?.IsEffectivelyVisible ?? false,
                        TitleBoxOnScreen: titleBox?.IsEffectivelyVisible ?? false,
                        NavOnScreen: nav?.IsEffectivelyVisible ?? false);
                });
                Assert.True(onSplash.LandingVisible, "The authored splash pane must be showing.");
                Assert.False(onSplash.TitleBoxOnScreen,
                    "BF-22: the splash must not keep a board title on screen.");
                Assert.False(onSplash.NavOnScreen,
                    "BF-22: the splash must not keep the board sidebar on screen.");
            }
            finally { CloseSplash(fixture); }
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = priorCuiTheme;
                SetTheme(priorMode == BoardsThemeMode.Dark);
                return 0;
            });
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Reloading_the_generic_link_does_not_reopen_the_last_board_but_choosing_one_still_works()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-splash-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var priorCuiTheme = TestUiThread.Run(() => CuiSurfacePaletteCatalog.ActiveTheme);
        var priorMode = TestUiThread.Run(() => BoardsTheme.Mode);
        TestUiThread.Run(() => { CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow; SetTheme(false); return 0; });
        try
        {
            using var documents = new BoardsLocalDocuments("splash-reload-profile", root);
            var existing = await documents.CreateAsync();
            await using var openedBoard = await documents.OpenAsync(existing);
            Assert.False(string.IsNullOrWhiteSpace(openedBoard.FilePath));
            // The old startup rule: a recent board exists, so the generic link would have reopened
            // it (or created another when none existed) instead of showing the splash.
            Assert.Equal(existing, await documents.LatestOrCreateAsync());
            Assert.True(BoardFileCount(root) > 0);

            // A splash requested while a recent board exists must still open nothing.
            var opened = new List<Guid>();
            var fixture = OpenSplash(documents, opened);
            try
            {
                Assert.False(TestUiThread.Run(() => fixture.Presentation.ViewModel.HasOpenBoard));
                Assert.Empty(opened);
            }
            finally { CloseSplash(fixture); }

            // Choosing New board from the splash is an explicit action and must produce a real
            // board through the owning open path.
            var chosen = new List<Guid>();
            var second = OpenSplash(documents, chosen);
            try
            {
                var created = Pump(TestUiThread.Run(async () =>
                {
                    var vm = second.Presentation.ViewModel;
                    Assert.NotNull(vm.CreateBoardAsync);
                    await vm.CreateBoardAsync!(default);
                    return chosen.Count;
                }), "new board from splash");
                Assert.Equal(1, created);
                Assert.NotEqual(existing, chosen[0]);
                Assert.Equal(2, BoardFileCount(root));
            }
            finally { CloseSplash(second); }
        }
        finally
        {
            TestUiThread.Run(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = priorCuiTheme;
                SetTheme(priorMode == BoardsThemeMode.Dark);
                return 0;
            });
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
