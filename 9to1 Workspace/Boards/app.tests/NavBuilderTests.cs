using Avalonia.Controls;
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
            var collapse = Assert.IsType<Button>(Assert.IsType<StackPanel>(nav.Children[0]).Children[0]);
            collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            NavBuilder.Rebuild(nav, firstVm);
            Assert.Single(nav.Children);

            NavBuilder.Rebuild(nav, secondVm);
            Assert.True(nav.Children.Count > 1);
        });
    }
}
