using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CakeOS.Apps.Boards.App;

/// <summary>The shared desktop boundary for choosing a local asset. The path is a temporary
/// import locator, never a Files identity or permission grant for an external board source.</summary>
public static class BoardsFilePicker
{
    public static async Task<string?> PickAsync(Control? owner, string kind, Func<bool> ownerIsCurrent)
    {
        if (owner is null || !ownerIsCurrent()) return null;
        var top = TopLevel.GetTopLevel(owner) ?? throw new InvalidOperationException("The board window is unavailable.");
        if (!top.StorageProvider.CanOpen) throw new NotSupportedException("This window cannot open a file picker.");
        var retired = false;
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => retired = true;
        owner.DetachedFromVisualTree += Detached;
        try
        {
            var options = kind switch
            {
                "image" => new FilePickerOpenOptions
                {
                    Title = "Choose an image", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"],
                        MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp"]
                    }]
                },
                "board" => new FilePickerOpenOptions
                {
                    Title = "Open board", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Boards") { Patterns = ["*.9to1board"] }]
                },
                _ => new FilePickerOpenOptions
                { Title = "Choose a file", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.All] }
            };
            var files = await top.StorageProvider.OpenFilePickerAsync(options);
            try
            {
                if (retired || !ownerIsCurrent() || !top.IsVisible || !ReferenceEquals(TopLevel.GetTopLevel(owner), top))
                    return null;
                return files.Count == 0 ? null : files[0].TryGetLocalPath() ??
                    throw new NotSupportedException("This location does not provide a readable local file.");
            }
            finally { foreach (var file in files) file.Dispose(); }
        }
        finally { owner.DetachedFromVisualTree -= Detached; }
    }
}
