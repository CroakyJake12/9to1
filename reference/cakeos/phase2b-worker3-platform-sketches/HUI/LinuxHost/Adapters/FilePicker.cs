using System.Diagnostics;
using System.Text.Json;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux file picker implementation using XDG Desktop Portal (GTK/KDE).</summary>
public sealed class PortalFilePicker : IFilePicker
{
    public async Task<string?> PickFileAsync(FilePickerOptions options, CancellationToken cancellationToken = default)
    {
        var files = await PickFilesAsync(options with { AllowMultiple = false }, cancellationToken);
        return files.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildFileChooserArgs(options, acceptMultiple: options.AllowMultiple);
            var result = await RunPortalAsync("org.freedesktop.portal.FileChooser", "OpenFile", args, cancellationToken);
            return ParseFileChooserResult(result);
        }
        catch
        {
            return [];
        }
    }

    public async Task<string?> PickFolderAsync(FolderPickerOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildFolderChooserArgs(options);
            var result = await RunPortalAsync("org.freedesktop.portal.FileChooser", "SelectFolder", args, cancellationToken);
            var paths = ParseFileChooserResult(result);
            return paths.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> SaveFileAsync(SaveFileOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildSaveFileArgs(options);
            var result = await RunPortalAsync("org.freedesktop.portal.FileChooser", "SaveFile", args, cancellationToken);
            var paths = ParseFileChooserResult(result);
            return paths.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static object BuildFileChooserArgs(FilePickerOptions options, bool acceptMultiple)
    {
        var dict = new Dictionary<string, object>
        {
            ["accept_multiple"] = acceptMultiple,
            ["directory"] = false
        };

        if (!string.IsNullOrWhiteSpace(options.Title))
            dict["title"] = options.Title;

        if (!string.IsNullOrWhiteSpace(options.DefaultFolder))
            dict["current_folder"] = options.DefaultFolder;

        if (!string.IsNullOrWhiteSpace(options.DefaultFileName))
            dict["current_name"] = options.DefaultFileName;

        if (options.Filters is not null && options.Filters.Count > 0)
        {
            var filters = options.Filters.Select(f => new
            {
                f.Name,
                patterns = f.Patterns.ToArray()
            }).ToArray();
            dict["filters"] = filters;
        }

        return dict;
    }

    private static object BuildFolderChooserArgs(FolderPickerOptions options)
    {
        var dict = new Dictionary<string, object>
        {
            ["directory"] = true
        };

        if (!string.IsNullOrWhiteSpace(options.Title))
            dict["title"] = options.Title;

        if (!string.IsNullOrWhiteSpace(options.DefaultFolder))
            dict["current_folder"] = options.DefaultFolder;

        return dict;
    }

    private static object BuildSaveFileArgs(SaveFileOptions options)
    {
        var dict = new Dictionary<string, object>
        {
            ["directory"] = false,
            ["current_name"] = options.DefaultFileName ?? ""
        };

        if (!string.IsNullOrWhiteSpace(options.Title))
            dict["title"] = options.Title;

        if (!string.IsNullOrWhiteSpace(options.DefaultFolder))
            dict["current_folder"] = options.DefaultFolder;

        if (options.Filters is not null && options.Filters.Count > 0)
        {
            var filters = options.Filters.Select(f => new
            {
                f.Name,
                patterns = f.Patterns.ToArray()
            }).ToArray();
            dict["filters"] = filters;
        }

        return dict;
    }

    private static async Task<string> RunPortalAsync(string service, string method, object args, CancellationToken cancellationToken)
    {
        var jsonArgs = JsonSerializer.Serialize(args);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "busctl",
            Arguments = $"--user call org.freedesktop.portal.Desktop /org/freedesktop/portal/desktop {service} {method} s '{jsonArgs}'",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
            throw new InvalidOperationException("busctl not found");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Portal call failed: {error}");
        }

        return output;
    }

    private static string[] ParseFileChooserResult(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var uris = new List<string>();
        
        foreach (var line in lines)
        {
            if (line.Contains("file://", StringComparison.Ordinal))
            {
                var start = line.IndexOf("file://", StringComparison.Ordinal);
                var end = line.IndexOf('"', start);
                if (end > start)
                {
                    var uri = line[start..end];
                    if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.IsFile)
                        uris.Add(u.LocalPath);
                }
            }
        }
        
        return uris.ToArray();
    }
}

/// <summary>Null implementation for environments without XDG Desktop Portal.</summary>
public sealed class NullFilePicker : IFilePicker
{
    public Task<string?> PickFileAsync(FilePickerOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickFolderAsync(FolderPickerOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(SaveFileOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}