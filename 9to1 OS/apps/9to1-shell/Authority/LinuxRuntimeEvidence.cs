using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Checks actual kernel process evidence, never client-supplied environment or a generic apphost's filename.</summary>
public static class LinuxRuntimeEvidence
{
    public static async Task<bool> HasSafeEnvironmentAsync(int processId, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || processId <= 0) return false;
        var bytes = await ReadBoundedAsync($"/proc/{processId}/environ", 1024 * 1024, ct);
        if (bytes is null) return false;
        try
        {
            var diagnosticsDisabled = false;
            for (var offset = 0; offset < bytes.Length;)
            {
                var end = Array.IndexOf(bytes, (byte)0, offset); if (end < 0) return false;
                var separator = Array.IndexOf(bytes, (byte)'=', offset, end - offset);
                if (separator <= offset) return false;
                var name = Encoding.UTF8.GetString(bytes, offset, separator - offset);
                if (name.Equals("DOTNET_EnableDiagnostics", StringComparison.Ordinal))
                {
                    if (diagnosticsDisabled || end - separator != 2 || bytes[separator + 1] != (byte)'0') return false;
                    diagnosticsDisabled = true;
                }
                else if (name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("LD_", StringComparison.Ordinal) || name.Equals("MONO_ENV_OPTIONS", StringComparison.Ordinal) ||
                    name.Equals("MONO_PATH", StringComparison.Ordinal)) return false;
                offset = end + 1;
            }
            return diagnosticsDisabled;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal static async Task<bool> IsTrustedSelfContainedRuntimeAsync(int processId, InstallationReceipt receipt, CancellationToken ct)
    {
        if (!await HasSafeEnvironmentAsync(processId, ct)) return false;
        var executableName = Path.GetFileName(receipt.ExecutablePath);
        var required = new[] { executableName + ".runtimeconfig.json", executableName + ".deps.json", "libcoreclr.so", "libhostfxr.so", "libhostpolicy.so" };
        if (required.Any(name => !receipt.Files.Any(f => f.Path == name))) return false;
        var config = await LinuxRootOwnedFiles.ReadAsync(Path.Combine(receipt.InstallRoot, required[0]), 1024 * 1024, ct);
        if (config is null) return false;
        using var document = JsonDocument.Parse(config, new JsonDocumentOptions { MaxDepth = 24 });
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var options) || options.ValueKind != JsonValueKind.Object ||
            options.TryGetProperty("framework", out _) || options.TryGetProperty("frameworks", out _) || options.TryGetProperty("additionalProbingPaths", out _) ||
            !options.TryGetProperty("includedFrameworks", out var included) || included.ValueKind != JsonValueKind.Array || included.GetArrayLength() == 0) return false;
        var maps = await ReadBoundedAsync($"/proc/{processId}/maps", 4 * 1024 * 1024, ct); if (maps is null) return false;
        var declaredFiles = receipt.Files.Select(f => Path.Combine(receipt.InstallRoot, f.Path)).ToHashSet(StringComparer.Ordinal);
        var observedCore = false; var observedHost = false;
        foreach (var line in Encoding.UTF8.GetString(maps).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            var fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries); if (fields.Length < 5) return false;
            if (fields.Length != 6 || fields[5].StartsWith('[')) continue;
            var path = fields[5].Replace("\\040", " ").Replace("\\134", "\\");
            // CLR's dual anonymous JIT mapping is not a package file and cannot attest arbitrary application memory.
            if (path.StartsWith("/memfd:doublemapper", StringComparison.Ordinal)) continue;
            if (path.EndsWith(" (deleted)", StringComparison.Ordinal)) return false;
            var name = Path.GetFileName(path);
            if (name is "libcoreclr.so" or "libhostpolicy.so" or "libhostfxr.so")
            {
                if (path != Path.Combine(receipt.InstallRoot, name)) return false;
                observedCore |= name == "libcoreclr.so"; observedHost |= name == "libhostpolicy.so";
            }
            if (!fields[1].Contains('x') && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)) continue;
            if (declaredFiles.Contains(path)) continue;
            if (!(path.StartsWith("/usr/lib/", StringComparison.Ordinal) || path.StartsWith("/usr/lib64/", StringComparison.Ordinal)) ||
                !LinuxRootOwnedFiles.RegularFileImmutable(path)) return false;
        }
        return observedCore && observedHost;
    }
    private static async Task<byte[]?> ReadBoundedAsync(string path, int maximum, CancellationToken ct)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, true);
        using var result = new MemoryStream(); var buffer = new byte[65536];
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer, ct); if (count == 0) break;
                if (result.Length + count > maximum) return null;
                result.Write(buffer, 0, count);
            }
            return result.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); if (result.TryGetBuffer(out var retained)) CryptographicOperations.ZeroMemory(retained.AsSpan()); }
    }
}
