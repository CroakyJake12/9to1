using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Boards;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class BoardsNotebookSwitchSnapshotTests
{
    [AvaloniaFact]
    public async Task Same_board_navigation_keeps_unsaved_native_title_and_picker_switch_persists_exact_page_identity()
    {
        await using var f = new Fixture();
        var alpha = await f.Boards.CreateNotebookAsync("Alpha", f.Token);
        var beta = await f.Boards.CreateNotebookAsync("Beta", f.Token);
        Assert.True(await f.Page.OpenDeepLinkAsync(new BoardsDeepLink(alpha.Id).ToString(), f.Token));
        var pageId = f.Page.CurrentPage!.Id;
        f.Page.PageTitleInput!.Text = "Unsaved native title";
        var live = f.Page.Document;
        Assert.True(await f.Page.OpenDeepLinkAsync(new BoardsDeepLink(alpha.Id).ToString(), f.Token));
        Assert.Same(live, f.Page.Document);
        Assert.Equal("Unsaved native title", f.Page.CurrentPage!.Title);
        var retiredTitle = f.Page.PageTitleInput!;
        await f.SelectAsync(beta.Id);
        Assert.Equal(beta.Id, f.Page.Document!.Id);
        var destinationTitle = f.Page.CurrentPage!.Title;
        retiredTitle.Text = "Retired editor must not write replacement board";
        Assert.Equal(destinationTitle, f.Page.CurrentPage.Title);
        var saved = await f.Repository.LoadAsync(alpha.Id, f.Token);
        Assert.Equal(pageId, saved!.Sections[0].Pages[0].Id);
        Assert.Equal("Unsaved native title", saved.Sections[0].Pages[0].Title);
    }

    [AvaloniaFact]
    public async Task Failed_current_save_keeps_actual_editor_draft_and_retry_switch_persists_it()
    {
        await using var f = new Fixture();
        var alpha = await f.Boards.CreateNotebookAsync("Alpha", f.Token);
        var beta = await f.Boards.CreateNotebookAsync("Beta", f.Token);
        Assert.True(await f.Page.OpenDeepLinkAsync(new BoardsDeepLink(alpha.Id).ToString(), f.Token));
        var oldVersion = (await f.Repository.LoadAsync(alpha.Id, f.Token))!.Version;
        f.Page.PageTitleInput!.Text = "Retry this draft";
        f.Proxy.FailNextSave = true;
        await f.SelectAsync(beta.Id);
        Assert.Equal(alpha.Id, f.Page.Document!.Id);
        Assert.Equal("Retry this draft", f.Page.CurrentPage!.Title);
        var unchanged = await f.Repository.LoadAsync(alpha.Id, f.Token);
        Assert.Equal(oldVersion, unchanged!.Version);
        Assert.NotEqual("Retry this draft", unchanged.Sections[0].Pages[0].Title);
        await f.SelectAsync(beta.Id);
        Assert.Equal(beta.Id, f.Page.Document!.Id);
        Assert.Equal("Retry this draft", (await f.Repository.LoadAsync(alpha.Id, f.Token))!.Sections[0].Pages[0].Title);
    }

    [AvaloniaFact]
    public async Task Actual_save_acknowledges_captured_title_but_later_native_edit_keeps_current_board_open_until_retry()
    {
        await using var f = new Fixture();
        var alpha = await f.Boards.CreateNotebookAsync("Alpha", f.Token);
        var beta = await f.Boards.CreateNotebookAsync("Beta", f.Token);
        Assert.True(await f.Page.OpenDeepLinkAsync(new BoardsDeepLink(alpha.Id).ToString(), f.Token));
        var pageId = f.Page.CurrentPage!.Id;
        f.Page.PageTitleInput!.Text = "Captured title";
        f.Proxy.HoldNextSave = true;
        var picker = Assert.IsType<BoardsNotebookPicker>(await f.Page.CreateBoardPickerAsync());
        picker.MatchButtons.Single(b => Equals(b.Tag, beta.Id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            await f.Proxy.Entered.Task.WaitAsync(f.Token);
            f.Page.PageTitleInput!.Text = "Later title";
        }
        finally { f.Proxy.Release.TrySetResult(); }
        await picker.PendingSelection.WaitAsync(f.Token);
        Assert.Equal(alpha.Id, f.Page.Document!.Id);
        Assert.Equal("Later title", f.Page.CurrentPage!.Title);
        var captured = await f.Repository.LoadAsync(alpha.Id, f.Token);
        Assert.Equal("Captured title", captured!.Sections[0].Pages[0].Title);
        Assert.Equal(captured.Version, f.Page.Document.Version);
        Assert.True(f.Page.Document.Recovery.HasUnsavedRecovery);
        await f.SelectAsync(beta.Id);
        Assert.Equal(beta.Id, f.Page.Document!.Id);
        var latest = await f.Repository.LoadAsync(alpha.Id, f.Token);
        Assert.Equal(pageId, latest!.Sections[0].Pages[0].Id);
        Assert.Equal("Later title", latest.Sections[0].Pages[0].Title);
        Assert.True(latest.Version > captured.Version);
    }

    public class SaveTiming : DispatchProxy
    {
        public IBoardsWorkspaceService Actual { get; set; } = null!;
        public CancellationToken Lifetime { get; set; }
        public bool HoldNextSave { get; set; }
        public bool FailNextSave { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.Name == nameof(IBoardsWorkspaceService.SaveAsync))
            {
                if (FailNextSave) { FailNextSave = false; return Task.FromException(new IOException("Before owning save")); }
                if (HoldNextSave) { HoldNextSave = false; return HeldAsync((NotesDocument)args![0]!, (string)args[1]!, (CancellationToken)args[2]!); }
            }
            return method.Invoke(Actual, args);
        }
        private async Task HeldAsync(NotesDocument document, string reason, CancellationToken ct)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(Lifetime);
            await Actual.SaveAsync(document, reason, ct);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(60));
        private readonly HavenEventBus _bus = new();
        private readonly BoardsSearchablePickerTests.Paths _paths = new();
        private readonly ProductionDiagnostics _diagnostics;
        internal NotesRepository Repository { get; }
        internal BoardsWorkspaceService Boards { get; }
        internal BoardsPage Page { get; }
        internal SaveTiming Proxy { get; }
        internal CancellationToken Token => _lifetime.Token;
        internal Fixture()
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            _diagnostics = new ProductionDiagnostics(_paths);
            Repository = new NotesRepository(_paths, new NotesDocumentValidator(), _diagnostics);
            Boards = new BoardsWorkspaceService(Repository);
            var port = DispatchProxy.Create<IBoardsWorkspaceService, SaveTiming>();
            Proxy = (SaveTiming)port; Proxy.Actual = Boards; Proxy.Lifetime = Token;
            Page = new BoardsPage(_bus, port);
        }
        internal async Task SelectAsync(Guid id)
        {
            var picker = Assert.IsType<BoardsNotebookPicker>(await Page.CreateBoardPickerAsync());
            picker.MatchButtons.Single(b => Equals(b.Tag, id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await picker.PendingSelection.WaitAsync(Token);
        }
        public async ValueTask DisposeAsync()
        {
            Proxy.Release.TrySetResult(); Page.Dispose(); _bus.Dispose(); _lifetime.Dispose();
            await _diagnostics.DisposeAsync(); Directory.Delete(_paths.DataDirectory, true);
        }
    }
}
