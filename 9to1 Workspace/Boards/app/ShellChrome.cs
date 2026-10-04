// ShellChrome builds the top document bar shared by the desktop host and the
// headless visual harness: File menu entry, undo/redo, save state, theme
// toggle and overflow. Behavior callbacks come from the host.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

public static class ShellChrome
{
    public static void BuildTopBar(
        StackPanel left,
        StackPanel right,
        BoardsViewModel vm,
        Action onFileMenu,
        Action onOverflow,
        Action onTheme,
        Action<TextBlock> onSaveState)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(vm);

        var file = new Button
        {
            Content = "File",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(10, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(file, "Board file menu");
        AutomationProperties.SetName(file, "File menu");
        file.Click += (_, _) => onFileMenu();
        left.Children.Add(file);
        var undo = BoardsIcons.IconButton(BoardsIcons.Undo, "Undo", "Undo (Ctrl+Z)");
        undo.Click += (_, _) => _ = vm.DispatchAsync("Undo", null);
        left.Children.Add(undo);
        var redo = BoardsIcons.IconButton(BoardsIcons.Redo, "Redo", "Redo (Ctrl+Y)");
        redo.Click += (_, _) => _ = vm.DispatchAsync("Redo", null);
        left.Children.Add(redo);

        var saveState = new TextBlock
        {
            Text = "Saved",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(saveState, "Save state");
        right.Children.Add(saveState);
        onSaveState(saveState);
        var theme = BoardsIcons.IconButton(BoardsIcons.Theme, "Theme", "Toggle light/dark theme");
        theme.Click += (_, _) => onTheme();
        right.Children.Add(theme);
        var overflow = BoardsIcons.IconButton(BoardsIcons.More, "More", "More actions");
        overflow.Click += (_, _) => onOverflow();
        right.Children.Add(overflow);
    }
}

