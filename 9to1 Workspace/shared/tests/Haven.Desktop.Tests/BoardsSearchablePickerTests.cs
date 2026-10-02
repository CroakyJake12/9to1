using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Boards;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class BoardsSearchablePickerTests
{
    [AvaloniaFact]
    public async Task Actual_persisted_board_picker_filters_titles_and_opens_exact_notebook_without_changing_saved_identity()
    {
        await using var fixture = new Fixture();
        var alpha = await fixture.Boards.CreateNotebookAsync("Alpha research", fixture.Token);
        var beta = await fixture.Boards.CreateNotebookAsync("Beta research", fixture.Token);
        var ordinary = NotesDocument.Create("Research outside Boards");
        await fixture.Repository.SaveAsync(ordinary, "Ordinary Write document", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(alpha.Id).ToString(), fixture.Token));
        var picker = Assert.IsType<BoardsNotebookPicker>(await fixture.Page.CreateBoardPickerAsync());
        Assert.Equal(2, picker.MatchButtons.Count);
        Assert.StartsWith("✓ ", Assert.IsType<string>(picker.MatchButtons.Single(button => Equals(button.Tag, alpha.Id)).Content));
        picker.Search.Text = "  BETA  ";
        var selected = Assert.Single(picker.MatchButtons);
        Assert.Equal(beta.Id, selected.Tag);
        selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await picker.PendingSelection.WaitAsync(fixture.Token);
        Assert.Equal(beta.Id, fixture.Page.Document!.Id);
        Assert.Equal(beta.Sections[0].Pages[0].Id, fixture.Page.CurrentPage!.Id);
        var reopened = await new NotesRepository(fixture.Paths, new NotesDocumentValidator(), fixture.Diagnostics)
            .LoadAsync(beta.Id, fixture.Token);
        Assert.NotNull(reopened);
        Assert.Equal(beta.Version, reopened.Version);
        Assert.Equal(beta.Sections[0].Pages[0].Blocks[0].Id, reopened.Sections[0].Pages[0].Blocks[0].Id);
        picker.Search.Text = "does not exist";
        Assert.Empty(picker.MatchButtons);
        picker.Search.Text = string.Empty;
        Assert.Equal(2, picker.MatchButtons.Count);
    }

    [AvaloniaFact]
    public async Task Notebook_deleted_after_list_is_not_adopted_by_picker_and_current_board_stays_open()
    {
        await using var fixture = new Fixture();
        var current = await fixture.Boards.CreateNotebookAsync("Current", fixture.Token);
        var removed = await fixture.Boards.CreateNotebookAsync("Removed", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(current.Id).ToString(), fixture.Token));
        var picker = Assert.IsType<BoardsNotebookPicker>(await fixture.Page.CreateBoardPickerAsync());
        Assert.True(await fixture.Boards.DeleteNotebookAsync(removed.Id, fixture.Token));
        picker.MatchButtons.Single(button => Equals(button.Tag, removed.Id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await picker.PendingSelection.WaitAsync(fixture.Token);
        Assert.Equal(current.Id, fixture.Page.Document!.Id);
        Assert.Null(await fixture.Boards.OpenNotebookAsync(removed.Id, fixture.Token));
        var refreshed = Assert.IsType<BoardsNotebookPicker>(await fixture.Page.CreateBoardPickerAsync());
        Assert.Equal(current.Id, Assert.Single(refreshed.MatchButtons).Tag);
    }

    [AvaloniaFact]
    public async Task Replaced_picker_and_disposed_surface_cannot_navigate_existing_current_board()
    {
        await using var fixture = new Fixture();
        var current = await fixture.Boards.CreateNotebookAsync("Current", fixture.Token);
        var other = await fixture.Boards.CreateNotebookAsync("Other", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(current.Id).ToString(), fixture.Token));
        var retired = Assert.IsType<BoardsNotebookPicker>(await fixture.Page.CreateBoardPickerAsync());
        var active = Assert.IsType<BoardsNotebookPicker>(await fixture.Page.CreateBoardPickerAsync());
        retired.MatchButtons.Single(button => Equals(button.Tag, other.Id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await retired.PendingSelection.WaitAsync(fixture.Token);
        Assert.Equal(current.Id, fixture.Page.Document!.Id);
        fixture.Page.Dispose();
        active.MatchButtons.Single(button => Equals(button.Tag, other.Id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await active.PendingSelection.WaitAsync(fixture.Token);
        Assert.Equal(current.Id, fixture.Page.Document!.Id);
        Assert.Null(await fixture.Page.CreateBoardPickerAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(60));
        private readonly HavenEventBus _bus = new();
        internal Paths Paths { get; } = new();
        internal ProductionDiagnostics Diagnostics { get; }
        internal NotesRepository Repository { get; }
        internal BoardsWorkspaceService Boards { get; }
        internal BoardsPage Page { get; }
        internal CancellationToken Token => _lifetime.Token;
        internal Fixture()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Diagnostics = new ProductionDiagnostics(Paths);
            Repository = new NotesRepository(Paths, new NotesDocumentValidator(), Diagnostics);
            Boards = new BoardsWorkspaceService(Repository);
            Page = new BoardsPage(_bus, Boards);
        }
        public async ValueTask DisposeAsync()
        {
            Page.Dispose(); _bus.Dispose(); _lifetime.Dispose();
            await Diagnostics.DisposeAsync();
            Directory.Delete(Paths.DataDirectory, true);
        }
    }

    internal sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-boards-picker-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
