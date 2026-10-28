using System.Text.Json;
using Avalonia;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class ChromiumViewportTests
{
    [Theory]
    [InlineData(2192, 1065, 2, 4384, 2130)]
    [InlineData(1536, 864, 1.25, 1920, 1080)]
    public void Capture_uses_physical_resolution_while_input_stays_in_CSS_pixels(
        int width, int height, double scale, int pixelsWide, int pixelsHigh)
    {
        var view = ChromiumViewport.From(new Size(width, height), scale);
        Assert.Equal(pixelsWide, view.CaptureWidth); Assert.Equal(pixelsHigh, view.CaptureHeight);
        Assert.Equal(new Point(width / 2d, height / 2d), view.MapPoint(new Point(width / 2d, height / 2d), new Size(width, height)));
    }
    [Fact]
    public void Old_frame_keeps_its_aspect_ratio_during_a_resize()
    {
        var rectangle = ChromiumViewport.Fit(new Size(1280, 800), new Size(1920, 1080));
        Assert.Equal(1728, rectangle.Width); Assert.Equal(1080, rectangle.Height); Assert.Equal(96, rectangle.X);
        var view = ChromiumViewport.Default;
        Assert.Equal(new Point(640, 400), view.MapPoint(new Point(960, 540), new Size(1920, 1080)));
    }
    [Fact]
    public void Wheel_uses_logical_pixels_correct_direction_fractional_trackpad_and_shift()
    {
        Assert.Equal(new Vector(0, -50), ChromiumViewport.WheelPixels(new Vector(0, 1), false));
        Assert.Equal(new Vector(0, 12.5), ChromiumViewport.WheelPixels(new Vector(0, -.25), false));
        Assert.Equal(new Vector(-50, 0), ChromiumViewport.WheelPixels(new Vector(0, 1), true));
    }
    [Fact]
    public void Invalid_or_extreme_viewports_have_bounded_capture_allocations()
    {
        Assert.Equal(ChromiumViewport.Default, ChromiumViewport.From(default, double.NaN));
        var view = ChromiumViewport.From(new Size(100000, 100000), 10);
        Assert.True((long)view.CaptureWidth * view.CaptureHeight <= 32020000);
        Assert.InRange(view.CaptureWidth, 1, 8192);
    }
    [Fact]
    public void Input_waits_for_a_decoded_frame_matching_the_latest_requested_and_acknowledged_viewport()
    {
        var state = new ChromiumViewportFrameState();
        Assert.False(state.CanAcceptInput);
        state.Publish(ChromiumViewport.Default);
        Assert.True(state.CanAcceptInput);
        var resized = ChromiumViewport.From(new Size(1600, 900), 1.5);
        state.Request(resized);
        Assert.False(state.CanAcceptInput);
        state.Publish(ChromiumViewport.Default);
        Assert.False(state.CanAcceptInput);
        state.Acknowledge(resized);
        Assert.False(state.CanAcceptInput);
        state.Publish(ChromiumViewport.Default); // An old JPEG decoded after the metrics ACK.
        Assert.False(state.CanAcceptInput);
        state.Publish(resized);
        Assert.True(state.CanAcceptInput);
        Assert.Equal(new Point(800, 450), state.Displayed.MapPoint(new Point(800, 450), new Size(1600, 900)));
        var newer = ChromiumViewport.From(new Size(1800, 1000), 2);
        state.Request(newer);
        state.Publish(resized); // A coalesced previous capture must not reopen input.
        Assert.False(state.CanAcceptInput);
        state.Acknowledge(newer);
        state.Publish(null); // Missing metadata is not a matching frame witness.
        Assert.False(state.CanAcceptInput);
        state.Publish(newer);
        Assert.True(state.CanAcceptInput);
        Assert.Equal(newer, state.Displayed);
    }

    [Theory]
    [InlineData(1600, 900, true)]
    [InlineData(2400, 1350, false)]
    [InlineData(1280, 800, false)]
    public void Screencast_frame_identity_uses_CDP_DIP_dimensions_not_bitmap_pixels(int width, int height, bool matches)
    {
        var viewport = ChromiumViewport.From(new Size(1600, 900), 1.5);
        using var frame = JsonDocument.Parse(JsonSerializer.Serialize(new { metadata = new { deviceWidth = width, deviceHeight = height } }));
        Assert.Equal(matches, ChromiumEngineTab.MatchScreencastViewport(frame.RootElement, viewport) is not null);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"metadata\":{}}")]
    [InlineData("{\"metadata\":{\"deviceWidth\":\"bad\",\"deviceHeight\":800}}")]
    public void Invalid_frame_metadata_cannot_enable_pointer_mapping(string json)
    {
        using var frame = JsonDocument.Parse(json);
        Assert.Null(ChromiumEngineTab.MatchScreencastViewport(frame.RootElement, ChromiumViewport.Default));
    }
}
