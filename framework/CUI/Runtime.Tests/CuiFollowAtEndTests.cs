using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiFollowAtEndTests
{
    [Fact]
    public async Task Transcript_follows_growth_but_preserves_reading_position_until_reader_returns_to_end()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack" VerticalScrolling="True" FollowAtEnd="True">
                  <Text Text="First response" Height="800" />
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var transcript = Assert.IsType<CuiContainerControl>(loaded.Root);
            var scroll = Assert.IsType<ScrollViewer>(transcript.Child);
            var body = transcript.AuthoredChildren[0];
            var window = new Window { Content = transcript, Width = 420, Height = 240 };
            try
            {
                window.Show(); Pump(window);
                AssertAtEnd(scroll);
                body.Height = 1000; Pump(window); AssertAtEnd(scroll);
                scroll.Offset = new Vector(0, 100); Pump(window);
                body.Height = 1200; Pump(window);
                Assert.Equal(100, scroll.Offset.Y, 1);
                window.Height = 280; Pump(window);
                Assert.Equal(100, scroll.Offset.Y, 1);
                scroll.ScrollToEnd(); Pump(window);
                body.Height = 1400; Pump(window); AssertAtEnd(scroll);
                transcript.SetFollowAtEnd(false);
                var previous = scroll.Offset.Y;
                body.Height = 1600; Pump(window); Assert.Equal(previous, scroll.Offset.Y, 1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reader_scroll_in_same_layout_as_stream_growth_stops_following()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Container Type=\"Vertical Stack\" VerticalScrolling=\"True\" FollowAtEnd=\"True\"><Text Height=\"800\" Text=\"Response\" /></Container>");
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var scroll = Assert.IsType<ScrollViewer>(container.Child);
            var window = new Window { Content = container, Width = 420, Height = 240 };
            try
            {
                window.Show(); Pump(window); AssertAtEnd(scroll);
                scroll.Offset = new Vector(0, 100);
                container.AuthoredChildren[0].Height = 1000;
                Pump(window);
                Assert.Equal(100, scroll.Offset.Y, 1);
                container.AuthoredChildren[0].Height = 1200;
                Pump(window);
                Assert.Equal(100, scroll.Offset.Y, 1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Vertical_follow_preserves_horizontal_position_and_tracks_layout_clamping()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Container Type=\"Vertical Stack\" HorizontalScrolling=\"True\" VerticalScrolling=\"True\" FollowAtEnd=\"True\"><Text Width=\"1000\" Height=\"800\" Text=\"Response\" /></Container>");
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var scroll = Assert.IsType<ScrollViewer>(container.Child);
            var window = new Window { Content = container, Width = 420, Height = 240 };
            try
            {
                window.Show(); Pump(window); AssertAtEnd(scroll);
                scroll.Offset = new Vector(160, scroll.Offset.Y); Pump(window);
                container.AuthoredChildren[0].Height = 1000; Pump(window);
                AssertAtEnd(scroll); Assert.Equal(160, scroll.Offset.X, 1);
                container.AuthoredChildren[0].Height = 500; Pump(window);
                AssertAtEnd(scroll); Assert.Equal(160, scroll.Offset.X, 1);
                container.AuthoredChildren[0].Height = 1100; Pump(window);
                AssertAtEnd(scroll); Assert.Equal(160, scroll.Offset.X, 1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Ordinary_scroll_container_does_not_jump_to_end_when_content_changes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Container Type=\"Vertical Stack\" VerticalScrolling=\"True\"><Text Height=\"800\" Text=\"Document\" /></Container>");
            Assert.Empty(loaded.Diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded.Root);
            var scroll = Assert.IsType<ScrollViewer>(container.Child);
            var window = new Window { Content = container, Width = 420, Height = 240 };
            try
            {
                window.Show(); Pump(window); Assert.Equal(0, scroll.Offset.Y);
                container.AuthoredChildren[0].Height = 1200; Pump(window);
                Assert.Equal(0, scroll.Offset.Y);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertAtEnd(ScrollViewer scroll)
    {
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.InRange(scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y, -1, 1);
    }

    private static void Pump(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
        }
    }
}
