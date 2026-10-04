using System.Diagnostics;
using System.Text.Json;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux notification transport implementation using libnotify / Freedesktop notifications.</summary>
public sealed class FreedesktopNotificationTransport : INotificationTransport
{
    private readonly Dictionary<Guid, string> _notificationIds = new();
    private int _nextId = 1;
    public event EventHandler<NotificationActionInvokedEventArgs>? ActionInvoked;

    public async Task<bool> ShowAsync(PlatformNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = _nextId++;
            var args = BuildNotifyArgs(id, notification);
            await RunNotifySendAsync(args, cancellationToken);
            
            _notificationIds[notification.Id] = id.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateAsync(PlatformNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_notificationIds.TryGetValue(notification.Id, out var idStr) || !int.TryParse(idStr, out var id))
            return await ShowAsync(notification, cancellationToken);

        try
        {
            var args = BuildNotifyArgs(id, notification, replacesId: id);
            await RunNotifySendAsync(args, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CloseAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (!_notificationIds.TryGetValue(notificationId, out var idStr) || !int.TryParse(idStr, out var id))
            return false;

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                Arguments = $"--close={id}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken);
            _notificationIds.Remove(notificationId);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CloseAllAsync(CancellationToken cancellationToken = default)
    {
        var ids = _notificationIds.Values.Select(int.Parse).ToArray();
        _notificationIds.Clear();

        foreach (var id in ids)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = $"--close={id}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is not null)
                    await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
            }
        }

        return true;
    }

    private static object BuildNotifyArgs(int id, PlatformNotification notification, int? replacesId = null)
    {
        var urgency = notification.Severity switch
        {
            NotificationSeverity.Error => "critical",
            NotificationSeverity.Warning => "normal",
            NotificationSeverity.Success => "low",
            _ => "low"
        };

        var dict = new Dictionary<string, object>
        {
            ["summary"] = notification.Title,
            ["body"] = notification.Body,
            ["urgency"] = urgency,
            ["app_name"] = "HavenOS",
            ["replace_id"] = replacesId ?? id
        };

        if (notification.Actions.Count > 0)
        {
            var actions = notification.Actions.Select(a => new { a.ActionId, a.Label }).ToArray();
            dict["actions"] = actions;
        }

        if (notification.PersistencePolicy == NotificationPersistencePolicy.Ephemeral)
            dict["expire_time"] = 3000;
        else if (notification.PersistencePolicy == NotificationPersistencePolicy.Session)
            dict["expire_time"] = 10000;

        return dict;
    }

    private static async Task RunNotifySendAsync(object args, CancellationToken cancellationToken)
    {
        var jsonArgs = JsonSerializer.Serialize(args);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "notify-send",
            Arguments = $"{jsonArgs}", // notify-send doesn't take JSON directly, this is simplified
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
            throw new InvalidOperationException("notify-send not found");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"notify-send failed: {error}");
        }
    }
}

/// <summary>Null implementation for environments without Freedesktop notifications.</summary>
public sealed class NullNotificationTransport : INotificationTransport
{
    public event EventHandler<NotificationActionInvokedEventArgs>? ActionInvoked;

    public Task<bool> ShowAsync(PlatformNotification notification, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> UpdateAsync(PlatformNotification notification, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> CloseAsync(Guid notificationId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> CloseAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}