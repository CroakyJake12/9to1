using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Services;
using SkiaSharp;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class NewTabBackgroundAssetsTests
{
    [AvaloniaFact]
    public async Task Preview_and_adoption_preserve_the_original_and_isolate_the_managed_image_by_profile()
    {
        using var image = new SKBitmap(3, 2);
        image.Erase(SKColors.Gold);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var original = Path.Combine(folder.FullName, "chosen.png");
            await File.WriteAllBytesAsync(original, bytes);
            var assets = new NewTabBackgroundAssets(folder.FullName, "profile one");
            var token = TestContext.Current.CancellationToken;
            await using var stream = File.OpenRead(original);
            using var candidate = await assets.ReadCandidateAsync(stream, token);
            Assert.Equal(new PixelSize(3, 2), candidate.Bitmap.PixelSize);
            Assert.Empty(folder.GetDirectories());
            var id = await assets.AdoptAsync(candidate, token);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), id);
            Assert.Equal(id, await assets.AdoptAsync(candidate, token));
            using var restored = await new NewTabBackgroundAssets(folder.FullName, "profile one").LoadAsync(id, token);
            Assert.Equal(candidate.Bitmap.PixelSize, restored!.PixelSize);
            Assert.Null(await new NewTabBackgroundAssets(folder.FullName, "profile two").LoadAsync(id, token));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(original, token));
            await Assert.ThrowsAsync<InvalidDataException>(() => assets.LoadAsync("../chosen.png", token));
            await Assert.ThrowsAsync<InvalidDataException>(() => assets.ReadCandidateAsync(new MemoryStream([1, 2, 3]), token));
        }
        finally { folder.Delete(recursive: true); }
    }
}
