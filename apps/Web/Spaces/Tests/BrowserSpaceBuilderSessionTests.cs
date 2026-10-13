using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace NineToOne.Web.Spaces.Tests;

public sealed class BrowserSpaceBuilderSessionTests
{
    [Fact]
    public async Task Switching_views_and_navigation_retains_one_draft_then_saves_same_space()
    {
        using var fixture = new Fixture();
        var registry = new SpaceRegistry(fixture.Store);
        var space = await registry.CreateAsync("A custom Space");
        space = await registry.SetLayoutAsync(space.Id, new([new(Guid.NewGuid(), "Logic", "Preserve me")], []));
        await using var session = fixture.Session(registry, space.Id);
        await session.PrepareAsync(default); session.Admit();
        Assert.True(session.TrySetValue("builder.name", "My revised Space"));
        Assert.True(session.TrySetValue("builder.instructions", "Keep my exact\ninstructions"));
        await session.DispatchAsync("builder.advanced", null);
        Assert.True(session.TrySetValue("builder.webIndex", (int)SpaceWebAccessPolicy.Disabled));
        await session.DispatchAsync("builder.expert", null);
        await session.DispatchAsync("builder.message", null);
        Assert.True(Read<bool>(session, "builder.graphVisible"));
        session.Deactivate();
        Assert.False(session.TrySetValue("builder.name", "Inactive write"));
        await session.PrepareAsync(default); session.Admit();
        Assert.Equal("My revised Space", Read<string>(session, "builder.name"));
        Assert.Equal("User Message Logic", Read<string>(session, "builder.page"));
        await session.DispatchAsync("builder.basic", null);
        await session.DispatchAsync("builder.save", null);

        var saved = (await new SpaceRegistry(new VersionedAtomicSettingsStore(fixture)).GetAsync(space.Id))!;
        Assert.Equal(space.Id, saved.Id);
        Assert.Equal(space.Revision + 1, saved.Revision);
        Assert.Equal("My revised Space", saved.Name);
        Assert.Equal("Keep my exact\ninstructions", saved.Instructions);
        Assert.Equal(SpaceWebAccessPolicy.Disabled, saved.ContextPolicy!.WebAccess);
        Assert.Equal(JsonSerializer.Serialize(space.LayoutDocument), JsonSerializer.Serialize(saved.LayoutDocument));
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task Revision_conflict_and_refresh_preserve_local_draft_until_explicit_discard()
    {
        using var fixture = new Fixture();
        var registry = new SpaceRegistry(fixture.Store);
        var space = await registry.CreateAsync("Original");
        await using var session = fixture.Session(registry, space.Id);
        await session.PrepareAsync(default); session.Admit();
        session.TrySetValue("builder.name", "My local draft");
        await new SpaceRegistry(new VersionedAtomicSettingsStore(fixture)).RenameAsync(space.Id, "Saved in another view");
        await session.DispatchAsync("builder.save", null);
        Assert.Equal("My local draft", Read<string>(session, "builder.name"));
        Assert.True(session.HasUnsavedChanges);
        await session.DispatchAsync("builder.refresh", null);
        Assert.Equal("My local draft", Read<string>(session, "builder.name"));
        Assert.Equal("Saved in another view", (await registry.GetAsync(space.Id))!.Name);
        await session.DispatchAsync("builder.discard", null);
        Assert.Equal("Saved in another view", Read<string>(session, "builder.name"));
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task Lost_commit_acknowledgement_is_not_replayed_and_original_error_is_retained()
    {
        using var fixture = new Fixture();
        var registry = new SpaceRegistry(fixture.Store);
        var space = await registry.CreateAsync("Before save");
        var session = fixture.Session(registry, space.Id);
        await session.PrepareAsync(default); session.Admit();
        session.TrySetValue("builder.name", "Actually saved");
        fixture.Store.LoseNextAcknowledgement = true;
        var fault = await Assert.ThrowsAsync<IOException>(() => session.DispatchAsync("builder.save", null).AsTask());
        Assert.Equal("Lost exact save acknowledgement", fault.Message);
        Assert.False(session.IsActionAvailable("builder.save"));
        var writes = fixture.Store.Writes;
        await session.DispatchAsync("builder.save", null);
        Assert.Equal(writes, fixture.Store.Writes);
        await session.DispatchAsync("builder.refresh", null);
        Assert.True(session.HasUnsavedChanges);
        Assert.False(session.IsActionAvailable("builder.save"));
        await session.DispatchAsync("builder.discard", null);
        Assert.Equal("Actually saved", Read<string>(session, "builder.name"));
        Assert.False(session.HasUnsavedChanges);
        Assert.Same(fault, await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask()));
    }

    [Fact]
    public async Task Private_close_cancels_pending_initial_refresh_and_clears_private_fields()
    {
        using var fixture = new Fixture();
        var registry = new SpaceRegistry(fixture.Store);
        var space = await registry.CreateAsync("Private Space");
        var session = fixture.Session(registry, space.Id);
        fixture.Store.BlockReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepare = session.PrepareAsync(default);
        await fixture.Store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RevokePrivateContext();
        await session.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare);
        Assert.Equal("", Read<string>(session, "builder.name"));
        Assert.Equal("", Read<string>(session, "builder.identity"));
        Assert.False(session.IsActionAvailable("builder.save"));
    }

    [Fact]
    public async Task Throwing_reentrant_cancel_callback_observes_same_close_and_joins_pending_read()
    {
        using var fixture = new Fixture();
        var registry = new SpaceRegistry(fixture.Store);
        var space = await registry.CreateAsync("Private Space");
        var session = fixture.Session(registry, space.Id);
        var fault = new IOException("Cancellation callback failed");
        Task? reenteredClose = null;
        fixture.Store.BlockReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.HoldReadAfterCancellation = true;
        fixture.Store.CancellationCallback = () =>
        {
            reenteredClose = session.DisposeAsync().AsTask();
            throw fault;
        };
        var prepare = session.PrepareAsync(default);
        await fixture.Store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = session.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        fixture.Store.BlockReads.TrySetCanceled();
        Assert.Same(fault, await Assert.ThrowsAsync<IOException>(() => close));
        Assert.Same(close, reenteredClose);
        Assert.True(prepare.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare);
        Assert.Equal("", Read<string>(session, "builder.identity"));
        Assert.False(session.IsActionAvailable("builder.save"));
    }

    private static T Read<T>(BrowserSpaceBuilderSession session, string path)
    {
        Assert.True(session.TryGetValue(path, out var value));
        return Assert.IsType<T>(value);
    }

    private sealed class Fixture : IDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "browser-space-builder-" + Guid.NewGuid().ToString("N"));
        public ObservedStore Store { get; }
        public Fixture() { Directory.CreateDirectory(DataDirectory); Store = new(new(this)); }
        public BrowserSpaceBuilderSession Session(SpaceRegistry registry, Guid id) => new(() => registry, () => { },
            action => { action(); return Task.CompletedTask; }, id);
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }

    private sealed class ObservedStore(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public bool LoseNextAcknowledgement { get; set; }
        public int Writes { get; private set; }
        public TaskCompletionSource? BlockReads { get; set; }
        public bool HoldReadAfterCancellation { get; set; }
        public Action? CancellationCallback { get; set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => actual.GetAsync<T>(key, cancellationToken);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class => actual.SetAsync(key, value, cancellationToken);
        public Task RemoveAsync(string key, CancellationToken cancellationToken) => actual.RemoveAsync(key, cancellationToken);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => actual.ImportAsync(manifest, cancellationToken);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            if (BlockReads is { } block)
            {
                using var registration = cancellationToken.Register(() => CancellationCallback?.Invoke());
                ReadStarted.TrySetResult();
                if (HoldReadAfterCancellation) await block.Task;
                else await block.Task.WaitAsync(cancellationToken);
            }
            return await actual.ExportAsync(cancellationToken);
        }
        public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
            string? replacementJson, CancellationToken cancellationToken)
        {
            var outcome = await actual.CompareExchangeAsync(key, expectedJson, replacementJson, cancellationToken);
            if (outcome.Exchanged)
            {
                Writes++;
                if (LoseNextAcknowledgement)
                {
                    LoseNextAcknowledgement = false;
                    throw new IOException("Lost exact save acknowledgement");
                }
            }
            return outcome;
        }
    }
}
