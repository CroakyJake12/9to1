using System.Text;
using Avalonia.Platform.Storage;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed record AssistantUcmlFile(string Name, string? Content, string? Error = null);

/// <summary>The native picker supplies files selected by the owner, never model-selected paths.</summary>
public interface IAssistantUcmlFilePicker
{
    Task<IReadOnlyList<AssistantUcmlFile>> PickImportAsync(CancellationToken token);
    Task<bool> SaveExportAsync(string suggestedName, Func<CancellationToken, Task<string>> readCurrent,
        CancellationToken token);
}

/// <summary>Avalonia's platform picker works on Linux, Windows and macOS. All selected
/// handles and reads are joined inside the caller-owned operation.</summary>
public sealed class AssistantUcmlFilePicker(Func<IStorageProvider?> currentStorage) : IAssistantUcmlFilePicker
{
    public const int MaximumFiles = 512;
    public const int MaximumFileCharacters = 65_536;
    private const int MaximumBatchCharacters = 16 * 1024 * 1024;
    private static readonly FilePickerFileType UcmlType = new("UCML context") { Patterns = ["*.ucml"] };

    public async Task<IReadOnlyList<AssistantUcmlFile>> PickImportAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var storage = currentStorage() ?? throw new InvalidOperationException("The native file picker is unavailable.");
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Review local UCML memories", AllowMultiple = true, FileTypeFilter = [UcmlType] });
        try
        {
            token.ThrowIfCancellationRequested();
            if (files.Count > MaximumFiles)
                return [new("Selection", null, $"Select at most {MaximumFiles} files in one review. No files were imported.")];
            var result = new List<AssistantUcmlFile>(); var total = 0;
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
                    var buffer = new char[MaximumFileCharacters + 1]; var length = 0;
                    while (length < buffer.Length)
                    {
                        var read = await reader.ReadAsync(buffer.AsMemory(length), token);
                        if (read == 0) break;
                        length += read;
                    }
                    if (length > MaximumFileCharacters || total + length > MaximumBatchCharacters)
                    { result.Add(new(file.Name, null, "File or batch size exceeds the local review limit.")); continue; }
                    total += length; result.Add(new(file.Name, new string(buffer, 0, length)));
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or DecoderFallbackException)
                { result.Add(new(file.Name, null, "Unable to read this local file: " + failure.Message)); }
            }
            return result.AsReadOnly();
        }
        finally { foreach (var file in files) file.Dispose(); }
    }

    public async Task<bool> SaveExportAsync(string suggestedName, Func<CancellationToken, Task<string>> readCurrent,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var storage = currentStorage() ?? throw new InvalidOperationException("The native save picker is unavailable.");
        using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Export private UCML memory locally", SuggestedFileName = suggestedName,
            DefaultExtension = "ucml", FileTypeChoices = [UcmlType], ShowOverwritePrompt = true });
        if (file is null) return false;
        token.ThrowIfCancellationRequested();
        var path = file.TryGetLocalPath() ?? throw new NotSupportedException("Choose a local file destination for private UCML export.");
        // Recheck canonical permission only after the actual destination is chosen.
        var content = await readCurrent(token);
        token.ThrowIfCancellationRequested();
        await WritePrivateLocalFileAsync(path, content, token);
        return true;
    }

    internal static async Task WritePrivateLocalFileAsync(string path, string content, CancellationToken token)
    {
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".ucml-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(content), token);
                await stream.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
