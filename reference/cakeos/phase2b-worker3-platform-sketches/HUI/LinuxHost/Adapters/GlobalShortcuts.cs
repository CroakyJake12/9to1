using System.Collections.Concurrent;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux global shortcuts implementation using XDG Desktop Portal GlobalShortcuts.</summary>
public sealed class PortalGlobalShortcuts : IGlobalShortcuts
{
    private readonly ConcurrentDictionary<string, GlobalShortcut> _shortcuts = new(StringComparer.Ordinal);
    public event EventHandler<GlobalShortcutActivatedEventArgs>? Activated;

    public async Task<bool> RegisterAsync(GlobalShortcut shortcut, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildRegisterArgs(shortcut);
            await RunPortalAsync("org.freedesktop.portal.GlobalShortcuts", "Register", args, cancellationToken);
            
            _shortcuts[shortcut.ShortcutId] = shortcut;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UnregisterAsync(string shortcutId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_shortcuts.TryGetValue(shortcutId, out var shortcut))
                return false;

            var args = new { shortcut_id = shortcutId };
            await RunPortalAsync("org.freedesktop.portal.GlobalShortcuts", "Unregister", args, cancellationToken);
            
            _shortcuts.TryRemove(shortcutId, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<GlobalShortcut>> GetRegisteredAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<GlobalShortcut>>(_shortcuts.Values.ToArray());
    }

    private static object BuildRegisterArgs(GlobalShortcut shortcut)
    {
        var modifiers = 0;
        foreach (var mod in shortcut.Combination.Modifiers)
        {
            modifiers |= mod switch
            {
                KeyModifier.Control => 1 << 0,
                KeyModifier.Alt => 1 << 1,
                KeyModifier.Shift => 1 << 2,
                KeyModifier.Super => 1 << 3,
                _ => 0
            };
        }

        return new
        {
            shortcut_id = shortcut.ShortcutId,
            description = shortcut.DisplayName,
            trigger = new
            {
                modifiers,
                key_code = (int)shortcut.Combination.Key
            }
        };
    }

    private static async Task RunPortalAsync(string service, string method, object args, CancellationToken cancellationToken)
    {
        var jsonArgs = System.Text.Json.JsonSerializer.Serialize(args);
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

/// <summary>Null implementation for environments without XDG Desktop Portal GlobalShortcuts.</summary>
public sealed class NullGlobalShortcuts : IGlobalShortcuts
{
    public event EventHandler<GlobalShortcutActivatedEventArgs>? Activated;

    public Task<bool> RegisterAsync(GlobalShortcut shortcut, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> UnregisterAsync(string shortcutId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<GlobalShortcut>> GetRegisteredAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GlobalShortcut>>([]);
}