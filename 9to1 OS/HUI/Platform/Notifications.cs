namespace CakeOS.Platform;

public enum NotificationSeverity
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public enum NotificationEventKind
{
    Published = 0,
    Read = 1,
    Dismissed = 2
}

public enum NotificationPersistencePolicy
{
    Session = 0,
    Persistent = 1,
    Ephemeral = 2
}

public sealed record NotificationAction(
    string ActionId,
    string Label,
    bool IsPrimary = false,
    bool DismissOnInvoke = true);

public sealed record NotificationDraft(
    string SourceId,
    NotificationSeverity Severity,
    string Title,
    string Body,
    IReadOnlyCollection<NotificationAction>? Actions = null,
    NotificationPersistencePolicy PersistencePolicy = NotificationPersistencePolicy.Persistent);

public sealed record PlatformNotification(
    Guid Id,
    string SourceId,
    NotificationSeverity Severity,
    string Title,
    string Body,
    IReadOnlyCollection<NotificationAction> Actions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc = null,
    DateTimeOffset? DismissedAtUtc = null,
    NotificationPersistencePolicy PersistencePolicy = NotificationPersistencePolicy.Persistent);

public sealed record NotificationEvent(NotificationEventKind Kind, PlatformNotification Notification);

public interface INotificationService
{
    event EventHandler<NotificationEvent>? Changed;
    Task<IReadOnlyList<PlatformNotification>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformNotification>> GetHistoryAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<PlatformNotification> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<bool> DismissAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<int> ClearDismissedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Shared notification event model with durable dismissal/read state, actions, and persistence policy. No platform UI transport dependency.</summary>
public sealed class NotificationService(IVersionedSettingsStore settings, Func<DateTimeOffset>? clock = null) : INotificationService
{
    public const string SettingsKey = "platform.notifications.v1";

    private readonly IVersionedSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public event EventHandler<NotificationEvent>? Changed;

    public async Task<IReadOnlyList<PlatformNotification>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false))
            .Where(notification => notification.DismissedAtUtc is null)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ToArray();

    public async Task<IReadOnlyList<PlatformNotification>> GetHistoryAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToArray();

    public async Task<PlatformNotification> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        PlatformContractValidation.RequireIdentifier(draft.SourceId, nameof(draft.SourceId));
        EnsureDefined(draft.Severity, nameof(draft.Severity));
        EnsureDefined(draft.PersistencePolicy, nameof(draft.PersistencePolicy));
        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.Body))
            throw new ArgumentException("Notification title and body are required.", nameof(draft));

        var actions = draft.Actions?.ToArray() ?? [];
        var notification = new PlatformNotification(
            Guid.NewGuid(),
            draft.SourceId,
            draft.Severity,
            draft.Title.Trim(),
            draft.Body.Trim(),
            actions,
            _clock(),
            PersistencePolicy: draft.PersistencePolicy);

        var notifications = (await LoadAsync(cancellationToken).ConfigureAwait(false)).Append(notification).ToArray();
        await _settings.SetAsync(SettingsKey, notifications, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, new NotificationEvent(NotificationEventKind.Published, notification));
        return notification;
    }

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (notificationId == Guid.Empty)
            throw new ArgumentException("Notification id is required.", nameof(notificationId));
        var notifications = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var index = Array.FindIndex(notifications, notification => notification.Id == notificationId && notification.ReadAtUtc is null && notification.DismissedAtUtc is null);
        if (index < 0)
            return false;

        var read = notifications[index] with { ReadAtUtc = _clock() };
        notifications[index] = read;
        await _settings.SetAsync(SettingsKey, notifications, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, new NotificationEvent(NotificationEventKind.Read, read));
        return true;
    }

    public async Task<bool> DismissAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (notificationId == Guid.Empty)
            throw new ArgumentException("Notification id is required.", nameof(notificationId));
        var notifications = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var index = Array.FindIndex(notifications, notification => notification.Id == notificationId && notification.DismissedAtUtc is null);
        if (index < 0)
            return false;

        var dismissed = notifications[index] with { DismissedAtUtc = _clock() };
        notifications[index] = dismissed;
        await _settings.SetAsync(SettingsKey, notifications, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, new NotificationEvent(NotificationEventKind.Dismissed, dismissed));
        return true;
    }

    public async Task<int> ClearDismissedAsync(CancellationToken cancellationToken = default)
    {
        var notifications = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var active = notifications.Where(notification => notification.DismissedAtUtc is null).ToArray();
        var cleared = notifications.Length - active.Length;
        if (cleared > 0)
            await _settings.SetAsync(SettingsKey, active, cancellationToken).ConfigureAwait(false);
        return cleared;
    }

    private async Task<PlatformNotification[]> LoadAsync(CancellationToken cancellationToken) =>
        await _settings.GetAsync<PlatformNotification[]>(SettingsKey, cancellationToken).ConfigureAwait(false) ?? [];

    private static void EnsureDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, $"Value is unsupported for {typeof(T).Name}.");
    }
}