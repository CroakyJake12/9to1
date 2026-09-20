using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux scheduler implementation using systemd user units.</summary>
public sealed class SystemdScheduler : IScheduler
{
    private readonly string _unitDir;
    private readonly ConcurrentDictionary<string, ScheduledTask> _tasks = new(StringComparer.Ordinal);
    public event EventHandler<TaskTriggeredEventArgs>? TaskTriggered;

    public SystemdScheduler()
    {
        var userConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        _unitDir = Path.Combine(userConfig, "systemd", "user", "cakeos-tasks");
        Directory.CreateDirectory(_unitDir);
    }

    public async Task<string> ScheduleAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        try
        {
            var unitName = $"{task.TaskId}.service";
            var timerName = $"{task.TaskId}.timer";
            
            var serviceContent = BuildServiceContent(task);
            var timerContent = BuildTimerContent(task);

            var servicePath = Path.Combine(_unitDir, unitName);
            var timerPath = Path.Combine(_unitDir, timerName);

            await File.WriteAllTextAsync(servicePath, serviceContent, cancellationToken);
            await File.WriteAllTextAsync(timerPath, timerContent, cancellationToken);

            await RunSystemctlAsync("daemon-reload", cancellationToken);
            await RunSystemctlAsync($"enable --now {timerName}", cancellationToken);

            _tasks[task.TaskId] = task;
            return task.TaskId;
        }
        catch
        {
            throw;
        }
    }

    public async Task<bool> CancelAsync(string taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var timerName = $"{taskId}.timer";
            await RunSystemctlAsync($"disable --now {timerName}", cancellationToken);
            
            var servicePath = Path.Combine(_unitDir, $"{taskId}.service");
            var timerPath = Path.Combine(_unitDir, timerName);
            
            if (File.Exists(servicePath)) File.Delete(servicePath);
            if (File.Exists(timerPath)) File.Delete(timerPath);

            await RunSystemctlAsync("daemon-reload", cancellationToken);
            _tasks.TryRemove(taskId, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RescheduleAsync(string taskId, ScheduledTask task, CancellationToken cancellationToken = default)
    {
        await CancelAsync(taskId, cancellationToken);
        await ScheduleAsync(task, cancellationToken);
        return true;
    }

    public Task<ScheduledTask?> GetAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

    public Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ScheduledTask>>(_tasks.Values.ToArray());
    }

    private string BuildServiceContent(ScheduledTask task)
    {
        var envVars = task.Environment?.Select(kvp => $"Environment={kvp.Key}={kvp.Value}") ?? [];
        var envSection = envVars.Any() ? string.Join("\n", envVars) + "\n" : "";

        return $""" 
[Unit]
Description={task.DisplayName}
After=network.target

[Service]
Type=oneshot
ExecStart={task.Command}
{envSection}StandardOutput=journal
StandardError=journal
""";
    }

    private string BuildTimerContent(ScheduledTask task)
    {
        var schedule = task.Trigger.Kind switch
        {
            ScheduleTriggerKind.Once => task.Trigger.At.HasValue ? $"OnCalendar={task.Trigger.At.Value:yyyy-MM-dd HH:mm:ss}" : throw new InvalidOperationException("Once trigger requires At value"),
            ScheduleTriggerKind.Interval => task.Trigger.IntervalValue.HasValue ? $"OnUnitActiveSec={task.Trigger.IntervalValue.Value.TotalSeconds}sec" : throw new InvalidOperationException("Interval trigger requires Interval value"),
            ScheduleTriggerKind.Calendar => !string.IsNullOrEmpty(task.Trigger.CronExpression) ? $"OnCalendar={task.Trigger.CronExpression}" : throw new InvalidOperationException("Calendar trigger requires CronExpression"),
            ScheduleTriggerKind.Startup => "OnBootSec=0",
            ScheduleTriggerKind.Login => "OnActiveSec=0",
            ScheduleTriggerKind.Idle => "OnIdleSec=60",
            _ => throw new NotSupportedException($"Trigger kind {task.Trigger.Kind} not supported")
        };

        var persistent = task.Persistent ? "Persistent=true\n" : "";

        return $"""
[Unit]
Description=Timer for {task.DisplayName}
Requires={task.TaskId}.service

[Timer]
{schedule}
{persistent}Unit={task.TaskId}.service

[Install]
WantedBy=timers.target
""";
    }

    private async Task RunSystemctlAsync(string arguments, CancellationToken cancellationToken)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "systemctl",
            Arguments = $"--user {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
            throw new InvalidOperationException("systemctl not found");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"systemctl failed: {error}");
        }
    }
}

/// <summary>Null implementation for environments without systemd.</summary>
public sealed class NullScheduler : IScheduler
{
    public event EventHandler<TaskTriggeredEventArgs>? TaskTriggered;

    public Task<string> ScheduleAsync(ScheduledTask task, CancellationToken cancellationToken = default)
        => Task.FromResult(task.TaskId);

    public Task<bool> CancelAsync(string taskId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> RescheduleAsync(string taskId, ScheduledTask task, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<ScheduledTask?> GetAsync(string taskId, CancellationToken cancellationToken = default)
        => Task.FromResult<ScheduledTask?>(null);

    public Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScheduledTask>>([]);
}