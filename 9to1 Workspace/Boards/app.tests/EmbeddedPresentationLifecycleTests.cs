using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Avalonia.Media;
using Haven.Application;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class EmbeddedPresentationLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embedded_board_borrows_live_Home_theme_without_changing_application_resources(bool writable)
    {
        TestUiThread.Run(() => { });
        var priorMode = BoardsTheme.Mode;
        var source = new ThemeSource();
        using var context = new CuiThemePreferenceContext(source);
        var owner = new ThemeHost(context);
        var sentinel = new SolidColorBrush(Colors.Magenta);
        var previous = TestUiThread.Run(() =>
        {
            var prior = Application.Current!.Resources["CuiPanelBrush"];
            Application.Current.Resources["CuiPanelBrush"] = sentinel;
            return prior;
        });
        IAsyncDisposable? presentation = null;
        try
        {
            Control content;
            if (writable)
            {
                var board = PumpUi(BoardsWritablePresentation.OpenWritableDocumentAsync(MakeBoardBytes(),
                    "files:theme", "revision-1", (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")),
                    embeddedArtifactHost: owner));
                presentation = board; content = board.Content;
            }
            else
            {
                var board = PumpUi(BoardsReadOnlyPresentation.OpenReadOnlyDocumentAsync(MakeBoardBytes(), embeddedArtifactHost: owner));
                presentation = board; content = board.Content;
            }
            TestUiThread.Run(() =>
            {
                Assert.Equal(BoardsThemeMode.Dark, BoardsTheme.Mode);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Dark, CuiTheme.Professional,
                    "#F97316", "app.boards").Accent, BoardsTheme.SharedPalette.Accent);
                Assert.Equal(BoardsTheme.SharedPalette.Panel, Assert.IsAssignableFrom<ISolidColorBrush>(content.Resources["CuiPanelBrush"]).Color);
                Assert.Same(sentinel, Application.Current!.Resources["CuiPanelBrush"]);
                source.Publish(CuiAppearance.Bright, "#318CE7");
                Assert.Equal(BoardsThemeMode.Light, BoardsTheme.Mode);
                Assert.Equal(CuiSurfacePaletteCatalog.For("Boards", CuiAppearance.Bright, CuiTheme.Professional,
                    "#318CE7", "app.boards").Accent, BoardsTheme.SharedPalette.Accent);
                Assert.Equal(BoardsTheme.SharedPalette.Panel, Assert.IsAssignableFrom<ISolidColorBrush>(content.Resources["CuiPanelBrush"]).Color);
                Assert.Same(sentinel, Application.Current.Resources["CuiPanelBrush"]);
            });
            await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
            presentation = null;
            TestUiThread.Run(() => source.Publish(CuiAppearance.Dark, "#F97316"));
            Assert.Equal(BoardsThemeMode.Light, BoardsTheme.Mode); // Disposed Boards no longer observes Home.
        }
        finally
        {
            if (presentation is not null) await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
            TestUiThread.Run(() => { Application.Current!.Resources["CuiPanelBrush"] = previous; BoardsTheme.SetMode(priorMode); });
        }
    }

    private sealed class ThemeSource : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 0, "boards-theme-test", false,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark, new(CuiAccentOverrideMode.AllApps, "#F97316"));
        public event EventHandler? Changed;
        public void Publish(CuiAppearance appearance, string accent)
        {
            Current = Current with { Revision = Current.Revision + 1, Appearance = appearance,
                Accent = new(CuiAccentOverrideMode.AllApps, accent) };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    private sealed class ThemeHost(CuiThemePreferenceContext context) : IBoardsEmbeddedArtifactHost
    {
        public CuiThemePreferenceContext? ThemeContext => context;
        public void BindOwningControl(Control control) { }
        public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference reference, BoardEmbeddedArtifactMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BoardEmbeddedArtifactReference?> SelectAsync(string app, CancellationToken ct = default) => Task.FromResult<BoardEmbeddedArtifactReference?>(null);
        public Task<BoardEmbeddedArtifactReference?> CreateAsync(string app, CancellationToken ct = default) => Task.FromResult<BoardEmbeddedArtifactReference?>(null);
    }

    [Fact]
    public async Task Readonly_and_writable_presentations_render_real_blocks_after_host_mount()
    {
        var bytes = MakeBoardBytes();
        TestUiThread.Run(() => { }); // Bind Avalonia's dispatcher to the shared test UI thread first.

        var readOnlyOpen = BoardsReadOnlyPresentation.OpenReadOnlyDocumentAsync(bytes);
        var readOnly = PumpUi(readOnlyOpen);
        var readOnlyWindow = TestUiThread.Run(() => MountAndAssert(readOnly.Content, "Embedded lifecycle content"));
        TestUiThread.Run(readOnlyWindow.Close);
        await DrainAndDisposeAsync(readOnly.DisposeAsync().AsTask());

        var writableOpen = BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "files:document-1", "revision-1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")));
        var writable = PumpUi(writableOpen);
        var writableWindow = TestUiThread.Run(() => MountAndAssert(writable.Content, "Embedded lifecycle content"));
        TestUiThread.Run(writableWindow.Close);
        await DrainAndDisposeAsync(writable.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Delayed_embedded_open_is_discarded_after_navigation()
    {
        var (bytes, sectionId, embeddedPageId, nativePageId, reference) = MakeBoardWithEmbeddedWrite();
        TestUiThread.Run(() => { });
        var owner = TestUiThread.Run(() => new DelayedEmbeddedHost(reference));
        var open = TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "files:document-2", "revision-1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")),
            embeddedArtifactHost: owner));
        var presentation = PumpUi(open);
        var window = TestUiThread.Run(() => MountAndAssert(presentation.Content, "Embedded lifecycle content"));
        try
        {
            PumpUi(TestUiThread.Run(() => presentation.WaitUntilReadyAsync()));
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, embeddedPageId));
            PumpUntilUi(owner.OpenStarted.Task);

            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, nativePageId));
            owner.CompleteOpen();
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));

            Assert.Equal(1, owner.Session.CloseCount);
            Assert.Equal(1, owner.Session.DisposeCount);
            var publishedState = TestUiThread.Run(() =>
            {
                var embeddedSurface = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(presentation.Content, "EmbeddedArtifactHost");
                return (presentation.ViewModel.SelectedPageId, owner.BoundOwner,
                    ContainsOwnerContent: embeddedSurface?.AuthoredChildren.Contains(owner.Session.Content) ?? false);
            });
            Assert.Equal(nativePageId, publishedState.SelectedPageId);
            Assert.Same(presentation.Content, publishedState.BoundOwner);
            Assert.False(publishedState.ContainsOwnerContent);
        }
        finally
        {
            TestUiThread.Run(window.Close);
            await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task Embedded_close_preparation_aborts_cleanly_or_commits_only_after_outer_acceptance()
    {
        var (bytes, sectionId, embeddedPageId, _, reference) = MakeBoardWithEmbeddedWrite();
        TestUiThread.Run(() => { });
        var owner = TestUiThread.Run(() => new DelayedEmbeddedHost(reference));
        var presentation = PumpUi(TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "files:document-close-gate", "revision-1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")),
            embeddedArtifactHost: owner)));
        var window = TestUiThread.Run(() => MountAndAssert(presentation.Content, "Embedded lifecycle content"));
        try
        {
            PumpUi(TestUiThread.Run(() => presentation.WaitUntilReadyAsync()));
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, embeddedPageId));
            PumpUntilUi(owner.OpenStarted.Task);
            owner.CompleteOpen();
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            Assert.True(owner.Session.RevalidateCount > 0);

            var aborted = PumpUi(TestUiThread.Run(() => presentation.PrepareToCloseAsync()));
            Assert.NotNull(aborted);
            Assert.True(aborted!.IsCurrent);
            Assert.Equal(0, owner.Session.CloseCount);
            PumpUi(TestUiThread.Run(() => aborted.AbortAsync()));
            Assert.Equal(1, owner.Session.AbortCloseCount);
            Assert.Equal(0, owner.Session.CloseCount);

            var accepted = PumpUi(TestUiThread.Run(() => presentation.PrepareToCloseAsync()));
            Assert.NotNull(accepted);
            PumpUi(TestUiThread.Run(() => accepted!.CommitAsync()));
            Assert.Equal(1, owner.Session.BeginCloseCount);
            Assert.Equal(1, owner.Session.CloseCount);
            Assert.Equal(1, owner.Session.DisposeCount);
            var remainsMounted = TestUiThread.Run(() =>
                BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(presentation.Content, "EmbeddedArtifactHost")?.AuthoredChildren
                    .Contains(owner.Session.Content) ?? false);
            Assert.False(remainsMounted);
        }
        finally
        {
            TestUiThread.Run(window.Close);
            await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task Mounted_embedded_page_closes_when_navigation_selects_a_native_page()
    {
        var (bytes, sectionId, embeddedPageId, nativePageId, reference) = MakeBoardWithEmbeddedWrite();
        TestUiThread.Run(() => { });
        var owner = TestUiThread.Run(() => new DelayedEmbeddedHost(reference));
        var presentation = PumpUi(TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "files:document-navigation-gate", "revision-1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")),
            embeddedArtifactHost: owner)));
        var window = TestUiThread.Run(() => MountAndAssert(presentation.Content, "Embedded lifecycle content"));
        try
        {
            PumpUi(TestUiThread.Run(() => presentation.WaitUntilReadyAsync()));
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, embeddedPageId));
            PumpUntilUi(owner.OpenStarted.Task);
            owner.CompleteOpen();
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            var mountedState = TestUiThread.Run(() =>
            {
                var authoredHost = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(presentation.Content, "EmbeddedArtifactHost");
                return (Host: authoredHost,
                    HasContent: authoredHost?.AuthoredChildren.Contains(owner.Session.Content) ?? false,
                    AuthoredChildCount: authoredHost?.AuthoredChildren.Count ?? -1,
                    Status: presentation.ViewModel.Get("EmbeddedPageMessage")?.ToString(),
                    RuntimeTree: BoardsCuiControlFinder.DescribeRuntimeTree(presentation.Content));
            });
            Assert.NotNull(mountedState.Host);
            Assert.True(mountedState.HasContent,
                $"The owner-opened control was not retained as an authored child (opens={owner.OpenCount}, " +
                $"revalidations={owner.Session.RevalidateCount}, childCount={mountedState.AuthoredChildCount}, " +
                $"status='{mountedState.Status}'). Runtime tree:\n{mountedState.RuntimeTree}");

            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, nativePageId));
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            Assert.Equal(nativePageId, presentation.ViewModel.SelectedPageId);
            Assert.Equal(1, owner.Session.BeginCloseCount);
            Assert.Equal(1, owner.Session.CloseCount);
            Assert.Equal(1, owner.Session.DisposeCount);
            var stillMounted = TestUiThread.Run(() =>
                BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(presentation.Content, "EmbeddedArtifactHost")?
                    .AuthoredChildren.Contains(owner.Session.Content) ?? false);
            Assert.False(stillMounted);
        }
        finally
        {
            TestUiThread.Run(window.Close);
            await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task Embedded_artifact_that_fails_fresh_revalidation_is_never_published()
    {
        var (bytes, sectionId, embeddedPageId, _, reference) = MakeBoardWithEmbeddedWrite();
        TestUiThread.Run(() => { });
        var owner = TestUiThread.Run(() => new DelayedEmbeddedHost(reference));
        owner.Session.RevalidationResult = false;
        var presentation = PumpUi(TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "files:document-revalidation-gate", "revision-1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")),
            embeddedArtifactHost: owner)));
        var window = TestUiThread.Run(() => MountAndAssert(presentation.Content, "Embedded lifecycle content"));
        try
        {
            PumpUi(TestUiThread.Run(() => presentation.WaitUntilReadyAsync()));
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, embeddedPageId));
            PumpUntilUi(owner.OpenStarted.Task);
            owner.CompleteOpen();
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            var embeddedHost = TestUiThread.Run(() =>
                BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(presentation.Content, "EmbeddedArtifactHost"));
            Assert.NotNull(embeddedHost);
            Assert.DoesNotContain(owner.Session.Content, embeddedHost!.AuthoredChildren);
            Assert.Equal(1, owner.Session.RevalidateCount);
            Assert.Equal(1, owner.Session.BeginCloseCount);
            Assert.Equal(1, owner.Session.CloseCount);
            Assert.True(presentation.ViewModel.Get("EmbeddedPageUnavailable") is true);
        }
        finally
        {
            TestUiThread.Run(window.Close);
            await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task Failed_publication_closes_unadopted_owner_and_keeps_both_publication_and_close_errors()
    {
        var (bytes, sectionId, embeddedPageId, _, reference) = MakeBoardWithEmbeddedWrite();
        TestUiThread.Run(() => { });
        var owner = TestUiThread.Run(() => new DelayedEmbeddedHost(reference));
        owner.Session.CloseFailure = new IOException("close sentinel");
        var presentation = PumpUi(TestUiThread.Run(() => BoardsWritablePresentation.OpenWritableDocumentAsync(
            bytes, "files:document-publication-failure", "revision-1",
            (_, _, _, _) => ValueTask.FromResult(new BoardsExternalSaveReceipt(true, "revision-2")),
            embeddedArtifactHost: owner)));
        var window = TestUiThread.Run(() => MountAndAssert(presentation.Content, "Embedded lifecycle content"));
        var unrelatedParent = TestUiThread.Run(() =>
        {
            var parent = new StackPanel();
            parent.Children.Add(owner.Session.Content);
            return parent;
        });
        try
        {
            PumpUi(TestUiThread.Run(() => presentation.WaitUntilReadyAsync()));
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));
            TestUiThread.Run(() => presentation.ViewModel.SelectPage(sectionId, embeddedPageId));
            PumpUntilUi(owner.OpenStarted.Task);
            owner.CompleteOpen();
            PumpUi(TestUiThread.Run(() => presentation.DrainRenderingAsync()));

            Assert.Equal(1, owner.Session.CloseCount);
            Assert.Equal(1, owner.Session.DisposeCount);
            var published = TestUiThread.Run(() => BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(
                presentation.Content, "EmbeddedArtifactHost")!.AuthoredChildren.Contains(owner.Session.Content));
            Assert.False(published);
            var status = presentation.ViewModel.Get("EmbeddedPageMessage")?.ToString() ?? string.Empty;
            Assert.Contains("could not be opened", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("owner page also failed to close", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("close sentinel", status, StringComparison.Ordinal);
            Assert.True(TestUiThread.Run(() => unrelatedParent.Children.Contains(owner.Session.Content)));
        }
        finally
        {
            TestUiThread.Run(window.Close);
            await DrainAndDisposeAsync(presentation.DisposeAsync().AsTask());
            GC.KeepAlive(unrelatedParent);
        }
    }

    private static Window MountAndAssert(Control content, string expectedText)
    {
        var window = new Window { Width = 1200, Height = 800, Content = content };
        window.Show();
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));
        content.Measure(new Size(1200, 800));
        content.Arrange(new Rect(0, 0, 1200, 800));
        for (var attempt = 0; attempt < 4; attempt++) Dispatcher.UIThread.RunJobs();

        var blocks = BoardsCuiControlFinder.FindByAutomationId<StackPanel>(content, "BlocksHost");
        Assert.True(blocks is not null,
            "Mounted embedded CUI did not expose BlocksHost. Runtime tree:\n" +
            BoardsCuiControlFinder.DescribeRuntimeTree(content));
        Assert.NotEmpty(blocks!.Children);
        Assert.Contains(content.GetVisualDescendants().OfType<TextBox>(), box => box.Text == expectedText);
        return window;
    }

    private static byte[] MakeBoardBytes()
    {
        var now = DateTimeOffset.UtcNow;
        var rich = HavenRichNotes.Create("Embedded lifecycle");
        rich.Sections[0].Pages[0].Blocks[0].PlainText = "Embedded lifecycle content";
        var document = new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, Guid.NewGuid(), now, now, null, rich);
        return JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static (byte[] Bytes, string SectionId, string EmbeddedPageId, string NativePageId,
        BoardEmbeddedArtifactReference Reference) MakeBoardWithEmbeddedWrite()
    {
        var now = DateTimeOffset.UtcNow;
        var rich = HavenRichNotes.Create("Embedded lifecycle");
        rich.Sections[0].Pages[0].Blocks[0].PlainText = "Embedded lifecycle content";
        var reference = new BoardEmbeddedArtifactReference("write", Guid.NewGuid(),
            new BoardEmbeddedArtifactRevision(1, null));
        var embedded = HavenRichNotesOps.AddEmbeddedArtifactPage(rich, rich.Sections[0].Id,
            HavenRichPageTypes.Write, reference, "Linked Write");
        var native = HavenRichNotesOps.AddPage(rich, rich.Sections[0].Id, "Native page");
        var document = new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, Guid.NewGuid(), now, now, null, rich);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return (bytes, rich.Sections[0].Id, embedded.Id, native.Id, reference);
    }

    private static void PumpUntilUi(Task task) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted, "Embedded owner did not begin opening after navigation.");
        task.GetAwaiter().GetResult();
    });

    private sealed class DelayedEmbeddedHost(BoardEmbeddedArtifactReference reference) : IBoardsEmbeddedArtifactHost
    {
        private readonly TaskCompletionSource<IBoardsEmbeddedArtifactSession> _open =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource OpenStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DelayedEmbeddedSession Session { get; } = new(reference);
        public Control? BoundOwner { get; private set; }
        public int OpenCount { get; private set; }

        public void BindOwningControl(Control control) => BoundOwner = control;
        public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference requested,
            BoardEmbeddedArtifactMode mode, CancellationToken ct = default)
        {
            Assert.Equal(reference, requested);
            OpenCount++;
            OpenStarted.TrySetResult();
            return _open.Task;
        }
        public Task<BoardEmbeddedArtifactReference?> SelectAsync(string applicationId, CancellationToken ct = default) =>
            Task.FromResult<BoardEmbeddedArtifactReference?>(null);
        public Task<BoardEmbeddedArtifactReference?> CreateAsync(string applicationId, CancellationToken ct = default) =>
            Task.FromResult<BoardEmbeddedArtifactReference?>(null);
        public void CompleteOpen() => _open.TrySetResult(Session);
    }

    private sealed class DelayedEmbeddedSession(BoardEmbeddedArtifactReference reference) : IBoardsEmbeddedArtifactSession
    {
        public Control Content { get; } = new Border();
        public BoardEmbeddedArtifactReference CurrentReference { get; } = reference;
        public bool IsPublicationCurrent => true;
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int RevalidateCount { get; private set; }
        public int BeginCloseCount { get; private set; }
        public int AbortCloseCount { get; private set; }
        public bool RevalidationResult { get; set; } = true;
        public Exception? CloseFailure { get; set; }
        public Task<bool> RevalidateAsync(CancellationToken ct = default)
        {
            RevalidateCount++;
            return Task.FromResult(RevalidationResult);
        }
        public Task<IBoardsEmbeddedArtifactClosePreparation?> PrepareToCloseAsync(CancellationToken ct = default) =>
            Task.FromResult<IBoardsEmbeddedArtifactClosePreparation?>(new ClosePreparation(this));
        public Task CloseAndDrainAsync()
        {
            CloseCount++;
            var failure = CloseFailure;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }

        private sealed class ClosePreparation(DelayedEmbeddedSession owner) : IBoardsEmbeddedArtifactClosePreparation
        {
            private int _finished;
            public bool IsCurrent => Volatile.Read(ref _finished) == 0;
            public Task BeginCloseAsync(CancellationToken ct = default)
            {
                if (Interlocked.Exchange(ref _finished, 1) != 0)
                    throw new InvalidOperationException("Preparation already finished.");
                owner.BeginCloseCount++;
                return Task.CompletedTask;
            }
            public Task AbortAsync()
            {
                if (Interlocked.Exchange(ref _finished, 1) == 0) owner.AbortCloseCount++;
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() => new(AbortAsync());
        }
    }

    private static T PumpUi<T>(Task<T> task) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted, "Boards presentation operation timed out while pumping the UI dispatcher.");
        return task.GetAwaiter().GetResult();
    });

    private static void PumpUi(Task task) => TestUiThread.Run(() =>
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted, "Boards presentation operation timed out while pumping the UI dispatcher.");
        task.GetAwaiter().GetResult();
    });

    private static async Task DrainAndDisposeAsync(Task task)
    {
        var cleanup = Task.Run(async () => await task.ConfigureAwait(false));
        TestUiThread.Run(() =>
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!cleanup.IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(cleanup.IsCompleted, "Boards presentation cleanup timed out while pumping the UI dispatcher.");
        });
        await cleanup.ConfigureAwait(false);
    }
}
