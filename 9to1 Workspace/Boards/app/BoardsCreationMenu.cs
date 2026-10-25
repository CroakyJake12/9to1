using Avalonia.Automation;
using Avalonia.Controls;

namespace CakeOS.Apps.Boards.App;

/// <summary>Accessible creation menu shared by the Boards sidebar's + New action.</summary>
public static class BoardsCreationMenu
{
    public static MenuFlyout Create(BoardsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(Action("Notes Page", "Create a notes page", "AddPage", viewModel));
        menu.Items.Add(Action("Attach existing Write…", "Attach an existing Write document as a page", "AddWritePage", viewModel,
            viewModel.CanAddWritePage));
        menu.Items.Add(Action("New Write…", "Create a Write document as a page", "CreateWritePage", viewModel,
            viewModel.CanAddWritePage));
        menu.Items.Add(Action("Attach existing Canvas…", "Attach an existing Canvas as a page", "AddCanvasPage", viewModel,
            viewModel.CanAddCanvasPage));
        menu.Items.Add(Action("New Canvas…", "Create a Canvas as a page", "CreateCanvasPage", viewModel,
            viewModel.CanAddCanvasPage));
        menu.Items.Add(Action("Board", "Create a separate board file", "NewBoard", viewModel));
        menu.Items.Add(Action("Section", "Create a section", "AddSection", viewModel));
        return menu;
    }

    private static MenuItem Action(string header, string accessibleName, string command, BoardsViewModel viewModel,
        bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        AutomationProperties.SetName(item, accessibleName);
        item.Click += async (_, _) => await viewModel.DispatchAsync(command, null);
        return item;
    }
}
