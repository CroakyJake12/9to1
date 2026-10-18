using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CakeOS.Cui.Build.Tasks;

public interface ICuiResourceLog
{
    void LogWarning(string message);

    void LogError(string message);

    void LogMessage(MessageImportance importance, string message);
}

public sealed class MsBuildResourceLog : ICuiResourceLog
{
    private readonly TaskLoggingHelper _log;

    public MsBuildResourceLog(TaskLoggingHelper log) => _log = log;

    public bool HasErrors { get; private set; }

    public void LogWarning(string message) => _log.LogWarning(message);

    public void LogError(string message)
    {
        HasErrors = true;
        _log.LogError(message);
    }

    public void LogMessage(MessageImportance importance, string message) => _log.LogMessage(importance, message);
}

// Plain immutable type rather than a record: netstandard2.0 has no IsExternalInit.
public sealed class CuiResourceEntry
{
    public CuiResourceEntry(string logicalName, byte[] data)
    {
        LogicalName = logicalName;
        Data = data;
    }

    public string LogicalName { get; }

    public byte[] Data { get; }
}

/// <summary>
/// Collects and writes the CUI resource bundle. Original resource bytes are embedded
/// unchanged. This byte-packaging API does not validate CUI; the build graph must
/// run the canonical compiler before bundling application markup. A resource is root-relative
/// only when it crosses a real normalized directory boundary, so a similarly prefixed
/// sibling such as C:/repo-other is never treated as inside C:/repo. A logical name is
/// the bundle identity, so a second resource mapping to an already claimed dot-separated
/// logical name is reported as an error instead of silently shadowing the first one.
/// Duplicate identities are errors; outside-root paths retain an explicit warning.
/// Write stages the complete bundle in the destination directory, flushes and closes it,
/// then promotes it over the destination, so a serialization, I/O or replacement failure
/// never truncates a previously valid bundle; the failed staging file is removed and the
/// original output is retained.
/// </summary>
public static class CuiResourceBundle
{
    public static List<CuiResourceEntry> Collect(string root, IEnumerable<string> fullPaths, ICuiResourceLog log)
    {
        var entries = new List<CuiResourceEntry>();
        var normalizedRoot = NormalizeRoot(root);
        var firstSourceByLogicalName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var fullPath in fullPaths)
        {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                log.LogMessage(MessageImportance.Low, $"Skipping missing CUI resource: {fullPath}");
                continue;
            }

            if (!TryGetRelativePath(normalizedRoot, fullPath, out var relativePath))
            {
                relativePath = Path.GetFullPath(fullPath);
                log.LogWarning($"CUI resource '{relativePath}' is outside the CUI resource root '{normalizedRoot}' and is embedded under its full path.");
            }

            var logicalName = relativePath.Replace('\\', '.').Replace('/', '.');
            var data = File.ReadAllBytes(fullPath);

            if (firstSourceByLogicalName.TryGetValue(logicalName, out var firstSource))
            {
                log.LogError(
                    $"Duplicate CUI resource logical name '{logicalName}': '{firstSource}' and '{relativePath}' " +
                    "map to the same identity. CUI resources must have unique logical names.");
                continue;
            }

            firstSourceByLogicalName[logicalName] = relativePath;

            entries.Add(new CuiResourceEntry(logicalName, data));
            log.LogMessage(MessageImportance.Low,
                $"CUI resource: {relativePath} -> {logicalName} ({data.Length} bytes)");
        }

        return entries;
    }

    public static void Write(string output, IReadOnlyList<CuiResourceEntry> entries, ICuiResourceLog log)
    {
        var dir = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var staging = Path.Combine(
            string.IsNullOrEmpty(dir) ? string.Empty : dir,
            Path.GetFileName(output) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(entries.Count);
                foreach (var entry in entries)
                {
                    writer.Write(entry.LogicalName);
                    writer.Write(entry.Data.Length);
                    writer.Write(entry.Data);
                }

                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            Promote(staging, output);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        log.LogMessage(MessageImportance.High,
            $"Generated CUI resource bundle: {entries.Count} resources -> {output}");
    }

    private static void Promote(string staging, string output)
    {
        if (!File.Exists(output))
        {
            File.Move(staging, output);
            return;
        }

        try
        {
            File.Replace(staging, output, null);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            // netstandard2.0 has no atomic overwrite, so the original is renamed aside and
            // restored if promotion fails.
        }

        var retained = staging + ".retained";
        File.Move(output, retained);

        try
        {
            File.Move(staging, output);
        }
        catch
        {
            TryMove(retained, output);
            throw;
        }

        TryDelete(retained);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static void TryMove(string source, string destination)
    {
        try
        {
            if (File.Exists(source) && !File.Exists(destination)) File.Move(source, destination);
        }
        catch (IOException)
        {
        }
    }

    public static string NormalizeRoot(string root)
    {
        if (string.IsNullOrEmpty(root)) return string.Empty;

        var full = Path.GetFullPath(root);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool TryGetRelativePath(string root, string fullPath, out string relativePath)
    {
        relativePath = string.Empty;

        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(fullPath)) return false;

        var normalizedRoot = NormalizeRoot(root);
        var normalizedPath = Path.GetFullPath(fullPath);
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!normalizedPath.StartsWith(normalizedRoot, comparison)) return false;

        var remainder = normalizedPath.Substring(normalizedRoot.Length);
        if (remainder.Length == 0) return false;

        if (remainder[0] != Path.DirectorySeparatorChar && remainder[0] != Path.AltDirectorySeparatorChar) return false;

        relativePath = remainder.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relativePath.Length > 0;
    }

}
