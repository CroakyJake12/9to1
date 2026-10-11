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

    void LogMessage(MessageImportance importance, string message);
}

public sealed class MsBuildResourceLog : ICuiResourceLog
{
    private readonly TaskLoggingHelper _log;

    public MsBuildResourceLog(TaskLoggingHelper log) => _log = log;

    public void LogWarning(string message) => _log.LogWarning(message);

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
/// unchanged; the canonical lexical form is only inspected for validation, so a
/// malformed resource still warns and is still embedded. A resource is root-relative
/// only when it crosses a real normalized directory boundary, so a similarly prefixed
/// sibling such as C:/repo-other is never treated as inside C:/repo.
/// </summary>
public static class CuiResourceBundle
{
    private static readonly CuiLexicalValidator Validator = new CuiLexicalValidator();

    public static List<CuiResourceEntry> Collect(string root, IEnumerable<string> fullPaths, ICuiResourceLog log)
    {
        var entries = new List<CuiResourceEntry>();
        var normalizedRoot = NormalizeRoot(root);

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

            ValidateResource(relativePath, data, log);

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

        using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(entries.Count);
            foreach (var entry in entries)
            {
                writer.Write(entry.LogicalName);
                writer.Write(entry.Data.Length);
                writer.Write(entry.Data);
            }
        }

        log.LogMessage(MessageImportance.High,
            $"Generated CUI resource bundle: {entries.Count} resources -> {output}");
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

    private static void ValidateResource(string relativePath, byte[] data, ICuiResourceLog log)
    {
        var expectedRoot = ExpectedRoot(Path.GetExtension(relativePath));

        // Binary resources (fonts, images) are embedded without lexical validation.
        if (expectedRoot is null) return;

        var result = Validator.Validate(Encoding.UTF8.GetString(data));
        if (!result.IsValid)
        {
            log.LogWarning($"CUI resource '{relativePath}' has invalid CUI syntax: {result.Error}");
            return;
        }

        if (result.RootLocalName != expectedRoot)
            log.LogWarning($"CUI resource '{relativePath}' does not have a <{expectedRoot}> root element.");
    }

    private static string? ExpectedRoot(string extension) => extension.ToLowerInvariant() switch
    {
        ".cui" => "Cui",
        ".cui-theme" => "Theme",
        ".cui-prefab" => "Prefab",
        _ => null,
    };
}
