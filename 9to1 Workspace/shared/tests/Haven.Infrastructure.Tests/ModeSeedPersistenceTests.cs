/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ModeSeedPersistenceTests.cs, in the automated test suite,
 *        where executable examples protect behavior against regressions.
 * What: This file owns ModeSeedPersistenceTests, SeedTestPaths. It proves, against a real SQLite
 *        database, that canonical built-in reseeding refreshes metadata while PRESERVING a user's
 *        stored is_enabled state, that explicit re-enable survives reseeding, and that custom
 *        records are untouched by seeding.
 * Why: The seed runs on every successful startup; without the guarantee, a built-in the user
 *        disabled would be silently re-enabled by the seed default.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and public
 *        shapes. No project edits are needed: this project compiles default compile items.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents mode seed persistence tests and keeps its related state and behavior together.
/// </summary>
public sealed class ModeSeedPersistenceTests : IDisposable
{
    private static readonly Guid BuildId = Guid.Parse("a0000000-0000-0000-0000-000000000028");
    private readonly SeedTestPaths _paths = new();

    /// <summary>The canonical built-in seed must refresh metadata (name) on every reseed while
    /// preserving a user-disabled is_enabled state, and the generic upsert must honor an explicit
    /// state update contract.</summary>
    [Fact]
    public async Task Seeding_preserves_user_disabled_builtin_and_refreshes_metadata()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var registry = new ModeRegistry(database);
        var service = new ModeSeedService(registry);

        await service.SeedBuiltInModesAsync(CancellationToken.None);
        var build = (await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!;
        Assert.True(build.IsEnabled);
        Assert.Equal("Build", build.Name);

        // Simulate a stale/older stored label on the SAME built-in stable ID, combined with a user
        // disable. Both changes are written through the real generic registry upsert (state-update
        // contract), so this proves the canonical reseed must refresh metadata, not skip it.
        await registry.UpsertModeAsync(build with { Name = "dev", IsEnabled = false }, CancellationToken.None);
        var renamed = (await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!;
        Assert.Equal("dev", renamed.Name);
        Assert.False(renamed.IsEnabled);

        // Reseed refreshes metadata back to the canonical "Build" name while PRESERVING the user-
        // disabled is_enabled state (the seed never re-enables a built-in the user turned off).
        await service.SeedBuiltInModesAsync(CancellationToken.None);
        var afterReseed = (await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!;
        Assert.Equal("Build", afterReseed.Name);
        Assert.False(afterReseed.IsEnabled);
        Assert.Equal(BuildId, afterReseed.Id);
    }

    /// <summary>Explicit re-enable survives later reseeding, and custom records are untouched by
    /// seeding.</summary>
    [Fact]
    public async Task Explicit_reenable_survives_seeding_and_custom_records_are_preserved()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var registry = new ModeRegistry(database);
        var service = new ModeSeedService(registry);

        await service.SeedBuiltInModesAsync(CancellationToken.None);
        var build = (await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!;

        // Disable, reseed (stays disabled), then explicitly re-enable and reseed again (stays on).
        await registry.UpsertModeAsync(build with { IsEnabled = false }, CancellationToken.None);
        await service.SeedBuiltInModesAsync(CancellationToken.None);
        Assert.False((await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!.IsEnabled);

        await registry.UpsertModeAsync((await registry.GetModeByIdAsync(BuildId, CancellationToken.None))! with { IsEnabled = true }, CancellationToken.None);
        Assert.True((await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!.IsEnabled);
        await service.SeedBuiltInModesAsync(CancellationToken.None);
        Assert.True((await registry.GetModeByIdAsync(BuildId, CancellationToken.None))!.IsEnabled);

        // Custom record: disabled, with a stable identity. Seeding never touches it.
        var customId = Guid.NewGuid();
        var custom = new ModeDefinition(
            customId, "custom-mode", "Custom Mode", "A user-created mode", "code",
            HavenMode.Tasks, "[\"Tasks\"]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        { IsEnabled = false };
        await registry.UpsertModeAsync(custom, CancellationToken.None);
        await service.SeedBuiltInModesAsync(CancellationToken.None);
        var reloadedCustom = (await registry.GetModeByIdAsync(customId, CancellationToken.None))!;
        Assert.Equal(customId, reloadedCustom.Id);
        Assert.False(reloadedCustom.IsEnabled);
        Assert.Equal("Custom Mode", reloadedCustom.Name);
        Assert.Equal("custom-mode", reloadedCustom.Key);
        Assert.Equal(ModeSource.Created, reloadedCustom.Source);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class SeedTestPaths : IAppPaths, IDisposable
    {
        public SeedTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-mode-seed-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
