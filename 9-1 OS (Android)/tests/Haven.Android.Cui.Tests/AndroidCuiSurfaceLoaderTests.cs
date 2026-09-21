using Haven.Android.Cui;

namespace Haven.Android.Cui.Tests;

public sealed class AndroidCuiSurfaceLoaderTests
{
    private readonly AndroidCuiSurfaceLoader _loader = new();

    [Fact]
    public void Parse_projects_the_supported_android_surface()
    {
        const string markup = """
            <Cui id="runtime">
              <Stack id="content" orientation="vertical" gap="12">
                <Text role="title">Android CUI</Text>
                <Text role="caption">Native boundary</Text>
                <Button action="close">Close</Button>
              </Stack>
            </Cui>
            """;

        var surface = _loader.Parse(markup, "runtime.cui");

        Assert.Equal("runtime", surface.Id);
        var stack = Assert.IsType<AndroidCuiStackNode>(Assert.Single(surface.Children));
        Assert.Equal(AndroidCuiStackDirection.Vertical, stack.Direction);
        Assert.Equal(12, stack.Gap);
        Assert.Collection(
            stack.Children,
            node => Assert.Equal(AndroidCuiTextRole.Title, Assert.IsType<AndroidCuiTextNode>(node).Role),
            node => Assert.Equal(AndroidCuiTextRole.Caption, Assert.IsType<AndroidCuiTextNode>(node).Role),
            node => Assert.Equal("close", Assert.IsType<AndroidCuiButtonNode>(node).Action));
    }

    [Theory]
    [InlineData("legacy.axaml")]
    [InlineData("legacy.hui")]
    public void Parse_rejects_legacy_input_extensions(string sourceName)
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            _loader.Parse("<Cui />", sourceName));

        Assert.Contains(".cui", exception.Message, StringComparison.Ordinal);
        Assert.Contains(sourceName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("legacy.axaml")]
    [InlineData("legacy.hui")]
    public async Task LoadAsync_rejects_legacy_identity_before_reading(string sourceName)
    {
        var source = new RecordingSource("<Cui />");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await _loader.LoadAsync(sourceName, source));

        Assert.False(source.WasRead);
    }

    [Fact]
    public async Task LoadAsync_reads_and_projects_a_cui_source()
    {
        var source = new RecordingSource("<Cui><Text>Hello Android</Text></Cui>");

        var surface = await _loader.LoadAsync("asset/surface.cui", source);

        Assert.True(source.WasRead);
        Assert.Equal("Hello Android", Assert.IsType<AndroidCuiTextNode>(Assert.Single(surface.Children)).Text);
    }

    [Fact]
    public void Parse_rejects_elements_outside_the_bounded_android_vocabulary()
    {
        var exception = Assert.Throws<AndroidCuiSurfaceException>(() =>
            _loader.Parse("<Cui><WebView /></Cui>", "unsafe.cui"));

        Assert.Contains("WebView", exception.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingSource(string markup) : IAndroidCuiSource
    {
        public bool WasRead { get; private set; }

        public ValueTask<string> ReadAsync(string sourceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasRead = true;
            return ValueTask.FromResult(markup);
        }
    }
}
