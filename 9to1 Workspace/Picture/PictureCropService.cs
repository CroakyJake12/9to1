using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Haven.Productivity.NativeUI;

namespace HavenOS.Images;

public enum PictureMetadataExportMode
{
    Preserve,
    RemoveLocation,
    RemoveAll,
}

/// <summary>Applies editable crop operations to the original source at export time.</summary>
public sealed partial class PictureCropService
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
        using var source = OpenVerifiedSource(document);

        var fullPath = Path.GetFullPath(destinationPath);
        if (sourcePath is not null && string.Equals(fullPath, Path.GetFullPath(sourcePath),
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
            if (sourcePath is not null) ApplyMetadataPolicy(sourcePath, temporaryPath, metadataMode);
            if (sourcePath is not null && metadataMode != PictureMetadataExportMode.RemoveAll &&
                document.Operations.Any(operation => operation is ColorAdjustmentOperation))
                ProjectWorkingColorMetadata(temporaryPath);
            File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Resolves the retained local source and checks its exact revision before any preview or export.</summary>
    public static Bitmap OpenVerifiedSource(PictureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SourcePath is null)
        {
            var width = document.InitialCanvasWidth ?? document.CanvasWidth;
            var height = document.InitialCanvasHeight ?? document.CanvasHeight;
            return new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        }
        if (!File.Exists(document.SourcePath))
            throw new FileNotFoundException("The linked source image is unavailable. Restore it before editing or exporting.", document.SourcePath);
        using var input = new FileStream(document.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
        if (!string.Equals(revision, document.SourceRevision, StringComparison.Ordinal))
            throw new InvalidDataException("The source image changed since this Picture document was opened. Reopen it before editing or exporting.");
        input.Position = 0;
        return new Bitmap(input);
    }

    public static Bitmap Render(PictureDocument document)
    {
        using var source = OpenVerifiedSource(document);
        return Render(source, document);
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

    // Rendered RGBA8 output uses the user's explicit sRGB interpretation. Keep
    // title/camera/capture/location (subject to the chosen privacy policy) and
    // unknown non-colour metadata, while removing stale source ICC/transfer
    // claims. This is not an ICC transform, and never changes source metadata.
    private static void ProjectWorkingColorMetadata(string originalTemporaryExport)
    {
        using var rendered = TagLib.File.Create(originalTemporaryExport);
        if (rendered is not TagLib.Image.File image)
            throw new NotSupportedException("The export cannot preserve non-colour metadata with the selected working colour space.");
        var visited = new HashSet<TagLib.IFD.IFDStructure>(ReferenceEqualityComparer.Instance);
        foreach (var tag in image.ImageTag.AllTags)
        {
            if (tag is TagLib.IFD.IFDTag exif) RemoveSourceColorEntries(exif.Structure, visited, 0);
            if (tag is TagLib.Xmp.XmpTag xmp) RemoveSourceColorNodes(xmp.NodeTree, 0);
        }
        rendered.Save();
    }
    private static void RemoveSourceColorEntries(TagLib.IFD.IFDStructure structure,
        HashSet<TagLib.IFD.IFDStructure> visited, int depth)
    {
        if (!visited.Add(structure)) return;
        if (depth > 32 || visited.Count > 256) throw new InvalidDataException("Colour metadata nesting exceeds the supported export limit.");
        for (var index = 0; index < structure.Directories.Length; index++)
        {
            // Standard TIFF/EXIF colour interpretation entries only; unrelated
            // fields and their original dictionary keys remain unchanged.
            foreach (ushort tag in new ushort[] { 301, 318, 319, 34675, 40961, 42240 }) structure.RemoveTag(index, tag);
            foreach (var pair in structure.Directories[index].ToArray())
            {
                if (pair.Value is TagLib.IFD.Entries.SubIFDEntry child)
                {
                    if (pair.Key == 40965) // EXIF interoperability IFD: remove source colour-space identifier/version.
                        for (var directory = 0; directory < child.Structure.Directories.Length; directory++)
                        { child.Structure.RemoveTag(directory, (ushort)1); child.Structure.RemoveTag(directory, (ushort)2); }
                    RemoveSourceColorEntries(child.Structure, visited, depth + 1);
                }
                else if (pair.Value is TagLib.IFD.Entries.SubIFDArrayEntry children)
                    foreach (var childStructure in children.Entries) RemoveSourceColorEntries(childStructure, visited, depth + 1);
            }
        }
    }
    private static void RemoveSourceColorNodes(TagLib.Xmp.XmpNode node, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Colour metadata nesting exceeds the supported export limit.");
        foreach (var child in node.Children.ToArray())
        {
            var staleColor = child.Namespace switch
            {
                "http://ns.adobe.com/exif/1.0/" => child.Name is "ColorSpace" or "Gamma" or "InteroperabilityIndex" or "InteroperabilityVersion",
                "http://ns.adobe.com/tiff/1.0/" => child.Name is "WhitePoint" or "PrimaryChromaticities" or "TransferFunction" or "ICCProfile",
                "http://ns.adobe.com/photoshop/1.0/" => child.Name == "ICCProfile",
                _ => false
            };
            if (staleColor) node.RemoveChild(child); else RemoveSourceColorNodes(child, depth + 1);
        }
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
            // Compose canonical objects in original source coordinates first.
            // The SAME existing non-destructive raster edit graph then crops,
            // rotates, flips or resizes the complete hybrid result exactly once.
            if (document.CompositionState is not null)
            {
                var graph = PictureCompositionAdapter.Read(document);
                var sourceObject = PictureCompositionAdapter.SourceObject(graph);
                current = SharedVisualCompositionRenderer.Render(graph, graph.Pages[0].PageId, source.PixelSize,
                    new Dictionary<Guid, Bitmap> { [sourceObject.ObjectId] = source }, out _);
            }
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

    private static Bitmap RenderOperation(Bitmap source, PictureOperation operation)
    {
        if (operation is CropOperation originalCrop)
        {
            ValidateOriginalPixelCrop(source, originalCrop);
            // Exact raster cropping is a rectangle copy, not a drawing or
            // alpha-format conversion. Retain the actual native source format.
            if (source.Format is { } format && source.AlphaFormat is { } alpha && format.BitsPerPixel % 8 == 0)
                return RenderOriginalPixelCrop(source, originalCrop, format, alpha);
        }
        if (operation is ColorAdjustmentOperation color)
        {
            if (color.WorkingSpace != PictureColorWorkingSpace.Srgb8 || color.Settings is null)
                throw new InvalidDataException("The colour operation has no supported working-space choice.");
            return SharedRasterColorRenderer.Render(source, color.Settings);
        }
        if (operation is BlurOperation blur)
            return SharedRasterBlurRenderer.Render(source, blur.Settings);
        if (operation is PixelationOperation pixels)
            return SharedRasterPixelationRenderer.Render(source, pixels.Settings);
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
            case StraightenOperation angle:
                var dimensions = angle.OutputDimensions(width, height);
                targetWidth = dimensions.Width; targetHeight = dimensions.Height;
                var rotation = angle.Rotation();
                // SAME Avalonia renderer, physical pixel coordinates and a
                // center-preserving transform. Expanded output keeps all corners;
                // keep-canvas output deliberately clips only the rendered result.
                transform = new Matrix(rotation.Cos, rotation.Sin, -rotation.Sin, rotation.Cos,
                    targetWidth / 2d - width / 2d * rotation.Cos + height / 2d * rotation.Sin,
                    targetHeight / 2d - width / 2d * rotation.Sin - height / 2d * rotation.Cos);
                break;
            case FlipOperation flip:
                transform = flip.Horizontal ? new Matrix(-1, 0, 0, 1, width, 0) : new Matrix(1, 0, 0, -1, 0, height);
                break;
            case ResizeOperation resize when resize.Width is >= 1 and <= 32768 && resize.Height is >= 1 and <= 32768
                && (long)resize.Width * resize.Height <= 100_000_000:
                targetWidth = resize.Width;
                targetHeight = resize.Height;
                break;
            case CanvasResizeOperation canvas when canvas.Width is >= 1 and <= 32768 && canvas.Height is >= 1 and <= 32768
                && (long)canvas.Width * canvas.Height <= 100_000_000
                && canvas.OffsetX is >= -32768 and <= 32768 && canvas.OffsetY is >= -32768 and <= 32768:
                targetWidth = canvas.Width; targetHeight = canvas.Height;
                transform = Matrix.CreateTranslation(canvas.OffsetX, canvas.OffsetY);
                break;
            default:
                throw new InvalidDataException("The image operation is unsupported or outside the current raster.");
        }
        var result = new RenderTargetBitmap(new PixelSize(targetWidth, targetHeight), source.Dpi);
        try
        {
            using var context = result.CreateDrawingContext(clear: true);
            using var quality = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = operation is CanvasResizeOperation ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality });
            // Canvas placement is expressed in actual source pixels. The
            // native target scales DrawingContext units by its retained DPI,
            // so cancel that target scale for this pixel-preserving operation.
            // Explicit DrawImage source rectangles already use bitmap pixels.
            if ((operation is CanvasResizeOperation or StraightenOperation or CropOperation) && (!double.IsFinite(source.Dpi.X) || !double.IsFinite(source.Dpi.Y) || source.Dpi.X <= 0 || source.Dpi.Y <= 0))
                throw new InvalidDataException("The source has no valid pixel-to-canvas DPI mapping.");
            using var canvasPixels = context.PushTransform(operation is CanvasResizeOperation or StraightenOperation or CropOperation
                ? Matrix.CreateScale(96 / source.Dpi.X, 96 / source.Dpi.Y) : Matrix.Identity);
            using var transformed = context.PushTransform(transform);
            var destination = operation is RotateOperation or FlipOperation or CanvasResizeOperation or StraightenOperation ? new Rect(0, 0, width, height) : new Rect(0, 0, targetWidth, targetHeight);
            context.DrawImage(source, sourceBounds, destination);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
