using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Boards;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class BoardsNativePageMovementTests
{
    [AvaloniaFact]
    public async Task Native_page_menu_reorders_then_moves_selected_page_and_physical_reopen_retains_payload_and_identity()
    {
        await using var fixture = new Fixture();
        var board = await fixture.Boards.CreateNotebookAsync("Organize", fixture.Token);
        var source = board.Sections[0];
        var second = fixture.Boards.AddPage(board, source.Id, "Second");
        var third = fixture.Boards.AddPage(board, source.Id, "Third");
        var target = fixture.Boards.AddSection(board, "Destination");
        var block = fixture.Boards.AddBlock(board, second.Id, NotesBlockKind.Heading, "Retained payload");
        await fixture.Boards.SaveAsync(board, "Seed actual movement hierarchy", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(board.Id, source.Id, second.Id).ToString(), fixture.Token));
        var original = fixture.Page.Document!;
        var reorder = fixture.Page.CreatePageMovementMenu(original, second.Id);
        var down = reorder.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move page down"));
        Assert.True(down.IsEnabled);
        down.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(await fixture.Page.PendingPageMovement.WaitAsync(fixture.Token));
        Assert.Equal(new[] { source.Pages[0].Id, third.Id, second.Id },
            original.Sections.Single(section => section.Id == source.Id).Pages.OrderBy(page => page.Order).Select(page => page.Id));
        var move = fixture.Page.CreatePageMovementMenu(original, second.Id);
        var destination = move.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, target.Id));
        Assert.True(destination.IsEnabled);
        destination.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(await fixture.Page.PendingPageMovement.WaitAsync(fixture.Token));
        Assert.Equal(second.Id, fixture.Page.CurrentPage!.Id);
        Assert.Equal(target.Id, fixture.Page.CurrentSection!.Id);
        var reopened = await new NotesRepository(fixture.Paths, new NotesDocumentValidator(), fixture.Diagnostics).LoadAsync(board.Id, fixture.Token);
        Assert.NotNull(reopened);
        var savedPage = reopened.Sections.Single(section => section.Id == target.Id).Pages.Single(page => page.Id == second.Id);
        Assert.Equal("Retained payload", savedPage.Blocks.Single(item => item.Id == block.Id).PlainText);
        Assert.DoesNotContain(reopened.Sections.Single(section => section.Id == source.Id).Pages, page => page.Id == second.Id);
        Assert.Equal(original.Version, reopened.Version);
    }

    [AvaloniaFact]
    public async Task Final_page_and_view_mode_moves_are_unavailable_and_retired_original_cannot_mutate_saved_board()
    {
        await using var fixture = new Fixture();
        var board = await fixture.Boards.CreateNotebookAsync("Guarded", fixture.Token);
        var source = board.Sections[0]; var pageId = source.Pages[0].Id;
        var target = fixture.Boards.AddSection(board, "Other section");
        await fixture.Boards.SaveAsync(board, "Seed guarded hierarchy", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(board.Id, source.Id, pageId).ToString(), fixture.Token));
        var original = fixture.Page.Document!;
        var menu = fixture.Page.CreatePageMovementMenu(original, pageId);
        Assert.False(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, target.Id)).IsEnabled);
        var before = await File.ReadAllBytesAsync(Path.Combine(fixture.Paths.DataDirectory, "Notes", "Documents", board.Id.ToString("D"), "current.haven-notes.json"), fixture.Token);
        Assert.False(await fixture.Page.MovePageFromHierarchyAsync(original, pageId, target.Id, 1));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(fixture.Paths.DataDirectory, "Notes", "Documents", board.Id.ToString("D"), "current.haven-notes.json"), fixture.Token));
        var extra = fixture.Boards.AddPage(original, source.Id, "Extra");
        fixture.Boards.SetEditMode(original, pageId, BoardsPageEditMode.View);
        await fixture.Boards.SaveAsync(original, "View mode", fixture.Token);
        var viewBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Paths.DataDirectory, "Notes", "Documents", board.Id.ToString("D"), "current.haven-notes.json"), fixture.Token);
        Assert.All(fixture.Page.CreatePageMovementMenu(original, pageId).Items.OfType<MenuItem>(), item => Assert.False(item.IsEnabled));
        Assert.False(await fixture.Page.MovePageFromHierarchyAsync(original, pageId, target.Id, 1));
        Assert.Equal(viewBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Paths.DataDirectory, "Notes", "Documents", board.Id.ToString("D"), "current.haven-notes.json"), fixture.Token));
        var other = await fixture.Boards.CreateNotebookAsync("Replacement", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(other.Id).ToString(), fixture.Token));
        Assert.False(await fixture.Page.MovePageFromHierarchyAsync(original, extra.Id, target.Id, 1));
        Assert.Equal(viewBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Paths.DataDirectory, "Notes", "Documents", board.Id.ToString("D"), "current.haven-notes.json"), fixture.Token));
        Assert.Equal(other.Id, fixture.Page.Document!.Id);
    }

    [AvaloniaFact]
    public async Task Menu_created_before_same_board_reorder_does_not_apply_stale_target_position()
    {
        await using var fixture = new Fixture();
        var board = await fixture.Boards.CreateNotebookAsync("Position freshness", fixture.Token);
        var source = board.Sections[0];
        var firstId = source.Pages[0].Id;
        var second = fixture.Boards.AddPage(board, source.Id, "Second");
        var third = fixture.Boards.AddPage(board, source.Id, "Third");
        await fixture.Boards.SaveAsync(board, "Seed position hierarchy", fixture.Token);
        Assert.True(await fixture.Page.OpenDeepLinkAsync(new BoardsDeepLink(board.Id, source.Id, second.Id).ToString(), fixture.Token));
        var original = fixture.Page.Document!;
        var stale = fixture.Page.CreatePageMovementMenu(original, second.Id);
        fixture.Boards.MovePage(original, third.Id, source.Id, 0);
        await fixture.Boards.SaveAsync(original, "Intervening genuine reorder", fixture.Token);
        var currentPath = Path.Combine(fixture.Paths.DataDirectory, "Notes", "Documents", board.Id.ToString("D"), "current.haven-notes.json");
        var before = await File.ReadAllBytesAsync(currentPath, fixture.Token);
        stale.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move page down"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(await fixture.Page.PendingPageMovement.WaitAsync(fixture.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(currentPath, fixture.Token));
        var fresh = fixture.Page.CreatePageMovementMenu(original, second.Id);
        fresh.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move page up"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(await fixture.Page.PendingPageMovement.WaitAsync(fixture.Token));
        var reopened = await new NotesRepository(fixture.Paths, new NotesDocumentValidator(), fixture.Diagnostics).LoadAsync(board.Id, fixture.Token);
        Assert.NotNull(reopened);
        Assert.Equal(new[] { third.Id, second.Id, firstId }, reopened.Sections[0].Pages.Select(page => page.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(60));
        private readonly HavenEventBus _bus = new();
        internal Paths Paths { get; } = new();
        internal ProductionDiagnostics Diagnostics { get; }
        internal BoardsWorkspaceService Boards { get; }
        internal BoardsPage Page { get; }
        internal CancellationToken Token => _lifetime.Token;
        internal Fixture()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Diagnostics = new ProductionDiagnostics(Paths);
            Boards = new BoardsWorkspaceService(new NotesRepository(Paths, new NotesDocumentValidator(), Diagnostics));
            Page = new BoardsPage(_bus, Boards);
        }
        public async ValueTask DisposeAsync()
        {
            Page.Dispose(); _bus.Dispose(); _lifetime.Dispose(); await Diagnostics.DisposeAsync();
            Directory.Delete(Paths.DataDirectory, true);
        }
    }
    internal sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-boards-move-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
