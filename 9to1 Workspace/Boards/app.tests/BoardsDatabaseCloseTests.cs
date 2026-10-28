using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsDatabaseCloseTests
{
    [Fact]
    public async Task Abandoned_close_resumes_database_surface_attached_during_preparation()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/deferred-database-close");
        var source = new UnavailableSource();
        var model = new BoardsViewModel(session);
        var block = new RichBoardBlock { Id = "database", Kind = "database",
            Database = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) };
        session.Document.Sections[0].Pages[0].Blocks.Clear();
        session.Document.Sections[0].Pages[0].Blocks.Add(block);
        var owner = new BoardsDatabaseBlocks(model, source);
        var preparing = TestUiThread.Run(() => owner.PrepareCloseAsync());
        await PumpAsync(preparing);
        var preparation = Assert.IsAssignableFrom<IBoardsPresentationClosePreparation>(await preparing);
        var surface = TestUiThread.Run(() =>
        {
            var mount = owner.Mount(block);
            var window = new Window { Content = mount, Width = 640, Height = 400 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Loading database…", Assert.IsType<TextBlock>(((Border)mount).Child).Text);
            return (Window: window, Mount: (Border)mount);
        });
        try
        {
            Assert.Equal(0, source.Opens);
            var abort = preparation.AbortAsync(); await PumpAsync(abort); await abort;
            var idle = owner.WhenIdleAsync(); await PumpAsync(idle); await idle;
            Assert.Equal(1, source.Opens);
            TestUiThread.Run(() =>
            {
                var message = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(surface.Mount.Child).Children[0]);
                Assert.Contains("Database could not be opened", message.Text);
            });
            Assert.False(preparation.IsCurrent);
        }
        finally
        {
            TestUiThread.Run(() => surface.Window.Close());
            var dispose = owner.DisposeAsync().AsTask(); await PumpAsync(dispose); await dispose;
            model.Dispose();
        }
    }

    private static async Task PumpAsync(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            TestUiThread.Run(() => Dispatcher.UIThread.RunJobs());
            await Task.Delay(5);
        }
        await task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private sealed class UnavailableSource : IBoardsDatabaseSource
    {
        public int Opens;
        public Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default) => throw new NotSupportedException();
        public Task<DataRecordSession> OpenDatabaseAsync(Guid id, CancellationToken token = default)
        { Opens++; return Task.FromException<DataRecordSession>(new IOException("Offline source")); }
    }
}
