using System.Text.Json;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux overlay manager implementation using XDG Desktop Portal or Wayland layer-shell.</summary>
public sealed class PortalOverlayManager : IOverlayManager
{
    private readonly Dictionary<string, OverlaySession> _sessions = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public async Task<OverlaySession?> CreateOverlayAsync(OverlayOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildCreateArgs(options);
            await RunPortalAsync("org.freedesktop.portal.Overlay", "Create", args, cancellationToken);
            
            var session = new OverlaySession(Guid.NewGuid().ToString("N"), options.Type);
            
            lock (_lock)
            {
                _sessions[session.SessionId] = session;
            }

            return session;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateOverlayAsync(string sessionId, OverlayUpdate update, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_sessions.ContainsKey(sessionId))
                return false;
        }

        try
        {
            var args = BuildUpdateArgs(sessionId, update);
            await RunPortalAsync("org.freedesktop.portal.Overlay", "Update", args, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DestroyOverlayAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_sessions.Remove(sessionId))
                return false;
        }

        try
        {
            var args = new { overlay_id = sessionId };
            await RunPortalAsync("org.freedesktop.portal.Overlay", "Destroy", args, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object BuildCreateArgs(OverlayOptions options)
    {
        var dict = new Dictionary<string, object>
        {
            ["overlay_id"] = Guid.NewGuid().ToString("N"),
            ["type"] = options.Type.ToString().ToLowerInvariant()
        };

        if (!string.IsNullOrWhiteSpace(options.Title))
            dict["title"] = options.Title;

        if (options.X.HasValue) dict["x"] = options.X.Value;
        if (options.Y.HasValue) dict["y"] = options.Y.Value;
        if (options.Width.HasValue) dict["width"] = options.Width.Value;
        if (options.Height.HasValue) dict["height"] = options.Height.Value;
        dict["click_through"] = options.ClickThrough;

        return dict;
    }

    private static object BuildUpdateArgs(string sessionId, OverlayUpdate update)
    {
        var dict = new Dictionary<string, object>
        {
            ["overlay_id"] = sessionId
        };

        if (update.X.HasValue) dict["x"] = update.X.Value;
        if (update.Y.HasValue) dict["y"] = update.Y.Value;
        if (update.Width.HasValue) dict["width"] = update.Width.Value;
        if (update.Height.HasValue) dict["height"] = update.Height.Value;
        if (!string.IsNullOrWhiteSpace(update.Content)) dict["content"] = update.Content;
        if (update.Visible.HasValue) dict["visible"] = update.Visible.Value;

        return dict;
    }

    private static async Task RunPortalAsync(string service, string method, object args, CancellationToken cancellationToken)
    {
        var jsonArgs = JsonSerializer.Serialize(args);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Portal call failed: {error}");
        }
    }
}

/// <summary>Null implementation for environments without overlay support.</summary>
public sealed class NullOverlayManager : IOverlayManager
{
    public Task<OverlaySession?> CreateOverlayAsync(OverlayOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult<OverlaySession?>(null);

    public Task<bool> UpdateOverlayAsync(string sessionId, OverlayUpdate update, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> DestroyOverlayAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}