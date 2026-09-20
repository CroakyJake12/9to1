using CakeOS.Notifications;
using CakeOS.Platform;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CakeOS.Notifications.Tests;

public sealed class NotificationsServiceTests
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-notifications-tests", Guid.NewGuid().ToString("N"), "haven");

    [Fact]
    public async Task PublishAndGetActive_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        var draft = new NotificationDraft("test.app", NotificationSeverity.Information, "Test Title", "Test Body");
        var notification = await notifications.PublishAsync(draft);

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal("test.app", notification.SourceId);
        Assert.Equal(NotificationSeverity.Information, notification.Severity);
        Assert.Equal("Test Title", notification.Title);
        Assert.Equal("Test Body", notification.Body);

        var active = await notifications.GetActiveAsync();
        Assert.Single(active);
        Assert.Equal(notification.Id, active[0].Id);
    }

    [Fact]
    public async Task PublishWithActions_PreservesActions()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        var actions = new[]
        {
            new NotificationAction("dismiss", "Dismiss"),
            new NotificationAction("open", "Open", true)
        };
        var draft = new NotificationDraft("test.app", NotificationSeverity.Warning, "Title", "Body", actions);
        var notification = await notifications.PublishAsync(draft);

        Assert.Equal(2, notification.Actions.Count);
        Assert.Contains(notification.Actions, a => a.ActionId == "dismiss");
        Assert.Contains(notification.Actions, a => a.ActionId == "open" && a.IsPrimary);
    }

    [Fact]
    public async Task MarkRead_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        var notification = await notifications.PublishAsync(
            new NotificationDraft("test.app", NotificationSeverity.Information, "Title", "Body"));

        Assert.Null(notification.ReadAtUtc);

        var result = await notifications.MarkReadAsync(notification.Id);
        Assert.True(result);

        var updated = (await notifications.GetActiveAsync()).First(n => n.Id == notification.Id);
        Assert.NotNull(updated.ReadAtUtc);
    }

    [Fact]
    public async Task Dismiss_RemovesFromActive()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        var notification = await notifications.PublishAsync(
            new NotificationDraft("test.app", NotificationSeverity.Information, "Title", "Body"));

        var activeBefore = await notifications.GetActiveAsync();
        Assert.Single(activeBefore);

        var result = await notifications.DismissAsync(notification.Id);
        Assert.True(result);

        var activeAfter = await notifications.GetActiveAsync();
        Assert.Empty(activeAfter);

        var history = await notifications.GetHistoryAsync(10);
        Assert.Single(history);
        Assert.NotNull(history[0].DismissedAtUtc);
    }

    [Fact]
    public async Task ClearDismissed_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        var n1 = await notifications.PublishAsync(new NotificationDraft("test.app", NotificationSeverity.Information, "1", "Body"));
        var n2 = await notifications.PublishAsync(new NotificationDraft("test.app", NotificationSeverity.Information, "2", "Body"));

        await notifications.DismissAsync(n1.Id);
        await notifications.DismissAsync(n2.Id);

        var cleared = await notifications.ClearDismissedAsync();
        Assert.Equal(2, cleared);

        var history = await notifications.GetHistoryAsync(10);
        Assert.Empty(history);
    }

    [Fact]
    public async Task Subscribe_ReceivesEvents()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        var events = new List<NotificationEvent>();
        using var subscription = notifications.Subscribe(e => events.Add(e));

        await notifications.PublishAsync(new NotificationDraft("test.app", NotificationSeverity.Information, "Title", "Body"));

        Assert.Single(events);
        Assert.Equal(NotificationEventKind.Published, events[0].Kind);
    }

    [Fact]
    public async Task GetBySeverity_FiltersCorrectly()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        await notifications.PublishAsync(new NotificationDraft("test.app", NotificationSeverity.Information, "Info", "Body"));
        await notifications.PublishAsync(new NotificationDraft("test.app", NotificationSeverity.Warning, "Warning", "Body"));
        await notifications.PublishAsync(new NotificationDraft("test.app", NotificationSeverity.Error, "Error", "Body"));

        var info = await notifications.GetBySeverityAsync(NotificationSeverity.Information);
        Assert.Single(info);
        Assert.Equal("Info", info[0].Title);

        var warnings = await notifications.GetBySeverityAsync(NotificationSeverity.Warning);
        Assert.Single(warnings);
        Assert.Equal("Warning", warnings[0].Title);
    }

    [Fact]
    public async Task GetBySource_FiltersCorrectly()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var notificationService = new NotificationService(settings, () => DateTimeOffset.UtcNow);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<NotificationsService>();
        var notifications = new NotificationsService(notificationService, logger);

        await notifications.PublishAsync(new NotificationDraft("app.one", NotificationSeverity.Information, "One", "Body"));
        await notifications.PublishAsync(new NotificationDraft("app.two", NotificationSeverity.Information, "Two", "Body"));
        await notifications.PublishAsync(new NotificationDraft("app.one", NotificationSeverity.Information, "One Again", "Body"));

        var fromOne = await notifications.GetBySourceAsync("app.one");
        Assert.Equal(2, fromOne.Count);
        Assert.All(fromOne, n => Assert.Equal("app.one", n.SourceId));
    }

    [Fact]
    public async Task NullNotificationUi_NoOp()
    {
        var ui = new NullNotificationUi();
        
        await ui.ShowAsync(new PlatformNotification(
            Guid.NewGuid(), "test", NotificationSeverity.Information, "Title", "Body", [], DateTimeOffset.UtcNow));
        await ui.UpdateAsync(new PlatformNotification(
            Guid.NewGuid(), "test", NotificationSeverity.Information, "Title", "Body", [], DateTimeOffset.UtcNow));
        await ui.RemoveAsync(Guid.NewGuid());
        await ui.ClearAsync();
        
        // Should not throw
    }
}