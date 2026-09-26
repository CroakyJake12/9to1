using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
namespace HavenOS.Images;

public enum ImageMetadataAvailability
{
    Available,
    NoMetadata,
    UnsupportedByReader,
}

/// <summary>Metadata exposed by the image decoder and metadata reader without inventing unavailable values.</summary>
public sealed record ImageMetadataSnapshot(
    string? Format,
    int PixelWidth,
    int PixelHeight,
    long? FileSizeBytes,
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
        string? format;
        try
        {
            format = DetectFormat(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            format = null;
        }
        long? fileSizeBytes = null;
        string? notice = null;
        try
        {
            fileSizeBytes = new FileInfo(fullPath).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            notice = "File size is unavailable.";
        }

        var fields = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var availability = ImageMetadataAvailability.UnsupportedByReader;

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
        catch (Exception)
        {
            notice = string.IsNullOrEmpty(notice)
                ? "This format's embedded metadata is not available through the installed metadata reader."
                : $"{notice} This format's embedded metadata is not available through the installed metadata reader.";
        }

        return new ImageMetadataSnapshot(format, pixelSize.Width, pixelSize.Height, fileSizeBytes,
            null,
            null,
            availability, new ReadOnlyDictionary<string, string>(fields), notice);
    }

    private static string? DetectFormat(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var count = stream.Read(header);
        var bytes = header[..count];
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "PNG";
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) return "JPEG";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "GIF";
        if (bytes.StartsWith("BM"u8)) return "BMP";
        if (bytes.StartsWith("II*\0"u8) || bytes.StartsWith("MM\0*"u8)) return "TIFF";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "WebP";
        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(bytes[8..12]);
            if (brand is "avif" or "avis") return "AVIF";
            if (brand.StartsWith("hei", StringComparison.Ordinal) || brand.StartsWith("mif", StringComparison.Ordinal)) return "HEIF/HEIC";
        }
        if (bytes.StartsWith("<svg"u8) || bytes.StartsWith("<?xml"u8)) return "SVG/XML";
        return null;
    }

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
            catch (Exception)
            {
                continue;
            }

            if (value is null || value is byte[] || value is Stream || value is System.Collections.IEnumerable and not string)
                continue;
            if (value is string text && string.IsNullOrWhiteSpace(text))
                continue;
            if (value.GetType().IsValueType && value.Equals(Activator.CreateInstance(value.GetType())))
                continue;

            var rendered = Convert.ToString(value, CultureInfo.CurrentCulture);
            if (!string.IsNullOrWhiteSpace(rendered))
                target.TryAdd(property.Name, rendered);
        }
    }
}
