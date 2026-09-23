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
        menu.Items.Add(Action("Board", "Create a separate board file", "NewBoard", viewModel));

        var canvas = new MenuItem { Header = "Canvas (coming later)", IsEnabled = false };
        AutomationProperties.SetName(canvas, "Canvas unavailable, coming later");
        ToolTip.SetTip(canvas, "Canvas is not available in Boards yet.");
        menu.Items.Add(canvas);

        menu.Items.Add(Action("Section", "Create a section", "AddSection", viewModel));
        return menu;
    }

    private static MenuItem Action(string header, string accessibleName, string command, BoardsViewModel viewModel)
    {
        var item = new MenuItem { Header = header };
        AutomationProperties.SetName(item, accessibleName);
        item.Click += async (_, _) => await viewModel.DispatchAsync(command, null);
        return item;
    }
}
