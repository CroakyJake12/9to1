using System.ComponentModel;
using System.Reflection;
using Haven.Infrastructure.Native.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual local Windows64 create-new/native/SQLite owning controls. No
/// alternate test path is passed to production; each sets the real existing selector.</summary>
[Trait("Platform", "Windows64")]
public sealed class WindowsNativePersonalTaskStoreSetupTests
{
    [Fact]
    public async Task Actual_new_configured_private_root_and_explicit_database_precede_same_AppPaths_and_SQLite_WAL()
    {
        using var fixture = new ConfiguredStore();
        var recoverySelector = Environment.GetEnvironmentVariable("HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY");
        var created = WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore();
        Assert.Equal(fixture.Path, created, ignoreCase: true);
        Assert.Equal(0, new FileInfo(fixture.Database).Length);
        Assert.False(File.Exists(fixture.Key));
        using (var ancestors = WindowsOriginalFileCustody.RetainRoot(created))
        using (var root = ancestors.OpenDirectory(created))
        using (var database = ancestors.OpenRead(fixture.Database))
        {
            var parent = WindowsOriginalFileCustody.ReadSecurity(root);
            var file = WindowsOriginalFileCustody.ReadSecurity(database);
            Assert.Equal(WindowsOriginalFileCustody.CurrentSid(), parent.OwnerSid);
            Assert.Equal(parent.OwnerSid, file.OwnerSid);
            Assert.True(parent.DaclProtected); Assert.True(file.DaclProtected);
            Assert.Single(parent.Dacl); Assert.Equal((byte)3, parent.Dacl[0].Flags);
            Assert.All(file.Dacl, ace => Assert.Equal(0, ace.Flags & ~3));
        }
        var paths = new AppPaths();
        Assert.Equal(created, WindowsOriginalFileCustody.NormalizeLocalPath(paths.DataDirectory));
        Assert.Equal(fixture.Database, WindowsOriginalFileCustody.NormalizeLocalPath(paths.DatabasePath));
        using (var source = NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath))
        {
            var database = new SqliteDatabase(paths);
            await using var connection = await database.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE setup_owning_control(value INTEGER NOT NULL); INSERT INTO setup_owning_control VALUES(7);";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            Assert.True(File.Exists(fixture.Database + "-wal")); Assert.True(File.Exists(fixture.Database + "-shm"));
            using (var original = WindowsOriginalFileCustody.RetainRoot(created))
            {
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    using var child = original.OpenRead(fixture.Database + suffix);
                    var actual = WindowsOriginalFileCustody.ReadSecurity(child);
                    Assert.False(actual.DaclProtected); Assert.Single(actual.Dacl);
                    Assert.Equal((byte)0x10, actual.Dacl[0].Flags);
                }
            }
            source.Validate();
            command.CommandText = "SELECT value FROM setup_owning_control";
            Assert.Equal(7L, await command.ExecuteScalarAsync(CancellationToken.None));
            Assert.False(File.Exists(fixture.Key));
        }
        SqliteConnection.ClearAllPools();
        Assert.Equal(recoverySelector, Environment.GetEnvironmentVariable("HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY"));
    }

    [Fact]
    public void Actual_existing_root_collision_preserves_all_existing_bytes_security_and_names()
    {
        using var fixture = new ConfiguredStore();
        WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore();
        File.WriteAllBytes(fixture.Database, [3, 5, 7]);
        File.WriteAllBytes(System.IO.Path.Combine(fixture.Path, "existing-data"), [11, 13]);
        using var ancestors = WindowsOriginalFileCustody.RetainRoot(fixture.Path);
        using var root = ancestors.OpenDirectory(fixture.Path);
        using var database = ancestors.OpenRead(fixture.Database);
        var rootSecurity = WindowsOriginalFileCustody.ReadSecurity(root).Fingerprint;
        var databaseSecurity = WindowsOriginalFileCustody.ReadSecurity(database).Fingerprint;
        var names = Directory.GetFiles(fixture.Path).Order(StringComparer.Ordinal).ToArray();
        var refused = Assert.ThrowsAny<Exception>(() => WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore());
        Assert.Contains(Causes(refused), WindowsOriginalFileCustody.IsOriginalNameCollision);
        Assert.Equal(names, Directory.GetFiles(fixture.Path).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new byte[] { 3, 5, 7 }, File.ReadAllBytes(fixture.Database));
        Assert.Equal(new byte[] { 11, 13 }, File.ReadAllBytes(System.IO.Path.Combine(fixture.Path, "existing-data")));
        Assert.Equal(rootSecurity, WindowsOriginalFileCustody.ReadSecurity(root).Fingerprint);
        Assert.Equal(databaseSecurity, WindowsOriginalFileCustody.ReadSecurity(database).Fingerprint);
        Assert.False(File.Exists(fixture.Key));
    }

    [Fact]
    public void Actual_readonly_same_parent_Flags0_refusal_precedes_new_directory_database_and_key_effects()
    {
        using var fixture = new ConfiguredStore();
        var parentPath = System.IO.Path.GetDirectoryName(fixture.Path)!;
        using var ancestors = WindowsOriginalFileCustody.RetainRoot(parentPath);
        using var readOnly = ancestors.OpenDirectory(parentPath, mutable: false);
        var native = Assert.ThrowsAny<Exception>(() => WindowsOriginalFileCustody.Flush(readOnly));
        SafeFileHandle? original = null;
        Seam("_beforeOriginalParentProbe", (Action<object>)(operation =>
        {
            var field = Field(operation, "_parent"); original = (SafeFileHandle)field.GetValue(operation)!;
            Assert.True(WindowsOriginalFileCustody.ReadIdentity(original).SameFile(WindowsOriginalFileCustody.ReadIdentity(readOnly)));
            field.SetValue(operation, readOnly);
        }));
        try
        {
            var refused = Assert.ThrowsAny<Exception>(() => WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore());
            Assert.Equal(native.GetType(), refused.GetType());
            if (native is Win32Exception first && refused is Win32Exception actual) Assert.Equal(first.NativeErrorCode, actual.NativeErrorCode);
            Assert.False(Directory.Exists(fixture.Path)); Assert.False(File.Exists(fixture.Database)); Assert.False(File.Exists(fixture.Key));
        }
        finally { Seam("_beforeOriginalParentProbe", null); original?.Dispose(); }
    }

    [Fact]
    public void Actual_same_new_root_readonly_sync_failure_preserves_partial_explicit_database_and_native_siblings()
    {
        using var fixture = new ConfiguredStore();
        WindowsOriginalRoot? retained = null; SafeFileHandle? original = null; Exception? native = null;
        Seam("_afterOriginalDatabaseCreate", (Action<object>)(operation =>
        {
            retained = WindowsOriginalFileCustody.RetainRoot(fixture.Path);
            var readOnly = retained.OpenDirectory(fixture.Path, mutable: false);
            native = Assert.ThrowsAny<Exception>(() => WindowsOriginalFileCustody.Flush(readOnly));
            var field = Field(operation, "_directory"); original = (SafeFileHandle)field.GetValue(operation)!;
            Assert.True(WindowsOriginalFileCustody.ReadIdentity(original).SameFile(WindowsOriginalFileCustody.ReadIdentity(readOnly)));
            field.SetValue(operation, readOnly);
        }));
        try
        {
            var refused = Assert.Throws<IOException>(() => WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore());
            Assert.NotNull(native);
            Assert.Contains("partial", refused.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Causes(refused).Count(cause => cause.GetType() == native!.GetType()) >= 2,
                "Original root-sync failure and independent after-database-close root-sync failure must both remain inspectable.");
            Assert.True(Directory.Exists(fixture.Path)); Assert.Equal(0, new FileInfo(fixture.Database).Length); Assert.False(File.Exists(fixture.Key));
        }
        finally { Seam("_afterOriginalDatabaseCreate", null); original?.Dispose(); retained?.Dispose(); }
        using var source = NativePersonalTaskRecoveryStore.Acquire(fixture.Path, fixture.Database);
        source.Validate(); // Current custody can be admitted; the failed setup is still not success.
    }

    [Fact]
    public void Post_create_fault_and_independent_close_fault_siblings_remain_inspectable_without_rollback()
    {
        using var fixture = new ConfiguredStore();
        var first = new IOException("Original finite post-create fault.");
        var second = new ArgumentException("Original post-create sibling.");
        var databaseClose = new IOException("Independent database-close boundary fault.");
        var rootClose = new IOException("Independent root-close boundary fault.");
        Seam("_afterOriginalDatabaseCreate", (Action<object>)(_ => throw new AggregateException(first, second)));
        Seam("_beforeOriginalDatabaseClose", (Action)(() => throw databaseClose));
        Seam("_beforeOriginalDirectoryClose", (Action)(() => throw rootClose));
        try
        {
            var refused = Assert.Throws<IOException>(() => WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore());
            foreach (var fault in new Exception[] { first, second, databaseClose, rootClose })
                Assert.Contains(Causes(refused), actual => ReferenceEquals(actual, fault));
            Assert.True(Directory.Exists(fixture.Path)); Assert.Equal(0, new FileInfo(fixture.Database).Length); Assert.False(File.Exists(fixture.Key));
        }
        finally
        {
            Seam("_afterOriginalDatabaseCreate", null); Seam("_beforeOriginalDatabaseClose", null); Seam("_beforeOriginalDirectoryClose", null);
        }
        // An actual fresh exclusive open verifies owning closes still occurred after
        // the negative callbacks. This is not a native close-status qualification.
        using var ancestors = WindowsOriginalFileCustody.RetainRoot(fixture.Path);
        using var parent = ancestors.OpenDirectory(fixture.Path);
        using var database = WindowsOriginalFileCustody.OpenRelative(parent, "haven.db", WindowsOriginalFileCustody.ExclusiveStage,
            0, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Actual_configuration_drift_refuses_before_foreign_path_effects_and_retains_honest_partial_state(bool afterCreate)
    {
        using var fixture = new ConfiguredStore();
        var foreign = fixture.Path + "-foreign";
        var name = afterCreate ? "_afterOriginalDatabaseCreate" : "_beforeOriginalParentProbe";
        Seam(name, (Action<object>)(_ => Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", foreign)));
        try
        {
            Assert.ThrowsAny<Exception>(() => WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore());
            Assert.False(Directory.Exists(foreign)); Assert.Equal(afterCreate, Directory.Exists(fixture.Path));
            Assert.Equal(afterCreate, File.Exists(fixture.Database)); Assert.False(File.Exists(fixture.Key));
        }
        finally { Seam(name, null); Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", fixture.Path); }
    }

    [Fact]
    public void Actual_database_name_collision_never_adopts_or_truncates_foreign_child()
    {
        using var fixture = new ConfiguredStore();
        Seam("_beforeOriginalDatabaseCreate", (Action<object>)(_ => File.WriteAllBytes(fixture.Database, [17, 19, 23])));
        try
        {
            var refused = Assert.Throws<IOException>(() => WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore());
            Assert.Contains(Causes(refused), WindowsOriginalFileCustody.IsOriginalNameCollision);
            Assert.Equal(new byte[] { 17, 19, 23 }, File.ReadAllBytes(fixture.Database)); Assert.False(File.Exists(fixture.Key));
        }
        finally { Seam("_beforeOriginalDatabaseCreate", null); }
    }

    [Fact]
    public void Actual_default_mapping_observation_preserves_AppPaths_rule_without_creating_default_root()
    {
        DemandWindows(); var saved = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var defaultPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Haven");
        var existed = Directory.Exists(defaultPath);
        try
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
            var method = typeof(WindowsNativePersonalTaskStoreSetup).GetMethod("ConfiguredPath", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Equal(WindowsOriginalFileCustody.NormalizeLocalPath(defaultPath), method.Invoke(null, null));
            Assert.Equal(existed, Directory.Exists(defaultPath));
        }
        finally { Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", saved); }
    }

    private static void Seam(string name, object? callback) => typeof(WindowsNativePersonalTaskStoreSetup)
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, callback);
    private static FieldInfo Field(object operation, string name) => operation.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static IEnumerable<Exception> Causes(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var cause in Causes(child)) yield return cause;
        else if (actual.InnerException is { } child) foreach (var cause in Causes(child)) yield return cause;
    }
    private static void DemandWindows()
    {
        Assert.True(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "Actual local Windows64 is required; no successful Linux return qualifies.");
        WindowsOriginalFileCustody.RequireCapabilities();
    }
    private sealed class ConfiguredStore : IDisposable
    {
        private readonly string? _previous;
        internal string Path { get; }
        internal string Database => System.IO.Path.Combine(Path, "haven.db");
        internal string Key => System.IO.Path.Combine(Path, ".task-recovery-auth.v1");
        internal ConfiguredStore()
        {
            DemandWindows(); _previous = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
            Path = WindowsOriginalFileCustody.NormalizeLocalPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-new-private-store51-" + Guid.NewGuid().ToString("N")));
            Assert.False(Directory.Exists(Path)); Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", Path);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _previous);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); // Test-owned namespace only.
        }
    }
}
