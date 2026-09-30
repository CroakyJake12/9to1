using System.Security.Cryptography;
using Avalonia.Platform.Storage;

namespace HavenOS.Images;

/// <summary>Owned bytes from an actual native picker selection. No path is retained or accepted.</summary>
public sealed class PicturePickedImage : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _bytes;
    private PicturePickedImage(string name, byte[] bytes, int width, int height, string mimeType)
    { Name = name; _bytes = bytes; Width = width; Height = height; MimeType = mimeType; ContentHash = Convert.ToHexString(SHA256.HashData(bytes)); SizeBytes = bytes.LongLength; }
    public string Name { get; }
    public string MimeType { get; }
    public int Width { get; }
    public int Height { get; }
    public string ContentHash { get; }
    public long SizeBytes { get; }

    public static async Task<PicturePickedImage?> PickAsync(IStorageProvider picker, PictureGlycinDecoder decoder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(picker); ArgumentNullException.ThrowIfNull(decoder);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Import picture", AllowMultiple = false }).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selected.Count == 0) return null;
            if (selected.Count != 1) throw new InvalidOperationException("Select exactly one picture.");
            var file = selected[0];
            var name = file.Name;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 220 || name.IndexOfAny(['/', '\\', '\0']) >= 0)
                throw new InvalidDataException("The selected file has an unsupported name.");
            await using var input = await file.OpenReadAsync().ConfigureAwait(false);
            using var captured = new MemoryStream();
            var buffer = new byte[65536];
            byte[]? bytes = null;
            try
            {
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    if (captured.Length + count > PictureGlycinDecoder.MaximumBufferBytes)
                        throw new InvalidDataException("The selected image exceeds the supported import size.");
                    captured.Write(buffer, 0, count);
                }
                bytes = captured.ToArray();
                var decoded = await Task.Run(() =>
                {
                    using var session = decoder.OpenFrames(bytes);
                    var mimeType = session.MimeType;
                    return (Frame: session.NextFrame(cancellationToken), MimeType: mimeType);
                }, cancellationToken).ConfigureAwait(false);
                var frame = decoded.Frame;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (frame.Width > 8192 || frame.Height > 8192 || (ulong)frame.Width * frame.Height * 4 > 64 * 1024 * 1024)
                        throw new NotSupportedException("The selected image exceeds the current shared raster display bounds.");
                    var result = new PicturePickedImage(name, bytes, checked((int)frame.Width), checked((int)frame.Height), decoded.MimeType);
                    bytes = null; // Ownership transfers; original encoded frames and metadata remain intact.
                    return result;
                }
                finally { PictureFilesSourceRenderer.ClearFrame(frame); }
            }
            finally
            {
                Array.Clear(buffer);
                if (bytes is not null) Array.Clear(bytes);
                if (captured.TryGetBuffer(out var storage)) Array.Clear(storage.Array!, storage.Offset, storage.Count);
            }
        }
        finally { foreach (var file in selected) file.Dispose(); }
    }

    internal byte[] CopyBytes()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_bytes is null, this);
            return _bytes!.ToArray();
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_bytes is null) return;
            Array.Clear(_bytes); _bytes = null;
        }
    }
}
