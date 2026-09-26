using HavenOS.Images;
using Avalonia.Media.Imaging;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Automation;
using System.Runtime.InteropServices;
using Avalonia;
using System.Reflection;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class ImageJourneyTests
{
    [Theory]
    [InlineData("sample.png")]
    [InlineData("sample.JPG")]
    [InlineData("sample.jpeg")]
    [InlineData("sample.bmp")]
    [InlineData("sample.GIF")]
    [InlineData("sample.webp")]
    public void PickerPolicyAcceptsSupportedRasterExtensions(string path)
    {
        Assert.True(ImageFilePolicy.IsSupportedPath(path));
    }

    [Theory]
    [InlineData("sample.svg")]
    [InlineData("sample.tiff")]
    [InlineData("sample.txt")]
    [InlineData("")]
    public void PickerPolicyRejectsUnlistedExtensions(string path)
    {
        Assert.False(ImageFilePolicy.IsSupportedPath(path));
    }

    [Fact]
    public void NavigationUsesOnlySupportedSiblingImagesInFileNameOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"haven-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var first = Path.Combine(directory, "a.webp");
            var selected = Path.Combine(directory, "b.JPG");
            var last = Path.Combine(directory, "z.png");
            var ignored = Path.Combine(directory, "notes.txt");

            File.WriteAllBytes(first, []);
            File.WriteAllBytes(selected, []);
            File.WriteAllBytes(last, []);
            File.WriteAllText(ignored, "not an image");

            var session = ImageNavigationSession.FromSelection(selected);

            Assert.Equal(selected, session.CurrentPath);
            Assert.True(session.CanMovePrevious);
            Assert.True(session.CanMoveNext);
            Assert.Equal(first, session.MovePrevious());
            Assert.Null(session.MovePrevious());
            Assert.Equal(selected, session.MoveNext());
            Assert.Equal(last, session.MoveNext());
            Assert.Null(session.MoveNext());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ViewportZoomKeepsTheImagePointUnderThePointerFixed()
    {
        var viewport = new ImageViewportState();
        const double width = 800;
        const double height = 600;
        const double pointerX = 300;
        const double pointerY = 200;

        var imageXBeforeZoom = width / 2 + (pointerX - width / 2 - viewport.OffsetX) / viewport.Scale;
        var imageYBeforeZoom = height / 2 + (pointerY - height / 2 - viewport.OffsetY) / viewport.Scale;

        Assert.True(viewport.ZoomAt(2, pointerX, pointerY, width, height));

        var imageXAfterZoom = width / 2 + (pointerX - width / 2 - viewport.OffsetX) / viewport.Scale;
        var imageYAfterZoom = height / 2 + (pointerY - height / 2 - viewport.OffsetY) / viewport.Scale;
        Assert.Equal(imageXBeforeZoom, imageXAfterZoom, precision: 10);
        Assert.Equal(imageYBeforeZoom, imageYAfterZoom, precision: 10);
        Assert.Equal(2, viewport.Scale);
    }

    [Fact]
    public void ViewportPanAndResetUpdateZoomAvailability()
    {
        var viewport = new ImageViewportState();

        Assert.True(viewport.CanZoomOut);
        Assert.True(viewport.CanZoomIn);
        Assert.True(viewport.ZoomAt(0.01, 400, 300, 800, 600));
        Assert.Equal(ImageViewportState.MinimumScale, viewport.Scale);
        Assert.False(viewport.CanZoomOut);
        Assert.True(viewport.Reset());
        Assert.True(viewport.ZoomAt(16, 400, 300, 800, 600));
        Assert.Equal(ImageViewportState.MaximumScale, viewport.Scale);
        Assert.False(viewport.CanZoomIn);
        Assert.True(viewport.PanBy(24, -12));
        Assert.Equal(24, viewport.OffsetX);
        Assert.Equal(-12, viewport.OffsetY);
        Assert.True(viewport.Reset());
        Assert.True(viewport.IsDefault);
        Assert.True(viewport.CanZoomOut);
    }

    [Fact]
    public void ViewportRejectsPanAndZoomThatWouldOverflowWithoutChangingState()
    {
        var viewport = new ImageViewportState();
        Assert.True(viewport.PanBy(double.MaxValue, double.MaxValue));

        Assert.False(viewport.PanBy(double.MaxValue, 1));
        Assert.Equal(double.MaxValue, viewport.OffsetX);
        Assert.Equal(double.MaxValue, viewport.OffsetY);
        Assert.Equal(1, viewport.Scale);

        Assert.False(viewport.ZoomAt(2, 0, 0, 2, 2));
        Assert.Equal(double.MaxValue, viewport.OffsetX);
        Assert.Equal(double.MaxValue, viewport.OffsetY);
        Assert.Equal(1, viewport.Scale);
        Assert.True(double.IsFinite(viewport.OffsetX));
        Assert.True(double.IsFinite(viewport.OffsetY));

        Assert.True(viewport.Reset());
        Assert.True(viewport.IsDefault);
        Assert.True(viewport.PanBy(12, -7));
        Assert.Equal(12, viewport.OffsetX);
        Assert.Equal(-7, viewport.OffsetY);
    }

    [Fact]
    public async Task PictureDocumentCropIsNonDestructiveAndRoundTripsWithStableIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = PictureDocument.Create(1600, 1200, "files:asset-42", "rev-7");
        var cropped = document.Crop(100, 80, 1200, 900);
        var path = Path.Combine(Path.GetTempPath(), $"picture-{Guid.NewGuid():N}.picture.json");
        try
        {
            await cropped.SaveAsync(path, cancellationToken);
            var reopened = await PictureDocument.OpenAsync(path, cancellationToken);

            Assert.Equal(document.DocumentId, reopened.DocumentId);
            Assert.Equal(document.FileId, reopened.FileId);
            Assert.Equal(document.SourcePath, reopened.SourcePath);
            Assert.Equal(document.SourceRevision, reopened.SourceRevision);
            Assert.Equal(1600, document.CanvasWidth);
            Assert.Equal(1200, document.CanvasHeight);
            Assert.Equal(1200, reopened.CanvasWidth);
            Assert.Equal(900, reopened.CanvasHeight);
            Assert.Equal(1, reopened.Revision);
            Assert.Equal(new CropOperation(100, 80, 1200, 900), Assert.IsType<CropOperation>(Assert.Single(reopened.Operations)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PictureDocumentRejectsOutOfBoundsCropWithoutChangingSource()
    {
        var document = PictureDocument.Create(100, 80);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.Crop(90, 0, 20, 20));
        Assert.Equal(100, document.CanvasWidth);
        Assert.Equal(80, document.CanvasHeight);
        Assert.Empty(document.Operations);
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public async Task PictureDocumentRejectsUnknownSchemaInsteadOfGuessing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"picture-{Guid.NewGuid():N}.picture.json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"documentId\":\"2bd17d91-7957-41a6-a2f5-d4d810d79d32\",\"schemaVersion\":99,\"canvasWidth\":10,\"canvasHeight\":10,\"operations\":[],\"revision\":0}", cancellationToken);
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => PictureDocument.OpenAsync(path, cancellationToken));
            Assert.Contains("schema version 99", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task CropExportUsesSourcePixelsAndReopenedDocument()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"picture-crop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.bmp");
        var documentPath = Path.Combine(directory, "edit.picture.json");
        var exportPath = Path.Combine(directory, "crop.png");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, CreateTwoPixelBmp(), TestContext.Current.CancellationToken);
            var service = new PictureCropService();
            var sourceDocument = service.OpenSource(sourcePath);
            Assert.Null(sourceDocument.FileId);
            Assert.Equal(Path.GetFullPath(sourcePath), sourceDocument.SourcePath);
            var document = sourceDocument.Crop(1, 0, 1, 1);
            await document.SaveAsync(documentPath, TestContext.Current.CancellationToken);
            var reopened = await PictureDocument.OpenAsync(documentPath, TestContext.Current.CancellationToken);

            service.ExportPng(reopened, exportPath);
            using var exported = new Bitmap(exportPath);
            Assert.Equal(new PixelSize(1, 1), exported.PixelSize);
            var pixel = ReadFirstPixel(exported);
            Assert.True(pixel.G > pixel.R, $"Expected the green source pixel, got R={pixel.R}, G={pixel.G}, B={pixel.B}.");
            Assert.True(File.Exists(sourcePath));
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken))), reopened.SourceRevision);
            var exportedBytes = await File.ReadAllBytesAsync(exportPath, TestContext.Current.CancellationToken);
            Assert.Throws<IOException>(() => service.ExportPng(reopened, exportPath));
            Assert.Throws<IOException>(() => service.ExportPng(reopened, sourcePath));
            Assert.Equal(exportedBytes, await File.ReadAllBytesAsync(exportPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ExportRejectsSourceChangedAfterDocumentWasOpened()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"picture-crop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.bmp");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, CreateTwoPixelBmp(), TestContext.Current.CancellationToken);
            var document = new PictureCropService().OpenSource(sourcePath).Crop(0, 0, 1, 1);
            await File.WriteAllBytesAsync(sourcePath, CreateTwoPixelBmp(alterPixels: true), TestContext.Current.CancellationToken);
            Assert.Throws<InvalidDataException>(() => new PictureCropService().ExportPng(document, Path.Combine(directory, "out.png")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MetadataReaderReportsAvailableFileFactsWithoutInventingOptionalMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"picture-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.bmp");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, CreateTwoPixelBmp(), TestContext.Current.CancellationToken);
            using var bitmap = new Bitmap(sourcePath);
            var metadata = ImageMetadataSnapshot.Read(sourcePath, bitmap);

            Assert.Equal("BMP", metadata.Format);
            Assert.Equal(2, metadata.PixelWidth);
            Assert.Equal(1, metadata.PixelHeight);
            Assert.Equal(new FileInfo(sourcePath).Length, metadata.FileSizeBytes);
            Assert.NotEqual(ImageMetadataAvailability.UnsupportedByReader, metadata.MetadataAvailability);
            Assert.DoesNotContain("Latitude", metadata.Fields.Keys);
            Assert.True(metadata.DpiX is null || double.IsFinite(metadata.DpiX.Value));
            Assert.True(metadata.DpiY is null || double.IsFinite(metadata.DpiY.Value));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task PngExportAppliesMetadataPrivacyModeAndNeverChangesTheSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"picture-metadata-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var bitmapPath = Path.Combine(directory, "source.bmp");
        var sourcePath = Path.Combine(directory, "source.png");
        var preservePath = Path.Combine(directory, "preserved.png");
        var privatePath = Path.Combine(directory, "private.png");
        var strippedPath = Path.Combine(directory, "stripped.png");
        try
        {
            await File.WriteAllBytesAsync(bitmapPath, CreateTwoPixelBmp(), TestContext.Current.CancellationToken);
            using (var bitmap = new Bitmap(bitmapPath))
            using (var stream = File.Create(sourcePath))
                bitmap.Save(stream);

            using (var sourceMetadata = TagLib.File.Create(sourcePath))
            {
                var pngTag = Assert.IsAssignableFrom<TagLib.Png.PngTag>(sourceMetadata.GetTag(TagLib.TagTypes.Png, create: true));
                pngTag.Title = "Preserve this image title";
                pngTag.Comment = "Preserve this non-location comment";
                if (sourceMetadata is TagLib.Image.File imageSource)
                {
                    imageSource.ImageTag.Latitude = 51.5;
                    imageSource.ImageTag.Longitude = -0.12;
                    imageSource.ImageTag.Altitude = 24;
                }
                sourceMetadata.Save();
            }
            using (var sourceMetadata = TagLib.File.Create(sourcePath))
            {
                var pngTag = Assert.IsAssignableFrom<TagLib.Png.PngTag>(sourceMetadata.GetTag(TagLib.TagTypes.Png, create: false));
                Assert.Equal("Preserve this image title", pngTag.Title);
            }

            var originalBytes = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
            var service = new PictureCropService();
            var document = service.OpenSource(sourcePath).Crop(0, 0, 1, 1);
            service.ExportPng(document, preservePath, PictureMetadataExportMode.Preserve);
            service.ExportPng(document, privatePath, PictureMetadataExportMode.RemoveLocation);
            service.ExportPng(document, strippedPath, PictureMetadataExportMode.RemoveAll);

            using (var preserved = TagLib.File.Create(preservePath))
            {
                var pngTag = Assert.IsAssignableFrom<TagLib.Png.PngTag>(preserved.GetTag(TagLib.TagTypes.Png, create: false));
                Assert.Equal("Preserve this image title", pngTag.Title);
                Assert.Equal("Preserve this non-location comment", pngTag.Comment);
            }
            using (var privateFile = TagLib.File.Create(privatePath))
            {
                Assert.Equal(TagLib.TagTypes.None, privateFile.TagTypesOnDisk);
            }
            using (var stripped = TagLib.File.Create(strippedPath))
                Assert.Null(stripped.GetTag(TagLib.TagTypes.Png, create: false));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void CropEditorRendersAtNormalAndMinimumWindowSizesWithAccessibleEmptyState()
    {
        var screenshotDirectory = Path.Combine(Path.GetTempPath(), "opencode", "picture-ui-qa");
        Directory.CreateDirectory(screenshotDirectory);
        var window = new MainWindow();
        try
        {
            window.Show();
            var cropBounds = window.FindControl<TextBox>("CropBoundsBox")!;
            var applyCrop = window.FindControl<Button>("ApplyCropButton")!;
            var exportCrop = window.FindControl<Button>("ExportCropButton")!;
            var info = window.FindControl<Button>("InfoButton")!;
            Assert.Equal("Crop bounds in current image pixels", AutomationProperties.GetName(cropBounds));
            Assert.False(applyCrop.IsEnabled);
            Assert.False(exportCrop.IsEnabled);
            Assert.False(info.IsEnabled);
            Assert.Equal("View image metadata", AutomationProperties.GetName(info));
            Assert.True(window.FindControl<StackPanel>("EmptyState")!.IsVisible);
            cropBounds.Focus();
            Assert.True(cropBounds.IsFocused);

            foreach (var (width, height, name) in new[] { (1080d, 760d, "normal"), (720d, 520d, "minimum") })
            {
                window.Width = width;
                window.Height = height;
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal((int)width, frame!.PixelSize.Width);
                Assert.Equal((int)height, frame.PixelSize.Height);
                using var output = File.Create(Path.Combine(screenshotDirectory, $"picture-{name}.png"));
                frame.Save(output);
            }

            var sourcePath = Path.Combine(screenshotDirectory, "qa-source.bmp");
            File.WriteAllBytes(sourcePath, CreateTwoPixelBmp());
            typeof(MainWindow).GetMethod("LoadLocalPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [sourcePath]);
            Assert.True(applyCrop.IsEnabled);
            Assert.False(exportCrop.IsEnabled);
            Assert.True(info.IsEnabled);
            cropBounds.Text = "not crop bounds";
            applyCrop.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var status = window.FindControl<TextBlock>("StatusText")!;
            Assert.Contains("Enter crop bounds", status.Text);
            using var errorFrame = window.CaptureRenderedFrame();
            Assert.NotNull(errorFrame);
            using var errorOutput = File.Create(Path.Combine(screenshotDirectory, "picture-error.png"));
            errorFrame!.Save(errorOutput);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CropExportDoesNotOverwriteSourceOrExistingUserFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"picture-crop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.bmp");
        var existingPath = Path.Combine(directory, "existing.png");
        try
        {
            var original = CreateTwoPixelBmp();
            await File.WriteAllBytesAsync(sourcePath, original);
            await File.WriteAllBytesAsync(existingPath, [1, 2, 3]);
            var service = new PictureCropService();
            var document = service.OpenSource(sourcePath).Crop(0, 0, 1, 1);

            Assert.Null(document.FileId);
            Assert.Equal(Path.GetFullPath(sourcePath), document.SourcePath);
            Assert.Throws<IOException>(() => service.ExportPng(document, sourcePath));
            Assert.Throws<IOException>(() => service.ExportPng(document, existingPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(sourcePath));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(existingPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateTwoPixelBmp(bool alterPixels = false)
    {
        var bytes = new byte[62];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(2).CopyTo(bytes, 18);
        BitConverter.GetBytes(1).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(8).CopyTo(bytes, 34);
        // BMP rows are BGR and padded to a four-byte boundary.
        bytes[54] = 0; bytes[55] = 0; bytes[56] = alterPixels ? (byte)0 : (byte)255;
        bytes[57] = 0; bytes[58] = alterPixels ? (byte)0 : (byte)255; bytes[59] = 0;
        return bytes;
    }

    private static (byte R, byte G, byte B) ReadFirstPixel(Bitmap bitmap)
    {
        var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), handle.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4);
            return (bytes[2], bytes[1], bytes[0]);
        }
        finally { handle.Free(); }
    }
}
