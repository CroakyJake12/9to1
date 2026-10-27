using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Browse;

// Explicit private development runtime, independent of the pinned ARM bundle.
// A matching archive manifest proves retained bytes, not native readiness.
internal static class ExplicitChromiumRuntime
{
    internal const string EnvironmentKey = "HAVEN_CHROMIUM_RUNTIME_MANIFEST";
    internal static string? FindConfigured() => Find(Environment.GetEnvironmentVariable(EnvironmentKey));
    internal static string? Find(string? manifestPath)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            string.IsNullOrWhiteSpace(manifestPath) || !Path.IsPathFullyQualified(manifestPath)) return null;
        try
        {
            var manifest = new FileInfo(manifestPath);
            if (!manifest.Exists || manifest.LinkTarget is not null || manifest.Length > 4 * 1024 * 1024) return null;
            var root = manifest.Directory!;
            for (DirectoryInfo? parent = root; parent is not null; parent = parent.Parent)
                if (parent.LinkTarget is not null) return null;
            using var json = JsonDocument.Parse(File.ReadAllBytes(manifest.FullName));
            var value = json.RootElement;
            if (value.GetProperty("schema").GetString() != "9to1.chromium-explicit-runtime/v1" ||
                value.GetProperty("platform").GetString() != "linux-x64" ||
                value.GetProperty("binary").GetString() != "chrome-linux64/chrome") return null;
            var files = value.GetProperty("files");
            if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is 0 or > 2048) return null;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in files.EnumerateArray())
            {
                var relative = item.GetProperty("path").GetString();
                if (relative is null || !relative.StartsWith("chrome-linux64/", StringComparison.Ordinal) ||
                    relative.Contains('\\') || relative.Split('/').Any(part => part is "" or "." or "..") || !declared.Add(relative)) return null;
                var full = Path.Combine(root.FullName, relative);
                var file = new FileInfo(full);
                if (!file.Exists || file.LinkTarget is not null || file.Length != item.GetProperty("bytes").GetInt64()) return null;
                for (var parent = file.Directory; parent is not null && parent.FullName != root.FullName; parent = parent.Parent)
                    if (parent.LinkTarget is not null) return null;
                using var stream = file.OpenRead();
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) return null;
            }
            var binary = Path.Combine(root.FullName, "chrome-linux64", "chrome");
            if (!declared.Contains("chrome-linux64/chrome")) return null;
            foreach (var entry in new DirectoryInfo(Path.Combine(root.FullName, "chrome-linux64")).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if (entry.LinkTarget is not null) return null;
                if (entry is FileInfo && !declared.Contains(Path.GetRelativePath(root.FullName, entry.FullName).Replace('\\', '/'))) return null;
            }
            using var executable = File.OpenRead(binary);
            Span<byte> elf = stackalloc byte[20]; executable.ReadExactly(elf);
            if (elf[0] != 0x7f || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F' || elf[4] != 2 || elf[5] != 1 || elf[18] != 62 || elf[19] != 0) return null;
            var mode = File.GetUnixFileMode(binary);
            return (mode & UnixFileMode.UserExecute) != 0 ? binary : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }
}
