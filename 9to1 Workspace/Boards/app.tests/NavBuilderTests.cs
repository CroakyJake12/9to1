using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Apps.Boards.App;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class NavBuilderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Page_hover_keeps_the_owning_row_radius_and_retains_visible_feedback(bool selected)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var vm = new BoardsViewModel(session);
        var section = session.Document.Sections[0];
        var first = section.Pages[0];
        var other = new RichBoardPage { Title = "Other page" };
        section.Pages.Add(other);
        vm.SelectPage(section.Id, selected ? first.Id : other.Id);
        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, vm);
            var row = nav.Children.OfType<Border>().Skip(1).First();
            var label = Assert.IsType<Grid>(row.Child).Children.OfType<Button>()
                .Single(button => Grid.GetColumn(button) == 3);
            var window = new Window { Content = nav, Width = 380, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using (var capture = window.CaptureRenderedFrame()) { }
                var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); Dispatcher.UIThread.RunJobs();
                var hoverFrame = label.GetVisualDescendants().OfType<CuiMaterialBorder>().First();
                Assert.True(label.IsPointerOver);
                Assert.NotNull(hoverFrame.HoverTint);
                Assert.Equal(new CornerRadius(selected ? 8 : 4), row.CornerRadius);
                Assert.Equal(row.CornerRadius, label.CornerRadius);
                Assert.Equal(row.CornerRadius, hoverFrame.CornerRadius);
            }
            finally { window.Close(); }
        });
        TestUiThread.Run(vm.Dispose);
    }

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
    public async Task Section_context_menu_opens_the_authored_persistable_icon_picker()
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
            var appearance = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Icon and color…"));
            appearance.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(vm.Get("IsSectionIconPickerOpen") as bool?);
        });

        var allChoices = Assert.IsAssignableFrom<IReadOnlyList<BoardsSectionIconChoiceView>>(vm.Get("SectionIconPickerChoices"));
        Assert.Equal(BoardsIcons.SectionIconChoices.Count, allChoices.Count);
        TestUiThread.Run(() => Assert.True(vm.TrySetValue("SectionIconPickerSearch", "stud")));
        var filtered = Assert.IsAssignableFrom<IReadOnlyList<BoardsSectionIconChoiceView>>(vm.Get("SectionIconPickerChoices"));
        Assert.Equal("study", Assert.Single(filtered).Key);
        await vm.DispatchAsync("ApplySectionIconChoice", "study");
        Assert.Equal("study", section.IconKey);
        Assert.Equal("#187B72", section.IconColor);
        Assert.True(vm.Get("IsSectionIconPickerOpen") as bool?);
        await vm.DispatchAsync("CloseSectionIconPicker", null);
        Assert.False(vm.Get("IsSectionIconPickerOpen") as bool?);
        vm.Dispose();
    }

    [Fact]
    public async Task Navigation_page_icon_uses_the_filled_file_family()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var vm = new BoardsViewModel(session);

        TestUiThread.Run(() =>
        {
            var nav = new StackPanel();
            NavBuilder.Rebuild(nav, vm);
            var pageRow = Assert.IsType<Border>(nav.Children.OfType<Border>().Skip(1).Single());
            var grid = Assert.IsType<Grid>(pageRow.Child);
            var icon = Assert.Single(grid.Children.OfType<Image>());
            Assert.Equal("Page icon", Avalonia.Automation.AutomationProperties.GetName(icon));
            var drawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(icon.Source).Drawing);
            Assert.NotNull(drawing.Brush);
            Assert.NotNull(drawing.Pen); // The section-family file silhouette retains its authored fold and text lines.
        });

        TestUiThread.Run(vm.Dispose);
    }
}
