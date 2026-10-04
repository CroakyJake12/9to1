using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HavenOS.Images;

public enum PictureMetadataExportMode
{
    Preserve,
    RemoveLocation,
    RemoveAll,
}

/// <summary>Applies editable crop operations to the original source at export time.</summary>
public sealed class PictureCropService
{
    public PictureDocument OpenSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var source = new Bitmap(path);
        using var input = File.OpenRead(path);
        var revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
        // A local path is not a Files FileID. Keep the two identities distinct until
        // the Files provider resolves and supplies a stable hosted identity.
        return PictureDocument.Create(source.PixelSize.Width, source.PixelSize.Height, sourceRevision: revision, sourcePath: Path.GetFullPath(path));
    }

    public void ExportPng(PictureDocument document, string destinationPath, PictureMetadataExportMode metadataMode = PictureMetadataExportMode.Preserve)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Enum.IsDefined(metadataMode))
            throw new ArgumentOutOfRangeException(nameof(metadataMode), metadataMode, "Choose a supported metadata export mode.");
        var sourcePath = document.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("The Picture source image is unavailable.", sourcePath);
        using (var input = File.OpenRead(sourcePath))
        {
            var currentRevision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
            if (!string.Equals(currentRevision, document.SourceRevision, StringComparison.Ordinal))
                throw new InvalidDataException("The source image changed since this Picture document was opened. Reopen it before exporting.");
        }
        using var source = new Bitmap(sourcePath);
        if (source.PixelSize.Width <= 0 || source.PixelSize.Height <= 0)
            throw new InvalidDataException("The source image has invalid dimensions.");

        var fullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(fullPath, Path.GetFullPath(sourcePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Export cannot replace the source image. Choose a separate destination.");
        if (File.Exists(fullPath))
            throw new IOException("The export destination already exists. Choose a new filename to preserve the existing image.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileNameWithoutExtension(fullPath)}.{Guid.NewGuid():N}.tmp.png");
        try
        {
            using var rendered = Render(source, document);
            using (var output = File.Create(temporaryPath)) rendered.Save(output);
            ApplyMetadataPolicy(sourcePath, temporaryPath, metadataMode);
            File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ApplyMetadataPolicy(string sourcePath, string renderedPath, PictureMetadataExportMode mode)
    {
        if (mode == PictureMetadataExportMode.RemoveAll)
        {
            using var rendered = TagLib.File.Create(renderedPath);
            rendered.RemoveTags(TagLib.TagTypes.AllTags);
            rendered.Save();
            return;
        }

        using var source = TagLib.File.Create(sourcePath);
        using var renderedFile = TagLib.File.Create(renderedPath);
        if (source is not TagLib.Image.File sourceImage || renderedFile is not TagLib.Image.File renderedImage)
            throw new NotSupportedException("The image format does not support metadata transfer to PNG. Choose Remove all metadata to export without metadata.");

        renderedImage.CopyFrom(sourceImage);
        if (mode == PictureMetadataExportMode.RemoveLocation)
        {
            foreach (var tag in renderedImage.ImageTag.AllTags)
            {
                if (tag is TagLib.IFD.IFDTag exif)
                    exif.Structure.RemoveTag(0, TagLib.IFD.Tags.IFDEntryTag.GPSIFD);
                if (tag is TagLib.Xmp.XmpTag xmp)
                    RemoveLocationNodes(xmp.NodeTree);
            }
            renderedImage.ImageTag.Latitude = null;
            renderedImage.ImageTag.Longitude = null;
            renderedImage.ImageTag.Altitude = null;
        }
        renderedImage.Save();
    }

    private static void RemoveLocationNodes(TagLib.Xmp.XmpNode node)
    {
        foreach (var child in node.Children.ToArray())
        {
            var location = child.Namespace switch
            {
                "http://ns.adobe.com/exif/1.0/" or "http://cipa.jp/exif/1.0/" => child.Name.StartsWith("GPS", StringComparison.Ordinal),
                "http://ns.adobe.com/photoshop/1.0/" => child.Name is "City" or "State" or "Country",
                "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/" => child.Name is "Location" or "CountryCode",
                "http://iptc.org/std/Iptc4xmpExt/2008-02-29/" => child.Name is "LocationCreated" or "LocationShown" or "LocationDetails",
                _ => false
            };
            if (location) node.RemoveChild(child);
            else RemoveLocationNodes(child);
        }
    }

    public static Bitmap Render(Bitmap source, PictureDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        Bitmap current = source;
        try
        {
            foreach (var operation in document.Operations)
            {
                var next = RenderOperation(current, operation);
                if (!ReferenceEquals(current, source)) current.Dispose();
                current = next;
            }
            if (current.PixelSize.Width != document.CanvasWidth || current.PixelSize.Height != document.CanvasHeight)
                throw new InvalidDataException("Document canvas dimensions do not match its operation graph.");
            if (ReferenceEquals(current, source))
                current = RenderOperation(source, new CropOperation(0, 0, source.PixelSize.Width, source.PixelSize.Height));
            var result = current;
            current = source;
            return result;
        }
        finally { if (!ReferenceEquals(current, source)) current.Dispose(); }
    }

    private static RenderTargetBitmap RenderOperation(Bitmap source, PictureOperation operation)
    {
        var width = source.PixelSize.Width;
        var height = source.PixelSize.Height;
        var sourceBounds = new Rect(0, 0, width, height);
        var targetWidth = width;
        var targetHeight = height;
        var transform = Matrix.Identity;
        switch (operation)
        {
            case CropOperation crop when crop.X >= 0 && crop.Y >= 0 && crop.Width > 0 && crop.Height > 0
                && (long)crop.X + crop.Width <= width && (long)crop.Y + crop.Height <= height:
                sourceBounds = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
                targetWidth = crop.Width;
                targetHeight = crop.Height;
                break;
            case RotateOperation rotate when rotate.ClockwiseQuarterTurns is >= 1 and <= 3:
                if (rotate.ClockwiseQuarterTurns % 2 != 0) { targetWidth = height; targetHeight = width; }
                transform = rotate.ClockwiseQuarterTurns switch
                {
                    1 => new Matrix(0, 1, -1, 0, height, 0),
                    2 => new Matrix(-1, 0, 0, -1, width, height),
                    _ => new Matrix(0, -1, 1, 0, 0, width)
                };
                break;
            case FlipOperation flip:
                transform = flip.Horizontal ? new Matrix(-1, 0, 0, 1, width, 0) : new Matrix(1, 0, 0, -1, 0, height);
                break;
            case ResizeOperation resize when resize.Width is >= 1 and <= 32768 && resize.Height is >= 1 and <= 32768
                && (long)resize.Width * resize.Height <= 100_000_000:
                targetWidth = resize.Width;
                targetHeight = resize.Height;
                break;
            default:
                throw new InvalidDataException("The image operation is unsupported or outside the current raster.");
        }
        var result = new RenderTargetBitmap(new PixelSize(targetWidth, targetHeight), source.Dpi);
        try
        {
            using var context = result.CreateDrawingContext();
            using var quality = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
            using var transformed = context.PushTransform(transform);
            var destination = operation is RotateOperation or FlipOperation ? new Rect(0, 0, width, height) : new Rect(0, 0, targetWidth, targetHeight);
            context.DrawImage(source, sourceBounds, destination);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
