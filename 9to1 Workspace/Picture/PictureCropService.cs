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
        if (mode is PictureMetadataExportMode.RemoveAll or PictureMetadataExportMode.RemoveLocation)
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
        renderedImage.Save();
    }

    public static Bitmap Render(Bitmap source, PictureDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        var x = 0;
        var y = 0;
        var width = source.PixelSize.Width;
        var height = source.PixelSize.Height;
        foreach (var operation in document.Operations)
        {
            if (operation is not CropOperation crop)
                throw new InvalidDataException("The document contains an unsupported image operation.");
            if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 || (long)crop.X + crop.Width > width || (long)crop.Y + crop.Height > height)
                throw new InvalidDataException("The crop operation is outside the source image.");
            x += crop.X;
            y += crop.Y;
            width = crop.Width;
            height = crop.Height;
        }
        if (width != document.CanvasWidth || height != document.CanvasHeight)
            throw new InvalidDataException("Document canvas dimensions do not match its operation graph.");

        var result = new RenderTargetBitmap(new PixelSize(width, height), source.Dpi);
        using (var context = result.CreateDrawingContext())
            context.DrawImage(source, new Rect(x, y, width, height), new Rect(0, 0, width, height));
        return result;
    }
}
