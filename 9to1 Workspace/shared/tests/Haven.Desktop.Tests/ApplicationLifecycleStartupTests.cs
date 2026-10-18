using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

// Actual production lifecycle and SQLite; controlled diagnostic/task siblings only.
public sealed class ApplicationLifecycleStartupTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Actual_migration_failure_reaches_the_caller_and_never_publishes_ready_or_clean_markers()
    {
        using var paths = new Paths();
        var database = await CorruptMigrationStore(paths);
        var diagnostics = new AppDiagnostics();
        var lifecycle = new ApplicationLifecycleService(paths, database, diagnostics);
        var failure = await Assert.ThrowsAsync<SqliteException>(() => lifecycle.StartupAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Equal(1, failure.SqliteErrorCode);
        Assert.Contains("version", failure.Message, StringComparison.Ordinal);
        Assert.True(lifecycle.IsSafeMode);
        Assert.False(lifecycle.IsStartupComplete);
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".startup")));
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".shutdown")));
        Assert.Contains(await diagnostics.GetRecentErrorsAsync(10, TestContext.Current.CancellationToken),
            row => row.Component == "Startup" && row.Error.StartsWith("Startup failed:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Real_SQLite_migration_and_raw_source_and_diagnostic_siblings_all_remain_in_the_failed_startup()
    {
        using var paths = new Paths();
        var database = await CorruptMigrationStore(paths);
        var sourceSibling = new IOException("independent actual initialization sibling");
        var reportFirst = new IOException("actual failure report source");
        var reportSecond = new InvalidOperationException("independent failure report sibling");
        var report = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        report.SetException([reportFirst, reportSecond]);
        var source = new DatabaseWithSibling(database, sourceSibling);
        var diagnostics = new Diagnostics((_, error) => error.StartsWith("Startup failed:", StringComparison.Ordinal) ? report.Task : Task.CompletedTask);
        var lifecycle = new ApplicationLifecycleService(paths, source, diagnostics);
        var startup = lifecycle.StartupAsync(TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => startup.WaitAsync(Bound, TestContext.Current.CancellationToken));
        var actualSqlite = Assert.Single(source.Actual!.Exception!.InnerExceptions.OfType<SqliteException>());
        Assert.Equal(4, failure.InnerExceptions.Count);
        foreach (var cause in new Exception[] { actualSqlite, sourceSibling, reportFirst, reportSecond })
            Assert.Contains(failure.InnerExceptions, actual => ReferenceEquals(actual, cause));
        Assert.True(startup.IsFaulted);
        Assert.True(lifecycle.IsSafeMode);
        Assert.False(lifecycle.IsStartupComplete);
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".startup")));
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".shutdown")));
    }

    [Fact]
    public async Task Success_diagnostic_failure_after_actual_SQLite_startup_does_not_leave_readiness_published()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        var cause = new IOException("actual startup diagnostic failed");
        var diagnostics = new Diagnostics((component, error) => component == "Startup" && error == "Application started successfully"
            ? Task.FromException(cause) : Task.CompletedTask);
        var lifecycle = new ApplicationLifecycleService(paths, database, diagnostics);
        var failure = await Assert.ThrowsAsync<IOException>(() => lifecycle.StartupAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Same(cause, failure);
        Assert.Empty(await new HavenNotificationRepository(database).GetRecentAsync(100, false, TestContext.Current.CancellationToken));
        Assert.True(lifecycle.IsSafeMode);
        Assert.False(lifecycle.IsStartupComplete);
        // The real started marker is truthful; it is not a completed/clean witness.
        Assert.True(File.Exists(Path.Combine(paths.DataDirectory, ".startup")));
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".shutdown")));
    }

    [Fact]
    public async Task Readiness_waits_for_the_same_held_success_diagnostic_after_actual_SQLite_startup()
    {
        using var paths = new Paths();
        var database = new SqliteDatabase(paths);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new Diagnostics((component, error) =>
        {
            if (component != "Startup" || error != "Application started successfully") return Task.CompletedTask;
            entered.TrySetResult();
            return release.Task;
        });
        var lifecycle = new ApplicationLifecycleService(paths, database, diagnostics);
        var startup = lifecycle.StartupAsync(TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.False(startup.IsCompleted);
            Assert.False(lifecycle.IsStartupComplete);
            Assert.True(File.Exists(Path.Combine(paths.DataDirectory, ".startup")));
            Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".shutdown")));
            Assert.Empty(await new HavenNotificationRepository(database).GetRecentAsync(100, false, TestContext.Current.CancellationToken));
            release.SetResult();
            await startup.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.True(lifecycle.IsStartupComplete);
            Assert.False(lifecycle.IsSafeMode);
        }
        finally
        {
            release.TrySetResult();
            await startup.WaitAsync(Bound, CancellationToken.None);
        }
    }

    private static async Task<SqliteDatabase> CorruptMigrationStore(Paths paths)
    {
        var database = new SqliteDatabase(paths);
        await using var connection = await database.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE schema_migrations(invalid_column INTEGER);";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private sealed class DatabaseWithSibling(SqliteDatabase database, Exception sibling) : IAppDatabase
    {
        internal Task? Actual;
        public Task InitializeAsync(CancellationToken token) => Actual = Task.WhenAll(
            database.InitializeAsync(token), Task.FromException(sibling));
    }

    private sealed class Diagnostics(Func<string, string, Task> record) : IAppDiagnostics
    {
        private readonly AppDiagnostics _actual = new();
        public Task RecordErrorAsync(string component, string error, string? detail, CancellationToken token)
        {
            _actual.RecordErrorAsync(component, error, detail, token).GetAwaiter().GetResult();
            return record(component, error);
        }
        public Task<IReadOnlyDictionary<string, string>> GetDiagnosticsAsync(CancellationToken token) => _actual.GetDiagnosticsAsync(token);
        public Task<IReadOnlyList<(string Component, string Error, DateTimeOffset Timestamp)>> GetRecentErrorsAsync(int limit, CancellationToken token) =>
            _actual.GetRecentErrorsAsync(limit, token);
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "9to1-actual-lifecycle-startup-" + Guid.NewGuid().ToString("N"));
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }
}
