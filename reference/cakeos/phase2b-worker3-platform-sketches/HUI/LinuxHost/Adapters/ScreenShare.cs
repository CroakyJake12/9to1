using System.Diagnostics;
using System.Text.Json;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux screen share implementation using XDG Desktop Portal ScreenCast.</summary>
public sealed class PortalScreenShare : IScreenShare
{
    private readonly Dictionary<string, ScreenShareSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public async Task<ScreenShareSession?> StartAsync(ScreenShareOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildScreenCastArgs(options);
            var result = await RunPortalAsync("org.freedesktop.portal.ScreenCast", "CreateSession", args, cancellationToken);
            var sessionHandle = ParseSessionHandle(result);
            
            if (sessionHandle is null)
                return null;

            var selectArgs = new { session_handle = sessionHandle, multiple = false, types = GetTargetTypes(options.Target) };
            var selectResult = await RunPortalAsync("org.freedesktop.portal.ScreenCast", "SelectSources", selectArgs, cancellationToken);
            
            var startArgs = new { session_handle = sessionHandle };
            await RunPortalAsync("org.freedesktop.portal.ScreenCast", "Start", startArgs, cancellationToken);

            var session = new ScreenShareSession(Guid.NewGuid().ToString("N"), sessionHandle, options.Target);
            
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

    public async Task StopAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        string? handle;
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                handle = session.NodeId;
            else
                return;
        }

        if (handle is not null)
        {
            try
            {
                var args = new { session_handle = handle };
                await RunPortalAsync("org.freedesktop.portal.ScreenCast", "Close", args, cancellationToken);
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            _sessions.Remove(sessionId);
        }
    }

    public async Task<ScreenShareFrame?> CaptureFrameAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_sessions.ContainsKey(sessionId))
                return null;
        }

        // Note: Actual frame capture requires PipeWire stream negotiation
        // This is a stub that returns a placeholder frame
        await Task.Delay(1, cancellationToken);
        return new ScreenShareFrame(
            sessionId,
            ReadOnlyMemory<byte>.Empty,
            0, 0,
            PixelFormat.Bgra8888,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    }

    public async IAsyncEnumerable<ScreenShareFrame> WatchFramesAsync(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await CaptureFrameAsync(sessionId, cancellationToken);
            if (frame is not null)
                yield return frame;
            
            await Task.Delay(16, cancellationToken); // ~60fps
        }
    }

    private static object BuildScreenCastArgs(ScreenShareOptions options)
    {
        return new
        {
            handle_token = "cakeos-screen-share",
            session_handle_token = "cakeos-screen-share-session"
        };
    }

    private static uint GetTargetTypes(ScreenShareTarget target)
    {
        // Portal source types: 1=Monitor, 2=Window, 4=Region
        return target switch
        {
            ScreenShareTarget.Monitor => 1,
            ScreenShareTarget.Window => 2,
            ScreenShareTarget.Region => 4,
            _ => 7 // All
        };
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

    private static string? ParseSessionHandle(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("object path", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    return parts[0];
            }
        }
        return null;
    }
}

/// <summary>Null implementation for environments without XDG Desktop Portal ScreenCast.</summary>
public sealed class NullScreenShare : IScreenShare
{
    public Task<ScreenShareSession?> StartAsync(ScreenShareOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult<ScreenShareSession?>(null);

    public Task StopAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<ScreenShareFrame?> CaptureFrameAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult<ScreenShareFrame?>(null);

    public async IAsyncEnumerable<ScreenShareFrame> WatchFramesAsync(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield break;
    }
}