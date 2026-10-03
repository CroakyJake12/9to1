using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ResourceStoreIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-store-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Actual_new_database_has_stable_uuid_but_reopening_is_not_new_creation()
    {
        Directory.CreateDirectory(_root);
        var paths = new Paths(_root);
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(default);
        var first = await database.GetStoreIdentityAsync(default);
        var again = await database.GetStoreIdentityAsync(default);
        var reopened = await new SqliteDatabase(paths).GetStoreIdentityAsync(default);
        Assert.True(first.NewlyCreated);
        Assert.Equal(first.StoreId, again.StoreId);
        Assert.Equal(first.StoreId, reopened.StoreId);
        Assert.False(reopened.NewlyCreated);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(paths.DatabasePath));
    }

    [Fact]
    public async Task Existing_database_is_never_claimed_new_and_unknown_identity_schema_is_preserved()
    {
        Directory.CreateDirectory(_root);
        var paths = new Paths(_root);
        await File.WriteAllBytesAsync(paths.DatabasePath, []);
        var database = new SqliteDatabase(paths);
        Assert.False((await database.GetStoreIdentityAsync(default)).NewlyCreated);
        await using (var connection = await database.OpenAsync(default))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE resource_store_identity SET schema_version=999;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteDatabase(paths).GetStoreIdentityAsync(default).AsTask());
        Assert.True(File.Exists(paths.DatabasePath));
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
