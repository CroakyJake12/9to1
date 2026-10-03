using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NineToOne.Web.Write;

/// <summary>Exact byte staging for the owner's native codec; browser repository remains the durable owner.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserWritePackageBroker : IWriteBrowserPackageBroker
{
    private const long MaximumPackageBytes = 128L * 1024 * 1024; // Original codec's package limit.

    public async Task<WriteStagedPackage?> PickImportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var registration = cancellationToken.Register(CancelPick);
        cancellationToken.ThrowIfCancellationRequested();
        string reply;
        try { reply = await PickPackage(); }
        catch (JSException error) { throw new IOException("The native package chooser could not complete.", error); }
        cancellationToken.ThrowIfCancellationRequested();
        using var result = JsonDocument.Parse(reply);
        if (result.RootElement.ValueKind == JsonValueKind.Null) return null;
        var name = result.RootElement.GetProperty("name").GetString() ?? "document.9to1w";
        var bytes = Convert.FromBase64String(result.RootElement.GetProperty("base64").GetString()!);
        if (bytes.LongLength > MaximumPackageBytes) throw new InvalidDataException("This package exceeds the owning codec's size limit.");
        var staged = CreateStage(name);
        try { await File.WriteAllBytesAsync(staged.Path, bytes, cancellationToken); return staged; }
        catch { await staged.DisposeAsync(); throw; }
    }

    public Task<WriteStagedPackage> StageExportAsync(string suggestedFilename, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateStage(suggestedFilename));
    }

    public async Task PublishAsync(WriteStagedPackage package, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = new FileInfo(package.Path).Length;
        if (length > MaximumPackageBytes) throw new InvalidDataException("This package exceeds the owning codec's size limit.");
        var bytes = await File.ReadAllBytesAsync(package.Path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try { DownloadPackage(package.DownloadName, Convert.ToBase64String(bytes)); }
        catch (JSException error) { throw new IOException("The native package download could not start.", error); }
    }

    private static WriteStagedPackage CreateStage(string name)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nine-to-one-write-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var safeName = System.IO.Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "document.9to1w";
        if (!safeName.EndsWith(".9to1w", StringComparison.OrdinalIgnoreCase)) safeName += ".9to1w";
        return new(System.IO.Path.Combine(directory, "staged.9to1w"), safeName, () =>
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        });
    }

    [JSImport("pickPackage", "nineToOneWritePackages")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> PickPackage();
    [JSImport("cancelPick", "nineToOneWritePackages")]
    private static partial void CancelPick();
    [JSImport("downloadPackage", "nineToOneWritePackages")]
    private static partial void DownloadPackage(string filename, string base64);
}
