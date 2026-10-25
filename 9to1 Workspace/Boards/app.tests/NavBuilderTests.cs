using Avalonia.Controls;
using Avalonia;
using CakeOS.Apps.Boards.App;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class NavBuilderTests
{
    [Fact]
    public async Task Collapsed_sections_do_not_leak_into_a_different_document_with_matching_ids()
    {
        await using var firstSession = new InMemoryRichBoardSession();
        await firstSession.OpenAsync(null);
        await using var secondSession = new InMemoryRichBoardSession();
        await secondSession.OpenAsync(null);

        var firstVm = new BoardsViewModel(firstSession);
        var secondVm = new BoardsViewModel(secondSession);
        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, firstVm);
            var sectionRow = Assert.IsType<Border>(nav.Children[0]);
            var collapse = Assert.IsType<Button>(Assert.IsType<Grid>(sectionRow.Child).Children[0]);
            collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            NavBuilder.Rebuild(nav, firstVm);
            Assert.Single(nav.Children);

            NavBuilder.Rebuild(nav, secondVm);
            Assert.True(nav.Children.Count > 1);
        });
    }

    [Fact]
    public async Task Deep_page_rows_have_consistent_depth_and_collapse_preserves_open_page()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var vm = new BoardsViewModel(session);
        var section = session.Document.Sections[0];
        var root = section.Pages[0];
        var child = new RichBoardPage { Title = "Worked examples", ParentPageId = root.Id };
        var grandchild = new RichBoardPage { Title = "Example 1", ParentPageId = child.Id };
        section.Pages.Add(child);
        section.Pages.Add(grandchild);
        vm.SelectPage(section.Id, grandchild.Id);

        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, vm);
            var rows = nav.Children.OfType<Border>().Skip(1).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(new[] { 16d, 32d, 48d }, rows.Select(row => row.Margin.Left));
            Assert.All(rows, row => Assert.Equal(34d, Assert.IsType<Grid>(row.Child).Height));
            var rootDisclosure = Assert.IsType<Button>(Assert.IsType<Grid>(rows[0].Child).Children.OfType<Button>().First());
            rootDisclosure.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            NavBuilder.Rebuild(nav, vm);
            Assert.Single(nav.Children.OfType<Border>().Skip(1));
            Assert.Equal(grandchild.Id, vm.SelectedPageId);

            vm.SelectPage(section.Id, root.Id);
            NavBuilder.Rebuild(nav, vm);
            vm.SelectPage(section.Id, grandchild.Id);
            NavBuilder.Rebuild(nav, vm);
            Assert.Equal(3, nav.Children.OfType<Border>().Skip(1).Count());
            var leafDisclosure = Assert.IsType<Button>(Assert.IsType<Grid>(nav.Children.OfType<Border>().Last().Child).Children.OfType<Button>().First());
            Assert.False(leafDisclosure.IsEnabled);
        });
    }

    [Fact]
    public async Task Section_context_menu_exposes_only_the_persistable_icon_and_color_choices()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var vm = new BoardsViewModel(session);
        var section = session.Document.Sections[0];
        await vm.SetSectionIconAsync(section.Id, "chat", "#187B72");
        Assert.Equal("chat", section.IconKey);
        Assert.Equal("#187B72", section.IconColor);

        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, vm);
            var header = Assert.IsType<Border>(nav.Children[0]);
            var row = Assert.IsType<Grid>(header.Child);
            var icon = Assert.IsType<Image>(row.Children[1]);
            Assert.Equal("Section icon", Avalonia.Automation.AutomationProperties.GetName(icon));
            var title = Assert.IsType<Button>(row.Children[2]);
            var menu = Assert.IsType<ContextMenu>(title.ContextMenu);
            var appearance = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Icon and color"));
            var iconMenu = Assert.Single(appearance.Items.OfType<MenuItem>(), item => Equals(item.Header, "Icon"));
            var colorMenu = Assert.Single(appearance.Items.OfType<MenuItem>(), item => Equals(item.Header, "Color"));
            Assert.Equal(BoardsIcons.SectionIconChoices.Count, iconMenu.Items.OfType<MenuItem>().Count());
            Assert.Equal(BoardsIcons.SectionIconColors.Count + 1, colorMenu.Items.OfType<MenuItem>().Count());
            Assert.Contains(iconMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Chat ✓"));
            Assert.Contains(colorMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Teal ✓"));
        });
    }
}
