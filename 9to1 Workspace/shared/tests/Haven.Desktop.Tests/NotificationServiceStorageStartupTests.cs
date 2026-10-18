using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

// Actual SQLite migration/query paths and the production native startup binding.
// These headless checks do not certify ordinary full-window native close.
public sealed class NotificationServiceStorageStartupTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Actual_notification_query_before_migration_fails_and_after_full_migration_reads_the_same_store()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        var repository = new HavenNotificationRepository(database);
        var failure = await Assert.ThrowsAsync<SqliteException>(() => repository.GetRecentAsync(100, false, TestContext.Current.CancellationToken));
        Assert.Equal(1, failure.SqliteErrorCode);
        Assert.Contains("no such table: haven_notifications", failure.Message, StringComparison.Ordinal);

        await database.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await repository.GetRecentAsync(100, false, TestContext.Current.CancellationToken));
        Assert.Empty(await repository.GetRecentAsync(100, true, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task Native_borrower_created_before_migration_refreshes_after_actual_storage_startup_and_retires_cleanly()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        using var backend = new HavenNotificationService(new HavenNotificationRepository(database), new ExecutionEventHub(new ExecutionEventRepository(database)));
        var notifications = new NotificationService(backend, refreshOnConstruction: false);
        var startupOwner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        try
        {
            var storageStartup = database.InitializeAsync(TestContext.Current.CancellationToken);
            await startupOwner.RunAsync(async original => Assert.True(
                await notifications.RefreshAfterStorageStartupAsync(original, storageStartup)))
                .WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.Empty(notifications.Notifications);
            await startupOwner.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            await notifications.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.True(notifications.OriginalClose!.IsCompletedSuccessfully);
        }
        finally
        {
            try { await startupOwner.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
            finally { await notifications.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
        }
    }

    [AvaloniaFact]
    public async Task Existing_immediate_constructor_retains_actual_early_read_failure_even_after_migration_repairs_the_store()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        using var backend = new HavenNotificationService(new HavenNotificationRepository(database), new ExecutionEventHub(new ExecutionEventRepository(database)));
        var notifications = new NotificationService(backend);
        SqliteException? verifiedFailure = null;
        try
        {
            await database.InitializeAsync(TestContext.Current.CancellationToken);
            var close = notifications.CloseAndDrainAsync();
            var failure = await Assert.ThrowsAsync<SqliteException>(() => close.WaitAsync(Bound, TestContext.Current.CancellationToken));
            Assert.Contains("no such table: haven_notifications", failure.Message, StringComparison.Ordinal);
            Assert.Same(close, notifications.CloseAndDrainAsync());
            verifiedFailure = failure;
        }
        finally
        {
            try { await notifications.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
            catch (SqliteException actual) when (ReferenceEquals(actual, verifiedFailure)) { }
        }
    }

    [AvaloniaFact]
    public async Task App_retirement_joins_held_storage_startup_without_admitting_late_notification_read()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        using var backend = new HavenNotificationService(new HavenNotificationRepository(database), new ExecutionEventHub(new ExecutionEventRepository(database)));
        var notifications = new NotificationService(backend, refreshOnConstruction: false);
        var startupOwner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task storageStartup = InitializeStorageAsync();
        Task? startup = null;
        try
        {
            startup = startupOwner.RunAsync(async original => Assert.False(
                await notifications.RefreshAfterStorageStartupAsync(original, storageStartup)));
            await entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            var close = startupOwner.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            release.SetResult();
            await startup.WaitAsync(Bound, TestContext.Current.CancellationToken);
            await close.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.Empty(notifications.Notifications);
            await notifications.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.True(notifications.OriginalClose!.IsCompletedSuccessfully);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                try { await storageStartup.WaitAsync(Bound, CancellationToken.None); }
                finally
                {
                    try { if (startup is not null) await startup.WaitAsync(Bound, CancellationToken.None); }
                    finally { await startupOwner.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
                }
            }
            finally { await notifications.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
        }

        async Task InitializeStorageAsync()
        {
            entered.SetResult();
            await release.Task;
            await database.InitializeAsync(CancellationToken.None);
            // If a late refresh runs, this genuine schema refusal becomes a retained
            // close fault. A successful close proves it never queried after retirement.
            await using var connection = await database.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE haven_notifications;";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [AvaloniaFact]
    public async Task Actual_migration_failure_is_retained_by_the_startup_owner_without_starting_notification_reads()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        await using (var connection = await database.OpenAsync(TestContext.Current.CancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE schema_migrations(invalid_column INTEGER);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        using var backend = new HavenNotificationService(new HavenNotificationRepository(database), new ExecutionEventHub(new ExecutionEventRepository(database)));
        var notifications = new NotificationService(backend, refreshOnConstruction: false);
        var startupOwner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        SqliteException? verifiedFailure = null;
        try
        {
            var storageStartup = database.InitializeAsync(TestContext.Current.CancellationToken);
            var startup = startupOwner.RunAsync(async original =>
                await notifications.RefreshAfterStorageStartupAsync(original, storageStartup));
            var failure = await Assert.ThrowsAsync<SqliteException>(() => startup.WaitAsync(Bound, TestContext.Current.CancellationToken));
            Assert.Same(storageStartup.Exception!.InnerException, failure);
            var closeFailure = await Assert.ThrowsAsync<SqliteException>(() => startupOwner.CloseAndDrainAsync()
                .WaitAsync(Bound, TestContext.Current.CancellationToken));
            Assert.Same(failure, closeFailure);
            verifiedFailure = failure;
            await notifications.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.True(notifications.OriginalClose!.IsCompletedSuccessfully);
        }
        finally
        {
            try
            {
                try { await startupOwner.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
                catch (SqliteException actual) when (ReferenceEquals(actual, verifiedFailure)) { }
            }
            finally { await notifications.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
        }
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "9to1-notification-storage-startup-" + Guid.NewGuid().ToString("N"));
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }
}
