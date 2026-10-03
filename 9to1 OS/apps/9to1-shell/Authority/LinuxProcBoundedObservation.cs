using System.Text;
using System.Globalization;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Bounded actual procfs observation. No parsed public bytes confer installed or
/// session authority; the supervisor must retain its actual original child and lease identity.
/// Only canonical numeric process status/stat/fdinfo paths are admitted; proc aliases are refused.</summary>
public static class LinuxProcBoundedObservation
{
    public static async ValueTask<string?> ReadAsync(string absoluteProcPath, int maximumBytes, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || maximumBytes is < 1 or > 65536 ||
            !absoluteProcPath.StartsWith("/proc/", StringComparison.Ordinal) ||
            Path.GetFullPath(absoluteProcPath) != absoluteProcPath) return null;
        var parts = absoluteProcPath.Split('/');
        if (parts.Length is not (4 or 5) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0 ||
            parts[2] != pid.ToString(CultureInfo.InvariantCulture)) return null;
        if (parts.Length == 4)
        { if (parts[3] is not ("status" or "stat")) return null; }
        else if (parts[3] != "fdinfo" || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var fd) ||
            fd < 0 || parts[4] != fd.ToString(CultureInfo.InvariantCulture)) return null;
        try
        {
            await using var input = new FileStream(absoluteProcPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, true);
            // Procfs reports zero st_size even when populated, so enforce ingress by reading
            // at most bound+1 rather than trusting Length or allocating from kernel metadata.
            var bytes = new byte[maximumBytes + 1]; var used = 0;
            while (used < bytes.Length)
            {
                var count = await input.ReadAsync(bytes.AsMemory(used), ct).ConfigureAwait(false);
                if (count == 0) return new UTF8Encoding(false, true).GetString(bytes, 0, used);
                used += count;
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { return null; }
    }
}
