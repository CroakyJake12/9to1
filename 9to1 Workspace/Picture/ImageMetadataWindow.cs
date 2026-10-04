using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace HavenOS.Images;

internal sealed class ImageMetadataWindow : Window
{
    public ImageMetadataWindow(ImageMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Title = "Image information";
        Width = 620;
        Height = 660;
        MinWidth = 420;
        MinHeight = 420;
        CanResize = true;

        var rows = new StackPanel { Spacing = 10 };
        AddSection(rows, "File");
        AddRow(rows, "Format", metadata.Format ?? "Unavailable");
        AddRow(rows, "Dimensions", $"{metadata.PixelWidth} × {metadata.PixelHeight} pixels");
        AddRow(rows, "File size", metadata.FileSizeBytes is long size ? FormatFileSize(size) : "Unavailable");
        AddRow(rows, "Colour profile", "Not exposed by the current decode backend");
        AddRow(rows, "Resolution", FormatResolution(metadata.DpiX, metadata.DpiY));

        AddSection(rows, "Embedded metadata");
        if (metadata.Fields.Count == 0)
        {
            var message = metadata.MetadataAvailability switch
            {
                ImageMetadataAvailability.NoMetadata => "No supported EXIF, IPTC or XMP fields were found.",
                _ => "Embedded metadata could not be read for this image format.",
            };
            rows.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.78 });
        }
        else
        {
            foreach (var (name, value) in metadata.Fields)
                AddRow(rows, SplitName(name), value);
        }

        if (!string.IsNullOrWhiteSpace(metadata.MetadataNotice))
        {
            rows.Children.Add(new TextBlock
            {
                Text = metadata.MetadataNotice,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Margin = new Avalonia.Thickness(0, 8, 0, 0),
            });
        }

        var closeButton = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
        closeButton.Click += (_, _) => Close();
        AutomationProperties.SetName(closeButton, "Close image information");
        var content = new DockPanel { Margin = new Avalonia.Thickness(24), LastChildFill = true };
        DockPanel.SetDock(closeButton, Dock.Bottom);
        content.Children.Add(closeButton);
        content.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = content;
    }

    private static void AddSection(Panel panel, string title)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 14, 0, 0),
        });
    }

    private static void AddRow(Panel panel, string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 16 };
        var name = new TextBlock { Text = label, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap };
        var content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Opacity = 0.86 };
        AutomationProperties.SetName(name, $"Metadata field {label}");
        AutomationProperties.SetName(content, $"{label}: {value}");
        Grid.SetColumn(content, 1);
        grid.Children.Add(name);
        grid.Children.Add(content);
        panel.Children.Add(grid);
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
