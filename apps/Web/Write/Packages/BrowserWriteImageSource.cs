using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NineToOne.Web.Write;

/// <summary>
/// Stages an image the writer picked in the browser so the owning attachment
/// store imports the exact picked bytes. The staged copy is disposable and is
/// never a canonical artifact identity.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserWriteImageSource : IWriteBrowserImageSource
{
    private const long MaximumImageBytes = 32L * 1024 * 1024; // Donor's own image ceiling is far above a document image.

    public async Task<WritePickedImage?> PickImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var registration = cancellationToken.Register(CancelPick);
        cancellationToken.ThrowIfCancellationRequested();
        string reply;
        try { reply = await PickImage(); }
        catch (JSException error) { throw new IOException("The native image chooser could not complete.", error); }
        cancellationToken.ThrowIfCancellationRequested();
        using var result = JsonDocument.Parse(reply);
        if (result.RootElement.ValueKind == JsonValueKind.Null) return null;
        var name = result.RootElement.GetProperty("name").GetString() ?? "image.png";
        var mediaType = result.RootElement.GetProperty("mediaType").GetString() ?? "image/png";
        var bytes = Convert.FromBase64String(result.RootElement.GetProperty("base64").GetString() ?? string.Empty);
        if (bytes.LongLength > MaximumImageBytes) throw new InvalidDataException("This image is too large to insert.");
        var staged = CreateStage(name);
        try
        {
            await File.WriteAllBytesAsync(staged.Path, bytes, cancellationToken);
            return new WritePickedImage(name, mediaType, staged);
        }
        catch { await staged.DisposeAsync(); throw; }
    }

    private static WriteStagedPackage CreateStage(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nine-to-one-write-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var safeName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "image.png";
        return new(Path.Combine(directory, safeName), safeName, () =>
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        });
    }

    [JSImport("pickImage", "nineToOneWritePackages")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> PickImage();

    [JSImport("cancelPick", "nineToOneWritePackages")]
    private static partial void CancelPick();
}
