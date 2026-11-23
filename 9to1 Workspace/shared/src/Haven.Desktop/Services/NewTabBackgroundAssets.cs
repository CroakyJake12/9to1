using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;

namespace Haven.Desktop.Services;

internal sealed class NewTabBackgroundImageCandidate(byte[] encoded, Bitmap bitmap) : IDisposable
{
    internal ReadOnlyMemory<byte> Encoded { get; } = encoded;
    internal Bitmap Bitmap { get; } = bitmap;
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>Imports a user-picked background into this profile. Preferences keep only its content digest.</summary>
internal sealed class NewTabBackgroundAssets
{
    private const int MaxFileBytes = 20 * 1024 * 1024;
    private const long MaxPixels = 32_000_000;
    private readonly string _directory;

    internal NewTabBackgroundAssets(string dataDirectory, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profileKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
        _directory = Path.Combine(dataDirectory, "new-tab-backgrounds", profileKey);
    }

    internal async Task<NewTabBackgroundImageCandidate?> PickAsync(TopLevel owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!owner.StorageProvider.CanOpen)
            throw new InvalidOperationException("A local image picker is unavailable in this window.");
        var selection = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a New Tab background",
            FileTypeFilter = [new("Background images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }]
        });
        using var file = selection.FirstOrDefault();
        token.ThrowIfCancellationRequested();
        if (file is null) return null;
        await using var stream = await file.OpenReadAsync();
        return await ReadCandidateAsync(stream, token);
    }

    internal async Task<NewTabBackgroundImageCandidate> ReadCandidateAsync(Stream source, CancellationToken token)
    {
        var bytes = await ReadImageBytesAsync(source, token).ConfigureAwait(false);
        var bitmap = await Task.Run(() => Decode(bytes), token).ConfigureAwait(false);
        return new(bytes, bitmap);
    }

    internal async Task<string> AdoptAsync(NewTabBackgroundImageCandidate candidate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Convert.ToHexStringLower(SHA256.HashData(candidate.Encoded.Span));
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
                await output.WriteAsync(candidate.Encoded, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Concurrent imports of the same digest have identical content. A re-import can also repair a lost asset.
            File.Move(temporary, Path.Combine(_directory, id), overwrite: true);
            return id;
        }
        finally { File.Delete(temporary); }
    }

    internal async Task<Bitmap?> LoadAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("The saved background reference is invalid.");
        byte[] bytes;
        try
        {
            await using var source = new FileStream(Path.Combine(_directory, id.ToLowerInvariant()), FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous);
            bytes = await ReadImageBytesAsync(source, token).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The stored background image has changed. Choose the image again.");
        return await Task.Run(() => Decode(bytes), token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadImageBytesAsync(Stream source, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > MaxFileBytes)
                throw new InvalidDataException("Choose an image smaller than 20 MiB.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp))
            throw new InvalidDataException("Choose a PNG, JPEG or WebP image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("Choose an image up to 32 megapixels.");
        using var pixels = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.GetPixels(pixels.Info, pixels.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The image could not be decoded completely.");
        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixels.GetPixels(),
            new PixelSize(info.Width, info.Height), new Vector(96, 96), pixels.RowBytes);
    }
}
