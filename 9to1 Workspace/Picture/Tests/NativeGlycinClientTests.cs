using Xunit;

namespace HavenOS.Images.Tests;

/// <summary>Requires the actual controlled libglycin client; never skips missing native packages.</summary>
public sealed class NativeGlycinClientTests
{
    [Fact]
    public void Actual_glycin_client_reports_native_image_failure_without_a_decoder_fallback()
    {
        var decoder = new PictureGlycinDecoder();
        // Invalid encoded bytes exercise real GBytes/loader allocations, forced
        // sandbox selection and native GError ownership. This proves client ABI
        // and error-path lifetime only, not successful format decoding.
        for (var index = 0; index < 3; index++)
        {
            var error = Assert.Throws<IOException>(() => decoder.DecodeFirstFrame([1, 2, 3]));
            Assert.StartsWith("Sandboxed libglycin load failed:", error.Message);
        }
    }
}
