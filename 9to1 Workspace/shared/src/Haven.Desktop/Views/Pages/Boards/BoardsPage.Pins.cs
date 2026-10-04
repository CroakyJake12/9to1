using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private readonly HashSet<Guid> _pinnedNotebookIds = [];

    private async Task RefreshPinnedAsync()
    {
        _pinnedNotebookIds.Clear();
        foreach (var summary in _notebooks)
        {
            var notebook = await _boards.OpenNotebookAsync(summary.Id, CancellationToken.None);
            if (notebook is not null && _boards.IsPinned(notebook))
            {
                _pinnedNotebookIds.Add(summary.Id);
            }
        }
    }

    private Control BuildNotebookLibraryRow(NotesDocumentSummary summary)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        if (_showingTrash)
        {
            var restore = new Button { Content = "Restore", MinWidth = 68 };
            AutomationProperties.SetName(restore, $"Restore {summary.Title}");
            restore.Click += async (_, _) => await RestoreNotebookFromTrashAsync(summary.Id);
            Grid.SetColumn(restore, 1);
            var deletedTitle = new TextBlock { Text = summary.Title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            AutomationProperties.SetName(deletedTitle, $"Deleted board {summary.Title}");
            row.Children.Add(deletedTitle);
            row.Children.Add(restore);
            return row;
        }
        var open = ActionButton(
            ( _pinnedNotebookIds.Contains(summary.Id) ? "Pinned · " : string.Empty) + summary.Title,
            async () => await OpenNotebookAsync(summary.Id));
        open.HorizontalContentAlignment = HorizontalAlignment.Left;
        var pin = new Button
        {
            Content = _pinnedNotebookIds.Contains(summary.Id) ? "Unpin" : "Pin",
            MinWidth = 58
        };
        AutomationProperties.SetName(pin, $"{pin.Content} {summary.Title}");
        pin.Click += async (_, _) => await TogglePinnedAsync(summary.Id);
        Grid.SetColumn(pin, 1);
        var delete = new Button { Content = "Move to trash", MinWidth = 92 };
        AutomationProperties.SetName(delete, $"Move {summary.Title} to trash");
        delete.Click += async (_, _) => await DeleteNotebookToTrashAsync(summary.Id);
        Grid.SetColumn(delete, 2);
        row.Children.Add(open);
        row.Children.Add(pin);
        row.Children.Add(delete);
        return row;
    }

    private async Task DeleteNotebookToTrashAsync(Guid notebookId)
    {
        if (!await _boards.DeleteNotebookAsync(notebookId, CancellationToken.None)) return;
        if (_document?.Id == notebookId)
        {
            if (_documentEditor is not null) _documentEditor.Changed -= OnDocumentChanged;
            _document = null;
            _section = null;
            _page = null;
            _documentEditor = null;
            _editor.Children.Clear();
            _editor.Children.Add(new TextBlock { Text = "This board is in Trash. Restore it to continue editing." });
        }
        SetStatus("Moved board to recoverable Trash");
        await RefreshLibraryAsync();
    }

    private async Task RestoreNotebookFromTrashAsync(Guid notebookId)
    {
        if (!await _boards.RestoreNotebookAsync(notebookId, CancellationToken.None)) return;
        SetStatus("Restored board from Trash");
        await RefreshLibraryAsync();
    }

    private async Task TogglePinnedAsync(Guid notebookId)
    {
        var notebook = await _boards.OpenNotebookAsync(notebookId, CancellationToken.None);
        if (notebook is null) return;
        var pinned = _boards.IsPinned(notebook);
        _boards.SetPinned(notebook, !pinned);
        await _boards.SaveAsync(notebook, pinned ? "Unpinned Boards notebook" : "Pinned Boards notebook", CancellationToken.None);
        if (_document?.Id == notebookId)
            _boards.SetPinned(_document, !pinned);
        await RefreshLibraryAsync();
    }
}
