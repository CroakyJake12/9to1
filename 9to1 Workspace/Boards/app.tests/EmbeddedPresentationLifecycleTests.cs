using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
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

    private static Window MountAndAssert(Control content, string expectedText)
    {
        var window = new Window { Width = 1200, Height = 800, Content = content };
        window.Show();
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));
        content.Measure(new Size(1200, 800));
        content.Arrange(new Rect(0, 0, 1200, 800));
        for (var attempt = 0; attempt < 4; attempt++) Dispatcher.UIThread.RunJobs();

        var blocks = content.GetVisualDescendants().OfType<StackPanel>().SingleOrDefault(panel =>
            Avalonia.Automation.AutomationProperties.GetAutomationId(panel) == "BlocksHost");
        Assert.NotNull(blocks);
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
