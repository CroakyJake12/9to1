namespace CakeOS.Platform;

/// <summary>Platform-neutral secret storage abstraction. Linux implementation uses libsecret/keyring.</summary>
public interface ISecretStore
{
    Task<bool> SetAsync(string collection, string key, string secret, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string collection, string key, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string collection, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListKeysAsync(string collection, CancellationToken cancellationToken = default);
}

/// <summary>Platform-neutral file picker abstraction. Linux implementation uses XDG Desktop Portal (GTK/KDE).</summary>
public interface IFilePicker
{
    Task<string?> PickFileAsync(FilePickerOptions options, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions options, CancellationToken cancellationToken = default);
    Task<string?> PickFolderAsync(FolderPickerOptions options, CancellationToken cancellationToken = default);
    Task<string?> SaveFileAsync(SaveFileOptions options, CancellationToken cancellationToken = default);
}

public sealed record FilePickerOptions(
    string? Title = null,
    IReadOnlyCollection<FileFilter>? Filters = null,
    string? DefaultFolder = null,
    string? DefaultFileName = null,
    bool AllowMultiple = false);

public sealed record FolderPickerOptions(
    string? Title = null,
    string? DefaultFolder = null);

public sealed record SaveFileOptions(
    string? Title = null,
    IReadOnlyCollection<FileFilter>? Filters = null,
    string? DefaultFolder = null,
    string? DefaultFileName = null);

public sealed record FileFilter(
    string Name,
    IReadOnlyCollection<string> Patterns);

/// <summary>Platform-neutral audio abstraction. Linux implementation uses PipeWire.</summary>
public interface IAudioManager
{
    Task<AudioDeviceInfo[]> GetOutputDevicesAsync(CancellationToken cancellationToken = default);
    Task<AudioDeviceInfo[]> GetInputDevicesAsync(CancellationToken cancellationToken = default);
    Task<bool> SetDefaultOutputAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<bool> SetDefaultInputAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<IAudioStream?> CreateOutputStreamAsync(AudioStreamParameters parameters, CancellationToken cancellationToken = default);
    Task<IAudioStream?> CreateInputStreamAsync(AudioStreamParameters parameters, CancellationToken cancellationToken = default);
}

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    string Description,
    bool IsDefault,
    AudioDeviceType Type);

public enum AudioDeviceType
{
    Output = 0,
    Input = 1
}

public sealed record AudioStreamParameters(
    int SampleRate,
    int Channels,
    AudioFormat Format,
    int BufferSizeFrames);

public enum AudioFormat
{
    Float32 = 0,
    Int16 = 1,
    Int24 = 2,
    Int32 = 3
}

public interface IAudioStream : IAsyncDisposable
{
    AudioStreamParameters Parameters { get; }
    Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Platform-neutral screen share abstraction. Linux implementation uses XDG Desktop Portal ScreenCast.</summary>
public interface IScreenShare
{
    Task<ScreenShareSession?> StartAsync(ScreenShareOptions options, CancellationToken cancellationToken = default);
    Task StopAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ScreenShareFrame?> CaptureFrameAsync(string sessionId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ScreenShareFrame> WatchFramesAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed record ScreenShareOptions(
    bool IncludeAudio = false,
    bool IncludeCursor = true,
    ScreenShareTarget Target = ScreenShareTarget.Any);

public enum ScreenShareTarget
{
    Any = 0,
    Window = 1,
    Monitor = 2,
    Region = 3
}

public sealed record ScreenShareSession(
    string SessionId,
    string? NodeId,
    ScreenShareTarget Target);

public sealed record ScreenShareFrame(
    string SessionId,
    ReadOnlyMemory<byte> PixelData,
    int Width,
    int Height,
    PixelFormat Format,
    long TimestampUs);

public enum PixelFormat
{
    Bgra8888 = 0,
    Rgba8888 = 1,
    Rgb888 = 2
}

/// <summary>Platform-neutral global shortcut abstraction. Linux implementation uses XDG Desktop Portal GlobalShortcuts or libinput/udev.</summary>
public interface IGlobalShortcuts
{
    Task<bool> RegisterAsync(GlobalShortcut shortcut, CancellationToken cancellationToken = default);
    Task<bool> UnregisterAsync(string shortcutId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GlobalShortcut>> GetRegisteredAsync(CancellationToken cancellationToken = default);
    event EventHandler<GlobalShortcutActivatedEventArgs>? Activated;
}

public sealed record GlobalShortcut(
    string ShortcutId,
    string DisplayName,
    KeyCombination Combination,
    ShortcutScope Scope = ShortcutScope.Global);

public sealed record KeyCombination(
    IReadOnlyCollection<KeyModifier> Modifiers,
    Key Key);

public enum KeyModifier
{
    Control = 0,
    Alt = 1,
    Shift = 2,
    Super = 3
}

public enum Key
{
    Unknown = 0,
    Space = 32,
    Enter = 13,
    Escape = 27,
    Tab = 9,
    A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75, L = 76, M = 77,
    N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90,
    D0 = 48, D1 = 49, D2 = 50, D3 = 51, D4 = 52, D5 = 53, D6 = 54, D7 = 55, D8 = 56, D9 = 57,
    F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117, F7 = 118, F8 = 119, F9 = 120, F10 = 121, F11 = 122, F12 = 123
}

public enum ShortcutScope
{
    Global = 0,
    Application = 1
}

public sealed record GlobalShortcutActivatedEventArgs(string ShortcutId);

/// <summary>Platform-neutral overlay/capture abstraction. Linux implementation uses XDG Desktop Portal or Wayland protocols.</summary>
public interface IOverlayManager
{
    Task<OverlaySession?> CreateOverlayAsync(OverlayOptions options, CancellationToken cancellationToken = default);
    Task<bool> UpdateOverlayAsync(string sessionId, OverlayUpdate update, CancellationToken cancellationToken = default);
    Task<bool> DestroyOverlayAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed record OverlayOptions(
    OverlayType Type,
    string? Title = null,
    int? X = null,
    int? Y = null,
    int? Width = null,
    int? Height = null,
    bool ClickThrough = false);

public enum OverlayType
{
    HUD = 0,
    Toast = 1,
    Prompt = 2,
    Custom = 3
}

public sealed record OverlayUpdate(
    int? X = null,
    int? Y = null,
    int? Width = null,
    int? Height = null,
    string? Content = null,
    bool? Visible = null);

public sealed record OverlaySession(
    string SessionId,
    OverlayType Type);

/// <summary>Platform-neutral notification transport abstraction. Linux implementation uses libnotify/Freedesktop notifications.</summary>
public interface INotificationTransport
{
    Task<bool> ShowAsync(PlatformNotification notification, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(PlatformNotification notification, CancellationToken cancellationToken = default);
    Task<bool> CloseAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<bool> CloseAllAsync(CancellationToken cancellationToken = default);
    event EventHandler<NotificationActionInvokedEventArgs>? ActionInvoked;
}

public sealed record NotificationActionInvokedEventArgs(Guid NotificationId, string ActionId);

/// <summary>Platform-neutral scheduler abstraction. Linux implementation uses systemd user units.</summary>
public interface IScheduler
{
    Task<string> ScheduleAsync(ScheduledTask task, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(string taskId, CancellationToken cancellationToken = default);
    Task<bool> RescheduleAsync(string taskId, ScheduledTask task, CancellationToken cancellationToken = default);
    Task<ScheduledTask?> GetAsync(string taskId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken = default);
    event EventHandler<TaskTriggeredEventArgs>? TaskTriggered;
}

public sealed record ScheduledTask(
    string TaskId,
    string DisplayName,
    ScheduleTrigger Trigger,
    string Command,
    IReadOnlyDictionary<string, string>? Environment = null,
    bool Persistent = false);

public sealed record ScheduleTrigger(
    ScheduleTriggerKind Kind,
    TimeSpan? IntervalValue = null,
    string? CronExpression = null,
    DateTimeOffset? At = null)
{
    public static ScheduleTrigger Once(DateTimeOffset at) => new(ScheduleTriggerKind.Once, At: at);
    public static ScheduleTrigger Interval(TimeSpan interval) => new(ScheduleTriggerKind.Interval, IntervalValue: interval);
    public static ScheduleTrigger Calendar(string cronExpression) => new(ScheduleTriggerKind.Calendar, CronExpression: cronExpression);
    public static ScheduleTrigger Startup => new(ScheduleTriggerKind.Startup);
    public static ScheduleTrigger Login => new(ScheduleTriggerKind.Login);
    public static ScheduleTrigger Idle => new(ScheduleTriggerKind.Idle);
}

public enum ScheduleTriggerKind
{
    Once = 0,
    Interval = 1,
    Calendar = 2,
    Startup = 3,
    Login = 4,
    Idle = 5
}

public sealed record TaskTriggeredEventArgs(string TaskId, DateTimeOffset TriggeredAtUtc);