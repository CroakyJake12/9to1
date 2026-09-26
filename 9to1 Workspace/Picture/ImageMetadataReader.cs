using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Avalonia;

namespace HavenOS.Images;

public enum ImageMetadataAvailability
{
    Available,
    NoMetadata,
    UnsupportedByReader,
}

/// <summary>Metadata exposed by the image decoder and metadata reader without inventing unavailable values.</summary>
public sealed record ImageMetadataSnapshot(
    string Format,
    int PixelWidth,
    int PixelHeight,
    long FileSizeBytes,
    double? DpiX,
    double? DpiY,
    ImageMetadataAvailability MetadataAvailability,
    IReadOnlyDictionary<string, string> Fields,
    string? MetadataNotice)
{
    public static ImageMetadataSnapshot Read(string path, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bitmap);
        var pixelSize = bitmap.PixelSize;
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize), "Decoded image dimensions must be positive.");

        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        var format = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant();
        var fields = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var availability = ImageMetadataAvailability.UnsupportedByReader;
        string? notice = null;

        try
        {
            using var media = TagLib.File.Create(fullPath);
            if (media.Tag is TagLib.Image.CombinedImageTag imageTag)
            {
                AddPublicScalarProperties(imageTag, fields);
                foreach (var tag in imageTag.AllTags)
                    AddPublicScalarProperties(tag, fields);
                availability = fields.Count == 0 ? ImageMetadataAvailability.NoMetadata : ImageMetadataAvailability.Available;
            }
            else
            {
                availability = ImageMetadataAvailability.NoMetadata;
            }
        }
        catch (Exception exception) when (exception is TagLib.UnsupportedFormatException
            or TagLib.CorruptFileException or NotSupportedException or IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            notice = "This format's embedded metadata is not available through the installed metadata reader.";
        }

        return new ImageMetadataSnapshot(format, pixelSize.Width, pixelSize.Height, file.Length,
            IsValidDpi(bitmap.Dpi.X) ? bitmap.Dpi.X : null,
            IsValidDpi(bitmap.Dpi.Y) ? bitmap.Dpi.Y : null,
            availability, new ReadOnlyDictionary<string, string>(fields), notice);
    }

    private static bool IsValidDpi(double value) => double.IsFinite(value) && value > 0;

    private static void AddPublicScalarProperties(object source, IDictionary<string, string> target)
    {
        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 || property.Name is "TagTypes" or "AllTags")
                continue;

            object? value;
            try
            {
                value = property.GetValue(source);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            if (value is null || value is byte[] || value is Stream || value is System.Collections.IEnumerable and not string)
                continue;
            if (value is string text && string.IsNullOrWhiteSpace(text))
                continue;

            var rendered = Convert.ToString(value, CultureInfo.CurrentCulture);
            if (!string.IsNullOrWhiteSpace(rendered))
                target.TryAdd(property.Name, rendered);
        }
    }
}
