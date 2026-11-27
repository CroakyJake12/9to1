using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

/// <summary>Exercises the actual SQLite owner used by Home and the existing pinned-app consumers.</summary>
public sealed class PinRepositoryOrderingTests
{
    [Fact]
    public async Task Reorder_preserves_pin_identity_time_membership_and_the_registered_apps()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        var modesBefore = await fixture.Modes.GetModesAsync(CancellationToken.None);
        var order = before.Select(pin => pin.ModeId).Reverse().ToArray();

        Assert.True(await fixture.Repository.TryReorderAsync(before, order, CancellationToken.None));

        var after = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        Assert.Equal(order, after.Select(pin => pin.ModeId));
        Assert.Equal(Enumerable.Range(0, after.Count), after.Select(pin => pin.SortOrder));
        foreach (var pin in after)
        {
            var original = Assert.Single(before, item => item.ModeId == pin.ModeId);
            Assert.Equal(original.Id, pin.Id);
            Assert.Equal(original.PinnedAt, pin.PinnedAt);
        }
        Assert.Equal(modesBefore, await fixture.Modes.GetModesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stale_reorder_cannot_lose_a_pin_added_by_another_page()
    {
        using var fixture = await Fixture.CreateAsync();
        var stale = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        var extra = BuiltInModeSeed.Modes.Single(mode => mode.Key == "automations");
        await fixture.Modes.UpsertModeAsync(extra, CancellationToken.None);
        Assert.True(await fixture.Repository.TrySetPinnedAsync(stale, extra.Id, true, CancellationToken.None));
        var published = await fixture.Repository.GetPinsAsync(CancellationToken.None);

        Assert.False(await fixture.Repository.TryReorderAsync(stale,
            stale.Select(pin => pin.ModeId).Reverse().ToArray(), CancellationToken.None));

        Assert.Equal(published, await fixture.Repository.GetPinsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stale_membership_change_cannot_overwrite_a_new_order_or_recreate_a_removed_pin()
    {
        using var fixture = await Fixture.CreateAsync();
        var stale = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        Assert.True(await fixture.Repository.TryReorderAsync(stale,
            stale.Select(pin => pin.ModeId).Reverse().ToArray(), CancellationToken.None));
        var reordered = await fixture.Repository.GetPinsAsync(CancellationToken.None);

        Assert.False(await fixture.Repository.TrySetPinnedAsync(stale, stale[0].ModeId, false, CancellationToken.None));
        Assert.Equal(reordered, await fixture.Repository.GetPinsAsync(CancellationToken.None));
        Assert.True(await fixture.Repository.TrySetPinnedAsync(reordered, stale[0].ModeId, false, CancellationToken.None));
        var removed = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        Assert.False(await fixture.Repository.TrySetPinnedAsync(reordered, stale[0].ModeId, true, CancellationToken.None));
        Assert.Equal(removed, await fixture.Repository.GetPinsAsync(CancellationToken.None));
        Assert.NotNull(await fixture.Modes.GetModeByIdAsync(stale[0].ModeId, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_permutation_and_unknown_app_leave_the_existing_store_intact()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.TryReorderAsync(before,
            [before[0].ModeId, before[0].ModeId, before[2].ModeId], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.TryReorderAsync(before,
            [before[0].ModeId, before[1].ModeId], CancellationToken.None));
        var foreignKey = await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.TrySetPinnedAsync(
            before, Guid.NewGuid(), true, CancellationToken.None));
        Assert.Equal(19, foreignKey.SqliteErrorCode);
        Assert.Equal(before, await fixture.Repository.GetPinsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Admission_lost_at_commit_rolls_back_both_order_and_membership_changes()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        var refused = new UnauthorizedAccessException("The original profile was revoked.");
        Task Refuse(CancellationToken _) => Task.FromException(refused);
        Assert.Same(refused, await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Repository.TryReorderAsync(before, before.Select(pin => pin.ModeId).Reverse().ToArray(),
                CancellationToken.None, Refuse)));
        Assert.Equal(before, await fixture.Repository.GetPinsAsync(CancellationToken.None));
        Assert.Same(refused, await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Repository.TrySetPinnedAsync(before, before[0].ModeId, false, CancellationToken.None, Refuse)));
        Assert.Equal(before, await fixture.Repository.GetPinsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Already_pinned_is_a_noop_and_cancellation_does_not_publish()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Repository.GetPinsAsync(CancellationToken.None);
        Assert.True(await fixture.Repository.TrySetPinnedAsync(before, before[0].ModeId, true, CancellationToken.None));
        Assert.Equal(before, await fixture.Repository.GetPinsAsync(CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Repository.TryReorderAsync(
            before, before.Select(pin => pin.ModeId).Reverse().ToArray(), cancelled.Token));
        Assert.Equal(before, await fixture.Repository.GetPinsAsync(CancellationToken.None));
    }

    private sealed class Fixture : ISqliteConnectionFactory, IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "haven-pin-order-" + Guid.NewGuid().ToString("N") + ".db");
        internal PinRepository Repository { get; }
        internal ModeRegistry Modes { get; }
        private Fixture() { Repository = new(this); Modes = new(this); }

        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                await using (var connection = await fixture.OpenAsync(CancellationToken.None))
                {
                    await using var ledger = connection.CreateCommand();
                    ledger.CommandText = "CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);";
                    await ledger.ExecuteNonQueryAsync();
                    foreach (var migration in Migrations.All)
                    {
                        await using var command = connection.CreateCommand();
                        command.CommandText = migration.Sql;
                        await command.ExecuteNonQueryAsync();
                    }
                }
                var at = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
                var keys = new[] { "spaces", "boards", "write" };
                for (var i = 0; i < keys.Length; i++)
                {
                    var mode = BuiltInModeSeed.Modes.Single(item => item.Key == keys[i]);
                    await fixture.Modes.UpsertModeAsync(mode, CancellationToken.None);
                    await fixture.Repository.UpsertPinAsync(new ModePin(Guid.NewGuid(), mode.Id,
                        3 + i * 4, at.AddMinutes(i)), CancellationToken.None);
                }
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqliteProviderBootstrap.EnsureInitialized();
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = _path, ForeignKeys = true, Pooling = false }.ToString());
            try { await connection.OpenAsync(cancellationToken); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }
        public void Dispose() => File.Delete(_path);
    }
}
