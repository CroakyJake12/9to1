using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Bounded, read-only OS adapter. Never starts an inference engine or changes GPU settings.</summary>
public sealed class LocalModelHardwareProbe : ILocalModelHardwareProbe
{
    public async Task<LocalModelHardware> ReadAsync(CancellationToken cancellationToken)
    {
        long? total = null, available = null;
        var gpus = new List<LocalModelGpu>();
        string? limitation = null;
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var memory = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken).ConfigureAwait(false);
                total = ReadKiB(memory, "MemTotal:"); available = ReadKiB(memory, "MemAvailable:");
            }
            catch (IOException) { limitation = "RAM observation unavailable."; }
            catch (UnauthorizedAccessException) { limitation = "RAM observation unavailable."; }
            // Use the installed vendor observer, with a fixed argv and no shell. Its output is data only.
            if (File.Exists("/usr/bin/nvidia-smi"))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/nvidia-smi")
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
                process.StartInfo.ArgumentList.Add("--query-gpu=name,memory.total,memory.free");
                process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
                try
                {
                    if (process.Start())
                    {
                        var output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
                        var errors = ReadBoundedAsync(process.StandardError, deadline.Token);
                        try
                        {
                            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                            await errors.ConfigureAwait(false);
                            if (process.ExitCode == 0) gpus.AddRange(ParseNvidia(await output.ConfigureAwait(false)));
                        }
                        finally
                        {
                            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
                            try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
                            catch (OperationCanceledException) { }
                            catch (IOException) { }
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { limitation = "GPU observation timed out."; }
                catch (System.ComponentModel.Win32Exception) { limitation = "GPU observer unavailable."; }
                catch (IOException) { limitation = "GPU observation unavailable."; }
            }
            if (gpus.Count == 0)
            {
                try
                {
                    foreach (var card in Directory.EnumerateDirectories("/sys/class/drm", "card*"))
                    {
                        var name = Path.GetFileName(card);
                        if (!name[4..].All(char.IsDigit) || !Directory.Exists(Path.Combine(card, "device"))) continue;
                        var device = Path.Combine(card, "device");
                        var vendor = File.Exists(Path.Combine(device, "vendor")) ? (await File.ReadAllTextAsync(Path.Combine(device, "vendor"), cancellationToken)).Trim() : "Unknown";
                        var vram = await ReadNumberAsync(Path.Combine(device, "mem_info_vram_total"), cancellationToken);
                        var used = await ReadNumberAsync(Path.Combine(device, "mem_info_vram_used"), cancellationToken);
                        gpus.Add(new(vendor switch { "0x1002" => "AMD GPU", "0x10de" => "NVIDIA GPU", "0x8086" => "Intel GPU", _ => "GPU" },
                            vram, vram is > 0 && used is >= 0 && used <= vram ? vram - used : null));
                    }
                }
                catch (IOException) { limitation ??= "GPU memory observation unavailable."; }
                catch (UnauthorizedAccessException) { limitation ??= "GPU memory observation unavailable."; }
            }
        }
        else limitation = "Device memory detection is unavailable on this platform.";
        cancellationToken.ThrowIfCancellationRequested();
        return new(DateTimeOffset.UtcNow, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            total, available, gpus, limitation);
    }

    private static long? ReadKiB(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        var parts = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 3 } && parts[2] == "kB" && long.TryParse(parts[1], out var number) && number is >= 0 and < long.MaxValue / 1024
            ? number * 1024 : null;
    }
    private static async Task<long?> ReadNumberAsync(string path, CancellationToken token) => File.Exists(path) &&
        long.TryParse((await File.ReadAllTextAsync(path, token).ConfigureAwait(false)).Trim(), out var value) && value >= 0 ? value : null;

    public static IReadOnlyList<LocalModelGpu> ParseNvidia(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Take(16).Select(line => line.Split(',').Select(value => value.Trim()).ToArray())
        .Where(parts => parts.Length == 3 && parts[0].Length is > 0 and <= 200 && !parts[0].Any(char.IsControl))
        .Select(parts => new LocalModelGpu(parts[0], MiB(parts[1]), MiB(parts[2]))).ToArray();
    private static long? MiB(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
        number is >= 0 and < long.MaxValue / (1024 * 1024) ? number * 1024 * 1024 : null;

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        var output = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > 64 * 1024) throw new IOException("Device observer output exceeds the bounded observation size.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}
