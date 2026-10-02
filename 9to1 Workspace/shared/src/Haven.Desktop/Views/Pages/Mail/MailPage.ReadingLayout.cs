using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.Views.Pages.Mail;

public enum MailReadingPanePlacement { Right, Bottom, Off }

public sealed partial class MailPage
{
    public MailReadingPanePlacement ReadingPanePlacement { get; private set; }

    private void OnReadingPlacementChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_disposed || sender is not ComboBox picker || picker.SelectedIndex is < 0 or > 2) return;
        ReadingPanePlacement = (MailReadingPanePlacement)picker.SelectedIndex;
        _showReadingOnNarrow = false;
        ApplyResponsiveLayout(Bounds.Width);
        if (!_applyingReadingPreference)
        {
            var generation = ++_readingPreferenceGeneration;
            PendingReadingPreference = SaveReadingPreferenceAsync(ReadingPanePlacement, generation);
        }
    }

    private void ResetReadingPaneGeometry()
    {
        Grid.SetRowSpan(FolderPanel, 1); Grid.SetColumnSpan(MessagePanel, 1);
        Grid.SetRow(ReadingPanel, 0); Grid.SetColumn(ReadingPanel, 2); Grid.SetColumnSpan(ReadingPanel, 1);
        MailboxGrid.RowDefinitions[0].Height = new(1, GridUnitType.Star);
        MailboxGrid.RowDefinitions[1].Height = new(0);
        ReadingPanel.Margin = new Thickness(0);
    }

    private void ApplyChosenReadingPlacement()
    {
        if (ReadingPanePlacement == MailReadingPanePlacement.Right) return;
        // Wider windows keep navigation; narrow windows use the existing single-pane route.
        MailboxGrid.ColumnDefinitions[1].Width = new(1, GridUnitType.Star);
        MailboxGrid.ColumnDefinitions[2].Width = new(0);
        Grid.SetColumnSpan(MessagePanel, 2);
        Grid.SetColumn(ReadingPanel, 1); Grid.SetColumnSpan(ReadingPanel, 2);
        if (ReadingPanePlacement == MailReadingPanePlacement.Bottom)
        {
            Grid.SetRowSpan(FolderPanel, 2); Grid.SetRow(ReadingPanel, 1);
            MailboxGrid.RowDefinitions[1].Height = new(1, GridUnitType.Star);
            ReadingPanel.Margin = new Thickness(0, 10, 0, 0);
            return;
        }
        MessagePanel.IsVisible = !_showReadingOnNarrow;
        ReadingPanel.IsVisible = _showReadingOnNarrow;
        BackToListButton.IsVisible = _showReadingOnNarrow;
    }
}
