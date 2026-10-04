using System.Diagnostics;
using System.Runtime.InteropServices;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux secret store implementation using libsecret via secret-tool CLI.</summary>
public sealed class LibsecretSecretStore : ISecretStore
{
    public async Task<bool> SetAsync(string collection, string key, string secret, CancellationToken cancellationToken = default)
    {
        try
        {
            var escapedSecret = secret.Replace("\"", "\\\"");
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = $"store --label=\"{collection}:{key}\" collection \"{collection}\" key \"{key}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            if (process is null)
                return false;

            await process.StandardInput.WriteAsync(escapedSecret.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = $"lookup collection \"{collection}\" key \"{key}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = $"clear collection \"{collection}\" key \"{key}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string collection, CancellationToken cancellationToken = default)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = $"search --all collection \"{collection}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return [];

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode != 0)
                return [];

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(':', 2).Length > 1 ? line.Split(':', 2)[1].Trim() : "")
                .Where(k => !string.IsNullOrEmpty(k))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}

/// <summary>Null implementation for environments without libsecret.</summary>
public sealed class NullSecretStore : ISecretStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _store = new(StringComparer.Ordinal);

    public Task<bool> SetAsync(string collection, string key, string secret, CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(collection, out var dict))
        {
            dict = new Dictionary<string, string>(StringComparer.Ordinal);
            _store[collection] = dict;
        }
        dict[key] = secret;
        return Task.FromResult(true);
    }

    public Task<string?> GetAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(collection, out var dict) && dict.TryGetValue(key, out var secret))
            return Task.FromResult<string?>(secret);
        return Task.FromResult<string?>(null);
    }

    public Task<bool> DeleteAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(collection, out var dict))
            return Task.FromResult(dict.Remove(key));
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string collection, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(collection, out var dict))
            return Task.FromResult<IReadOnlyList<string>>(dict.Keys.ToArray());
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}