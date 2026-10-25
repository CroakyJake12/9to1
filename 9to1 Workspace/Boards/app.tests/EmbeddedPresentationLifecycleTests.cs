using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class EmbeddedPresentationLifecycleTests
{
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
        var owner = new DelayedEmbeddedHost(reference);
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
                var embeddedSurface = BoardsCuiControlFinder.FindByAutomationId<Panel>(presentation.Content, "EmbeddedArtifactHost");
                return (presentation.ViewModel.SelectedPageId, owner.BoundOwner,
                    ContainsOwnerContent: embeddedSurface?.Children.Contains(owner.Session.Content) ?? false);
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

        public void BindOwningControl(Control control) => BoundOwner = control;
        public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference requested,
            BoardEmbeddedArtifactMode mode, CancellationToken ct = default)
        {
            Assert.Equal(reference, requested);
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
        public Task<bool> TryPrepareToCloseAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task CloseAndDrainAsync() { CloseCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private static T PumpUi<T>(Task<T> task) => TestUiThread.Run(() =>
    {
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        return task.GetAwaiter().GetResult();
    });

    private static async Task DrainAndDisposeAsync(Task task)
    {
        var cleanup = Task.Run(async () => await task.ConfigureAwait(false));
        TestUiThread.Run(() =>
        {
            while (!cleanup.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
        });
        await cleanup.ConfigureAwait(false);
    }
}
