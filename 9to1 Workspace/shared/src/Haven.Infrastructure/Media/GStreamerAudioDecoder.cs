using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Haven.Core.Media;

namespace Haven.Infrastructure.Media;

public sealed record GStreamerAudioDecodeLimits(long MaximumDecodedBytes = 536870912,
    TimeSpan? MaximumExecutionTime = null)
{
    public TimeSpan ExecutionTime => MaximumExecutionTime ?? TimeSpan.FromMinutes(10);
    public void Validate()
    {
        if (MaximumDecodedBytes < 1024 || MaximumDecodedBytes > uint.MaxValue - 128
            || ExecutionTime <= TimeSpan.Zero || ExecutionTime > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(MaximumDecodedBytes));
    }
}

/// <summary>Actual donor decoding. Fixed process arguments preserve native rate/channels in float64 WAV;
/// Files retains original ownership/bytes and the caller retains its immutable read lease.</summary>
public sealed class GStreamerAudioDecoder(string executablePath, GStreamerAudioDecodeLimits? limits = null,
    string? discovererPath = null)
    : IMediaAudioDecoder
{
    public GStreamerAudioDecodeLimits Limits { get; } = limits ?? new();
    private const string Profile = "gstreamer/f64le/preserve-rate-channels/dither-none/v1";

    public async Task<MediaEngineResult<MediaAudioDecodedLease>> DecodeAsync(MediaAssetReadLease source,
        CancellationToken cancellationToken = default)
    {
        string? directory = null;
        try
        {
            ArgumentNullException.ThrowIfNull(source);
            Limits.Validate();
            var input = source.Source;
            input.Validate();
            if (!input.SourceUri.IsFile || string.IsNullOrWhiteSpace(input.SourceRevisionId))
                return Fail(MediaEngineErrorCode.UnsupportedSource, "Decode requires a pinned Files materialisation.", input.HostedItemId);
            var executable = Path.GetFullPath(executablePath);
            if (!File.Exists(executable)) return Fail(MediaEngineErrorCode.BackendUnavailable, "GStreamer decoder is unavailable.", input.HostedItemId);
            var executableHash = await HashAsync(executable, cancellationToken).ConfigureAwait(false);
            var version = await VersionAsync(executable, cancellationToken).ConfigureAwait(false);
            if (version is null) return Fail(MediaEngineErrorCode.BackendUnavailable, "GStreamer runtime version could not be observed.", input.HostedItemId);
            var sourceHash = await HashAsync(input.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false);
            var discoverer = Path.GetFullPath(discovererPath ?? Path.Combine(Path.GetDirectoryName(executable)!,
                OperatingSystem.IsWindows() ? "gst-discoverer-1.0.exe" : "gst-discoverer-1.0"));
            if (!File.Exists(discoverer))
                return Fail(MediaEngineErrorCode.BackendUnavailable, "GStreamer stream discovery is unavailable.", input.HostedItemId);
            if (!await HasSingleAudioStreamAsync(discoverer, input.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false))
                return Fail(MediaEngineErrorCode.UnsupportedSource, "Audio decoding requires exactly one discovered audio stream and no video or subtitle streams.", input.HostedItemId);
            directory = Directory.CreateTempSubdirectory("9to1-audio-decode-").FullName;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var output = Path.Combine(directory, "decoded.wav");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true };
            // Each pipeline element/property is a separate argument; no shell, model-authored pipeline or network source.
            foreach (var argument in new[] { "-q", "filesrc", "location=" + QuoteProperty(input.SourceUri.LocalPath), "!",
                "decodebin", "!", "audioconvert", "dithering=0", "noise-shaping=0", "!", "audio/x-raw,format=F64LE",
                "!", "wavenc", "!", "filesink", "location=" + QuoteProperty(output) }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Decoder process did not start.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Limits.ExecutionTime);
            var stdout = DrainAsync(process.StandardOutput, deadline.Token);
            var stderr = DrainAsync(process.StandardError, deadline.Token);
            var exceeded = false;
            try
            {
                while (!process.HasExited)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (File.Exists(output) && new FileInfo(output).Length > Limits.MaximumDecodedBytes)
                    { exceeded = true; break; }
                    await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                }
                if (exceeded) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                throw;
            }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (exceeded || (File.Exists(output) && new FileInfo(output).Length > Limits.MaximumDecodedBytes))
                return Fail(MediaEngineErrorCode.ExportFailed, "Decoded audio exceeds the configured operation byte limit.", input.HostedItemId);
            if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length < 44)
                return Fail(MediaEngineErrorCode.CodecUnsupported, "GStreamer could not decode this source with the installed plugins.", input.HostedItemId);
            if (await HashAsync(input.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false) != sourceHash
                || await HashAsync(executable, cancellationToken).ConfigureAwait(false) != executableHash)
                return Fail(MediaEngineErrorCode.RevisionConflict, "Source or decoder changed during decoding.", input.HostedItemId);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var decodedHash = await HashAsync(output, cancellationToken).ConfigureAwait(false);
            var ownedDirectory = directory;
            var result = new MediaAudioDecodedLease(new(input.AssetId, input.HostedItemId, input.SourceRevisionId,
                sourceHash, decodedHash, Profile, new("GStreamer", version, executableHash), output),
                () => { Cleanup(ownedDirectory); return ValueTask.CompletedTask; });
            directory = null;
            return MediaEngineResult<MediaAudioDecodedLease>.Success(result);
        }
        catch (OperationCanceledException)
        { return Fail(MediaEngineErrorCode.OperationCancelled, "Audio decoding was cancelled or exceeded its configured execution time."); }
        catch (UnauthorizedAccessException) { return Fail(MediaEngineErrorCode.PermissionDenied, "Audio materialisation is not accessible."); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception)
        { return Fail(MediaEngineErrorCode.SourceUnavailable, "Audio decoder or materialisation is unavailable."); }
        finally { if (directory is not null) Cleanup(directory); }
    }

    private static string QuoteProperty(string path) => "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    private static async Task<bool> HasSingleAudioStreamAsync(string executable, string path, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        start.Environment["LC_ALL"] = "C";
        start.ArgumentList.Add("--timeout=5");
        start.ArgumentList.Add(new Uri(path).AbsoluteUri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = Process.Start(start) ?? throw new IOException("Stream discovery did not start.");
        var output = DrainAsync(process.StandardOutput, deadline.Token, 65536);
        var errors = DrainAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            // Source URI is printed before Properties; only inspect the final native properties block.
            var properties = text.LastIndexOf("\nProperties:\n", StringComparison.Ordinal);
            if (process.ExitCode != 0 || properties < 0 || text.Length >= 65536) return false;
            var block = text[(properties + 13)..];
            var audio = Regex.Matches(block, @"(?m)^\s+audio #[0-9]+:", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var streams = Regex.Matches(block, @"(?m)^\s+([a-zA-Z][a-zA-Z -]*) #[0-9]+:",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return audio.Count == 1 && streams.All(stream => stream.Groups[1].Value is "audio" or "container");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
    private static async Task<string?> VersionAsync(string executable, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new IOException("Decoder version probe did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var output = DrainAsync(process.StandardOutput, deadline.Token);
        var errors = DrainAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            throw;
        }
        var text = await output.ConfigureAwait(false);
        await errors.ConfigureAwait(false);
        var match = Regex.Match(text, @"\bversion\s+([0-9]+\.[0-9]+\.[0-9]+)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return process.ExitCode == 0 && match.Success ? match.Groups[1].Value : null;
    }
    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token = default, int maximumCharacters = 4096)
    {
        var buffer = new char[1024];
        var collected = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            if (collected.Length < maximumCharacters) collected.Append(buffer, 0, Math.Min(read, maximumCharacters - collected.Length));
        return collected.ToString();
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }
    private static void Cleanup(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static MediaEngineResult<MediaAudioDecodedLease> Fail(MediaEngineErrorCode code, string message, Guid? fileID = null) =>
        MediaEngineResult<MediaAudioDecodedLease>.Failure(new(code, message, "decode-audio", fileID?.ToString("D"), true, false));
}
