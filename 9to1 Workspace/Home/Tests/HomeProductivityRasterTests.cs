using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeProductivityRasterTests
{
    [Fact]
    public void Frame_detaches_source_and_each_reader_and_preserves_row_stride()
    {
        byte[] source = [1, 2, 3, 255, 9, 9, 9, 9];
        var frame = new HomeProductivityRasterFrame(1, 1, 8, source);
        source[0] = 99;
        var first = frame.CopyPixels(); first[1] = 99;
        Assert.Equal(new byte[] { 1, 2, 3, 255, 9, 9, 9, 9 }, frame.CopyPixels());
        Assert.Equal(8, frame.Stride);
    }
    [Fact]
    public void Frame_rejects_partial_overflow_and_unbounded_dimensions()
    {
        Assert.Throws<ArgumentException>(() => new HomeProductivityRasterFrame(1, 1, 4, new byte[3]));
        Assert.Throws<ArgumentException>(() => new HomeProductivityRasterFrame(8193, 1, 32772, []));
        Assert.Throws<ArgumentException>(() => new HomeProductivityRasterFrame(1, 2, int.MaxValue, []));
        Assert.Throws<ArgumentException>(() => new HomeProductivityRasterFrame(2, 1, 4, new byte[4]));
    }
}
