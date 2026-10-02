using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

/// <summary>A local searchable switcher over the actual available Boards notebooks.</summary>
internal sealed class BoardsNotebookPicker : UserControl
{
    private readonly IReadOnlyList<NotesDocumentSummary> _boards;
    private readonly Guid? _current;
    private readonly Func<Guid, Task<bool>> _open;
    private readonly StackPanel _matches = new() { Spacing = 4 };
    internal TextBox Search { get; } = new() { PlaceholderText = "Search boards..." };
    internal Task PendingSelection { get; private set; } = Task.CompletedTask;
    internal event EventHandler? Opened;

    internal BoardsNotebookPicker(IReadOnlyList<NotesDocumentSummary> boards, Guid? current, Func<Guid, Task<bool>> open)
    {
        _boards = boards.ToArray();
        _current = current;
        _open = open;
        AutomationProperties.SetName(this, "Searchable board picker");
        AutomationProperties.SetName(Search, "Search available boards");
        var panel = new StackPanel { Spacing = 8, Width = 320 };
        panel.Children.Add(Search);
        panel.Children.Add(new ScrollViewer { Content = _matches, MaxHeight = 360 });
        Content = panel;
        AttachedToVisualTree += (_, _) => Search.Focus();
        Search.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) RefreshMatches();
        };
        RefreshMatches();
    }

    internal IReadOnlyList<Button> MatchButtons => _matches.Children.OfType<Button>().ToArray();

    private void RefreshMatches()
    {
        _matches.Children.Clear();
        var query = (Search.Text ?? string.Empty).Trim();
        foreach (var board in _boards.Where(value => value.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                     .OrderBy(value => value.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Id))
        {
            var button = new Button
            {
                Content = (_current == board.Id ? "✓ " : string.Empty) + board.Title,
                Tag = board.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            AutomationProperties.SetName(button, (_current == board.Id ? "Current board: " : "Open board: ") + board.Title);
            button.Click += (_, _) =>
            {
                if (!PendingSelection.IsCompleted) return;
                PendingSelection = OpenAsync(board.Id);
            };
            _matches.Children.Add(button);
        }
        if (_matches.Children.Count == 0)
            _matches.Children.Add(new TextBlock { Text = _boards.Count == 0 ? "No available boards" : "No matching boards" });
    }

    private async Task OpenAsync(Guid id)
    {
        IsEnabled = false;
        try
        {
            if (await _open(id)) Opened?.Invoke(this, EventArgs.Empty);
            else ShowFailure("This board is no longer available. Reopen the picker to refresh.");
        }
        catch (Exception)
        {
            ShowFailure("Couldn’t open this board. Reopen the picker to try again.");
        }
        finally { IsEnabled = true; }
    }

    private void ShowFailure(string message)
    {
        _matches.Children.Clear();
        _matches.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
    }
}
