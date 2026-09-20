using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace CakeOS.Notifications;

/// <summary>
/// Notifications consumer service consuming the canonical NotificationService.
/// Provides a platform-neutral API for notification management with UI stub support.
/// </summary>
public sealed class NotificationsService(
    INotificationService notificationService,
    ILogger<NotificationsService> logger)
{
    /// <summary>
    /// Publishes a notification.
    /// </summary>
    public async ValueTask<PlatformNotification> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        logger.LogDebug("Publishing notification from {SourceId}: {Title}", draft.SourceId, draft.Title);
        return await notificationService.PublishAsync(draft, cancellationToken);
    }

    /// <summary>
    /// Publishes a simple notification with title and body.
    /// </summary>
    public async ValueTask<PlatformNotification> PublishAsync(
        string sourceId,
        NotificationSeverity severity,
        string title,
        string body,
        IReadOnlyCollection<NotificationAction>? actions = null,
        NotificationPersistencePolicy persistencePolicy = NotificationPersistencePolicy.Persistent,
        CancellationToken cancellationToken = default)
    {
        var draft = new NotificationDraft(sourceId, severity, title, body, actions, persistencePolicy);
        return await PublishAsync(draft, cancellationToken);
    }

    /// <summary>
    /// Gets active (non-dismissed) notifications.
    /// </summary>
    public async ValueTask<IReadOnlyList<PlatformNotification>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await notificationService.GetActiveAsync(cancellationToken);
    }

    /// <summary>
    /// Gets notification history including dismissed.
    /// </summary>
    public async ValueTask<IReadOnlyList<PlatformNotification>> GetHistoryAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await notificationService.GetHistoryAsync(limit, cancellationToken);
    }

    /// <summary>
    /// Marks a notification as read.
    /// </summary>
    public async ValueTask<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Marking notification {NotificationId} as read", notificationId);
        return await notificationService.MarkReadAsync(notificationId, cancellationToken);
    }

    /// <summary>
    /// Dismisses a notification.
    /// </summary>
    public async ValueTask<bool> DismissAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Dismissing notification {NotificationId}", notificationId);
        return await notificationService.DismissAsync(notificationId, cancellationToken);
    }

    /// <summary>
    /// Clears all dismissed notifications.
    /// </summary>
    public async ValueTask<int> ClearDismissedAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Clearing dismissed notifications");
        return await notificationService.ClearDismissedAsync(cancellationToken);
    }

    /// <summary>
    /// Subscribes to notification changes.
    /// </summary>
    public IDisposable Subscribe(Action<NotificationEvent> handler)
    {
        var wrapper = new EventHandler<NotificationEvent>((_, e) => handler(e));
        notificationService.Changed += wrapper;
        return new Subscription(notificationService, wrapper);
    }

    /// <summary>
    /// Gets notifications filtered by severity.
    /// </summary>
    public async ValueTask<IReadOnlyList<PlatformNotification>> GetBySeverityAsync(
        NotificationSeverity severity,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var notifications = activeOnly
            ? await notificationService.GetActiveAsync(cancellationToken)
            : await notificationService.GetHistoryAsync(int.MaxValue, cancellationToken);

        return notifications.Where(n => n.Severity == severity).ToImmutableList();
    }

    /// <summary>
    /// Gets notifications filtered by source.
    /// </summary>
    public async ValueTask<IReadOnlyList<PlatformNotification>> GetBySourceAsync(
        string sourceId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var notifications = activeOnly
            ? await notificationService.GetActiveAsync(cancellationToken)
            : await notificationService.GetHistoryAsync(int.MaxValue, cancellationToken);

        return notifications.Where(n => string.Equals(n.SourceId, sourceId, StringComparison.Ordinal)).ToImmutableList();
    }
}

/// <summary>
/// Extension methods for dependency injection registration.
/// </summary>
public static class NotificationsServiceExtensions
{
    public static IServiceCollection AddNotificationsService(this IServiceCollection services)
    {
        return services.AddScoped<NotificationsService>();
    }
}

/// <summary>
/// UI stub interface for notification presentation.
/// Implementations provide platform-specific UI rendering.
/// </summary>
public interface INotificationUi
{
    /// <summary>
    /// Shows a notification in the UI.
    /// </summary>
    ValueTask ShowAsync(PlatformNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a notification in the UI.
    /// </summary>
    ValueTask UpdateAsync(PlatformNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a notification from the UI.
    /// </summary>
    ValueTask RemoveAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all notifications from the UI.
    /// </summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default no-op UI stub for headless/testing scenarios.
/// </summary>
public sealed class NullNotificationUi : INotificationUi
{
    public ValueTask ShowAsync(PlatformNotification notification, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask UpdateAsync(PlatformNotification notification, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask RemoveAsync(Guid notificationId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>
/// Subscription for notification events.
/// </summary>
internal sealed class Subscription : IDisposable
{
    private readonly INotificationService _service;
    private readonly EventHandler<NotificationEvent> _handler;
    private bool _disposed;

    public Subscription(INotificationService service, EventHandler<NotificationEvent> handler)
    {
        _service = service;
        _handler = handler;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _service.Changed -= _handler;
            _disposed = true;
        }
    }
}