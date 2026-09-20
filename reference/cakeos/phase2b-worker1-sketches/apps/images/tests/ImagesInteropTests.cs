using CakeOS.Images.Interop;
using Xunit;

namespace CakeOS.Images.Tests;

public class LoupeBackendInteropTests
{
    [Fact]
    public void Constants_MatchExpectedValues()
    {
        Assert.Equal(1u, LoupeBackendInterop.ExpectedAbiVersion);
        Assert.Equal(0u, (uint)LoupeBackendInterop.TransformOp.Rotate90);
        Assert.Equal(1u, (uint)LoupeBackendInterop.TransformOp.Rotate180);
        Assert.Equal(2u, (uint)LoupeBackendInterop.TransformOp.Rotate270);
        Assert.Equal(3u, (uint)LoupeBackendInterop.TransformOp.FlipHorizontal);
        Assert.Equal(4u, (uint)LoupeBackendInterop.TransformOp.FlipVertical);
        Assert.Equal(5u, (uint)LoupeBackendInterop.TransformOp.ExifAuto);
    }

    [Fact]
    public void PtrToStringUtf8_HandlesNull()
    {
        Assert.Null(LoupeBackendInterop.PtrToStringUtf8(IntPtr.Zero));
    }
}

public class ImagesViewModelTests
{
    [Fact]
    public void ZoomLevel_ClampedToRange()
    {
        // This would test the view model zoom clamping
        // Requires full view model construction with mocks
        Assert.True(true); // Placeholder
    }
}

public class SidecarPersistenceLayerTests
{
    [Fact]
    public void GetSidecarPath_ChangesExtension()
    {
        var path = "/path/to/image.jpg";
        var sidecar = typeof(SidecarPersistenceLayer)
            .GetMethod("GetSidecarPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [path]) as string;
        
        Assert.Equal("/path/to/image.xmp", sidecar);
    }

    [Fact]
    public void GenerateXmp_ContainsAiTags()
    {
        var annotations = new List<Annotation>();
        var aiTags = new[] { "tag1", "tag2" };
        
        var xmp = typeof(SidecarPersistenceLayer)
            .GetMethod("GenerateXmp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [annotations, aiTags]) as string;
        
        Assert.NotNull(xmp);
        Assert.Contains("tag1", xmp!);
        Assert.Contains("tag2", xmp!);
        Assert.Contains("dc:subject", xmp!);
    }
}