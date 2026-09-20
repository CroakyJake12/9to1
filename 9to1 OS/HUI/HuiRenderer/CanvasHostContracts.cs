using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CakeOS.Hui.Renderer;

/// <summary>Platform file dialogs supplied by each host (Avalonia StorageProvider).</summary>
public interface ICanvasFileDialogs
{
    Task<string?> PickOpenRnoteAsync(string directory, string title);
    Task<string?> PickSaveRnoteAsync(string directory, string suggestedName, string title);
}

/// <summary>StorageProvider-backed dialogs. Resolves the TopLevel lazily so the
/// host can construct application roots before the window is shown.</summary>
public sealed class TopLevelFileDialogs : ICanvasFileDialogs
{
    private readonly Visual _anchor;

    public TopLevelFileDialogs(Visual anchor) => _anchor = anchor;

    public async Task<string?> PickOpenRnoteAsync(string directory, string title)
    {
        var provider = TopLevel.GetTopLevel(_anchor)?.StorageProvider;
        if (provider is null)
            return null;
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Rnote document") { Patterns = ["*.rnote"] }],
        };
        await WithStartLocation(provider, options, directory).ConfigureAwait(true);
        var files = await provider.OpenFilePickerAsync(options).ConfigureAwait(true);
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveRnoteAsync(string directory, string suggestedName, string title)
    {
        var provider = TopLevel.GetTopLevel(_anchor)?.StorageProvider;
        if (provider is null)
            return null;
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "rnote",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("Rnote document") { Patterns = ["*.rnote"] }],
        };
        await WithStartLocation(provider, options, directory).ConfigureAwait(true);
        var file = await provider.SaveFilePickerAsync(options).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    private static async Task WithStartLocation(
        IStorageProvider provider, FilePickerOpenOptions options, string directory)
    {
        try
        {
            var folder = await provider.TryGetFolderFromPathAsync(directory).ConfigureAwait(true);
            if (folder is not null)
                options.SuggestedStartLocation = folder;
        }
        catch { /* best effort */ }
    }

    private static async Task WithStartLocation(
        IStorageProvider provider, FilePickerSaveOptions options, string directory)
    {
        try
        {
            var folder = await provider.TryGetFolderFromPathAsync(directory).ConfigureAwait(true);
            if (folder is not null)
                options.SuggestedStartLocation = folder;
        }
        catch { /* best effort */ }
    }
}
