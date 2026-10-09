using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace HavenOS.Images;

internal sealed class ImageMetadataWindow
{
    private readonly ImageMetadataSnapshot _metadata;
    private readonly ICuiSceneReadiness _readiness;

    internal ImageMetadataWindow(ImageMetadataSnapshot metadata, ICuiSceneReadiness readiness)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    internal async Task OpenAsync(Window owner)
    {
        var model = new CuiViewModel();
        model.Set("FileFacts", $"Format: {_metadata.Format ?? "Unavailable"}\nDimensions: {_metadata.PixelWidth} × {_metadata.PixelHeight} pixels\n" +
            $"File size: {(_metadata.FileSizeBytes is long size ? FormatFileSize(size) : "Unavailable")}\nColour profile: Not exposed by the current decode backend\nResolution: {FormatResolution(_metadata.DpiX, _metadata.DpiY)}");
        model.Set("EmbeddedMetadata", _metadata.Fields.Count > 0
            ? string.Join("\n", _metadata.Fields.Select(field => SplitName(field.Key) + ": " + field.Value))
            : _metadata.MetadataAvailability == ImageMetadataAvailability.NoMetadata
                ? "No supported EXIF, IPTC or XMP fields were found." : "Embedded metadata could not be read for this image format.");
        model.Set("MetadataNotice", _metadata.MetadataNotice ?? "");
        using var dialog = new PictureCuiDialog("Image information", "PictureMetadata", _readiness, model);
        await dialog.OpenAsync(owner);
    }

    private static string FormatResolution(double? x, double? y)
    {
        if (x is null || y is null)
            return "Unavailable";
        return $"{x:0.#} × {y:0.#} dpi";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    private static string SplitName(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) && char.IsLower(value[index - 1]))
                result.Append(' ');
            result.Append(character);
        }
        return result.ToString();
    }
}
